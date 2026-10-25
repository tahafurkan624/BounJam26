using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace BounJam
{
    /// <summary>
    /// The second half of the main menu's Play wipe (UI_LiquidWipe.shader): the liquid that rose over the menu is moved
    /// to a canvas of its own that survives the scene load, and drains away once the level has drawn behind it.
    /// </summary>
    public class ScreenWipe : MonoBehaviour
    {
        static readonly int LevelId = Shader.PropertyToID("_Level");
        static readonly int WipeTimeId = Shader.PropertyToID("_WipeTime");

        Material material;
        float time;

        /// <summary>Takes over the wipe drawn with <paramref name="material"/>, already covering the screen.</summary>
        public static void Drain(Canvas from, Material material, float elapsed)
        {
            var go = new GameObject("ScreenWipe", typeof(Canvas));
            DontDestroyOnLoad(go);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var image = new GameObject("Liquid", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            image.transform.SetParent(go.transform, false);
            var rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            image.raycastTarget = false;
            var copy = new Material(material);
            image.material = copy;
            var wipe = go.AddComponent<ScreenWipe>();
            wipe.material = copy;
            wipe.time = elapsed;
        }

        void OnEnable() => GlassSceneCapture.Acquire();

        void OnDisable() => GlassSceneCapture.Release();

        IEnumerator Start()
        {
            // Let the new scene load and draw a couple of frames behind the liquid.
            for (int i = 0; i < 3; i++)
            {
                Step(1.15f);
                yield return null;
            }
            const float drain = 0.8f;
            for (float t = 0f; t < drain; t += Time.unscaledDeltaTime)
            {
                float k = t / drain;
                Step(Mathf.Lerp(1.15f, -0.2f, k * k * (3f - 2f * k)));
                yield return null;
            }
            Destroy(gameObject);
        }

        void Step(float level)
        {
            time += Time.unscaledDeltaTime;
            material.SetFloat(LevelId, level);
            material.SetFloat(WipeTimeId, time);
        }
    }
}
