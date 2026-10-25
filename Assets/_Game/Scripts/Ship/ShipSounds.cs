using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// The ship's FMOD sounds, 3D and at the ship.
    /// event:/Water_Splash when the hull hits the liquid blob hard enough to splash it (see <see cref="ShipSplash"/>):
    /// falling onto the jelly, the blob landing or running in under the ship, or the ship carrying on as the blob stops
    /// dead. The frozen blob isn't liquid, so landing on the crystal stays quiet. Setting off from standing still with the
    /// ship riding on top stays quiet too: the jelly shrinks to its moving size and drops the ship into itself. A ship
    /// the blob picks up off the ground (or a dock, a crate) as it sets off still splashes. Only one splash sounds at a
    /// time: a hit while the last one is still playing stays quiet. The level start stays quiet while the ship settles.
    /// event:/Ship_Drop when the ship comes down hard enough on the ground or anything else flat (a dock, the top of a
    /// crate). Hitting a wall or the side of something, or the blob, stays quiet.
    /// </summary>
    [RequireComponent(typeof(ShipSplash))]
    public class ShipSounds : MonoBehaviour
    {
        [SerializeField] EventReference splashEvent;
        [Tooltip("How long after the blob sets off with the ship riding on it a splash stays quiet, s.")]
        [SerializeField] float setOffQuiet = 0.6f;

        [SerializeField] EventReference dropEvent;
        [Tooltip("Hitting something slower than this makes no sound, m/s.")]
        [SerializeField] float minHitSpeed = 1f;
        [Tooltip("Only landing on a surface at most this steep makes the hit sound, so hitting a wall stays quiet, " +
                 "degrees from flat.")]
        [SerializeField] float maxGroundAngle = 45f;
        [Tooltip("Shortest time between two hit sounds, s. The hull and the funnel hitting at once are one hit.")]
        [SerializeField] float hitGap = 0.25f;

        // The blob moving slower than this share of its top speed is standing still.
        const float StandingSpeed = 0.1f;
        // How long the level start stays quiet while the ship settles into the jelly, s.
        const float StartQuiet = 0.4f;

        ShipSplash splash;
        ShipBuoyancy buoyancy;
        // The one splash event, started again for each splash, so a new one can't play over it.
        EventInstance splashSound;
        float lastHitTime;
        BlobSkin riddenSkin;
        BlobMotor ridden;
        bool blobMoving;
        bool touchingOther; // touched something that isn't the blob in the last physics step
        float quietUntil;

        /// <summary>Splashes played since the ship started (for testing).</summary>
        public int Splashes { get; private set; }
        /// <summary>Hit sounds played since the ship started (for testing).</summary>
        public int Hits { get; private set; }
        /// <summary>True while a splash sound is playing, so another can't start.</summary>
        public bool IsSplashing
        {
            get
            {
                if (!splashSound.isValid()) return false;
                splashSound.getPlaybackState(out PLAYBACK_STATE state);
                return state != PLAYBACK_STATE.STOPPED;
            }
        }

        void Awake()
        {
            splash = GetComponent<ShipSplash>();
            buoyancy = GetComponent<ShipBuoyancy>();
        }

        void OnEnable()
        {
            lastHitTime = Time.time;
            quietUntil = Time.time + StartQuiet;
            splash.Hit += OnSplash;
        }

        void OnDisable() => splash.Hit -= OnSplash;

        void OnDestroy()
        {
            // Released, so a splash still playing finishes and FMOD frees it after.
            if (splashSound.isValid()) splashSound.release();
            splashSound.clearHandle();
        }

        void FixedUpdate()
        {
            BlobMotor motor = Ridden();
            bool moving = motor == null || motor.NormalizedSpeed >= StandingSpeed;
            // Setting off with the ship on top, not one the blob has only just reached, still resting on the ground.
            if (moving && !blobMoving && buoyancy.IsFloating && !touchingOther) quietUntil = Time.time + setOffQuiet;
            blobMoving = moving;
            touchingOther = false;
        }

        /// <summary>The motor of the blob the ship is on, kept up to date as it moves from piece to piece.</summary>
        BlobMotor Ridden()
        {
            BlobSkin skin = buoyancy.Blob;
            if (skin == riddenSkin) return ridden;
            riddenSkin = skin;
            ridden = skin != null ? skin.GetComponent<BlobMotor>() : null;
            // Not seen standing still yet, so not setting off either.
            blobMoving = true;
            return ridden;
        }

        void OnSplash()
        {
            if (splashEvent.IsNull || Time.time < quietUntil || IsSplashing) return;
            if (!splashSound.isValid())
            {
                splashSound = RuntimeManager.CreateInstance(splashEvent);
                RuntimeManager.AttachInstanceToGameObject(splashSound, gameObject, GetComponent<Rigidbody>());
            }
            Splashes++;
            splashSound.start();
        }

        void OnCollisionEnter(Collision collision)
        {
            // The blob (its core, or the crystal) carries the ship rather than being hit by it.
            if (IsBlob(collision)) return;
            touchingOther = true;
            if (!HitGround(collision)) return;
            if (collision.relativeVelocity.magnitude < minHitSpeed || Time.time - lastHitTime < hitGap) return;
            lastHitTime = Time.time;
            Hits++;
            if (!dropEvent.IsNull) RuntimeManager.PlayOneShotAttached(dropEvent, gameObject);
        }

        void OnCollisionStay(Collision collision)
        {
            if (!IsBlob(collision)) touchingOther = true;
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
    }
}
