using System;
using System.Collections;
using BlobShip.UI.Liquid3D;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BounJam
{
    /// <summary>
    /// The main menu's options: a glass panel with the music and sound effects volumes (<see cref="GameAudio"/>) and a
    /// button that resets the game's progress (<see cref="GameProgress"/>; it asks for a second click first). Back or
    /// Esc drains it away.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class MenuOptions : MonoBehaviour
    {
        [SerializeField] LiquidPanel3D panel;
        [SerializeField] Graphic backdrop;
        [SerializeField] Slider music;
        [SerializeField] TMP_Text musicValue;
        [SerializeField] Slider sfx;
        [SerializeField] TMP_Text sfxValue;
        [SerializeField] Button resetButton;
        [SerializeField] TMP_Text resetLabel;
        [SerializeField] Button backButton;
        [SerializeField] float fadeTime = 0.25f;
        [Tooltip("How long the reset button waits for the second click, s.")]
        [SerializeField] float confirmTime = 3f;

        CanvasGroup group;
        Coroutine fading;
        string resetText;
        float confirmUntil = -1f;
        float doneUntil = -1f;

        public bool IsOpen { get; private set; }
        public GameObject FirstControl => music.gameObject;
        public event Action Closed;

        void Awake()
        {
            group = GetComponent<CanvasGroup>();
            SetBlocking(false);
            backdrop.canvasRenderer.SetAlpha(0f);
            panel.gameObject.SetActive(false);
            resetText = resetLabel.text;

            music.SetValueWithoutNotify(GameAudio.Music);
            sfx.SetValueWithoutNotify(GameAudio.Sfx);
            ShowValues();
            music.onValueChanged.AddListener(value => { GameAudio.Music = value; ShowValues(); });
            sfx.onValueChanged.AddListener(value => { GameAudio.Sfx = value; ShowValues(); });
            resetButton.onClick.AddListener(OnReset);
            backButton.onClick.AddListener(Close);
            UISound.AddTo(music, sfx, resetButton, backButton);
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            music.SetValueWithoutNotify(GameAudio.Music);
            sfx.SetValueWithoutNotify(GameAudio.Sfx);
            ShowValues();
            SetBlocking(true);
            Fade(1f);
            panel.Show();
            GameSounds.UI.Play(GameSounds.UIParameter, GameSounds.UIPopUp);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            SetBlocking(false);
            Fade(0f);
            panel.Hide();
            confirmUntil = doneUntil = -1f;
            resetLabel.text = resetText;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            Closed?.Invoke();
        }

        void Update()
        {
            if (!IsOpen) return;
            if (EventSystem.current != null && EventSystem.current.currentInputModule is InputSystemUIInputModule module &&
                module.cancel != null && module.cancel.action != null && module.cancel.action.WasPressedThisFrame())
            {
                Close();
                return;
            }
            float now = Time.unscaledTime;
            if ((confirmUntil > 0f && now > confirmUntil) || (doneUntil > 0f && now > doneUntil))
            {
                confirmUntil = doneUntil = -1f;
                resetLabel.text = resetText;
            }
        }

        void OnReset()
        {
            if (confirmUntil < 0f)
            {
                confirmUntil = Time.unscaledTime + confirmTime;
                doneUntil = -1f;
                resetLabel.text = "Click again to reset";
                return;
            }
            GameProgress.Reset();
            confirmUntil = -1f;
            doneUntil = Time.unscaledTime + 2f;
            resetLabel.text = "Progress reset";
            resetButton.GetComponent<LiquidSlab>().Poke();
        }

        void ShowValues()
        {
            musicValue.text = $"{Mathf.RoundToInt(music.value * 100f)}%";
            sfxValue.text = $"{Mathf.RoundToInt(sfx.value * 100f)}%";
        }

        void SetBlocking(bool on)
        {
            group.interactable = on;
            group.blocksRaycasts = on;
        }

        void Fade(float to)
        {
            if (fading != null) StopCoroutine(fading);
            fading = StartCoroutine(FadeRoutine(to));
        }

        IEnumerator FadeRoutine(float to)
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
