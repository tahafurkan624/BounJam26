// BlobShip - 3D liquid UI: shared state (Sıvı / Katı), palette and material loading.
//   LiquidUI.SetSolid(true);    // every liquid button freezes (call it from your form switch too)
using System;
using UnityEngine;

namespace BlobShip.UI.Liquid3D
{
    [Serializable]
    public struct LiquidColors
    {
        [ColorUsage(false, true)] public Color core;
        [ColorUsage(false, true)] public Color rim;
        [Tooltip("How fast each colour channel is absorbed. Turquoise absorbs red.")]
        public Vector3 absorb;
        public LiquidColors(Color core, Color rim, Vector3 absorb) { this.core = core; this.rim = rim; this.absorb = absorb; }
    }

    /// <summary>The game's own colours: turquoise / mint liquid, icy glass.</summary>
    public static class LiquidPalette
    {
        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        public static LiquidColors Turquoise => new LiquidColors(Hex("#0FB5A6"), Hex("#8AF7E2"), new Vector3(2.6f, 0.32f, 0.55f));
        public static LiquidColors Cyan => new LiquidColors(Hex("#0FA7B8"), Hex("#86ECF5"), new Vector3(2.8f, 0.45f, 0.4f));
        public static LiquidColors Aqua => new LiquidColors(Hex("#1592B8"), Hex("#8AD8F7"), new Vector3(2.9f, 0.7f, 0.3f));
        public static LiquidColors ForIndex(int i) => i % 3 == 0 ? Turquoise : i % 3 == 1 ? Cyan : Aqua;
        public static Color GlassTint => new Color(0.86f, 0.95f, 1.0f, 1f);
        public static Color GlassEdge => new Color(0.4f, 0.9f, 1.0f, 1f);
        public static Color GlassMilk => new Color(0.035f, 0.06f, 0.075f, 0f);
        public static Color GlassRim => Hex("#8FE9FF");
    }

    public static class LiquidUI
    {
        static float s_target, s_value;
        static int s_lastFrame = -1;
        static readonly int SolidId = Shader.PropertyToID("_BlobLiquidSolid");
        static Material s_slab, s_sim;

        /// <summary>Raised when Sıvı / Katı changes (true = Katı).</summary>
        public static event Action<bool> SolidChanged;
        public static bool Solid => s_target > 0.5f;
        /// <summary>0 = liquid .. 1 = frozen, eased.</summary>
        public static float SolidValue => s_value;

        public static void SetSolid(bool on)
        {
            float t = on ? 1f : 0f;
            if (Mathf.Approximately(t, s_target)) return;
            s_target = t;
            SolidChanged?.Invoke(on);
        }

        /// <summary>Called by every slab; does its work once per frame.</summary>
        public static void Tick()
        {
            if (Time.frameCount == s_lastFrame) return;
            s_lastFrame = Time.frameCount;
            s_value += (s_target - s_value) * Mathf.Min(1f, Time.unscaledDeltaTime * 4f);
            if (Mathf.Abs(s_target - s_value) < 0.001f) s_value = s_target;
            Shader.SetGlobalFloat(SolidId, s_value);
        }

        public static Material SlabMaterial
        {
            get
            {
                if (s_slab == null) s_slab = Resources.Load<Material>("BlobShipLiquid3D/M_LiquidSlab");
                if (s_slab == null) { var sh = Shader.Find("BlobShip/UI/Liquid Slab"); if (sh != null) s_slab = new Material(sh); }
                return s_slab;
            }
        }

        public static Material SimMaterial
        {
            get
            {
                if (s_sim == null) s_sim = Resources.Load<Material>("BlobShipLiquid3D/M_LiquidSim");
                if (s_sim == null) { var sh = Shader.Find("Hidden/BlobShip/LiquidSim"); if (sh != null) s_sim = new Material(sh); }
                return s_sim;
            }
        }
    }
}
