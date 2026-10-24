using System;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Something the blob can take with it. It has no collider, so the liquid flows right over it; the way to pick it
    /// up is to cover it completely with jelly and freeze. Whatever the crystal sets around gets stuck inside and goes
    /// where the crystal goes (see <see cref="BlobCargo"/>). Melting drops it: it falls from there onto whatever is
    /// below and waits to be caught again, and one lost below the level goes back to where it started. While it waits
    /// it sits on a glowing floor marker, and it glows itself while it is fully inside the jelly: the cue to freeze
    /// now. Held in the crystal it glows softly, so it shows through.
    /// This object is the item's centre; the model and marker are children.
    /// </summary>
    public class Collectible : MonoBehaviour
    {
        [Tooltip("Radius of the item, m. All of it has to be inside the jelly to be caught.")]
        [SerializeField] float radius = 0.15f;
        [Tooltip("Gets the glow while the item is inside the jelly.")]
        [SerializeField] Renderer modelRenderer;
        [Tooltip("Floor marker shown while the item rests, waiting to be caught.")]
        [SerializeField] Transform marker;

        [Header("Dropping")]
        [Tooltip("What a dropped item lands and rests on.")]
        [SerializeField] LayerMask groundMask;
        [Tooltip("A dropped item that falls below this height goes back to where it started, m.")]
        [SerializeField] float lostHeight = -5f;

        [Header("Look")]
        [Tooltip("Glow while inside the jelly. The model's material needs emission enabled.")]
        [ColorUsage(false, true)]
        [SerializeField] Color engulfedGlow = new Color(4f, 2f, 0.5f);
        [Tooltip("Glow while held in the crystal, so it shows through.")]
        [ColorUsage(false, true)]
        [SerializeField] Color carriedGlow = new Color(0.6f, 1.2f, 2.6f);
        [Tooltip("How quickly the glow fades in and out, 1/s.")]
        [SerializeField] float glowRate = 8f;
        [Tooltip("Marker pulses per second.")]
        [SerializeField] float markerPulseRate = 0.7f;

        // A dropped item looks this far above itself for what it rests on, every physics step, so it rides up on a
        // rising lift instead of sinking into it.
        const float SupportReach = 0.3f;

        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly List<Collectible> waiting = new List<Collectible>();

        MaterialPropertyBlock block;
        Vector3 markerScale;
        Color glow = Color.black;
        float engulfedReportTime = float.NegativeInfinity;
        Vector3 home;
        // Dropped at least once: from then on it falls and rests on whatever is below it.
        bool loose;
        bool resting;
        float fallSpeed;

        /// <summary>Every collectible in the loaded scenes that hasn't been collected yet.</summary>
        public static IReadOnlyList<Collectible> Waiting => waiting;

        public float Radius => radius;
        public bool IsCollected { get; private set; }
        /// <summary>True while it is fully inside the liquid blob, i.e. freezing now would catch it.</summary>
        public bool IsEngulfed { get; private set; }
        public event Action<Collectible> Collected;
        public event Action<Collectible> Dropped;
        /// <summary>Raised when a dropped item comes to rest, with what it landed on.</summary>
        public event Action<Collectible, Collider> Landed;

        // Statics survive entering play mode with domain reload off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => waiting.Clear();

        void Reset() => groundMask = LayerMask.GetMask("Default", "Environment", "Pushable");

        void Awake()
        {
            block = new MaterialPropertyBlock();
            if (marker != null) markerScale = marker.localScale;
            home = transform.position;
            ApplyGlow();
        }

        void OnEnable()
        {
            if (!IsCollected) waiting.Add(this);
        }

        void OnDisable() => waiting.Remove(this);

        /// <summary>
        /// Each blob piece reports every physics step; the item counts as inside if it's inside any of them, so a
        /// second piece reporting "not inside me" can't switch the glow off.
        /// </summary>
        public void SetEngulfed(bool value)
        {
            bool sameStep = Time.fixedTime == engulfedReportTime;
            engulfedReportTime = Time.fixedTime;
            IsEngulfed = (value || (sameStep && IsEngulfed)) && !IsCollected;
        }

        /// <summary>
        /// Called by the blob that froze around it. It stops waiting; the blob moves it from now on. Plays
        /// event:/Item_Collect, labelled for a seed or a pocket magnet.
        /// </summary>
        public void Collect()
        {
            if (IsCollected) return;
            IsCollected = true;
            IsEngulfed = false;
            waiting.Remove(this);
            ShowMarker(false);
            PlayCollectSound();
            Collected?.Invoke(this);
        }

        void PlayCollectSound()
        {
            string label = TryGetComponent(out IronBall _) ? GameSounds.ItemMagnet
                : TryGetComponent(out Seed _) ? GameSounds.ItemSeed
                : null;
            if (label != null) GameSounds.ItemCollect.PlayAt(transform.position, GameSounds.ItemParameter, label);
            else GameSounds.ItemCollect.PlayAt(transform.position);
        }

        /// <summary>Called by the crystal when it melts: the item falls from where it is and waits to be caught again.</summary>
        public void Drop()
        {
            if (!IsCollected) return;
            IsCollected = false;
            loose = true;
            resting = false;
            fallSpeed = 0f;
            // The crystal may have tumbled it; the marker below it has to lie flat.
            transform.rotation = Quaternion.identity;
            waiting.Add(this);
            Dropped?.Invoke(this);
        }

        void FixedUpdate()
        {
            if (!loose || IsCollected) return;

            float dt = Time.fixedDeltaTime;
            fallSpeed -= Physics.gravity.y * dt;
            Vector3 origin = transform.position + Vector3.up * SupportReach;
            if (Physics.SphereCast(origin, radius, Vector3.down, out RaycastHit hit, SupportReach + fallSpeed * dt,
                                   groundMask, QueryTriggerInteraction.Ignore))
            {
                transform.position = origin + Vector3.down * hit.distance;
                fallSpeed = 0f;
                ShowMarker(true);
                if (!resting)
                {
                    resting = true;
                    Landed?.Invoke(this, hit.collider);
                }
                return;
            }

            transform.position += Vector3.down * (fallSpeed * dt);
            resting = false;
            ShowMarker(false);
            if (transform.position.y < lostHeight)
            {
                transform.position = home;
                loose = false;
                ShowMarker(true);
            }
        }

        void ShowMarker(bool show)
        {
            if (marker != null && marker.gameObject.activeSelf != show) marker.gameObject.SetActive(show);
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
