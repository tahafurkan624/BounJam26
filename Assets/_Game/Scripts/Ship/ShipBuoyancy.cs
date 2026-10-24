using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Floats the ship on the liquid blob. Each float point on the hull that is below the jelly surface is pushed up
    /// in proportion to its depth (with damping), and dragged toward the jelly's own velocity, which is what carries
    /// the ship along. A gentle pull toward the blob's top centre keeps it from wandering off, while a little downhill
    /// push on sloped surfaces lets it slide off if the blob is jerked around too hard. Once a piercer pipe has cut the
    /// blob in two, the ship floats on whichever half it's on.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ShipBuoyancy : MonoBehaviour
    {
        [Tooltip("The liquid the ship starts on. Found automatically when empty.")]
        [SerializeField] BlobSkin blob;
        [Tooltip("Points on the hull, in local space, that test for and receive buoyancy.")]
        [SerializeField] Vector3[] floatPoints =
        {
            new Vector3(0.075f, 0.01f, 0.25f), new Vector3(-0.075f, 0.01f, 0.25f),
            new Vector3(0.075f, 0.01f, -0.25f), new Vector3(-0.075f, 0.01f, -0.25f),
            new Vector3(0f, 0.06f, 0.38f), new Vector3(0f, 0.06f, -0.38f),
        };
        [Tooltip("Local centre of mass; low keeps the ship upright.")]
        [SerializeField] Vector3 centerOfMass = new Vector3(0f, 0.04f, 0f);
        [Tooltip("Top speed when the physics pushes the ship out of an overlap, m/s. Low = eased out, not popped off. " +
                 "(Not saved on the Rigidbody itself, so it's applied from here.)")]
        [SerializeField] float maxDepenetrationSpeed = 1.5f;

        [Header("Floating")]
        [Tooltip("Upward push per metre below the surface, m/s². Resting depth is about 9.81 / this.")]
        [SerializeField] float buoyancy = 350f;
        [Tooltip("Depth beyond which the push stops growing, m.")]
        [SerializeField] float maxDepth = 0.25f;
        [Tooltip("Damps bobbing up and down relative to the surface, 1/s.")]
        [SerializeField] float verticalDamping = 15f;

        [Header("Carrying")]
        [Tooltip("How strongly the jelly drags the ship along with it, 1/s. Higher = stickier, harder to lose.")]
        [SerializeField] float viscosity = 40f;
        [Tooltip("Pull toward the top centre of the blob, (m/s²) per metre off centre.")]
        [SerializeField] float centering = 50f;
        [Tooltip("Share of gravity pushing the ship downhill where the jelly surface slopes.")]
        [Range(0f, 1f)]
        [SerializeField] float slopeSlide = 0.3f;
        [Tooltip("Rotation damping while floating; thick liquid resists spinning and rocking.")]
        [SerializeField] float floatingAngularDamping = 4f;
        [Tooltip("Turns the ship back upright while floating, like a keel, rad/s² per radian of tilt.")]
        [SerializeField] float uprighting = 40f;
        [Tooltip("While the blob is in the air, how much faster than it the ship may rise, m/s. The jelly stretches up as " +
                 "it leaves the ground, and without a limit the buoyancy turns that into a throw.")]
        [SerializeField] float airborneMaxRise = 0.5f;

        [Header("Adhesion")]
        [Tooltip("A float point this far above the surface still clings to it, so the ship stays on when the blob " +
                 "flattens or dips faster than it can fall, m.")]
        [SerializeField] float adhesionDistance = 0.15f;
        [Tooltip("Pull back down onto the surface at the edge of the clinging range, m/s² on top of gravity. " +
                 "Grows from zero at the surface, so resting points just above a curved top aren't dragged under.")]
        [SerializeField] float adhesionPull = 40f;
        [Tooltip("Share of the carrying drag that still acts while clinging above the surface.")]
        [Range(0f, 1f)]
        [SerializeField] float adhesionDrag = 0.6f;

        Rigidbody body;
        Collider[] colliders;
        BlobForm blobForm;
        BlobMotor blobMotor;
        float dryAngularDamping;

        /// <summary>True while at least one float point is in or clinging to the jelly.</summary>
        public bool IsFloating { get; private set; }
        /// <summary>Seconds since the ship last touched the jelly.</summary>
        public float TimeSinceFloating { get; private set; }
        public Rigidbody Body => body;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.centerOfMass = centerOfMass;
            body.maxDepenetrationVelocity = maxDepenetrationSpeed;
            dryAngularDamping = body.angularDamping;
            colliders = GetComponentsInChildren<Collider>();
            if (blob == null) blob = FindAnyObjectByType<BlobSkin>();
            if (blob != null)
            {
                blobForm = blob.GetComponent<BlobForm>();
                blobMotor = blob.GetComponent<BlobMotor>();
            }
        }

        void OnEnable() => Subscribe(true);

        void OnDisable() => Subscribe(false);

        void Subscribe(bool on)
        {
            // A half that flowed into the other is gone; there's nothing to unsubscribe from.
            if (blobForm != null)
            {
                if (on) blobForm.FormChanged += OnBlobFormChanged;
                else blobForm.FormChanged -= OnBlobFormChanged;
            }
            if (blobMotor != null)
            {
                if (on) blobMotor.Jumped += OnBlobJumped;
                else blobMotor.Jumped -= OnBlobJumped;
            }
        }

        /// <summary>
        /// With the blob in pieces, float on whichever one is under the hull: while the ship isn't touching its own
        /// piece (or that piece flowed into the other half), it takes the first piece whose jelly is below it.
        /// </summary>
        void FollowPieceUnder()
        {
            if (IsFloating && blob != null) return;
            IReadOnlyList<BlobSplit> pieces = BlobSplit.All;
            for (int i = 0; i < pieces.Count; i++)
            {
                BlobSkin skin = pieces[i].Skin;
                if (skin == blob || !skin.TrySampleSurface(body.worldCenterOfMass, out _)) continue;
                Subscribe(false);
                blob = skin;
                blobForm = skin.GetComponent<BlobForm>();
                blobMotor = skin.GetComponent<BlobMotor>();
                Subscribe(true);
                return;
            }
        }

        /// <summary>
        /// The jelly under the hull has just jumped. Take off with it: otherwise the ship sinks into the suddenly
        /// rising surface and the buoyancy fires it high off the top.
        /// </summary>
        void OnBlobJumped(float _)
        {
            if (!IsFloating) return;
            Vector3 velocity = body.linearVelocity;
            velocity.y = Mathf.Max(velocity.y, blobMotor.Velocity.y);
            body.linearVelocity = velocity;
        }

        void LimitAirborneRise()
        {
            Vector3 velocity = body.linearVelocity;
            float limit = blobMotor.Velocity.y + airborneMaxRise;
            if (velocity.y <= limit) return;
            velocity.y = limit;
            body.linearVelocity = velocity;
        }

        /// <summary>
        /// The jelly the hull was sitting in has just become a collider. Lift the ship straight out onto it and match
        /// its speed, instead of letting the physics shove it out mid-slide and flip it.
        /// </summary>
        void OnBlobFormChanged(bool solid)
        {
            if (!solid || !IsFloating) return;

            Vector3 lift = Vector3.zero;
            foreach (Collider crystal in blobForm.SolidColliders)
            foreach (Collider own in colliders)
            {
                if (Physics.ComputePenetration(own, own.transform.position, own.transform.rotation,
                                               crystal, crystal.transform.position, crystal.transform.rotation,
                                               out Vector3 direction, out float distance)
                    && distance > lift.magnitude)
                    lift = direction * distance;
            }

            body.position += lift;
            transform.position += lift;
            body.linearVelocity = blob.GetComponent<Rigidbody>().linearVelocity;
            body.angularVelocity = Vector3.zero;
        }

        void FixedUpdate()
        {
            FollowPieceUnder();
            if (IsFloating && blobMotor != null && !blobMotor.IsGrounded) LimitAirborneRise();

            int touching = 0;
            Vector3 horizontal = Vector3.zero;
            if (blob != null)
            {
                float share = 1f / floatPoints.Length;
                foreach (Vector3 local in floatPoints)
                    if (ApplyFloatPoint(transform.TransformPoint(local), share, ref horizontal)) touching++;
            }

            IsFloating = touching > 0;
            TimeSinceFloating = IsFloating ? 0f : TimeSinceFloating + Time.fixedDeltaTime;
            body.angularDamping = IsFloating ? floatingAngularDamping : dryAngularDamping;
            if (!IsFloating) return;

            // Sideways forces act at the centre of mass: applied at the hull bottom they would roll the ship over
            // every time the blob changes direction.
            Vector3 offCentre = blob.ShapeCenter - body.worldCenterOfMass;
            offCentre.y = 0f;
            horizontal += offCentre * (centering * touching / floatPoints.Length);
            body.AddForce(horizontal, ForceMode.Acceleration);

            Vector3 tiltAxis = Vector3.Cross(transform.up, Vector3.up);
            float tilt = Mathf.Asin(Mathf.Clamp(tiltAxis.magnitude, 0f, 1f));
            if (tilt > 1e-3f) body.AddTorque(tiltAxis.normalized * (tilt * uprighting), ForceMode.Acceleration);
        }

        /// <summary>
        /// Applies vertical buoyancy (or clinging) at one float point and adds its share of the horizontal
        /// carrying forces to <paramref name="horizontal"/>. Returns whether the point is touching the jelly.
        /// </summary>
        bool ApplyFloatPoint(Vector3 point, float share, ref Vector3 horizontal)
        {
            if (!blob.TrySampleSurface(point, out BlobSkin.SurfaceSample surface)) return false;
            float depth = surface.Height - point.y;
            if (depth <= -adhesionDistance) return false;

            Vector3 relative = body.GetPointVelocity(point) - surface.Velocity;
            Vector3 relativeFlat = new Vector3(relative.x, 0f, relative.z);
            float vertical;
            float drag;
            if (depth > 0f)
            {
                vertical = buoyancy * Mathf.Min(depth, maxDepth) - verticalDamping * relative.y;
                drag = viscosity;
            }
            else
            {
                // Just above the surface: cling to it rather than letting the blob slide away underneath.
                vertical = -adhesionPull * (-depth / adhesionDistance);
                drag = viscosity * adhesionDrag;
            }

            body.AddForceAtPosition(Vector3.up * (vertical * body.mass * share), point, ForceMode.Force);
            Vector3 downhill = new Vector3(surface.Normal.x, 0f, surface.Normal.z) * (-Physics.gravity.y * slopeSlide);
            horizontal += (downhill - relativeFlat * drag) * share;
            return true;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.blue;
            foreach (Vector3 local in floatPoints) Gizmos.DrawSphere(transform.TransformPoint(local), 0.015f);
        }
    }
}
