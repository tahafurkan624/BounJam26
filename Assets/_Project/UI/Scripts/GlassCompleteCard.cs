using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BounJam
{
    /// <summary>
    /// Level-complete card: the delivery message, the time the level took, the cargo carried home (when the level has
    /// any) and the replay / next-level buttons. While it is bound, the LevelManager waits for the next-level button.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class GlassCompleteCard : MonoBehaviour
    {
        [SerializeField] TMP_Text kicker;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text timeValue;
        [SerializeField] GameObject cargoTile;
        [SerializeField] TMP_Text cargoValue;
        [SerializeField] Button replayButton;
        [SerializeField] Button nextButton;
        [SerializeField] TMP_Text nextLabel;
        [SerializeField] string nextText = "Next level";
        [SerializeField] string finalNextText = "Play again";
        [SerializeField] RectTransform card;
        [SerializeField] float showTime = 0.45f;

        CanvasGroup group;
        Collectible[] cargo = new Collectible[0];
        float startTime;

        void Awake()
        {
            group = GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }

        public void Bind(GlassUI ui)
        {
            startTime = Time.time;
            kicker.text = ui.Level.LevelTitle.ToUpperInvariant();
            cargo = FindObjectsByType<Collectible>(FindObjectsSortMode.None);
            replayButton.onClick.AddListener(ui.Level.Restart);
            nextButton.onClick.AddListener(ui.Level.LoadNext);
            nextLabel.text = ui.Level.IsFinalLevel ? finalNextText : nextText;
        }

        public void Show(string message)
        {
            title.text = message;
            int seconds = Mathf.FloorToInt(Time.time - startTime);
            timeValue.text = $"{seconds / 60:00}:{seconds % 60:00}";
            cargoTile.SetActive(cargo.Length > 0);
            int held = 0;
            foreach (Collectible item in cargo)
                if (item != null && item.IsCollected) held++;
            cargoValue.text = $"{held} / {cargo.Length}";

            GlassUI.SetPlayerInput(false);
            group.interactable = true;
            group.blocksRaycasts = true;
            StartCoroutine(Pop());
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(nextButton.gameObject);
        }

        IEnumerator Pop()
        {
            for (float t = 0f; t < showTime; t += Time.unscaledDeltaTime)
            {
                float k = t / showTime;
                group.alpha = Mathf.SmoothStep(0f, 1f, Mathf.Min(k * 1.6f, 1f));
                // Overshooting ease: the card lands like a drop.
                float s = 1f + Mathf.Sin(k * Mathf.PI) * 0.06f * (1f - k);
                card.localScale = Vector3.one * Mathf.Lerp(0.9f, 1f, Mathf.SmoothStep(0f, 1f, k)) * s;
                yield return null;
            }
            group.alpha = 1f;
            card.localScale = Vector3.one;
        }
    }
}
