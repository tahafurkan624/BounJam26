using System.Collections;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Soft skin around the blob's core. Every skin point is pulled by a spring toward a rest ellipsoid that sits on
    /// the ground and flattens with speed (keeping its volume), smoothed toward its neighbours (surface tension) and
    /// pushed outward when volume is lost (pressure). Raycasts from the core keep the points out of the world, so the
    /// liquid squishes against floors, walls and obstacles. Runs right after each physics step and drives the skin mesh.
    /// </summary>
    [RequireComponent(typeof(BlobMotor))]
    public class BlobSkin : MonoBehaviour
    {
        [Tooltip("Renders the skin. Its transform must sit at the blob's origin with no rotation or scale.")]
        [SerializeField] MeshFilter skinFilter;

        [Header("Shape")]
        [Tooltip("Radius of the round blob at rest, m.")]
        [SerializeField] float radius = 0.5f;
        [Tooltip("Skin detail: 1 = 42 points, 2 = 162, 3 = 642.")]
        [Range(1, 3)]
        [SerializeField] int subdivisions = 2;
        [Tooltip("Smoothing steps for the rendered surface; each one makes it 4x finer. 2 turns 162 points into 2562 vertices.")]
        [Range(0, 3)]
        [SerializeField] int renderSmoothing = 2;

        [Header("Softness")]
        [Tooltip("Pull of each point toward its rest position. Higher = firmer jelly.")]
        [SerializeField] float stiffness = 260f;
        [Tooltip("Damps point motion relative to the core. Lower = longer wobble.")]
        [SerializeField] float damping = 11f;
        [Tooltip("Pull toward the average of neighbouring points; smooths dents and bumps.")]
        [SerializeField] float surfaceTension = 40f;
        [Tooltip("Outward push when squished below the rest volume, so the liquid spreads instead of shrinking.")]
        [SerializeField] float pressure = 300f;
        [Tooltip("Share of gravity acting on the skin; makes it sag slightly.")]
        [Range(0f, 1f)]
        [SerializeField] float gravitySag = 0.3f;

        [Header("Speed Flattening")]
        [Tooltip("How flat it gets at full speed: 0 = stays round, 0.5 = half height (and wider, same volume).")]
        [Range(0f, 0.8f)]
        [SerializeField] float flattenAtFullSpeed = 0.45f;
        [Tooltip("Extra stretch along the movement direction at full speed.")]
        [Range(0f, 0.5f)]
        [SerializeField] float stretchAtFullSpeed = 0.15f;
        [Tooltip("How quickly the shape follows speed changes.")]
        [SerializeField] float flattenStiffness = 120f;
        [Tooltip("Lower = more bounce between flat and tall when speeding up or stopping.")]
        [SerializeField] float flattenDamping = 9.5f;
        [Tooltip("Extra squash when landing, per m/s of falling speed.")]
        [SerializeField] float landingSquash = 0.5f;
        [Tooltip("Stretch upward when jumping, per m/s of take-off speed.")]
        [SerializeField] float jumpStretch = 0.5f;
        [Tooltip("Gentle squash-and-stretch while standing still, so the blob looks alive. Fades out with speed.")]
        [Range(0f, 0.15f)]
        [SerializeField] float idleBreath = 0.04f;
        [Tooltip("Idle breaths per second.")]
        [SerializeField] float idleBreathRate = 0.8f;

        [Header("Draping")]
        [Tooltip("Lower skin points look this far down for ground and sink onto it, so the liquid drapes over bumps and edges, m.")]
        [SerializeField] float drapeDistance = 0.6f;
        [Tooltip("How much of the gap below a point it sinks into. Strongest at the very bottom, fading toward the sides.")]
        [Range(0f, 1f)]
        [SerializeField] float drapeStrength = 0.9f;

        [Header("Collision")]
        [SerializeField] LayerMask collisionMask = ~0;
        [Tooltip("Gap kept between the skin and surfaces, m.")]
        [SerializeField] float pointRadius = 0.03f;
        [Tooltip("Share of sliding speed a touching point loses per physics step; drags the bottom behind a little.")]
        [Range(0f, 1f)]
        [SerializeField] float contactFriction = 0.05f;

        BlobMotor motor;
        Rigidbody body;
        Mesh mesh;
        Coroutine simulation;

        Vector3[] restDirections;
        int[] triangles;
        int[][] neighbours;
        float restVolume;

        Vector3[] positions;
        Vector3[] velocities;
        Vector3[] restPositions;
        Vector3[] tensionTargets;
        Vector3[] previousOffsets;
        Vector3[] currentOffsets;
        Vector3[] coarseVertices;
        Vector3[] smoothVertices;
        LoopSubdivision smoothing;
        bool initialised;
        Vector3[] frozenLocal;
        Mesh frozenMesh;

        float flatten;
        float flattenVelocity;
        float stretch;
        bool wasGrounded;
        Vector3 previousCoreVelocity;
        Vector3 frameUp = Vector3.up;
        Vector3 frameForward = Vector3.forward;

        /// <summary>Current skin point positions in world space (last physics step).</summary>
        public Vector3[] Points => positions;
        /// <summary>Centre of the rest shape the skin is pulled toward.</summary>
        public Vector3 ShapeCenter { get; private set; }
        /// <summary>0 = round, positive = flattened by speed, negative = briefly taller than round.</summary>
        public float Flatten => flatten;
        /// <summary>Current volume divided by rest volume.</summary>
        public float VolumeRatio { get; private set; } = 1f;

        /// <summary>The blob's top surface at one spot, from <see cref="TrySampleSurface"/>.</summary>
        public struct SurfaceSample
        {
            public float Height;
            public Vector3 Velocity;
            public Vector3 Normal;
        }

        /// <summary>True while hardened into the solid form: the shape is fixed and moves rigidly with the body.</summary>
        public bool IsFrozen { get; private set; }
        /// <summary>False until the first physics step has placed the skin points.</summary>
        public bool IsReady => initialised;

        /// <summary>
        /// Locks the current shape. Returns the skin points in the blob's local space (at the moment of freezing
        /// the blob has no rotation, so these are the offsets from the core). The first 42 points are an even
        /// coarse subset that's good for building a collider.
        /// </summary>
        public Vector3[] Freeze()
        {
            if (frozenLocal == null) frozenLocal = new Vector3[positions.Length];
            Vector3 core = body.position;
            for (int i = 0; i < positions.Length; i++) frozenLocal[i] = positions[i] - core;

            // Flat-shaded facets: every triangle gets its own vertices, so the frozen blob reads as a hard crystal.
            var vertices = new Vector3[triangles.Length];
            var faceTriangles = new int[triangles.Length];
            for (int k = 0; k < triangles.Length; k++)
            {
                vertices[k] = frozenLocal[triangles[k]];
                faceTriangles[k] = k;
            }
            if (frozenMesh == null) frozenMesh = new Mesh { name = "Blob Frozen" };
            frozenMesh.Clear();
            frozenMesh.vertices = vertices;
            frozenMesh.triangles = faceTriangles;
            frozenMesh.RecalculateNormals();
            frozenMesh.RecalculateBounds();
            if (skinFilter != null) skinFilter.sharedMesh = frozenMesh;

            IsFrozen = true;
            return frozenLocal;
        }

        /// <summary>
        /// Turns back into liquid from wherever the frozen shape is now. Call before resetting the body's
        /// rotation: the points start exactly where the (possibly tumbled) solid was and flow back to rest.
        /// </summary>
        public void Melt()
        {
            if (!IsFrozen) return;
            Vector3 core = body.position;
            Vector3 velocity = body.linearVelocity;
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] = transform.TransformPoint(frozenLocal[i]);
                velocities[i] = velocity;
                previousOffsets[i] = currentOffsets[i] = positions[i] - core;
            }
            if (skinFilter != null) skinFilter.sharedMesh = mesh;
            IsFrozen = false;
        }

        /// <summary>
        /// Finds the top of the skin straight above or below a point (only its x and z matter).
        /// Returns false when the point is outside the blob's footprint, or while frozen (the solid is a collider).
        /// </summary>
        public bool TrySampleSurface(Vector3 point, out SurfaceSample sample)
        {
            sample = default;
            if (!initialised || IsFrozen) return false;

            float reach = radius * 3f;
            float dx = point.x - ShapeCenter.x, dz = point.z - ShapeCenter.z;
            if (dx * dx + dz * dz > reach * reach) return false;

            // Every skin triangle whose footprint covers the point; the highest one is the top surface.
            float bestHeight = float.NegativeInfinity;
            int bestFace = -1;
            float bestA = 0f, bestB = 0f, bestC = 0f;
            for (int f = 0; f < triangles.Length; f += 3)
            {
                Vector3 a = positions[triangles[f]], b = positions[triangles[f + 1]], c = positions[triangles[f + 2]];
                float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(denominator) < 1e-8f) continue;
                float wa = ((b.z - c.z) * (point.x - c.x) + (c.x - b.x) * (point.z - c.z)) / denominator;
                float wb = ((c.z - a.z) * (point.x - c.x) + (a.x - c.x) * (point.z - c.z)) / denominator;
                float wc = 1f - wa - wb;
                if (wa < 0f || wb < 0f || wc < 0f) continue;

                float height = wa * a.y + wb * b.y + wc * c.y;
                if (height <= bestHeight) continue;
                bestHeight = height;
                bestFace = f;
                bestA = wa; bestB = wb; bestC = wc;
            }
            if (bestFace < 0) return false;

            int ia = triangles[bestFace], ib = triangles[bestFace + 1], ic = triangles[bestFace + 2];
            Vector3 normal = Vector3.Cross(positions[ib] - positions[ia], positions[ic] - positions[ia]).normalized;
            sample.Height = bestHeight;
            sample.Velocity = velocities[ia] * bestA + velocities[ib] * bestB + velocities[ic] * bestC;
            sample.Normal = normal.y >= 0f ? normal : -normal;
            return true;
        }

        void Awake()
        {
            motor = GetComponent<BlobMotor>();
            body = GetComponent<Rigidbody>();
            BuildTopology();
        }

        void OnEnable()
        {
            motor.Jumped += OnJumped;
            simulation = StartCoroutine(SimulateAfterPhysics());
        }

        void OnDisable()
        {
            motor.Jumped -= OnJumped;
            if (simulation != null) StopCoroutine(simulation);
            initialised = false;
        }

        /// <summary>
        /// The whole jelly takes off, not just the core: points left behind would spring up past it a moment later and
        /// fling a ship off the top. Negative flatten is taller than round, so the jelly stretches up, then settles.
        /// </summary>
        void OnJumped(float launch)
        {
            for (int i = 0; i < velocities.Length; i++) velocities[i].y += launch;
            flattenVelocity -= jumpStretch * launch;
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (frozenMesh != null) Destroy(frozenMesh);
        }

        void BuildTopology()
        {
            IcoSphere.Build(subdivisions, out restDirections, out triangles);
            int count = restDirections.Length;
            neighbours = IcoSphere.Neighbours(count, triangles);

            positions = new Vector3[count];
            velocities = new Vector3[count];
            restPositions = new Vector3[count];
            tensionTargets = new Vector3[count];
            previousOffsets = new Vector3[count];
            currentOffsets = new Vector3[count];
            coarseVertices = new Vector3[count];

            for (int i = 0; i < count; i++) coarseVertices[i] = restDirections[i] * radius;
            restVolume = IcoSphere.Volume(coarseVertices, triangles);

            smoothing = LoopSubdivision.Build(count, triangles, renderSmoothing);
            smoothVertices = new Vector3[smoothing.VertexCount];
            smoothing.Apply(coarseVertices, smoothVertices);

            mesh = new Mesh { name = "Blob Skin" };
            mesh.MarkDynamic();
            mesh.vertices = smoothVertices;
            mesh.triangles = smoothing.Triangles;
            mesh.RecalculateNormals();
            if (skinFilter != null) skinFilter.sharedMesh = mesh;
        }

        // WaitForFixedUpdate resumes after the physics step, so the core is already where it will be rendered.
        IEnumerator SimulateAfterPhysics()
        {
            var afterPhysics = new WaitForFixedUpdate();
            while (true)
            {
                yield return afterPhysics;
                Step(Time.fixedDeltaTime);
            }
        }

        void Step(float dt)
        {
            if (IsFrozen) return;
            Vector3 core = body.position;
            Vector3 coreVelocity = body.linearVelocity;

            UpdateShape(coreVelocity, dt);
            ComputeRestPositions(core);

            if (!initialised)
            {
                for (int i = 0; i < positions.Length; i++)
                {
                    positions[i] = restPositions[i];
                    velocities[i] = coreVelocity;
                    currentOffsets[i] = positions[i] - core;
                }
                initialised = true;
            }

            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 sum = Vector3.zero;
                int[] around = neighbours[i];
                for (int n = 0; n < around.Length; n++) sum += positions[around[n]];
                tensionTargets[i] = sum / around.Length;
            }

            // Volume and centroid relative to the core keep the float math small far from the origin.
            Vector3 centroid = Vector3.zero;
            for (int i = 0; i < positions.Length; i++)
            {
                coarseVertices[i] = positions[i] - core;
                centroid += coarseVertices[i];
            }
            centroid /= positions.Length;
            VolumeRatio = IcoSphere.Volume(coarseVertices, triangles) / restVolume;
            float pressureAcceleration = pressure * (1f - VolumeRatio);

            Vector3 sag = Physics.gravity * gravitySag;
            float escapeDistance = radius * 4f;
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 p = positions[i];
                Vector3 v = velocities[i];
                Vector3 outward = (coarseVertices[i] - centroid).normalized;

                Vector3 acceleration = stiffness * (restPositions[i] - p)
                                       - damping * (v - coreVelocity)
                                       + surfaceTension * (tensionTargets[i] - p)
                                       + pressureAcceleration * outward
                                       + sag;
                v += acceleration * dt;
                p += v * dt;

                // Lost (teleport, extreme hit): snap back instead of stretching across the level.
                if ((p - core).sqrMagnitude > escapeDistance * escapeDistance)
                {
                    p = restPositions[i];
                    v = coreVelocity;
                }

                positions[i] = p;
                velocities[i] = v;
                Collide(i, core);
            }

            for (int i = 0; i < positions.Length; i++)
            {
                previousOffsets[i] = currentOffsets[i];
                currentOffsets[i] = positions[i] - core;
            }
        }

        void UpdateShape(Vector3 coreVelocity, float dt)
        {
            bool grounded = motor.IsGrounded;
            Vector3 targetUp = grounded ? motor.GroundNormal : Vector3.up;
            frameUp = Vector3.Slerp(frameUp, targetUp, 1f - Mathf.Exp(-10f * dt)).normalized;

            Vector3 planar = Vector3.ProjectOnPlane(coreVelocity, frameUp);
            if (planar.sqrMagnitude > 0.25f)
                frameForward = Vector3.Slerp(frameForward, planar.normalized, 1f - Mathf.Exp(-8f * dt));
            frameForward = Vector3.ProjectOnPlane(frameForward, frameUp);
            frameForward = frameForward.sqrMagnitude > 1e-4f ? frameForward.normalized : Vector3.ProjectOnPlane(Vector3.forward, frameUp).normalized;

            // The physics step has already stopped the fall, so the impact speed comes from the step before.
            if (grounded && !wasGrounded)
                flattenVelocity += landingSquash * Mathf.Max(0f, -Vector3.Dot(previousCoreVelocity, frameUp));
            wasGrounded = grounded;
            previousCoreVelocity = coreVelocity;

            // In the air too: a blob that jumps or flows off an edge at speed stays spread out, instead of snapping back
            // to round and flinging a ship off its top.
            float speed01 = Mathf.Clamp01(motor.NormalizedSpeed);
            float eased = speed01 * speed01 * (3f - 2f * speed01);
            float breath = idleBreath * Mathf.Sin(Time.time * idleBreathRate * 2f * Mathf.PI) * (1f - eased);
            float target = flattenAtFullSpeed * eased + breath;
            flattenVelocity += (flattenStiffness * (target - flatten) - flattenDamping * flattenVelocity) * dt;
            flatten = Mathf.Clamp(flatten + flattenVelocity * dt, -0.3f, 0.8f);
            stretch = stretchAtFullSpeed * eased;
        }

        void ComputeRestPositions(Vector3 core)
        {
            // Ellipsoid with the same volume as the sphere: height shrinks by (1 - flatten), width grows to match.
            float height = 1f - flatten;
            float width = 1f / Mathf.Sqrt(height);
            float along = width * (1f + stretch);
            float across = width / (1f + stretch);
            Quaternion frame = Quaternion.LookRotation(frameForward, frameUp);

            // On the ground the shape rests on the floor rather than around the (smaller) core.
            Vector3 center = motor.IsGrounded ? core + frameUp * (radius * height - motor.CoreRadius) : core;
            ShapeCenter = center;

            bool drape = motor.IsGrounded && drapeStrength > 0f;
            for (int i = 0; i < restDirections.Length; i++)
            {
                Vector3 d = restDirections[i];
                Vector3 rest = center + frame * new Vector3(d.x * across * radius, d.y * height * radius, d.z * along * radius);
                if (drape && d.y < -0.05f) rest = Drape(rest, Mathf.Clamp01(-d.y * 1.4f));
                restPositions[i] = rest;
            }
        }

        Vector3 Drape(Vector3 rest, float weight)
        {
            const float lift = 0.05f;
            if (!Physics.Raycast(rest + Vector3.up * lift, Vector3.down, out RaycastHit hit, drapeDistance + lift, collisionMask, QueryTriggerInteraction.Ignore))
                return rest;
            float gap = hit.distance - lift - pointRadius;
            return gap > 0f ? rest + Vector3.down * (gap * drapeStrength * weight) : rest;
        }

        void Collide(int i, Vector3 core)
        {
            Vector3 toPoint = positions[i] - core;
            float distance = toPoint.magnitude;
            if (distance < 1e-4f) return;

            Vector3 direction = toPoint / distance;
            if (!Physics.Raycast(core, direction, out RaycastHit hit, distance + pointRadius, collisionMask, QueryTriggerInteraction.Ignore))
                return;

            positions[i] = core + direction * Mathf.Max(0f, hit.distance - pointRadius);

            Vector3 v = velocities[i];
            float into = Vector3.Dot(v, hit.normal);
            if (into < 0f) v -= into * hit.normal;
            Vector3 sliding = v - Vector3.Dot(v, hit.normal) * hit.normal;
            velocities[i] = v - sliding * contactFriction;
        }

        void LateUpdate()
        {
            if (!initialised || IsFrozen) return;

            // Match the rigidbody's interpolation so the skin moves as smoothly as the core between physics steps.
            float t = body.interpolation == RigidbodyInterpolation.Interpolate
                ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime)
                : 1f;
            for (int i = 0; i < coarseVertices.Length; i++)
                coarseVertices[i] = Vector3.LerpUnclamped(previousOffsets[i], currentOffsets[i], t);
            smoothing.Apply(coarseVertices, smoothVertices);

            mesh.SetVertices(smoothVertices);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        void OnDrawGizmosSelected()
        {
            if (!initialised) return;
            Gizmos.color = Color.cyan;
            foreach (Vector3 p in positions) Gizmos.DrawSphere(p, 0.015f);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(ShapeCenter, 0.05f);
        }
    }
}
