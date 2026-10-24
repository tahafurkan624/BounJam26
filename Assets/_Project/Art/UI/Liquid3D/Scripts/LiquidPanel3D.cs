// BlobShip - the glass panel behind the 3D liquid buttons (pause menu, level complete...).
// Open: the glass pours in from the top (drippy front, waves), then the buttons pop in one after another.
// Close: the buttons fade, the glass drains downward and lets its last drops fall.
// The buttons float a little in front of the glass and throw their colour onto it.
//   panel.Show();   panel.Hide();   panel.Hide(() => SceneManager.LoadScene(0));
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlobShip.UI.Liquid3D
{
    [RequireComponent(typeof(LiquidSlab))]
    [AddComponentMenu("UI/BlobShip/Liquid Panel 3D")]
    public class LiquidPanel3D : MonoBehaviour
    {
        [Tooltip("Liquid buttons on this panel (default: child LiquidSlabs).")]
        public List<LiquidSlab> buttons = new List<LiquidSlab>();
        [Tooltip("Titles / other text that fade in with the glass (default: the panel's own labels).")]
        public List<Graphic> fadeWith = new List<Graphic>();
        [Tooltip("Gap between the glass and the buttons, in button heights.")]
        public float buttonGap = 0.22f;
        [Tooltip("Coloured light from the buttons on the glass.")]
        [Range(0f, 0.3f)] public float spill = 0.1f;
        public bool showOnEnable = true;
        [Tooltip("Deactivate the GameObject when closed.")]
        public bool deactivateOnHide = true;

        [Header("Timing (unscaled)")]
        public float pourDuration = 0.75f;
        public float buttonsDelay = 0.45f;
        public float buttonsStagger = 0.07f;
        public float drainDuration = 0.85f;
        public bool dropsOnClose = true;

        LiquidSlab _slab;
        Coroutine _running;
        bool _visible;
        readonly List<Drop> _drops = new List<Drop>();
        static Mesh s_sphere;

        class Drop { public GameObject go; public Material mat; public float vy, life, r; }

        public bool IsVisible => _visible;

        void Awake()
        {
            _slab = GetComponent<LiquidSlab>();
            _slab.glass = true;
            _slab.panelMask = true;
            if (buttons.Count == 0)
                foreach (var s in GetComponentsInChildren<LiquidSlab>(true)) if (s != _slab) buttons.Add(s);
            if (fadeWith.Count == 0) { _slab.FindLabels(); fadeWith.AddRange(_slab.labels); }
        }

        void OnEnable()
        {
            if (showOnEnable && Application.isPlaying) Show();
            else _visible = true;
        }

        void OnDisable()
        {
            foreach (var d in _drops) if (d.go != null) Destroy(d.go);
            _drops.Clear();
        }

        float ButtonUnit
        {
            get { foreach (var b in buttons) if (b != null && b.Height > 1f) return b.Height; return Mathf.Max(1f, _slab.Height / 6f); }
        }

        void LateUpdate()
        {
            float u = ButtonUnit;
            _slab.unitOverride = u;
            // float the buttons in front of the glass and paint their colour onto it
            for (int i = 0; i < 3; i++) { _slab.spillRects[i] = Vector4.zero; _slab.spillColors[i] = Color.clear; }
            int k = 0;
            foreach (var b in buttons)
            {
                if (b == null) continue;
                var lp = b.Rect.localPosition;
                if (b.transform.parent == transform)
                    b.Rect.localPosition = new Vector3(lp.x, lp.y, -(_slab.HalfThickness + buttonGap * u + b.HalfThickness));
                if (k < 3 && b.isActiveAndEnabled)
                {
                    Vector3 c = transform.InverseTransformPoint(b.Rect.TransformPoint(b.Rect.rect.center));
                    var rc = _slab.Rect.rect.center;
                    _slab.spillRects[k] = new Vector4(c.x - rc.x, c.y - rc.y - 0.08f * u, b.Width * 0.5f, b.Height * 0.5f);
                    Color col = b.colors.core; col.a = spill * b.fade;
                    _slab.spillColors[k] = col;
                    k++;
                }
            }
            StepDrops(Mathf.Min(Time.unscaledDeltaTime, 0.05f));
        }

        public void Show()
        {
            if (!gameObject.activeSelf) { bool k = showOnEnable; showOnEnable = false; gameObject.SetActive(true); showOnEnable = k; }
            if (_running != null) StopCoroutine(_running);
            _running = StartCoroutine(OpenRoutine());
        }

        public void Hide() => Hide(null);
        public void HideMenu() => Hide(null);

        public void Hide(Action onHidden)
        {
            if (!gameObject.activeInHierarchy) { onHidden?.Invoke(); return; }
            if (_running != null) StopCoroutine(_running);
            _running = StartCoroutine(CloseRoutine(onHidden));
        }

        static float EaseOut(float k) { k = Mathf.Clamp01(k); return 1f - (1f - k) * (1f - k) * (1f - k); }
        static float EaseInOut(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }

        void SetButtons(float alpha, bool interactable)
        {
            foreach (var b in buttons)
            {
                if (b == null) continue;
                b.fade = alpha; b.interactive = interactable;
                foreach (var l in b.labels) if (l != null) l.canvasRenderer.SetAlpha(alpha);
                var sel = b.GetComponent<Selectable>(); if (sel != null) sel.interactable = interactable;
            }
        }

        void SetTitles(float a) { foreach (var g in fadeWith) if (g != null) g.canvasRenderer.SetAlpha(a); }

        IEnumerator OpenRoutine()
        {
            _visible = true;
            _slab.seed = UnityEngine.Random.Range(0f, 10f);
            _slab.fill = 0f; _slab.drain = 0f; _slab.fade = 1f;
            SetButtons(0f, false); SetTitles(0f);
            var scale = new List<Vector3>(); foreach (var b in buttons) scale.Add(b != null ? b.Rect.localScale : Vector3.one);
            var pop = new float[buttons.Count]; var popV = new float[buttons.Count]; var popped = new bool[buttons.Count];
            float pourT = 0f;
            float total = Mathf.Max(pourDuration + 0.4f, buttonsDelay + buttonsStagger * buttons.Count + 0.5f);
            for (float e = 0f; e < total; )
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                e += dt;
                float f = EaseOut(e / pourDuration);
                _slab.fill = f;
                // the pour makes waves along the falling front
                pourT -= dt;
                if (pourT <= 0f && f < 0.98f)
                {
                    pourT = 0.05f;
                    float spread = 0.3f + 0.7f * EaseInOut(f / 0.6f);
                    _slab.Impulse(new Vector2(0.5f + (UnityEngine.Random.value - 0.5f) * spread * 0.9f, 1f - Mathf.Min(1f, f * 1.15f)), 0.22f, 0.1f);
                }
                SetTitles(Mathf.Clamp01((e - 0.4f) / 0.2f));
                for (int i = 0; i < buttons.Count; i++)
                {
                    var b = buttons[i]; if (b == null) continue;
                    float st = buttonsDelay + buttonsStagger * i;
                    if (e < st) continue;
                    if (!popped[i]) { popped[i] = true; pop[i] = 0.6f; popV[i] = 0f; b.Impulse(new Vector2(0.5f, 0.5f), 0.2f, 0.6f); }
                    float h = dt / 4f;
                    for (int s = 0; s < 4; s++) { popV[i] += ((1f - pop[i]) * 260f - popV[i] * 12f) * h; pop[i] += popV[i] * h; }
                    float a = Mathf.Clamp01((e - st) / 0.14f);
                    b.fade = a; foreach (var l in b.labels) if (l != null) l.canvasRenderer.SetAlpha(a);
                    b.Rect.localScale = scale[i] * pop[i];
                }
                yield return null;
            }
            _slab.fill = 1f;
            for (int i = 0; i < buttons.Count; i++) if (buttons[i] != null) buttons[i].Rect.localScale = scale[i];
            SetButtons(1f, true); SetTitles(1f);
            _running = null;
        }

        IEnumerator CloseRoutine(Action onHidden)
        {
            _visible = false;
            _slab.seed = UnityEngine.Random.Range(0f, 10f);
            foreach (var b in buttons) if (b != null) { b.interactive = false; var sel = b.GetComponent<Selectable>(); if (sel != null) sel.interactable = false; }
            var scale = new List<Vector3>(); foreach (var b in buttons) scale.Add(b != null ? b.Rect.localScale : Vector3.one);
            bool dropped = false;
            int n = buttons.Count;
            for (float e = 0f; ; )
            {
                e += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                for (int i = 0; i < n; i++)
                {
                    var b = buttons[i]; if (b == null) continue;
                    float k = Mathf.Clamp01((e - 0.03f * (n - 1 - i)) / 0.16f);
                    b.fade = 1f - k; foreach (var l in b.labels) if (l != null) l.canvasRenderer.SetAlpha(1f - k);
                    b.Rect.localScale = scale[i] * (1f - 0.18f * k);
                }
                SetTitles(Mathf.Max(0f, 1f - e / 0.16f));
                float raw = Mathf.Clamp01((e - 0.1f) / drainDuration);
                float d = 0.5f - 0.5f * Mathf.Cos(raw * Mathf.PI);           // sine ease: soft start, soft end
                _slab.drain = d;
                _slab.fade = 1f - EaseInOut((raw - 0.55f) / 0.45f);           // the last bit thins out instead of popping
                if (dropsOnClose && !dropped && raw >= 0.62f) { dropped = true; ReleaseDrops(d); }
                if (raw >= 1f) break;
                yield return null;
            }
            for (int i = 0; i < n; i++) if (buttons[i] != null) buttons[i].Rect.localScale = scale[i];
            _slab.drain = 0f; _slab.fade = 1f;
            _running = null;
            if (deactivateOnHide)
            {
                // drops keep falling on the parent while the panel is off
                foreach (var dr in _drops) if (dr.go != null) dr.go.transform.SetParent(transform.parent, true);
                var host = transform.parent != null ? transform.parent.GetComponentInParent<MonoBehaviour>() : null;
                if (_drops.Count > 0 && host != null) host.StartCoroutine(FinishDrops(new List<Drop>(_drops)));
                _drops.Clear();
                bool k = showOnEnable; showOnEnable = false; gameObject.SetActive(false); showOnEnable = k;
            }
            onHidden?.Invoke();
        }

        float Fingers(float u, float seed)
        {
            float a = Mathf.Sin(u * 17f + seed * 3.1f) * 0.5f + 0.5f, b = Mathf.Sin(u * 29f + seed * 7.7f) * 0.5f + 0.5f, c = Mathf.Sin(u * 7f + seed * 1.3f) * 0.5f + 0.5f;
            return Mathf.Pow(a, 6f) * 0.7f + Mathf.Pow(b, 10f) * 0.5f + c * 0.25f;
        }

        void ReleaseDrops(float drain)
        {
            if (_slab.BodyMaterial == null) return;
            if (s_sphere == null) s_sphere = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
            float u = ButtonUnit, spread = Mathf.Lerp(1f, 0.32f, EaseInOut((drain - 0.45f) / 0.55f));
            var rect = _slab.Rect.rect;
            int made = 0;
            for (int i = 1; i < 47 && made < 6; i++)
            {
                float x = i / 48f, f = Fingers(x, _slab.seed);
                if (f < 0.55f || f < Fingers(x - 1f / 48f, _slab.seed) || f < Fingers(x + 1f / 48f, _slab.seed)) continue;
                float r = (0.07f + 0.07f * f) * u;
                var go = new GameObject("LiquidDrop") { layer = gameObject.layer, hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(rect.center.x + (x - 0.5f) * rect.width * spread, rect.yMin - 0.05f * u, -_slab.HalfThickness);
                go.AddComponent<MeshFilter>().sharedMesh = s_sphere;
                var mr = go.AddComponent<MeshRenderer>();
                var m = new Material(_slab.BodyMaterial) { hideFlags = HideFlags.DontSave };
                m.SetTexture("_SimTex", Texture2D.blackTexture);
                m.SetFloat("_GlassAll", 1f); m.SetFloat("_PanelMask", 0f); m.SetFloat("_Fade", 0f);
                m.SetVector("_Size", new Vector4(2f * r, 2f * r, r, u));
                m.SetVector("_SimParams", new Vector4(0f, 0f, 0.01f, 0.01f));
                m.SetColor("_Milk", new Color(0.08f, 0.16f, 0.2f, 0f));
                m.SetFloat("_RefrK", 0.03f); m.SetFloat("_Iri", 0.6f);
                mr.sharedMaterial = m;
                mr.sortingLayerID = _slab.BodyRenderer.sortingLayerID; mr.sortingOrder = _slab.BodyRenderer.sortingOrder + 1;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _drops.Add(new Drop { go = go, mat = m, vy = -0.6f * u, r = r });
                made++;
            }
        }

        bool StepOne(Drop d, float dt, float u)
        {
            if (d.go == null) return false;
            d.life += dt; d.vy -= 13f * u * dt;
            var t = d.go.transform;
            t.localPosition += new Vector3(0f, d.vy * dt, 0f);
            float st = Mathf.Min(1.8f, 1f + Mathf.Abs(d.vy / u) * 0.06f);
            float sz = d.r * 2f;
            t.localScale = new Vector3(sz / Mathf.Sqrt(st), sz * st, sz / Mathf.Sqrt(st));
            d.mat.SetFloat("_Fade", Mathf.Min(1f, d.life / 0.12f) * (1f - EaseInOut((d.life - 0.35f) / 0.4f)));
            if (d.life > 0.8f) { Destroy(d.go); Destroy(d.mat); return false; }
            return true;
        }

        void StepDrops(float dt)
        {
            float u = ButtonUnit;
            for (int i = _drops.Count - 1; i >= 0; i--) if (!StepOne(_drops[i], dt, u)) _drops.RemoveAt(i);
        }

        IEnumerator FinishDrops(List<Drop> list)
        {
            float u = ButtonUnit;
            while (list.Count > 0)
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                for (int i = list.Count - 1; i >= 0; i--) if (!StepOne(list[i], dt, u)) list.RemoveAt(i);
                yield return null;
            }
        }
    }
}
