// BlobShip - liquid UI particles for uGUI.
// A self-contained particle system drawn as a UI Graphic: works in every Canvas render mode,
// sorts with the rest of the UI, respects Mask / RectMask2D, and (by default) keeps running while the
// game is paused (unscaled time). No packages required.
//
// Put it on a RectTransform inside a Canvas, pick a Preset, done. The emitter area is the RectTransform.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlobShip.UI
{
    public enum LiquidPreset
    {
        AmbientBubbles,   // slow glassy bubbles rising inside a glass panel, popping near the top
        BarBubbles,       // tiny bubbles drifting inside a progress-bar fill
        Drip,             // drops form on the bottom edge, hang, then fall and stretch
        DropSplash,       // one-shot burst of droplets (drop collected, button press)
        Ripple,           // one-shot expanding water rings
        Celebration,      // droplets + glints raining down for "level complete"
        Custom            // keep the values you set in the inspector
    }

    public enum LiquidSprite { Bubble = 0, Droplet = 1, Ring = 2, Sparkle = 3 }
    public enum LiquidShape { Area, Point, BottomEdge, TopEdge, AboveTop }

    [RequireComponent(typeof(CanvasRenderer))]
    [AddComponentMenu("UI/Effects/Liquid Particles (BlobShip)")]
    public class UILiquidParticles : MaskableGraphic
    {
        const string AtlasResource = "BlobShipUI/T_LiquidParticles";

        [Header("Preset")]
        [Tooltip("Picking a preset fills in the values below; tweak them freely afterwards.")]
        public LiquidPreset preset = LiquidPreset.AmbientBubbles;
#pragma warning disable 0414   // only read in the editor (OnValidate)
        [SerializeField, HideInInspector] int _appliedPreset = -1;
#pragma warning restore 0414

        [Header("Emission")]
        public bool playOnEnable = true;
        public bool prewarm = true;
        [Tooltip("Particles per second while playing (0 = bursts only).")]
        public float rate = 4f;
        public int maxParticles = 60;
        public LiquidShape shape = LiquidShape.Area;
        [Tooltip("Seconds of emission after Play (0 = until Stop).")]
        public float duration = 0f;
        [Tooltip("Remove this object once it stops emitting and the last particle is gone (one-shot effects).")]
        public bool destroyWhenDone = false;

        [Header("Look")]
        public Texture atlas;
        public LiquidSprite sprite = LiquidSprite.Bubble;
        public LiquidSprite secondarySprite = LiquidSprite.Sparkle;
        [Range(0, 1)] public float secondaryChance = 0f;
        public Color colorStart = new Color(1, 1, 1, 0.9f);
        public Color colorEnd = new Color(1, 1, 1, 0.6f);
        [Tooltip("Size in canvas pixels (min, max).")]
        public Vector2 size = new Vector2(10, 26);
        [Tooltip("Size multiplier reached at the end of life.")]
        public float sizeEnd = 1.15f;
        [Range(0, 0.5f)] public float fadeIn = 0.15f;
        [Range(0, 0.5f)] public float fadeOut = 0.15f;
        [Tooltip("Bubble 'pop': quick swell + fade over the last part of life.")]
        public bool popAtEnd = true;

        [Header("Motion (canvas pixels)")]
        public Vector2 lifetime = new Vector2(3f, 5.5f);
        public Vector2 speed = new Vector2(18f, 40f);
        [Tooltip("Emission direction in degrees (90 = up).")]
        public float direction = 90f;
        public float spread = 20f;
        [Tooltip("Pixels / s^2. Negative pulls down (drops), positive lifts (buoyancy).")]
        public float gravity = 6f;
        public float drag = 0.2f;
        public float wobbleAmplitude = 6f;
        public float wobbleFrequency = 0.6f;
        [Tooltip("Stretch along velocity (droplets). 0 = off.")]
        public float stretch = 0f;
        [Tooltip("Rotate the sprite to face its velocity (droplets).")]
        public bool alignToVelocity = false;
        public float spin = 0f;
        [Tooltip("Drip: seconds a drop clings to the edge while it grows, before it falls (min, max).")]
        public Vector2 hangTime = Vector2.zero;
        [Tooltip("Kill particles that leave the rect by more than this many pixels (-1 = never).")]
        public float killOutside = -1f;

        [Header("Time")]
        public bool useUnscaledTime = true;

        struct Particle
        {
            public Vector2 pos, vel;
            public float age, life, size, rot, spin, seed, hang;
            public int cell;
        }

        readonly List<Particle> _particles = new List<Particle>(64);
        float _emitAccumulator, _playTime;
        bool _emitting;
        static Texture2D s_atlas;

        public override Texture mainTexture
        {
            get
            {
                if (atlas != null) return atlas;
                if (s_atlas == null) s_atlas = Resources.Load<Texture2D>(AtlasResource);
                return s_atlas != null ? (Texture)s_atlas : s_WhiteTexture;
            }
        }

        public int ParticleCount => _particles.Count;
        public bool IsEmitting => _emitting;

        // ------------------------------------------------------------------ lifecycle
#if UNITY_EDITOR
        protected override void Reset()
        {
            base.Reset();
            raycastTarget = false;
            ApplyPreset(preset);
        }
#endif

        protected override void OnEnable()
        {
            base.OnEnable();
            raycastTarget = false;
            if (Application.isPlaying && playOnEnable) Play();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            if (preset != LiquidPreset.Custom && (int)preset != _appliedPreset) ApplyPreset(preset);
        }
#endif

        // ------------------------------------------------------------------ public API
        public void Play()
        {
            _emitting = true; _playTime = 0f;
            if (prewarm && rate > 0f)
            {
                float step = 1f / 30f, total = Mathf.Max(lifetime.y, 0.1f);
                for (float t = 0; t < total; t += step) Simulate(step);
            }
        }

        public void Stop() { _emitting = false; }

        public void Clear() { _particles.Clear(); SetVerticesDirty(); }

        /// <summary>Emit <paramref name="count"/> particles using the current shape.</summary>
        public void Burst(int count) { for (int i = 0; i < count; i++) Emit(null); }

        /// <summary>Emit <paramref name="count"/> particles from a point in this RectTransform's local space.</summary>
        public void Burst(int count, Vector2 localPosition) { for (int i = 0; i < count; i++) Emit(localPosition); }

        public void ApplyPreset(LiquidPreset p)
        {
            preset = p; _appliedPreset = (int)p; duration = 0f;
            // shared defaults
            secondaryChance = 0f; popAtEnd = false; stretch = 0f; alignToVelocity = false; spin = 0f;
            hangTime = Vector2.zero; killOutside = -1f; sizeEnd = 1f; fadeIn = 0.1f; fadeOut = 0.2f;
            wobbleAmplitude = 0f; wobbleFrequency = 0.5f; drag = 0.2f; direction = 90f; spread = 20f;
            colorStart = Color.white; colorEnd = Color.white;
            switch (p)
            {
                case LiquidPreset.AmbientBubbles:
                    shape = LiquidShape.BottomEdge; sprite = LiquidSprite.Bubble; rate = 1.6f; maxParticles = 24;
                    size = new Vector2(16, 38); lifetime = new Vector2(4f, 7f); speed = new Vector2(12f, 26f);
                    gravity = 3f; drag = 0.05f; wobbleAmplitude = 7f; wobbleFrequency = 0.45f; spread = 12f;
                    popAtEnd = true; sizeEnd = 1.12f; fadeIn = 0.15f; fadeOut = 0.08f; killOutside = 4f;
                    colorStart = new Color(1, 1, 1, 0.95f); colorEnd = new Color(1, 1, 1, 0.75f);
                    playOnEnable = true; prewarm = true; break;

                case LiquidPreset.BarBubbles:
                    shape = LiquidShape.BottomEdge; sprite = LiquidSprite.Bubble; rate = 6f; maxParticles = 30;
                    size = new Vector2(5, 10); lifetime = new Vector2(0.8f, 1.6f); speed = new Vector2(4f, 10f);
                    direction = 60f; spread = 25f; gravity = 6f; wobbleAmplitude = 1.5f; wobbleFrequency = 1.2f;
                    popAtEnd = true; killOutside = 1f; colorStart = new Color(1, 1, 1, 0.9f); colorEnd = new Color(1, 1, 1, 0.7f);
                    playOnEnable = true; prewarm = true; break;

                case LiquidPreset.Drip:
                    shape = LiquidShape.BottomEdge; sprite = LiquidSprite.Droplet; rate = 0.8f; maxParticles = 12;
                    size = new Vector2(15, 24); lifetime = new Vector2(2.6f, 3.4f); speed = new Vector2(0f, 4f);
                    direction = -90f; spread = 4f; gravity = -520f; drag = 0.6f; stretch = 0.0016f; alignToVelocity = true;
                    hangTime = new Vector2(0.7f, 1.4f); fadeIn = 0.2f; fadeOut = 0.12f; killOutside = 260f;
                    colorStart = new Color(1, 1, 1, 0.95f); colorEnd = new Color(1, 1, 1, 0.85f);
                    playOnEnable = true; prewarm = false; break;

                case LiquidPreset.DropSplash:
                    shape = LiquidShape.Point; sprite = LiquidSprite.Droplet; secondarySprite = LiquidSprite.Sparkle;
                    secondaryChance = 0.25f; rate = 0f; maxParticles = 40;
                    size = new Vector2(12, 22); lifetime = new Vector2(0.6f, 0.95f); speed = new Vector2(170f, 340f);
                    direction = 90f; spread = 75f; gravity = -900f; drag = 1.2f; stretch = 0.0022f; alignToVelocity = true;
                    sizeEnd = 0.55f; fadeIn = 0.02f; fadeOut = 0.35f; playOnEnable = false; prewarm = false; break;

                case LiquidPreset.Ripple:
                    shape = LiquidShape.Point; sprite = LiquidSprite.Ring; rate = 0f; maxParticles = 6;
                    size = new Vector2(36, 42); lifetime = new Vector2(0.6f, 0.8f); speed = Vector2.zero; gravity = 0f;
                    sizeEnd = 3.0f; fadeIn = 0.02f; fadeOut = 0.75f; colorStart = new Color(1, 1, 1, 1f);
                    colorEnd = new Color(1, 1, 1, 0.35f); playOnEnable = false; prewarm = false; break;

                case LiquidPreset.Celebration:
                    shape = LiquidShape.AboveTop; sprite = LiquidSprite.Droplet; secondarySprite = LiquidSprite.Sparkle;
                    secondaryChance = 0.35f; rate = 18f; maxParticles = 110;
                    size = new Vector2(14, 28); lifetime = new Vector2(2.2f, 3.2f); speed = new Vector2(30f, 90f);
                    direction = -90f; spread = 18f; gravity = -260f; drag = 0.5f; stretch = 0.0012f; alignToVelocity = true;
                    wobbleAmplitude = 10f; wobbleFrequency = 0.7f; spin = 90f; fadeIn = 0.05f; fadeOut = 0.25f;
                    killOutside = 80f; duration = 3.5f; playOnEnable = true; prewarm = false; break;

                case LiquidPreset.Custom:
                    break;
            }
        }

        // ------------------------------------------------------------------ simulation
        void Update()
        {
            if (!Application.isPlaying) return;
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (dt <= 0f) return;
            Simulate(Mathf.Min(dt, 0.05f));
            SetVerticesDirty();
            if (destroyWhenDone && !_emitting && _particles.Count == 0) Destroy(gameObject);
        }

        void Simulate(float dt)
        {
            if (_emitting && duration > 0f)
            {
                _playTime += dt;
                if (_playTime >= duration) _emitting = false;
            }
            if (_emitting && rate > 0f)
            {
                _emitAccumulator += rate * dt;
                while (_emitAccumulator >= 1f) { _emitAccumulator -= 1f; Emit(null); }
            }

            Rect r = rectTransform.rect;
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                Particle p = _particles[i];
                p.age += dt;
                if (p.age >= p.life) { _particles.RemoveAt(i); continue; }

                if (p.age > p.hang)
                {
                    p.vel.y += gravity * dt;
                    p.vel *= Mathf.Max(0f, 1f - drag * dt);
                    p.pos += p.vel * dt;
                }
                p.rot += p.spin * dt;

                if (killOutside >= 0f)
                {
                    float k = killOutside + p.size;
                    if (p.pos.x < r.xMin - k || p.pos.x > r.xMax + k || p.pos.y < r.yMin - k || p.pos.y > r.yMax + k)
                    { _particles.RemoveAt(i); continue; }
                    // bubbles pop when they reach the top of their container
                    if (popAtEnd && gravity > 0f && p.pos.y > r.yMax - p.size * 0.5f && p.age < p.life - 0.12f)
                        p.age = Mathf.Max(p.age, p.life - 0.12f);
                }
                _particles[i] = p;
            }
        }

        void Emit(Vector2? at)
        {
            if (_particles.Count >= maxParticles) return;
            Rect r = rectTransform.rect;
            Vector2 pos;
            if (at.HasValue) pos = at.Value;
            else switch (shape)
            {
                case LiquidShape.Point: pos = r.center; break;
                // bubbles start just inside the bottom edge; drops hang just below it (tail touching the edge)
                case LiquidShape.BottomEdge: pos = new Vector2(Random.Range(r.xMin + 6f, r.xMax - 6f), gravity < 0 ? r.yMin - size.y * 0.42f : r.yMin + size.y * 0.5f); break;
                case LiquidShape.TopEdge: pos = new Vector2(Random.Range(r.xMin, r.xMax), r.yMax); break;
                case LiquidShape.AboveTop: pos = new Vector2(Random.Range(r.xMin, r.xMax), r.yMax + size.y); break;
                default: pos = new Vector2(Random.Range(r.xMin, r.xMax), Random.Range(r.yMin, r.yMax)); break;
            }
            float ang = (direction + Random.Range(-spread, spread)) * Mathf.Deg2Rad;
            var p = new Particle
            {
                pos = pos,
                vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Random.Range(speed.x, speed.y),
                life = Random.Range(lifetime.x, lifetime.y),
                size = Random.Range(size.x, size.y),
                rot = Random.Range(0f, 360f),
                spin = spin * Random.Range(-1f, 1f),
                seed = Random.Range(0f, 100f),
                hang = hangTime.y > 0f ? Random.Range(hangTime.x, hangTime.y) : 0f,
                cell = (int)(Random.value < secondaryChance ? secondarySprite : sprite)
            };
            p.life += p.hang;
            _particles.Add(p);
        }

        // ------------------------------------------------------------------ mesh
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Color tint = color;
            for (int i = 0; i < _particles.Count; i++)
            {
                Particle p = _particles[i];
                float t = Mathf.Clamp01(p.age / p.life);

                // size: drips swell while hanging, everything else lerps to sizeEnd
                float s = p.size;
                if (p.hang > 0f && p.age < p.hang) s *= Mathf.Lerp(0.25f, 1f, Mathf.SmoothStep(0f, 1f, p.age / p.hang));
                else s *= Mathf.Lerp(1f, sizeEnd, t);
                float a = Mathf.Lerp(colorStart.a, colorEnd.a, t);
                if (fadeIn > 0f) a *= Mathf.Clamp01(t / fadeIn);
                if (popAtEnd)
                {
                    float popT = Mathf.InverseLerp(1f - Mathf.Max(fadeOut, 0.05f), 1f, t);
                    s *= 1f + 0.45f * popT; a *= 1f - popT;
                }
                else if (fadeOut > 0f) a *= Mathf.Clamp01((1f - t) / fadeOut);

                Color c = Color.Lerp(colorStart, colorEnd, t) * tint;
                c.a = a * tint.a;
                if (c.a <= 0.003f) continue;

                // wobble (visual sideways sway)
                Vector2 center = p.pos;
                if (wobbleAmplitude > 0f && (p.hang <= 0f || p.age > p.hang))
                    center.x += Mathf.Sin((p.age * wobbleFrequency + p.seed) * 6.2831853f) * wobbleAmplitude;

                // orientation + stretch: drops point their tail against the motion and stretch with speed,
                // bubbles and rings stay upright (consistent highlight), glints spin
                float hx = s * 0.5f, hy = s * 0.5f, angle = 0f;
                bool falling = p.hang <= 0f || p.age > p.hang;
                if (p.cell == (int)LiquidSprite.Droplet)
                {
                    float sp = p.vel.magnitude;
                    if (falling && alignToVelocity && sp > 1f) angle = Mathf.Atan2(p.vel.y, p.vel.x) + Mathf.PI * 0.5f;
                    if (falling && stretch > 0f) { float st = Mathf.Min(1f + sp * stretch, 2.2f); hy *= st; hx /= Mathf.Sqrt(st); }
                }
                else if (p.cell == (int)LiquidSprite.Sparkle) angle = p.rot * Mathf.Deg2Rad;

                float cs = Mathf.Cos(angle), sn = Mathf.Sin(angle);
                Vector2 ax = new Vector2(cs, sn) * hx, ay = new Vector2(-sn, cs) * hy;
                float u0 = p.cell * 0.25f + 0.002f, u1 = (p.cell + 1) * 0.25f - 0.002f;

                int vi = vh.currentVertCount;
                Color32 c32 = c;
                vh.AddVert(center - ax - ay, c32, new Vector2(u0, 0f));
                vh.AddVert(center - ax + ay, c32, new Vector2(u0, 1f));
                vh.AddVert(center + ax + ay, c32, new Vector2(u1, 1f));
                vh.AddVert(center + ax - ay, c32, new Vector2(u1, 0f));
                vh.AddTriangle(vi, vi + 1, vi + 2);
                vh.AddTriangle(vi + 2, vi + 3, vi);
            }
        }
    }
}
