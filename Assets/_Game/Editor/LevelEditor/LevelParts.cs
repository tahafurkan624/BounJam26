using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static BounJam.EditorTools.LevelKit;

namespace BounJam.EditorTools
{
    /// <summary>
    /// The prefabs levels are made of, so a mesh or material changed in one prefab changes in every level. Each fixed
    /// piece (button, gate, crate, magnet, pickups, tiles, the dock) is a prefab in Assets/_Game/Prefabs/Level, and the
    /// shaped pieces are put together from the part prefabs in its Parts folder (platform blocks, wall segments, ramps,
    /// pool walls, hot plates, grate panels and the piercer pipe's parts), scaled or tiled to the piece's size. A prefab
    /// is made from the level kits the first time it's needed and never rebuilt after, so edits made to it stay.
    /// </summary>
    public static class LevelParts
    {
        public const string Folder = "Assets/_Game/Prefabs/Level";
        public const string PartsFolder = Folder + "/Parts";

        const float PlatformBlockSize = 2f;
        const float RampThickness = 0.4f;
        // Walls lower than this wear the low SM_HalfWall (0.7 m), taller ones SM_Wall (1.5 m).
        const float LowWallHeight = 1.1f;
        static readonly Vector3 HousingSize = new Vector3(1.6f, 1.5f, 1.5f);
        const float BladeRadius = 0.55f;

        // ---------------------------------------------------------------- Prefabs

        /// <summary>
        /// The prefab at <paramref name="path"/>; if it's missing, <paramref name="make"/> builds the one object to save
        /// as it under the given parent, standing on the origin.
        /// </summary>
        public static GameObject Prefab(string path, System.Action<Transform> make)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) return prefab;

            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            var space = new GameObject("Prefab Space").transform;
            try
            {
                make(space);
                Transform made = space.GetChild(0);
                made.SetParent(null, true);
                prefab = PrefabUtility.SaveAsPrefabAsset(made.gameObject, path);
                Object.DestroyImmediate(made.gameObject);
            }
            finally
            {
                Object.DestroyImmediate(space.gameObject);
            }
            Debug.Log($"[BounJam] Made the prefab {path}.");
            return prefab;
        }

        /// <summary>Makes every level prefab that's missing; the ones already there are left as they are.</summary>
        [MenuItem("BounJam/Make Missing Level Prefabs")]
        public static void MakeAll()
        {
            GameObject[] parts =
            {
                Dock, PlatformBlock, WallPart, HalfWallPart, RampPart, PoolWallPart, HotPlatePart, GratePanel,
                PipeSegment, PipeHousing, PipeOpening, PipeShipOpening, PipeSaw,
            };
            foreach (Placeable piece in Placeable.All)
                if (piece.Prefab != null && piece.Name != "Tile") piece.Prefab();
            foreach (string look in Placeable.TileLooks) Placeable.TilePrefab(look);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BounJam] Level prefabs are in {Folder} ({parts.Length} parts in {PartsFolder}).");
        }

        public static GameObject Instance(GameObject prefab, Transform parent, string name)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            return go;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        /// <summary>The goal, with no ship set: the level points it at its ship once it's placed.</summary>
        public static GameObject Dock => Prefab($"{Folder}/Dock.prefab", t => LevelKit.Dock("Dock", t, Vector3.zero, CreatePalette(), null));

        static GameObject Part(string name, System.Action<Transform> make) => Prefab($"{PartsFolder}/{name}.prefab", make);

        /// <summary>One 2 m platform block, its pivot at the top centre; only seen, the platform holds the collider.</summary>
        static GameObject PlatformBlock => Part("Platform Block", t =>
        {
            GameObject platform = ArtKit.Platform("Platform Block", t, new Vector3(0f, -PlatformBlockSize * 0.5f, 0f),
                                                  Vector3.one * PlatformBlockSize, CreatePalette().Floor);
            if (platform.transform.childCount == 0) return; // no model: the greybox stands in
            Transform block = platform.transform.GetChild(0);
            block.SetParent(t, true);
            block.name = "Platform Block";
            Object.DestroyImmediate(platform);
        });

        /// <summary>One SM_Wall segment, 2 m long and 1.5 m tall, standing on its pivot along X; only seen, the wall holds the collider.</summary>
        static GameObject WallPart => Part("Wall", t => ArtKit.WallSegment("Wall", t, false, CreatePalette().Wall));
        /// <summary>The same in the low SM_HalfWall, 0.7 m tall.</summary>
        static GameObject HalfWallPart => Part("Half Wall", t => ArtKit.WallSegment("Half Wall", t, true, CreatePalette().Wall));

        // Unit cubes with a collider, scaled to size.
        static GameObject RampPart => Part("Ramp", t => Box("Ramp", t, Vector3.zero, Vector3.one, CreatePalette().Structure));
        static GameObject PoolWallPart => Part("Pool Wall", t => Box("Pool Wall", t, Vector3.zero, Vector3.one, CreatePalette().Structure));

        /// <summary>A 1 x 1 m hot plate with its heat above it, scaled across the floor.</summary>
        static GameObject HotPlatePart => Part("Hot Plate", t => LevelKit.HotPlate("Hot Plate", t, Vector3.zero, Vector2.one, CreatePalette()));

        /// <summary>One SM_Grate panel at the model's own size, centred on its pivot.</summary>
        static GameObject GratePanel => Part("Grate Panel", t =>
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ArtKit.ModelsFolder + "/SM_Grate.fbx");
            Vector2 size = new Vector2(2f, 1.5f);
            if (model != null)
                foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh != null && (filter.sharedMesh.name == "SM_Grate" || filter.name == "SM_Grate"))
                        size = new Vector2(filter.sharedMesh.bounds.size.x, filter.sharedMesh.bounds.size.y);
            ArtKit.Grate("Grate Panel", t, Vector3.zero, size, CreatePalette().Grate);
        });

        // A Unity cylinder (1 m across, 2 m long along Y) and a unit cube, scaled to fit.
        static GameObject PipeSegment => Part("Pipe Segment", t =>
            Shape(PrimitiveType.Cylinder, "Pipe Segment", t, Vector3.zero, Vector3.one, PipeKit.CreateMaterials().Pipe));
        static GameObject PipeHousing => Part("Pipe Blade Housing", t =>
            Box("Pipe Blade Housing", t, Vector3.zero, Vector3.one, PipeKit.CreateMaterials().Housing));
        static GameObject PipeOpening => Part("Pipe Opening", t => OpeningPart("Pipe Opening", t, PipeKit.CreateMaterials().Rim));
        static GameObject PipeShipOpening => Part("Pipe Opening Ship", t => OpeningPart("Pipe Opening Ship", t, PipeKit.CreateMaterials().ShipRim));
        static GameObject PipeSaw => Part("Pipe Saw", SawPart);

        /// <summary>The end of a pipe lying on the floor at the origin, opening toward +Z: a rim and a dark hole called Hole.</summary>
        static void OpeningPart(string name, Transform parent, Material rim)
        {
            var point = new GameObject(name).transform;
            point.SetParent(parent, false);
            float r = PipeKit.PipeRadius;
            Quaternion across = Quaternion.FromToRotation(Vector3.up, Vector3.forward);
            Decoration(PrimitiveType.Cylinder, "Rim", point, new Vector3(0f, r, -0.08f), across, new Vector3(r * 2.24f, 0.08f, r * 2.24f), rim);
            Decoration(PrimitiveType.Cylinder, "Hole", point, new Vector3(0f, r, 0.006f), across, new Vector3(r * 1.64f, 0.005f, r * 1.64f),
                       PipeKit.CreateMaterials().Hole);
        }

        /// <summary>A round saw blade with red teeth that spins about its Y axis.</summary>
        static void SawPart(Transform parent)
        {
            PipeKit.Materials m = PipeKit.CreateMaterials();
            var pivot = new GameObject("Pipe Saw").transform;
            pivot.SetParent(parent, false);
            Decoration(PrimitiveType.Cylinder, "Disc", pivot, Vector3.zero, Quaternion.identity, new Vector3(BladeRadius * 2f, 0.03f, BladeRadius * 2f), m.Blade);
            Decoration(PrimitiveType.Cylinder, "Hub", pivot, Vector3.zero, Quaternion.identity, new Vector3(0.22f, 0.05f, 0.22f), m.Rim);
            const int teeth = 10;
            for (int i = 0; i < teeth; i++)
            {
                float angle = i * 2f * Mathf.PI / teeth;
                Decoration(PrimitiveType.Cube, "Tooth", pivot, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (BladeRadius + 0.02f),
                           Quaternion.Euler(0f, -angle * Mathf.Rad2Deg + 20f, 0f), new Vector3(0.12f, 0.035f, 0.09f), m.Teeth);
            }
        }

        /// <summary>A shape that is only seen: no collider, no shadow.</summary>
        static void Decoration(PrimitiveType type, string name, Transform parent, Vector3 position, Quaternion rotation, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(position, rotation);
            go.transform.localScale = scale;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ---------------------------------------------------------------- Shaped pieces from parts
        // Each is built under a parent at the origin, so positions there are the piece's own.

        /// <summary>
        /// A solid box at <paramref name="center"/> of <paramref name="size"/> covered in platform blocks, as
        /// ArtKit.Platform lays them: 2 m on a side, stretched a little to fit, tops level with the box top.
        /// </summary>
        public static GameObject Platform(string name, Transform parent, Vector3 center, Vector3 size)
        {
            var root = new GameObject(name) { layer = LayerMask.NameToLayer("Environment") };
            root.transform.SetParent(parent, false);
            root.transform.localPosition = center;
            root.AddComponent<BoxCollider>().size = size;

            GameObject block = PlatformBlock;
            int countX = Mathf.Max(1, Mathf.RoundToInt(size.x / PlatformBlockSize));
            int countY = Mathf.Max(1, Mathf.RoundToInt(size.y / PlatformBlockSize));
            int countZ = Mathf.Max(1, Mathf.RoundToInt(size.z / PlatformBlockSize));
            var step = new Vector3(size.x / countX, Mathf.Max(PlatformBlockSize, size.y / countY), size.z / countZ);
            var corner = new Vector3(-size.x * 0.5f, size.y * 0.5f, -size.z * 0.5f);
            for (int y = 0; y < countY; y++)
            for (int x = 0; x < countX; x++)
            for (int z = 0; z < countZ; z++)
            {
                Transform tile = Instance(block, root.transform, $"Block {x},{y},{z}").transform;
                tile.localPosition = corner + new Vector3((x + 0.5f) * step.x, -y * step.y, (z + 0.5f) * step.z);
                tile.localScale = step / PlatformBlockSize;
            }
            return root;
        }

        /// <summary>
        /// A wall standing on the origin along its longer side, X or Z: one box collider, covered in wall segments side
        /// by side, about 2 m each and stretched to fit. Walls lower than <see cref="LowWallHeight"/> are low walls.
        /// </summary>
        public static void Wall(Transform parent, Vector3 size)
        {
            var root = new GameObject("Wall") { layer = LayerMask.NameToLayer("Environment") };
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
            root.AddComponent<BoxCollider>().size = size;

            bool low = size.y < LowWallHeight, alongZ = size.z > size.x;
            GameObject segment = low ? HalfWallPart : WallPart;
            Vector3 natural = ArtKit.WallSize(low);
            float length = alongZ ? size.z : size.x, thickness = alongZ ? size.x : size.z;
            Vector3 along = alongZ ? Vector3.forward : Vector3.right;
            Quaternion turn = alongZ ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
            int count = Mathf.Max(1, Mathf.RoundToInt(length / natural.x));
            for (int i = 0; i < count; i++)
            {
                Transform part = Instance(segment, root.transform, $"Segment {i}").transform;
                part.SetLocalPositionAndRotation(along * ((i + 0.5f) * length / count - length * 0.5f) + Vector3.down * (size.y * 0.5f), turn);
                part.localScale = new Vector3(length / count / natural.x, size.y / natural.y, thickness / natural.z);
            }
        }

        /// <summary>A ramp whose footprint is centred on the origin, rising <c>size.y</c> toward +Z over <c>size.z</c>.</summary>
        public static void Ramp(Transform parent, Vector3 size)
        {
            float rise = Mathf.Max(0.05f, size.y), run = Mathf.Max(0.1f, size.z);
            float angle = Mathf.Atan2(rise, run);
            Transform ramp = Instance(RampPart, parent, "Ramp").transform;
            // As LevelKit.Ramp: the top surface runs from the bottom edge to the top edge, the slab below it.
            ramp.localPosition = new Vector3(0f, rise * 0.5f - RampThickness * 0.5f / Mathf.Cos(angle), 0f);
            ramp.localRotation = Quaternion.Euler(-angle * Mathf.Rad2Deg, 0f, 0f);
            ramp.localScale = new Vector3(size.x, RampThickness, rise / Mathf.Sin(angle));
        }

        /// <summary>A hot plate on the floor at the origin, <c>size.x</c> by <c>size.z</c>.</summary>
        public static void HotPlate(Transform parent, Vector3 size)
        {
            Transform plate = Instance(HotPlatePart, parent, "Hot Plate").transform;
            plate.localScale = new Vector3(size.x, 1f, size.z);
        }

        /// <summary>A grate standing on the origin across Z: panels side by side, about the model's width each.</summary>
        public static void Grate(Transform parent, Vector2 size)
        {
            var root = new GameObject("Grate") { layer = LayerMask.NameToLayer("LiquidPassable") };
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);

            GameObject panel = GratePanel;
            MeshFilter filter = panel.GetComponentInChildren<MeshFilter>();
            Vector3 natural = filter != null ? Vector3.Scale(filter.sharedMesh.bounds.size, filter.transform.localScale) : new Vector3(2f, 1.5f, 0.3f);
            int count = Mathf.Max(1, Mathf.RoundToInt(size.x / natural.x));
            for (int i = 0; i < count; i++)
            {
                Transform part = Instance(panel, root.transform, $"Panel {i}").transform;
                part.localPosition = new Vector3((i + 0.5f) * size.x / count - size.x * 0.5f, 0f, 0f);
                part.localScale = new Vector3(size.x / count / natural.x, size.y / natural.y, 1f);
            }
        }

        /// <summary>
        /// A floor with its top at the origin's height (<paramref name="floor"/> its footprint) and a pool sunk into it
        /// (<paramref name="pool"/>, inside the floor), <paramref name="depth"/> deep, filled to <paramref name="waterBelow"/>
        /// under the floor top. As LevelKit.FloorWithPool.
        /// </summary>
        public static void Pool(Transform parent, Rect floor, Rect pool, float depth, float waterBelow)
        {
            Transform root = Group("Pool", parent);
            FloorSlab("Floor South", root, floor.xMin, floor.xMax, floor.yMin, pool.yMin);
            FloorSlab("Floor North", root, floor.xMin, floor.xMax, pool.yMax, floor.yMax);
            FloorSlab("Floor West", root, floor.xMin, pool.xMin, pool.yMin, pool.yMax);
            FloorSlab("Floor East", root, pool.xMax, floor.xMax, pool.yMin, pool.yMax);

            float bottom = -depth;
            PoolWall("Pool Bottom", root, pool.xMin, pool.xMax, pool.yMin, pool.yMax, bottom, 1f);
            if (depth > 1f)
            {
                const float wall = 0.5f;
                float wallTop = -1f, wallHeight = depth - 1f;
                PoolWall("Pool Wall South", root, pool.xMin - wall, pool.xMax + wall, pool.yMin - wall, pool.yMin, wallTop, wallHeight);
                PoolWall("Pool Wall North", root, pool.xMin - wall, pool.xMax + wall, pool.yMax, pool.yMax + wall, wallTop, wallHeight);
                PoolWall("Pool Wall West", root, pool.xMin - wall, pool.xMin, pool.yMin, pool.yMax, wallTop, wallHeight);
                PoolWall("Pool Wall East", root, pool.xMax, pool.xMax + wall, pool.yMin, pool.yMax, wallTop, wallHeight);
            }

            float surface = -waterBelow;
            WaterVolume("Water", root, new Vector3(pool.center.x, (bottom + surface) * 0.5f, pool.center.y),
                        new Vector3(pool.width, surface - bottom, pool.height), CreatePalette());
        }

        static void FloorSlab(string name, Transform parent, float x0, float x1, float z0, float z1)
        {
            if (x1 - x0 < 0.001f || z1 - z0 < 0.001f) return;
            Platform(name, parent, new Vector3((x0 + x1) * 0.5f, -0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, 1f, z1 - z0));
        }

        static void PoolWall(string name, Transform parent, float x0, float x1, float z0, float z1, float top, float thickness)
        {
            if (x1 - x0 < 0.001f || z1 - z0 < 0.001f) return;
            Transform wall = Instance(PoolWallPart, parent, name).transform;
            wall.localPosition = new Vector3((x0 + x1) * 0.5f, top - thickness * 0.5f, (z0 + z1) * 0.5f);
            wall.localScale = new Vector3(x1 - x0, thickness, z1 - z0);
        }

        /// <summary>
        /// A piercer pipe as PipeKit.PiercerPipe makes it, from parts: the mouth on the floor at the origin, the pipe
        /// running along +Z to the blade <paramref name="bladeDistance"/> in, where it forks to the two exits.
        /// </summary>
        public static PiercerPipe Pipe(Transform parent, float bladeDistance, Vector3 firstExit, Vector3 secondExit)
        {
            Vector3 forward = Vector3.forward;
            Vector3 fork = forward * bladeDistance;
            var root = new GameObject("Piercer Pipe").transform;
            root.SetParent(parent, false);
            root.localPosition = fork;

            Segment("Pipe In", root, Vector3.zero, fork);
            Segment("Pipe Out 1", root, fork, firstExit);
            Segment("Pipe Out 2", root, fork, secondExit);
            Transform housing = Instance(PipeHousing, root, "Blade Housing").transform;
            housing.SetPositionAndRotation(fork + Vector3.up * (HousingSize.y * 0.5f), Quaternion.LookRotation(forward));
            housing.localScale = HousingSize;

            Transform mouth = Opening("Mouth", root, PipeOpening, Vector3.zero, -forward);
            Transform exit1 = Opening("Exit 1 (Ship)", root, PipeShipOpening, firstExit, Flat(firstExit - fork, forward));
            Transform exit2 = Opening("Exit 2", root, PipeOpening, secondExit, Flat(secondExit - fork, forward));
            var blade = new GameObject("Blade Point").transform;
            blade.SetParent(root, false);
            blade.SetPositionAndRotation(fork, Quaternion.LookRotation(forward));
            // The saw sticks out of the housing's top, cutting along the pipe, so the blob comes apart left and right.
            Transform saw = Instance(PipeSaw, root, "Saw").transform;
            saw.SetPositionAndRotation(fork + Vector3.up * HousingSize.y, Quaternion.FromToRotation(Vector3.up, Vector3.Cross(Vector3.up, forward)));

            var pipe = root.gameObject.AddComponent<PiercerPipe>();
            var settings = new SerializedObject(pipe);
            settings.FindProperty("mouth").objectReferenceValue = mouth;
            settings.FindProperty("blade").objectReferenceValue = blade;
            settings.FindProperty("firstExit").objectReferenceValue = exit1;
            settings.FindProperty("secondExit").objectReferenceValue = exit2;
            settings.FindProperty("bladeModel").objectReferenceValue = saw;
            settings.FindProperty("mouthRenderer").objectReferenceValue = mouth.Find("Hole")?.GetComponent<Renderer>();
            settings.ApplyModifiedPropertiesWithoutUndo();
            return pipe;
        }

        /// <summary>A straight stretch of pipe between two floor points, lying on the floor.</summary>
        static void Segment(string name, Transform parent, Vector3 from, Vector3 to)
        {
            Vector3 along = to - from;
            if (along.sqrMagnitude < 1e-4f) return;
            Transform segment = Instance(PipeSegment, parent, name).transform;
            float diameter = PipeKit.PipeRadius * 2f;
            segment.SetPositionAndRotation((from + to) * 0.5f + Vector3.up * PipeKit.PipeRadius, Quaternion.FromToRotation(Vector3.up, along));
            segment.localScale = new Vector3(diameter, along.magnitude * 0.5f, diameter);
        }

        /// <summary>An opening on the floor at <paramref name="floor"/>, facing <paramref name="outward"/>. Returns its point.</summary>
        static Transform Opening(string name, Transform parent, GameObject part, Vector3 floor, Vector3 outward)
        {
            Transform point = Instance(part, parent, name).transform;
            point.SetPositionAndRotation(floor, Quaternion.LookRotation(outward));
            return point;
        }

        static Vector3 Flat(Vector3 direction, Vector3 fallback)
        {
            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            return flat.sqrMagnitude < 1e-6f ? fallback : flat.normalized;
        }
    }
}
