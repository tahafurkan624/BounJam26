using System;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// An electromagnet that pulls the blob while the blob carries iron (the pull itself is
    /// <see cref="BlobMagnetism"/>). Pressing one of its on-buttons switches it on, and it stays on until one of its
    /// off-buttons is pressed. The field comes out of the front face (this object's position and forward): it reaches
    /// up to its range, only in front of the face and only where nothing solid is in between, and holds the blob a
    /// little in front of the face. The face glows while it's on, and a beam shows while it pulls. It plays event:/Magnet
    /// while it draws the blob in, fading out once the blob is held at the hold point or let go.
    /// </summary>
    public class Magnet : MonoBehaviour
    {
        [Tooltip("Pressing any of these switches the magnet on.")]
        [SerializeField] PressureButton[] onButtons;
        [Tooltip("Pressing any of these switches it off.")]
        [SerializeField] PressureButton[] offButtons;
        [SerializeField] bool startActive;

        [Header("Field")]
        [Tooltip("How far from the face the field reaches, m.")]
        [SerializeField] float range = 7f;
        [Tooltip("Where the blob's core is held, m in front of the face.")]
        [SerializeField] float holdDistance = 0.5f;
        [Tooltip("Top speed of the pull, m/s.")]
        [SerializeField] float pullSpeed = 6f;
        [Tooltip("How quickly the pull gets up to speed, m/s².")]
        [SerializeField] float pullAcceleration = 25f;
        [Tooltip("The pull slows down over this last stretch before the hold point, m.")]
        [SerializeField] float arriveDistance = 1.2f;
        [Tooltip("What blocks the field.")]
        [SerializeField] LayerMask sightMask;

        [Header("Look")]
        [SerializeField] Renderer faceRenderer;
        [SerializeField] Material idleMaterial;
        [SerializeField] Material activeMaterial;
        [Tooltip("Drawn from the face to the blob while pulling it.")]
        [SerializeField] LineRenderer beam;
        [SerializeField] float beamWidth = 0.05f;

        // The sight line starts just in front of the face, clear of the magnet's own housing.
        const float SightStart = 0.05f;
        // The pull sound plays while the blob is farther than this from the hold point, m...
        const float PullingDistance = 0.15f;
        // ...and fades out over this long once it's there, s.
        const float SoundFadeOut = 0.3f;

        static readonly List<Magnet> all = new List<Magnet>();

        LoopingSound pullSound;

        /// <summary>Every magnet in the loaded scenes, on or off.</summary>
        public static IReadOnlyList<Magnet> All => all;
        public bool IsActive { get; private set; }
        public Vector3 HoldPoint => transform.position + transform.forward * holdDistance;
        public float PullSpeed => pullSpeed;
        public float PullAcceleration => pullAcceleration;
        public float ArriveDistance => arriveDistance;
        /// <summary>What the magnet is pulling or holding right now; set by the pulled body, used for the beam.</summary>
        public Transform Pulling { get; set; }
        /// <summary>Raised when the magnet is switched on (true) or off (false).</summary>
        public event Action<bool> ActiveChanged;

        // Statics survive entering play mode with domain reload off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => all.Clear();

        void Reset() => sightMask = LayerMask.GetMask("Default", "Environment", "Pushable");

        void Awake()
        {
            IsActive = startActive;
            ApplyLook();
            pullSound = new LoopingSound(GameSounds.Magnet.Reference, transform, SoundFadeOut);
        }

        void Update()
        {
            bool pulling = IsActive && Pulling != null
                           && (Pulling.position - HoldPoint).sqrMagnitude > PullingDistance * PullingDistance;
            if (pulling && !pullSound.IsPlaying) pullSound.Play();
            else if (!pulling) pullSound.Stop();
            pullSound.Tick(Time.deltaTime);
        }

        void OnEnable()
        {
            all.Add(this);
            Subscribe(onButtons, OnOnButton, true);
            Subscribe(offButtons, OnOffButton, true);
        }

        void OnDisable()
        {
            all.Remove(this);
            pullSound?.Release();
            Subscribe(onButtons, OnOnButton, false);
            Subscribe(offButtons, OnOffButton, false);
        }

        public void Activate() => Switch(true);

        public void Deactivate() => Switch(false);

        /// <summary>Does the field reach this point: in range, in front of the face, with nothing solid in between?</summary>
        public bool Reaches(Vector3 point)
        {
            Vector3 toPoint = point - transform.position;
            if (toPoint.sqrMagnitude > range * range || Vector3.Dot(toPoint, transform.forward) <= 0f) return false;
            Vector3 start = transform.position + transform.forward * SightStart;
            return !Physics.Linecast(start, point, sightMask, QueryTriggerInteraction.Ignore);
        }

        void OnOnButton(bool pressed)
        {
            if (pressed) Activate();
        }

        void OnOffButton(bool pressed)
        {
            if (pressed) Deactivate();
        }

        static void Subscribe(PressureButton[] buttons, Action<bool> handler, bool add)
        {
            if (buttons == null) return;
            foreach (PressureButton button in buttons)
            {
                if (button == null) continue;
                if (add) button.PressedChanged += handler;
                else button.PressedChanged -= handler;
            }
        }

        void Switch(bool on)
        {
            if (on == IsActive) return;
            IsActive = on;
            ApplyLook();
            ActiveChanged?.Invoke(on);
        }

        void ApplyLook()
        {
            if (faceRenderer != null) faceRenderer.sharedMaterial = IsActive ? activeMaterial : idleMaterial;
        }

        void LateUpdate()
        {
            if (beam == null) return;
            bool show = IsActive && Pulling != null;
            beam.enabled = show;
            if (!show) return;
            beam.SetPosition(0, transform.position);
            beam.SetPosition(1, Pulling.position);
            beam.widthMultiplier = beamWidth * (0.6f + 0.4f * Mathf.Sin(Time.time * 40f));
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, range);
            Gizmos.DrawLine(transform.position, HoldPoint);
            Gizmos.DrawWireSphere(HoldPoint, 0.1f);
        }
    }
}
