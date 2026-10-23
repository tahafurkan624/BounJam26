using System;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// A floor plate pressed by weight. Every physics step it adds up the mass of the bodies standing on it, so
    /// heavy things press it and light ones don't: the solid blob (5 kg) and crates (3-4 kg) do, the liquid blob
    /// (1 kg) and the paper ship don't. Changing form while standing on it presses or releases it straight away.
    /// The plate has no collider, so nothing snags on its edge when sliding or pushing onto it.
    /// </summary>
    public class PressureButton : MonoBehaviour
    {
        [Tooltip("The visible plate; it sinks while pressed.")]
        [SerializeField] Transform cap;
        [SerializeField] Renderer capRenderer;
        [SerializeField] Material idleMaterial;
        [SerializeField] Material pressedMaterial;

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

        readonly Collider[] overlaps = new Collider[16];
        readonly HashSet<Rigidbody> counted = new HashSet<Rigidbody>();
        Vector3 capRestPosition;

        public bool IsPressed { get; private set; }
        /// <summary>Mass found on the button in the last physics step, kg.</summary>
        public float MassOnTop { get; private set; }
        /// <summary>Raised whenever the button is pressed (true) or released (false).</summary>
        public event Action<bool> PressedChanged;

        void Awake()
        {
            if (cap != null) capRestPosition = cap.localPosition;
            ApplyLook();
        }

        void FixedUpdate()
        {
            MassOnTop = MeasureMass();
            bool pressed = (latch && IsPressed) || MassOnTop >= requiredMass;
            if (pressed == IsPressed) return;

            IsPressed = pressed;
            ApplyLook();
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
            if (cap != null) cap.localPosition = capRestPosition + Vector3.down * (IsPressed ? pressDepth : 0f);
            if (capRenderer != null) capRenderer.sharedMaterial = IsPressed ? pressedMaterial : idleMaterial;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = IsPressed ? Color.green : new Color(1f, 0.5f, 0f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position + Vector3.up * (detectSize.y * 0.5f), transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, detectSize);
        }
    }
}
