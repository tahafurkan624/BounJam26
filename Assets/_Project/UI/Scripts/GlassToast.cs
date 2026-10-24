using System.Collections;
using TMPro;
using UnityEngine;

namespace BounJam
{
    /// <summary>A glass pill with one line of text that fades in, optionally holds and fades out (unscaled time).</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class GlassToast : MonoBehaviour
    {
        [SerializeField] TMP_Text label;
        [SerializeField] float fadeTime = 0.35f;

        CanvasGroup group;
        Coroutine running;

        void Awake()
        {
            group = GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
        }

        /// <summary>Shows the text; hold &lt;= 0 keeps it up until <see cref="Hide"/> or the next scene.</summary>
        public void Show(string text, float hold)
        {
            label.text = text;
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(Run(hold));
        }

        public void Hide()
        {
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(Fade(group.alpha, 0f));
        }

        IEnumerator Run(float hold)
        {
            yield return Fade(group.alpha, 1f);
            if (hold <= 0f) yield break;
            yield return new WaitForSecondsRealtime(hold);
            yield return Fade(1f, 0f);
        }

        IEnumerator Fade(float from, float to)
        {
            for (float t = 0f; t < fadeTime; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / fadeTime);
                group.alpha = Mathf.Lerp(from, to, k);
                transform.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, to > from ? k : 1f - k);
                yield return null;
            }
            group.alpha = to;
            transform.localScale = Vector3.one;
        }
    }
}
