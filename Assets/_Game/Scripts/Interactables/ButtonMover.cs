using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Moves a door, lift or bridge between its start position and an offset while its buttons are pressed.
    /// It is a kinematic body moved with MovePosition, so whatever stands on it (blob, ship, crates) rides along
    /// and whatever is in its way gets pushed. An optional hold time keeps it active after the buttons are released,
    /// which allows timing puzzles: press as a solid, melt, rush through as liquid.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ButtonMover : MonoBehaviour
    {
        [SerializeField] PressureButton[] buttons;
        [Tooltip("Needs every button pressed, instead of any one of them.")]
        [SerializeField] bool requireAll;
        [Tooltip("World-space offset from the start position while active; e.g. down 1.55 m for a gate that sinks into the floor.")]
        [SerializeField] Vector3 activeOffset = new Vector3(0f, 2f, 0f);
        [Tooltip("Travel speed, m/s.")]
        [SerializeField] float speed = 2f;
        [Tooltip("Stays active this long after the buttons are released, s.")]
        [SerializeField] float holdTime;

        Rigidbody body;
        Vector3 restPosition;
        float activeUntil = float.NegativeInfinity;

        /// <summary>True while moving toward (or resting at) the active position.</summary>
        public bool IsActive { get; private set; }
        /// <summary>0 at the start position, 1 at the active position.</summary>
        public float Progress { get; private set; }

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            restPosition = body.position;
        }

        void FixedUpdate()
        {
            if (ButtonsSatisfied()) activeUntil = Time.time + holdTime;
            IsActive = Time.time <= activeUntil;

            Vector3 target = restPosition + (IsActive ? activeOffset : Vector3.zero);
            Vector3 next = Vector3.MoveTowards(body.position, target, speed * Time.fixedDeltaTime);
            if (next != body.position) body.MovePosition(next);

            float travel = activeOffset.magnitude;
            Progress = travel > 0f ? Vector3.Distance(next, restPosition) / travel : 0f;
        }

        bool ButtonsSatisfied()
        {
            if (buttons == null || buttons.Length == 0) return false;
            foreach (PressureButton button in buttons)
            {
                if (button == null) continue;
                if (requireAll && !button.IsPressed) return false;
                if (!requireAll && button.IsPressed) return true;
            }
            return requireAll;
        }

        void OnDrawGizmosSelected()
        {
            Vector3 start = Application.isPlaying ? restPosition : transform.position;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(start, start + activeOffset);
            Gizmos.DrawWireCube(start + activeOffset, transform.lossyScale);
        }
    }
}
