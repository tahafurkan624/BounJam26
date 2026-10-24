using System.Collections;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Soft skin around the blob's core. Every skin point is pulled by a spring toward a rest shape (the modelled
    /// balloon, or a sphere) that sits on the ground, flattens with speed, stretches out the longer it keeps flowing the
    /// same way and spreads into a flat puddle once it stands still (keeping its volume), smoothed toward its
    /// neighbours (surface tension) and pushed outward when volume is lost (pressure). Raycasts from the core keep the
    /// points out of the world, so the liquid squishes against floors, walls and obstacles. Runs right after each
    /// physics step and drives the skin mesh, whose sides and tail turn ragged while stretched.
    /// </summary>
    [RequireComponent(typeof(BlobMotor))]
    public class BlobSkin : MonoBehaviour
    {
        [Tooltip("Renders the skin. Its transform must sit at the blob's origin with no rotation or scale.")]
        [SerializeField] MeshFilter skinFilter;

        [Header("Shape")]
        [Tooltip("Modelled rest shape, pivot at the bottom centre (needs Read/Write enabled). Empty = a sphere of the radius below.")]
        [SerializeField] Mesh restShape;
        [Tooltip("Radius of the round blob at rest when there's no rest shape, m.")]
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
        [SerializeField] float flattenAtFullSpeed = 0.6f;
        [Tooltip("Extra stretch along the movement direction at full speed.")]
        [Range(0f, 0.5f)]
        [SerializeField] float stretchAtFullSpeed = 0.25f;
        [Tooltip("Stretch after flowing the same way at speed for a while; it builds up from the speed stretch above. " +
                 "Same volume, so the blob gets longer and thinner: a long run turns it into a streak.")]
        [Range(0f, 1.5f)]
        [SerializeField] float sustainedStretch = 1f;
        [Tooltip("Seconds of flowing the same way at speed to build the sustained stretch up fully.")]
        [SerializeField] float stretchBuildTime = 1.2f;
        [Tooltip("Seconds to lose it all again after turning away or slowing down.")]
        [SerializeField] float stretchReleaseTime = 0.35f;
        [Tooltip("Heading more than this far from the way the stretch built up lets it go, degrees. Gentle curves keep it.")]
        [SerializeField] float stretchTurnTolerance = 30f;
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

        [Header("Standing Puddle")]
        [Tooltip("How flat the liquid spreads out once it stands still on the ground: 0 = keeps the round rest shape, " +
                 "0.35 = about two thirds of the height (and a little wider, same volume), so a resting puddle reads apart " +
                 "from the moving streak.")]
        [Range(0f, 0.8f)]
        [SerializeField] float restSpread = 0.35f;
        [Tooltip("Below this share of top speed on the ground the liquid starts spreading out.")]
        [Range(0f, 1f)]
        [SerializeField] float spreadBelowSpeed = 0.25f;
        [Tooltip("Slowing down faster than this, m/s², counts as stopping, so it spreads out while it brakes instead of " +
                 "first popping back up into a round bead.")]
        [SerializeField] float spreadWhileBraking = 8f;
        [Tooltip("Seconds of standing still to spread out fully.")]
        [SerializeField] float spreadTime = 0.5f;
        [Tooltip("Seconds to gather back up after moving off.")]
        [SerializeField] float gatherTime = 0.25f;

        [Header("Draping")]
        [Tooltip("Lower skin points look this far down for ground and sink onto it, so the liquid drapes over bumps and edges, m.")]
        [SerializeField] float drapeDistance = 0.6f;
        [Tooltip("How much of the gap below a point it sinks into. Strongest at the very bottom, fading toward the sides.")]
        [Range(0f, 1f)]
        [SerializeField] float drapeStrength = 0.9f;

        [Header("Ragged Edges")]
        [Tooltip("How far the sides and tail of the fully stretched liquid jut in and out, m, so a streak doesn't have " +
                 "a perfect oval outline. Grows with the stretch: the round blob stays smooth, and so does the front. " +
                 "Only the rendered surface moves.")]
        [SerializeField] float edgeRaggedness = 0.12f;
        [Tooltip("Rough size of the bumps along the edge, m.")]
        [SerializeField] float edgeBumpSize = 0.3f;
        [Tooltip("How fast the bumps run back along the edge while flowing, as a share of the blob's speed.")]
        [SerializeField] float edgeFlow = 0.25f;

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
        // Each point's place in the round rest shape, from the shape's centre; flattening scales these.
        Vector3[] restOffsets;
        float restHalfHeight;
        // Farthest rest point from the centre, m.
        float extent;
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
        // Where on the round rest shape each rendered vertex sits, and how much that spot turns ragged: the sides and
        // tail, not the top, the underside or the front.
        Vector3[] edgeDirections;
        float[] edgeWeights;
        // The rest shape's footprint scale across and along the heading (see ComputeRestPositions).
        float shapeAcross = 1f;
        float shapeAlong = 1f;
        // Runs up while flowing, so the bumps slide back along the edge, in bumps.
        float edgeScroll;
        // Lengths relative to the starting blob (cut in half, grown by droplets).
        float size = 1f;
        LoopSubdivision smoothing;
        bool initialised;
        Vector3[] frozenLocal;
        Mesh frozenMesh;

        float flatten;
        float flattenVelocity;
        float stretch;
        // 0..1: how much of the sustained stretch has built up, and the way it built up along (flat).
        float stretchCharge;
        Vector3 stretchHeading;
        // 0..1: how far it has spread out into the standing puddle.
        float spreadCharge;
        bool wasGrounded;
        Vector3 previousCoreVelocity;
        Vector3 frameUp = Vector3.up;
        // Starts facing -Z, toward the follow camera, so the eyes are in view until the blob first moves.
        Vector3 frameForward = Vector3.back;

        /// <summary>Current skin point positions in world space (last physics step).</summary>
        public Vector3[] Points => positions;
        /// <summary>Centre of the rest shape the skin is pulled toward.</summary>
        public Vector3 ShapeCenter { get; private set; }
        /// <summary>0 = round, positive = flattened by speed or spread out standing still, negative = briefly taller than round.</summary>
        public float Flatten => flatten;
        /// <summary>0..1: how far it has spread out into the flat standing puddle.</summary>
        public float Spread { get; private set; }
        /// <summary>Current volume divided by rest volume.</summary>
        public float VolumeRatio { get; private set; } = 1f;
        /// <summary>Up of the shape: the ground normal while grounded, world up in the air.</summary>
        public Vector3 Up => frameUp;
        /// <summary>The way the blob is heading (or last headed), flat on <see cref="Up"/>.</summary>
        public Vector3 Forward => frameForward;
        /// <summary>Middle of the rendered skin this frame, in the skin's local space.</summary>
        public Vector3 LocalCenter { get; private set; }
        /// <summary>How far the jelly reaches ahead of the core along <see cref="Forward"/>, m (last physics step).</summary>
        public float ReachAhead { get; private set; }
        /// <summary>How far the jelly trails behind the core, against <see cref="Forward"/>, m (last physics step).</summary>
        public float ReachBehind { get; private set; }

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
        /// Scales the rest shape: lengths by <paramref name="factor"/>, the volume by its cube (a blob cut in half, or two
        /// halves flowed back together). The points keep their places, so the jelly shrinks or swells into the new size.
        /// </summary>
        public void Resize(float factor)
        {
            for (int i = 0; i < restOffsets.Length; i++) restOffsets[i] *= factor;
            restHalfHeight *= factor;
            extent *= factor;
            size *= factor;
            restVolume *= factor * factor * factor;
        }

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

            // Stretched out, the ends lie farther out than the rest shape's.
            float reach = extent * (3f + stretch);
            float dx = point.x - ShapeCenter.x, dz = point.z - ShapeCenter.z;
            if (dx * dx + dz * dz > reach * reach) return false;

            // Every skin triangle whose footprint covers the point; the highest one is the top surface.
            float bestHeight = float.NegativeInfinity;
            int bestFace = -1;
            float bestA = 0f, bestB = 0f, bestC = 0f;
            for (int f = 0; f < triangles.Length; f += 3)
            {
                Vector3 a = positions[triangles[f]], b = positions[triangles[f + 1]], c = positions[triangles[f + 2]];
                if (!Footprint(a, b, c, point, out float wa, out float wb, out float wc)) continue;

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

        /// <summary>
        /// Is the point inside the skin? Works in both forms. A ray straight up from inside a closed surface crosses
        /// it an odd number of times, so this counts the skin triangles above the point.
        /// </summary>
        public bool Contains(Vector3 point)
        {
            if (!initialised) return false;
            float reach = extent * (3f + stretch);
            if ((point - body.position).sqrMagnitude > reach * reach) return false;
            // The rest shape has a skin point straight above the core, shared by six triangles; a ray through it would
            // count six crossings. Nudged a fraction of a millimetre, the ray can't line up with points like that.
            point.x += 1.3e-4f;
            point.z += 0.7e-4f;

            int crossings = 0;
            for (int f = 0; f < triangles.Length; f += 3)
            {
                Vector3 a = PointAt(triangles[f]), b = PointAt(triangles[f + 1]), c = PointAt(triangles[f + 2]);
                if (Footprint(a, b, c, point, out float wa, out float wb, out float wc)
                    && wa * a.y + wb * b.y + wc * c.y > point.y)
                    crossings++;
            }
            return (crossings & 1) == 1;
        }

        /// <summary>A skin point in world space; frozen points move rigidly with the body.</summary>
        Vector3 PointAt(int i) => IsFrozen ? body.position + body.rotation * frozenLocal[i] : positions[i];

        /// <summary>
        /// Where a ray from inside the skin leaves the surface rendered this frame, all in the skin's local space.
        /// False while frozen or before the first physics step.
        /// </summary>
        public bool TryFindSurface(Vector3 localOrigin, Vector3 direction, out Vector3 point, out Vector3 normal)
        {
            point = normal = Vector3.zero;
            return initialised && !IsFrozen
                   && MeshRay.Exit(smoothVertices, smoothing.Triangles, localOrigin, direction, out point, out normal);
        }

        /// <summary>
        /// The skin points' rest places, as offsets from the middle of the shape: where a ray from the centre of
        /// <paramref name="shape"/> along each direction leaves its surface. <paramref name="halfHeight"/> is how far
        /// its bottom is below that centre. Without a readable shape it's a sphere of <paramref name="sphereRadius"/>.
        /// </summary>
        public static Vector3[] RestOffsets(Mesh shape, Vector3[] directions, float sphereRadius, out float halfHeight)
        {
            var offsets = new Vector3[directions.Length];
            halfHeight = sphereRadius;
            if (shape != null && !shape.isReadable)
                Debug.LogWarning($"[BounJam] Blob rest shape '{shape.name}' isn't readable; using a sphere.");
            if (shape == null || !shape.isReadable)
            {
                for (int i = 0; i < directions.Length; i++) offsets[i] = directions[i] * sphereRadius;
                return offsets;
            }

            Vector3[] vertices = shape.vertices;
            int[] shapeTriangles = shape.triangles;
            Bounds bounds = shape.bounds;
            halfHeight = bounds.extents.y;
            for (int i = 0; i < directions.Length; i++)
            {
                offsets[i] = MeshRay.Exit(vertices, shapeTriangles, bounds.center, directions[i], out Vector3 hit, out _)
                    ? hit - bounds.center
                    : Vector3.Scale(directions[i], bounds.extents);
            }
            return offsets;
        }

        /// <summary>Barycentric weights of the point's x and z in the triangle's footprint; false when outside it.</summary>
        static bool Footprint(Vector3 a, Vector3 b, Vector3 c, Vector3 point, out float wa, out float wb, out float wc)
        {
            wa = wb = wc = 0f;
            float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(denominator) < 1e-8f) return false;
            wa = ((b.z - c.z) * (point.x - c.x) + (c.x - b.x) * (point.z - c.z)) / denominator;
            wb = ((c.z - a.z) * (point.x - c.x) + (a.x - c.x) * (point.z - c.z)) / denominator;
            wc = 1f - wa - wb;
            return wa >= 0f && wb >= 0f && wc >= 0f;
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

            restOffsets = RestOffsets(restShape, restDirections, radius, out restHalfHeight);
            extent = 0f;
            for (int i = 0; i < count; i++)
            {
                coarseVertices[i] = restOffsets[i];
                extent = Mathf.Max(extent, restOffsets[i].magnitude);
            }
            restVolume = IcoSphere.Volume(coarseVertices, triangles);

            smoothing = LoopSubdivision.Build(count, triangles, renderSmoothing);
            smoothVertices = new Vector3[smoothing.VertexCount];
            // The rest directions smoothed the same way say where on the shape each rendered vertex sits.
            edgeDirections = new Vector3[smoothing.VertexCount];
            edgeWeights = new float[smoothing.VertexCount];
            smoothing.Apply(restDirections, edgeDirections);
            for (int i = 0; i < edgeDirections.Length; i++)
            {
                Vector3 d = edgeDirections[i] = edgeDirections[i].normalized;
                float side = 1f - d.y * d.y;
                // The front stays a smooth bead, which also keeps the eyes on it still.
                edgeWeights[i] = side * side * Mathf.Clamp01((0.65f - d.z) / 0.25f);
            }
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
            float escapeDistance = extent * (4f + stretch);
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

            float ahead = 0f, behind = 0f;
            for (int i = 0; i < positions.Length; i++)
            {
                previousOffsets[i] = currentOffsets[i];
                currentOffsets[i] = positions[i] - core;
                float along = Vector3.Dot(currentOffsets[i], frameForward);
                ahead = Mathf.Max(ahead, along);
                behind = Mathf.Max(behind, -along);
            }
            ReachAhead = ahead;
            ReachBehind = behind;
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
            // A puddle skips it: it's flat already, and the squash would crush its thin layer and splash the ship off.
            if (grounded && !wasGrounded)
                flattenVelocity += landingSquash * (1f - Spread) * Mathf.Max(0f, -Vector3.Dot(previousCoreVelocity, frameUp));
            wasGrounded = grounded;
            float slowing = (Vector3.ProjectOnPlane(previousCoreVelocity, frameUp).magnitude - planar.magnitude) / dt;
            LandPuddle(coreVelocity);
            previousCoreVelocity = coreVelocity;

            // In the air too: a blob that jumps or flows off an edge at speed stays spread out, instead of snapping back
            // to round and flinging a ship off its top.
            // Ease-out: the squeeze sets in early, so it shows during the slow build-up to top speed too.
            float speed01 = Mathf.Clamp01(motor.NormalizedSpeed);
            float eased = 1f - (1f - speed01) * (1f - speed01);
            float breath = idleBreath * Mathf.Sin(Time.time * idleBreathRate * 2f * Mathf.PI) * (1f - eased);
            UpdateSpread(speed01, slowing > spreadWhileBraking, grounded, dt);
            // Ease-out: it slumps quickly at first, while the speed flattening is still falling away.
            Spread = 1f - (1f - spreadCharge) * (1f - spreadCharge);
            float target = Mathf.Lerp(flattenAtFullSpeed * eased, restSpread, Spread) + breath;
            flattenVelocity += (flattenStiffness * (target - flatten) - flattenDamping * flattenVelocity) * dt;
            flatten = Mathf.Clamp(flatten + flattenVelocity * dt, -0.3f, 0.8f);
            UpdateStretchCharge(coreVelocity, speed01, grounded, dt);
            stretch = eased * Mathf.Lerp(stretchAtFullSpeed, sustainedStretch, stretchCharge);
        }

        /// <summary>
        /// The sustained stretch builds up while flowing the same way at speed on the ground and lets go after turning
        /// away or slowing down. The way it builds up along follows gentle curves; in the air it holds.
        /// </summary>
        void UpdateStretchCharge(Vector3 coreVelocity, float speed01, bool grounded, float dt)
        {
            var flat = new Vector3(coreVelocity.x, 0f, coreVelocity.z);
            if (speed01 < 0.5f || flat.sqrMagnitude < 0.01f)
            {
                stretchCharge = Mathf.MoveTowards(stretchCharge, 0f, dt / stretchReleaseTime);
                return;
            }

            Vector3 heading = flat.normalized;
            if (stretchCharge <= 0f) stretchHeading = heading;
            if (Vector3.Angle(stretchHeading, heading) > stretchTurnTolerance)
            {
                stretchCharge = Mathf.MoveTowards(stretchCharge, 0f, dt / stretchReleaseTime);
                return;
            }
            stretchHeading = Vector3.Slerp(stretchHeading, heading, 1f - Mathf.Exp(-3f * dt)).normalized;
            if (grounded) stretchCharge = Mathf.MoveTowards(stretchCharge, 1f, dt / stretchBuildTime);
        }

        /// <summary>
        /// Standing still (or braking to a stop) on the ground the liquid spreads out into a puddle, and gathers back up
        /// once it moves off. In the air it holds, so a standing jump doesn't round up under the ship and throw it.
        /// </summary>
        void UpdateSpread(float speed01, bool braking, bool grounded, float dt)
        {
            if (speed01 > spreadBelowSpeed && !braking)
                spreadCharge = Mathf.MoveTowards(spreadCharge, 0f, dt / Mathf.Max(0.01f, gatherTime));
            else if (grounded)
                spreadCharge = Mathf.MoveTowards(spreadCharge, 1f, dt / Mathf.Max(0.01f, spreadTime));
        }

        /// <summary>
        /// A spread-out puddle is too thin to soak up its own fall: when the core hits the floor the jelly would crash
        /// through itself, and the pressure would splash it out and fling the ship off the top. So the part of the fall
        /// the core just lost, the jelly loses too, as much as it's spread. (Take-off is <see cref="OnJumped"/>.)
        /// </summary>
        void LandPuddle(Vector3 coreVelocity)
        {
            if (Spread <= 0f) return;
            float falling = -Vector3.Dot(previousCoreVelocity, frameUp);
            float stopped = Mathf.Clamp(Vector3.Dot(coreVelocity - previousCoreVelocity, frameUp), 0f, Mathf.Max(0f, falling));
            if (stopped <= 0f) return;
            Vector3 kick = frameUp * (stopped * Spread);
            for (int i = 0; i < velocities.Length; i++) velocities[i] += kick;
        }

        void ComputeRestPositions(Vector3 core)
        {
            // The rest shape squashed with the same volume: height shrinks by (1 - flatten), width grows to match.
            float height = 1f - flatten;
            float width = 1f / Mathf.Sqrt(height);
            float along = width * (1f + stretch);
            float across = width / (1f + stretch);
            shapeAlong = along;
            shapeAcross = across;
            Quaternion frame = Quaternion.LookRotation(frameForward, frameUp);

            // On the ground the shape rests on the floor rather than around the (smaller) core. A puddle thinner than the
            // core keeps hanging from its bottom in the air too: centred on the core it would hover, then drop onto the
            // floor at landing and fling the ship off its top.
            float onFloor = restHalfHeight * height - motor.CoreRadius;
            Vector3 center = core + frameUp * (motor.IsGrounded ? onFloor : Mathf.Min(0f, onFloor) * Spread);
            ShapeCenter = center;

            bool drape = motor.IsGrounded && drapeStrength > 0f;
            for (int i = 0; i < restDirections.Length; i++)
            {
                Vector3 d = restDirections[i];
                Vector3 o = restOffsets[i];
                Vector3 rest = center + frame * new Vector3(o.x * across, o.y * height, o.z * along);
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
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < coarseVertices.Length; i++)
            {
                coarseVertices[i] = Vector3.LerpUnclamped(previousOffsets[i], currentOffsets[i], t);
                sum += coarseVertices[i];
            }
            LocalCenter = sum / coarseVertices.Length;
            smoothing.Apply(coarseVertices, smoothVertices);
            Roughen();

            mesh.SetVertices(smoothVertices);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// Jags the rendered outline of the stretched liquid: the sides and tail jut in and out along the ground,
        /// following a noise pattern that slides back along the edge while it flows.
        /// </summary>
        void Roughen()
        {
            float amount = edgeRaggedness * size * Mathf.Clamp01(stretch / Mathf.Max(0.01f, sustainedStretch));
            if (amount < 1e-4f) return;

            float bump = Mathf.Max(0.05f, edgeBumpSize * size);
            float speed = Vector3.ProjectOnPlane(body.linearVelocity, frameUp).magnitude;
            edgeScroll += edgeFlow * speed * Time.deltaTime / bump;
            if (edgeScroll > 1000f) edgeScroll -= 1000f;

            Vector3 right = Vector3.Cross(frameUp, frameForward);
            for (int i = 0; i < smoothVertices.Length; i++)
            {
                float weight = edgeWeights[i];
                if (weight < 0.01f) continue;
                // Straight out of the oval's outline, not away from its middle: on a long streak those differ a lot.
                Vector3 d = edgeDirections[i];
                Vector3 outward = (right * (d.x / shapeAcross) + frameForward * (d.z / shapeAlong)).normalized;
                Vector3 p = smoothVertices[i] - LocalCenter;
                float x = Vector3.Dot(p, right) / bump, z = Vector3.Dot(p, frameForward) / bump;
                smoothVertices[i] += outward * (Bumps(x + 37.1f, z + edgeScroll + 11.3f) * amount * weight);
            }
        }

        /// <summary>Two octaves of Perlin noise centred on 0, about -1..1. Keep the inputs positive: it mirrors around 0.</summary>
        static float Bumps(float u, float v)
        {
            float n = (Mathf.PerlinNoise(u, v) - 0.5f) * 2.6f
                      + (Mathf.PerlinNoise(u * 2.3f + 5.2f, v * 2.3f + 1.9f) - 0.5f) * 1.3f;
            return Mathf.Clamp(n, -1f, 1f);
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
