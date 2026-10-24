// BlobShip - one-line liquid UI effects.
//   UILiquidFX.Splash(dropIconRect);            // drop collected: droplets + ripple from the icon
//   UILiquidFX.Ripple(buttonRect, localPoint);  // ripple only
//   UILiquidFX.Celebrate(fullScreenRect);       // level complete rain (3.5 s)
// Effects are spawned as children that ignore layout groups and remove themselves when finished.
// They run on unscaled time, so they also play in the pause menu.

using UnityEngine;
using UnityEngine.UI;

namespace BlobShip.UI
{
    public static class UILiquidFX
    {
        public static UILiquidParticles Spawn(RectTransform parent, LiquidPreset preset, Vector2 localPos,
                                              Vector2? areaSize = null, bool continuous = false)
        {
            if (parent == null) return null;
            var go = new GameObject("LiquidFX_" + preset, typeof(RectTransform), typeof(CanvasRenderer),
                                    typeof(LayoutElement), typeof(UILiquidParticles));
            go.layer = parent.gameObject.layer;
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.SetAsLastSibling();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = areaSize ?? new Vector2(4f, 4f);
            rt.localPosition = new Vector3(localPos.x, localPos.y, 0f);   // parent-local point (rect space)

            var fx = go.GetComponent<UILiquidParticles>();
            fx.raycastTarget = false;
            fx.ApplyPreset(preset);
            fx.prewarm = false;
            fx.destroyWhenDone = true;
            fx.Stop(); fx.Clear();          // OnEnable may already have started the default preset
            if (continuous) fx.Play();
            return fx;
        }

        /// <summary>Droplet burst + ripple centred on <paramref name="target"/> (e.g. the HUD drop icon).</summary>
        public static void Splash(RectTransform target, int droplets = 14, float scale = 1f)
        {
            if (target == null) return;
            Vector2 c = target.rect.center;
            var drops = Spawn(target, LiquidPreset.DropSplash, c);
            if (drops != null) { drops.size *= scale; drops.speed *= scale; drops.Burst(droplets, Vector2.zero); }
            Ripple(target, c, scale);
        }

        /// <summary>Two expanding rings at a point in <paramref name="target"/>'s local space.</summary>
        public static void Ripple(RectTransform target, Vector2 localPoint, float scale = 1f)
        {
            var rings = Spawn(target, LiquidPreset.Ripple, localPoint);
            if (rings == null) return;
            rings.size *= scale;
            rings.Burst(1, Vector2.zero);
            rings.lifetime += new Vector2(0.15f, 0.15f);
            rings.Burst(1, Vector2.zero);
        }

        /// <summary>Droplets and glints raining over <paramref name="area"/> for a few seconds.</summary>
        public static UILiquidParticles Celebrate(RectTransform area, float seconds = 3.5f)
        {
            if (area == null) return null;
            var fx = Spawn(area, LiquidPreset.Celebration, area.rect.center, area.rect.size, continuous: false);
            if (fx == null) return null;
            fx.duration = seconds;
            var rt = fx.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
            fx.Play();
            return fx;
        }
    }
}
