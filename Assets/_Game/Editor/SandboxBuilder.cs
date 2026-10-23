using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Builds the greybox sandbox: test geometry, the Blob prefab (core, motor, soft skin), the fixed-angle follow
    /// camera and the restarter.
    /// Running it again replaces everything it created in the scene; the template's light and volume stay. Materials,
    /// the physics material and the Blob prefab are only created when missing, so tweaks made in the editor survive a
    /// rebuild ("Rebuild Blob Prefab" regenerates the prefab on purpose).
    /// </summary>
    public static class SandboxBuilder
    {
        const string ScenePath = "Assets/_Game/Scenes/Sandbox.unity";
        const string MaterialsFolder = "Assets/_Game/Art/Materials";
        const string BlobPrefabPath = "Assets/_Game/Prefabs/Blob.prefab";
        const string GridShaderName = "BounJam/Greybox Grid";

        static readonly string[] GeneratedRoots = { "Level", "Blob", "PaperShip", "CM FollowCam", "Game" };
        // Core resting on the floor, so the blob also sits on the ground in the editor.
        static readonly Vector3 SpawnPoint = new Vector3(0f, CoreRadius, -12f);
        static readonly Vector3 CameraOffset = new Vector3(0f, 9f, -7.5f);
        const float CameraFieldOfView = 40f;
        const float CoreRadius = 0.3f;
        const float BlobRadius = 0.5f;
        const string RestPreviewMeshPath = "Assets/_Game/Art/Models/Blob_RestPreview.asset";
        const string ShipModelPath = "Assets/_Game/Art/Models/PaperShip.fbx";
        const string ShipPrefabPath = "Assets/_Game/Prefabs/PaperShip.prefab";
        // Dropped from just above the resting blob so it settles onto the jelly.
        static readonly Vector3 ShipSpawnOffset = new Vector3(0f, 1.0f, 0f);

        struct Palette
        {
            public Material Floor, Wall, Structure, Obstacle, Crate, Grate, Blob, Paper;
        }

        [MenuItem("BounJam/Build Sandbox Level")]
        public static void Build()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(ScenePath);
            }

            foreach (GameObject root in scene.GetRootGameObjects())
                if (System.Array.IndexOf(GeneratedRoots, root.name) >= 0)
                    Object.DestroyImmediate(root);

            Palette palette = CreatePalette();
            BuildLevel(palette);

            // Reuse the existing Blob prefab so tuning done in the Inspector survives a level rebuild.
            GameObject blobPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlobPrefabPath);
            if (blobPrefab == null) blobPrefab = BuildBlobPrefab(palette.Blob);
            var blob = (GameObject)PrefabUtility.InstantiatePrefab(blobPrefab);
            blob.transform.position = SpawnPoint;

            GameObject shipPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShipPrefabPath);
            if (shipPrefab == null) shipPrefab = BuildShipPrefab(palette.Paper);
            var ship = (GameObject)PrefabUtility.InstantiatePrefab(shipPrefab);
            ship.transform.position = SpawnPoint + ShipSpawnOffset;
            BuildCamera(blob.transform);
            BuildGame(blob.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[BounJam] Sandbox level built.");
        }

        /// <summary>Regenerates the Blob prefab from scratch with code defaults. Inspector tuning on it is lost.</summary>
        [MenuItem("BounJam/Rebuild Blob Prefab")]
        public static void RebuildBlobPrefab()
        {
            BuildBlobPrefab(CreatePalette().Blob);
            AssetDatabase.SaveAssets();
            Debug.Log("[BounJam] Blob prefab rebuilt.");
        }

        static Palette CreatePalette() => new Palette
        {
            Floor = GridMaterial("Greybox_Floor", new Color(0.62f, 0.63f, 0.66f), new Color(0.50f, 0.51f, 0.54f), 1f),
            Wall = GridMaterial("Greybox_Wall", new Color(0.42f, 0.44f, 0.50f), new Color(0.35f, 0.37f, 0.42f), 1f),
            Structure = GridMaterial("Greybox_Structure", new Color(0.55f, 0.60f, 0.70f), new Color(0.45f, 0.50f, 0.60f), 1f),
            Obstacle = GridMaterial("Greybox_Obstacle", new Color(0.72f, 0.64f, 0.56f), new Color(0.60f, 0.53f, 0.46f), 0.5f),
            Crate = GridMaterial("Greybox_Crate", new Color(0.92f, 0.56f, 0.22f), new Color(0.70f, 0.40f, 0.14f), 0.5f),
            Grate = GridMaterial("Greybox_Grate", new Color(0.30f, 0.55f, 0.95f), new Color(0.12f, 0.25f, 0.55f), 0.2f),
            // The jelly shader's property defaults are the tuned look; the material only needs creating.
            Blob = LoadOrCreate($"{MaterialsFolder}/Blob_Jelly.mat", () => new Material(Shader.Find("BounJam/Jelly"))),
            Paper = LoadOrCreate($"{MaterialsFolder}/PaperShip_Paper.mat", () =>
            {
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor", new Color(0.93f, 0.91f, 0.84f));
                material.SetFloat("_Smoothness", 0.15f);
                return material;
            }),
        };

        static void BuildLevel(Palette m)
        {
            var level = new GameObject("Level").transform;

            // Arena: 36 x 36 m floor, top surface at y = 0, low walls all around.
            Transform arena = Group("Arena", level);
            Box("Floor", arena, new Vector3(0f, -0.5f, 0f), new Vector3(36f, 1f, 36f), m.Floor);
            Box("Wall North", arena, new Vector3(0f, 0.75f, 18.5f), new Vector3(38f, 1.5f, 1f), m.Wall);
            Box("Wall South", arena, new Vector3(0f, 0.75f, -18.5f), new Vector3(38f, 1.5f, 1f), m.Wall);
            Box("Wall East", arena, new Vector3(18.5f, 0.75f, 0f), new Vector3(1f, 1.5f, 36f), m.Wall);
            Box("Wall West", arena, new Vector3(-18.5f, 0.75f, 0f), new Vector3(1f, 1.5f, 36f), m.Wall);

            // 15 degree ramp up to a 2 m platform with open edges to fall off.
            Transform ramp = Group("Ramp And Platform", level);
            const float rampAngle = 15f, platformHeight = 2f, rampThickness = 0.4f, platformFrontZ = 5f;
            float rampLength = platformHeight / Mathf.Sin(rampAngle * Mathf.Deg2Rad);
            float rampRun = platformHeight / Mathf.Tan(rampAngle * Mathf.Deg2Rad);
            float rampCenterY = platformHeight * 0.5f - rampThickness * 0.5f / Mathf.Cos(rampAngle * Mathf.Deg2Rad);
            Box("Platform", ramp, new Vector3(11f, platformHeight * 0.5f, 9f), new Vector3(10f, platformHeight, 8f), m.Structure);
            Box("Ramp", ramp, new Vector3(11f, rampCenterY, platformFrontZ - rampRun * 0.5f), new Vector3(4f, rampThickness, rampLength), m.Structure,
                euler: new Vector3(-rampAngle, 0f, 0f));

            // Steps rising 0.15 m each, then a landing: shows which step heights the blob climbs.
            Transform steps = Group("Steps", level);
            for (int i = 0; i < 4; i++)
            {
                float height = 0.15f * (i + 1);
                Box($"Step {height:0.00}m", steps, new Vector3(-11f, height * 0.5f, -2f + i * 1.5f), new Vector3(4f, height, 1.5f), m.Structure);
            }
            Box("Landing", steps, new Vector3(-11f, 0.3f, 5f), new Vector3(4f, 0.6f, 3f), m.Structure);

            // Low tunnel, 0.65 m clearance: the liquid has to flatten to pass (Stage 2).
            Transform tunnel = Group("Low Tunnel", level);
            Box("Tunnel Wall Left", tunnel, new Vector3(-1.75f, 0.6f, 11f), new Vector3(0.5f, 1.2f, 6f), m.Wall);
            Box("Tunnel Wall Right", tunnel, new Vector3(1.75f, 0.6f, 11f), new Vector3(0.5f, 1.2f, 6f), m.Wall);
            Box("Tunnel Ceiling", tunnel, new Vector3(0f, 0.8f, 11f), new Vector3(4f, 0.3f, 6f), m.Structure);

            // Narrow gap, 0.7 m wide: the liquid has to squeeze through sideways (Stage 2).
            Transform gap = Group("Narrow Gap", level);
            Box("Gap Wall Left", gap, new Vector3(-14.925f, 0.75f, 10f), new Vector3(5.15f, 1.5f, 0.5f), m.Wall);
            Box("Gap Wall Right", gap, new Vector3(-8.825f, 0.75f, 10f), new Vector3(5.65f, 1.5f, 0.5f), m.Wall);

            // Pillars to wrap around and low bumps to flow over.
            Transform obstacles = Group("Pillars And Bumps", level);
            Shape(PrimitiveType.Cylinder, "Pillar Wide", obstacles, new Vector3(-4f, 1f, 4f), new Vector3(1f, 1f, 1f), m.Obstacle);
            Shape(PrimitiveType.Cylinder, "Pillar Thin", obstacles, new Vector3(-2f, 1f, 5.5f), new Vector3(0.6f, 1f, 0.6f), m.Obstacle);
            Shape(PrimitiveType.Sphere, "Bump A", obstacles, new Vector3(3f, -0.4f, -1f), Vector3.one * 1.4f, m.Obstacle);
            Shape(PrimitiveType.Sphere, "Bump B", obstacles, new Vector3(4.5f, -0.45f, 0.2f), Vector3.one * 1.2f, m.Obstacle);
            Shape(PrimitiveType.Sphere, "Bump C", obstacles, new Vector3(3.4f, -0.4f, 1.4f), Vector3.one * 1.0f, m.Obstacle);
            Shape(PrimitiveType.Sphere, "Bump D", obstacles, new Vector3(5f, -0.5f, -1.8f), Vector3.one * 1.6f, m.Obstacle);

            // Grate in a wall: liquid flows through (LiquidPassable layer), the solid form and the ship don't.
            Transform grate = Group("Grate Wall", level);
            Box("Grate Wall Left", grate, new Vector3(5f, 0.75f, -8f), new Vector3(6f, 1.5f, 0.5f), m.Wall);
            Box("Grate Wall Right", grate, new Vector3(15.1f, 0.75f, -8f), new Vector3(5.8f, 1.5f, 0.5f), m.Wall);
            Box("Grate", grate, new Vector3(10f, 0.75f, -8f), new Vector3(4.4f, 1.5f, 0.15f), m.Grate, "LiquidPassable");

            // Pushable crates: heavy enough that only the solid form should move them (Stage 6).
            Transform crates = Group("Crates", level);
            Crate("Crate Big", crates, new Vector3(-5f, 0.5f, -6f), 1f, 20f, m.Crate);
            Crate("Crate Small", crates, new Vector3(-6.6f, 0.4f, -6f), 0.8f, 10f, m.Crate);
        }

        static GameObject BuildBlobPrefab(Material material)
        {
            PhysicsMaterial liquidPhysics = LoadOrCreate($"{MaterialsFolder}/Blob_Liquid_Physics.asset", () => new PhysicsMaterial("Blob Liquid")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            });

            var root = new GameObject("Blob") { layer = LayerMask.NameToLayer("PlayerLiquid") };
            var body = root.AddComponent<Rigidbody>();
            body.mass = 1f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints = RigidbodyConstraints.FreezeRotation;

            var core = root.AddComponent<SphereCollider>();
            core.radius = CoreRadius;
            core.sharedMaterial = liquidPhysics;

            var input = root.AddComponent<PlayerInputReader>();
            var motor = root.AddComponent<BlobMotor>();
            var motorSettings = new SerializedObject(motor);
            motorSettings.FindProperty("input").objectReferenceValue = input;
            motorSettings.FindProperty("groundMask").intValue = ~LayerMask.GetMask("PlayerLiquid", "PlayerSolid", "Triggers", "Ignore Raycast");
            motorSettings.ApplyModifiedPropertiesWithoutUndo();

            // The skin mesh is rebuilt every frame in play mode; the saved mesh is only the round rest shape,
            // so the blob is visible in the editor too.
            var skin = new GameObject("Skin") { layer = root.layer };
            skin.transform.SetParent(root.transform, false);
            skin.AddComponent<MeshFilter>().sharedMesh = BuildRestPreviewMesh();
            skin.AddComponent<MeshRenderer>().sharedMaterial = material;

            var skinSim = root.AddComponent<BlobSkin>();
            var skinSettings = new SerializedObject(skinSim);
            skinSettings.FindProperty("skinFilter").objectReferenceValue = skin.GetComponent<MeshFilter>();
            skinSettings.FindProperty("collisionMask").intValue = LayerMask.GetMask("Default", "Environment", "Pushable", "Interactable");
            skinSettings.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, BlobPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>Regenerates the PaperShip prefab from the imported model with code defaults.</summary>
        [MenuItem("BounJam/Rebuild Ship Prefab")]
        public static void RebuildShipPrefab()
        {
            BuildShipPrefab(CreatePalette().Paper);
            AssetDatabase.SaveAssets();
            Debug.Log("[BounJam] Ship prefab rebuilt.");
        }

        static GameObject BuildShipPrefab(Material paper)
        {
            Mesh mesh = null;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ShipModelPath))
                if (asset is Mesh found) mesh = found;
            if (mesh == null) throw new System.InvalidOperationException($"No mesh in {ShipModelPath}; run Tools/Blender/make_paper_ship.py first.");

            var root = new GameObject("PaperShip") { layer = LayerMask.NameToLayer("Ship") };
            var model = new GameObject("Model") { layer = root.layer };
            model.transform.SetParent(root.transform, false);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            model.AddComponent<MeshRenderer>().sharedMaterial = paper;

            // The boat's length should run along Z; turn the model if the export came out sideways.
            Bounds bounds = mesh.bounds;
            if (bounds.size.x > bounds.size.z)
            {
                model.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                bounds.size = new Vector3(bounds.size.z, bounds.size.y, bounds.size.x);
            }
            Debug.Log($"[BounJam] Ship model size {bounds.size.x:0.000} x {bounds.size.y:0.000} x {bounds.size.z:0.000}, bottom {bounds.min.y:0.000}");

            var body = root.AddComponent<Rigidbody>();
            body.mass = 0.25f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.angularDamping = 0.5f;

            // Hull plus a slim sail box, so low ceilings and overhangs can knock the ship off the blob.
            const float hullHeight = 0.14f;
            var hull = root.AddComponent<BoxCollider>();
            hull.center = new Vector3(0f, bounds.min.y + hullHeight * 0.5f, 0f);
            hull.size = new Vector3(bounds.size.x, hullHeight, bounds.size.z * 0.9f);
            float sailHeight = bounds.max.y - (bounds.min.y + hullHeight);
            var sail = root.AddComponent<BoxCollider>();
            sail.center = new Vector3(0f, bounds.min.y + hullHeight + sailHeight * 0.5f, 0f);
            sail.size = new Vector3(0.05f, sailHeight, bounds.size.z * 0.35f);

            root.AddComponent<ShipBuoyancy>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ShipPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static Mesh BuildRestPreviewMesh()
        {
            // Same topology and smoothing as the runtime skin, so the editor preview matches play mode.
            IcoSphere.Build(2, out Vector3[] directions, out int[] triangles);
            var coarse = new Vector3[directions.Length];
            var restCenter = new Vector3(0f, BlobRadius - CoreRadius, 0f);
            for (int i = 0; i < directions.Length; i++) coarse[i] = restCenter + directions[i] * BlobRadius;

            LoopSubdivision smoothing = LoopSubdivision.Build(coarse.Length, triangles, 2);
            var vertices = new Vector3[smoothing.VertexCount];
            smoothing.Apply(coarse, vertices);

            var mesh = new Mesh { name = "Blob Rest Preview", vertices = vertices, triangles = smoothing.Triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, RestPreviewMeshPath);
            return mesh;
        }

        static void BuildCamera(Transform target)
        {
            Camera main = Camera.main;
            if (main == null)
            {
                main = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            }
            if (!main.TryGetComponent(out CinemachineBrain _)) main.gameObject.AddComponent<CinemachineBrain>();

            var rig = new GameObject("CM FollowCam");
            var vcam = rig.AddComponent<CinemachineCamera>();
            vcam.Follow = target;
            vcam.Lens.FieldOfView = CameraFieldOfView;

            // World-space offset and no rotation control: the camera keeps one fixed angle and just slides along.
            var follow = rig.AddComponent<CinemachineFollow>();
            follow.FollowOffset = CameraOffset;
            follow.TrackerSettings.BindingMode = BindingMode.WorldSpace;
            follow.TrackerSettings.PositionDamping = new Vector3(0.4f, 0.3f, 0.4f);

            float pitch = Mathf.Atan2(CameraOffset.y, -CameraOffset.z) * Mathf.Rad2Deg;
            Vector3 position = target.position + CameraOffset;
            Quaternion rotation = Quaternion.Euler(pitch, 0f, 0f);
            rig.transform.SetPositionAndRotation(position, rotation);
            main.transform.SetPositionAndRotation(position, rotation);
            main.fieldOfView = CameraFieldOfView;
        }

        static void BuildGame(Transform player)
        {
            var restarter = new GameObject("Game").AddComponent<LevelRestarter>();
            var settings = new SerializedObject(restarter);
            settings.FindProperty("player").objectReferenceValue = player;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        static Transform Group(string name, Transform parent)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }

        static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material material,
                              string layer = "Environment", Vector3 euler = default)
            => Shape(PrimitiveType.Cube, name, parent, position, size, material, layer, euler);

        static GameObject Shape(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material,
                                string layer = "Environment", Vector3 euler = default)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.layer = LayerMask.NameToLayer(layer);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(euler));
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        static void Crate(string name, Transform parent, Vector3 position, float size, float mass, Material material)
        {
            GameObject crate = Box(name, parent, position, Vector3.one * size, material, "Pushable");
            var body = crate.AddComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        static Material GridMaterial(string name, Color baseColor, Color lineColor, float cellSize)
            => LoadOrCreate($"{MaterialsFolder}/{name}.mat", () =>
            {
                Shader shader = Shader.Find(GridShaderName);
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
                var material = new Material(shader);
                material.SetColor("_BaseColor", baseColor);
                material.SetColor("_LineColor", lineColor);
                material.SetFloat("_CellSize", cellSize);
                return material;
            });

        static T LoadOrCreate<T>(string path, System.Func<T> create) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = create();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
