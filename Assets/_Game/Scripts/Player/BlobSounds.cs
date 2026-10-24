using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// The blob's FMOD sound effects apart from moving (see <see cref="BlobMovementSound"/>), each 3D and following the
    /// piece that makes it: event:/Water_Jump as it takes off, event:/Water_Freeze as it hardens into the crystal,
    /// event:/Water_Merge as two halves flow back into one, and event:/Water_Split for as long as a piercer pipe has the
    /// blob, from being pulled into the mouth until the first half comes out, where it fades away. The halves of a cut
    /// blob jump and freeze together, and each plays its own.
    /// </summary>
    [RequireComponent(typeof(BlobMotor), typeof(BlobForm), typeof(BlobSplit))]
    public class BlobSounds : MonoBehaviour
    {
        [SerializeField] EventReference jumpEvent;
        [SerializeField] EventReference freezeEvent;
        [SerializeField] EventReference splitEvent;
        [SerializeField] EventReference mergeEvent;
        [Tooltip("The split sound fades out over this long once the first half is out of the pipe, s.")]
        [SerializeField] float splitFadeOut = 0.3f;

        Rigidbody body;
        BlobMotor motor;
        BlobForm form;
        BlobSplit split;
        EventInstance splitting;
        bool fading;
        float fadeLeft;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            motor = GetComponent<BlobMotor>();
            form = GetComponent<BlobForm>();
            split = GetComponent<BlobSplit>();
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
        }

        void Update()
        {
            if (!fading) return;
            fadeLeft -= Time.deltaTime;
            if (fadeLeft > 0f) splitting.setVolume(fadeLeft / splitFadeOut);
            else StopSplitting();
        }

        void OnJumped(float _) => Play(jumpEvent);

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
    }
}
