using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static BounJam.EditorTools.LevelKit;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Level pieces for growing the blob: water droplets (the SM_WaterDrop model) that the liquid soaks up, and the
    /// size part the blob needs to grow. Materials are only created when missing.
    /// </summary>
    public static class DropletKit
    {
        const string DropModelPath = ArtKit.ModelsFolder + "/SM_WaterDrop.fbx";
        // The drop's centre hovers this far above the floor; the model is scaled to this height.
        const float HoverHeight = 0.3f;
        const float DropHeight = 0.4f;
        const float DropRadius = 0.15f;

        public struct Materials
        {
            public Material Drop, Marker;
        }

        public static Materials CreateMaterials() => new Materials
        {
            Drop = LitMaterial("Droplet_Water", new Color(0.40f, 0.72f, 1.00f), 0.95f, new Color(0.10f, 0.35f, 0.70f)),
            Marker = LitMaterial("Droplet_Marker", new Color(0.55f, 0.85f, 1.00f), 0.3f, new Color(0.20f, 0.60f, 1.20f)),
        };

        // ---------------------------------------------------------------- Pieces

        /// <summary>
        /// A water droplet hovering over <paramref name="floorPosition"/> on a glowing marker. Soaking it up adds
        /// <paramref name="volume"/> of the starting blob's water to the blob.
        /// </summary>
        public static WaterDroplet Droplet(string name, Transform parent, Vector3 floorPosition, Materials m, float volume = 0.3f)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = floorPosition + Vector3.up * HoverHeight;

            GameObject model = DropModel(root.transform, m.Drop);
            GameObject marker = Decoration(PrimitiveType.Cylinder, "Marker", root.transform, new Vector3(0f, 0.005f - HoverHeight, 0f),
                                           new Vector3(0.7f, 0.004f, 0.7f), m.Marker);

            var droplet = root.AddComponent<WaterDroplet>();
            var settings = new SerializedObject(droplet);
            settings.FindProperty("volume").floatValue = volume;
            settings.FindProperty("radius").floatValue = DropRadius;
            settings.FindProperty("model").objectReferenceValue = model.transform;
            settings.FindProperty("marker").objectReferenceValue = marker.transform;
            settings.ApplyModifiedPropertiesWithoutUndo();
            return droplet;
        }

        /// <summary>The SM_WaterDrop model, <see cref="DropHeight"/> tall and centred on the drop; a sphere if it's missing.</summary>
        static GameObject DropModel(Transform parent, Material material)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(DropModelPath);
            if (asset == null)
                return Decoration(PrimitiveType.Sphere, "Model", parent, Vector3.zero, Vector3.one * (DropRadius * 2f), material);

            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            foreach (Renderer renderer in renderers)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
            }

            // Whatever the file's units and pivot: scale to the drop's height, then centre it on the drop.
            Bounds bounds = RenderBounds(renderers);
            if (bounds.size.y > 1e-4f) model.transform.localScale *= DropHeight / bounds.size.y;
            bounds = RenderBounds(renderers);
            model.transform.position += parent.position - bounds.center;
            return model;
        }

        static Bounds RenderBounds(Renderer[] renderers)
        {
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

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
        /// Sandbox test pieces: four droplets along the open lane north of the spawn, and two 2 m ledges off the big
        /// platform for stretching across gaps. North of it, across a 3 m gap: the starting blob reaches over once it's
        /// fully stretched (a straight run up the ramp and over the platform). South of it, beside the ramp, across a
        /// 4.2 m gap: only a blob grown by the droplets reaches that far (run south over the platform).
        /// </summary>
        public static void SandboxPieces(Transform level, Palette palette)
        {
            Materials m = CreateMaterials();
            Transform root = Group("Droplets And Gaps", level);
            Droplet("Water Droplet 1", root, new Vector3(-1.5f, 0f, -9f), m);
            Droplet("Water Droplet 2", root, new Vector3(1f, 0f, -6f), m);
            Droplet("Water Droplet 3", root, new Vector3(-1f, 0f, -2.5f), m);
            Droplet("Water Droplet 4", root, new Vector3(1f, 0f, 1.5f), m);

            // The platform is 10 x 8 m, its top at 2 m: x 6..16, z 5..13.
            const float top = 2f;
            ArtKit.Platform("Ledge Short Gap", root, new Vector3(7.5f, top * 0.5f, 17f), new Vector3(3f, top, 2f), palette.Structure);
            ArtKit.Platform("Ledge Long Gap", root, new Vector3(7.4f, top * 0.5f, -0.3f), new Vector3(2.8f, top, 2.2f), palette.Structure);
        }

        // ---------------------------------------------------------------- Blob

        /// <summary>Adds the size part to a blob root that doesn't have it yet. True if it changed.</summary>
        public static bool AddBlobParts(GameObject blob)
        {
            if (blob.TryGetComponent(out BlobSize _)) return false;
            blob.AddComponent<BlobSize>();
            return true;
        }

        /// <summary>Adds that part to the saved Blob prefab, leaving all its tuning as it is.</summary>
        [MenuItem("BounJam/Add Growing To Blob Prefab")]
        public static void UpgradeBlobPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(BlobPrefabPath) == null) return;
            GameObject root = PrefabUtility.LoadPrefabContents(BlobPrefabPath);
            if (AddBlobParts(root))
            {
                PrefabUtility.SaveAsPrefabAsset(root, BlobPrefabPath);
                Debug.Log("[BounJam] Blob prefab: added growing.");
            }
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
