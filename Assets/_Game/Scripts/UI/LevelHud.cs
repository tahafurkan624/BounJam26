using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace BounJam
{
    /// <summary>
    /// On-screen text for a level: the title fades in and out at the start; messages (delivered, ship lost)
    /// stay in the middle of the screen until the next scene loads.
    /// </summary>
    public class LevelHud : MonoBehaviour
    {
        [SerializeField] Text title;
        [SerializeField] Text message;
        [Tooltip("How long the level title stays fully visible, s.")]
        [SerializeField] float titleHold = 2f;
        [SerializeField] float fadeTime = 0.4f;

        void Awake()
        {
            SetAlpha(title, 0f);
            SetAlpha(message, 0f);
        }

        public void ShowTitle(string text)
        {
            title.text = text;
            StartCoroutine(FadeInOut(title));
        }

        public void ShowMessage(string text)
        {
            message.text = text;
            StartCoroutine(Fade(message, 0f, 1f));
        }

        IEnumerator FadeInOut(Text label)
        {
            yield return Fade(label, 0f, 1f);
            yield return new WaitForSeconds(titleHold);
            yield return Fade(label, 1f, 0f);
        }

        IEnumerator Fade(Text label, float from, float to)
        {
            for (float t = 0f; t < fadeTime; t += Time.deltaTime)
            {
                SetAlpha(label, Mathf.Lerp(from, to, t / fadeTime));
                yield return null;
            }
            SetAlpha(label, to);
        }

        static void SetAlpha(Text label, float alpha)
        {
            Color color = label.color;
            color.a = alpha;
            label.color = color;
        }
    }
}
