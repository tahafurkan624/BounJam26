using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Plays the FMOD movement event (event:/Water_Movement) while the blob actually moves, whatever moves it: its own
    /// input, a slope, a push. Speeding up past the start speed starts the event with State at 0: its start, then a loop
    /// it holds by itself. Slowing below the lower stop speed, and staying there for the stop delay, sets State to 1: the
    /// event plays its ending and stops on its own. Only speed along the ground counts, so a fall or a straight-up jump
    /// doesn't slosh. Moving again before an ending is over stops that ending and starts a fresh event, so a blob never
    /// plays more than one at a time. A piece a pipe carries, or a dissolved one, counts as not moving. The event is 3D
    /// and follows the blob; each piece of a cut blob plays its own.
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

        Rigidbody body;
        BlobMotor motor;
        BlobSplit split;
        BlobWater water;
        EventInstance instance;
        EventInstance ending; // the last instance let go of, which may still be playing its ending
        bool moving;
        float slowTime; // how long the speed has been below the stop speed while moving, s

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            motor = GetComponent<BlobMotor>();
            split = GetComponent<BlobSplit>();
            water = GetComponent<BlobWater>();
            if (movementEvent.IsNull)
            {
                Debug.LogWarning("[BounJam] BlobMovementSound has no FMOD event assigned.", this);
                enabled = false;
            }
        }

        void Update()
        {
            bool carried = (split != null && split.InPipe) || (water != null && water.IsDissolved);
            float speed = carried ? 0f : Vector3.ProjectOnPlane(motor.Velocity, motor.GroundNormal).magnitude;
            if (!moving)
            {
                if (speed > startSpeed) StartMoving();
                return;
            }
            if (speed >= stopSpeed) slowTime = 0f;
            else if ((slowTime += Time.deltaTime) >= stopDelay) StopMoving();
        }

        // Disabled or destroyed mid-move (a level restart, a half flowing back into the other): end it the usual way.
        void OnDisable()
        {
            if (moving) StopMoving();
        }

        void StartMoving()
        {
            moving = true;
            slowTime = 0f;
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
            instance.setParameterByName(stateParameter, StateStopping);
            instance.release();
            ending = instance;
        }
    }
}
