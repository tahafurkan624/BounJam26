using System.Collections;
using BlobShip.UI;
using BlobShip.UI.Liquid3D;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BounJam
{
    /// <summary>
    /// The main menu (MainMenu scene): Play carries on from the furthest level reached (<see cref="GameProgress"/>)
    /// behind a rising wall of liquid, the round Options button pours in the options panel, and Exit melts the picture
    /// away and quits without asking. The whole menu sits on the liquid canvas (Screen Space - Camera), so the exit melt,
    /// which redraws the camera's picture, takes the menu with it.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        [SerializeField] Canvas liquidCanvas;
        [SerializeField] float liquidPlaneDistance = 0.5f;
        [Tooltip("Play, Options, Exit, title and studio: hidden while the options are open.")]
        [SerializeField] CanvasGroup mainGroup;
        [SerializeField] Button playButton;
        [SerializeField] Button optionsButton;
        [SerializeField] Button exitButton;
        [SerializeField] MenuOptions options;
        [SerializeField] ScreenMelt exitMelt;
        [SerializeField] Graphic wipe;
        [Tooltip("Scene Play loads when no level has been reached yet.")]
        [SerializeField] string firstLevel = "Level01";
        [SerializeField] float wipeRise = 0.6f;
        [SerializeField] float fadeTime = 0.25f;

        static readonly int LevelId = Shader.PropertyToID("_Level");
        static readonly int WipeTimeId = Shader.PropertyToID("_WipeTime");

        bool leaving;
        Coroutine fading;

        void Awake()
        {
            liquidCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            liquidCanvas.planeDistance = liquidPlaneDistance;
            AttachCamera();
            if (wipe != null) wipe.gameObject.SetActive(false);
        }

        void OnEnable() => GlassSceneCapture.Acquire();

        void OnDisable() => GlassSceneCapture.Release();

        void Start()
        {
            // The liquid state is static and outlives the last level; a level paused or left mid-way leaves these behind.
            LiquidUI.SetSolid(false);
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            GameAudio.Apply();
            if (EventSystem.current == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            playButton.onClick.AddListener(Play);
            optionsButton.onClick.AddListener(OpenOptions);
            exitButton.onClick.AddListener(Exit);
            UISound.AddTo(playButton, optionsButton, exitButton);
            options.Closed += OnOptionsClosed;
        }

        void LateUpdate() => AttachCamera();

        void AttachCamera()
        {
            if (liquidCanvas.worldCamera != null && liquidCanvas.worldCamera.isActiveAndEnabled) return;
            Camera main = Camera.main;
            liquidCanvas.worldCamera = main;
            // Nested canvases with their own raycaster (the sliders) don't take the root's camera.
            foreach (GraphicRaycaster raycaster in liquidCanvas.GetComponentsInChildren<GraphicRaycaster>(true))
                raycaster.GetComponent<Canvas>().worldCamera = main;
        }

        void Update()
        {
            // Keyboard and gamepad: the first move picks Play (or the panel's first control) when nothing is selected.
            if (leaving || EventSystem.current == null || EventSystem.current.currentSelectedGameObject != null) return;
            if (EventSystem.current.currentInputModule is InputSystemUIInputModule module && module.move != null &&
                module.move.action != null && module.move.action.WasPressedThisFrame())
                EventSystem.current.SetSelectedGameObject(options.IsOpen ? options.FirstControl : playButton.gameObject);
        }

        void Play()
        {
            if (leaving) return;
            leaving = true;
            SetMainInteractable(false);
            UILiquidFX.Splash((RectTransform)playButton.transform, 18, 1.4f);
            StartCoroutine(LoadBehindWipe(GameProgress.LevelToPlay(firstLevel)));
        }

        IEnumerator LoadBehindWipe(string scene)
        {
            if (wipe != null)
            {
                // The wipe carries over into the level and drains away there (ScreenWipe).
                wipe.gameObject.SetActive(true);
                Material material = wipe.materialForRendering;
                float start = Time.unscaledTime;
                for (float t = 0f; t < wipeRise; t = Time.unscaledTime - start)
                {
                    float k = t / wipeRise;
                    material.SetFloat(LevelId, Mathf.Lerp(-0.1f, 1.15f, k * k * (3f - 2f * k)));
                    material.SetFloat(WipeTimeId, t);
                    yield return null;
                }
                material.SetFloat(LevelId, 1.15f);
                ScreenWipe.Drain(wipe.canvas, material, Time.unscaledTime - start);
            }
            SceneManager.LoadScene(scene);
        }

        void OpenOptions()
        {
            if (leaving) return;
            SetMainInteractable(false);
            Fade(0f);
            options.Open();
        }

        void OnOptionsClosed()
        {
            SetMainInteractable(true);
            Fade(1f);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        void Exit()
        {
            if (leaving) return;
            leaving = true;
            SetMainInteractable(false);
            exitMelt.Play(Quit);
        }

        static void Quit()
        {
#if UNITY_EDITOR
            Time.timeScale = 1f;
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void SetMainInteractable(bool on)
        {
            mainGroup.interactable = on;
            mainGroup.blocksRaycasts = on;
        }

        void Fade(float to)
        {
            if (fading != null) StopCoroutine(fading);
            fading = StartCoroutine(FadeRoutine(to));
        }

        IEnumerator FadeRoutine(float to)
        {
            float from = mainGroup.alpha;
            for (float t = 0f; t < fadeTime; t += Time.unscaledDeltaTime)
            {
                mainGroup.alpha = Mathf.Lerp(from, to, t / fadeTime);
                yield return null;
            }
            mainGroup.alpha = to;
        }
    }
}
