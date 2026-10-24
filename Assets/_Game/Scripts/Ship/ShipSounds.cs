using FMODUnity;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// The ship's FMOD sounds, 3D and at the ship.
    /// event:/Water_Splash when the ship lands in the blob's jelly: the blob picking it up off the floor or a dock,
    /// catching it again after it was thrown clear, or coming back down from a jump or a fall with the ship riding on it.
    /// The ship only counts as out of the jelly once it has been clear of it for the minimum time, and the blob only
    /// counts as coming down after the minimum air time, so bobbing at the surface and small drops stay quiet. A ship
    /// that starts the level in the jelly, or rides through a piercer pipe with it, doesn't splash.
    /// event:/Ship_Drop when the hull hits anything but the blob (the floor, a wall, a crate) hard enough.
    /// </summary>
    [RequireComponent(typeof(ShipBuoyancy))]
    public class ShipSounds : MonoBehaviour
    {
        [SerializeField] EventReference splashEvent;
        [Tooltip("The ship has to be clear of the jelly at least this long before coming back down into it splashes, s.")]
        [SerializeField] float minTimeOut = 0.15f;
        [Tooltip("The liquid blob carrying the ship has to be in the air at least this long before landing splashes, s. " +
                 "A full jump is about 0.8.")]
        [SerializeField] float minAirTime = 0.25f;

        [SerializeField] EventReference dropEvent;
        [Tooltip("Hitting something slower than this makes no sound, m/s.")]
        [SerializeField] float minHitSpeed = 1f;
        [Tooltip("Shortest time between two hit sounds, s. The hull and the funnel hitting at once are one hit.")]
        [SerializeField] float hitGap = 0.25f;

        // A splash this soon after the last (the ship thrown clear in a jump and landing back as the blob does) is
        // part of the same one, s. Also how long the level start stays quiet while the ship settles.
        const float SplashGap = 0.4f;

        ShipBuoyancy buoyancy;
        BlobSkin blob;
        BlobMotor blobMotor;
        bool wasOut;
        float airTime; // how long the blob under the ship has been in the air, s
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
            if (buoyancy.Blob != blob)
            {
                blob = buoyancy.Blob;
                blobMotor = blob.GetComponent<BlobMotor>();
                airTime = 0f;
            }

            bool landed = BlobLanded();
            if (!buoyancy.IsFloating)
            {
                if (buoyancy.TimeSinceFloating >= minTimeOut) wasOut = true;
                return;
            }
            if (!wasOut && !landed) return;
            wasOut = false;
            if (Time.time - lastSplashTime < SplashGap) return;
            lastSplashTime = Time.time;
            Splashes++;
            Play(splashEvent);
        }

        /// <summary>True on the step the liquid blob under the ship comes down after the minimum air time.</summary>
        bool BlobLanded()
        {
            if (blobMotor == null || blob.IsFrozen)
            {
                airTime = 0f;
                return false;
            }
            if (!blobMotor.IsGrounded)
            {
                airTime += Time.fixedDeltaTime;
                return false;
            }
            bool landed = airTime >= minAirTime;
            airTime = 0f;
            return landed;
        }

        void OnCollisionEnter(Collision collision)
        {
            // The blob (its core, or the crystal) carries the ship rather than being hit by it.
            if (collision.collider.GetComponentInParent<BlobMotor>() != null) return;
            if (collision.relativeVelocity.magnitude < minHitSpeed || Time.time - lastHitTime < hitGap) return;
            lastHitTime = Time.time;
            Hits++;
            Play(dropEvent);
        }

        void Play(EventReference sound)
        {
            if (!sound.IsNull) RuntimeManager.PlayOneShotAttached(sound, gameObject);
        }
    }
}
