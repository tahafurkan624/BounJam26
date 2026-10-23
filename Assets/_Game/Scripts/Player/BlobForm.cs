using System;
using System.Collections;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Switches the blob between liquid and solid. Freezing locks the skin's current shape, gives the body a convex
    /// collider of exactly that shape, makes it heavy, lets it rotate and moves it to the PlayerSolid layer (so it
    /// is stopped by grates, carries the ship as a solid and, later, presses buttons and pushes crates). Melting
    /// reverses all of it, and the liquid flows back to rest from wherever the solid ended up.
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
        Mesh hullMesh;
        int[] hullTriangles;
        Vector3[] hullPoints;
        // The skin's own copy of the jelly material, so _Solid can change for this blob only.
        Material skinMaterial;
        float solidLook;
        float lastSwitchTime = float.NegativeInfinity;
        Coroutine lookTransition;
        readonly Collider[] nearby = new Collider[32];

        public bool IsSolid { get; private set; }
        /// <summary>The collider that has the frozen shape (only enabled while solid).</summary>
        public Collider SolidCollider => solidCollider;
        /// <summary>Raised after every form change; true = now solid.</summary>
        public event Action<bool> FormChanged;

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

            IcoSphere.Build(1, out _, out hullTriangles);
            hullPoints = new Vector3[HullPointCount];
            hullMesh = new Mesh { name = "Blob Solid Hull" };
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
            else Freeze();
        }

        public void Freeze()
        {
            if (IsSolid || !skin.IsReady) return;

            Vector3[] frozen = skin.Freeze();
            Array.Copy(frozen, hullPoints, HullPointCount);
            hullMesh.Clear();
            hullMesh.vertices = hullPoints;
            hullMesh.triangles = hullTriangles;
            hullMesh.RecalculateBounds();
            solidCollider.sharedMesh = null; // reassigning forces the collider to rebuild from the new shape
            solidCollider.sharedMesh = hullMesh;
            solidCollider.enabled = true;
            liquidCore.enabled = false;
            ResolveOverlaps();

            SetLayer(solidLayer);
            body.mass = solidMass;
            body.angularDamping = solidAngularDamping;
            body.constraints = RigidbodyConstraints.None;
            body.ResetCenterOfMass();
            body.ResetInertiaTensor();
            motor.SetSolid(true);

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
                    if (otherBody == body) continue;

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
            skinMaterial.SetFloat(SolidProperty, solid);
            skinRenderer.transform.localScale = Vector3.one * scale;
        }
    }
}
