using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static BounJam.EditorTools.LevelKit;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Level pieces for planting: the seed (SM_Seed), the soil bed (SM_Tile_Soil) and the flower lift that grows on it
    /// (SK_LeafLift), their prefabs, and the concrete tiles (SM_Tile_*) the sandbox builds a ledge from. Tiles are 2 m
    /// square and 3 m deep with their pivot at the top centre, and the open flower's leaf is level with the top of a
    /// tile one step (3 m) up. Materials and prefabs are only created when missing, so tweaks made in the editor survive
    /// a rebuild.
    /// </summary>
    public static class GardenKit
    {
        public const string SeedPrefabPath = "Assets/_Game/Prefabs/Seed.prefab";
        public const string SoilBedPrefabPath = "Assets/_Game/Prefabs/SoilBed.prefab";
        const string SeedModelPath = ArtKit.ModelsFolder + "/SM_Seed.fbx";
        const string FlowerModelPath = ArtKit.ModelsFolder + "/SK_LeafLift.fbx";
        const string SeedTexturePrefix = "Assets/_Project/Art/Textures/Props/T_Seed_";

        public const float SeedRadius = 0.12f;
        public const float TileSize = 2f, TileDepth = 3f;
        // A soil bed sits this much proud of a floor level with its top, so its rim doesn't flicker against it.
        const float SoilLift = 0.01f;
        const float StemRadius = 0.1f;

        struct Materials
        {
            public Material Seed, Marker, Leaf, Stem, PetalOuter, PetalInner, Stamen, StamenStalk;
        }

        // Colours are Blender's linear values; Material.SetColor takes sRGB, hence .gamma.
        static Materials CreateMaterials() => new Materials
        {
            Seed = LoadOrCreate($"{MaterialsFolder}/Seed.mat", () =>
            {
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor", Color.white);
                material.SetTexture("_BaseMap", SeedTexture("Albedo", TextureImporterType.Default, true));
                material.SetTexture("_BumpMap", SeedTexture("Normal", TextureImporterType.NormalMap, false));
                material.EnableKeyword("_NORMALMAP");
                // Smoothness from the map's alpha, scaled by _Smoothness.
                material.SetTexture("_MetallicGlossMap", SeedTexture("MetallicSmoothness", TextureImporterType.Default, false));
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.SetFloat("_Smoothness", 1f);
                // Emission stays switched on so the seed can glow (per renderer) while it's inside the jelly.
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                return material;
            }),
            Marker = MagnetKit.CreateMaterials().Marker,
            Leaf = Lit("Flower_Leaf", new Color(0.072f, 0.552f, 0.144f), 0.58f),
            Stem = Lit("Flower_Stem", new Color(0.05f, 0.342f, 0.095f), 0.5f),
            PetalOuter = Lit("Flower_PetalOuter", new Color(1f, 1f, 1f), 0.62f),
            PetalInner = Lit("Flower_PetalInner", new Color(1f, 1f, 1f), 0.62f),
            Stamen = Lit("Flower_Stamen", new Color(1f, 0.644f, 0.05f), 0.65f),
            StamenStalk = Lit("Flower_StamenStalk", new Color(1f, 0.888f, 0.451f), 0.5f),
        };

        // ---------------------------------------------------------------- Pieces

        /// <summary>A seed resting on the floor at <paramref name="floorPosition"/>, on a glowing marker.</summary>
        public static Collectible Seed(string name, Transform parent, Vector3 floorPosition)
        {
            GameObject go = Place(SeedPrefabPath, BuildSeedPrefab, name, parent);
            go.transform.position = floorPosition + Vector3.up * SeedRadius;
            return go.GetComponent<Collectible>();
        }

        /// <summary>
        /// A soil bed whose top centre is at <paramref name="topCenter"/> (level with the floor around it), its flower's
        /// petal ramps unfolding toward <paramref name="rampSide"/>: put the upper tile on that side.
        /// </summary>
        public static Soil SoilBed(string name, Transform parent, Vector3 topCenter, Vector3 rampSide, bool startGrown = false)
        {
            GameObject go = Place(SoilBedPrefabPath, BuildSoilBedPrefab, name, parent);
            go.transform.SetPositionAndRotation(topCenter + Vector3.up * SoilLift, Quaternion.LookRotation(rampSide));
            var soil = go.GetComponent<Soil>();
            if (startGrown)
            {
                var settings = new SerializedObject(soil);
                settings.FindProperty("startGrown").boolValue = true;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            return soil;
        }

        /// <summary>A solid concrete tile (SM_Tile_&lt;<paramref name="variant"/>&gt;) whose top centre is at <paramref name="topCenter"/>.</summary>
        public static GameObject Tile(string name, Transform parent, string variant, Vector3 topCenter)
        {
            var root = new GameObject(name) { layer = LayerMask.NameToLayer("Environment") };
            root.transform.SetParent(parent, false);
            root.transform.position = topCenter;
            TileModel("Model", root.transform, variant);
            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(TileSize, TileDepth, TileSize);
            box.center = new Vector3(0f, -TileDepth * 0.5f, 0f);
            return root;
        }

        static GameObject Place(string path, System.Func<GameObject> build, string name, Transform parent)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) prefab = build();
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            return go;
        }

        // ---------------------------------------------------------------- Prefabs

        /// <summary>Saves the Seed and SoilBed prefabs from the current models, replacing the old ones.</summary>
        [MenuItem("BounJam/Rebuild Garden Prefabs")]
        public static void BuildPrefabs()
        {
            BuildSeedPrefab();
            BuildSoilBedPrefab();
        }

        /// <summary>
        /// The seed: a collectible whose centre sits <see cref="SeedRadius"/> above the floor, the model lying flat
        /// under it, a glowing floor marker and the <see cref="BounJam.Seed"/> part that plants it.
        /// </summary>
        public static GameObject BuildSeedPrefab()
        {
            Materials m = CreateMaterials();
            var root = new GameObject("Seed");
            GameObject model = Copy("Model", root.transform, Part(SeedModelPath, "SM_Seed"), m.Seed);
            var renderer = model.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.On;
            // Centred on the item, its underside where the item's radius reaches down to the floor.
            Bounds bounds = renderer.bounds;
            model.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y + SeedRadius, bounds.center.z);

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "Marker";
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.SetParent(root.transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.005f - SeedRadius, 0f);
            marker.transform.localScale = new Vector3(0.6f, 0.004f, 0.6f);
            var markerRenderer = marker.GetComponent<MeshRenderer>();
            markerRenderer.sharedMaterial = m.Marker;
            markerRenderer.shadowCastingMode = ShadowCastingMode.Off;

            var collectible = root.AddComponent<Collectible>();
            var settings = new SerializedObject(collectible);
            settings.FindProperty("radius").floatValue = SeedRadius;
            settings.FindProperty("modelRenderer").objectReferenceValue = renderer;
            settings.FindProperty("marker").objectReferenceValue = marker.transform;
            settings.FindProperty("groundMask").intValue = LayerMask.GetMask("Default", "Environment", "Pushable");
            settings.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<BounJam.Seed>();
            return Save(root, SeedPrefabPath);
        }

        /// <summary>
        /// The soil bed: the soil tile with its collider, and under it the flower lift, switched off until a seed is
        /// planted. The flower is turned so its petal ramps unfold toward the bed's forward axis. Kinematic bodies follow
        /// the model's collider nodes (leaf, ramps) and a capsule follows the stem.
        /// </summary>
        public static GameObject BuildSoilBedPrefab()
        {
            Materials m = CreateMaterials();
            int environment = LayerMask.NameToLayer("Environment");
            var root = new GameObject("Soil Bed") { layer = environment };
            GameObject tile = TileModel("Model", root.transform, "Soil");
            // The mound's exact shape, so a dropped seed lands on the soil you see.
            tile.AddComponent<MeshCollider>().sharedMesh = tile.GetComponent<MeshFilter>().sharedMesh;

            var flowerRoot = new GameObject("Flower Lift") { layer = environment };
            flowerRoot.transform.SetParent(root.transform, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FlowerModelPath),
                                                                    flowerRoot.transform);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model";
            foreach (Transform child in model.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = environment;
            foreach (SkinnedMeshRenderer skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                skin.sharedMaterials = skin.sharedMaterials.Select(material => FlowerMaterial(material, m)).ToArray();

            // The clips play through an Animator; the model is imported without one.
            if (!model.TryGetComponent(out Animator animator)) animator = model.AddComponent<Animator>();

            // Open at the top, ramps out: turn the flower a quarter turn at a time, keeping it square to the tiles,
            // until they point forward (they sit a little off to one side).
            AnimationClip idleTop = Clip("LeafLift_IdleTop");
            idleTop.SampleAnimation(model, 0f);
            Transform platformNode = Find(model.transform, "SM_LeafLift_PlatformCol");
            Transform[] rampNodes = { Find(model.transform, "SM_LeafLift_RampCol_00"), Find(model.transform, "SM_LeafLift_RampCol_01") };
            Vector3 rampSide = rampNodes.Aggregate(Vector3.zero, (sum, ramp) => sum + ramp.GetComponent<Renderer>().bounds.center)
                               / rampNodes.Length - flowerRoot.transform.position;
            float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(rampSide, Vector3.up), Vector3.forward, Vector3.up);
            yaw = Mathf.Round(yaw / 90f) * 90f;
            model.transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * model.transform.rotation;

            var flower = flowerRoot.AddComponent<FlowerLift>();
            var settings = new SerializedObject(flower);
            settings.FindProperty("animator").objectReferenceValue = animator;
            settings.FindProperty("sproutClip").objectReferenceValue = Clip("LeafLift_Sprout");
            settings.FindProperty("idleBottomClip").objectReferenceValue = Clip("LeafLift_IdleBottom");
            settings.FindProperty("riseClip").objectReferenceValue = Clip("LeafLift_Rise");
            settings.FindProperty("idleTopClip").objectReferenceValue = idleTop;
            settings.FindProperty("descendClip").objectReferenceValue = Clip("LeafLift_Descend");
            SetFollower(settings.FindProperty("leaf"), Follower("Leaf Body", flowerRoot.transform, platformNode));
            SerializedProperty ramps = settings.FindProperty("ramps");
            ramps.arraySize = rampNodes.Length;
            for (int i = 0; i < rampNodes.Length; i++)
                SetFollower(ramps.GetArrayElementAtIndex(i), Follower($"Ramp Body {i}", flowerRoot.transform, rampNodes[i]));

            var stem = new GameObject("Stem Body") { layer = environment };
            stem.transform.SetParent(flowerRoot.transform, false);
            var stemShape = stem.AddComponent<CapsuleCollider>();
            stemShape.radius = StemRadius;
            settings.FindProperty("stemBody").objectReferenceValue = KinematicBody(stem);
            settings.FindProperty("stemShape").objectReferenceValue = stemShape;
            settings.FindProperty("stemFoot").objectReferenceValue = Find(model.transform, "Stem_01");
            settings.FindProperty("stemTop").objectReferenceValue = Find(model.transform, "Leaf");
            settings.FindProperty("riderMask").intValue = LayerMask.GetMask("PlayerLiquid", "PlayerSolid", "Ship", "Pushable");
            settings.ApplyModifiedPropertiesWithoutUndo();

            var soil = root.AddComponent<Soil>();
            var soilSettings = new SerializedObject(soil);
            soilSettings.FindProperty("flower").objectReferenceValue = flower;
            soilSettings.ApplyModifiedPropertiesWithoutUndo();
            flowerRoot.SetActive(false);
            return Save(root, SoilBedPrefabPath);
        }

        /// <summary>
        /// A kinematic body that will follow <paramref name="node"/>, a collider mesh in the model: the mesh becomes its
        /// convex collider, and the node keeps only its transform.
        /// </summary>
        static (Rigidbody body, Collider shape, Transform node) Follower(string name, Transform parent, Transform node)
        {
            var go = new GameObject(name) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(node.position, node.rotation);
            go.transform.localScale = node.lossyScale;
            var shape = go.AddComponent<MeshCollider>();
            shape.sharedMesh = node.GetComponent<MeshFilter>().sharedMesh;
            shape.convex = true;
            Object.DestroyImmediate(node.GetComponent<Renderer>());
            Object.DestroyImmediate(node.GetComponent<MeshFilter>());
            return (KinematicBody(go), shape, node);
        }

        static void SetFollower(SerializedProperty property, (Rigidbody body, Collider shape, Transform node) follower)
        {
            property.FindPropertyRelative("node").objectReferenceValue = follower.node;
            property.FindPropertyRelative("body").objectReferenceValue = follower.body;
            property.FindPropertyRelative("shape").objectReferenceValue = follower.shape;
        }

        static Rigidbody KinematicBody(GameObject go)
        {
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            // Unseen, so no interpolation: the riders interpolate themselves.
            body.interpolation = RigidbodyInterpolation.None;
            return body;
        }

        static Material FlowerMaterial(Material imported, Materials m)
        {
            string name = imported != null ? imported.name : "";
            if (name.StartsWith("M_LiftLeaf")) return m.Leaf;
            if (name.StartsWith("M_LiftStem")) return m.Stem;
            if (name.StartsWith("M_LiftPetalOuter")) return m.PetalOuter;
            if (name.StartsWith("M_LiftPetalInner")) return m.PetalInner;
            if (name.StartsWith("M_LiftStamenStalk")) return m.StamenStalk;
            if (name.StartsWith("M_LiftStamen")) return m.Stamen;
            return imported;
        }

        // ---------------------------------------------------------------- Sandbox

        /// <summary>
        /// Sandbox test pieces, between the grate wall and the ramp: a seed, a soil bed and, north of it, a 6 x 2 m ledge (4 m deep in the middle)
        /// one tile (3 m) up that only the flower reaches. Freeze around the seed, carry it onto the bed and melt.
        /// </summary>
        public static void SandboxPieces(Transform level)
        {
            Transform root = Group("Garden", level);
            Seed("Seed", root, new Vector3(4f, 0f, -6f));
            SoilBed("Soil Bed", root, new Vector3(7f, 0f, -6f), Vector3.forward);
            for (int i = 0; i < 3; i++) Tile($"Ledge Tile {i}", root, "Grey", new Vector3(5f + i * TileSize, TileDepth, -4f));
            // One more behind the middle, so stepping off the flower doesn't carry the blob straight over the far edge.
            Tile("Ledge Tile 3", root, "Grey", new Vector3(7f, TileDepth, -4f + TileSize));
        }

        // ---------------------------------------------------------------- Helpers

        /// <summary>The tile model SM_Tile_&lt;<paramref name="variant"/>&gt; with its own material, pivot at the top centre.</summary>
        static GameObject TileModel(string name, Transform parent, string variant)
        {
            MeshFilter part = Part($"{ArtKit.ModelsFolder}/SM_Tile_{variant}.fbx", $"SM_Tile_{variant}");
            return Copy(name, parent, part, part.GetComponent<MeshRenderer>().sharedMaterials);
        }

        /// <summary>A copy of a model's mesh node under <paramref name="parent"/>, placed as it is in the model.</summary>
        static GameObject Copy(string name, Transform parent, MeshFilter part, params Material[] materials)
        {
            var go = new GameObject(name) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(part.transform.position, part.transform.rotation);
            go.transform.localScale = part.transform.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = part.sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            return go;
        }

        static MeshFilter Part(string path, string nodeName)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model != null)
                foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.name == nodeName) return filter;
            throw new System.InvalidOperationException($"No mesh node '{nodeName}' in {path}.");
        }

        static AnimationClip Clip(string take)
            => AssetDatabase.LoadAllAssetsAtPath(FlowerModelPath).OfType<AnimationClip>()
                   .FirstOrDefault(clip => clip.name.EndsWith(take) && !clip.name.StartsWith("__preview__"))
               ?? throw new System.InvalidOperationException($"No clip '{take}' in {FlowerModelPath}.");

        static Transform Find(Transform root, string name)
            => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name)
               ?? throw new System.InvalidOperationException($"No '{name}' in {FlowerModelPath}.");

        static Texture2D SeedTexture(string map, TextureImporterType type, bool sRGB)
        {
            string path = $"{SeedTexturePrefix}{map}.png";
            if (AssetImporter.GetAtPath(path) is TextureImporter importer
                && (importer.textureType != type || importer.sRGBTexture != sRGB))
            {
                importer.textureType = type;
                importer.sRGBTexture = sRGB;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Lit(string name, Color linear, float smoothness)
            => LoadOrCreate($"{MaterialsFolder}/{name}.mat", () =>
            {
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor", linear.gamma);
                material.SetFloat("_Smoothness", smoothness);
                material.enableInstancing = true;
                return material;
            });

        static GameObject Save(GameObject root, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Debug.Log($"[BounJam] Saved {path}.");
            return prefab;
        }
    }
}
