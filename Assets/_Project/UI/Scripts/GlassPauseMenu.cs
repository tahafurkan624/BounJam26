using System.Collections;
using BlobShip.UI.Liquid3D;
using FMOD.Studio;
using FMODUnity;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BounJam
{
    /// <summary>
    /// Pause menu on the Player/Pause action (Esc, gamepad Start) or the HUD's pause button: time stops, the blob's
    /// controls are off, and a 3D glass panel pours in with liquid buttons for resume, restart and quit, and the
    /// master volume (FMOD's master bus, kept between sessions). Closing drains the glass away. The HUD hides while it
    /// is open.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class GlassPauseMenu : MonoBehaviour
    {
        const string VolumeKey = "BounJam.Volume";

        [SerializeField] LiquidPanel3D panel;
        [Tooltip("Dims the level behind the panel.")]
        [SerializeField] Graphic backdrop;
        [SerializeField] TMP_Text subtitle;
        [SerializeField] Button resumeButton;
        [SerializeField] Button restartButton;
        [SerializeField] Button quitButton;
        [SerializeField] Slider volume;
        [SerializeField] TMP_Text volumeValue;
        [SerializeField] float fadeTime = 0.25f;

        GlassUI owner;
        CanvasGroup group;
        InputAction pauseAction;
        float timeScaleBefore = 1f;
        Coroutine fading;

        public bool IsOpen { get; private set; }

        void Awake()
        {
            group = GetComponent<CanvasGroup>();
            group.alpha = 1f;
            SetBlocking(false);
            backdrop.canvasRenderer.SetAlpha(0f);
            panel.gameObject.SetActive(false);
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
            owner.SetHudVisible(false);
            SetBlocking(true);
            FadeBackdrop(1f);
            panel.Show();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Time.timeScale = timeScaleBefore;
            GlassUI.SetPlayerInput(true);
            // The level may be ending (the complete card takes over and keeps the HUD hidden).
            if (owner.Level.Current == LevelManager.State.Playing) owner.SetHudVisible(true);
            SetBlocking(false);
            FadeBackdrop(0f);
            panel.Hide();
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
            // Every sound plays through FMOD, so its master bus is the whole mix (Unity's AudioListener hears nothing).
            if (RuntimeManager.StudioSystem.getBus("bus:/", out Bus master) == FMOD.RESULT.OK) master.setVolume(value);
            PlayerPrefs.SetFloat(VolumeKey, value);
            volumeValue.text = $"{Mathf.RoundToInt(value * 100f)}%";
        }

        void SetBlocking(bool on)
        {
            group.interactable = on;
            group.blocksRaycasts = on;
        }

        void FadeBackdrop(float to)
        {
            if (fading != null) StopCoroutine(fading);
            if (isActiveAndEnabled) fading = StartCoroutine(Fade(to));
            else backdrop.canvasRenderer.SetAlpha(to);
        }

        IEnumerator Fade(float to)
        {
            float from = backdrop.canvasRenderer.GetAlpha();
            for (float t = 0f; t < fadeTime; t += Time.unscaledDeltaTime)
            {
                backdrop.canvasRenderer.SetAlpha(Mathf.Lerp(from, to, t / fadeTime));
                yield return null;
            }
            backdrop.canvasRenderer.SetAlpha(to);
        }
    }
}
