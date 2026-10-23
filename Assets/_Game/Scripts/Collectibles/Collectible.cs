using System;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Something the blob can take with it. It has no collider, so the liquid flows right over it; the way to pick it
    /// up is to cover it completely with jelly and freeze. Whatever the crystal sets around gets stuck inside and is
    /// carried from then on, in both forms (see <see cref="BlobCargo"/>). While it waits it sits on a glowing floor
    /// marker, and it glows itself while it is fully inside the jelly: the cue to freeze now. Once carried it glows
    /// softly, so it shows through the jelly and the crystal.
    /// This object is the item's centre; the model and marker are children.
    /// </summary>
    public class Collectible : MonoBehaviour
    {
        [Tooltip("Radius of the item, m. All of it has to be inside the jelly to be caught.")]
        [SerializeField] float radius = 0.15f;
        [Tooltip("Gets the glow while the item is inside the jelly.")]
        [SerializeField] Renderer modelRenderer;
        [Tooltip("Floor marker shown until the item is collected.")]
        [SerializeField] Transform marker;

        [Header("Look")]
        [Tooltip("Glow while inside the jelly. The model's material needs emission enabled.")]
        [ColorUsage(false, true)]
        [SerializeField] Color engulfedGlow = new Color(4f, 2f, 0.5f);
        [Tooltip("Glow while carried, so it shows through the jelly and the crystal.")]
        [ColorUsage(false, true)]
        [SerializeField] Color carriedGlow = new Color(0.6f, 1.2f, 2.6f);
        [Tooltip("How quickly the glow fades in and out, 1/s.")]
        [SerializeField] float glowRate = 8f;
        [Tooltip("Marker pulses per second.")]
        [SerializeField] float markerPulseRate = 0.7f;

        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly List<Collectible> waiting = new List<Collectible>();

        MaterialPropertyBlock block;
        Vector3 markerScale;
        Color glow = Color.black;

        /// <summary>Every collectible in the loaded scenes that hasn't been collected yet.</summary>
        public static IReadOnlyList<Collectible> Waiting => waiting;

        public float Radius => radius;
        public bool IsCollected { get; private set; }
        /// <summary>True while it is fully inside the liquid blob, i.e. freezing now would catch it.</summary>
        public bool IsEngulfed { get; private set; }
        public event Action<Collectible> Collected;

        // Statics survive entering play mode with domain reload off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => waiting.Clear();

        void Awake()
        {
            block = new MaterialPropertyBlock();
            if (marker != null) markerScale = marker.localScale;
            ApplyGlow();
        }

        void OnEnable()
        {
            if (!IsCollected) waiting.Add(this);
        }

        void OnDisable() => waiting.Remove(this);

        public void SetEngulfed(bool value) => IsEngulfed = value && !IsCollected;

        /// <summary>Called by the blob that froze around it. It stops waiting; the blob moves it from now on.</summary>
        public void Collect()
        {
            if (IsCollected) return;
            IsCollected = true;
            IsEngulfed = false;
            waiting.Remove(this);
            if (marker != null) marker.gameObject.SetActive(false);
            Collected?.Invoke(this);
        }

        void Update()
        {
            float flicker = 0.8f + 0.2f * Mathf.Sin(Time.time * 12f);
            Color target = IsCollected ? carriedGlow : IsEngulfed ? engulfedGlow * flicker : Color.black;
            glow = Color.Lerp(glow, target, 1f - Mathf.Exp(-glowRate * Time.deltaTime));
            ApplyGlow();
            if (marker != null && !IsCollected)
                marker.localScale = markerScale * (1f + 0.08f * Mathf.Sin(Time.time * markerPulseRate * 2f * Mathf.PI));
        }

        void ApplyGlow()
        {
            if (modelRenderer == null) return;
            modelRenderer.GetPropertyBlock(block);
            block.SetColor(EmissionColor, glow);
            modelRenderer.SetPropertyBlock(block);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = IsCollected ? Color.gray : new Color(1f, 0.6f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
