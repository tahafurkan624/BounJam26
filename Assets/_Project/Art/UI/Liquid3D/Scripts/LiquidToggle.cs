// BlobShip - the Sıvı / Katı switch: one end liquid, the other clear glass. Pressing it sloshes the liquid across
// and (optionally) freezes every liquid button through LiquidUI.SetSolid.
//   GetComponent<LiquidToggle>().SetSolid(true);
//   LiquidUI.SolidChanged += solid => blob.SetForm(solid);
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BlobShip.UI.Liquid3D
{
    [RequireComponent(typeof(LiquidSlab))]
    [AddComponentMenu("UI/BlobShip/Liquid Toggle 3D (Sıvı-Katı)")]
    public class LiquidToggle : MonoBehaviour
    {
        public bool solid;
        [Tooltip("Label on the liquid (left) end.")]
        public Graphic liquidLabel;
        [Tooltip("Label on the solid (right) end.")]
        public Graphic solidLabel;
        [Range(0f, 1f)] public float inactiveAlpha = 0.35f;
        [Range(0.15f, 0.45f)] public float liquidShare = 0.27f;
        [Tooltip("Also freeze / melt every liquid button (LiquidUI.SetSolid).")]
        public bool driveGlobal = true;
        public UnityEvent<bool> onChanged = new UnityEvent<bool>();

        LiquidSlab _slab;
        float _c, _v;

        void Awake()
        {
            _slab = GetComponent<LiquidSlab>();
            _slab.isSwitch = true;
            _slab.onClick.AddListener(Flip);
        }

        void OnEnable()
        {
            if (_slab == null) _slab = GetComponent<LiquidSlab>();
            if (driveGlobal) { solid = LiquidUI.Solid; LiquidUI.SolidChanged += OnGlobal; }
            _c = Target; _v = 0f;
            Apply();
        }

        void OnDisable() { LiquidUI.SolidChanged -= OnGlobal; }

        void OnGlobal(bool s) { if (s != solid) SetSolid(s, false); }

        float Target => solid ? 1f - liquidShare : liquidShare;

        public void Flip() => SetSolid(!solid);

        public void SetSolid(bool on, bool notify = true)
        {
            if (on == solid) return;
            solid = on;
            // the liquid travels: a row of pushes along the way gives the slosh
            float from = on ? 0.2f : 0.8f, to = on ? 0.8f : 0.2f;
            for (int i = 0; i < 4; i++) _slab.Impulse(new Vector2(from + (to - from) * i / 3f, 0.5f), 0.3f, (on ? 1f : -1f) * 0.28f);
            if (driveGlobal) LiquidUI.SetSolid(on);
            if (notify) onChanged.Invoke(on);
            Apply();
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            _v += ((Target - _c) * 140f - _v * 7f) * dt;       // springy: overshoots and sloshes back
            _c += _v * dt;
            _slab.liquidCentre = _c;
            _slab.liquidHalf = liquidShare;
        }

        void Apply()
        {
            if (liquidLabel != null) liquidLabel.canvasRenderer.SetAlpha(solid ? inactiveAlpha : 1f);
            if (solidLabel != null) solidLabel.canvasRenderer.SetAlpha(solid ? 1f : inactiveAlpha);
        }
    }
}
