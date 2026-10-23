using BounJam.Core;
using BounJam.Player;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Generates PlayerRig.prefab (stand-in beaver, cameras and HUD) and PlayerSpawn.prefab.
    /// Rebuilding overwrites any hand edits made to those two prefabs.
    /// </summary>
    static class PlayerPrefabBuilder
    {
        public const string RigPath = "Assets/_Game/Prefabs/Player/PlayerRig.prefab";
        public const string SpawnPath = "Assets/_Game/Prefabs/Level/PlayerSpawn.prefab";

        [MenuItem("BounJam/Setup/Rebuild Player Prefabs")]
        internal static void Build()
        {
            GameObject rig = BuildRig();
            GameObject rigAsset = PrefabUtility.SaveAsPrefabAsset(rig, RigPath);
            Object.DestroyImmediate(rig);

            var spawn = new GameObject("PlayerSpawn", typeof(PlayerSpawn));
            BuilderUtility.SetField(spawn.GetComponent<PlayerSpawn>(), "playerRig", rigAsset);
            PrefabUtility.SaveAsPrefabAsset(spawn, SpawnPath);
            Object.DestroyImmediate(spawn);
            Debug.Log("[BounJam] Built player prefabs.");
        }

        static GameObject BuildRig()
        {
            var rig = new GameObject("PlayerRig");
            GameObject beaver = BuildBeaver(rig.transform, out Transform cameraPivot);

            var mainCamera = new GameObject("Main Camera", typeof(Camera), typeof(UniversalAdditionalCameraData),
                typeof(AudioListener), typeof(CinemachineBrain));
            mainCamera.tag = "MainCamera";
            mainCamera.transform.SetParent(rig.transform, false);
            mainCamera.GetComponent<Camera>().nearClipPlane = 0.1f;
            BuilderUtility.SetField(beaver.GetComponent<PlayerAim>(), "viewCamera", mainCamera.GetComponent<Camera>());

            var followCamera = new GameObject("Follow Camera", typeof(CinemachineCamera), typeof(CinemachineThirdPersonFollow));
            followCamera.transform.SetParent(rig.transform, false);
            var cinemachineCamera = followCamera.GetComponent<CinemachineCamera>();
            cinemachineCamera.Follow = cameraPivot;
            cinemachineCamera.Lens.FieldOfView = 55f;
            var thirdPerson = followCamera.GetComponent<CinemachineThirdPersonFollow>();
            thirdPerson.ShoulderOffset = new Vector3(0.5f, 0.1f, 0f);
            thirdPerson.VerticalArmLength = 0.3f;
            thirdPerson.CameraDistance = 4f;
            thirdPerson.AvoidObstacles.Enabled = true;
            thirdPerson.AvoidObstacles.CollisionFilter = GameLayers.MaskOf(GameLayers.Default, GameLayers.Ground, GameLayers.Tree);
            thirdPerson.AvoidObstacles.CameraRadius = 0.2f;

            BuildHud(rig.transform);
            return rig;
        }

        // A capsule stand-in until the real beaver model arrives; the teeth show which way it faces.
        static GameObject BuildBeaver(Transform parent, out Transform cameraPivot)
        {
            var beaver = new GameObject("Beaver", typeof(CharacterController), typeof(BeaverInput),
                typeof(PlayerLook), typeof(PlayerMotor), typeof(PlayerAim));
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
            BuilderUtility.SetField(beaver.GetComponent<PlayerLook>(), "pivot", cameraPivot);

            BuilderUtility.SetLayerRecursively(beaver, GameLayers.Player);
            return beaver;
        }

        static void BuildHud(Transform parent)
        {
            var hud = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler));
            hud.transform.SetParent(parent, false);
            hud.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = hud.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // PlayerAim casts through the screen centre, so the crosshair sits there.
            var crosshair = new GameObject("Crosshair", typeof(RectTransform), typeof(Image));
            crosshair.transform.SetParent(hud.transform, false);
            ((RectTransform)crosshair.transform).sizeDelta = new Vector2(10f, 10f);
            var image = crosshair.GetComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            image.color = new Color(1f, 1f, 1f, 0.85f);
            image.raycastTarget = false;

            BuilderUtility.SetLayerRecursively(hud, LayerMask.NameToLayer("UI"));
        }
    }
}
