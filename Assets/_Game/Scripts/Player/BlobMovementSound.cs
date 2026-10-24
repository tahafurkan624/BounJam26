using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Plays the FMOD movement event (event:/Water_Movement) while the player moves the blob. Pushing the Move input
    /// starts the event with State at 0: its start, then a loop it holds by itself. Letting go sets State to 1: the event
    /// plays its ending and stops on its own. Moving again before an ending is over stops that ending and starts a fresh
    /// event, so a blob never plays more than one at a time. A piece a pipe carries, or a dissolved one, counts as not
    /// moving. The event is 3D and follows the blob; each piece of a cut blob plays its own.
    /// </summary>
    [RequireComponent(typeof(BlobMotor))]
    public class BlobMovementSound : MonoBehaviour
    {
        // Values of the event's State parameter.
        const float StateMoving = 0f;
        const float StateStopping = 1f;

        [SerializeField] PlayerInputReader input;
        [SerializeField] EventReference movementEvent;
        [Tooltip("The event's parameter that switches from its loop (0) to its ending (1).")]
        [SerializeField] string stateParameter = "State";
        [Tooltip("Move input shorter than this counts as none, so a drifting stick doesn't start the sound. 0..1.")]
        [Range(0f, 1f)]
        [SerializeField] float deadZone = 0.1f;

        Rigidbody body;
        BlobSplit split;
        BlobWater water;
        EventInstance instance;
        EventInstance ending; // the last instance let go of, which may still be playing its ending
        bool moving;

        void Reset() => input = GetComponent<PlayerInputReader>();

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            split = GetComponent<BlobSplit>();
            water = GetComponent<BlobWater>();
            if (input == null) input = GetComponent<PlayerInputReader>();
            if (movementEvent.IsNull)
            {
                Debug.LogWarning("[BounJam] BlobMovementSound has no FMOD event assigned.", this);
                enabled = false;
            }
        }

        void Update()
        {
            bool wantsToMove = input != null && input.Move.magnitude > deadZone
                               && !(split != null && split.InPipe) && !(water != null && water.IsDissolved);
            if (wantsToMove == moving) return;
            if (wantsToMove) StartMoving();
            else StopMoving();
        }

        // Disabled or destroyed mid-move (a level restart, a half flowing back into the other): end it the usual way.
        void OnDisable()
        {
            if (moving) StopMoving();
        }

        void StartMoving()
        {
            moving = true;
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
