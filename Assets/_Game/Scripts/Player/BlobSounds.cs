using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// The blob's FMOD sound effects apart from moving (see <see cref="BlobMovementSound"/>), each 3D and following the
    /// piece that makes it: event:/Water_Jump as it takes off (JumpState Up) and as it comes back down onto the ground
    /// after a jump or a fall as liquid (JumpState Down; the crystal landing plays <see cref="BlobIceSound"/>'s Drop
    /// instead), event:/Water_Freeze as it hardens into the crystal,
    /// event:/Water_Merge as two halves flow back into one, and event:/Water_Split for as long as a piercer pipe has the
    /// blob, from being pulled into the mouth until the first half comes out, where it fades away. The halves of a cut
    /// blob jump and freeze together, and each plays its own.
    /// </summary>
    [RequireComponent(typeof(BlobMotor), typeof(BlobForm), typeof(BlobSplit))]
    public class BlobSounds : MonoBehaviour
    {
        const string JumpStateParameter = "JumpState";
        const string JumpUpLabel = "Up";
        const string JumpDownLabel = "Down";

        [SerializeField] EventReference jumpEvent;
        [Tooltip("The blob has to be off the ground at least this long before coming down plays the landing sound, " +
                 "so bumps along the way stay quiet, s.")]
        [SerializeField] float minAirTime = 0.15f;
        [Tooltip("Coming down slower than this makes no landing sound, m/s.")]
        [SerializeField] float minLandSpeed = 1f;
        [SerializeField] EventReference freezeEvent;
        [SerializeField] EventReference splitEvent;
        [SerializeField] EventReference mergeEvent;
        [Tooltip("The split sound fades out over this long once the first half is out of the pipe, s.")]
        [SerializeField] float splitFadeOut = 0.3f;

        Rigidbody body;
        BlobMotor motor;
        BlobForm form;
        BlobSplit split;
        BlobWater water;
        BlobIceSound iceSound;
        EventInstance splitting;
        bool fading;
        float fadeLeft;
        float airTime; // how long the blob has been off the ground, s
        float fallSpeed; // the fastest it has come down while off the ground, m/s

        /// <summary>Landing sounds played since the blob started (for testing).</summary>
        public int Landings { get; private set; }

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            motor = GetComponent<BlobMotor>();
            form = GetComponent<BlobForm>();
            split = GetComponent<BlobSplit>();
            water = GetComponent<BlobWater>();
            iceSound = GetComponent<BlobIceSound>();
        }

        void OnEnable()
        {
            motor.Jumped += OnJumped;
            form.FormChanged += OnFormChanged;
            split.EnteredPipe += OnEnteredPipe;
            split.LeftPipe += OnLeftPipe;
            split.Merged += OnMerged;
        }

        // Disabled or destroyed mid-cut (a level restart): the split sound is long, so don't leave it playing.
        void OnDisable()
        {
            motor.Jumped -= OnJumped;
            form.FormChanged -= OnFormChanged;
            split.EnteredPipe -= OnEnteredPipe;
            split.LeftPipe -= OnLeftPipe;
            split.Merged -= OnMerged;
            StopSplitting();
            airTime = fallSpeed = 0f;
        }

        void Update()
        {
            UpdateLanding(Time.deltaTime);
            if (!fading) return;
            fadeLeft -= Time.deltaTime;
            if (fadeLeft > 0f) splitting.setVolume(fadeLeft / splitFadeOut);
            else StopSplitting();
        }

        /// <summary>Plays the landing sound on the frame the blob is back on the ground after the minimum air time.</summary>
        void UpdateLanding(float dt)
        {
            // Carried by a magnet or a pipe, dissolved or floating in water: not falling onto anything.
            bool carried = motor.IsDriven || motor.IsSwimming || split.InPipe || (water != null && water.IsDissolved);
            if (!carried && !motor.IsGrounded)
            {
                airTime += dt;
                fallSpeed = Mathf.Max(fallSpeed, -body.linearVelocity.y);
                return;
            }
            if (!carried && airTime >= minAirTime && fallSpeed >= minLandSpeed)
            {
                // The crystal has its own landing sound.
                if (!form.IsSolid) Play(jumpEvent, JumpDownLabel);
                else if (iceSound != null) iceSound.PlayDrop();
            }
            airTime = fallSpeed = 0f;
        }

        void OnJumped(float _) => Play(jumpEvent, JumpUpLabel);

        void OnFormChanged(bool solid)
        {
            if (solid) Play(freezeEvent);
        }

        void OnMerged() => Play(mergeEvent);

        void OnEnteredPipe()
        {
            StopSplitting();
            if (splitEvent.IsNull) return;
            splitting = RuntimeManager.CreateInstance(splitEvent);
            RuntimeManager.AttachInstanceToGameObject(splitting, gameObject, body);
            splitting.start();
        }

        // Only the piece that went in has the sound: the other half was copied from it inside the pipe, without it.
        void OnLeftPipe()
        {
            if (!splitting.isValid() || fading) return;
            fading = true;
            fadeLeft = splitFadeOut;
        }

        void StopSplitting()
        {
            fading = false;
            if (!splitting.isValid()) return;
            splitting.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            splitting.release();
            splitting.clearHandle();
        }

        void Play(EventReference sound)
        {
            if (!sound.IsNull) RuntimeManager.PlayOneShotAttached(sound, gameObject);
        }

        void Play(EventReference sound, string jumpState)
        {
            if (sound.IsNull) return;
            if (jumpState == JumpDownLabel) Landings++;
            var instance = RuntimeManager.CreateInstance(sound);
            RuntimeManager.AttachInstanceToGameObject(instance, gameObject, body);
            instance.setParameterByNameWithLabel(JumpStateParameter, jumpState);
            instance.start();
            instance.release();
        }
    }
}
