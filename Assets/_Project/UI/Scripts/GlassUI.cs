using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BounJam
{
    /// <summary>
    /// Root of the Liquid Glass UI (Resources/GlassUI prefab). It installs itself into every scene that has a
    /// <see cref="LevelManager"/>, so the level scenes need no changes: it takes over from the old text HUD, shows the
    /// level title and end messages, and owns the in-game HUD, the pause menu and the level-complete card.
    /// It has two canvases. The overlay canvas holds the flat glass (HUD pills, toasts); it is Screen Space - Overlay,
    /// so post-processing leaves it alone and <see cref="GlassSceneCapture"/> hands the glass the finished frame to
    /// blur. The liquid canvas holds the 3D liquid buttons and glass panels (BlobShip Liquid3D: pause menu, complete
    /// card, pause button, form switch); they are meshes, so it is Screen Space - Camera on the main camera, close in
    /// front of it, where they can refract the scene and the LED labels catch the bloom.
    /// </summary>
    public class GlassUI : MonoBehaviour
    {
        const string PrefabPath = "GlassUI";

        [SerializeField] Canvas overlayCanvas;
        [SerializeField] Canvas liquidCanvas;
        [Tooltip("Distance of the liquid canvas from the camera, m. Anything in the level closer than this covers it.")]
        [SerializeField] float liquidPlaneDistance = 0.5f;
        [SerializeField] GlassHud hud;
        [SerializeField] GlassToast titleToast;
        [SerializeField] GlassToast messageToast;
        [SerializeField] GlassPauseMenu pauseMenu;
        [SerializeField] GlassCompleteCard completeCard;
        [Tooltip("How long the level title stays up at the start, s.")]
        [SerializeField] float titleHold = 2.5f;
        [Tooltip("Hide the old text HUD (LevelHud) while the glass UI runs.")]
        [SerializeField] bool hideLegacyHud = true;

        static bool hooked;

        public LevelManager Level { get; private set; }
        public bool IsPaused => pauseMenu != null && pauseMenu.IsOpen;

        // Statics survive entering play mode with domain reload off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => hooked = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            if (!hooked)
            {
                SceneManager.sceneLoaded += (_, _) => Install();
                hooked = true;
            }
            Install();
        }

        /// <summary>Adds the glass UI to the loaded scene if it runs a level and doesn't have one yet.</summary>
        static void Install()
        {
            var level = FindAnyObjectByType<LevelManager>();
            if (level == null || FindAnyObjectByType<GlassUI>() != null) return;
            var prefab = Resources.Load<GlassUI>(PrefabPath);
            if (prefab != null) Instantiate(prefab).Bind(level);
        }

        /// <summary>Enables or disables the blob's controls (all Player actions except Pause).</summary>
        public static void SetPlayerInput(bool enabled)
        {
            InputActionMap map = InputSystem.actions != null ? InputSystem.actions.FindActionMap("Player") : null;
            if (map == null) return;
            foreach (InputAction action in map.actions)
            {
                if (action.name == "Pause") continue;
                if (enabled) action.Enable();
                else action.Disable();
            }
        }

        void Awake()
        {
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            liquidCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            liquidCanvas.planeDistance = liquidPlaneDistance;
            // GlassGraphic shapes (the volume slider) draw on the liquid canvas too.
            foreach (Canvas canvas in new[] { overlayCanvas, liquidCanvas })
                canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            AttachCamera();
        }

        void OnEnable() => GlassSceneCapture.Acquire();

        void OnDisable() => GlassSceneCapture.Release();

        // A level may swap or re-enable its camera after the UI is installed.
        void LateUpdate() => AttachCamera();

        void AttachCamera()
        {
            if (liquidCanvas.worldCamera != null && liquidCanvas.worldCamera.isActiveAndEnabled) return;
            Camera main = Camera.main;
            liquidCanvas.worldCamera = main;
            // A nested canvas with its own raycaster (the volume slider) doesn't take the root's camera, and without
            // one its raycaster tests the pointer as if the canvas were an overlay.
            foreach (GraphicRaycaster raycaster in liquidCanvas.GetComponentsInChildren<GraphicRaycaster>(true))
                raycaster.GetComponent<Canvas>().worldCamera = main;
        }

        /// <summary>Shows or hides the in-game HUD (the pause menu covers it).</summary>
        public void SetHudVisible(bool visible)
        {
            if (hud != null) hud.SetVisible(visible);
        }

        void Bind(LevelManager level)
        {
            Level = level;
            level.HoldOnComplete = completeCard != null;
            level.TitleShown += OnTitleShown;
            level.Ended += OnEnded;

            if (hideLegacyHud)
            {
                foreach (LevelHud legacy in FindObjectsByType<LevelHud>(FindObjectsSortMode.None))
                    if (legacy.TryGetComponent(out Canvas legacyCanvas)) legacyCanvas.enabled = false;
            }
            if (EventSystem.current == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            // A level left from the complete card had the controls off.
            SetPlayerInput(true);

            if (hud != null) hud.Bind(this);
            if (pauseMenu != null) pauseMenu.Bind(this);
            if (completeCard != null) completeCard.Bind(this);
        }

        void OnDestroy()
        {
            if (Level == null) return;
            Level.TitleShown -= OnTitleShown;
            Level.Ended -= OnEnded;
        }

        public void TogglePause()
        {
            if (pauseMenu != null) pauseMenu.Toggle();
        }

        void OnTitleShown(string title)
        {
            if (titleToast != null) titleToast.Show(title, titleHold);
        }

        void OnEnded(LevelManager.State state, string message)
        {
            if (pauseMenu != null) pauseMenu.Close();
            if (state == LevelManager.State.Complete && completeCard != null)
            {
                if (hud != null) hud.SetVisible(false);
                completeCard.Show(message);
            }
            else if (messageToast != null) messageToast.Show(message, 0f);
        }
    }
}
