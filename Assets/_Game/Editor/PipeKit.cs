using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static BounJam.EditorTools.LevelKit;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Level pieces for the piercer pipe: a Y-shaped pipe on the floor with a spinning blade in a housing where it forks,
    /// and the part the blob needs to be cut in two and flow back together. Materials are only created when missing.
    /// </summary>
    public static class PipeKit
    {
        /// <summary>Outer radius of the pipe, m; it lies on the floor, so it's twice this tall.</summary>
        public const float PipeRadius = 0.6f;
        static readonly Vector3 HousingSize = new Vector3(1.6f, 1.5f, 1.5f);
        const float BladeRadius = 0.55f;

        public struct Materials
        {
            public Material Pipe, Housing, Rim, ShipRim, Hole, Blade, Teeth;
        }

        public static Materials CreateMaterials() => new Materials
        {
            Pipe = GridMaterial("Greybox_Pipe", new Color(0.36f, 0.62f, 0.58f), new Color(0.28f, 0.50f, 0.47f), 0.5f),
            Housing = GridMaterial("Greybox_PipeHousing", new Color(0.40f, 0.42f, 0.47f), new Color(0.33f, 0.35f, 0.40f), 0.5f),
            Rim = LitMaterial("Pipe_Rim", new Color(0.25f, 0.27f, 0.30f), 0.55f, Color.black),
            // The exit the ship comes out of wears the paper's colour.
            ShipRim = LitMaterial("Pipe_Rim_Ship", new Color(0.93f, 0.91f, 0.84f), 0.3f, new Color(0.25f, 0.24f, 0.20f)),
            Hole = LoadOrCreate($"{MaterialsFolder}/Pipe_Hole.mat", () =>
            {
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor", new Color(0.03f, 0.03f, 0.04f));
                material.SetFloat("_Smoothness", 0.2f);
                // Emission stays switched on so the mouth can flash (per renderer) when something can't get in.
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                return material;
            }),
            Blade = LitMaterial("Pipe_Blade", new Color(0.80f, 0.82f, 0.86f), 0.85f, Color.black),
            Teeth = LitMaterial("Pipe_Blade_Teeth", new Color(0.85f, 0.20f, 0.15f), 0.5f, new Color(0.30f, 0.03f, 0.02f)),
        };

        // ---------------------------------------------------------------- Pieces

        /// <summary>
        /// A piercer pipe lying on the floor. <paramref name="mouth"/> is the floor point in the middle of the opening the
        /// blob flows into, <paramref name="forward"/> the way the pipe runs from there. The blade sits
        /// <paramref name="bladeDistance"/> m in, where the pipe forks into two branches that end at the exits (floor
        /// points too). The ship comes out of <paramref name="firstExit"/>.
        /// </summary>
        public static PiercerPipe PiercerPipe(string name, Transform parent, Vector3 mouth, Vector3 forward, float bladeDistance,
                                              Vector3 firstExit, Vector3 secondExit, Materials m)
        {
            forward = Flat(forward);
            Vector3 fork = mouth + forward * bladeDistance;
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = fork;
            Transform frame = root.transform;

            Segment("Pipe In", frame, mouth, fork, m.Pipe);
            Segment("Pipe Out 1", frame, fork, firstExit, m.Pipe);
            Segment("Pipe Out 2", frame, fork, secondExit, m.Pipe);
            Box("Blade Housing", frame, fork + Vector3.up * (HousingSize.y * 0.5f), HousingSize, m.Housing,
                euler: Quaternion.LookRotation(forward).eulerAngles);

            Transform mouthPoint = Opening("Mouth", frame, mouth, -forward, m.Rim, m, out Renderer hole);
            Transform exit1 = Opening("Exit 1 (Ship)", frame, firstExit, Flat(firstExit - fork), m.ShipRim, m, out _);
            Transform exit2 = Opening("Exit 2", frame, secondExit, Flat(secondExit - fork), m.Rim, m, out _);
            var bladePoint = new GameObject("Blade Point").transform;
            bladePoint.SetParent(frame, false);
            bladePoint.SetPositionAndRotation(fork, Quaternion.LookRotation(forward));

            // The saw sticks out of the housing's top, cutting along the pipe, so the blob comes apart left and right.
            Transform saw = Saw(frame, fork + Vector3.up * HousingSize.y, Vector3.Cross(Vector3.up, forward), m);

            var pipe = root.AddComponent<PiercerPipe>();
            var settings = new SerializedObject(pipe);
            settings.FindProperty("mouth").objectReferenceValue = mouthPoint;
            settings.FindProperty("blade").objectReferenceValue = bladePoint;
            settings.FindProperty("firstExit").objectReferenceValue = exit1;
            settings.FindProperty("secondExit").objectReferenceValue = exit2;
            settings.FindProperty("bladeModel").objectReferenceValue = saw;
            settings.FindProperty("mouthRenderer").objectReferenceValue = hole;
            settings.ApplyModifiedPropertiesWithoutUndo();
            return pipe;
        }

        /// <summary>A straight stretch of pipe between two floor points, lying on the floor. Solid from the outside.</summary>
        static void Segment(string name, Transform parent, Vector3 from, Vector3 to, Material material)
        {
            Vector3 lift = Vector3.up * PipeRadius;
            Vector3 along = to - from;
            Shape(PrimitiveType.Cylinder, name, parent, (from + to) * 0.5f + lift,
                  new Vector3(PipeRadius * 2f, along.magnitude * 0.5f, PipeRadius * 2f), material,
                  euler: Quaternion.FromToRotation(Vector3.up, along).eulerAngles);
        }

        /// <summary>
        /// The end of a pipe at the floor point <paramref name="floor"/>, opening toward <paramref name="outward"/>: a rim
        /// and a dark hole. Returns the opening's point (on the floor, facing out).
        /// </summary>
        static Transform Opening(string name, Transform parent, Vector3 floor, Vector3 outward, Material rim, Materials m,
                                 out Renderer hole)
        {
            var point = new GameObject(name).transform;
            point.SetParent(parent, false);
            point.SetPositionAndRotation(floor, Quaternion.LookRotation(outward));

            Quaternion across = Quaternion.FromToRotation(Vector3.up, outward);
            Vector3 center = floor + Vector3.up * PipeRadius;
            GameObject collar = Decoration(PrimitiveType.Cylinder, "Rim", point, Vector3.zero,
                                           new Vector3(PipeRadius * 2.24f, 0.08f, PipeRadius * 2.24f), rim);
            collar.transform.SetPositionAndRotation(center - outward * 0.08f, across);
            GameObject disc = Decoration(PrimitiveType.Cylinder, "Hole", point, Vector3.zero,
                                         new Vector3(PipeRadius * 1.64f, 0.005f, PipeRadius * 1.64f), m.Hole);
            disc.transform.SetPositionAndRotation(center + outward * 0.006f, across);
            hole = disc.GetComponent<MeshRenderer>();
            return point;
        }

        /// <summary>A round saw blade with red teeth, spinning about <paramref name="axis"/>. Returns its pivot.</summary>
        static Transform Saw(Transform parent, Vector3 center, Vector3 axis, Materials m)
        {
            var pivot = new GameObject("Saw").transform;
            pivot.SetParent(parent, false);
            pivot.SetPositionAndRotation(center, Quaternion.FromToRotation(Vector3.up, axis));

            Decoration(PrimitiveType.Cylinder, "Disc", pivot, Vector3.zero, new Vector3(BladeRadius * 2f, 0.03f, BladeRadius * 2f), m.Blade);
            Decoration(PrimitiveType.Cylinder, "Hub", pivot, Vector3.zero, new Vector3(0.22f, 0.05f, 0.22f), m.Rim);
            const int teeth = 10;
            for (int i = 0; i < teeth; i++)
            {
                float angle = i * 2f * Mathf.PI / teeth;
                GameObject tooth = Decoration(PrimitiveType.Cube, "Tooth", pivot,
                                              new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (BladeRadius + 0.02f),
                                              new Vector3(0.12f, 0.035f, 0.09f), m.Teeth);
                tooth.transform.localRotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg + 20f, 0f);
            }
            return pivot;
        }

        static Vector3 Flat(Vector3 direction) => Vector3.ProjectOnPlane(direction, Vector3.up).normalized;

        /// <summary>A shape that is only seen: no collider, no shadow.</summary>
        static GameObject Decoration(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        // ---------------------------------------------------------------- Sandbox

        /// <summary>
        /// The sandbox's piercer pipe, in the free north-west corner. Its mouth faces east, next to the opening between
        /// the narrow gap and the low tunnel; the ship comes out of the north branch. To join the halves again, push them
        /// west against the wall, then north into the corner.
        /// </summary>
        public static void SandboxPieces(Transform level)
        {
            Materials m = CreateMaterials();
            PiercerPipe("Piercer Pipe", level, new Vector3(-4.5f, 0f, 13f), Vector3.left, 3.5f,
                        new Vector3(-12f, 0f, 16f), new Vector3(-12f, 0f, 11.5f), m);
        }

        // ---------------------------------------------------------------- Blob

        /// <summary>Adds the part that lets a pipe cut the blob in two to a blob root that doesn't have it yet. True if it changed.</summary>
        public static bool AddBlobParts(GameObject blob)
        {
            if (blob.TryGetComponent(out BlobSplit _)) return false;
            blob.AddComponent<BlobSplit>();
            return true;
        }

        /// <summary>Adds that part to the saved Blob prefab, leaving all its tuning as it is.</summary>
        [MenuItem("BounJam/Add Splitting To Blob Prefab")]
        public static void UpgradeBlobPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(BlobPrefabPath) == null) return;
            GameObject root = PrefabUtility.LoadPrefabContents(BlobPrefabPath);
            if (AddBlobParts(root))
            {
                PrefabUtility.SaveAsPrefabAsset(root, BlobPrefabPath);
                Debug.Log("[BounJam] Blob prefab: added splitting.");
            }
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
