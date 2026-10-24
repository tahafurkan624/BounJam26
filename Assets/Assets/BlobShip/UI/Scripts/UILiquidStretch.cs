// BlobShip - stretches a moving UI element like a drop (slider handles, sliding indicators, dragged items).
// The faster it moves, the longer and thinner it gets; when it stops it wobbles back to round.

using UnityEngine;

namespace BlobShip.UI
{
    [AddComponentMenu("UI/Effects/Liquid Stretch (BlobShip)")]
    public class UILiquidStretch : MonoBehaviour
    {
        [Tooltip("What gets stretched (default: this). Use the handle's graphic, not the slider itself.")]
        public RectTransform visual;
        [Tooltip("Stretch per canvas pixel/second of speed.")]
        public float stretchPerSpeed = 0.0012f;
        public float maxStretch = 1.6f;
        [Range(50f, 800f)] public float stiffness = 300f;
        [Range(1f, 40f)] public float damping = 10f;
        [Tooltip("Only horizontal motion stretches (sliders). Off = the dominant axis stretches.")]
        public bool horizontalOnly = true;

        Vector3 _lastPos, _base;
        Vector2 _s = Vector2.one, _v;
        bool _init;

        void OnEnable()
        {
            if (visual == null) visual = (RectTransform)transform;
            if (!_init) { _base = visual.localScale; _init = true; }
            _lastPos = transform.position; _s = Vector2.one; _v = Vector2.zero;
        }

        void OnDisable() { if (visual != null && _init) visual.localScale = _base; }

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (dt <= 0f) return;
            Vector3 wp = transform.position;
            Vector3 delta = wp - _lastPos;
            _lastPos = wp;
            float canvasScale = transform.parent != null ? Mathf.Max(1e-5f, Mathf.Abs(transform.parent.lossyScale.x)) : 1f;
            float sx = Mathf.Abs(delta.x) / canvasScale / dt, sy = Mathf.Abs(delta.y) / canvasScale / dt;
            Vector2 target = Vector2.one;
            if (horizontalOnly || sx >= sy)
            {
                float st = Mathf.Min(1f + sx * stretchPerSpeed, maxStretch);
                target = new Vector2(st, 1f / Mathf.Sqrt(st));
            }
            else
            {
                float st = Mathf.Min(1f + sy * stretchPerSpeed, maxStretch);
                target = new Vector2(1f / Mathf.Sqrt(st), st);
            }
            const int steps = 4;
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                Vector2 acc = (target - _s) * stiffness - _v * damping;
                _v += acc * h; _s += _v * h;
            }
            visual.localScale = new Vector3(_base.x * _s.x, _base.y * _s.y, _base.z);
        }
    }
}
