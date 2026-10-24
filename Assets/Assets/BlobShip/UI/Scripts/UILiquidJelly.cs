// BlobShip - makes a UI Graphic's own shape liquid.
// Subdivides the mesh of an Image / RawImage (sliced rounded panels included) and moves its vertices:
//   * hover : the surface swells towards the pointer
//   * press : a real ripple runs across the surface from the press point (rim included)
//   * idle  : a very slow wave travels around the edge so the glass looks alive
// Driven by UILiquidInteractable (SetPointer / AddRipple) or by your own code.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlobShip.UI
{
    [AddComponentMenu("UI/Effects/Liquid Jelly Mesh (BlobShip)")]
    [RequireComponent(typeof(Graphic))]
    public class UILiquidJelly : BaseMeshEffect
    {
        [Tooltip("Each level splits every triangle into 4. 2 is plenty for buttons, 3 for big panels.")]
        [Range(0, 3)] public int subdivisions = 2;

        [Header("Hover swell (pixels)")]
        public float bulge = 6f;
        [Range(0.1f, 2f)] public float bulgeRadius = 0.6f;

        [Header("Press ripple (pixels)")]
        public float rippleAmplitude = 5f;
        public float rippleSpeed = 420f;
        public float rippleWavelength = 70f;
        public float rippleDamping = 3f;

        [Header("Idle edge wave (pixels)")]
        public float idleWave = 0.7f;
        public float idleSpeed = 1.3f;

        public bool useUnscaledTime = true;

        struct Ripple { public Vector2 center; public float start, strength; }
        readonly List<Ripple> _ripples = new List<Ripple>(8);
        Vector2 _pointer;
        float _weight, _targetWeight;

        static readonly List<UIVertex> s_in = new List<UIVertex>(256);
        static readonly List<UIVertex> s_out = new List<UIVertex>(2048);
        static readonly List<UIVertex> s_tmp = new List<UIVertex>(2048);

        float Now => useUnscaledTime ? Time.unscaledTime : Time.time;

        /// <summary>Pointer position in this Graphic's local space; inside = hovering.</summary>
        public void SetPointer(Vector2 local, bool inside) { _pointer = local; _targetWeight = inside ? 1f : 0f; }

        /// <summary>Start a ripple at a local point (strength 1 = press, 0.4 = soft hover).</summary>
        public void AddRipple(Vector2 local, float strength = 1f)
        {
            if (_ripples.Count >= 6) _ripples.RemoveAt(0);
            _ripples.Add(new Ripple { center = local, start = Now, strength = strength });
        }

        void Update()
        {
            if (graphic == null) return;
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            _weight = Mathf.MoveTowards(_weight, _targetWeight, dt * 4f);
            for (int i = _ripples.Count - 1; i >= 0; i--)
                if (Now - _ripples[i].start > 2.5f) _ripples.RemoveAt(i);
            if (_weight > 0.001f || _ripples.Count > 0 || idleWave > 0f) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || graphic == null) return;
            s_in.Clear();
            vh.GetUIVertexStream(s_in);
            if (s_in.Count == 0) return;
            Subdivide(s_in, s_out, subdivisions);

            Rect r = graphic.rectTransform.rect;
            Vector2 c = r.center, half = r.size * 0.5f;
            float minHalf = Mathf.Max(1f, Mathf.Min(half.x, half.y));
            float R = minHalf * 2f * bulgeRadius;
            float t = Now;

            for (int i = 0; i < s_out.Count; i++)
            {
                UIVertex v = s_out[i];
                Vector2 p = v.position;
                Vector2 fromC = p - c;
                Vector2 dir = fromC.sqrMagnitude > 1e-4f ? fromC.normalized : Vector2.zero;
                // 0 at the centre, 1 at the rim: the rim moves most, the middle stays readable
                float edge = Mathf.Clamp01(Mathf.Max(Mathf.Abs(fromC.x) / Mathf.Max(half.x, 1f), Mathf.Abs(fromC.y) / Mathf.Max(half.y, 1f)));
                Vector2 offs = Vector2.zero;

                if (_weight > 0.001f && bulge != 0f)
                {
                    Vector2 d = p - _pointer;
                    float f = Mathf.Exp(-d.sqrMagnitude / (R * R));
                    offs += dir * (bulge * _weight * f * edge);
                }

                for (int k = 0; k < _ripples.Count; k++)
                {
                    Ripple rp = _ripples[k];
                    Vector2 rd = p - rp.center;
                    float dist = rd.magnitude, age = t - rp.start;
                    float x = (dist - age * rippleSpeed) / Mathf.Max(rippleWavelength, 1f);
                    if (Mathf.Abs(x) < 1.5f && dist > 0.001f)
                    {
                        float env = Mathf.Exp(-rippleDamping * age) * (1f - Mathf.Abs(x) / 1.5f);
                        float w = Mathf.Sin(x * 6.2831853f) * rippleAmplitude * rp.strength * env;
                        offs += (rd / dist) * (w * Mathf.Max(edge, 0.35f));
                    }
                }

                if (idleWave > 0f)
                {
                    float ang = Mathf.Atan2(fromC.y, fromC.x);
                    offs += dir * (Mathf.Sin(ang * 3f + t * idleSpeed) * idleWave * edge);
                }

                v.position = new Vector3(p.x + offs.x, p.y + offs.y, v.position.z);
                s_out[i] = v;
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(s_out);
        }

        static void Subdivide(List<UIVertex> src, List<UIVertex> dst, int levels)
        {
            dst.Clear(); dst.AddRange(src);
            for (int l = 0; l < levels; l++)
            {
                s_tmp.Clear();
                for (int i = 0; i + 2 < dst.Count; i += 3)
                {
                    UIVertex a = dst[i], b = dst[i + 1], c = dst[i + 2];
                    UIVertex ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    s_tmp.Add(a); s_tmp.Add(ab); s_tmp.Add(ca);
                    s_tmp.Add(ab); s_tmp.Add(b); s_tmp.Add(bc);
                    s_tmp.Add(ca); s_tmp.Add(bc); s_tmp.Add(c);
                    s_tmp.Add(ab); s_tmp.Add(bc); s_tmp.Add(ca);
                }
                dst.Clear(); dst.AddRange(s_tmp);
            }
        }

        static UIVertex Mid(UIVertex a, UIVertex b)
        {
            UIVertex m = a;
            m.position = (a.position + b.position) * 0.5f;
            m.color = Color32.Lerp(a.color, b.color, 0.5f);
            m.uv0 = (a.uv0 + b.uv0) * 0.5f;
            m.uv1 = (a.uv1 + b.uv1) * 0.5f;
            m.uv2 = (a.uv2 + b.uv2) * 0.5f;
            m.uv3 = (a.uv3 + b.uv3) * 0.5f;
            m.normal = (a.normal + b.normal) * 0.5f;
            m.tangent = (a.tangent + b.tangent) * 0.5f;
            return m;
        }
    }
}
