// BlobShip - makes a TextMeshPro label read like a white LED inside the liquid: bright, crisp core and a small,
// soft halo (the halo takes the liquid's colour). The brightness goes over 1, so the UI camera's Bloom adds a gentle glow.
using TMPro;
using UnityEngine;

namespace BlobShip.UI.Liquid3D
{
    [RequireComponent(typeof(TMP_Text))]
    [AddComponentMenu("UI/BlobShip/Liquid LED Label")]
    public class LiquidLEDLabel : MonoBehaviour
    {
        [Range(1f, 4f)] public float brightness = 1.5f;
        [ColorUsage(true, true)] public Color halo = new Color(0.55f, 1f, 0.95f, 0.5f);
        [Range(0f, 1f)] public float haloSize = 0.28f;

        static readonly int FaceColor = Shader.PropertyToID("_FaceColor"), GlowColor = Shader.PropertyToID("_GlowColor"),
            GlowOuter = Shader.PropertyToID("_GlowOuter"), GlowInner = Shader.PropertyToID("_GlowInner"),
            GlowPower = Shader.PropertyToID("_GlowPower"), GlowOffset = Shader.PropertyToID("_GlowOffset");

        void OnEnable() { if (Application.isPlaying) Apply(); }

        public void Apply()
        {
            var t = GetComponent<TMP_Text>();
            if (t == null) return;
            t.color = Color.white;
            var m = t.fontMaterial;                         // per-label copy
            m.SetColor(FaceColor, new Color(brightness, brightness * 0.985f, brightness, 1f));
            m.EnableKeyword("GLOW_ON");
            m.SetColor(GlowColor, halo);
            m.SetFloat(GlowOffset, 0f);
            m.SetFloat(GlowInner, 0.05f);
            m.SetFloat(GlowOuter, haloSize);
            m.SetFloat(GlowPower, 0.75f);
            t.UpdateMeshPadding();
            t.SetAllDirty();
        }
    }
}
