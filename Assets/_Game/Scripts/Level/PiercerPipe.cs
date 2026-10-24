using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// A pipe with a spinning blade inside. A whole liquid blob that flows into its mouth is pulled in and carried
    /// through, out of the player's hands until it comes out. At the blade it's cut into two halves (see
    /// <see cref="BlobSplit"/>), which come out of the two exits at the same time. If the ship was riding on the blob it
    /// goes through too, and always comes out of the first exit on top of that half. Halves and the solid crystal can't
    /// get in: they bump into the mouth, which flashes red.
    /// The mouth, the blade and both exits are child transforms, each on the floor in the middle of its opening and,
    /// except the blade, facing out of the pipe. The blob travels in straight lines from the mouth to the blade, then
    /// each half from there to its exit.
    /// </summary>
    public class PiercerPipe : MonoBehaviour
    {
        [SerializeField] Transform mouth;
        [SerializeField] Transform blade;
        [Tooltip("The ship always comes out here, on the half that leaves this way.")]
        [SerializeField] Transform firstExit;
        [SerializeField] Transform secondExit;

        [Header("Mouth")]
        [Tooltip("A blob whose core comes into this box in front of the mouth is pulled in, m: width, height, and depth " +
                 "out from the mouth.")]
        [SerializeField] Vector3 intake = new Vector3(1.1f, 1.2f, 0.8f);

        [Header("Travel")]
        [Tooltip("Speed through the pipe, m/s.")]
        [SerializeField] float travelSpeed = 4f;
        [Tooltip("Pause at the blade while it cuts, s.")]
        [SerializeField] float cutPause = 0.3f;
        [Tooltip("The halves are let go this far out in front of each exit, clear of the pipe, m.")]
        [SerializeField] float exitClearance = 0.6f;
        [Tooltip("Speed the halves come out with, m/s.")]
        [SerializeField] float exitSpeed = 2.5f;
        [Tooltip("The ship is let go this high above the top of its half's jelly, m.")]
        [SerializeField] float shipDrop = 0.05f;

        [Header("Look")]
        [Tooltip("Spins about its local up axis.")]
        [SerializeField] Transform bladeModel;
        [Tooltip("Blade spin while idle, degrees/s.")]
        [SerializeField] float idleSpin = 240f;
        [Tooltip("Blade spin while cutting, degrees/s.")]
        [SerializeField] float cutSpin = 1500f;
        [Tooltip("Flashes when something that can't go in presses into the mouth. Its material needs emission enabled.")]
        [SerializeField] Renderer mouthRenderer;
        [ColorUsage(false, true)]
        [SerializeField] Color refuseGlow = new Color(2.5f, 0.25f, 0.15f);
        [Tooltip("How quickly the flash fades, 1/s.")]
        [SerializeField] float refuseFade = 4f;

        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        /// <summary>The ship while it rides through the pipe: no physics of its own, moved along with its half.</summary>
        class StowedShip
        {
            public ShipBuoyancy Ship;
            public Collider[] Colliders;
            public Renderer[] Renderers;
            public Behaviour Guard;
            public Vector3 Offset;
        }

        /// <summary>One piece on its way along a path; let go out of <see cref="Exit"/> once it's at the end, if set.</summary>
        class Rider
        {
            public BlobSplit Piece;
            public Vector3[] Path;
            public int Next;
            public Transform Exit;
            public StowedShip Ship;
            public bool Done => Next >= Path.Length;
        }

        ShipBuoyancy ship;
        MaterialPropertyBlock block;
        float refuse;
        float spin;
        bool busy;
        bool cutting;

        void Awake()
        {
            ship = FindAnyObjectByType<ShipBuoyancy>();
            block = new MaterialPropertyBlock();
            spin = idleSpin;
        }

        void FixedUpdate()
        {
            IReadOnlyList<BlobSplit> pieces = BlobSplit.All;
            for (int i = 0; i < pieces.Count; i++)
            {
                BlobSplit piece = pieces[i];
                if (piece.InPipe || !InIntake(piece.Body.position)) continue;
                if (piece.CanBeCut && !busy)
                {
                    StartCoroutine(Carry(piece));
                    return;
                }
                refuse = 1f;
            }
        }

        void Update()
        {
            spin = Mathf.Lerp(spin, cutting ? cutSpin : idleSpin, 1f - Mathf.Exp(-6f * Time.deltaTime));
            if (bladeModel != null) bladeModel.Rotate(Vector3.up, spin * Time.deltaTime, Space.Self);

            refuse = Mathf.Max(0f, refuse - refuseFade * Time.deltaTime);
            if (mouthRenderer != null)
            {
                mouthRenderer.GetPropertyBlock(block);
                block.SetColor(EmissionColor, refuseGlow * refuse);
                mouthRenderer.SetPropertyBlock(block);
            }
        }

        bool InIntake(Vector3 point)
        {
            Vector3 local = mouth.InverseTransformPoint(point);
            return Mathf.Abs(local.x) <= intake.x * 0.5f && local.y >= 0f && local.y <= intake.y
                   && local.z >= 0f && local.z <= intake.z;
        }

        IEnumerator Carry(BlobSplit blob)
        {
            busy = true;
            StowedShip stowed = blob.CarriesShip ? Stow(blob) : null;
            blob.EnterPipe();

            // Pressed into the mouth, then out of sight along the pipe to the blade.
            var rider = new Rider { Piece = blob, Path = new[] { OnPath(mouth, blob) }, Ship = stowed };
            yield return Travel(rider, null);
            blob.SetHidden(true);
            if (stowed != null) SetShipHidden(stowed, true);
            rider.Path = new[] { OnPath(blade, blob) };
            rider.Next = 0;
            yield return Travel(rider, null);

            cutting = true;
            Hold(blob, stowed);
            yield return new WaitForSeconds(cutPause);
            BlobSplit second = blob.Split();
            cutting = false;

            // Both halves at once, each to its own exit and a little way out of it; the ship stays with the first.
            var first = new Rider { Piece = blob, Path = ExitPath(firstExit, blob), Exit = firstExit, Ship = stowed };
            var other = new Rider { Piece = second, Path = ExitPath(secondExit, second), Exit = secondExit };
            yield return Travel(first, other);
            busy = false;
        }

        /// <summary>Moves the riders along their paths, one physics step at a time, until both are at the end.</summary>
        IEnumerator Travel(Rider a, Rider b)
        {
            var afterPhysics = new WaitForFixedUpdate();
            while (!a.Done || (b != null && !b.Done))
            {
                Step(a);
                if (b != null) Step(b);
                yield return afterPhysics;
            }
        }

        void Step(Rider rider)
        {
            if (rider.Done) return;
            float dt = Time.fixedDeltaTime;
            Rigidbody body = rider.Piece.Body;
            Vector3 from = body.position;
            Vector3 to = Vector3.MoveTowards(from, rider.Path[rider.Next], travelSpeed * dt);
            if ((rider.Path[rider.Next] - to).sqrMagnitude < 1e-6f) rider.Next++;
            body.linearVelocity = (to - from) / dt;
            if (rider.Ship != null)
            {
                Rigidbody hull = rider.Ship.Ship.Body;
                hull.linearVelocity = (to + rider.Ship.Offset - hull.position) / dt;
                hull.angularVelocity = Vector3.zero;
            }
            if (rider.Done && rider.Exit != null) Release(rider);
        }

        void Hold(BlobSplit blob, StowedShip stowed)
        {
            blob.Body.linearVelocity = Vector3.zero;
            if (stowed != null) stowed.Ship.Body.linearVelocity = Vector3.zero;
        }

        /// <summary>Out of the exit, back in the player's hands; the ship, if it rode along, set down on top.</summary>
        void Release(Rider rider)
        {
            Vector3 outward = Vector3.ProjectOnPlane(rider.Exit.forward, Vector3.up).normalized;
            rider.Piece.LeavePipe(outward * exitSpeed);
            if (rider.Ship == null) return;

            StowedShip stowed = rider.Ship;
            Rigidbody hull = stowed.Ship.Body;
            float top = float.NegativeInfinity;
            foreach (Vector3 point in rider.Piece.Skin.Points) top = Mathf.Max(top, point.y);
            Vector3 core = rider.Piece.Body.position;
            Vector3 position = new Vector3(core.x, top + shipDrop, core.z);
            Quaternion rotation = Quaternion.LookRotation(outward, Vector3.up);
            hull.position = position;
            hull.rotation = rotation;
            stowed.Ship.transform.SetPositionAndRotation(position, rotation);
            SetShipHidden(stowed, false);
            foreach (Collider part in stowed.Colliders) part.enabled = true;
            hull.useGravity = true;
            hull.linearVelocity = outward * exitSpeed;
            hull.angularVelocity = Vector3.zero;
            stowed.Ship.enabled = true;
            if (stowed.Guard != null) stowed.Guard.enabled = true;
        }

        /// <summary>The ship rides through with the blob: no floating, collisions or gravity of its own until it's out.</summary>
        StowedShip Stow(BlobSplit blob)
        {
            var stowed = new StowedShip
            {
                Ship = ship,
                Colliders = ship.GetComponentsInChildren<Collider>(),
                Renderers = ship.GetComponentsInChildren<Renderer>(),
                Guard = ship.GetComponent<GroundGuard>(),
                Offset = ship.Body.position - blob.Body.position,
            };
            ship.enabled = false;
            if (stowed.Guard != null) stowed.Guard.enabled = false;
            foreach (Collider part in stowed.Colliders) part.enabled = false;
            ship.Body.useGravity = false;
            ship.Body.linearVelocity = blob.Body.linearVelocity;
            ship.Body.angularVelocity = Vector3.zero;
            return stowed;
        }

        static void SetShipHidden(StowedShip stowed, bool hidden)
        {
            foreach (Renderer part in stowed.Renderers) part.forceRenderingOff = hidden;
        }

        /// <summary>Where the piece's core runs through an opening: its centre, the core's height above the floor.</summary>
        static Vector3 OnPath(Transform opening, BlobSplit piece) => opening.position + Vector3.up * piece.Motor.CoreRadius;

        Vector3[] ExitPath(Transform exit, BlobSplit piece)
        {
            Vector3 end = OnPath(exit, piece);
            return new[] { end, end + Vector3.ProjectOnPlane(exit.forward, Vector3.up).normalized * exitClearance };
        }

        void OnDrawGizmos()
        {
            if (mouth == null) return;
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.7f);
            Gizmos.matrix = mouth.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, intake.y * 0.5f, intake.z * 0.5f), intake);
            Gizmos.matrix = Matrix4x4.identity;
            if (blade == null) return;
            Gizmos.DrawLine(mouth.position, blade.position);
            Gizmos.color = new Color(0.95f, 0.9f, 0.75f);
            if (firstExit != null) Gizmos.DrawLine(blade.position, firstExit.position);
            Gizmos.color = new Color(0.6f, 0.6f, 0.6f);
            if (secondExit != null) Gizmos.DrawLine(blade.position, secondExit.position);
        }
    }
}
