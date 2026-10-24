using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// The crystal's FMOD sounds, 3D and following it. On the ground, event:/Ice_Movement with its Movement parameter:
    /// sliding along without turning plays it on the Slide label for as long as the slide lasts; turning (rolling,
    /// tipping over) plays nothing until the crystal stops turning, then the Rotate label once, the thud of it settling.
    /// Floating in water and moving, event:/Ice_On_Water plays instead. Liquid, carried by a pipe or a magnet, or
    /// dissolved, it's silent (the liquid's sound is <see cref="BlobMovementSound"/>).
    /// </summary>
    [RequireComponent(typeof(BlobMotor), typeof(BlobForm))]
    public class BlobIceSound : MonoBehaviour
    {
        const string MovementParameter = "Movement";
        const string SlideLabel = "Slide";
        const string RotateLabel = "Rotate";

        [SerializeField] EventReference iceMovementEvent;
        [SerializeField] EventReference iceOnWaterEvent;
        [Tooltip("Speed that starts the slide or the water sound, m/s.")]
        [SerializeField] float startSpeed = 0.3f;
        [Tooltip("Speed below which they stop, m/s. Lower than the start speed, so they don't flick on and off.")]
        [SerializeField] float stopSpeed = 0.15f;
        [Tooltip("The speed has to stay below the stop speed (or the crystal off the ground) this long before they " +
                 "stop, s.")]
        [SerializeField] float stopDelay = 0.1f;
        [Tooltip("Turning faster than this counts as rolling or tipping, not sliding, rad/s.")]
        [SerializeField] float turnSpeed = 1f;
        [Tooltip("Once it has turned, it has to stay below this turning speed for the settle time before the Rotate " +
                 "sound plays, rad/s.")]
        [SerializeField] float settledTurnSpeed = 0.2f;
        [SerializeField] float settleTime = 0.1f;
        [Tooltip("The Rotate sound is stopped this long after it starts, s. The event loops for sliding, so it never " +
                 "ends by itself; set this past the longest Rotate sound.")]
        [SerializeField] float rotateSoundLength = 3f;
        [Tooltip("How long the slide and water sounds take to fade out, s.")]
        [SerializeField] float fadeOut = 0.2f;

        Rigidbody body;
        BlobMotor motor;
        BlobForm form;
        BlobSplit split;
        BlobWater water;
        LoopingSound slide;
        LoopingSound floating;
        EventInstance settle;
        float settleStopTime;
        bool turning;
        float calmTime; // how long the turning has been slow since it last turned fast, s
        float slowTime; // how long the slide or water sound has been without enough speed, s

        /// <summary>Slide sounds started since the blob started (for testing).</summary>
        public int Slides { get; private set; }
        /// <summary>Settling (Rotate) sounds played since the blob started (for testing).</summary>
        public int Settles { get; private set; }
        /// <summary>Water sounds started since the blob started (for testing).</summary>
        public int Glides { get; private set; }

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            motor = GetComponent<BlobMotor>();
            form = GetComponent<BlobForm>();
            split = GetComponent<BlobSplit>();
            water = GetComponent<BlobWater>();
            slide = new LoopingSound(iceMovementEvent, transform, fadeOut, body, MovementParameter, SlideLabel);
            floating = new LoopingSound(iceOnWaterEvent, transform, fadeOut, body);
        }

        void OnDisable()
        {
            slide.Release();
            floating.Release();
            StopSettle();
            turning = false;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            slide.Tick(dt);
            floating.Tick(dt);
            if (settle.isValid() && Time.time >= settleStopTime) StopSettle();

            bool silent = !form.IsSolid || motor.IsDriven || (split != null && split.InPipe)
                          || (water != null && water.IsDissolved);
            if (silent)
            {
                slide.Stop();
                floating.Stop();
                turning = false;
                return;
            }

            if (motor.IsSwimming)
            {
                slide.Stop();
                turning = false;
                Keep(floating, Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude, dt);
                return;
            }
            floating.Stop();

            // Turning: quiet until it has stopped, then the settling sound once.
            float spin = body.angularVelocity.magnitude;
            if (spin > turnSpeed)
            {
                turning = true;
                calmTime = 0f;
                slide.Stop();
                return;
            }
            if (turning)
            {
                calmTime = spin < settledTurnSpeed ? calmTime + dt : 0f;
                if (calmTime < settleTime) return;
                turning = false;
                PlaySettle();
            }

            float speed = motor.IsGrounded ? Vector3.ProjectOnPlane(body.linearVelocity, motor.GroundNormal).magnitude : 0f;
            Keep(slide, speed, dt);
        }

        /// <summary>Starts the sound above the start speed and stops it once below the stop speed for the stop delay.</summary>
        void Keep(LoopingSound sound, float speed, float dt)
        {
            if (!sound.IsPlaying)
            {
                if (speed > startSpeed)
                {
                    if (sound == slide) Slides++;
                    else Glides++;
                    sound.Play();
                    slowTime = 0f;
                }
                return;
            }
            if (speed >= stopSpeed) slowTime = 0f;
            else if ((slowTime += dt) >= stopDelay) sound.Stop();
        }

        void PlaySettle()
        {
            if (iceMovementEvent.IsNull) return;
            Settles++;
            StopSettle();
            settle = RuntimeManager.CreateInstance(iceMovementEvent);
            RuntimeManager.AttachInstanceToGameObject(settle, gameObject, body);
            settle.setParameterByNameWithLabel(MovementParameter, RotateLabel);
            settle.start();
            settleStopTime = Time.time + rotateSoundLength;
        }

        void StopSettle()
        {
            if (!settle.isValid()) return;
            settle.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            settle.release();
            settle.clearHandle();
        }
    }
}
