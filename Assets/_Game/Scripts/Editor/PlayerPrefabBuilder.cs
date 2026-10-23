using BounJam.Core;
using BounJam.Liquids;
using BounJam.Player;
using BounJam.UI;
using TMPro;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Generates PlayerRig.prefab (stand-in beaver with its liquid gun, cameras and HUD) and
    /// PlayerSpawn.prefab. Rebuilding overwrites any hand edits made to those two prefabs.
    /// </summary>
    static class PlayerPrefabBuilder
    {
        public const string RigPath = "Assets/_Game/Prefabs/Player/PlayerRig.prefab";
        public const string SpawnPath = "Assets/_Game/Prefabs/Level/PlayerSpawn.prefab";

        // Starting stock of each limited liquid, so switching and the HUD can be tried before pickups exist.
        const float TestingStartAmount = 5f;

        [MenuItem("BounJam/Setup/Rebuild Player Prefabs")]
        internal static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[BounJam] Exit Play Mode before rebuilding the player prefabs.");
                return;
            }
            if (!ProjectSetup.TextMeshProReady)
            {
                ProjectSetup.ImportTextMeshProEssentials();
                Debug.LogWarning("[BounJam] Imported TextMeshPro essentials for the HUD. Rebuild the player prefabs once the import finishes.");
                return;
            }
            LiquidType[] liquids = LiquidPrefabBuilder.EnsureLiquidTypes();
            LiquidStream streamPrefab = LiquidPrefabBuilder.BuildStreamPrefab();

            var rig = new GameObject("PlayerRig");
            GameObject rigAsset;
            try
            {
                BuildRig(rig, liquids, streamPrefab);
                rigAsset = PrefabUtility.SaveAsPrefabAsset(rig, RigPath);
            }
            finally
            {
                Object.DestroyImmediate(rig);
            }

            var spawn = new GameObject("PlayerSpawn", typeof(PlayerSpawn));
            try
            {
                BuilderUtility.SetField(spawn.GetComponent<PlayerSpawn>(), "playerRig", rigAsset);
                PrefabUtility.SaveAsPrefabAsset(spawn, SpawnPath);
            }
            finally
            {
                Object.DestroyImmediate(spawn);
            }
            Debug.Log("[BounJam] Built player prefabs.");
        }

        static void BuildRig(GameObject rig, LiquidType[] liquids, LiquidStream streamPrefab)
        {
            GameObject beaver = BuildBeaver(rig.transform, out Transform cameraPivot);
            LiquidInventory inventory = AddLiquidGun(beaver, liquids, streamPrefab);

            var mainCamera = new GameObject("Main Camera", typeof(Camera), typeof(UniversalAdditionalCameraData),
                typeof(AudioListener), typeof(CinemachineBrain));
            mainCamera.tag = "MainCamera";
            mainCamera.transform.SetParent(rig.transform, false);
            mainCamera.GetComponent<Camera>().nearClipPlane = 0.1f;
            BuilderUtility.SetField(beaver.GetComponent<PlayerAim>(), "viewCamera", mainCamera.GetComponent<Camera>());

            CinemachineOrbitalFollow orbit = BuildFreeLookCamera(rig.transform, cameraPivot);
            BuilderUtility.SetField(beaver.GetComponent<PlayerLook>(), "orbit", orbit);

            BuildHud(rig.transform, inventory, beaver.GetComponent<TreeEater>());
        }

        // Orbits the pivot in world space (PlayerLook drives the angles) and keeps the beaver centred,
        // slightly below the crosshair so the crosshair is not hidden behind it.
        static CinemachineOrbitalFollow BuildFreeLookCamera(Transform parent, Transform cameraPivot)
        {
            var followCamera = new GameObject("Free Look Camera", typeof(CinemachineCamera),
                typeof(CinemachineOrbitalFollow), typeof(CinemachineRotationComposer), typeof(CinemachineDeoccluder));
            followCamera.transform.SetParent(parent, false);

            var cinemachineCamera = followCamera.GetComponent<CinemachineCamera>();
            cinemachineCamera.Follow = cameraPivot;
            cinemachineCamera.Lens.FieldOfView = 55f;

            var orbit = followCamera.GetComponent<CinemachineOrbitalFollow>();
            orbit.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
            orbit.Radius = 6f;
            orbit.TrackerSettings.BindingMode = BindingMode.WorldSpace;
            orbit.TrackerSettings.PositionDamping = new Vector3(0.3f, 0.3f, 0.3f);
            orbit.VerticalAxis.Range = new Vector2(-15f, 65f);
            orbit.VerticalAxis.Value = orbit.VerticalAxis.Center = 20f;

            var composer = followCamera.GetComponent<CinemachineRotationComposer>();
            composer.TargetOffset = new Vector3(0f, 0.5f, 0f);
            composer.Damping = Vector2.zero;

            var deoccluder = followCamera.GetComponent<CinemachineDeoccluder>();
            deoccluder.CollideAgainst = GameLayers.MaskOf(GameLayers.Default, GameLayers.Ground, GameLayers.Tree);
            deoccluder.MinimumDistanceFromTarget = 0.5f;
            deoccluder.AvoidObstacles.Enabled = true;
            deoccluder.AvoidObstacles.CameraRadius = 0.2f;
            deoccluder.AvoidObstacles.Strategy = CinemachineDeoccluder.ObstacleAvoidance.ResolutionStrategy.PullCameraForward;
            deoccluder.AvoidObstacles.Damping = 0.2f;
            deoccluder.AvoidObstacles.DampingWhenOccluded = 0f;
            return orbit;
        }

        // A capsule stand-in until the real beaver model arrives; the teeth show which way it faces.
        static GameObject BuildBeaver(Transform parent, out Transform cameraPivot)
        {
            var beaver = new GameObject("Beaver", typeof(CharacterController), typeof(BeaverInput),
                typeof(PlayerLook), typeof(PlayerMotor), typeof(PlayerAim), typeof(LiquidInventory), typeof(LiquidGun), typeof(TreeEater));
            beaver.transform.SetParent(parent, false);

            var controller = beaver.GetComponent<CharacterController>();
            controller.height = 1.2f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.6f, 0f);
            controller.stepOffset = 0.3f;
            controller.skinWidth = 0.03f;
            controller.minMoveDistance = 0f;

            Material fur = BuilderUtility.FlatMaterial("Greybox_Beaver", new Color(0.45f, 0.30f, 0.18f));
            Material teeth = BuilderUtility.FlatMaterial("Greybox_Teeth", new Color(0.95f, 0.85f, 0.45f));
            BuilderUtility.VisualPrimitive(PrimitiveType.Capsule, "Body", beaver.transform,
                new Vector3(0f, 0.6f, 0f), new Vector3(0.7f, 0.6f, 0.7f), fur);
            BuilderUtility.VisualPrimitive(PrimitiveType.Cube, "Teeth", beaver.transform,
                new Vector3(0f, 0.85f, 0.33f), new Vector3(0.18f, 0.14f, 0.1f), teeth);
            BuilderUtility.VisualPrimitive(PrimitiveType.Cube, "Tail", beaver.transform,
                new Vector3(0f, 0.12f, -0.5f), new Vector3(0.4f, 0.08f, 0.5f), fur);

            cameraPivot = new GameObject("Camera Pivot").transform;
            cameraPivot.SetParent(beaver.transform, false);
            cameraPivot.localPosition = new Vector3(0f, 1.1f, 0f);

            BuilderUtility.SetLayerRecursively(beaver, GameLayers.Player);
            return beaver;
        }

        static LiquidInventory AddLiquidGun(GameObject beaver, LiquidType[] liquids, LiquidStream streamPrefab)
        {
            // Behind the teeth and inside the CharacterController, so particles never start inside a wall.
            var nozzle = new GameObject("Nozzle").transform;
            nozzle.SetParent(beaver.transform, false);
            nozzle.localPosition = new Vector3(0f, 0.85f, 0.25f);
            BuilderUtility.SetValues(beaver.GetComponent<LiquidGun>(), ("nozzle", nozzle), ("streamPrefab", streamPrefab));

            var inventory = beaver.GetComponent<LiquidInventory>();
            var serialized = new SerializedObject(inventory);
            SerializedProperty slots = serialized.FindProperty("slots");
            slots.arraySize = liquids.Length;
            for (int i = 0; i < liquids.Length; i++)
            {
                SerializedProperty slot = slots.GetArrayElementAtIndex(i);
                slot.FindPropertyRelative("liquid").objectReferenceValue = liquids[i];
                slot.FindPropertyRelative("startingAmount").floatValue = liquids[i].IsInfinite ? 0f : TestingStartAmount;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return inventory;
        }

        static void BuildHud(Transform parent, LiquidInventory inventory, TreeEater eater)
        {
            var hud = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler));
            hud.transform.SetParent(parent, false);
            hud.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = hud.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // PlayerAim casts through the screen centre, so the crosshair sits there.
            RectTransform crosshair = BuilderUtility.UiObject("Crosshair", hud.transform);
            crosshair.sizeDelta = new Vector2(10f, 10f);
            var image = crosshair.gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            image.color = new Color(1f, 1f, 1f, 0.85f);
            image.raycastTarget = false;

            BuildLiquidPanel(hud.transform, inventory);

            // Above the liquid tanks, shown only while a tree is within reach.
            TMP_Text eatPrompt = BuilderUtility.UiLabel("Eat Prompt", hud.transform, "Hold E to eat", 30f, TextAlignmentOptions.Center);
            eatPrompt.rectTransform.anchorMin = eatPrompt.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            eatPrompt.rectTransform.sizeDelta = new Vector2(600f, 50f);
            eatPrompt.rectTransform.anchoredPosition = new Vector2(0f, 190f);
            eatPrompt.enabled = false;
            BuilderUtility.SetValues(eatPrompt.gameObject.AddComponent<EatPrompt>(), ("eater", eater), ("label", eatPrompt));
            BuilderUtility.SetLayerRecursively(hud, LayerMask.NameToLayer("UI"));
        }

        // A row of small tanks along the bottom of the screen, one per liquid slot, with the selected
        // liquid's name above them.
        static void BuildLiquidPanel(Transform hud, LiquidInventory inventory)
        {
            const float slotSize = 72f, spacing = 88f;
            int slotCount = inventory.SlotCount;

            RectTransform panel = BuilderUtility.UiObject("Liquids", hud);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0f);
            panel.anchoredPosition = new Vector2(0f, 32f);
            panel.sizeDelta = new Vector2(slotCount * spacing, slotSize + 44f);

            TMP_Text liquidName = BuilderUtility.UiLabel("Liquid Name", panel, "Water", 30f, TextAlignmentOptions.Bottom);
            RectTransform nameRect = liquidName.rectTransform;
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = Vector2.one;
            nameRect.pivot = new Vector2(0.5f, 1f);
            nameRect.sizeDelta = new Vector2(0f, 40f);

            var views = new LiquidSlotView[slotCount];
            for (int i = 0; i < slotCount; i++)
            {
                RectTransform slot = BuilderUtility.UiObject($"Slot {i + 1}", panel);
                slot.anchorMin = slot.anchorMax = slot.pivot = new Vector2(0.5f, 0f);
                slot.anchoredPosition = new Vector2((i - (slotCount - 1) / 2f) * spacing, 0f);
                slot.sizeDelta = new Vector2(slotSize, slotSize);
                Image frame = BuilderUtility.UiImage(slot, Color.white);

                RectTransform tank = BuilderUtility.UiObject("Tank", slot);
                BuilderUtility.Stretch(tank, 5f);
                BuilderUtility.UiImage(tank, new Color(0.08f, 0.1f, 0.14f, 0.85f));

                RectTransform fillRect = BuilderUtility.UiObject("Fill", tank);
                BuilderUtility.Stretch(fillRect, 0f);
                Image fill = BuilderUtility.UiImage(fillRect, Color.white);

                TMP_Text key = BuilderUtility.UiLabel("Key", slot, (i + 1).ToString(), 22f, TextAlignmentOptions.TopLeft);
                BuilderUtility.Stretch(key.rectTransform, 9f);

                views[i] = slot.gameObject.AddComponent<LiquidSlotView>();
                BuilderUtility.SetValues(views[i], ("frame", frame), ("fill", fill));
            }

            BuilderUtility.SetValues(panel.gameObject.AddComponent<LiquidHud>(),
                ("inventory", inventory), ("liquidName", liquidName), ("slots", views));
        }
    }
}
