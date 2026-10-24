// BlobShip - liquid feedback for buttons / toggles.
// Add to any Button (or the "Sıvı / Katı" toggle halves): a ripple + a few droplets at the press point,
// and an optional soft ripple on hover.

using UnityEngine;
using UnityEngine.EventSystems;

namespace BlobShip.UI
{
    [AddComponentMenu("UI/Effects/Liquid Button FX (BlobShip)")]
    [RequireComponent(typeof(RectTransform))]
    public class UILiquidButtonFX : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler
    {
        [Tooltip("Droplets thrown on press (0 = ripple only).")]
        public int droplets = 6;
        [Range(0.3f, 2f)] public float scale = 0.7f;
        public bool rippleOnHover = false;

        RectTransform _rt;
        void Awake() { _rt = (RectTransform)transform; }

        public void OnPointerDown(PointerEventData e)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, e.position, e.pressEventCamera, out var local))
                local = _rt.rect.center;
            UILiquidFX.Ripple(_rt, local, scale);
            if (droplets > 0)
            {
                var d = UILiquidFX.Spawn(_rt, LiquidPreset.DropSplash, local);
                if (d != null) { d.size *= scale; d.speed *= scale * 0.8f; d.Burst(droplets, Vector2.zero); }
            }
        }

        public void OnPointerEnter(PointerEventData e)
        {
            if (!rippleOnHover) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, e.position, e.enterEventCamera, out var local))
                local = _rt.rect.center;
            UILiquidFX.Ripple(_rt, local, scale * 0.6f);
        }

        /// <summary>Call from code (e.g. when the Liquid/Solid mode switches with the Space key).</summary>
        public void Play() => UILiquidFX.Splash(_rt != null ? _rt : (RectTransform)transform, droplets, scale);
    }
}
