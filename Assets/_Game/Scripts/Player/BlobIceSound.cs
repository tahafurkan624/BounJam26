using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// The crystal's FMOD sounds, 3D and following it. On the ground, event:/Ice_Movement with its Movement parameter:
    /// moving along the ground plays it on the Slide label for as long as the move lasts, whether or not the crystal
    /// turns as it goes. Landing after a fall plays it on the Drop label once (<see cref="BlobSounds"/> notices the
    /// landing and calls <see cref="PlayDrop"/>). Floating in water and moving, event:/Ice_On_Water plays instead. Liquid, carried by a pipe or a magnet, or
    /// dissolved, it's silent (the liquid's sound is <see cref="BlobMovementSound"/>).
    /// </summary>
    [RequireComponent(typeof(BlobMotor), typeof(BlobForm))]
    public class BlobIceSound : MonoBehaviour
    {
        const string MovementParameter = "Movement";
        const string SlideLabel = "Slide";
        const string DropLabel = "Drop";

        [SerializeField] EventReference iceMovementEvent;
        [SerializeField] EventReference iceOnWaterEvent;
        [Tooltip("Speed that starts the slide or the water sound, m/s.")]
        [SerializeField] float startSpeed = 0.3f;
        [Tooltip("Speed below which they stop, m/s. Lower than the start speed, so they don't flick on and off.")]
        [SerializeField] float stopSpeed = 0.15f;
        [Tooltip("The speed has to stay below the stop speed (or the crystal off the ground) this long before they " +
                 "stop, s.")]
        [SerializeField] float stopDelay = 0.1f;
        [Tooltip("How long the slide and water sounds take to fade out, s.")]
        [SerializeField] float fadeOut = 0.2f;
        [Tooltip("The Drop sound is stopped this long after it starts, s. The event loops for sliding, so it may " +
                 "never end by itself; set this past the Drop sound's length.")]
        [SerializeField] float dropSoundLength = 3f;

        Rigidbody body;
        BlobMotor motor;
        BlobForm form;
        BlobSplit split;
        BlobWater water;
        LoopingSound slide;
        LoopingSound floating;
        EventInstance drop;
        float dropStopTime;
        float slowTime; // how long the slide or water sound has been without enough speed, s

        /// <summary>Slide sounds started since the blob started (for testing).</summary>
        public int Slides { get; private set; }
        /// <summary>Landing (Drop) sounds played since the blob started (for testing).</summary>
        public int Drops { get; private set; }
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
            StopDrop();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            slide.Tick(dt);
            floating.Tick(dt);
            if (drop.isValid() && Time.time >= dropStopTime) StopDrop();

            bool silent = !form.IsSolid || motor.IsDriven || (split != null && split.InPipe)
                          || (water != null && water.IsDissolved);
            if (silent)
            {
                slide.Stop();
                floating.Stop();
                return;
            }

            if (motor.IsSwimming)
            {
                slide.Stop();
                Keep(floating, Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude, dt);
                return;
            }
            floating.Stop();

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

        /// <summary>The crystal came down onto the ground after a fall: the Drop sound, once.</summary>
        public void PlayDrop()
        {
            if (iceMovementEvent.IsNull) return;
            Drops++;
            StopDrop();
            drop = RuntimeManager.CreateInstance(iceMovementEvent);
            RuntimeManager.AttachInstanceToGameObject(drop, gameObject, body);
            drop.setParameterByNameWithLabel(MovementParameter, DropLabel);
            drop.start();
            dropStopTime = Time.time + dropSoundLength;
        }

        void StopDrop()
        {
            if (!drop.isValid()) return;
            drop.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            drop.release();
            drop.clearHandle();
        }
    }
}
