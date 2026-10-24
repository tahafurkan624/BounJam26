using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BounJam
{
    /// <summary>
    /// Pause menu on the Player/Pause action (Esc, gamepad Start) or the HUD's pause button: time stops, the blob's
    /// controls are off, and the frosted panel offers resume, restart, quit and the master volume.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class GlassPauseMenu : MonoBehaviour
    {
        const string VolumeKey = "BounJam.Volume";

        [SerializeField] TMP_Text subtitle;
        [SerializeField] Button resumeButton;
        [SerializeField] Button restartButton;
        [SerializeField] Button quitButton;
        [SerializeField] Slider volume;
        [SerializeField] TMP_Text volumeValue;
        [SerializeField] float fadeTime = 0.18f;

        GlassUI owner;
        CanvasGroup group;
        InputAction pauseAction;
        float timeScaleBefore = 1f;
        Coroutine fading;

        public bool IsOpen { get; private set; }

        void Awake()
        {
            group = GetComponent<CanvasGroup>();
            Show(false, true);
        }

        public void Bind(GlassUI ui)
        {
            owner = ui;
            subtitle.text = ui.Level.LevelTitle;
            resumeButton.onClick.AddListener(Close);
            restartButton.onClick.AddListener(RestartLevel);
            quitButton.onClick.AddListener(Quit);

            float saved = PlayerPrefs.GetFloat(VolumeKey, 1f);
            volume.SetValueWithoutNotify(saved);
            SetVolume(saved);
            volume.onValueChanged.AddListener(SetVolume);

            pauseAction = InputSystem.actions != null ? InputSystem.actions.FindAction("Player/Pause") : null;
            if (pauseAction != null) pauseAction.performed += OnPausePressed;
        }

        void OnDestroy()
        {
            if (pauseAction != null) pauseAction.performed -= OnPausePressed;
            if (IsOpen) Time.timeScale = timeScaleBefore;
        }

        void OnPausePressed(InputAction.CallbackContext _) => Toggle();

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (IsOpen || owner == null || owner.Level.Current != LevelManager.State.Playing) return;
            IsOpen = true;
            timeScaleBefore = Time.timeScale;
            Time.timeScale = 0f;
            GlassUI.SetPlayerInput(false);
            Show(true, false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Time.timeScale = timeScaleBefore;
            GlassUI.SetPlayerInput(true);
            Show(false, false);
            // A selected button would take the next Space (UI Submit) and pause again.
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        void RestartLevel()
        {
            Close();
            owner.Level.Restart();
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void SetVolume(float value)
        {
            AudioListener.volume = value;
            PlayerPrefs.SetFloat(VolumeKey, value);
            volumeValue.text = $"{Mathf.RoundToInt(value * 100f)}%";
        }

        void Show(bool visible, bool instant)
        {
            group.interactable = visible;
            group.blocksRaycasts = visible;
            if (fading != null) StopCoroutine(fading);
            if (instant || !isActiveAndEnabled) group.alpha = visible ? 1f : 0f;
            else fading = StartCoroutine(Fade(visible ? 1f : 0f));
        }

        IEnumerator Fade(float to)
        {
            float from = group.alpha;
            for (float t = 0f; t < fadeTime; t += Time.unscaledDeltaTime)
            {
                group.alpha = Mathf.Lerp(from, to, t / fadeTime);
                yield return null;
            }
            group.alpha = to;
        }
    }
}
