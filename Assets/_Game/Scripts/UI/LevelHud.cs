using BounJam.Level;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BounJam.UI
{
    /// <summary>The fire counter while playing, and the complete or failed screen when the level ends.</summary>
    public class LevelHud : MonoBehaviour
    {
        [SerializeField] LevelManager level;
        [SerializeField] TMP_Text fireCounter;
        [SerializeField] GameObject endScreen;
        [SerializeField] TMP_Text endTitle;
        [SerializeField] TMP_Text endMessage;
        [SerializeField] Button restartButton;
        [SerializeField] Button nextButton;

        int shownFiresLeft = -1;
        bool endShown;

        void Start()
        {
            restartButton.onClick.AddListener(level.Restart);
            nextButton.onClick.AddListener(level.LoadNextLevel);
            endScreen.SetActive(false);
        }

        void LateUpdate()
        {
            if (level.FiresLeft != shownFiresLeft)
            {
                shownFiresLeft = level.FiresLeft;
                fireCounter.text = $"Fires left: {shownFiresLeft}";
            }
            if (!endShown && level.State != LevelManager.Outcome.Playing)
                ShowEnd();
        }

        void ShowEnd()
        {
            endShown = true;
            bool complete = level.State == LevelManager.Outcome.Complete;
            int seconds = Mathf.FloorToInt(level.PlayTime);
            endTitle.text = complete ? "Level Complete!" : "Level Failed";
            endMessage.text = complete
                ? $"All {level.FireCount} fires are out, in {seconds / 60}:{seconds % 60:00}."
                : level.FailReason;
            nextButton.gameObject.SetActive(complete && level.HasNextLevel);
            fireCounter.gameObject.SetActive(false);
            endScreen.SetActive(true);
        }
    }
}
