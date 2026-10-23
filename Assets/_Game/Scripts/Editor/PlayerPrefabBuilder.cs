using BounJam.Core;
using BounJam.Player;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
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
            var rig = new GameObject("PlayerRig");
            GameObject rigAsset;
            try
            {
                BuildRig(rig);
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

        static void BuildRig(GameObject rig)
        {
            GameObject beaver = BuildBeaver(rig.transform, out Transform cameraPivot);

            var mainCamera = new GameObject("Main Camera", typeof(Camera), typeof(UniversalAdditionalCameraData),
                typeof(AudioListener), typeof(CinemachineBrain));
            mainCamera.tag = "MainCamera";
            mainCamera.transform.SetParent(rig.transform, false);
            mainCamera.GetComponent<Camera>().nearClipPlane = 0.1f;
            BuilderUtility.SetField(beaver.GetComponent<PlayerAim>(), "viewCamera", mainCamera.GetComponent<Camera>());

            CinemachineOrbitalFollow orbit = BuildFreeLookCamera(rig.transform, cameraPivot);
            BuilderUtility.SetField(beaver.GetComponent<PlayerLook>(), "orbit", orbit);

            BuildHud(rig.transform);
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
