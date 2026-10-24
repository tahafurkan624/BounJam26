using System;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// A floor plate pressed by weight. Every physics step it adds up the mass of the bodies standing on it, so
    /// heavy things press it and light ones don't: the solid blob (5 kg) and crates (3-4 kg) do, the liquid blob
    /// (1 kg) and the paper ship don't. Changing form while standing on it presses or releases it straight away.
    /// The plate has no collider, so nothing snags on its edge when sliding or pushing onto it. Pressing it plays
    /// event:/Buttons once; letting go is silent.
    /// </summary>
    public class PressureButton : MonoBehaviour
    {
        [Tooltip("The visible plate; it springs down while pressed and back up when released.")]
        [SerializeField] Transform cap;
        [SerializeField] Renderer capRenderer;
        [SerializeField] Material idleMaterial;
        [SerializeField] Material pressedMaterial;
        [Tooltip("Optional ring that lights up while pressed.")]
        [SerializeField] Renderer lightRenderer;
        [SerializeField] Material lightOffMaterial;
        [SerializeField] Material lightOnMaterial;

        [Tooltip("Total mass standing on the button needed to press it, kg. " +
                 "Liquid blob 1, paper ship 0.25, solid blob 5, crates 3-4.")]
        [SerializeField] float requiredMass = 3f;
        [Tooltip("Once pressed, stays pressed for good.")]
        [SerializeField] bool latch;
        [Tooltip("Size of the box above the plate that counts as standing on it, m.")]
        [SerializeField] Vector3 detectSize = new Vector3(1.1f, 0.6f, 1.1f);
        [SerializeField] LayerMask detectMask = ~0;
        [Tooltip("How far the plate sinks when pressed, m.")]
        [SerializeField] float pressDepth = 0.03f;

        [Header("Animation")]
        [Tooltip("How fast the plate springs to its pressed or resting height, Hz.")]
        [SerializeField] float springFrequency = 7f;
        [Tooltip("Below 1 the plate overshoots and wobbles before settling: it sinks past its pressed depth when " +
                 "pressed and pops above its rest height when released. 1 settles without overshoot.")]
        [SerializeField, Range(0.1f, 1f)] float springDamping = 0.35f;
        [Tooltip("How much the plate squashes flat when pressed and stretches tall when released.")]
        [SerializeField] float squashStrength = 0.35f;

        // Spring steps no longer than this, so the stiff spring stays stable at low frame rates.
        const float MaxSpringStep = 1f / 240f;

        readonly Collider[] overlaps = new Collider[16];
        readonly HashSet<Rigidbody> counted = new HashSet<Rigidbody>();
        Vector3 capRestPosition;
        Vector3 capRestScale;
        // Spring states: the plate's height below rest (m) and its squash (0 is its modelled shape).
        float capOffset, capOffsetVelocity;
        float squash, squashVelocity;

        public bool IsPressed { get; private set; }
        /// <summary>Mass found on the button in the last physics step, kg.</summary>
        public float MassOnTop { get; private set; }
        /// <summary>Raised whenever the button is pressed (true) or released (false).</summary>
        public event Action<bool> PressedChanged;

        void Awake()
        {
            if (cap != null)
            {
                capRestPosition = cap.localPosition;
                capRestScale = cap.localScale;
            }
            ApplyLook();
        }

        void Update()
        {
            if (cap == null) return;

            float target = IsPressed ? -pressDepth : 0f;
            float omega = 2f * Mathf.PI * springFrequency;
            for (float left = Mathf.Min(Time.deltaTime, 0.1f); left > 0f; left -= MaxSpringStep)
            {
                float dt = Mathf.Min(left, MaxSpringStep);
                Spring(ref capOffset, ref capOffsetVelocity, target, omega, dt);
                Spring(ref squash, ref squashVelocity, 0f, omega, dt);
            }

            // Squash keeps the volume: flatter is wider, taller is thinner.
            float height = Mathf.Max(0.3f, 1f + squash);
            float width = 1f / Mathf.Sqrt(height);
            cap.localPosition = capRestPosition + Vector3.up * capOffset;
            cap.localScale = Vector3.Scale(capRestScale, new Vector3(width, height, width));
        }

        /// <summary>Steps a damped spring pulling <paramref name="value"/> toward <paramref name="target"/>.</summary>
        void Spring(ref float value, ref float velocity, float target, float omega, float dt)
        {
            velocity += (-(value - target) * omega * omega - 2f * springDamping * omega * velocity) * dt;
            value += velocity * dt;
        }

        void FixedUpdate()
        {
            MassOnTop = MeasureMass();
            bool pressed = (latch && IsPressed) || MassOnTop >= requiredMass;
            if (pressed == IsPressed) return;

            IsPressed = pressed;
            // Kick the squash: flatten on press, stretch on release; the spring brings it back.
            squashVelocity += (pressed ? -1f : 1f) * squashStrength * 2f * Mathf.PI * springFrequency;
            ApplyLook();
            if (pressed) GameSounds.Buttons.PlayAt(transform.position);
            PressedChanged?.Invoke(pressed);
        }

        float MeasureMass()
        {
            Vector3 center = transform.position + Vector3.up * (detectSize.y * 0.5f);
            int count = Physics.OverlapBoxNonAlloc(center, detectSize * 0.5f, overlaps, transform.rotation, detectMask, QueryTriggerInteraction.Ignore);

            // A body can touch the box with several colliders; count each one once.
            counted.Clear();
            float total = 0f;
            for (int i = 0; i < count; i++)
            {
                Rigidbody body = overlaps[i].attachedRigidbody;
                if (body != null && !body.isKinematic && counted.Add(body)) total += body.mass;
            }
            return total;
        }

        void ApplyLook()
        {
            if (capRenderer != null) capRenderer.sharedMaterial = IsPressed ? pressedMaterial : idleMaterial;
            if (lightRenderer != null) lightRenderer.sharedMaterial = IsPressed ? lightOnMaterial : lightOffMaterial;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = IsPressed ? Color.green : new Color(1f, 0.5f, 0f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position + Vector3.up * (detectSize.y * 0.5f), transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, detectSize);
        }
    }
}
