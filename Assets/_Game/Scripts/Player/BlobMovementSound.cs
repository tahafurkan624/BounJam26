using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Plays the FMOD movement event (event:/Water_Movement) while the blob actually moves, whatever moves it: its own
    /// input, a slope, a push. Speeding up past the start speed starts the event with State at 0: its start, then a loop
    /// it holds by itself. Slowing below the lower stop speed, and staying there for the stop delay, sets State to 1: the
    /// event plays its ending and stops on its own. A sound that has played for less than the minimum play time skips
    /// the ending and fades out quickly instead, so a short nudge doesn't play a whole ending; so does one cut off by
    /// freezing into the crystal. Only speed along the ground counts, so a fall or a straight-up jump
    /// doesn't slosh. Moving again before an ending is over stops that ending and starts a fresh event, so a blob never
    /// plays more than one at a time. A piece a pipe carries, a dissolved one or the crystal (see
    /// <see cref="BlobIceSound"/>) counts as not moving. The event is 3D and follows the blob; each piece of a cut blob
    /// plays its own.
    /// Melting back into jelly starts the event too. If the blob sets off within the melt play time, that same sound goes
    /// on as its movement sound, uncut and not started again; if it stays put, the sound is cut short and fades out.
    /// The liquid landing sound (<see cref="BlobSounds"/>) stands in for the ending: an ending playing when the blob lands
    /// fades out, and so does a sound that stops within the land quiet time after.
    /// </summary>
    [RequireComponent(typeof(BlobMotor))]
    public class BlobMovementSound : MonoBehaviour
    {
        // Values of the event's State parameter.
        const float StateMoving = 0f;
        const float StateStopping = 1f;

        [SerializeField] EventReference movementEvent;
        [Tooltip("The event's parameter that switches from its loop (0) to its ending (1).")]
        [SerializeField] string stateParameter = "State";
        [Tooltip("Speed along the ground that starts the sound, m/s.")]
        [SerializeField] float startSpeed = 0.5f;
        [Tooltip("Speed along the ground below which the sound ends, m/s. Lower than the start speed, so a speed " +
                 "hovering around one value doesn't flick the sound on and off.")]
        [SerializeField] float stopSpeed = 0.2f;
        [Tooltip("The speed has to stay below the stop speed this long before the ending plays, so a bump that stalls " +
                 "the blob for a moment doesn't cut the loop, s.")]
        [SerializeField] float stopDelay = 0.1f;
        [Tooltip("A sound stopped before it has played this long fades out instead of playing its ending, so stopping " +
                 "while the start is still playing doesn't put the ending right after it. About the length of the " +
                 "event's start, s.")]
        [SerializeField] float minPlayTime = 0.6f;
        [Tooltip("How long the sound melting starts plays when the blob doesn't set off, s. Setting off before then " +
                 "keeps it going as the movement sound.")]
        [SerializeField] float meltPlayTime = 0.5f;
        [Tooltip("How long a cut sound (a melt sound or one too short for its ending) takes to fade out, s.")]
        [SerializeField] float meltFadeOut = 0.2f;
        [Tooltip("Stopping within this long of the liquid landing sound fades out instead of playing the ending, so " +
                 "the two don't play back to back, s.")]
        [SerializeField] float landQuietTime = 0.6f;

        Rigidbody body;
        BlobMotor motor;
        BlobForm form;
        BlobSplit split;
        BlobWater water;
        BlobSounds sounds;
        EventInstance instance;
        EventInstance ending; // the last instance let go of, which may still be playing its ending
        EventInstance fading; // a cut sound on its way out
        bool moving;
        bool melting; // the instance was started by melting, and the blob hasn't set off since
        float slowTime; // how long the speed has been below the stop speed while moving, s
        float playTime; // how long the current instance has played, s
        float meltLeft; // how long the melt sound plays on before it's cut, unless the blob sets off, s
        float fadeLeft; // how long the cut melt sound has left to fade out, s
        float landQuietLeft; // how long a stop still fades out instead of playing its ending after a landing, s

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            motor = GetComponent<BlobMotor>();
            form = GetComponent<BlobForm>();
            split = GetComponent<BlobSplit>();
            water = GetComponent<BlobWater>();
            sounds = GetComponent<BlobSounds>();
            if (movementEvent.IsNull)
            {
                Debug.LogWarning("[BounJam] BlobMovementSound has no FMOD event assigned.", this);
                enabled = false;
            }
        }

        void OnEnable()
        {
            if (form != null) form.FormChanged += OnFormChanged;
            if (sounds != null) sounds.Landed += OnLanded;
        }

        void Update()
        {
            if (landQuietLeft > 0f) landQuietLeft -= Time.deltaTime;
            if (fading.isValid())
            {
                fadeLeft -= Time.deltaTime;
                if (fadeLeft > 0f) fading.setVolume(fadeLeft / meltFadeOut);
                else StopFading();
            }

            bool carried = (split != null && split.InPipe) || (water != null && water.IsDissolved);
            // The crystal has its own sounds (BlobIceSound).
            bool solid = form != null && form.IsSolid;
            float speed = carried || solid ? 0f : Vector3.ProjectOnPlane(motor.Velocity, motor.GroundNormal).magnitude;
            if (!moving)
            {
                if (speed > startSpeed) StartMoving();
                return;
            }
            playTime += Time.deltaTime;
            if (melting)
            {
                if (speed > startSpeed)
                {
                    melting = false;
                    slowTime = 0f;
                }
                else if ((meltLeft -= Time.deltaTime) <= 0f) Cut();
                return;
            }
            if (speed >= stopSpeed) slowTime = 0f;
            else if ((slowTime += Time.deltaTime) >= stopDelay)
            {
                if (playTime < minPlayTime || landQuietLeft > 0f) Cut();
                else StopMoving();
            }
        }

        // Disabled or destroyed mid-move (a level restart, a half flowing back into the other): end it the usual way.
        void OnDisable()
        {
            if (form != null) form.FormChanged -= OnFormChanged;
            if (sounds != null) sounds.Landed -= OnLanded;
            landQuietLeft = 0f;
            if (moving)
            {
                // Too short for its ending: cut, and stopped right away just below.
                if (playTime < minPlayTime) Cut();
                else StopMoving();
            }
            StopFading();
        }

        // Already sloshing (melted again before the last slosh ended): that sound just goes on.
        void OnFormChanged(bool solid)
        {
            // Froze mid-move: the slosh fades out without its ending.
            if (solid)
            {
                if (moving) Cut();
                return;
            }
            if (moving) return;
            StartMoving();
            melting = true;
            meltLeft = meltPlayTime;
        }

        // The landing sound covers the stop: an ending already playing (stopped mid-fall) fades out, and stopping
        // shortly after fades out instead of playing its ending.
        void OnLanded()
        {
            landQuietLeft = landQuietTime;
            if (!ending.isValid()) return;
            StopFading();
            fading = ending;
            ending.clearHandle();
            fadeLeft = meltFadeOut;
        }

        void StartMoving()
        {
            moving = true;
            slowTime = 0f;
            playTime = 0f;
            // Cut the previous ending short so quick stop-and-go doesn't pile up instances. Fade-out keeps any release
            // the event has in FMOD Studio; without one it stops right away.
            if (ending.isValid()) ending.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            instance = RuntimeManager.CreateInstance(movementEvent);
            RuntimeManager.AttachInstanceToGameObject(instance, gameObject, body);
            instance.setParameterByName(stateParameter, StateMoving);
            instance.start();
        }

        /// <summary>On to the ending. Released, so FMOD frees the instance once the ending has played.</summary>
        void StopMoving()
        {
            moving = false;
            melting = false;
            instance.setParameterByName(stateParameter, StateStopping);
            instance.release();
            ending = instance;
        }

        /// <summary>
        /// Melted and stayed put, froze, or stopped before the minimum play time: the sound fades out quickly instead of
        /// playing its ending.
        /// </summary>
        void Cut()
        {
            moving = false;
            melting = false;
            StopFading();
            fading = instance;
            fadeLeft = meltFadeOut;
        }

        void StopFading()
        {
            if (!fading.isValid()) return;
            fading.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            fading.release();
            fading.clearHandle();
        }
    }
}
