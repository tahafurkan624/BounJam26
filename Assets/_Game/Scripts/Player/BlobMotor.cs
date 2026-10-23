using System;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Moves the blob's core rigidbody. Input is camera-relative on the ground plane, and the body is
    /// steered toward a target velocity (accelerate while there is input, brake when there isn't),
    /// so it reacts quickly but still carries momentum. The liquid can jump, the solid swims when it floats in water
    /// (<see cref="BlobWater"/> decides when). The core never rotates; the skin does the visuals.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public class BlobMotor : MonoBehaviour
    {
        [Serializable]
        public struct MoveSettings
        {
            [Tooltip("Top speed on flat ground, m/s.")]
            public float maxSpeed;
            [Tooltip("How quickly it reaches top speed while there is input, m/s².")]
            public float acceleration;
            [Tooltip("How quickly it stops once input is released, m/s².")]
            public float braking;
        }

        [SerializeField] PlayerInputReader input;
        [Tooltip("Movement is relative to this camera's facing. Defaults to the main camera.")]
        [SerializeField] Transform cameraTransform;
        [SerializeField] MoveSettings liquid = new MoveSettings { maxSpeed = 5f, acceleration = 12f, braking = 25f };
        [Tooltip("Solid form: pushes itself along the input direction (its frozen shape decides whether it rolls or " +
                 "slides) and brakes, spin included, when there's no input.")]
        [SerializeField] MoveSettings solid = new MoveSettings { maxSpeed = 1.8f, acceleration = 15f, braking = 6f };
        [Tooltip("Solid form floating in water: paddles along the input direction, no footing needed, and drifts to a " +
                 "stop when there's no input.")]
        [SerializeField] MoveSettings swim = new MoveSettings { maxSpeed = 2.2f, acceleration = 5f, braking = 1.5f };
        [Tooltip("Fraction of acceleration and braking available while airborne.")]
        [Range(0f, 1f)]
        [SerializeField] float airControl = 0.3f;

        [Header("Jump")]
        [Tooltip("How high a jump from flat ground lifts the blob, m. Liquid form only.")]
        [SerializeField] float jumpHeight = 0.8f;
        [Tooltip("A jump pressed this long before landing still happens when it lands, s.")]
        [SerializeField] float jumpBuffer = 0.15f;
        [Tooltip("A jump is still allowed this long after flowing off an edge, s.")]
        [SerializeField] float coyoteTime = 0.1f;

        [Header("Ground")]
        [SerializeField] LayerMask groundMask = ~0;
        [Tooltip("How far below the core the ground is still detected, m.")]
        [SerializeField] float groundProbeDistance = 0.2f;
        [SerializeField] float maxGroundAngle = 50f;
        [Tooltip("Extra pull into the ground while grounded, keeps the blob hugging ramps and crests, m/s².")]
        [SerializeField] float groundStick = 15f;
        [Tooltip("Right after leaving the ground, ground within this distance below pulls the blob back down " +
                 "instead of letting it launch off bumps and crests, m. Larger drops still fall.")]
        [SerializeField] float snapDistance = 0.6f;
        [Tooltip("Above this speed the blob may leave the ground over crests, m/s.")]
        [SerializeField] float snapMaxSpeed = 9f;

        [Header("Steps")]
        [Tooltip("Edges up to this height above the bottom of the core are flowed up, m.")]
        [SerializeField] float maxStepHeight = 0.25f;
        [Tooltip("Upward speed while flowing up a step, m/s.")]
        [SerializeField] float stepClimbSpeed = 2.5f;

        [Header("Shore")]
        [Tooltip("Swimming into the edge of the water climbs out onto a ledge whose top is at most this far above the " +
                 "crystal's centre, m. Higher walls can't be climbed.")]
        [SerializeField] float shoreClimbHeight = 0.6f;
        [Tooltip("Upward speed while climbing out of the water, m/s.")]
        [SerializeField] float shoreClimbSpeed = 1.5f;

        // Below this gap the core counts as touching the ground rather than skimming over it.
        const float ContactGap = 0.02f;
        // Physics steps after a step climb during which hugging and snapping stay off, so they don't undo the lift.
        const int ClimbGraceSteps = 4;
        // Physics steps after a jump during which the ground is ignored: the probe still reaches it for a moment, and
        // hugging, snapping or sticking to it would pull the blob straight back down.
        const int JumpGraceSteps = 6;
        // A climb out of the water gives up after this many physics steps, in case something holds the crystal back.
        const int MaxClimbSteps = 75;
        // Climbing out lifts the crystal's underside this far above the edge before moving it over, m.
        const float ShoreClearance = 0.05f;

        Rigidbody body;
        SphereCollider core;
        int stepsSinceGrounded;
        int stepsSinceClimb = ClimbGraceSteps;
        int stepsSinceJump = JumpGraceSteps;
        bool climbingOut;
        float shoreTop;
        Vector3 climbDirection;
        int climbSteps;
        // True from a jump until the blob is back on the ground, so coyote time can't give a second jump.
        bool hasJumped;
        float jumpRequestTime = float.NegativeInfinity;
        bool stepContact;
        Vector3 stepNormal;
        float groundGap;
        bool isSolid;
        bool solidGroundContact;
        bool solidCrateContact;
        Vector3 solidGroundNormal = Vector3.up;
        bool shoreContact;
        Vector3 shoreNormal;
        Vector3 shorePoint;
        Collider solidShape;
        int pushableLayer;

        public Rigidbody Body => body;
        public bool IsGrounded { get; private set; }
        /// <summary>True while the solid floats in water and swims instead of pushing itself along the ground.</summary>
        public bool IsSwimming { get; private set; }
        public Vector3 GroundNormal { get; private set; } = Vector3.up;
        public Vector3 Velocity => body.linearVelocity;
        public MoveSettings ActiveSettings => isSolid ? (IsSwimming ? swim : solid) : liquid;
        /// <summary>
        /// While true, something else steers the body (a magnet carrying the blob): the motor doesn't move, brake
        /// or jump, and the blob counts as airborne.
        /// </summary>
        public bool IsDriven { get; set; }
        /// <summary>Raised on the physics step the blob takes off, with the upward speed the jump added, m/s.</summary>
        public event Action<float> Jumped;

        /// <summary>Jumps on the next physics step it can: now if on the ground, or on landing within the jump buffer.</summary>
        public void RequestJump() => jumpRequestTime = Time.time;

        /// <summary>
        /// Switches between liquid movement (core sphere) and solid movement (whatever shape is frozen;
        /// <paramref name="shape"/> is its collider).
        /// </summary>
        public void SetSolid(bool value, Collider shape = null)
        {
            isSolid = value;
            solidShape = shape;
            IsSwimming = false;
            climbingOut = false;
            solidGroundContact = false;
            stepContact = false;
            shoreContact = false;
        }

        /// <summary>Solid form only: true while floating in water.</summary>
        public void SetSwimming(bool value) => IsSwimming = value && isSolid;
        /// <summary>Speed along the ground relative to the current top speed; 0 at rest, about 1 at full speed.</summary>
        public float NormalizedSpeed => Vector3.ProjectOnPlane(body.linearVelocity, GroundNormal).magnitude / Mathf.Max(0.01f, ActiveSettings.maxSpeed);
        /// <summary>World radius of the core collider.</summary>
        public float CoreRadius => core.radius * MaxAbsComponent(transform.lossyScale);

        void Reset()
        {
            input = GetComponent<PlayerInputReader>();
            groundMask = ~LayerMask.GetMask("PlayerLiquid", "PlayerSolid", "Triggers", "Ignore Raycast");
        }

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            core = GetComponent<SphereCollider>();
            if (input == null) input = GetComponent<PlayerInputReader>();
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
            pushableLayer = LayerMask.NameToLayer("Pushable");
        }

        void OnEnable()
        {
            if (input != null) input.JumpPressed += RequestJump;
        }

        void OnDisable()
        {
            if (input != null) input.JumpPressed -= RequestJump;
        }

        void FixedUpdate()
        {
            if (IsDriven)
            {
                // No coyote-time jump once let go in mid-air either: that waits for the next landing.
                ClearGround();
                stepsSinceGrounded++;
                hasJumped = true;
                jumpRequestTime = float.NegativeInfinity;
                stepContact = false;
                solidGroundContact = false;
                solidCrateContact = false;
                return;
            }

            if (isSolid)
            {
                MoveSolid();
                return;
            }

            ProbeGround();
            stepsSinceJump++;
            if (stepsSinceJump < JumpGraceSteps) ClearGround();
            else if (IsGrounded) hasJumped = false;
            stepsSinceGrounded = IsGrounded ? 0 : stepsSinceGrounded + 1;
            stepsSinceClimb++;

            if (Time.time - jumpRequestTime <= jumpBuffer && !hasJumped && stepsSinceGrounded * Time.fixedDeltaTime <= coyoteTime)
                Jump();

            bool climbing = stepsSinceClimb < ClimbGraceSteps;
            bool takingOff = stepsSinceJump < JumpGraceSteps;
            if (!climbing && !takingOff)
            {
                if (!IsGrounded && stepsSinceGrounded <= 1) SnapToGround();
                else if (IsGrounded && groundGap > ContactGap) HugGround();
            }

            Vector3 up = IsGrounded ? GroundNormal : Vector3.up;
            Vector3 wish = WishDirection();
            if (IsGrounded && wish.sqrMagnitude > 0f)
                wish = Vector3.ProjectOnPlane(wish, up).normalized * wish.magnitude;

            MoveSettings settings = ActiveSettings;
            float rate = wish.sqrMagnitude > 0.0001f ? settings.acceleration : settings.braking;
            if (!IsGrounded) rate *= airControl;

            Vector3 planar = Vector3.ProjectOnPlane(body.linearVelocity, up);
            Vector3 steered = Vector3.MoveTowards(planar, wish * settings.maxSpeed, rate * Time.fixedDeltaTime);
            body.AddForce(steered - planar, ForceMode.VelocityChange);

            if (IsGrounded) body.AddForce(-GroundNormal * groundStick, ForceMode.Acceleration);

            // Pressing into a low edge: flow up it instead of relying on a bounce off the corner.
            if (stepContact && wish.sqrMagnitude > 0.01f && Vector3.Dot(wish.normalized, -stepNormal) > 0.3f)
            {
                Vector3 velocity = body.linearVelocity;
                if (velocity.y < stepClimbSpeed) body.linearVelocity = new Vector3(velocity.x, stepClimbSpeed, velocity.z);
                stepsSinceClimb = 0;
            }
            stepContact = false;
        }

        /// <summary>Straight up, keeping the speed along the ground; a blob already rising faster (a ramp, a lift) keeps that.</summary>
        void Jump()
        {
            Vector3 velocity = body.linearVelocity;
            float before = velocity.y;
            velocity.y = Mathf.Max(velocity.y, Mathf.Sqrt(2f * -Physics.gravity.y * jumpHeight));
            body.linearVelocity = velocity;

            jumpRequestTime = float.NegativeInfinity;
            hasJumped = true;
            stepsSinceJump = 0;
            ClearGround();
            Jumped?.Invoke(velocity.y - before);
        }

        void ClearGround()
        {
            IsGrounded = false;
            GroundNormal = Vector3.up;
            groundGap = float.PositiveInfinity;
        }

        /// <summary>
        /// Solid form: a heavy push along the input direction at the centre of mass, only while resting on something
        /// and below top speed; the frozen shape rolls, tips or slides on its own while pushed. Without input it
        /// brakes, so it doesn't roll or slide on (and off edges) by itself. No liquid ground helpers.
        /// </summary>
        void MoveSolid()
        {
            IsGrounded = solidGroundContact;
            GroundNormal = solidGroundContact ? solidGroundNormal : Vector3.up;
            bool pressingCrate = solidCrateContact;
            bool pressingShore = shoreContact;
            solidGroundContact = false;
            solidCrateContact = false;
            shoreContact = false;

            Vector3 wish = WishDirection();
            if (IsSwimming || climbingOut)
            {
                Swim(wish, pressingShore);
                return;
            }
            if (wish.sqrMagnitude < 0.0001f)
            {
                if (IsGrounded) BrakeSolid();
                return;
            }
            // A resting solid falls asleep and stops reporting contacts; wake it so the next step sees the ground.
            if (body.IsSleeping()) body.WakeUp();
            // Leaning on a crate counts as footing too: a round crystal shoving a crate can lift off the floor a little.
            if (!IsGrounded && !pressingCrate) return;

            Vector3 planar = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            float speedAlongWish = Vector3.Dot(planar, wish.normalized);
            if (speedAlongWish < solid.maxSpeed * wish.magnitude)
                body.AddForce(wish * solid.acceleration, ForceMode.Acceleration);
        }

        /// <summary>
        /// Floating: steered like the liquid, along the water's surface and without needing footing, and drifting to a
        /// stop without input. Swimming into a low enough edge starts a climb out onto it.
        /// </summary>
        void Swim(Vector3 wish, bool pressingShore)
        {
            if (climbingOut)
            {
                ClimbOut(wish);
                return;
            }

            float rate = wish.sqrMagnitude > 0.0001f ? swim.acceleration : swim.braking;
            Vector3 planar = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            Vector3 steered = Vector3.MoveTowards(planar, wish * swim.maxSpeed, rate * Time.fixedDeltaTime);
            body.AddForce(steered - planar, ForceMode.VelocityChange);

            if (!pressingShore || wish.sqrMagnitude < 0.01f || Vector3.Dot(wish.normalized, -shoreNormal) < 0.3f) return;
            // The edge's top, looked for just past the contact. From inside a wall taller than the reach the ray
            // starts inside it and finds nothing, so only low enough edges are climbed.
            float reach = body.worldCenterOfMass.y + shoreClimbHeight;
            Vector3 probe = shorePoint - shoreNormal * 0.1f;
            probe.y = reach;
            if (!Physics.Raycast(probe, Vector3.down, out RaycastHit top, reach - shorePoint.y, groundMask, QueryTriggerInteraction.Ignore)
                || top.normal.y < 0.7f)
                return;

            climbingOut = true;
            shoreTop = top.point.y;
            climbDirection = -shoreNormal;
            climbSteps = 0;
        }

        /// <summary>
        /// Climbing out of the water onto the edge it swam into: rise until the underside clears the edge, move over it
        /// until the centre is above the ledge, then drop on. Letting go of the input drops it back into the water.
        /// </summary>
        void ClimbOut(Vector3 wish)
        {
            Vector3 center = body.worldCenterOfMass;
            float bottom = solidShape != null ? solidShape.bounds.min.y : center.y - CoreRadius;
            bool overLedge = Physics.Raycast(center, Vector3.down, out RaycastHit below, center.y - bottom + 0.1f, groundMask,
                                             QueryTriggerInteraction.Ignore)
                             && below.normal.y > 0.7f && below.point.y > shoreTop - 0.05f;
            if (wish.sqrMagnitude < 0.01f || overLedge || ++climbSteps > MaxClimbSteps)
            {
                climbingOut = false;
                return;
            }

            // Rising: lean on the edge so it doesn't drift off. Clear of it: hold the height and move over. No spin:
            // rubbing up the edge would otherwise roll the crystal over backwards.
            bool rising = bottom < shoreTop + ShoreClearance;
            Vector3 velocity = climbDirection * (rising ? 0.3f : shoreClimbSpeed);
            velocity.y = rising ? Mathf.Max(body.linearVelocity.y, shoreClimbSpeed) : Mathf.Max(body.linearVelocity.y, 0f);
            body.linearVelocity = velocity;
            body.angularVelocity = Vector3.zero;
        }

        void BrakeSolid()
        {
            float dt = Time.fixedDeltaTime;
            Vector3 planar = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            Vector3 slowed = Vector3.MoveTowards(planar, Vector3.zero, solid.braking * dt);
            body.AddForce(slowed - planar, ForceMode.VelocityChange);
            // A rolling shape would spin itself back up to speed through friction, so slow the spin as well
            // (spin rate ~ speed / radius, with a radius of about half a metre).
            body.angularVelocity = Vector3.MoveTowards(body.angularVelocity, Vector3.zero, solid.braking * 2f * dt);
        }

        void OnCollisionStay(Collision collision)
        {
            if ((groundMask.value & (1 << collision.gameObject.layer)) == 0) return;

            if (isSolid)
            {
                if (collision.gameObject.layer == pushableLayer) solidCrateContact = true;
                // The level itself (fixed, or a door or lift) can be a shore to climb out of the water onto.
                bool partOfLevel = collision.rigidbody == null || collision.rigidbody.isKinematic;
                // Anything holding the solid up from below counts as ground for pushing.
                for (int i = 0; i < collision.contactCount; i++)
                {
                    ContactPoint contact = collision.GetContact(i);
                    Vector3 normal = contact.normal;
                    if (normal.y > 0.5f)
                    {
                        solidGroundContact = true;
                        solidGroundNormal = normal;
                    }
                    else if (partOfLevel && normal.y > -0.3f)
                    {
                        shoreContact = true;
                        shoreNormal = Vector3.ProjectOnPlane(normal, Vector3.up).normalized;
                        shorePoint = contact.point;
                    }
                }
                return;
            }

            float bottom = transform.TransformPoint(core.center).y - CoreRadius;
            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                float height = contact.point.y - bottom;
                // A steep surface touched low on the core is a step edge; touched higher up it is a wall.
                if (contact.normal.y < 0.7f && height > ContactGap && height < maxStepHeight)
                {
                    stepContact = true;
                    stepNormal = Vector3.ProjectOnPlane(contact.normal, Vector3.up).normalized;
                }
            }
        }

        void ProbeGround()
        {
            float radius = CoreRadius;
            float castRadius = radius * 0.9f;
            Vector3 origin = transform.TransformPoint(core.center);
            float distance = radius - castRadius + groundProbeDistance;

            IsGrounded = Physics.SphereCast(origin, castRadius, Vector3.down, out RaycastHit hit, distance, groundMask, QueryTriggerInteraction.Ignore)
                         && Vector3.Angle(hit.normal, Vector3.up) <= maxGroundAngle;
            GroundNormal = IsGrounded ? hit.normal : Vector3.up;
            groundGap = IsGrounded ? hit.distance - (radius - castRadius) : float.PositiveInfinity;
        }

        /// <summary>Skimming just above the ground: turn any velocity away from it along the surface, keeping speed.</summary>
        void HugGround()
        {
            Vector3 velocity = body.linearVelocity;
            float away = Vector3.Dot(velocity, GroundNormal);
            if (away > 0f) body.linearVelocity = (velocity - GroundNormal * away).normalized * velocity.magnitude;
        }

        void SnapToGround()
        {
            Vector3 velocity = body.linearVelocity;
            float speed = velocity.magnitude;
            if (speed > snapMaxSpeed) return;

            float castRadius = CoreRadius * 0.9f;
            Vector3 origin = transform.TransformPoint(core.center);
            if (!Physics.SphereCast(origin, castRadius, Vector3.down, out RaycastHit hit, snapDistance, groundMask, QueryTriggerInteraction.Ignore)
                || Vector3.Angle(hit.normal, Vector3.up) > maxGroundAngle)
                return;

            IsGrounded = true;
            GroundNormal = hit.normal;
            stepsSinceGrounded = 0;
            groundGap = hit.distance - (CoreRadius - castRadius);
            // Keep the speed but turn it along the surface, so the blob flows over the crest.
            float away = Vector3.Dot(velocity, hit.normal);
            if (away > 0f) body.linearVelocity = (velocity - hit.normal * away).normalized * speed;
        }

        Vector3 WishDirection()
        {
            Vector2 move = input != null ? input.Move : Vector2.zero;
            if (move.sqrMagnitude < 0.0001f) return Vector3.zero;

            Vector3 forward = Vector3.forward;
            if (cameraTransform != null)
            {
                forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
                // Looking straight down: the camera's up vector is the screen's forward.
                if (forward.sqrMagnitude < 0.01f) forward = Vector3.ProjectOnPlane(cameraTransform.up, Vector3.up);
                forward.Normalize();
            }
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            return forward * move.y + right * move.x;
        }

        static float MaxAbsComponent(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));
    }
}
