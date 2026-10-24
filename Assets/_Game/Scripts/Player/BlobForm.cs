using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Switches the blob between liquid and solid. Freezing locks the skin's current shape, gives the body a convex
    /// collider of exactly that shape, makes it heavy, lets it rotate and moves it to the PlayerSolid layer (so it
    /// is stopped by grates, carries the ship as a solid and, later, presses buttons and pushes crates). Melting
    /// reverses all of it, and the liquid flows back to rest from wherever the solid ended up. It can't freeze while the
    /// liquid is partway through a grate (anything only the liquid passes): the crystal would stand inside it.
    /// </summary>
    [RequireComponent(typeof(BlobMotor), typeof(BlobSkin))]
    public class BlobForm : MonoBehaviour
    {
        [SerializeField] PlayerInputReader input;
        [Tooltip("The small sphere the liquid moves on.")]
        [SerializeField] SphereCollider liquidCore;
        [Tooltip("Convex collider that takes the frozen shape. Disabled while liquid.")]
        [SerializeField] MeshCollider solidCollider;
        [Tooltip("Skin renderer; a copy of its material gets _Solid blended between the jelly and the hardened look.")]
        [SerializeField] Renderer skinRenderer;

        [Header("Solid Body")]
        [SerializeField] float solidMass = 5f;
        [Tooltip("Share of the liquid's speed the crystal keeps when it hardens. Low = it sets where it is instead of " +
                 "sliding or rolling on (and off edges).")]
        [Range(0f, 1f)]
        [SerializeField] float momentumKept = 0.25f;
        [Tooltip("Rotation damping when solid: odd frozen shapes tip over slowly instead of spinning wildly.")]
        [SerializeField] float solidAngularDamping = 2f;

        [Header("Feel")]
        [Tooltip("Shortest time between form changes, s.")]
        [SerializeField] float cooldown = 0.25f;
        [Tooltip("How long the look takes to harden, s.")]
        [SerializeField] float hardenTime = 0.12f;
        [Tooltip("How long the look takes to turn back into jelly, s.")]
        [SerializeField] float meltTime = 0.3f;
        [Tooltip("Brief size pop when hardening, as a fraction of the size.")]
        [SerializeField] float freezePop = 0.06f;

        [Header("Overlaps")]
        [Tooltip("What the new crystal is checked against when it appears. The hull is convex, so where the jelly " +
                 "was wrapped around something it can cut into it; the blob steps out of heavy things and pushes " +
                 "light ones (the ship) out of itself.")]
        [SerializeField] LayerMask overlapMask = ~0;
        [Tooltip("Top speed the physics may use to push the blob out of an overlap, m/s. Low = eased out, never shot through a floor.")]
        [SerializeField] float maxDepenetrationSpeed = 3f;

        // The first 42 skin points are the level-1 icosphere: an even subset whose hull stays far below the
        // 255-polygon limit of convex mesh colliders.
        const int HullPointCount = 42;
        static readonly int SolidProperty = Shader.PropertyToID("_Solid");

        BlobMotor motor;
        BlobSkin skin;
        Rigidbody body;
        float liquidMass;
        float liquidAngularDamping;
        RigidbodyConstraints liquidConstraints;
        int liquidLayer;
        int solidLayer;
        // Grates and the like: the liquid flows through them, the crystal doesn't.
        int passableMask;
        Mesh hullMesh;
        int[] hullTriangles;
        Vector3[] hullPoints;
        // The skin's own copy of the jelly material, so _Solid can change for this blob only.
        Material skinMaterial;
        float solidLook;
        float lastSwitchTime = float.NegativeInfinity;
        Coroutine lookTransition;
        readonly Collider[] nearby = new Collider[32];
        readonly RaycastHit[] passageHits = new RaycastHit[8];

        public bool IsSolid { get; private set; }
        /// <summary>The collider that has the frozen shape (only enabled while solid).</summary>
        public Collider SolidCollider => solidCollider;
        /// <summary>The frozen shape's collider points, in the blob's local space (only meaningful while solid).</summary>
        public Vector3[] HullPoints => hullPoints;
        /// <summary>Raised after every form change; true = now solid.</summary>
        public event Action<bool> FormChanged;
        /// <summary>
        /// Raised while freezing, once <see cref="SolidCollider"/> has the frozen shape and before the crystal is
        /// checked for overlaps. Whatever a listener makes it ignore (Physics.IgnoreCollision) it may set around,
        /// like a conveyor's spike it grabs (see <see cref="BlobGrip"/>).
        /// </summary>
        public event Action Hardening;

        void Awake()
        {
            motor = GetComponent<BlobMotor>();
            skin = GetComponent<BlobSkin>();
            body = GetComponent<Rigidbody>();
            if (input == null) input = GetComponent<PlayerInputReader>();

            liquidMass = body.mass;
            liquidAngularDamping = body.angularDamping;
            liquidConstraints = body.constraints;
            body.maxDepenetrationVelocity = maxDepenetrationSpeed;
            liquidLayer = LayerMask.NameToLayer("PlayerLiquid");
            solidLayer = LayerMask.NameToLayer("PlayerSolid");
            passableMask = LayerMask.GetMask("LiquidPassable");

            IcoSphere.Build(1, out _, out hullTriangles);
            hullPoints = new Vector3[HullPointCount];
            if (skinRenderer != null) skinMaterial = skinRenderer.material;
            solidCollider.enabled = false;
            ApplyLook(0f, 1f);
        }

        void OnEnable()
        {
            if (input != null) input.ToggleFormPressed += Toggle;
        }

        void OnDisable()
        {
            if (input != null) input.ToggleFormPressed -= Toggle;
        }

        void OnDestroy()
        {
            if (hullMesh != null) Destroy(hullMesh);
            if (skinMaterial != null) Destroy(skinMaterial);
        }

        public void Toggle()
        {
            if (Time.time - lastSwitchTime < cooldown || !skin.IsReady) return;
            if (IsSolid) Melt();
            // The pieces of a cut blob freeze together: none of them while another is partway through a grate.
            else if (!OtherPieceInPassage()) Freeze();
        }

        public void Freeze()
        {
            if (IsSolid || !skin.IsReady || IsInPassage()) return;

            Vector3[] frozen = skin.Freeze();
            Array.Copy(frozen, hullPoints, HullPointCount);
            // A new mesh every freeze, so the collider is built exactly as on the first freeze. Refilling the mesh the
            // collider had already been built from could leave the second crystal without a working shape, and it
            // fell straight through the floor.
            hullMesh = new Mesh { name = "Blob Solid Hull" };
            hullMesh.vertices = hullPoints;
            hullMesh.triangles = hullTriangles;
            hullMesh.RecalculateBounds();
            solidCollider.sharedMesh = hullMesh;
            solidCollider.enabled = true;
            liquidCore.enabled = false;
            Hardening?.Invoke();
            ResolveOverlaps();

            SetLayer(solidLayer);
            body.mass = solidMass;
            body.angularDamping = solidAngularDamping;
            body.constraints = RigidbodyConstraints.None;
            body.ResetCenterOfMass();
            body.ResetInertiaTensor();
            body.linearVelocity *= momentumKept;
            body.angularVelocity = Vector3.zero;
            motor.SetSolid(true, solidCollider);

            IsSolid = true;
            lastSwitchTime = Time.time;
            StartLookTransition(1f, hardenTime, freezePop);
            FormChanged?.Invoke(true);
        }

        public void Melt()
        {
            if (!IsSolid) return;

            // The skin reads the solid's current pose, so this has to happen before the rotation is cleared.
            skin.Melt();
            body.angularVelocity = Vector3.zero;
            body.rotation = Quaternion.identity;
            transform.rotation = Quaternion.identity;

            liquidCore.enabled = true;
            solidCollider.enabled = false;
            solidCollider.sharedMesh = null;
            Destroy(hullMesh);
            hullMesh = null;
            SetLayer(liquidLayer);
            body.mass = liquidMass;
            body.angularDamping = liquidAngularDamping;
            body.constraints = liquidConstraints;
            body.ResetCenterOfMass();
            body.ResetInertiaTensor();
            motor.SetSolid(false);

            IsSolid = false;
            lastSwitchTime = Time.time;
            StartLookTransition(0f, meltTime, 0f);
            FormChanged?.Invoke(false);
        }

        /// <summary>Multiplies the liquid and solid masses, for a blob that was cut in half or grew back whole.</summary>
        public void ScaleMass(float factor)
        {
            liquidMass *= factor;
            solidMass *= factor;
            body.mass = IsSolid ? solidMass : liquidMass;
        }

        /// <summary>
        /// True while the liquid is partway through something only it can pass, like a grate: a crystal of this shape
        /// would stand inside it. Looks along the lines from the core to the points the crystal would be built from,
        /// and around the core itself, which a ray starting inside a collider can't see. Conveyor spikes share the
        /// layer but are there to be frozen around (see <see cref="BlobGrip"/>), so they don't count.
        /// </summary>
        bool IsInPassage()
        {
            if (IsSolid || !skin.IsReady || passableMask == 0) return false;

            Vector3 core = body.position;
            int count = Physics.OverlapSphereNonAlloc(core, motor.CoreRadius, nearby, passableMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (IsPassage(nearby[i])) return true;

            Vector3[] points = skin.Points;
            for (int i = 0; i < HullPointCount; i++)
            {
                Vector3 toPoint = points[i] - core;
                int hits = Physics.RaycastNonAlloc(core, toPoint, passageHits, toPoint.magnitude, passableMask,
                                                   QueryTriggerInteraction.Ignore);
                for (int h = 0; h < hits; h++)
                    if (IsPassage(passageHits[h].collider)) return true;
            }
            return false;
        }

        static bool IsPassage(Collider other) => other.GetComponentInParent<ConveyorSpike>() == null;

        bool OtherPieceInPassage()
        {
            IReadOnlyList<BlobSplit> pieces = BlobSplit.All;
            for (int i = 0; i < pieces.Count; i++)
            {
                BlobForm other = pieces[i].Form;
                if (other != this && other.isActiveAndEnabled && other.IsInPassage()) return true;
            }
            return false;
        }

        /// <summary>
        /// The crystal is the convex hull of the jelly, so where the jelly was wrapped around something (a crate's
        /// corner, a ship being scooped up) it cuts into it. Left alone, the physics would shove the bodies apart
        /// hard enough to send the blob through the floor. Instead: step out of anything heavy or fixed, and move
        /// light bodies (the ship) out of the crystal. Two passes settle overlaps with several things at once.
        /// </summary>
        void ResolveOverlaps()
        {
            Bounds hull = hullMesh.bounds;
            for (int pass = 0; pass < 2; pass++)
            {
                Vector3 center = transform.TransformPoint(hull.center);
                int count = Physics.OverlapBoxNonAlloc(center, hull.extents + Vector3.one * 0.05f, nearby, transform.rotation,
                                                       overlapMask, QueryTriggerInteraction.Ignore);
                Vector3 step = Vector3.zero;
                for (int i = 0; i < count; i++)
                {
                    Collider other = nearby[i];
                    Rigidbody otherBody = other.attachedRigidbody;
                    if (otherBody == body || Physics.GetIgnoreCollision(solidCollider, other)) continue;

                    // Moving bodies render interpolated; use where the physics actually has them.
                    bool onBody = otherBody != null && other.transform == otherBody.transform;
                    Vector3 otherPosition = onBody ? otherBody.position : other.transform.position;
                    Quaternion otherRotation = onBody ? otherBody.rotation : other.transform.rotation;
                    if (!Physics.ComputePenetration(solidCollider, body.position + step, body.rotation,
                                                    other, otherPosition, otherRotation,
                                                    out Vector3 direction, out float distance))
                        continue;

                    if (otherBody != null && !otherBody.isKinematic && otherBody.mass < body.mass * 0.5f)
                    {
                        // Light and loose: move it out of the crystal instead of moving the crystal.
                        otherBody.position -= direction * distance;
                        otherBody.transform.position -= direction * distance;
                    }
                    else step += direction * distance;
                }
                if (step == Vector3.zero) break;
                body.position += step;
                transform.position += step;
            }
        }

        void SetLayer(int layer)
        {
            gameObject.layer = layer;
            if (skinRenderer != null) skinRenderer.gameObject.layer = layer;
        }

        void StartLookTransition(float target, float duration, float pop)
        {
            if (lookTransition != null) StopCoroutine(lookTransition);
            lookTransition = StartCoroutine(LookTransition(target, duration, pop));
        }

        IEnumerator LookTransition(float target, float duration, float pop)
        {
            float start = solidLook;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = t / duration;
                ApplyLook(Mathf.Lerp(start, target, k), 1f + pop * Mathf.Sin(k * Mathf.PI));
                yield return null;
            }
            ApplyLook(target, 1f);
            lookTransition = null;
        }

        void ApplyLook(float solid, float scale)
        {
            solidLook = solid;
            if (skinRenderer == null) return;
            skinMaterial.SetFloat(SolidProperty, Mathf.Clamp(solid, 0 , 0.75f));
            skinRenderer.transform.localScale = Vector3.one * scale;
        }
    }
}
