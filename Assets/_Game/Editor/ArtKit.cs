using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BounJam.EditorTools
{
    /// <summary>
    /// The modelled art from ArtSource/Blender (exported to Assets/_Project/Art/Models) and the level kit pieces built
    /// from it: the blob's balloon rest shape and eyes, the tugboat, the magnet and pocket magnet, push blocks,
    /// pressure buttons and the 2 m platform blocks that cover floors. Materials are made here from the Blender colours
    /// or the baked texture sets (the FBX files carry none) and, like the rest of the kit's, only created when missing.
    /// </summary>
    public static class ArtKit
    {
        public const string ModelsFolder = "Assets/_Project/Art/Models";
        const string PropTexturesFolder = "Assets/_Project/Art/Textures/Props";
        const string BlobBodyPath = ModelsFolder + "/SM_BlobBase.fbx";
        const string BlobEyePath = ModelsFolder + "/SM_BlobEye.fbx";
        const string ShipPath = ModelsFolder + "/SM_TugBoat.fbx";
        const string MagnetPath = ModelsFolder + "/SM_Magnet.fbx";
        const string PocketMagnetPath = ModelsFolder + "/SM_PocketMagnet.fbx";
        const string PushBlockPath = ModelsFolder + "/SM_PushBlock.fbx";
        const string ButtonPath = ModelsFolder + "/SM_PressureButton.fbx";
        const string PlatformPath = ModelsFolder + "/SM_Platform_2x2.fbx";
        const string CheckerShaderName = "BounJam/Platform Checker";

        const float PlatformSize = 2f;
        // The modelled button is 0.3 m tall; squashed and sunk a little, it's low enough to slide crates onto.
        const float ButtonSquash = 0.4f;
        const float ButtonSink = 0.035f;
        const float ButtonPressDepth = 0.05f;

        struct Materials
        {
            public Material EyeBlack, EyeShine, PushBlock, PushBlockPanel, ButtonBase, ButtonLightOff, ButtonLightOn;
            public Material PlatformTop, PlatformSide;
        }

        // Colours are Blender's linear values; Material.SetColor takes sRGB, hence .gamma.
        static Materials CreateMaterials() => new Materials
        {
            EyeBlack = Lit("BlobEye_Black", new Color(0.01f, 0.012f, 0.03f), 0.94f),
            EyeShine = Lit("BlobEye_Shine", Color.white, 0.8f, emission: Color.white),
            PushBlock = Lit("PushBlock_Body", new Color(0.30f, 0.20f, 0.95f), 0.7f),
            PushBlockPanel = Lit("PushBlock_Panel", new Color(1.0f, 0.72f, 0.05f), 0.65f),
            ButtonBase = Lit("Button_Base", new Color(1.0f, 0.68f, 0.08f), 0.72f, metallic: 0.35f),
            ButtonLightOff = Lit("Button_LightOff", new Color(1.0f, 0.97f, 0.90f), 0.7f),
            ButtonLightOn = Lit("Button_LightOn", new Color(1.0f, 0.97f, 0.90f), 0.7f, emission: new Color(2.0f, 1.8f, 1.2f)),
            PlatformTop = Checker("Platform_Top", new Color(1.0f, 0.90f, 0.62f), new Color(1.0f, 0.70f, 0.30f)),
            PlatformSide = Checker("Platform_Side", new Color(0.97f, 0.95f, 0.90f), new Color(0.97f, 0.95f, 0.90f)),
        };

        // ---------------------------------------------------------------- Blob

        /// <summary>
        /// Gives a blob its modelled look: the skin settles into the SM_BlobBase balloon instead of a sphere (the editor
        /// preview shows it too), and two SM_BlobEye eyes ride on its surface. Running it again just refreshes them.
        /// </summary>
        public static void AddBlobArt(GameObject root)
        {
            Materials m = CreateMaterials();
            Mesh body = Part(BlobBodyPath, "SM_BlobBase").sharedMesh;

            var skin = root.GetComponent<BlobSkin>();
            var skinSettings = new SerializedObject(skin);
            skinSettings.FindProperty("restShape").objectReferenceValue = body;
            skinSettings.ApplyModifiedPropertiesWithoutUndo();
            var skinFilter = (MeshFilter)skinSettings.FindProperty("skinFilter").objectReferenceValue;
            Mesh preview = LevelKit.BuildRestPreviewMesh(body);
            skinFilter.sharedMesh = preview;

            Mesh eyeMesh = Part(BlobEyePath, "SM_BlobEye").sharedMesh;
            Transform left = Eye("Eye Left", skinFilter.transform, eyeMesh, m);
            Transform right = Eye("Eye Right", skinFilter.transform, eyeMesh, m);
            if (!root.TryGetComponent(out BlobEyes eyes)) eyes = root.AddComponent<BlobEyes>();
            var eyeSettings = new SerializedObject(eyes);
            eyeSettings.FindProperty("leftEye").objectReferenceValue = left;
            eyeSettings.FindProperty("rightEye").objectReferenceValue = right;
            eyeSettings.ApplyModifiedPropertiesWithoutUndo();
            // Facing the camera, like the skin does at the start.
            eyes.PlaceOn(preview, Vector3.back);
        }

        static Transform Eye(string name, Transform skin, Mesh mesh, Materials m)
        {
            Transform eye = skin.Find(name);
            if (eye == null) eye = Model(name, skin, mesh, m.EyeBlack, m.EyeShine).transform;
            eye.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return eye;
        }

        // ---------------------------------------------------------------- Ship

        /// <summary>The tugboat's visible mesh (pivot at the bottom of the hull, bow along -Z).</summary>
        public static Mesh ShipMesh() => Part(ShipPath, "SM_TugBoat").sharedMesh;

        /// <summary>Convex hull of the tugboat's hull and cabin, without the funnel.</summary>
        public static Mesh ShipHullMesh() => Part(ShipPath, "SM_TugBoat_Collider").sharedMesh;

        public static Material ShipMaterial() => Baked("TugBoat", "TugBoat");

        // ---------------------------------------------------------------- Magnets

        /// <summary>
        /// The magnet's housing in the space of its face: the face centre at the origin pointing along +Z, the housing
        /// behind it and its base plate below.
        /// </summary>
        public static Bounds MagnetBounds() => Part(MagnetPath, "SM_Magnet_Housing").sharedMesh.bounds;

        /// <summary>
        /// Builds the modelled magnet under <paramref name="frame"/> (the face centre, facing +Z): the housing, solid
        /// behind the face so the field starts clear of it, and the face disc, returned so the magnet can light it up.
        /// </summary>
        public static MeshRenderer DressMagnet(Transform frame, Material housingMaterial, Material faceMaterial)
        {
            Mesh housingMesh = Part(MagnetPath, "SM_Magnet_Housing").sharedMesh;
            GameObject housing = Model("Housing", frame, housingMesh, housingMaterial);
            housing.layer = LayerMask.NameToLayer("Environment");
            Bounds bounds = housingMesh.bounds;
            var solid = housing.AddComponent<BoxCollider>();
            solid.center = new Vector3(bounds.center.x, bounds.center.y, bounds.min.z * 0.5f);
            solid.size = new Vector3(bounds.size.x, bounds.size.y, -bounds.min.z);

            GameObject face = Model("Face", frame, Part(MagnetPath, "SM_Magnet_Face").sharedMesh, faceMaterial);
            return face.GetComponent<MeshRenderer>();
        }

        /// <summary>
        /// Adds the pocket magnet model to a collectible: lying flat, centred on <paramref name="item"/> with its
        /// underside <paramref name="radius"/> below it, on the floor.
        /// </summary>
        public static MeshRenderer PocketMagnet(Transform item, float radius, Material material)
        {
            Mesh mesh = Part(PocketMagnetPath, "SM_PocketMagnet").sharedMesh;
            GameObject model = Model("Model", item, mesh, material);
            Bounds bounds = mesh.bounds;
            model.transform.localPosition = -new Vector3(bounds.center.x, bounds.min.y + radius, bounds.center.z);
            return model.GetComponent<MeshRenderer>();
        }

        // ---------------------------------------------------------------- Level pieces

        /// <summary>Adds the push block model to a crate: a unit cube centred on <paramref name="crate"/>.</summary>
        public static void PushBlock(Transform crate)
        {
            Materials m = CreateMaterials();
            GameObject model = Model("Model", crate, Part(PushBlockPath, "SM_PushBlock").sharedMesh, m.PushBlock, m.PushBlockPanel);
            model.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        }

        /// <summary>
        /// Builds the modelled button under the PressureButton's object: a gold ring, the dome cap that sinks while
        /// pressed (wearing <paramref name="capMaterial"/>) and a light ring that glows while pressed. Visual only, no
        /// colliders, so nothing snags on it.
        /// </summary>
        public static void DressButton(PressureButton button, Material capMaterial)
        {
            Materials m = CreateMaterials();
            Transform root = button.transform;
            ButtonPart("Base", root, "SM_PressureButton_Base", m.ButtonBase);
            GameObject cap = ButtonPart("Cap", root, "SM_PressureButton_Cap", capMaterial);
            GameObject ring = ButtonPart("Light", root, "SM_PressureButton_Light", m.ButtonLightOff);

            var settings = new SerializedObject(button);
            settings.FindProperty("cap").objectReferenceValue = cap.transform;
            settings.FindProperty("capRenderer").objectReferenceValue = cap.GetComponent<MeshRenderer>();
            settings.FindProperty("lightRenderer").objectReferenceValue = ring.GetComponent<MeshRenderer>();
            settings.FindProperty("lightOffMaterial").objectReferenceValue = m.ButtonLightOff;
            settings.FindProperty("lightOnMaterial").objectReferenceValue = m.ButtonLightOn;
            settings.FindProperty("pressDepth").floatValue = ButtonPressDepth;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject ButtonPart(string name, Transform root, string meshName, Material material)
        {
            MeshFilter part = Part(ButtonPath, meshName);
            GameObject go = Model(name, root, part.sharedMesh, material);
            // The cap and light ring sit on the ring in the model; keep that, squashed.
            Transform ring = Part(ButtonPath, "SM_PressureButton_Base").transform;
            float height = ring.InverseTransformPoint(part.transform.position).y;
            go.transform.localPosition = new Vector3(0f, height * ButtonSquash - ButtonSink, 0f);
            go.transform.localScale = new Vector3(1f, ButtonSquash, 1f);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        /// <summary>
        /// A solid box at <paramref name="center"/> of <paramref name="size"/>, covered in platform blocks: 2 m on a
        /// side, stretched a little where the box isn't a whole number of blocks, tops level with the box top and at
        /// least 2 m deep. The blocks are only seen; one box collider does the physics, so nothing catches on seams.
        /// Without the model it falls back to a greybox in <paramref name="fallback"/>.
        /// </summary>
        public static GameObject Platform(string name, Transform parent, Vector3 center, Vector3 size, Material fallback)
        {
            Mesh mesh = TryPart(PlatformPath, "SM_Platform_2x2")?.sharedMesh;
            if (mesh == null) return LevelKit.Box(name, parent, center, size, fallback);

            var root = new GameObject(name) { layer = LayerMask.NameToLayer("Environment") };
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            root.AddComponent<BoxCollider>().size = size;

            Materials m = CreateMaterials();
            Material[] materials = TopAndSides(mesh, m.PlatformTop, m.PlatformSide);
            int countX = Mathf.Max(1, Mathf.RoundToInt(size.x / PlatformSize));
            int countY = Mathf.Max(1, Mathf.RoundToInt(size.y / PlatformSize));
            int countZ = Mathf.Max(1, Mathf.RoundToInt(size.z / PlatformSize));
            var block = new Vector3(size.x / countX, Mathf.Max(PlatformSize, size.y / countY), size.z / countZ);
            Vector3 corner = new Vector3(-size.x * 0.5f, size.y * 0.5f, -size.z * 0.5f);
            for (int y = 0; y < countY; y++)
            for (int x = 0; x < countX; x++)
            for (int z = 0; z < countZ; z++)
            {
                // Pivot at the top centre of each block.
                GameObject tile = Model($"Block {x},{y},{z}", root.transform, mesh, materials);
                tile.transform.localPosition = corner + new Vector3((x + 0.5f) * block.x, -y * block.y, (z + 0.5f) * block.z);
                tile.transform.localScale = block / PlatformSize;
            }
            return root;
        }

        // ---------------------------------------------------------------- Applying to saved assets

        /// <summary>
        /// Reimports the art models with the game's import settings, then puts the art on the saved Blob prefab (keeping
        /// its tuning) and rebuilds the PaperShip prefab around the new model. Rebuild the levels afterwards for the
        /// level pieces.
        /// </summary>
        [MenuItem("BounJam/Apply Art Models")]
        public static void ApplyToPrefabs()
        {
            foreach (string guid in AssetDatabase.FindAssets("SM_ t:Model", new[] { ModelsFolder }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);

            GameObject blob = PrefabUtility.LoadPrefabContents(LevelKit.BlobPrefabPath);
            AddBlobArt(blob);
            PrefabUtility.SaveAsPrefabAsset(blob, LevelKit.BlobPrefabPath);
            PrefabUtility.UnloadPrefabContents(blob);

            LevelKit.RebuildShipPrefab();
            AssetDatabase.SaveAssets();
            Debug.Log("[BounJam] Art models applied to the Blob and PaperShip prefabs.");
        }

        // ---------------------------------------------------------------- Helpers

        /// <summary>The imported mesh called <paramref name="meshName"/> in a model file, with the node that holds it.</summary>
        static MeshFilter Part(string path, string meshName)
            => TryPart(path, meshName)
               ?? throw new System.InvalidOperationException(
                   $"No mesh '{meshName}' in {path}; export it with its script in ArtSource/Blender/scripts.");

        static MeshFilter TryPart(string path, string meshName)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) return null;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null && (filter.sharedMesh.name == meshName || filter.name == meshName))
                    return filter;
            return null;
        }

        /// <summary>
        /// <paramref name="top"/> for the submesh facing up, <paramref name="sides"/> for the rest. The FBX importer
        /// orders submeshes by which material it meets first, not by Blender's slots.
        /// </summary>
        static Material[] TopAndSides(Mesh mesh, Material top, Material sides)
        {
            Vector3[] normals = mesh.normals;
            var materials = new Material[mesh.subMeshCount];
            for (int s = 0; s < materials.Length; s++)
            {
                float up = 0f;
                int[] indices = mesh.GetIndices(s);
                foreach (int i in indices) up += normals[i].y;
                materials[s] = up > 0.75f * indices.Length ? top : sides;
            }
            return materials;
        }

        static GameObject Model(string name, Transform parent, Mesh mesh, params Material[] materials)
        {
            var go = new GameObject(name) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            return go;
        }

        static Material Lit(string name, Color linear, float smoothness, float metallic = 0f, Color emission = default)
            => LevelKit.LoadOrCreate($"{LevelKit.MaterialsFolder}/{name}.mat", () =>
            {
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor", linear.gamma);
                material.SetFloat("_Smoothness", smoothness);
                material.SetFloat("_Metallic", metallic);
                if (emission != default)
                {
                    material.EnableKeyword("_EMISSION");
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    material.SetColor("_EmissionColor", emission);
                }
                material.enableInstancing = true;
                return material;
            });

        static Material Checker(string name, Color linearA, Color linearB)
            => LevelKit.LoadOrCreate($"{LevelKit.MaterialsFolder}/{name}.mat", () =>
            {
                var material = new Material(Shader.Find(CheckerShaderName));
                material.SetColor("_ColorA", linearA.gamma);
                material.SetColor("_ColorB", linearB.gamma);
                material.SetFloat("_CellSize", 0.5f);
                material.enableInstancing = true;
                return material;
            });

        /// <summary>
        /// A lit material wearing a baked texture set from the props folder: T_<paramref name="set"/>_Albedo, _Normal
        /// and _MetallicSmoothness (smoothness in its alpha). With an <paramref name="emission"/> colour, emission stays
        /// switched on; black still lets renderers glow through their own property blocks.
        /// </summary>
        public static Material Baked(string name, string set, Color? emission = null)
            => LevelKit.LoadOrCreate($"{LevelKit.MaterialsFolder}/{name}.mat", () =>
            {
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor", Color.white);
                material.SetTexture("_BaseMap", PropTexture(set, "Albedo", TextureImporterType.Default, true));
                material.SetTexture("_BumpMap", PropTexture(set, "Normal", TextureImporterType.NormalMap, false));
                material.EnableKeyword("_NORMALMAP");
                // Smoothness from the map's alpha, scaled by _Smoothness.
                material.SetTexture("_MetallicGlossMap", PropTexture(set, "MetallicSmoothness", TextureImporterType.Default, false));
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.SetFloat("_Smoothness", 1f);
                if (emission is Color glow)
                {
                    material.EnableKeyword("_EMISSION");
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    material.SetColor("_EmissionColor", glow);
                }
                material.enableInstancing = true;
                return material;
            });

        static Texture2D PropTexture(string set, string map, TextureImporterType type, bool sRGB)
        {
            string path = $"{PropTexturesFolder}/T_{set}_{map}.png";
            if (AssetImporter.GetAtPath(path) is TextureImporter importer
                && (importer.textureType != type || importer.sRGBTexture != sRGB))
            {
                importer.textureType = type;
                importer.sRGBTexture = sRGB;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path)
                   ?? throw new System.InvalidOperationException($"No texture {path}.");
        }
    }
}
