using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Moves a door, lift or bridge between its start position and an offset while its buttons are pressed.
    /// It is a kinematic body moved with MovePosition, so whatever stands on a lift (blob, ship, crates) rides along.
    /// It never crushes anything: before each step it sweeps its box ahead, and if the blob, the ship or a crate is
    /// in the way it waits until the way is clear (an unstoppable mover pressing a body into the floor would push it
    /// through). An optional hold time keeps it active after the buttons are released, which allows timing puzzles:
    /// press as a solid, melt, rush through as liquid. In cycle mode it shuttles back and forth while powered, like an
    /// elevator you board at either end.
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
        [Tooltip("While active, keep travelling back and forth instead of staying at the offset (a powered elevator).")]
        [SerializeField] bool cycleWhileActive;
        [Tooltip("When cycling, how long it waits at each end, s.")]
        [SerializeField] float endPause = 1.5f;

        [Header("Safety")]
        [Tooltip("Lifts: things standing on top ride along when it goes up. Off for doors and gates, which stop for " +
                 "anything in their way in either direction (so a crate can jam a gate open).")]
        [SerializeField] bool carriesRiders;
        [Tooltip("Bodies it won't push or crush; it waits for them to move out of the way.")]
        [SerializeField] LayerMask blockingMask;

        // Extra look-ahead beyond one step, m.
        const float SafetyMargin = 0.05f;

        Rigidbody body;
        BoxCollider box;
        Vector3 restPosition;
        float activeUntil = float.NegativeInfinity;
        bool headingOut = true;
        float pauseUntil = float.NegativeInfinity;

        /// <summary>True while moving toward (or resting at) the active position.</summary>
        public bool IsActive { get; private set; }
        /// <summary>0 at the start position, 1 at the active position.</summary>
        public float Progress { get; private set; }

        void Reset() => blockingMask = LayerMask.GetMask("PlayerLiquid", "PlayerSolid", "Ship", "Pushable");

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            box = GetComponent<BoxCollider>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            restPosition = body.position;
        }

        void FixedUpdate()
        {
            if (ButtonsSatisfied()) activeUntil = Time.time + holdTime;
            IsActive = Time.time <= activeUntil;

            Vector3 target = restPosition + (IsActive && headingOut ? activeOffset : Vector3.zero);
            if (!IsActive) headingOut = true;
            else if (cycleWhileActive && (body.position - target).sqrMagnitude < 1e-6f)
            {
                // Arrived at one end: wait there, then head for the other.
                if (pauseUntil < 0f) pauseUntil = Time.time + endPause;
                else if (Time.time >= pauseUntil)
                {
                    headingOut = !headingOut;
                    pauseUntil = float.NegativeInfinity;
                }
            }

            Vector3 next = Vector3.MoveTowards(body.position, target, speed * Time.fixedDeltaTime);
            if (next != body.position && IsBlocked(next - body.position)) next = body.position;
            if (next != body.position) body.MovePosition(next);

            float travel = activeOffset.magnitude;
            Progress = travel > 0f ? Vector3.Distance(next, restPosition) / travel : 0f;
        }

        /// <summary>Is a body this mover mustn't push sitting where its next step would go?</summary>
        bool IsBlocked(Vector3 step)
        {
            if (box == null || blockingMask.value == 0) return false;
            if (carriesRiders && step.y > 0f) return false;

            // Sweep the box (a touch smaller, so bodies merely brushing its sides don't count) one step ahead.
            // Bodies already touching it at the start, like riders on a lift, are ignored by the sweep.
            Vector3 halfExtents = Vector3.Scale(box.size, transform.lossyScale) * 0.475f;
            Vector3 center = transform.TransformPoint(box.center);
            return Physics.BoxCast(center, halfExtents, step.normalized, transform.rotation,
                                   step.magnitude + SafetyMargin, blockingMask, QueryTriggerInteraction.Ignore);
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
