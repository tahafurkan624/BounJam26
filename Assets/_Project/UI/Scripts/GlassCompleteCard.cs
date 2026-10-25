using System.Collections;
using BlobShip.UI;
using BlobShip.UI.Liquid3D;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BounJam
{
    /// <summary>
    /// Level-complete card: a 3D glass panel that pours in with the delivery message, the time the level took and
    /// liquid replay / next-level buttons, with a splash of the badge
    /// and a droplet rain as it lands. While it is bound, the LevelManager waits for the next-level button.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class GlassCompleteCard : MonoBehaviour
    {
        [SerializeField] LiquidPanel3D panel;
        [Tooltip("Tints the level behind the card.")]
        [SerializeField] Graphic wash;
        [SerializeField] TMP_Text kicker;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text timeValue;
        [SerializeField] TMP_Text timeName;
        [SerializeField] Button replayButton;
        [SerializeField] Button nextButton;
        [SerializeField] TMP_Text nextLabel;
        [SerializeField] string nextText = "Next level";
        [SerializeField] string finalNextText = "Play again";
        [SerializeField] float washTime = 0.45f;
        [SerializeField] UILiquidSequence celebration;

        CanvasGroup group;
        float startTime;

        void Awake()
        {
            group = GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = false;
            group.blocksRaycasts = false;
            wash.canvasRenderer.SetAlpha(0f);
            panel.gameObject.SetActive(false);
        }

        public void Bind(GlassUI ui)
        {
            startTime = Time.time;
            kicker.text = ui.Level.LevelTitle.ToUpperInvariant();
            replayButton.onClick.AddListener(ui.Level.Restart);
            nextButton.onClick.AddListener(ui.Level.LoadNext);
            UISound.AddTo(replayButton, nextButton);
            nextLabel.text = ui.Level.IsFinalLevel ? finalNextText : nextText;
        }

        public void Show(string message)
        {
            title.text = message;
            int seconds = Mathf.FloorToInt(Time.time - startTime);
            timeValue.text = $"{seconds / 60:00}:{seconds % 60:00}";

            GlassUI.SetPlayerInput(false);
            group.interactable = true;
            group.blocksRaycasts = true;
            StartCoroutine(FadeWash());
            panel.Show();
            if (celebration != null) celebration.Play();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(nextButton.gameObject);
        }

        IEnumerator FadeWash()
        {
            for (float t = 0f; t < washTime; t += Time.unscaledDeltaTime)
            {
                wash.canvasRenderer.SetAlpha(Mathf.SmoothStep(0f, 1f, t / washTime));
                yield return null;
            }
            wash.canvasRenderer.SetAlpha(1f);
        }
    }
}
