// BlobShip - makes the ice pit feel freezing: cold mist rolls up out of the pit, snowflakes drift round the
// block, and little ice sparkles twinkle on it. Everything is built in code (particle systems + tiny generated
// textures), so there is nothing else to set up: drag this onto SM_IcePit (pivot at the top centre of the block).
using UnityEngine;

namespace BlobShip.Props
{
    [AddComponentMenu("BlobShip/Ice Pit Cold FX")]
    public class IcePitColdFX : MonoBehaviour
    {
        [Tooltip("Half size of the pit opening (m). Matches SM_IcePit.")]
        public float pitHalf = 0.78f;
        [Range(0f, 2f)] public float mistAmount = 1f;
        [Range(0f, 2f)] public float snowAmount = 1f;
        [Range(0f, 2f)] public float sparkleAmount = 1f;
        public Color mistColor = new Color(0.86f, 0.97f, 1f, 0.55f);

        static Texture2D s_soft, s_flake, s_star;
        static Material s_mat;

        void Start()
        {
            if (s_mat == null)
            {
                var sh = Shader.Find("Sprites/Default");
                s_mat = new Material(sh) { name = "IcePitFX" };
            }
            if (s_soft == null) s_soft = SoftDot(64);
            if (s_flake == null) s_flake = Snowflake(64);
            if (s_star == null) s_star = Star(64);
            if (mistAmount > 0f) Mist();
            if (snowAmount > 0f) Snow();
            if (sparkleAmount > 0f) Sparkles();
        }

        ParticleSystem NewSystem(string name, Texture2D tex, out ParticleSystemRenderer r)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            r = go.GetComponent<ParticleSystemRenderer>();
            var m = new Material(s_mat) { mainTexture = tex };
            r.sharedMaterial = m;
            var main = ps.main;
            main.loop = true; main.playOnAwake = true; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            return ps;
        }

        static Gradient Fade(Color c, float inT, float outT, float peak)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, inT), new GradientAlphaKey(peak, outT), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        void Mist()
        {
            var ps = NewSystem("ColdMist", s_soft, out var r);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.6f, 3.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.95f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            main.gravityModifier = -0.015f;                         // cold air lifts slowly...
            main.maxParticles = 80;
            var em = ps.emission; em.rateOverTime = 9f * mistAmount;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(pitHalf * 1.6f, 0.05f, pitHalf * 1.6f); sh.position = new Vector3(0f, -0.25f, 0f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(mistColor, 0.25f, 0.6f, mistColor.a);
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
            var vel = ps.velocityOverLifetime; vel.enabled = true;          // ...and spills over the rim
            vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f); vel.y = new ParticleSystem.MinMaxCurve(0.05f, 0.12f); vel.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.15f; noise.frequency = 0.6f; noise.scrollSpeed = 0.2f;
            r.sortingFudge = 2f;
            ps.Play();
        }

        void Snow()
        {
            var ps = NewSystem("Snowflakes", s_flake, out var r);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 5f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.95f, 0.99f, 1f, 1f);
            main.gravityModifier = 0.02f;
            main.maxParticles = 120;
            var em = ps.emission; em.rateOverTime = 12f * snowAmount;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(3.2f, 0.1f, 3.2f); sh.position = new Vector3(0f, 2.2f, 0f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, 0.12f, 0.8f, 1f);
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 0.45f; noise.scrollSpeed = 0.3f;
            ps.Play();
        }

        void Sparkles()
        {
            var ps = NewSystem("IceSparkles", s_star, out var r);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.startColor = new Color(1f, 1f, 1f, 1f);
            main.maxParticles = 30;
            var em = ps.emission; em.rateOverTime = 5f * sparkleAmount;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(2.05f, 1.2f, 2.05f); sh.position = new Vector3(0f, -0.5f, 0f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, 0.3f, 0.5f, 1f);
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.4f, 1f), new Keyframe(1f, 0f)));
            ps.Play();
        }

        // ---------------------------------------------------------------- little generated textures
        static Texture2D NewTex(int n) => new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };

        static Texture2D SoftDot(int n)
        {
            var t = NewTex(n);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d); a = a * a * (3f - 2f * a);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply(); return t;
        }

        static Texture2D Snowflake(int n)
        {
            var t = NewTex(n);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx);
                    float arm = Mathf.Abs(Mathf.Sin(a * 3f));                              // six arms
                    float line = Mathf.Clamp01(1f - arm * r * 9f) * Mathf.Clamp01((0.95f - r) * 8f);
                    float branch = Mathf.Clamp01(1f - Mathf.Abs(r - 0.55f) * 14f) * Mathf.Clamp01(1f - arm * 3.2f);
                    float core = Mathf.Clamp01((0.18f - r) * 12f);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(line + branch * 0.9f + core)));
                }
            t.Apply(); return t;
        }

        static Texture2D Star(int n)
        {
            var t = NewTex(n);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float rays = Mathf.Clamp01(1f - Mathf.Abs(dx) * 14f) * Mathf.Clamp01(1f - Mathf.Abs(dy) * 1.1f)
                               + Mathf.Clamp01(1f - Mathf.Abs(dy) * 14f) * Mathf.Clamp01(1f - Mathf.Abs(dx) * 1.1f);
                    float glow = Mathf.Clamp01(1f - r * 2.2f);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(rays + glow * glow)));
                }
            t.Apply(); return t;
        }
    }
}
