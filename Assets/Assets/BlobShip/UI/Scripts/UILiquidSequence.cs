// BlobShip - "Level complete" sequence: splashes each earned drop icon one after another,
// then (optionally) rains droplets over the screen. Runs on unscaled time.
//
//   Assign the drop icons (earned ones first), set "earned", call Play() when the panel opens
//   or tick "playOnEnable".

using System.Collections;
using UnityEngine;

namespace BlobShip.UI
{
    [AddComponentMenu("UI/Effects/Liquid Sequence (BlobShip)")]
    public class UILiquidSequence : MonoBehaviour
    {
        public RectTransform[] dropIcons;
        [Tooltip("How many of the icons were earned (they splash in order).")]
        public int earned = 2;
        public float firstDelay = 0.35f;
        public float interval = 0.28f;
        [Tooltip("Pop each earned icon (scale punch) as it splashes.")]
        public bool punchIcons = true;
        [Tooltip("Full-screen rect for the droplet rain (leave empty to skip).")]
        public RectTransform celebrationArea;
        public float celebrationSeconds = 3.5f;
        public bool playOnEnable = true;

        void OnEnable() { if (playOnEnable) Play(); }

        public void Play() { StopAllCoroutines(); StartCoroutine(Run()); }

        IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(firstDelay);
            if (celebrationArea != null) UILiquidFX.Celebrate(celebrationArea, celebrationSeconds);
            for (int i = 0; i < Mathf.Min(earned, dropIcons != null ? dropIcons.Length : 0); i++)
            {
                var icon = dropIcons[i];
                if (icon == null) continue;
                UILiquidFX.Splash(icon, 16, 1.1f);
                if (punchIcons) StartCoroutine(Punch(icon));
                yield return new WaitForSecondsRealtime(interval);
            }
        }

        static IEnumerator Punch(RectTransform t)
        {
            Vector3 baseScale = t.localScale;
            float d = 0.35f;
            for (float e = 0; e < d; e += Time.unscaledDeltaTime)
            {
                float k = e / d;
                float s = 1f + Mathf.Sin(k * Mathf.PI) * 0.22f * (1f - k * 0.5f);     // squash & settle
                t.localScale = new Vector3(baseScale.x * s, baseScale.y * (2f - s), baseScale.z);
                yield return null;
            }
            t.localScale = baseScale;
        }
    }
}
