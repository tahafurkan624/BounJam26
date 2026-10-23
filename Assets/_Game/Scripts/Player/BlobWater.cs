using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// What <see cref="Water"/> does to the blob. The liquid can't touch it: once the bottom of its core dips under
    /// the surface the jelly mixes into the water, sinking and shrinking away, and the level is lost. The solid floats:
    /// buoyancy at points spread through the frozen shape holds it up (deeper points push harder, so a flat crystal
    /// floats flat and rocks back level), the water slows its spin, and the motor swims it while it's in. Melting
    /// while afloat dissolves it like falling in as liquid.
    /// </summary>
    [RequireComponent(typeof(BlobMotor), typeof(BlobForm))]
    public class BlobWater : MonoBehaviour
    {
        [Tooltip("Skin renderer; shrinks away while the liquid dissolves.")]
        [SerializeField] Renderer skinRenderer;

        [Header("Liquid")]
        [Tooltip("The liquid is lost once the bottom of its core is this far under the surface, m.")]
        [SerializeField] float dissolveDepth = 0.02f;
        [SerializeField] string dissolveMessage = "You dissolved in the water!";
        [Tooltip("How long the jelly takes to disappear into the water, s.")]
        [SerializeField] float dissolveTime = 0.8f;
        [Tooltip("Water drag on the sinking jelly, 1/s. Sinks at about 9.81 / this m/s.")]
        [SerializeField] float sinkDrag = 6f;

        [Header("Solid")]
        [Tooltip("Upward push when fully under water, in multiples of gravity. 2 floats half under; higher floats higher.")]
        [SerializeField] float buoyancy = 2.4f;
        [Tooltip("Damps bobbing and rocking, 1/s.")]
        [SerializeField] float verticalDamping = 5f;
        [Tooltip("Slows spinning and tumbling in water, 1/s.")]
        [SerializeField] float angularDrag = 4f;
        [Tooltip("Swims once at least this share of the crystal is under water.")]
        [Range(0f, 1f)]
        [SerializeField] float swimSubmersion = 0.1f;

        // The buoyancy points sit this far out from the centre toward the hull: together they fill its volume.
        const float SampleSpread = 0.7f;

        BlobMotor motor;
        BlobForm form;
        Rigidbody body;
        LevelManager level;
        // Buoyancy points in the blob's local space, from the frozen shape.
        Vector3[] samples;
        // Each point stands for a slice of the crystal this thick either side of it; fully wet once that's under.
        float sampleRadius;
        float dissolveStart;

        /// <summary>Share of the solid under water, 0..1 (0 while liquid).</summary>
        public float Submersion { get; private set; }
        /// <summary>True once the liquid has touched water.</summary>
        public bool IsDissolved { get; private set; }

        void Awake()
        {
            motor = GetComponent<BlobMotor>();
            form = GetComponent<BlobForm>();
            body = GetComponent<Rigidbody>();
            level = FindAnyObjectByType<LevelManager>();
        }

        void OnEnable() => form.FormChanged += OnFormChanged;

        void OnDisable() => form.FormChanged -= OnFormChanged;

        void OnFormChanged(bool solid)
        {
            Submersion = 0f;
            if (!solid) return;

            Vector3[] hull = form.HullPoints;
            if (samples == null || samples.Length != hull.Length + 1) samples = new Vector3[hull.Length + 1];
            Vector3 center = Vector3.zero;
            foreach (Vector3 point in hull) center += point;
            center /= hull.Length;

            var bounds = new Bounds(center, Vector3.zero);
            for (int i = 0; i < hull.Length; i++)
            {
                samples[i] = center + (hull[i] - center) * SampleSpread;
                bounds.Encapsulate(hull[i]);
            }
            samples[hull.Length] = center;
            float size = (bounds.size.x + bounds.size.y + bounds.size.z) / 3f;
            sampleRadius = Mathf.Max(0.05f, size * 0.5f * (1f - SampleSpread));
        }

        void FixedUpdate()
        {
            if (IsDissolved) body.AddForce(-body.linearVelocity * sinkDrag, ForceMode.Acceleration);
            else if (form.IsSolid) { if (samples != null) Float(); }
            else if (Water.TryGetDepth(body.position + Vector3.down * motor.CoreRadius, out float depth) && depth > dissolveDepth)
                Dissolve();
        }

        void Float()
        {
            float share = 1f / samples.Length;
            float lift = -Physics.gravity.y * buoyancy;
            float wetTotal = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                // Where the physics has the body now; the transform is interpolated.
                Vector3 point = body.position + body.rotation * samples[i];
                if (!Water.TryGetDepth(point, out float depth)) continue;
                float wet = Mathf.Clamp01(0.5f + depth / (2f * sampleRadius));
                if (wet <= 0f) continue;

                wetTotal += wet;
                float push = (lift - verticalDamping * body.GetPointVelocity(point).y) * wet * share;
                body.AddForceAtPosition(Vector3.up * (push * body.mass), point, ForceMode.Force);
            }

            Submersion = wetTotal * share;
            if (Submersion > 0f) body.AddTorque(-body.angularVelocity * (angularDrag * Submersion), ForceMode.Acceleration);
            motor.SetSwimming(Submersion >= swimSubmersion);
        }

        void Dissolve()
        {
            IsDissolved = true;
            dissolveStart = Time.time;
            // No more steering, jumping or freezing out of it.
            motor.enabled = false;
            form.enabled = false;
            if (level != null) level.Fail(dissolveMessage);
        }

        // After every Update and coroutine, so a melt's look transition can't put the size back.
        void LateUpdate()
        {
            if (!IsDissolved || skinRenderer == null) return;
            float k = Mathf.Clamp01((Time.time - dissolveStart) / dissolveTime);
            skinRenderer.transform.localScale = Vector3.one * (1f - k * k);
            if (k >= 1f) skinRenderer.enabled = false;
        }
    }
}
