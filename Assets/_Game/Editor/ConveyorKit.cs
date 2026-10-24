using UnityEditor;
using UnityEngine;
using static BounJam.EditorTools.LevelKit;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Level pieces for the spiked conveyor: its prefab (built from SM_Conveyor, made by
    /// ArtSource/Blender/scripts/conveyor.py), placing one, and the part the blob needs to hold on to a spike.
    /// Materials and the prefab are only created when missing, so tweaks made in the editor survive a rebuild.
    /// </summary>
    public static class ConveyorKit
    {
        public const string PrefabPath = "Assets/_Game/Prefabs/Conveyor.prefab";
        const string ModelPath = ArtKit.ModelsFolder + "/SM_Conveyor.fbx";

        struct Materials
        {
            public Material Belt, Steel, SpikeTip, Drum, Lid, Hub;
        }

        // Colours are Blender's linear values; Material.SetColor takes sRGB, hence .gamma.
        static Materials CreateMaterials() => new Materials
        {
            Belt = Lit("Conveyor_Belt", new Color(0.045f, 0.05f, 0.06f), 0.35f),
            // Emission stays switched on for the spikes' parts, so each spike can glow (per renderer) while it's inside
            // the jelly or held.
            Steel = Lit("Conveyor_Steel", new Color(0.72f, 0.74f, 0.78f), 0.78f, metallic: 0.85f, glows: true),
            SpikeTip = Lit("Conveyor_SpikeTip", new Color(0.55f, 0.82f, 1.0f), 0.85f, metallic: 0.6f, glows: true),
            Drum = Lit("Conveyor_Drum", new Color(0.16f, 0.17f, 0.2f), 0.65f, metallic: 0.8f),
            Lid = Lit("Conveyor_Lid", new Color(0.92f, 0.22f, 0.14f), 0.7f),
            Hub = Lit("Conveyor_Hub", new Color(1.0f, 0.68f, 0.08f), 0.72f, metallic: 0.35f),
        };

        /// <summary>
        /// A conveyor whose middle is at <paramref name="center"/> on the floor, its belt running along
        /// <paramref name="along"/> (outward on the right-hand side, back on the left).
        /// </summary>
        public static Conveyor Conveyor(string name, Transform parent, Vector3 center, Vector3 along, float length,
                                        float speed = 1.2f)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) prefab = BuildPrefab();
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.SetPositionAndRotation(center, Quaternion.LookRotation(along));
            var conveyor = go.GetComponent<Conveyor>();
            var settings = new SerializedObject(conveyor);
            settings.FindProperty("length").floatValue = length;
            settings.FindProperty("speed").floatValue = speed;
            settings.ApplyModifiedPropertiesWithoutUndo();
            return conveyor;
        }

        /// <summary>Saves the Conveyor prefab from the current model, replacing the old one (placed ones keep their settings).</summary>
        [MenuItem("BounJam/Rebuild Conveyor Prefab")]
        public static GameObject BuildPrefab()
        {
            Materials m = CreateMaterials();
            var root = new GameObject("Conveyor");
            var settings = new SerializedObject(root.AddComponent<Conveyor>());
            SetMesh(settings, "slatMesh", "SM_ConveyorSlat");
            SetMesh(settings, "spikeMesh", "SM_ConveyorSpike");
            SetMesh(settings, "spikeTipMesh", "SM_ConveyorSpikeTip");
            SetMesh(settings, "drumMesh", "SM_ConveyorDrum");
            SetMesh(settings, "lidMiddleMesh", "SM_ConveyorLidMid");
            SetMesh(settings, "lidEndMesh", "SM_ConveyorLidEnd");
            SetMesh(settings, "hubMesh", "SM_ConveyorHub");
            settings.FindProperty("beltMaterial").objectReferenceValue = m.Belt;
            settings.FindProperty("steelMaterial").objectReferenceValue = m.Steel;
            settings.FindProperty("spikeTipMaterial").objectReferenceValue = m.SpikeTip;
            settings.FindProperty("drumMaterial").objectReferenceValue = m.Drum;
            settings.FindProperty("lidMaterial").objectReferenceValue = m.Lid;
            settings.FindProperty("hubMaterial").objectReferenceValue = m.Hub;
            settings.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[BounJam] Saved {PrefabPath}.");
            return prefab;
        }

        // ---------------------------------------------------------------- Sandbox

        /// <summary>
        /// Sandbox test piece: a conveyor across the west pool, from the south bank to the north bank. Flow up to the
        /// east side of its south end as liquid, freeze once a spike's tip glows inside the jelly, and melt on the
        /// north bank to drop off. Melting over the water dissolves the blob.
        /// </summary>
        public static void SandboxPieces(Transform level)
        {
            Conveyor("Spike Conveyor", level, new Vector3(-14.5f, 0f, -8.2f), Vector3.forward, 10.4f);
        }

        // ---------------------------------------------------------------- Blob

        /// <summary>Adds the part that holds on to spikes to a blob root that doesn't have it yet. True if it changed.</summary>
        public static bool AddBlobParts(GameObject blob)
        {
            if (blob.TryGetComponent(out BlobGrip _)) return false;
            blob.AddComponent<BlobGrip>();
            return true;
        }

        /// <summary>Adds that part to the saved Blob prefab, leaving all its tuning as it is.</summary>
        [MenuItem("BounJam/Add Spike Grip To Blob Prefab")]
        public static void UpgradeBlobPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(BlobPrefabPath) == null) return;
            GameObject root = PrefabUtility.LoadPrefabContents(BlobPrefabPath);
            if (AddBlobParts(root))
            {
                PrefabUtility.SaveAsPrefabAsset(root, BlobPrefabPath);
                Debug.Log("[BounJam] Blob prefab: added the spike grip.");
            }
            PrefabUtility.UnloadPrefabContents(root);
        }

        // ---------------------------------------------------------------- Helpers

        static void SetMesh(SerializedObject settings, string property, string meshName)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Mesh mesh = null;
            if (model != null)
                foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh != null && (filter.sharedMesh.name == meshName || filter.name == meshName))
                        mesh = filter.sharedMesh;
            if (mesh == null)
                throw new System.InvalidOperationException(
                    $"No mesh '{meshName}' in {ModelPath}; export it with ArtSource/Blender/scripts/conveyor.py.");
            settings.FindProperty(property).objectReferenceValue = mesh;
        }

        static Material Lit(string name, Color linear, float smoothness, float metallic = 0f, bool glows = false)
            => LoadOrCreate($"{MaterialsFolder}/{name}.mat", () =>
            {
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor", linear.gamma);
                material.SetFloat("_Smoothness", smoothness);
                material.SetFloat("_Metallic", metallic);
                if (glows)
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", Color.black);
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                return material;
            });
    }
}
