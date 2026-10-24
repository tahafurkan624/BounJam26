using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Shared building blocks for the level builders: materials, greybox shapes, crates, buttons, movers, hot plates,
    /// water pools, the dock, the HUD, the player and water prefabs and the follow camera. Assets (materials, physics materials, prefabs)
    /// are only created when missing, so tweaks made in the editor survive a rebuild.
    /// </summary>
    public static class LevelKit
    {
        public const string MaterialsFolder = "Assets/_Game/Art/Materials";
        public const string BlobPrefabPath = "Assets/_Game/Prefabs/Blob.prefab";
        public const string ShipPrefabPath = "Assets/_Game/Prefabs/PaperShip.prefab";
        public const string WaterPrefabPath = "Assets/_Game/Prefabs/Water.prefab";
        const string RestPreviewMeshPath = "Assets/_Game/Art/Models/Blob_RestPreview.asset";
        const string VolumeProfilePath = "Assets/Settings/SampleSceneProfile.asset";
        const string GridShaderName = "BounJam/Greybox Grid";

        public const float CoreRadius = 0.3f;
        const float BlobRadius = 0.5f;
        // Dropped from just above the resting blob's flat top so it settles onto the jelly.
        public static readonly Vector3 ShipOnBlobOffset = new Vector3(0f, 0.5f, 0f);
        static readonly Vector3 CameraOffset = new Vector3(0f, 8f, -6.5f);
        const float CameraFieldOfView = 40f;
        public const string CameraGroupName = "CM Blob Group";
        // Room the camera keeps around each piece of the blob, m.
        const float CameraPieceRadius = 1f;

        public struct Palette
        {
            public Material Floor, Wall, Structure, Obstacle, Crate, Grate, Blob;
            public Material Door, Lift, ButtonIdle, ButtonPressed, HotPlate, DockPad, DockPost, Water;
            public PhysicsMaterial CratePhysics;
        }

        public static Palette CreatePalette() => new Palette
        {
            Floor = GridMaterial("Greybox_Floor", new Color(0.62f, 0.63f, 0.66f), new Color(0.50f, 0.51f, 0.54f), 1f),
            Wall = GridMaterial("Greybox_Wall", new Color(0.42f, 0.44f, 0.50f), new Color(0.35f, 0.37f, 0.42f), 1f),
            Structure = GridMaterial("Greybox_Structure", new Color(0.55f, 0.60f, 0.70f), new Color(0.45f, 0.50f, 0.60f), 1f),
            Obstacle = GridMaterial("Greybox_Obstacle", new Color(0.72f, 0.64f, 0.56f), new Color(0.60f, 0.53f, 0.46f), 0.5f),
            Crate = GridMaterial("Greybox_Crate", new Color(0.92f, 0.56f, 0.22f), new Color(0.70f, 0.40f, 0.14f), 0.5f),
            Grate = GridMaterial("Greybox_Grate", new Color(0.30f, 0.55f, 0.95f), new Color(0.12f, 0.25f, 0.55f), 0.2f),
            // The jelly shader's property defaults are the tuned look; the material only needs creating.
            Blob = LoadOrCreate($"{MaterialsFolder}/Blob_Jelly.mat", () => new Material(Shader.Find("BounJam/Jelly"))),
            Door = GridMaterial("Greybox_Door", new Color(0.75f, 0.30f, 0.25f), new Color(0.55f, 0.20f, 0.18f), 0.5f),
            Lift = GridMaterial("Greybox_Lift", new Color(0.95f, 0.80f, 0.30f), new Color(0.75f, 0.60f, 0.20f), 0.5f),
            ButtonIdle = LitMaterial("Button_Idle", new Color(0.90f, 0.30f, 0.20f), 0.4f, Color.black),
            ButtonPressed = LitMaterial("Button_Pressed", new Color(0.35f, 0.90f, 0.40f), 0.4f, new Color(0.15f, 0.60f, 0.20f)),
            HotPlate = LitMaterial("Hazard_HotPlate", new Color(0.90f, 0.25f, 0.10f), 0.6f, new Color(1.2f, 0.30f, 0.05f)),
            DockPad = LitMaterial("Dock_Pad", new Color(0.20f, 0.70f, 1.00f), 0.5f, new Color(0.12f, 0.42f, 0.60f)),
            DockPost = LitMaterial("Dock_Post", new Color(0.85f, 0.95f, 1.00f), 0.5f, new Color(0.50f, 0.80f, 1.00f)),
            // Like the jelly, the water shader's property defaults are the tuned look.
            Water = LoadOrCreate($"{MaterialsFolder}/Water.mat", () => new Material(Shader.Find("BounJam/Water"))),
            // Slick enough that the solid can shove a crate along at a slow, heavy pace. Minimum combine keeps it
            // slick against everything, so a pushing crystal slides along its face instead of rolling up it.
            CratePhysics = LoadOrCreate($"{MaterialsFolder}/Crate_Physics.asset", () => new PhysicsMaterial("Crate")
            {
                dynamicFriction = 0.3f,
                staticFriction = 0.35f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            }),
        };

        // ---------------------------------------------------------------- Shapes

        public static Transform Group(string name, Transform parent)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }

        public static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material material,
                                     string layer = "Environment", Vector3 euler = default)
            => Shape(PrimitiveType.Cube, name, parent, position, size, material, layer, euler);

        public static GameObject Shape(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material,
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

        /// <summary>
        /// A floor slab whose top surface is at <paramref name="top"/>; 1 m thick, covered in platform blocks
        /// (<paramref name="material"/> only if the platform model is missing).
        /// </summary>
        public static GameObject Floor(string name, Transform parent, float x, float z, float width, float depth, float top, Material material)
            => ArtKit.Platform(name, parent, new Vector3(x, top - 0.5f, z), new Vector3(width, 1f, depth), material);

        /// <summary>
        /// A ramp rising toward +Z from (x, bottom, startZ) by <paramref name="rise"/> metres at <paramref name="angle"/> degrees.
        /// Returns the Z where it reaches the top.
        /// </summary>
        public static float Ramp(string name, Transform parent, float x, float startZ, float bottom, float rise, float width, float angle, Material material)
        {
            const float thickness = 0.4f;
            float length = rise / Mathf.Sin(angle * Mathf.Deg2Rad);
            float run = rise / Mathf.Tan(angle * Mathf.Deg2Rad);
            float centerY = bottom + rise * 0.5f - thickness * 0.5f / Mathf.Cos(angle * Mathf.Deg2Rad);
            Box(name, parent, new Vector3(x, centerY, startZ + run * 0.5f), new Vector3(width, thickness, length), material,
                euler: new Vector3(-angle, 0f, 0f));
            return startZ + run;
        }

        // ---------------------------------------------------------------- Gameplay pieces

        public static void Crate(string name, Transform parent, Vector3 position, float size, float mass, Palette m)
        {
            var crate = new GameObject(name) { layer = LayerMask.NameToLayer("Pushable") };
            crate.transform.SetParent(parent, false);
            crate.transform.position = position;
            crate.transform.localScale = Vector3.one * size;
            crate.AddComponent<BoxCollider>().sharedMaterial = m.CratePhysics;
            ArtKit.PushBlock(crate.transform);
            var body = crate.AddComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            crate.AddComponent<Pushable>();
        }

        public static PressureButton Button(string name, Transform parent, Vector3 position, Palette m)
        {
            var root = new GameObject(name) { layer = LayerMask.NameToLayer("Interactable") };
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            var button = root.AddComponent<PressureButton>();
            // The modelled ring, cap and light ring; visual only, no collider, so nothing snags on its edge.
            ArtKit.DressButton(button, m.ButtonIdle);
            var settings = new SerializedObject(button);
            settings.FindProperty("idleMaterial").objectReferenceValue = m.ButtonIdle;
            settings.FindProperty("pressedMaterial").objectReferenceValue = m.ButtonPressed;
            settings.FindProperty("detectMask").intValue = LayerMask.GetMask("PlayerLiquid", "PlayerSolid", "Pushable", "Ship");
            settings.ApplyModifiedPropertiesWithoutUndo();
            return button;
        }

        /// <summary>
        /// A door, gate or lift driven by buttons. Lifts (<paramref name="carriesRiders"/>) carry what stands on them;
        /// doors stop for anything in their way. Neither ever crushes the blob, the ship or a crate.
        /// </summary>
        public static ButtonMover Mover(string name, Transform parent, Vector3 position, Vector3 size, Material material,
                                        PressureButton[] buttons, Vector3 activeOffset, float speed, float holdTime,
                                        bool cycle = false, bool carriesRiders = false)
        {
            GameObject go = Box(name, parent, position, size, material);
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            var mover = go.AddComponent<ButtonMover>();
            var settings = new SerializedObject(mover);
            SerializedProperty list = settings.FindProperty("buttons");
            list.arraySize = buttons.Length;
            for (int i = 0; i < buttons.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
            settings.FindProperty("activeOffset").vector3Value = activeOffset;
            settings.FindProperty("speed").floatValue = speed;
            settings.FindProperty("holdTime").floatValue = holdTime;
            settings.FindProperty("cycleWhileActive").boolValue = cycle;
            settings.FindProperty("carriesRiders").boolValue = carriesRiders;
            settings.FindProperty("blockingMask").intValue = LayerMask.GetMask("PlayerLiquid", "PlayerSolid", "Ship", "Pushable");
            settings.ApplyModifiedPropertiesWithoutUndo();
            return mover;
        }

        /// <summary>
        /// A glowing plate that burns the paper ship. The blob crosses it safely; dropping the ship on it loses the level.
        /// <paramref name="center"/> is on the floor surface.
        /// </summary>
        public static void HotPlate(string name, Transform parent, Vector3 center, Vector2 size, Palette m)
        {
            const float plateHeight = 0.02f, heatHeight = 0.25f;
            Transform root = Group(name, parent);
            Box("Plate", root, center + Vector3.up * (plateHeight * 0.5f), new Vector3(size.x, plateHeight, size.y), m.HotPlate);

            var heat = new GameObject("Heat") { layer = LayerMask.NameToLayer("Triggers") };
            heat.transform.SetParent(root, false);
            heat.transform.position = center + Vector3.up * (plateHeight + heatHeight * 0.5f);
            var trigger = heat.AddComponent<BoxCollider>();
            trigger.size = new Vector3(size.x, heatHeight, size.y);
            trigger.isTrigger = true;
            heat.AddComponent<HazardZone>();
        }

        /// <summary>
        /// A floor slab like <see cref="Floor"/> (<paramref name="floor"/> is its x/z footprint) with a pool sunk into
        /// it (<paramref name="pool"/>, inside the floor): a basin <paramref name="depth"/> deep, filled with water up to
        /// <paramref name="waterBelowFloor"/> under the floor top, so the liquid can roll right up to the edge and only
        /// falls in past it.
        /// </summary>
        public static Water FloorWithPool(string name, Transform parent, Rect floor, Rect pool, float top, float depth,
                                          float waterBelowFloor, Palette m)
        {
            Transform root = Group(name, parent);
            // Four slabs around the hole: full-width strips south and north, short pieces west and east.
            FloorSlab("Floor South", root, floor.xMin, floor.xMax, floor.yMin, pool.yMin, top, m.Floor);
            FloorSlab("Floor North", root, floor.xMin, floor.xMax, pool.yMax, floor.yMax, top, m.Floor);
            FloorSlab("Floor West", root, floor.xMin, pool.xMin, pool.yMin, pool.yMax, top, m.Floor);
            FloorSlab("Floor East", root, pool.xMax, floor.xMax, pool.yMin, pool.yMax, top, m.Floor);

            // The basin: a bottom under the hole and, below the floor slab, walls closing it in.
            float bottom = top - depth;
            Slab("Pool Bottom", root, pool.xMin, pool.xMax, pool.yMin, pool.yMax, bottom, 1f, m.Structure);
            if (depth > 1f)
            {
                const float wall = 0.5f;
                float wallTop = top - 1f, wallHeight = depth - 1f;
                Slab("Pool Wall South", root, pool.xMin - wall, pool.xMax + wall, pool.yMin - wall, pool.yMin, wallTop, wallHeight, m.Structure);
                Slab("Pool Wall North", root, pool.xMin - wall, pool.xMax + wall, pool.yMax, pool.yMax + wall, wallTop, wallHeight, m.Structure);
                Slab("Pool Wall West", root, pool.xMin - wall, pool.xMin, pool.yMin, pool.yMax, wallTop, wallHeight, m.Structure);
                Slab("Pool Wall East", root, pool.xMax, pool.xMax + wall, pool.yMin, pool.yMax, wallTop, wallHeight, m.Structure);
            }

            float surface = top - waterBelowFloor;
            return WaterVolume("Water", root, new Vector3(pool.center.x, (bottom + surface) * 0.5f, pool.center.y),
                               new Vector3(pool.width, surface - bottom, pool.height), m);
        }

        /// <summary>Like <see cref="Floor"/>, spanning x0..x1 and z0..z1. Empty spans are skipped.</summary>
        static void FloorSlab(string name, Transform parent, float x0, float x1, float z0, float z1, float top, Material material)
        {
            if (x1 - x0 < 0.001f || z1 - z0 < 0.001f) return;
            Floor(name, parent, (x0 + x1) * 0.5f, (z0 + z1) * 0.5f, x1 - x0, z1 - z0, top, material);
        }

        /// <summary>A box spanning x0..x1 and z0..z1, <paramref name="thickness"/> thick under <paramref name="top"/>. Empty spans are skipped.</summary>
        static void Slab(string name, Transform parent, float x0, float x1, float z0, float z1, float top, float thickness, Material material)
        {
            if (x1 - x0 < 0.001f || z1 - z0 < 0.001f) return;
            Box(name, parent, new Vector3((x0 + x1) * 0.5f, top - thickness * 0.5f, (z0 + z1) * 0.5f),
                new Vector3(x1 - x0, thickness, z1 - z0), material);
        }

        /// <summary>A body of water filling the box at <paramref name="center"/> of <paramref name="size"/>; its surface is the box top.</summary>
        public static Water WaterVolume(string name, Transform parent, Vector3 center, Vector3 size, Palette m)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WaterPrefabPath);
            if (prefab == null) prefab = BuildWaterPrefab(m.Water);
            var water = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            water.name = name;
            water.transform.SetPositionAndRotation(center, Quaternion.identity);
            water.transform.localScale = size;
            return water.GetComponent<Water>();
        }

        /// <summary>The goal: a glowing pad with corner posts and a beacon. <paramref name="position"/> is on the floor surface.</summary>
        public static ShipDock Dock(string name, Transform parent, Vector3 position, Palette m, ShipBuoyancy ship)
        {
            const float size = 2.4f;
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            GameObject pad = Decoration(PrimitiveType.Cube, "Pad", root.transform, new Vector3(0f, 0.015f, 0f),
                                        new Vector3(size, 0.03f, size), m.DockPad);
            float corner = size * 0.5f;
            foreach (Vector2 c in new[] { new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(-1, -1) })
                Decoration(PrimitiveType.Cylinder, "Post", root.transform, new Vector3(c.x * corner, 0.25f, c.y * corner),
                           new Vector3(0.12f, 0.25f, 0.12f), m.DockPost);
            Decoration(PrimitiveType.Cylinder, "Beacon", root.transform, new Vector3(0f, 2f, corner),
                       new Vector3(0.06f, 2f, 0.06f), m.DockPost);

            var dock = root.AddComponent<ShipDock>();
            var settings = new SerializedObject(dock);
            settings.FindProperty("ship").objectReferenceValue = ship;
            settings.FindProperty("padRenderer").objectReferenceValue = pad.GetComponent<MeshRenderer>();
            settings.FindProperty("zoneSize").vector3Value = new Vector3(size, 2f, size);
            settings.ApplyModifiedPropertiesWithoutUndo();
            return dock;
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

        // ---------------------------------------------------------------- Player, camera, level logic

        /// <summary>Places the blob at <paramref name="blobSpawn"/> (on the floor) and the ship at <paramref name="shipSpawn"/>.</summary>
        public static (GameObject blob, ShipBuoyancy ship) PlacePlayer(Palette m, Vector3 blobSpawn, Vector3 shipSpawn)
        {
            // Reuse existing prefabs so tuning done in the Inspector survives a level rebuild.
            GameObject blobPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlobPrefabPath);
            if (blobPrefab == null) blobPrefab = BuildBlobPrefab(m.Blob);
            else AddMissingBlobParts();
            var blob = (GameObject)PrefabUtility.InstantiatePrefab(blobPrefab);
            blob.transform.position = blobSpawn + Vector3.up * CoreRadius;

            GameObject shipPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShipPrefabPath);
            if (shipPrefab == null) shipPrefab = BuildShipPrefab();
            var ship = (GameObject)PrefabUtility.InstantiatePrefab(shipPrefab);
            ship.transform.position = shipSpawn;
            return (blob, ship.GetComponent<ShipBuoyancy>());
        }

        public static void BuildCamera(Transform target)
        {
            Camera main = Camera.main;
            if (main == null) main = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            if (!main.TryGetComponent(out CinemachineBrain _)) main.gameObject.AddComponent<CinemachineBrain>();
            main.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            // The camera follows the blob's pieces as a group: the whole blob, or both halves once a piercer pipe has
            // cut it, kept in step with them by BlobCameraGroup.
            var pieces = new GameObject(CameraGroupName);
            pieces.transform.position = target.position;
            var group = pieces.AddComponent<CinemachineTargetGroup>();
            group.AddMember(target, 1f, CameraPieceRadius);
            pieces.AddComponent<BlobCameraGroup>();

            var rig = new GameObject("CM FollowCam");
            var vcam = rig.AddComponent<CinemachineCamera>();
            vcam.Follow = group.transform;
            vcam.Lens.FieldOfView = CameraFieldOfView;

            // World-space offset and no rotation control: the camera keeps one fixed angle and just slides along.
            var follow = rig.AddComponent<CinemachineFollow>();
            follow.FollowOffset = CameraOffset;
            follow.TrackerSettings.BindingMode = BindingMode.WorldSpace;
            follow.TrackerSettings.PositionDamping = new Vector3(0.4f, 0.3f, 0.4f);

            // Pulls back along the view when the halves are far apart, never in closer than the usual offset
            // (despite its tooltip, a negative dolly moves the camera back).
            var framing = rig.AddComponent<CinemachineGroupFraming>();
            framing.SizeAdjustment = CinemachineGroupFraming.SizeAdjustmentModes.DollyOnly;
            framing.DollyRange = new Vector2(-15f, 0f);
            framing.FramingSize = 0.7f;
            framing.Damping = 0.5f;

            float pitch = Mathf.Atan2(CameraOffset.y, -CameraOffset.z) * Mathf.Rad2Deg;
            Vector3 position = target.position + CameraOffset;
            Quaternion rotation = Quaternion.Euler(pitch, 0f, 0f);
            rig.transform.SetPositionAndRotation(position, rotation);
            main.transform.SetPositionAndRotation(position, rotation);
            main.fieldOfView = CameraFieldOfView;
        }

        /// <summary>The "Game" object: level logic and HUD.</summary>
        public static LevelManager BuildGame(string title, string nextScene, bool isFinalLevel, GameObject blob, ShipBuoyancy ship, ShipDock dock)
        {
            var game = new GameObject("Game");
            LevelHud hud = BuildHud(game.transform);
            var level = game.AddComponent<LevelManager>();
            var settings = new SerializedObject(level);
            settings.FindProperty("levelTitle").stringValue = title;
            settings.FindProperty("nextScene").stringValue = nextScene ?? "";
            settings.FindProperty("isFinalLevel").boolValue = isFinalLevel;
            settings.FindProperty("blob").objectReferenceValue = blob.transform;
            settings.FindProperty("ship").objectReferenceValue = ship;
            settings.FindProperty("dock").objectReferenceValue = dock;
            settings.FindProperty("hud").objectReferenceValue = hud;
            settings.ApplyModifiedPropertiesWithoutUndo();
            return level;
        }

        static LevelHud BuildHud(Transform parent)
        {
            var canvasObject = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(parent, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Text title = HudText("Title", canvasObject.transform, new Vector2(0.5f, 1f), new Vector2(0f, -140f), 60);
            Text message = HudText("Message", canvasObject.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 80f), 84);

            var hud = canvasObject.AddComponent<LevelHud>();
            var settings = new SerializedObject(hud);
            settings.FindProperty("title").objectReferenceValue = title;
            settings.FindProperty("message").objectReferenceValue = message;
            settings.ApplyModifiedPropertiesWithoutUndo();
            return hud;
        }

        static Text HudText(string name, Transform canvas, Vector2 anchor, Vector2 position, int size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Shadow));
            go.transform.SetParent(canvas, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(1600f, 300f);

            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            go.GetComponent<Shadow>().effectDistance = new Vector2(3f, -3f);
            return text;
        }

        /// <summary>Lighting and post-processing for a freshly created scene, matching the sandbox.</summary>
        public static void SetupEnvironment()
        {
            Light sun = Object.FindAnyObjectByType<Light>();
            if (sun == null) sun = new GameObject("Directional Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 2f;
            sun.color = Color.white;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var volume = new GameObject("Global Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);

            // Courses float over the void: a soft sky colour behind them reads cleaner than the skybox's ground.
            Camera main = Camera.main;
            if (main != null)
            {
                main.clearFlags = CameraClearFlags.SolidColor;
                main.backgroundColor = new Color(0.58f, 0.72f, 0.86f);
            }
        }

        // ---------------------------------------------------------------- Prefabs

        /// <summary>Regenerates the Blob prefab from scratch with code defaults. Inspector tuning on it is lost.</summary>
        [MenuItem("BounJam/Rebuild Blob Prefab")]
        public static void RebuildBlobPrefab()
        {
            BuildBlobPrefab(CreatePalette().Blob);
            AssetDatabase.SaveAssets();
            Debug.Log("[BounJam] Blob prefab rebuilt.");
        }

        /// <summary>Regenerates the PaperShip prefab from the imported model with code defaults.</summary>
        [MenuItem("BounJam/Rebuild Ship Prefab")]
        public static void RebuildShipPrefab()
        {
            BuildShipPrefab();
            AssetDatabase.SaveAssets();
            Debug.Log("[BounJam] Ship prefab rebuilt.");
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

            // The skin mesh is rebuilt every frame in play mode; the saved mesh is only the rest shape (set with the
            // eyes by ArtKit.AddBlobArt), so the blob is visible in the editor too.
            var skin = new GameObject("Skin") { layer = root.layer };
            skin.transform.SetParent(root.transform, false);
            skin.AddComponent<MeshFilter>();
            skin.AddComponent<MeshRenderer>().sharedMaterial = material;

            var skinSim = root.AddComponent<BlobSkin>();
            var skinSettings = new SerializedObject(skinSim);
            skinSettings.FindProperty("skinFilter").objectReferenceValue = skin.GetComponent<MeshFilter>();
            skinSettings.FindProperty("collisionMask").intValue = LayerMask.GetMask("Default", "Environment", "Pushable", "Interactable");
            skinSettings.ApplyModifiedPropertiesWithoutUndo();
            ArtKit.AddBlobArt(root);

            // Solid form: a convex collider that takes the frozen shape, with real friction.
            PhysicsMaterial solidPhysics = LoadOrCreate($"{MaterialsFolder}/Blob_Solid_Physics.asset", () => new PhysicsMaterial("Blob Solid")
            {
                dynamicFriction = 0.4f,
                staticFriction = 0.5f,
                bounciness = 0.05f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Average,
            });
            var solidCollider = root.AddComponent<MeshCollider>();
            solidCollider.convex = true;
            solidCollider.sharedMaterial = solidPhysics;
            solidCollider.enabled = false;

            var form = root.AddComponent<BlobForm>();
            var formSettings = new SerializedObject(form);
            formSettings.FindProperty("input").objectReferenceValue = input;
            formSettings.FindProperty("liquidCore").objectReferenceValue = core;
            formSettings.FindProperty("solidCollider").objectReferenceValue = solidCollider;
            formSettings.FindProperty("skinRenderer").objectReferenceValue = skin.GetComponent<MeshRenderer>();
            formSettings.ApplyModifiedPropertiesWithoutUndo();
            AddBlobWater(root);
            MagnetKit.AddBlobParts(root);
            PipeKit.AddBlobParts(root);
            AddGroundGuard(root);
            SplashKit.AddBlobParts(root);
            DropletKit.AddBlobParts(root);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, BlobPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>Adds components newer than the saved Blob prefab to it, leaving all its tuning as it is.</summary>
        static void AddMissingBlobParts()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(BlobPrefabPath);
            if (!root.TryGetComponent(out BlobWater _))
            {
                AddBlobWater(root);
                PrefabUtility.SaveAsPrefabAsset(root, BlobPrefabPath);
                Debug.Log("[BounJam] Blob prefab: added water handling.");
            }
            if (!root.TryGetComponent(out BlobEyes _))
            {
                ArtKit.AddBlobArt(root);
                PrefabUtility.SaveAsPrefabAsset(root, BlobPrefabPath);
                Debug.Log("[BounJam] Blob prefab: added the modelled body and eyes.");
            }
            PrefabUtility.UnloadPrefabContents(root);
        }

        static void AddBlobWater(GameObject root)
        {
            var water = root.AddComponent<BlobWater>();
            var settings = new SerializedObject(water);
            settings.FindProperty("skinRenderer").objectReferenceValue = root.GetComponentInChildren<MeshRenderer>();
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>A unit cube of water, no collider and no shadow; levels scale it to fill pools.</summary>
        static GameObject BuildWaterPrefab(Material material)
        {
            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "Water";
            root.layer = LayerMask.NameToLayer("Water");
            Object.DestroyImmediate(root.GetComponent<Collider>());
            var renderer = root.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            root.AddComponent<Water>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, WaterPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static GameObject BuildShipPrefab()
        {
            Mesh mesh = ArtKit.ShipMesh();
            Mesh hullShape = ArtKit.ShipHullMesh();

            var root = new GameObject("PaperShip") { layer = LayerMask.NameToLayer("Ship") };
            // The tugboat is modelled bow to -Z; turned round, it heads into the level.
            var model = new GameObject("Model") { layer = root.layer };
            model.transform.SetParent(root.transform, false);
            model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            model.AddComponent<MeshRenderer>().sharedMaterial = ArtKit.ShipMaterial();

            var body = root.AddComponent<Rigidbody>();
            body.mass = 0.25f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.angularDamping = 0.5f;

            // The modelled hull and cabin (turned with the model) plus a box round the funnel and horn above them, so
            // low ceilings and overhangs can knock the ship off the blob.
            var hull = model.AddComponent<MeshCollider>();
            hull.sharedMesh = hullShape;
            hull.convex = true;
            float cabinTop = hullShape.bounds.max.y;
            Bounds funnel = default;
            bool found = false;
            foreach (Vector3 vertex in mesh.vertices)
            {
                if (vertex.y <= cabinTop) continue;
                Vector3 point = model.transform.localRotation * vertex;
                if (found) funnel.Encapsulate(point);
                else funnel = new Bounds(point, Vector3.zero);
                found = true;
            }
            var funnelBox = root.AddComponent<BoxCollider>();
            funnelBox.center = funnel.center;
            funnelBox.size = funnel.size;

            root.AddComponent<ShipBuoyancy>();
            AddGroundGuard(root);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ShipPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>Never let this body end up inside or under the floor it stands on (static level and moving platforms).</summary>
        static void AddGroundGuard(GameObject root)
        {
            var guard = root.AddComponent<GroundGuard>();
            var settings = new SerializedObject(guard);
            settings.FindProperty("groundMask").intValue = LayerMask.GetMask("Default", "Environment");
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>The skin at rest on the floor, for the editor; <paramref name="restShape"/> as on <see cref="BlobSkin"/>.</summary>
        internal static Mesh BuildRestPreviewMesh(Mesh restShape)
        {
            // Same topology, rest shape and smoothing as the runtime skin, so the editor preview matches play mode.
            IcoSphere.Build(2, out Vector3[] directions, out int[] triangles);
            Vector3[] coarse = BlobSkin.RestOffsets(restShape, directions, BlobRadius, out float halfHeight);
            var restCenter = new Vector3(0f, halfHeight - CoreRadius, 0f);
            for (int i = 0; i < coarse.Length; i++) coarse[i] += restCenter;

            LoopSubdivision smoothing = LoopSubdivision.Build(coarse.Length, triangles, 2);
            var vertices = new Vector3[smoothing.VertexCount];
            smoothing.Apply(coarse, vertices);

            // Updated in place, so the prefab's reference to it holds.
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(RestPreviewMeshPath);
            bool created = mesh == null;
            if (created) mesh = new Mesh { name = "Blob Rest Preview" };
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = smoothing.Triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (created) AssetDatabase.CreateAsset(mesh, RestPreviewMeshPath);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // ---------------------------------------------------------------- Assets

        public static Material LitMaterial(string name, Color color, float smoothness, Color emission)
            => LoadOrCreate($"{MaterialsFolder}/{name}.mat", () =>
            {
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor", color);
                material.SetFloat("_Smoothness", smoothness);
                if (emission != Color.black)
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", emission);
                }
                return material;
            });

        public static Material GridMaterial(string name, Color baseColor, Color lineColor, float cellSize)
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

        public static T LoadOrCreate<T>(string path, System.Func<T> create) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = create();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
