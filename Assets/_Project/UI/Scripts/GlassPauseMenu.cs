using System.Collections;
using BlobShip.UI.Liquid3D;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace BounJam
{
    /// <summary>
    /// Pause menu on the Player/Pause action (Esc, gamepad Start) or the HUD's pause button: time stops, the blob's
    /// controls are off, and a 3D glass panel pours in with liquid buttons for resume, restart and quit, and the
    /// music and sound effects volumes (see GameAudio, kept between sessions). Closing drains the glass away. The HUD hides while it
    /// is open. Opening plays event:/UI's PopUp, and its controls play Hover and Click (see <see cref="UISound"/>).
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class GlassPauseMenu : MonoBehaviour
    {
        [SerializeField] LiquidPanel3D panel;
        [Tooltip("Dims the level behind the panel.")]
        [SerializeField] Graphic backdrop;
        [SerializeField] TMP_Text subtitle;
        [SerializeField] Button resumeButton;
        [SerializeField] Button restartButton;
        [SerializeField] Button quitButton;
        [SerializeField, FormerlySerializedAs("volume")] Slider music;
        [SerializeField, FormerlySerializedAs("volumeValue")] TMP_Text musicValue;
        [SerializeField] Slider sfx;
        [SerializeField] TMP_Text sfxValue;
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
            UISound.AddTo(resumeButton, restartButton, quitButton, music);

            music.SetValueWithoutNotify(GameAudio.Music);
            music.onValueChanged.AddListener(value => { GameAudio.Music = value; ShowVolumes(); });
            // A GlassUI prefab built before the effects slider has only the music one.
            if (sfx != null)
            {
                UISound.AddTo(sfx);
                sfx.SetValueWithoutNotify(GameAudio.Sfx);
                sfx.onValueChanged.AddListener(value => { GameAudio.Sfx = value; ShowVolumes(); });
            }
            ShowVolumes();

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
            GameSounds.UI.Play(GameSounds.UIParameter, GameSounds.UIPopUp);
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

        void ShowVolumes()
        {
            musicValue.text = Percent(GameAudio.Music);
            if (sfxValue != null) sfxValue.text = Percent(GameAudio.Sfx);
        }

        static string Percent(float value) => $"{Mathf.RoundToInt(value * 100f)}%";

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
