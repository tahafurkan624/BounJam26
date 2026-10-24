using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Switches the blob between liquid and solid. Freezing locks the skin's current shape, gives the body colliders of
    /// that shape, makes it heavy, lets it rotate and moves it to the PlayerSolid layer (so it is stopped by grates,
    /// carries the ship as a solid and, later, presses buttons and pushes crates). The colliders are one convex hull, or
    /// where that would fill in the dents the jelly took around a post or over an edge and cut into the level, convex
    /// pieces that keep them (see <see cref="BuildCrystal"/>). Melting reverses all of it, and the liquid flows back to
    /// rest from wherever the solid ended up. It can't freeze while the liquid is partway through a grate (anything only
    /// the liquid passes): the crystal would stand inside it.
    /// </summary>
    [RequireComponent(typeof(BlobMotor), typeof(BlobSkin))]
    public class BlobForm : MonoBehaviour
    {
        [SerializeField] PlayerInputReader input;
        [Tooltip("The small sphere the liquid moves on.")]
        [SerializeField] SphereCollider liquidCore;
        [Tooltip("Convex collider that takes the frozen shape; more like it are added when the crystal is cut into pieces. " +
                 "Disabled while liquid.")]
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

        [Header("Crystal Shape")]
        [Tooltip("The crystal is one convex hull unless that would cut deeper than this into the level, m. Where the " +
                 "jelly was wrapped around a post or draped over an edge, the hull fills in the dent; then the crystal " +
                 "is cut into convex pieces that keep it.")]
        [SerializeField] float fitTolerance = 0.03f;
        [Tooltip("Most pieces the crystal may be cut into.")]
        [SerializeField] int maxPieces = 16;
        [Tooltip("Thinnest piece a cut may leave, m.")]
        [SerializeField] float minPieceSize = 0.06f;
        [Tooltip("For obstacles other than boxes and capsules: a piece cut along all the sides of the obstacle's " +
                 "bounds is left out when at least this share of the shape inside it is the obstacle.")]
        [Range(0f, 1f)]
        [SerializeField] float dropShare = 0.5f;
        [Tooltip("Carve what the crystal was cut around (a post the jelly closed over, an edge it draped over) out of " +
                 "its look, so the post stands in a hole through the ice instead of inside it. A few ms when freezing.")]
        [SerializeField] bool carveLook = true;
        [Tooltip("How much bigger all round than the obstacle the hole carved for it is, m. The skin stops a few cm short " +
                 "of what it presses against; carving exactly the obstacle leaves that gap as a paper-thin shell around " +
                 "the hole, its loose edges sticking out.")]
        [SerializeField] float carveMargin = 0.05f;

        [Header("Overlaps")]
        [Tooltip("What the new crystal is checked against when it appears. Where it still cuts into something (a ship " +
                 "being scooped up, a dent too fine to cut around), the blob steps out of heavy things and pushes light " +
                 "ones (the ship) out of itself.")]
        [SerializeField] LayerMask overlapMask = ~0;
        [Tooltip("Top speed the physics may use to push the blob out of an overlap, m/s. Low = eased out, never shot through a floor.")]
        [SerializeField] float maxDepenetrationSpeed = 3f;

        // The first 42 skin points are the level-1 icosphere: an even subset whose hull stays far below the
        // 255-polygon limit of convex mesh colliders.
        const int HullPointCount = 42;
        // Thinner than this, a piece has no volume to build a convex collider from, m.
        const float MinThickness = 0.005f;
        // A point this close to an obstacle's surface counts as inside it, m.
        const float InsideTolerance = 0.001f;
        // Points along each side of the grid a piece is sampled at, to see whether it's in an obstacle and how much of it is.
        const int ShareSamples = 5;
        // Most sides a round post is ringed with.
        const int MaxSidesAround = 16;
        // A part of the carved look that hangs together with nothing else is a scrap when it's smaller than this share
        // of the biggest part (across its bounds), or holds less than this share of its volume (a thin sheet).
        const double LooseBitShare = 0.2;
        const double LooseBitVolume = 0.03;
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
        Vector3[] hullPoints;
        // What the liquid flows around: what the crystal mustn't cut into.
        LayerMask fitMask;
        FrozenShape shape;
        readonly List<Piece> pieces = new List<Piece>();
        readonly List<Collider> solidColliders = new List<Collider>();
        // Every collider the crystal can be built from, solidCollider first, and those not in use.
        readonly List<MeshCollider> crystalColliders = new List<MeshCollider>();
        readonly Stack<MeshCollider> spareColliders = new Stack<MeshCollider>();
        readonly List<Vector3> lowerPoints = new List<Vector3>();
        readonly List<Vector3> upperPoints = new List<Vector3>();
        readonly List<Plane> obstacleSides = new List<Plane>();
        readonly List<int> fanTriangles = new List<int>();
        // What this freeze's crystal cut into and was cut around: what its look is carved by.
        readonly List<Collider> cutAround = new List<Collider>();
        Mesh carvedLook;
        // Counts carves started and melts, so a carved look that's ready after its crystal melted isn't shown.
        int carveCount;
        // The skin's own copy of the jelly material, so _Solid can change for this blob only.
        Material skinMaterial;
        float solidLook;
        float lastSwitchTime = float.NegativeInfinity;
        Coroutine lookTransition;
        readonly Collider[] nearby = new Collider[32];
        readonly RaycastHit[] passageHits = new RaycastHit[8];

        public bool IsSolid { get; private set; }
        /// <summary>The colliders that have the frozen shape: one, or the pieces it was cut into (none while liquid).</summary>
        public IReadOnlyList<Collider> SolidColliders => solidColliders;
        /// <summary>
        /// An even spread of points over the frozen shape (the first 42 skin points), in the blob's local space (only
        /// meaningful while solid).
        /// </summary>
        public Vector3[] HullPoints => hullPoints;
        /// <summary>Raised after every form change; true = now solid.</summary>
        public event Action<bool> FormChanged;
        /// <summary>
        /// Raised while freezing, once <see cref="SolidColliders"/> have the frozen shape and before the crystal is
        /// checked for overlaps. Whatever a listener makes it ignore (<see cref="IgnoreCollision"/>) it may set around,
        /// like a conveyor's spike it grabs (see <see cref="BlobGrip"/>).
        /// </summary>
        public event Action Hardening;

        /// <summary>One convex collider of the crystal.</summary>
        class Piece
        {
            public MeshCollider Collider;
            public Mesh Mesh;
            // The region it was cut from (planes facing out of it), the points of the shape inside it and their
            // bounds, in the blob's local space.
            public List<Plane> Region;
            public List<Vector3> Points;
            public Bounds Extent;
            // How far it cuts into the level, and what it cuts into deepest.
            public float Depth;
            public Collider Obstacle;
            public bool Uncuttable;
        }

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
            fitMask = skin.CollisionMask;

            hullPoints = new Vector3[HullPointCount];
            if (skinRenderer != null) skinMaterial = skinRenderer.material;
            // A copy of a blob (a half from a piercer pipe) comes with the colliders the original had added.
            GetComponents(crystalColliders);
            crystalColliders.Remove(solidCollider);
            crystalColliders.Insert(0, solidCollider);
            foreach (MeshCollider crystalCollider in crystalColliders) crystalCollider.enabled = false;
            ResetSpareColliders();
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
            foreach (Piece piece in pieces) Destroy(piece.Mesh);
            if (skinMaterial != null) Destroy(skinMaterial);
            if (carvedLook != null) Destroy(carvedLook);
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
            BuildCrystal(frozen);
            liquidCore.enabled = false;
            Hardening?.Invoke();
            ResolveOverlaps();
            if (carveLook) CarveLook();

            SetLayer(solidLayer);
            body.mass = solidMass;
            body.angularDamping = solidAngularDamping;
            body.constraints = RigidbodyConstraints.None;
            body.ResetCenterOfMass();
            body.ResetInertiaTensor();
            body.linearVelocity *= momentumKept;
            body.angularVelocity = Vector3.zero;
            motor.SetSolid(true, solidColliders);

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
            carveCount++;
            body.angularVelocity = Vector3.zero;
            body.rotation = Quaternion.identity;
            transform.rotation = Quaternion.identity;

            liquidCore.enabled = true;
            foreach (Piece piece in pieces) Release(piece);
            pieces.Clear();
            solidColliders.Clear();
            ResetSpareColliders();
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

        /// <summary>Makes every collider of the crystal ignore collisions with <paramref name="other"/>, or stop ignoring them.</summary>
        public void IgnoreCollision(Collider other, bool ignore)
        {
            foreach (Collider piece in solidColliders) Physics.IgnoreCollision(piece, other, ignore);
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
        /// Gives the crystal its colliders. Colliders on a moving body have to be convex, and the convex hull of the
        /// frozen shape fills in every dent the jelly took: wrapped around a post or draped over an edge, the hull
        /// stands in the post or the edge. So the hull is the whole crystal only as long as it doesn't cut into the
        /// level. Otherwise the piece that cuts in deepest is cut in two along one of the sides of what it cuts into
        /// (<see cref="Sides"/>): everything beyond a side is clear of it. Each piece is the part of the shape inside
        /// its region, with only the dents inside the region filled in. Cut along every side it reaches past, a piece
        /// is left with what's inside the obstacle's sides: the obstacle and slivers of jelly at most, where the jelly
        /// closed over a post. That's left out, so the crystal sets around the post, its pieces ringing it. This goes on
        /// until every piece fits or there are as many as allowed. Whatever still cuts in,
        /// <see cref="ResolveOverlaps"/> deals with.
        /// </summary>
        void BuildCrystal(Vector3[] frozen)
        {
            if (shape == null) shape = new FrozenShape(frozen.Length);
            shape.SetPoints(frozen);
            cutAround.Clear();
            lowerPoints.Clear();
            lowerPoints.AddRange(hullPoints);
            var whole = new List<Plane>(6);
            FrozenShape.AddBox(shape.Bounds, whole);
            AddPiece(lowerPoints, whole);

            // A piece left out adds none, so the cuts are counted too.
            for (int cuts = 0; pieces.Count < maxPieces && cuts < maxPieces * 2;)
            {
                Piece deepest = null;
                foreach (Piece piece in pieces)
                    if (!piece.Uncuttable && piece.Depth > fitTolerance && (deepest == null || piece.Depth > deepest.Depth))
                        deepest = piece;
                if (deepest == null) break;

                bool closeFit = Sides(deepest.Obstacle, obstacleSides);
                if (!FrozenShape.ChooseCut(deepest.Points, obstacleSides, minPieceSize, out Plane cut))
                {
                    // Nothing more to part off along the obstacle's sides. Where they fit it closely and the piece is
                    // inside them all, it's the obstacle and slivers of jelly at most; where they don't, it's left out
                    // only if it's mostly the obstacle.
                    bool inside = closeFit
                        ? FrozenShape.Overhang(deepest.Points, obstacleSides) <= minPieceSize
                        : IsMostlyObstacle(deepest.Points, deepest.Region, deepest.Obstacle);
                    if (inside)
                    {
                        cuts++;
                        pieces.Remove(deepest);
                        Release(deepest);
                        continue;
                    }
                    // At the moment of freezing the blob has no rotation, so local bounds are world bounds moved.
                    Bounds obstacle = deepest.Obstacle.bounds;
                    obstacle.center -= body.position;
                    if (!FrozenShape.MiddleCut(deepest.Extent, obstacle, minPieceSize, out cut))
                    {
                        deepest.Uncuttable = true;
                        continue;
                    }
                }

                var lower = new List<Plane>(deepest.Region) { cut };
                var upper = new List<Plane>(deepest.Region) { cut.flipped };
                shape.Clip(lower, lowerPoints);
                shape.Clip(upper, upperPoints);
                if (!CanBuild(lowerPoints, out _) || !CanBuild(upperPoints, out _))
                {
                    deepest.Uncuttable = true;
                    continue;
                }

                cuts++;
                pieces.Remove(deepest);
                Release(deepest);
                AddPiece(lowerPoints, lower);
                AddPiece(upperPoints, upper);
            }

            solidColliders.Clear();
            foreach (Piece piece in pieces) solidColliders.Add(piece.Collider);
        }

        /// <summary>
        /// The planes an obstacle's sides lie in (facing out of it), in the blob's local space at the moment of
        /// freezing: everything beyond any of them is clear of it. A box's six faces; around a capsule (a round post),
        /// as many as it takes for the gaps between them and it to stay within the fit tolerance, and its two ends.
        /// Anything else gets the sides of its bounds. True when they fit it closely.
        /// </summary>
        bool Sides(Collider obstacle, List<Plane> sides)
        {
            sides.Clear();
            Vector3 origin = body.position;
            Transform frame = obstacle.transform;
            switch (obstacle)
            {
                case BoxCollider box:
                {
                    Vector3 center = frame.TransformPoint(box.center);
                    for (int axis = 0; axis < 3; axis++)
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        Vector3 offset = Vector3.zero;
                        offset[axis] = sign * box.size[axis] * 0.5f;
                        Vector3 face = frame.TransformPoint(box.center + offset);
                        Vector3 normal = face - center;
                        if (normal.sqrMagnitude < 1e-8f) normal = frame.rotation * offset.normalized;
                        sides.Add(new Plane(normal.normalized, face - origin));
                    }
                    return true;
                }
                case CapsuleCollider capsule:
                {
                    Vector3 scale = frame.lossyScale;
                    int along = capsule.direction;
                    Vector3 axisLocal = Vector3.zero;
                    axisLocal[along] = 1f;
                    Vector3 axis = (frame.rotation * axisLocal).normalized;
                    float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale[(along + 1) % 3]), Mathf.Abs(scale[(along + 2) % 3]));
                    float half = Mathf.Max(capsule.height * Mathf.Abs(scale[along]) * 0.5f, radius);
                    Vector3 center = frame.TransformPoint(capsule.center) - origin;
                    sides.Add(new Plane(axis, center + axis * half));
                    sides.Add(new Plane(-axis, center - axis * half));

                    // Starting from world X, so a standing post gets the sides of its bounds first, like a box.
                    Vector3 across = Vector3.ProjectOnPlane(Vector3.right, axis);
                    if (across.sqrMagnitude < 1e-4f) across = Vector3.ProjectOnPlane(Vector3.forward, axis);
                    across.Normalize();
                    Vector3 other = Vector3.Cross(axis, across);
                    int count = SidesAround(radius);
                    for (int k = 0; k < count; k++)
                    {
                        float angle = k * 2f * Mathf.PI / count;
                        Vector3 normal = Mathf.Cos(angle) * across + Mathf.Sin(angle) * other;
                        sides.Add(new Plane(normal, center + normal * radius));
                    }
                    return true;
                }
                default:
                {
                    Bounds bounds = obstacle.bounds;
                    bounds.center -= origin;
                    FrozenShape.AddBox(bounds, sides);
                    return false;
                }
            }
        }

        /// <summary>
        /// How many sides around a round post of <paramref name="radius"/>: the fewest (a multiple of four, so the sides
        /// of its bounds are among them) that leave gaps between it and them no wider than the fit tolerance.
        /// </summary>
        int SidesAround(float radius)
        {
            for (int count = 4; count < MaxSidesAround; count += 4)
                if (radius * (1f / Mathf.Cos(Mathf.PI / count) - 1f) <= fitTolerance) return count;
            return MaxSidesAround;
        }

        /// <summary>
        /// Builds a convex collider around <paramref name="points"/> (the part of the shape in <paramref name="region"/>)
        /// and measures how far it cuts into the level.
        /// </summary>
        void AddPiece(List<Vector3> points, List<Plane> region)
        {
            if (!CanBuild(points, out Bounds extent)) return;

            // A convex collider is built from the points alone; the triangles only have to be valid.
            int indexCount = (points.Count - 2) * 3;
            for (int k = fanTriangles.Count / 3; fanTriangles.Count < indexCount; k++)
            {
                fanTriangles.Add(0);
                fanTriangles.Add(k + 1);
                fanTriangles.Add(k + 2);
            }
            // A new mesh every time, so each collider is built exactly as on the first freeze. Refilling a mesh a
            // collider had already been built from could leave the crystal without a working shape, and it fell
            // straight through the floor.
            var mesh = new Mesh { name = "Blob Crystal" };
            mesh.SetVertices(points);
            mesh.SetTriangles(fanTriangles, 0, indexCount, 0);

            MeshCollider meshCollider = spareColliders.Count > 0 ? spareColliders.Pop() : AddCrystalCollider();
            meshCollider.sharedMesh = mesh;
            meshCollider.enabled = true;
            var piece = new Piece
            {
                Collider = meshCollider, Mesh = mesh, Region = region, Points = new List<Vector3>(points), Extent = extent,
            };
            piece.Depth = MeasureDepth(piece, out piece.Obstacle);
            pieces.Add(piece);
            if (piece.Depth > fitTolerance && !cutAround.Contains(piece.Obstacle)) cutAround.Add(piece.Obstacle);
        }

        /// <summary>
        /// Carves what the crystal cut into out of its look. The skin is one closed surface, so where the jelly closed
        /// over a post the frozen look runs right through the post; with the post taken out, it stands in a hole through
        /// the ice. Each obstacle is cut out as the mesh it's drawn with, or as its box collider when it has no mesh.
        /// Only the look: the colliders are the convex pieces <see cref="BuildCrystal"/> made.
        /// </summary>
        void CarveLook()
        {
            if (cutAround.Count == 0 || skin.FrozenMesh == null) return;
            // Into the blob's space, where the skin froze. It has no rotation yet, but ResolveOverlaps may have moved it.
            Matrix4x4 toLocal = Matrix4x4.TRS(body.position, body.rotation, Vector3.one).inverse;
            var cutters = new List<MeshCsg>();
            foreach (Collider obstacle in cutAround)
            {
                MeshCsg cutter = Cutter(obstacle, toLocal);
                if (cutter != null) cutters.Add(cutter);
            }
            if (cutters.Count == 0) return;

            MeshCsg crystal = MeshCsg.FromMesh(skin.FrozenMesh, Matrix4x4.identity);
            Func<MeshCsg> carve = () =>
            {
                MeshCsg look = crystal;
                foreach (MeshCsg cutter in cutters) look = look.Subtract(cutter);
                // A cut can leave scraps of the skin floating beside the crystal: bits, and thin sheets.
                return look.WithoutLooseBits(LooseBitShare, LooseBitVolume);
            };
            // Carving a skin this fine takes longer than a frame. Off the main thread it doesn't hold the game up: the
            // crystal hardens with the plain look and the carved one replaces it when it's ready. WebGL has no threads.
            Task<MeshCsg> carving = Application.platform == RuntimePlatform.WebGLPlayer
                ? Task.FromResult(carve())
                : Task.Run(carve);
            StartCoroutine(ShowCarvedLook(carving, ++carveCount));
        }

        IEnumerator ShowCarvedLook(Task<MeshCsg> carving, int carve)
        {
            while (!carving.IsCompleted) yield return null;
            if (carving.IsFaulted)
            {
                Debug.LogWarning($"[BounJam] Carving the crystal's look failed; it keeps the plain one. {carving.Exception}");
                yield break;
            }
            // Melted, or frozen again, while it was carving: this look belongs to a crystal that's gone.
            if (carve != carveCount || !IsSolid) yield break;
            if (carvedLook == null) carvedLook = new Mesh { name = "Blob Crystal Look" };
            skin.ShowFrozenLook(carving.Result.ToMesh(carvedLook));
        }

        /// <summary>
        /// The solid to carve out for an obstacle, <see cref="carveMargin"/> bigger all round than it, in the space
        /// <paramref name="toLocal"/> leads into; null when it has no shape to give.
        /// </summary>
        MeshCsg Cutter(Collider obstacle, Matrix4x4 toLocal)
        {
            if (obstacle.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null && filter.sharedMesh.isReadable)
            {
                Mesh mesh = filter.sharedMesh;
                Bounds grown = Grown(mesh.bounds, filter.transform.lossyScale);
                // Stretched about its middle to the grown size: a post or a box gets the margin on every side.
                Matrix4x4 grow = Matrix4x4.Translate(grown.center)
                                 * Matrix4x4.Scale(new Vector3(Stretch(grown, mesh.bounds, 0), Stretch(grown, mesh.bounds, 1),
                                                               Stretch(grown, mesh.bounds, 2)))
                                 * Matrix4x4.Translate(-grown.center);
                return MeshCsg.FromMesh(mesh, toLocal * filter.transform.localToWorldMatrix * grow);
            }
            if (obstacle is BoxCollider box)
            {
                Bounds grown = Grown(new Bounds(box.center, box.size), box.transform.lossyScale);
                return MeshCsg.Box(grown.center, grown.size, toLocal * box.transform.localToWorldMatrix);
            }
            return null;
        }

        /// <summary>Bounds in an obstacle's own space, grown by <see cref="carveMargin"/> (a world length) on every side.</summary>
        Bounds Grown(Bounds bounds, Vector3 scale)
        {
            Vector3 size = bounds.size;
            for (int axis = 0; axis < 3; axis++)
                if (Mathf.Abs(scale[axis]) > 1e-6f) size[axis] += 2f * carveMargin / Mathf.Abs(scale[axis]);
            bounds.size = size;
            return bounds;
        }

        static float Stretch(Bounds grown, Bounds original, int axis)
            => original.size[axis] > 1e-6f ? grown.size[axis] / original.size[axis] : 1f;

        static bool CanBuild(List<Vector3> points, out Bounds extent)
        {
            extent = default;
            if (points.Count < 4) return false;
            extent = new Bounds(points[0], Vector3.zero);
            foreach (Vector3 point in points) extent.Encapsulate(point);
            Vector3 size = extent.size;
            return Mathf.Min(size.x, Mathf.Min(size.y, size.z)) >= MinThickness;
        }

        /// <summary>
        /// Is this part of the shape (its corners, and the region it was cut from, in the blob's local space) mostly
        /// the obstacle? For obstacles whose sides (<see cref="Sides"/>) don't fit them closely, like a convex mesh's
        /// bounds: cut along all of them, what's left can still hold plenty of jelly. Looks at a grid over the part: of
        /// the points inside the region and the shape, the share inside the obstacle. A part too thin for the grid is
        /// mostly the obstacle when all its corners are in it. Only convex colliders can say what's inside them.
        /// </summary>
        bool IsMostlyObstacle(List<Vector3> points, List<Plane> region, Collider obstacle)
        {
            if (!CanTellInside(obstacle)) return false;
            if (points.Count == 0) return true;
            GetPose(obstacle, out Vector3 position, out Quaternion rotation);

            var extent = new Bounds(points[0], Vector3.zero);
            foreach (Vector3 point in points) extent.Encapsulate(point);
            int inShape = 0, inObstacle = 0;
            for (int x = 0; x < ShareSamples; x++)
            for (int y = 0; y < ShareSamples; y++)
            for (int z = 0; z < ShareSamples; z++)
            {
                // The middles of the grid's cells, so no point lies on the part's sides.
                var cell = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) / ShareSamples;
                Vector3 point = extent.min + Vector3.Scale(cell, extent.size);
                if (!FrozenShape.Inside(point, region) || !shape.Contains(point)) continue;
                inShape++;
                // At the moment of freezing the blob has no rotation.
                if (Inside(body.position + point, obstacle, position, rotation)) inObstacle++;
            }
            if (inShape > 0) return inObstacle >= inShape * dropShare;

            foreach (Vector3 point in points)
                if (!Inside(body.position + point, obstacle, position, rotation)) return false;
            return true;
        }

        /// <summary>Only convex colliders can say what's inside them.</summary>
        static bool CanTellInside(Collider other)
            => other is BoxCollider || other is SphereCollider || other is CapsuleCollider || other is MeshCollider { convex: true };

        /// <summary>Is the world point inside the collider, which is at that pose? A point inside comes back as itself.</summary>
        static bool Inside(Vector3 point, Collider other, Vector3 position, Quaternion rotation)
            => (Physics.ClosestPoint(point, other, position, rotation) - point).sqrMagnitude <= InsideTolerance * InsideTolerance;

        /// <summary>How far the piece cuts into what the liquid flows around, and what it cuts into deepest.</summary>
        float MeasureDepth(Piece piece, out Collider obstacle)
        {
            obstacle = null;
            float deepest = 0f;
            Vector3 position = body.position;
            Quaternion rotation = body.rotation;
            int count = Physics.OverlapBoxNonAlloc(position + rotation * piece.Extent.center, piece.Extent.extents + Vector3.one * 0.05f,
                                                   nearby, rotation, fitMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider other = nearby[i];
                if (other.attachedRigidbody == body) continue;
                GetPose(other, out Vector3 otherPosition, out Quaternion otherRotation);
                if (!Physics.ComputePenetration(piece.Collider, position, rotation, other, otherPosition, otherRotation,
                                                out _, out float distance))
                    distance = SampledDepth(piece, other, otherPosition, otherRotation);
                if (distance > deepest)
                {
                    deepest = distance;
                    obstacle = other;
                }
            }
            return deepest;
        }

        /// <summary>
        /// How far the piece cuts into <paramref name="other"/>, for when the physics can't say: deep inside a round
        /// post (the jelly closed over it), <see cref="Physics.ComputePenetration"/> finds no way out and reports no
        /// overlap at all. Looks at a grid over where their bounds meet; a point inside both means they overlap, and
        /// the depth is taken as the thinnest side of where the bounds meet. 0 when no point is inside both.
        /// </summary>
        float SampledDepth(Piece piece, Collider other, Vector3 otherPosition, Quaternion otherRotation)
        {
            if (!CanTellInside(other)) return 0f;
            // At the moment of freezing the blob has no rotation, so local bounds are world bounds moved.
            Vector3 position = body.position;
            Bounds otherBounds = other.bounds;
            Vector3 min = Vector3.Max(position + piece.Extent.min, otherBounds.min);
            Vector3 size = Vector3.Min(position + piece.Extent.max, otherBounds.max) - min;
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f) return 0f;

            for (int x = 0; x < ShareSamples; x++)
            for (int y = 0; y < ShareSamples; y++)
            for (int z = 0; z < ShareSamples; z++)
            {
                var cell = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) / ShareSamples;
                Vector3 point = min + Vector3.Scale(cell, size);
                if (Inside(point, other, otherPosition, otherRotation) && Inside(point, piece.Collider, position, body.rotation))
                    return Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            }
            return 0f;
        }

        /// <summary>One more collider for the crystal, set up like the first.</summary>
        MeshCollider AddCrystalCollider()
        {
            var added = gameObject.AddComponent<MeshCollider>();
            added.enabled = false;
            added.convex = true;
            added.cookingOptions = solidCollider.cookingOptions;
            added.sharedMaterial = solidCollider.sharedMaterial;
            crystalColliders.Add(added);
            // The ground guard keeps a list of the body's colliders.
            if (TryGetComponent(out GroundGuard guard)) guard.RefreshColliders();
            return added;
        }

        void Release(Piece piece)
        {
            piece.Collider.enabled = false;
            piece.Collider.sharedMesh = null;
            Destroy(piece.Mesh);
            spareColliders.Push(piece.Collider);
        }

        /// <summary>All colliders spare, solidCollider on top: a crystal that isn't cut is built on it, as always.</summary>
        void ResetSpareColliders()
        {
            spareColliders.Clear();
            for (int i = crystalColliders.Count - 1; i >= 0; i--) spareColliders.Push(crystalColliders[i]);
        }

        /// <summary>
        /// Where the new crystal still cuts into something (a ship being scooped up, a dent too fine to cut around),
        /// the physics would shove the bodies apart hard enough to send the blob through the floor. Instead: step out
        /// of anything heavy or fixed, and move light bodies (the ship) out of the crystal. Two passes settle overlaps
        /// with several things at once.
        /// </summary>
        void ResolveOverlaps()
        {
            Bounds extent = shape.Bounds;
            for (int pass = 0; pass < 2; pass++)
            {
                Vector3 center = transform.TransformPoint(extent.center);
                int count = Physics.OverlapBoxNonAlloc(center, extent.extents + Vector3.one * 0.05f, nearby, transform.rotation,
                                                       overlapMask, QueryTriggerInteraction.Ignore);
                Vector3 step = Vector3.zero;
                for (int i = 0; i < count; i++)
                {
                    Collider other = nearby[i];
                    Rigidbody otherBody = other.attachedRigidbody;
                    if (otherBody == body) continue;
                    GetPose(other, out Vector3 otherPosition, out Quaternion otherRotation);
                    foreach (Piece piece in pieces)
                    {
                        if (Physics.GetIgnoreCollision(piece.Collider, other)
                            || !Physics.ComputePenetration(piece.Collider, body.position + step, body.rotation,
                                                           other, otherPosition, otherRotation,
                                                           out Vector3 direction, out float distance))
                            continue;

                        if (otherBody != null && !otherBody.isKinematic && otherBody.mass < body.mass * 0.5f)
                        {
                            // Light and loose: move it out of the crystal instead of moving the crystal.
                            otherBody.position -= direction * distance;
                            otherBody.transform.position -= direction * distance;
                            otherPosition -= direction * distance;
                        }
                        else step += direction * distance;
                    }
                }
                if (step == Vector3.zero) break;
                body.position += step;
                transform.position += step;
            }
        }

        /// <summary>Where the physics has a collider: moving bodies render interpolated.</summary>
        static void GetPose(Collider other, out Vector3 position, out Quaternion rotation)
        {
            Rigidbody otherBody = other.attachedRigidbody;
            bool onBody = otherBody != null && other.transform == otherBody.transform;
            position = onBody ? otherBody.position : other.transform.position;
            rotation = onBody ? otherBody.rotation : other.transform.rotation;
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
