using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static BounJam.EditorTools.LevelKit;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Level pieces for collectibles and magnets: the iron ball, the magnet and the buttons that switch it on and off,
    /// and the parts the blob needs to carry things and be pulled. Materials are only created when missing.
    /// </summary>
    public static class MagnetKit
    {
        public const float IronBallRadius = 0.15f;
        static readonly Vector3 HousingSize = new Vector3(1.1f, 1.1f, 0.5f);

        public struct Materials
        {
            public Material Iron, Marker, Housing, FaceIdle, FaceActive, Beam, ButtonOn, ButtonOff;
        }

        public static Materials CreateMaterials() => new Materials
        {
            Iron = LoadOrCreate($"{MaterialsFolder}/Collectible_Iron.mat", () =>
            {
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor", new Color(0.20f, 0.21f, 0.23f));
                material.SetFloat("_Metallic", 0.9f);
                material.SetFloat("_Smoothness", 0.6f);
                // Emission stays switched on so the ball can glow (per renderer) while it's inside the jelly.
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                return material;
            }),
            Marker = LitMaterial("Collectible_Marker", new Color(1.00f, 0.70f, 0.30f), 0.3f, new Color(1.2f, 0.6f, 0.15f)),
            Housing = LitMaterial("Magnet_Housing", new Color(0.24f, 0.26f, 0.30f), 0.55f, Color.black),
            FaceIdle = LitMaterial("Magnet_Face_Idle", new Color(0.72f, 0.42f, 0.22f), 0.5f, Color.black),
            FaceActive = LitMaterial("Magnet_Face_Active", new Color(0.35f, 0.65f, 1.00f), 0.6f, new Color(0.5f, 1.1f, 2.4f)),
            Beam = LoadOrCreate($"{MaterialsFolder}/Magnet_Beam.mat", () =>
            {
                var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                material.SetColor("_BaseColor", new Color(0.60f, 0.90f, 1.00f));
                return material;
            }),
            // Each button wears the colour of what it switches the magnet to.
            ButtonOn = LitMaterial("Button_MagnetOn", new Color(0.30f, 0.60f, 1.00f), 0.4f, new Color(0.10f, 0.25f, 0.50f)),
            ButtonOff = LitMaterial("Button_MagnetOff", new Color(0.72f, 0.42f, 0.22f), 0.4f, Color.black),
        };

        // ---------------------------------------------------------------- Pieces

        /// <summary>An iron ball resting on the floor at <paramref name="floorPosition"/>, on a glowing marker.</summary>
        public static Collectible IronBall(string name, Transform parent, Vector3 floorPosition, Materials m)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = floorPosition + Vector3.up * IronBallRadius;

            GameObject ball = Decoration(PrimitiveType.Sphere, "Model", root.transform, Vector3.zero,
                                         Vector3.one * (IronBallRadius * 2f), m.Iron);
            ball.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
            GameObject marker = Decoration(PrimitiveType.Cylinder, "Marker", root.transform, new Vector3(0f, 0.005f - IronBallRadius, 0f),
                                           new Vector3(0.7f, 0.004f, 0.7f), m.Marker);

            var collectible = root.AddComponent<Collectible>();
            var settings = new SerializedObject(collectible);
            settings.FindProperty("radius").floatValue = IronBallRadius;
            settings.FindProperty("modelRenderer").objectReferenceValue = ball.GetComponent<MeshRenderer>();
            settings.FindProperty("marker").objectReferenceValue = marker.transform;
            settings.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<IronBall>();
            return collectible;
        }

        /// <summary>
        /// A magnet whose face centre is at <paramref name="face"/>, pointing along <paramref name="facing"/>, with its
        /// housing behind the face. Pressing one of <paramref name="onButtons"/> switches it on, one of
        /// <paramref name="offButtons"/> off.
        /// </summary>
        public static Magnet Magnet(string name, Transform parent, Vector3 face, Vector3 facing,
                                    PressureButton[] onButtons, PressureButton[] offButtons, Materials m, float range = 7f)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(face, Quaternion.LookRotation(facing));
            Transform frame = root.transform;

            Box("Housing", frame, frame.TransformPoint(new Vector3(0f, 0f, -HousingSize.z * 0.5f)), HousingSize, m.Housing,
                euler: frame.eulerAngles);
            GameObject plate = Decoration(PrimitiveType.Cube, "Face", frame, new Vector3(0f, 0f, 0.01f),
                                          new Vector3(HousingSize.x * 0.8f, HousingSize.y * 0.8f, 0.04f), m.FaceIdle);

            var beamObject = new GameObject("Beam");
            beamObject.transform.SetParent(frame, false);
            var beam = beamObject.AddComponent<LineRenderer>();
            beam.useWorldSpace = true;
            beam.positionCount = 2;
            beam.numCapVertices = 2;
            beam.sharedMaterial = m.Beam;
            beam.shadowCastingMode = ShadowCastingMode.Off;
            beam.receiveShadows = false;
            beam.enabled = false;

            var magnet = root.AddComponent<Magnet>();
            var settings = new SerializedObject(magnet);
            SetButtons(settings.FindProperty("onButtons"), onButtons);
            SetButtons(settings.FindProperty("offButtons"), offButtons);
            settings.FindProperty("range").floatValue = range;
            settings.FindProperty("sightMask").intValue = LayerMask.GetMask("Default", "Environment", "Pushable");
            settings.FindProperty("faceRenderer").objectReferenceValue = plate.GetComponent<MeshRenderer>();
            settings.FindProperty("idleMaterial").objectReferenceValue = m.FaceIdle;
            settings.FindProperty("activeMaterial").objectReferenceValue = m.FaceActive;
            settings.FindProperty("beam").objectReferenceValue = beam;
            settings.ApplyModifiedPropertiesWithoutUndo();
            return magnet;
        }

        /// <summary>A pressure button that switches magnets on (<paramref name="switchesOn"/>) or off, coloured to match.</summary>
        public static PressureButton MagnetButton(string name, Transform parent, Vector3 position, bool switchesOn,
                                                  Palette palette, Materials m)
        {
            PressureButton button = Button(name, parent, position, palette);
            Material idle = switchesOn ? m.ButtonOn : m.ButtonOff;
            var settings = new SerializedObject(button);
            settings.FindProperty("idleMaterial").objectReferenceValue = idle;
            ((Renderer)settings.FindProperty("capRenderer").objectReferenceValue).sharedMaterial = idle;
            settings.ApplyModifiedPropertiesWithoutUndo();
            return button;
        }

        static void SetButtons(SerializedProperty list, PressureButton[] buttons)
        {
            list.arraySize = buttons.Length;
            for (int i = 0; i < buttons.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
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
        /// Sandbox test pieces: an iron ball near the spawn, and a magnet on a 2 m tower north of the big platform,
        /// across a 3 m gap. Freeze on the blue button on the platform to switch it on and get pulled over; the
        /// copper button under the magnet switches it off again.
        /// </summary>
        public static void SandboxPieces(Transform level, Palette palette)
        {
            Materials m = CreateMaterials();
            Transform root = Group("Collectibles And Magnets", level);
            IronBall("Iron Ball", root, new Vector3(2.5f, 0f, -12f), m);

            const float top = 2f;
            Box("Magnet Tower", root, new Vector3(14f, top * 0.5f, 17f), new Vector3(4f, top, 2f), palette.Structure);
            PressureButton on = MagnetButton("Button Magnet On", root, new Vector3(14f, top, 11.5f), true, palette, m);
            PressureButton off = MagnetButton("Button Magnet Off", root, new Vector3(14f, top, 16.85f), false, palette, m);
            Magnet("Magnet", root, new Vector3(14f, top + HousingSize.y * 0.5f, 18f - HousingSize.z), Vector3.back,
                   new[] { on }, new[] { off }, m, range: 7.5f);
        }

        // ---------------------------------------------------------------- Blob

        /// <summary>Adds the carrying and magnet parts to a blob root that doesn't have them yet. True if it changed.</summary>
        public static bool AddBlobParts(GameObject blob)
        {
            bool changed = false;
            if (!blob.TryGetComponent(out BlobCargo _))
            {
                blob.AddComponent<BlobCargo>();
                changed = true;
            }
            if (!blob.TryGetComponent(out BlobMagnetism _))
            {
                blob.AddComponent<BlobMagnetism>();
                changed = true;
            }
            return changed;
        }

        /// <summary>Adds those parts to the saved Blob prefab, leaving all its tuning as it is.</summary>
        [MenuItem("BounJam/Add Carrying And Magnets To Blob Prefab")]
        public static void UpgradeBlobPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(BlobPrefabPath) == null) return;
            GameObject root = PrefabUtility.LoadPrefabContents(BlobPrefabPath);
            if (AddBlobParts(root))
            {
                PrefabUtility.SaveAsPrefabAsset(root, BlobPrefabPath);
                Debug.Log("[BounJam] Blob prefab: added carrying and magnets.");
            }
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
