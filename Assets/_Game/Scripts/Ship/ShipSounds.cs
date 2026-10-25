using FMODUnity;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// The ship's FMOD sounds, 3D and at the ship.
    /// event:/Water_Splash when the blob picks the ship up: the ship comes into the jelly after resting on something
    /// that isn't the blob (the floor, a dock, a crate). The ship bouncing on the blob while it's carried (the blob
    /// jumping, landing, or throwing it clear and catching it again) stays quiet. The ship only counts as out of the
    /// jelly once it has been clear of it for the minimum time. A ship that starts the level in the jelly, or rides
    /// through a piercer pipe with it, doesn't splash.
    /// event:/Ship_Drop when the ship comes down hard enough on the ground or anything else flat (a dock, the top of a
    /// crate). Hitting a wall or the side of something, or the blob, stays quiet.
    /// </summary>
    [RequireComponent(typeof(ShipBuoyancy))]
    public class ShipSounds : MonoBehaviour
    {
        [SerializeField] EventReference splashEvent;
        [Tooltip("The ship has to be clear of the jelly at least this long before being picked up again splashes, s.")]
        [SerializeField] float minTimeOut = 0.15f;

        [SerializeField] EventReference dropEvent;
        [Tooltip("Hitting something slower than this makes no sound, m/s.")]
        [SerializeField] float minHitSpeed = 1f;
        [Tooltip("Only landing on a surface at most this steep makes the hit sound, so hitting a wall stays quiet, " +
                 "degrees from flat.")]
        [SerializeField] float maxGroundAngle = 45f;
        [Tooltip("Shortest time between two hit sounds, s. The hull and the funnel hitting at once are one hit.")]
        [SerializeField] float hitGap = 0.25f;

        // A splash this soon after the last is part of the same one, s. Also how long the level start stays quiet
        // while the ship settles.
        const float SplashGap = 0.4f;

        ShipBuoyancy buoyancy;
        bool wasOut;
        bool restedOff; // touched something that isn't the blob while out of the jelly, so the next landing is a pickup
        float lastSplashTime;
        float lastHitTime;

        /// <summary>Splashes played since the ship started (for testing).</summary>
        public int Splashes { get; private set; }
        /// <summary>Hit sounds played since the ship started (for testing).</summary>
        public int Hits { get; private set; }

        void Awake() => buoyancy = GetComponent<ShipBuoyancy>();

        void OnEnable() => lastSplashTime = lastHitTime = Time.time;

        void FixedUpdate()
        {
            // Not placed yet, so not out of the jelly either.
            if (buoyancy.Blob == null || !buoyancy.Blob.IsReady) return;
            if (!buoyancy.IsFloating)
            {
                if (buoyancy.TimeSinceFloating >= minTimeOut) wasOut = true;
                return;
            }
            bool pickedUp = wasOut && restedOff;
            wasOut = restedOff = false;
            if (!pickedUp || Time.time - lastSplashTime < SplashGap) return;
            lastSplashTime = Time.time;
            Splashes++;
            Play(splashEvent);
        }

        void OnCollisionEnter(Collision collision)
        {
            // The blob (its core, or the crystal) carries the ship rather than being hit by it.
            if (IsBlob(collision)) return;
            MarkRestedOff();
            if (!HitGround(collision)) return;
            if (collision.relativeVelocity.magnitude < minHitSpeed || Time.time - lastHitTime < hitGap) return;
            lastHitTime = Time.time;
            Hits++;
            Play(dropEvent);
        }

        void OnCollisionStay(Collision collision)
        {
            if (!IsBlob(collision)) MarkRestedOff();
        }

        void MarkRestedOff()
        {
            if (!buoyancy.IsFloating) restedOff = true;
        }

        /// <summary>True when the ship came down on the surface it hit (a contact facing up), not into its side.</summary>
        bool HitGround(Collision collision)
        {
            float minUp = Mathf.Cos(maxGroundAngle * Mathf.Deg2Rad);
            for (int i = 0; i < collision.contactCount; i++)
                if (collision.GetContact(i).normal.y >= minUp) return true;
            return false;
        }

        static bool IsBlob(Collision collision) => collision.collider.GetComponentInParent<BlobMotor>() != null;

        void Play(EventReference sound)
        {
            if (!sound.IsNull) RuntimeManager.PlayOneShotAttached(sound, gameObject);
        }
    }
}
