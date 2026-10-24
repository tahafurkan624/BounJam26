// BlobShip - liquid segmented control (the "Sıvı / Katı" pill).
// The highlight pill flows to the selected option like a drop of liquid: its leading edge rushes ahead,
// the trailing edge catches up, it gets thinner while stretched and wobbles when it lands.
//
// Setup: indicator = the highlight pill Image (anchors min = max, e.g. middle-centre),
//        options   = the option RectTransforms (same order), optional toggles = their Toggles.
//        Call Select(i) from code (e.g. on the Space key), or let the toggles drive it.

using UnityEngine;
using UnityEngine.UI;

namespace BlobShip.UI
{
    [AddComponentMenu("UI/Effects/Liquid Segmented (BlobShip)")]
    public class UILiquidSegmented : MonoBehaviour
    {
        public RectTransform indicator;
        public RectTransform[] options;
        [Tooltip("Optional: Toggles in the same order as options; turning one on selects it.")]
        public Toggle[] toggles;
        public int selected;

        [Header("Flow")]
        public float leadStiffness = 520f;
        public float trailStiffness = 150f;
        [Range(0.2f, 1.2f)] public float dampingRatio = 0.55f;
        [Tooltip("How much thinner the pill gets while stretched (liquid keeps its volume).")]
        [Range(0f, 0.6f)] public float thinning = 0.25f;
        public bool splashOnSelect = true;

        float _l, _r, _vl, _vr, _baseH;
        bool _snapped;

        void Start()
        {
            if (indicator != null) _baseH = indicator.sizeDelta.y;
            if (toggles != null)
                for (int i = 0; i < toggles.Length; i++)
                {
                    int idx = i;
                    if (toggles[i] != null) toggles[i].onValueChanged.AddListener(on => { if (on) Select(idx); });
                }
            Snap();
        }

        public void Select(int index)
        {
            if (options == null || index < 0 || index >= options.Length || index == selected) return;
            selected = index;
            if (splashOnSelect && options[index] != null) UILiquidFX.Splash(options[index], 6, 0.6f);
        }

        public void Snap()
        {
            if (!TargetEdges(out float l, out float r)) return;
            _l = l; _r = r; _vl = _vr = 0f; _snapped = true; Apply();
        }

        bool TargetEdges(out float l, out float r)
        {
            l = r = 0f;
            if (indicator == null || options == null || selected < 0 || selected >= options.Length || options[selected] == null) return false;
            var parent = indicator.parent as RectTransform;
            if (parent == null) return false;
            var corners = new Vector3[4];
            options[selected].GetWorldCorners(corners);
            l = parent.InverseTransformPoint(corners[0]).x;
            r = parent.InverseTransformPoint(corners[2]).x;
            return true;
        }

        void Update()
        {
            if (!TargetEdges(out float tl, out float tr)) return;
            if (!_snapped) { Snap(); return; }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (dt <= 0f) return;
            bool movingRight = (tl + tr) > (_l + _r);
            float kl = movingRight ? trailStiffness : leadStiffness;
            float kr = movingRight ? leadStiffness : trailStiffness;
            const int steps = 4;
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                _vl += ((tl - _l) * kl - _vl * 2f * dampingRatio * Mathf.Sqrt(kl)) * h; _l += _vl * h;
                _vr += ((tr - _r) * kr - _vr * 2f * dampingRatio * Mathf.Sqrt(kr)) * h; _r += _vr * h;
            }
            Apply();
        }

        void Apply()
        {
            if (indicator == null) return;
            float targetW = 1f;
            if (options != null && selected >= 0 && selected < options.Length && options[selected] != null)
                targetW = Mathf.Max(1f, options[selected].rect.width);
            float w = Mathf.Max(4f, _r - _l);
            float stretch = Mathf.Max(0f, w / targetW - 1f);
            float hgt = (_baseH > 0f ? _baseH : indicator.sizeDelta.y) * (1f - thinning * Mathf.Clamp01(stretch));
            indicator.sizeDelta = new Vector2(w, hgt);
            Vector3 lp = indicator.localPosition;
            indicator.localPosition = new Vector3((_l + _r) * 0.5f + (indicator.pivot.x - 0.5f) * w, lp.y, lp.z);
        }
    }
}
