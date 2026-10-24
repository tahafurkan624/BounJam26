// BlobShip - liquid buttons / cards.
// Add to a Button (or any UI element). It turns the element into a drop of liquid glass:
//   hover : swells like jelly (springy overshoot), the shape bulges towards the pointer,
//           a soft light sheen inside the glass follows the pointer with a lag
//   press : squashes and spreads, a ripple runs across the surface, droplets + rings splash out
//   release / exit : rebounds and wobbles back
// Uses unscaled time, so it also works in the pause menu. Adds UILiquidJelly automatically.

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlobShip.UI
{
    [AddComponentMenu("UI/Effects/Liquid Interactable (BlobShip)")]
    [RequireComponent(typeof(RectTransform))]
    public class UILiquidInteractable : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerMoveHandler
    {
        [Header("Jelly body")]
        [Tooltip("What squashes and wobbles (default: this object, children follow).")]
        public RectTransform body;
        public float hoverScale = 1.035f;
        public Vector2 pressSquash = new Vector2(1.07f, 0.9f);
        [Range(50f, 800f)] public float stiffness = 320f;
        [Tooltip("Lower = wobblier.")]
        [Range(1f, 40f)] public float damping = 11f;
        public float enterKick = 0.8f;
        public float releaseKick = 1.6f;

        [Header("Liquid surface")]
        [Tooltip("Deforms the shape itself. Added automatically to the body's Graphic if empty.")]
        public UILiquidJelly jelly;
        public bool autoAddJelly = true;

        [Header("Sheen (light inside the glass)")]
        public bool sheen = true;
        [Range(0.2f, 2f)] public float sheenSize = 0.9f;
        [Range(0f, 1f)] public float sheenAlpha = 0.55f;
        public float sheenFollow = 14f;

        [Header("Splash")]
        public int pressDroplets = 6;
        public bool pressRings = true;
        public bool hoverRipple = true;
        [Range(0.3f, 2f)] public float fxScale = 0.7f;

        Selectable _selectable;
        Vector3 _baseScale;
        Vector2 _scale = Vector2.one, _vel;
        bool _hover, _pressed, _init;
        Vector2 _pointer, _sheenPos;
        float _sheenA;
        RawImage _sheen;
        RectTransform _sheenRt;
        static Texture2D s_sheenTex;

        bool Interactable => _selectable == null || _selectable.IsInteractable();

        void Awake() { Init(); }

        void Init()
        {
            if (_init) return;
            _init = true;
            if (body == null) body = (RectTransform)transform;
            _baseScale = body.localScale;
            _selectable = GetComponent<Selectable>();
            if (jelly == null)
            {
                jelly = body.GetComponent<UILiquidJelly>();
                if (jelly == null && autoAddJelly && body.GetComponent<Graphic>() != null)
                    jelly = body.gameObject.AddComponent<UILiquidJelly>();
            }
            if (sheen) CreateSheen();
        }

        void CreateSheen()
        {
            if (s_sheenTex == null) s_sheenTex = Resources.Load<Texture2D>("BlobShipUI/T_LiquidSheen");
            var go = new GameObject("LiquidSheen", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(LayoutElement));
            go.layer = gameObject.layer;
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            _sheenRt = (RectTransform)go.transform;
            _sheenRt.SetParent(body, false);
            _sheenRt.SetSiblingIndex(0);                       // above the button background, below its label
            _sheenRt.anchorMin = _sheenRt.anchorMax = new Vector2(0.5f, 0.5f);
            _sheen = go.GetComponent<RawImage>();
            _sheen.texture = s_sheenTex;
            _sheen.raycastTarget = false;
            _sheen.color = new Color(1f, 1f, 1f, 0f);
            _sheen.enabled = false;
        }

        Vector2 Local(PointerEventData e, Camera cam)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(body, e.position, cam, out Vector2 l);
            return l;
        }

        public void OnPointerEnter(PointerEventData e)
        {
            Init();
            if (!Interactable) return;
            _hover = true;
            _pointer = Local(e, e.enterEventCamera);
            _sheenPos = _pointer;
            _vel += new Vector2(enterKick, enterKick);
            if (jelly != null) { jelly.SetPointer(_pointer, true); if (hoverRipple) jelly.AddRipple(_pointer, 0.35f); }
        }

        public void OnPointerMove(PointerEventData e)
        {
            if (!_hover) return;
            _pointer = Local(e, e.enterEventCamera);
            if (jelly != null) jelly.SetPointer(_pointer, true);
        }

        public void OnPointerExit(PointerEventData e)
        {
            _hover = false; _pressed = false;
            if (jelly != null) jelly.SetPointer(_pointer, false);
            _vel -= new Vector2(enterKick * 0.5f, enterKick * 0.5f);
        }

        public void OnPointerDown(PointerEventData e)
        {
            Init();
            if (!Interactable) return;
            _pressed = true;
            Vector2 p = Local(e, e.pressEventCamera);
            _pointer = p;
            if (jelly != null) jelly.AddRipple(p, 1f);
            if (pressRings) UILiquidFX.Ripple(body, p, fxScale);
            if (pressDroplets > 0)
            {
                var d = UILiquidFX.Spawn(body, LiquidPreset.DropSplash, p);
                if (d != null) { d.size *= fxScale; d.speed *= fxScale * 0.8f; d.Burst(pressDroplets, Vector2.zero); }
            }
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (!_pressed) return;
            _pressed = false;
            _vel += new Vector2(-releaseKick * 0.75f, releaseKick);   // pops back up taller, then settles
        }

        void OnDisable()
        {
            if (body != null && _init) body.localScale = _baseScale;
            _scale = Vector2.one; _vel = Vector2.zero; _hover = _pressed = false; _sheenA = 0f;
            if (_sheen != null) _sheen.enabled = false;
        }

        void Update()
        {
            if (!_init) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (dt <= 0f) return;

            Vector2 target = _pressed ? pressSquash : (_hover ? new Vector2(hoverScale, hoverScale) : Vector2.one);
            const int steps = 4;
            float h = dt / steps;
            for (int i = 0; i < steps; i++)                 // damped spring = jelly wobble
            {
                Vector2 acc = (target - _scale) * stiffness - _vel * damping;
                _vel += acc * h;
                _scale += _vel * h;
            }
            body.localScale = new Vector3(_baseScale.x * _scale.x, _baseScale.y * _scale.y, _baseScale.z);

            if (_sheen != null)
            {
                Rect r = body.rect;
                float s = Mathf.Min(r.width, r.height) * sheenSize;
                _sheenRt.sizeDelta = new Vector2(s * 1.35f, s);
                Vector2 tgt = _pointer;
                float ix = Mathf.Min(s * 0.45f, r.width * 0.5f), iy = Mathf.Min(s * 0.3f, r.height * 0.5f);
                tgt.x = Mathf.Clamp(tgt.x, r.xMin + ix, r.xMax - ix);
                tgt.y = Mathf.Clamp(tgt.y, r.yMin + iy, r.yMax - iy);
                _sheenPos = Vector2.Lerp(_sheenPos, tgt, 1f - Mathf.Exp(-sheenFollow * dt));
                _sheenRt.localPosition = new Vector3(_sheenPos.x, _sheenPos.y, 0f);
                float ta = _hover ? sheenAlpha * (_pressed ? 1.35f : 1f) : 0f;
                _sheenA = Mathf.MoveTowards(_sheenA, ta, dt * 3f);
                Color c = _sheen.color; c.a = _sheenA; _sheen.color = c;
                _sheen.enabled = _sheenA > 0.002f;
            }
        }
    }
}
