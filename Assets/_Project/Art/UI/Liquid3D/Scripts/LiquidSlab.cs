// BlobShip - a thick 3D liquid (or glass) button / panel inside a Screen Space - Camera canvas.
// Put it on a UI object (a Button, a panel...). It builds the 3D body from the RectTransform size, runs a small
// wave simulation on the GPU (click = rings that bounce off the edges, hover = a wake, idle = a soft shimmer),
// lifts on hover, squashes on press, and keeps the labels (TextMeshPro / Text children) sharp in front of it.
// The object's own Image is hidden but still catches clicks, so Button / EventTrigger keep working.
// Needs: URP asset "Opaque Texture" on (Tools > BlobShip > Liquid UI > Set Up URP does it).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlobShip.UI.Liquid3D
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("UI/BlobShip/Liquid Slab 3D")]
    public class LiquidSlab : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerMoveHandler, IPointerClickHandler
    {
        public enum SlabShape { Pill, Rounded }

        [Header("Shape")]
        public SlabShape shape = SlabShape.Pill;
        [Tooltip("Corner radius in canvas units (Rounded only).")]
        public float cornerRadius = 40f;
        [Tooltip("How round the front edge is, canvas units. 0 = auto (42% of the height).")]
        public float edgeRadius = 0f;
        [Tooltip("Half thickness, canvas units. 0 = auto (40% of the height).")]
        public float thickness = 0f;

        [Header("Look")]
        [Tooltip("Clear glass instead of coloured liquid (panel).")]
        public bool glass;
        public LiquidColors colors = LiquidPalette.Turquoise;
        [Range(0f, 2f)] public float glow = 0.4f;
        [Range(0f, 0.08f)] public float refraction = 0.022f;
        [Range(0f, 1f)] public float iridescence = 0.3f;
        [Range(0f, 1f)] public float frost = 0f;
        [Range(0f, 3f)] public float reflections = 1f;
        public Color glassTint = LiquidPalette.GlassTint;
        public Color glassEdge = LiquidPalette.GlassEdge;
        public Color glassMilk = LiquidPalette.GlassMilk;
        [Tooltip("Freezes when LiquidUI.SetSolid(true) (Katı).")]
        public bool reactsToSolid = true;

        [Header("Behaviour")]
        public bool interactive = true;
        [Tooltip("Hide this object's own Image (it still receives clicks).")]
        public bool hideGraphic = true;
        [Tooltip("Gentle idle shimmer and the occasional drop.")]
        public bool alive = true;
        [Range(48, 256)] public int simRows = 128;
        [Tooltip("Draw order above the canvas. Panel 1, buttons on it 2.")]
        public int sortingOffset = 2;
        [Tooltip("Size of one 'unit' in canvas units (0 = the shorter side). Panels use their button height.")]
        public float unitOverride = 0f;
        [Tooltip("Text in front of the body (auto: TMP / Text children that belong to this slab).")]
        public List<Graphic> labels = new List<Graphic>();
        public bool autoFindLabels = true;
        public UnityEvent onClick = new UnityEvent();

        // driven by LiquidToggle / LiquidPanel3D
        [System.NonSerialized] public float liquidCentre = 0.5f, liquidHalf = 2f;
        [System.NonSerialized] public bool isSwitch;
        [System.NonSerialized] public float fill = 1f, drain = 0f, seed = 1.7f, fade = 1f;
        [System.NonSerialized] public bool panelMask;
        // the surface still ripples under the pointer and rings on a click, without lifting, squashing or clicking
        [System.NonSerialized] public bool ripplesOnly;
        [System.NonSerialized] public readonly Vector4[] spillRects = new Vector4[3];
        [System.NonSerialized] public readonly Color[] spillColors = new Color[3];

        RectTransform _rt;
        GameObject _body;
        MeshFilter _mf;
        MeshRenderer _mr;
        Mesh _mesh;
        Material _mat;
        RenderTexture _simA, _simB;
        Vector2 _builtSize = new Vector2(-1, -1);
        float _builtR, _builtRb, _builtT;
        readonly List<Vector4> _pending = new List<Vector4>();
        readonly Vector4[] _imp = new Vector4[4];
        float _energy, _idleT;
        float _lift, _liftV, _sq = 1f, _sqV, _hoverT;
        bool _hovering;
        Vector2 _lastUv; bool _hasLast;
        readonly List<CanvasGroup> _groups = new List<CanvasGroup>();
        Graphic _ownGraphic; Color _ownColor; bool _ownHidden;

        static readonly int IdSim = Shader.PropertyToID("_SimTex"), IdSize = Shader.PropertyToID("_Size"), IdInner = Shader.PropertyToID("_InnerB"),
            IdSimP = Shader.PropertyToID("_SimParams"), IdCore = Shader.PropertyToID("_Core"), IdRim = Shader.PropertyToID("_Rim"),
            IdAbsorb = Shader.PropertyToID("_Absorb"), IdGlow = Shader.PropertyToID("_GlowK"), IdTint = Shader.PropertyToID("_GlassTint"),
            IdEdge = Shader.PropertyToID("_GlassEdge"), IdMilk = Shader.PropertyToID("_Milk"), IdGlass = Shader.PropertyToID("_GlassAll"),
            IdLiqC = Shader.PropertyToID("_LiqC"), IdLiqH = Shader.PropertyToID("_LiqHalf"), IdRefr = Shader.PropertyToID("_RefrK"),
            IdIri = Shader.PropertyToID("_Iri"), IdFrost = Shader.PropertyToID("_FrostAmt"), IdEnv = Shader.PropertyToID("_EnvK"),
            IdSolidMask = Shader.PropertyToID("_SolidMask"), IdHover = Shader.PropertyToID("_Hover"), IdFade = Shader.PropertyToID("_Fade"),
            IdFill = Shader.PropertyToID("_Fill"), IdDrain = Shader.PropertyToID("_Drain"), IdSeed = Shader.PropertyToID("_Seed"),
            IdPanel = Shader.PropertyToID("_PanelMask"), IdSimSize = Shader.PropertyToID("_SimSize"), IdImp = Shader.PropertyToID("_Imp"),
            IdDamp = Shader.PropertyToID("_Damp");
        static readonly int[] IdSpillR = { Shader.PropertyToID("_SpillR0"), Shader.PropertyToID("_SpillR1"), Shader.PropertyToID("_SpillR2") };
        static readonly int[] IdSpillC = { Shader.PropertyToID("_SpillC0"), Shader.PropertyToID("_SpillC1"), Shader.PropertyToID("_SpillC2") };

        public RectTransform Rect => _rt != null ? _rt : (_rt = (RectTransform)transform);
        public float Width => Rect.rect.width;
        public float Height => Rect.rect.height;
        public float Unit => unitOverride > 0f ? unitOverride : Mathf.Max(1f, Mathf.Min(Width, Height));
        public float CornerRadius => shape == SlabShape.Pill ? Mathf.Min(Width, Height) * 0.5f : Mathf.Min(cornerRadius, Mathf.Min(Width, Height) * 0.5f);
        public float EdgeRadius => Mathf.Min(edgeRadius > 0f ? edgeRadius : Mathf.Min(Width, Height) * 0.42f, CornerRadius);
        public float HalfThickness => thickness > 0f ? thickness : Mathf.Min(Width, Height) * 0.4f;
        /// <summary>Front surface z in this object's local space (where labels sit).</summary>
        public float FrontZ => _body != null ? _body.transform.localPosition.z - HalfThickness * _body.transform.localScale.z : -HalfThickness;
        public Renderer BodyRenderer => _mr;
        public Material BodyMaterial => _mat;

        // ------------------------------------------------------------------ lifecycle
        void OnEnable()
        {
            _rt = (RectTransform)transform;
            EnsureBody();
            if (Application.isPlaying) SetupInteraction();
            CollectGroups();
            if (autoFindLabels) FindLabels();
            Rebuild(true);
            _idleT = Random.Range(0.5f, 3f);
        }

        void OnDisable()
        {
            if (_body != null) _body.SetActive(false);
            if (_ownHidden && _ownGraphic != null) { _ownGraphic.color = _ownColor; _ownHidden = false; }
            _hovering = false;
        }

        void OnDestroy()
        {
            ReleaseSim();
            if (_body != null) DestroyObj(_body);
            if (_mat != null) DestroyObj(_mat);
            if (_mesh != null) DestroyObj(_mesh);
        }

        static void DestroyObj(Object o) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

        void OnTransformParentChanged() { CollectGroups(); }
        void OnRectTransformDimensionsChange() { _dirty = true; }

        void EnsureBody()
        {
            if (_body == null)
            {
                var t = transform.Find("__Liquid3D");
                _body = t != null ? t.gameObject : new GameObject("__Liquid3D");
                _body.hideFlags = HideFlags.HideAndDontSave;
                _body.transform.SetParent(transform, false);
                _mf = _body.GetComponent<MeshFilter>(); if (_mf == null) _mf = _body.AddComponent<MeshFilter>();
                _mr = _body.GetComponent<MeshRenderer>(); if (_mr == null) _mr = _body.AddComponent<MeshRenderer>();
                _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _mr.receiveShadows = false;
                _mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                _mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }
            _body.SetActive(true);
            _body.layer = gameObject.layer;
            if (_mat == null && LiquidUI.SlabMaterial != null)
            {
                _mat = new Material(LiquidUI.SlabMaterial) { name = "LiquidSlab (instance)", hideFlags = HideFlags.HideAndDontSave };
                _mr.sharedMaterial = _mat;
            }
        }

        void SetupInteraction()
        {
            _ownGraphic = GetComponent<Graphic>();
            if (_ownGraphic == null && interactive) _ownGraphic = gameObject.AddComponent<LiquidRaycastTarget>();
            if (_ownGraphic != null && hideGraphic && !(_ownGraphic is LiquidRaycastTarget) && !_ownHidden)
            {
                _ownColor = _ownGraphic.color;
                var c = _ownColor; c.a = 0f; _ownGraphic.color = c;      // invisible, still a raycast target
                _ownHidden = true;
            }
            if (_ownGraphic != null) _ownGraphic.raycastTarget = interactive;
        }

        void CollectGroups()
        {
            _groups.Clear();
            var t = transform;
            while (t != null)
            {
                var g = t.GetComponent<CanvasGroup>();
                if (g != null) { _groups.Add(g); if (g.ignoreParentGroups) break; }
                t = t.parent;
            }
        }

        /// <summary>Text children that belong to this slab (not to a slab nested inside it).</summary>
        public void FindLabels()
        {
            labels.Clear();
            foreach (var g in GetComponentsInChildren<Graphic>(true))
            {
                if (g.transform == transform || g is LiquidRaycastTarget) continue;
                if (g.GetComponentInParent<LiquidSlab>() != this) continue;
                if (g is Image && !(g is RawImage) && ((Image)g).sprite == null && g.color.a < 0.01f) continue;
                labels.Add(g);
            }
        }

        // ------------------------------------------------------------------ building
        public void Rebuild(bool force)
        {
            if (_rt == null) _rt = (RectTransform)transform;
            EnsureBody();
            float w = Width, d = Height;
            if (w < 2f || d < 2f) { _body.SetActive(false); return; }
            float r = CornerRadius, rb = EdgeRadius, t = HalfThickness;
            if (force || _mesh == null || new Vector2(w, d) != _builtSize || r != _builtR || rb != _builtRb || t != _builtT)
            {
                if (_mesh == null) _mesh = new Mesh { name = "LiquidSlab", hideFlags = HideFlags.HideAndDontSave };
                int rows = Mathf.Clamp(simRows, 48, 256);
                int capY = Mathf.Clamp(Mathf.RoundToInt(rows * 0.6f), 8, 160);
                int capX = Mathf.Clamp(Mathf.RoundToInt(capY * w / d), 8, 400);
                LiquidSlabMesh.Build(_mesh, w, d, r, rb, t, 240, 22, capX, capY, 0.06f * Unit);
                _mf.sharedMesh = _mesh;
                _builtSize = new Vector2(w, d); _builtR = r; _builtRb = rb; _builtT = t;
                if (Application.isPlaying) CreateSim();
            }
            ApplyStatic();
            UpdateBodyTransform();
        }

        void CreateSim()
        {
            ReleaseSim();
            if (LiquidUI.SimMaterial == null) return;
            int h = Mathf.Clamp(simRows, 48, 256);
            int w = Mathf.Clamp(Mathf.RoundToInt(h * Width / Height), 8, 1024);
            var fmt = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGHalf) ? RenderTextureFormat.RGHalf : RenderTextureFormat.ARGBHalf;
            _simA = new RenderTexture(w, h, 0, fmt) { name = "LiquidSimA", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            _simB = new RenderTexture(w, h, 0, fmt) { name = "LiquidSimB", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            _simA.Create(); _simB.Create();
            Graphics.Blit(Texture2D.blackTexture, _simA);
            Graphics.Blit(Texture2D.blackTexture, _simB);
            _energy = 0f;
        }

        void ReleaseSim()
        {
            if (_simA != null) { _simA.Release(); DestroyObj(_simA); _simA = null; }
            if (_simB != null) { _simB.Release(); DestroyObj(_simB); _simB = null; }
        }

        void ApplyStatic()
        {
            if (_mat == null) return;
            float u = Unit;
            _mat.SetVector(IdSize, new Vector4(Width, Height, HalfThickness, u));
            _mat.SetVector(IdInner, new Vector4(Width * 0.5f - EdgeRadius, Height * 0.5f - EdgeRadius, Mathf.Max(0.001f, CornerRadius - EdgeRadius), 0f));
            int sh = _simA != null ? _simA.height : 128, sw = _simA != null ? _simA.width : 128;
            _mat.SetVector(IdSimP, new Vector4(6f, 0.03f * u, 1f / sw, 1f / sh));
            _mat.SetTexture(IdSim, _simA != null ? (Texture)_simA : Texture2D.blackTexture);
            _mat.SetColor(IdCore, colors.core); _mat.SetColor(IdRim, colors.rim);
            _mat.SetVector(IdAbsorb, colors.absorb);
            _mat.SetFloat(IdGlow, glow);
            _mat.SetColor(IdTint, glassTint); _mat.SetColor(IdEdge, glassEdge); _mat.SetColor(IdMilk, glassMilk);
            _mat.SetFloat(IdGlass, glass ? 1f : 0f);
            _mat.SetFloat(IdRefr, refraction); _mat.SetFloat(IdIri, iridescence); _mat.SetFloat(IdFrost, frost); _mat.SetFloat(IdEnv, reflections);
            _mat.SetFloat(IdSolidMask, reactsToSolid && !glass ? 1f : 0f);
        }

        bool _dirty;
        void OnValidate() { _dirty = true; }          // rebuilt in LateUpdate (no object creation inside OnValidate)

        // ------------------------------------------------------------------ interaction
        bool UvFrom(PointerEventData e, out Vector2 uv)
        {
            uv = default;
            var cam = e.pressEventCamera != null ? e.pressEventCamera : e.enterEventCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, e.position, cam, out var lp)) return false;
            var r = Rect.rect;
            uv = new Vector2((lp.x - r.xMin) / r.width, (lp.y - r.yMin) / r.height);
            return Inside(uv);
        }

        public bool Inside(Vector2 uv)
        {
            float w = Width, d = Height, rr = CornerRadius;
            float px = (uv.x - 0.5f) * w, py = (uv.y - 0.5f) * d;
            float qx = Mathf.Abs(px) - w * 0.5f + rr, qy = Mathf.Abs(py) - d * 0.5f + rr;
            float sd = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - rr;
            return sd < 0f;
        }

        float Frozen => reactsToSolid && !glass && !isSwitch ? LiquidUI.SolidValue : 0f;

        public void OnPointerEnter(PointerEventData e) { if (interactive) _hovering = true; _hasLast = false; }
        public void OnPointerExit(PointerEventData e) { _hovering = false; _hasLast = false; }

        bool TakesPointer => interactive || ripplesOnly;

        // A ripples-only slab answers only where it is hit itself, not while the pointer is over a button on it.
        bool Ripples(PointerEventData e) => interactive || (ripplesOnly && e.pointerCurrentRaycast.gameObject == gameObject);

        public void OnPointerMove(PointerEventData e)
        {
            if (!Ripples(e) || !UvFrom(e, out var uv)) { _hasLast = false; return; }
            if (_hasLast)
            {
                float sp = new Vector2((uv.x - _lastUv.x) * Width, (uv.y - _lastUv.y) * Height).magnitude / Unit;
                if (sp > 0.004f) Impulse(uv, 0.06f, Mathf.Min(0.07f, sp * 0.5f) * (1f - 0.9f * Frozen));    // a small wake behind the pointer
            }
            _lastUv = uv; _hasLast = true;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (!Ripples(e) || !UvFrom(e, out var uv)) return;
            float k = (1f - 0.88f * Frozen) * (glass ? 0.5f : 1f);
            Impulse(uv, 0.06f, 1.25f * k);           // a sharp drop: a peak with a trough round it -> crisp rings
            Impulse(uv, 0.14f, -0.5f * k);
            if (!glass && interactive) _sqV -= 2.2f;
        }

        public void OnPointerClick(PointerEventData e) { if (interactive) onClick.Invoke(); }

        /// <summary>Push the surface. uv 0..1 over the rect, radius and strength in units (button heights).</summary>
        public void Impulse(Vector2 uv, float radius, float strength)
        {
            if (!Application.isPlaying) return;
            if (_pending.Count > 32) _pending.RemoveAt(0);
            _pending.Add(new Vector4(uv.x, uv.y, radius, strength));
            _energy = Mathf.Max(_energy, Mathf.Abs(strength) * 60f);
        }

        /// <summary>Press feedback from code (keyboard / gamepad submit).</summary>
        public void Poke() { Impulse(new Vector2(0.5f, 0.5f), 0.08f, 0.9f); if (!glass) _sqV -= 2.2f; }

        // ------------------------------------------------------------------ per frame
        void LateUpdate()
        {
            LiquidUI.Tick();
            if (_mat == null || _body == null) return;
            if (_dirty || new Vector2(Width, Height) != _builtSize) { bool f = _dirty; _dirty = false; Rebuild(f); }
            float dt = Application.isPlaying ? Mathf.Min(Time.unscaledDeltaTime, 0.05f) : 0f;

            if (Application.isPlaying)
            {
                // interactive / ripplesOnly change after OnEnable (LiquidPanel3D turns them off to drain, on again after
                // the next pour); a panel that only ripples gets its catch-all graphic the first time it opens
                if (_ownGraphic == null && TakesPointer) _ownGraphic = gameObject.AddComponent<LiquidRaycastTarget>();
                if (_ownGraphic != null && _ownGraphic.raycastTarget != TakesPointer) _ownGraphic.raycastTarget = TakesPointer;

                // springs: hover lift, press squash
                const float kS = 260f, kD = 12f;
                float h = dt / 4f;
                for (int i = 0; i < 4; i++)
                {
                    _liftV += ((_hovering ? 1f : 0f) - _lift) * kS * h - _liftV * kD * h; _lift += _liftV * h;
                    _sqV += (1f - _sq) * kS * h - _sqV * kD * h; _sq += _sqV * h;
                }
                _hoverT += ((_hovering ? 1f : 0f) - _hoverT) * Mathf.Min(1f, dt * 8f);

                // a living surface: faint shimmer + the occasional soft drop (liquid only)
                if (alive && !glass && Frozen < 0.5f)
                {
                    if (Random.value < 0.45f * dt * 60f) Impulse(new Vector2(Random.value, 0.15f + Random.value * 0.7f), 0.07f, (Random.value - 0.5f) * 0.06f);
                    _idleT -= dt;
                    if (_idleT <= 0f) { _idleT = 2f + Random.value * 3f; Impulse(new Vector2(0.15f + Random.value * 0.7f, 0.3f + Random.value * 0.4f), 0.06f, 0.22f); }
                }
                StepSim(dt);
            }
            UpdateBodyTransform();

            ApplyStatic();
            float a = fade;
            foreach (var g in _groups) if (g != null) a *= g.alpha;
            _mat.SetFloat(IdFade, a);
            _mat.SetFloat(IdHover, _hoverT);
            _mat.SetFloat(IdLiqC, liquidCentre); _mat.SetFloat(IdLiqH, liquidHalf);
            _mat.SetFloat(IdFill, fill); _mat.SetFloat(IdDrain, drain); _mat.SetFloat(IdSeed, seed);
            _mat.SetFloat(IdPanel, panelMask ? 1f : 0f);
            for (int i = 0; i < 3; i++) { _mat.SetVector(IdSpillR[i], spillRects[i]); _mat.SetColor(IdSpillC[i], spillColors[i]); }
            _mr.enabled = a > 0.001f;
            UpdateSorting();
        }

        void UpdateBodyTransform()
        {
            if (_body == null) return;
            float u = Unit, s = 1f - _sq;
            var c = Rect.rect.center;
            _body.transform.localPosition = new Vector3(c.x, c.y, -_lift * 0.14f * u + s * 0.35f * u);
            _body.transform.localRotation = Quaternion.identity;
            _body.transform.localScale = new Vector3(1f + s * 0.06f + _lift * 0.012f, 1f + s * 0.1f + _lift * 0.012f, Mathf.Max(0.3f, 1f - s * 0.9f));
            if (!Application.isPlaying) return;
            // clear of the ripples: a frozen wave can bulge the face ~0.06 units forward and cut into the text
            float front = FrontZ - 0.08f * u;
            foreach (var l in labels)
            {
                if (l == null) continue;
                var lp = l.rectTransform.localPosition;
                if (l.transform.parent == transform) l.rectTransform.localPosition = new Vector3(lp.x, lp.y, front);
            }
        }

        void UpdateSorting()
        {
            var canvas = transform.parent != null ? transform.parent.GetComponentInParent<Canvas>() : null;
            if (canvas == null) return;
            int order = canvas.sortingOrder + sortingOffset;
            _mr.sortingLayerID = canvas.sortingLayerID;
            _mr.sortingOrder = order;
            // labels draw after the body: each gets its own sorting (a nested canvas)
            foreach (var l in labels)
            {
                if (l == null) continue;
                var lc = l.GetComponent<Canvas>();
                if (lc == null) { if (!Application.isPlaying) continue; lc = l.gameObject.AddComponent<Canvas>(); }
                if (!lc.overrideSorting) lc.overrideSorting = true;
                if (lc.sortingLayerID != canvas.sortingLayerID) lc.sortingLayerID = canvas.sortingLayerID;
                if (lc.sortingOrder != order + 1) lc.sortingOrder = order + 1;
                if (l.raycastTarget && Application.isPlaying) l.raycastTarget = false;
            }
        }

        void StepSim(float dt)
        {
            var sim = LiquidUI.SimMaterial;
            if (_simA == null || sim == null) return;
            if (_energy <= 0.0005f && _pending.Count == 0) return;
            float u = Unit;
            float damp = glass ? 0.997f : 0.9985f - Frozen * 0.1f;
            sim.SetVector(IdSimSize, new Vector4(Width / u, Height / u, CornerRadius / u, 0f));
            sim.SetFloat(IdDamp, damp);
            int steps = Mathf.Clamp(Mathf.RoundToInt(dt * 180f), 1, 6);
            for (int s = 0; s < steps; s++)
            {
                for (int i = 0; i < 4; i++) _imp[i] = new Vector4(0f, 0f, 0.1f, 0f);
                if (s == 0)
                {
                    int n = Mathf.Min(4, _pending.Count);
                    for (int i = 0; i < n; i++) _imp[i] = _pending[i];
                    _pending.RemoveRange(0, n);
                }
                sim.SetVectorArray(IdImp, _imp);
                Graphics.Blit(_simA, _simB, sim);
                var t = _simA; _simA = _simB; _simB = t;
            }
            _mat.SetTexture(IdSim, _simA);
            _energy *= Mathf.Pow(damp, steps);
        }
    }
}
