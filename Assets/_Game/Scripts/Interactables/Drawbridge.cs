using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// A ramp hinged at its top edge that stands raised, closing the way, until its buttons are pressed; then it falls
    /// down onto the floor below and stays there as a bridge. The deck is a kinematic body turned with MoveRotation
    /// round the hinge, so it gathers speed as it falls, bounces a little where it lands, and shoves nothing on the way:
    /// if the blob, the ship or a crate is under it, it waits until they move off. With Stays Down off it rises again
    /// when the buttons are released (and waits, too, while anything is standing on it).
    /// Lowering and raising each play event:/Doors once.
    /// </summary>
    public class Drawbridge : MonoBehaviour
    {
        [SerializeField] PressureButton[] buttons;
        [Tooltip("Needs every button pressed, instead of any one of them.")]
        [SerializeField] bool requireAll;
        [Tooltip("Once lowered, stays down for good.")]
        [SerializeField] bool staysDown = true;

        [Tooltip("The deck: a child kinematic body whose pivot is the hinge, lying lowered at its rest rotation.")]
        [SerializeField] Rigidbody deck;
        [Tooltip("How far the deck swings up about the hinge (its local X) when raised, degrees.")]
        [SerializeField] float raisedAngle = 105f;

        [Header("Motion")]
        [Tooltip("How fast the falling deck gathers speed, degrees/s².")]
        [SerializeField] float fallAcceleration = 160f;
        [Tooltip("Top speed of the falling deck, degrees/s.")]
        [SerializeField] float maxFallSpeed = 140f;
        [Tooltip("How much of its speed the deck keeps bouncing up off the floor; 0 lands dead.")]
        [SerializeField, Range(0f, 0.8f)] float bounce = 0.25f;
        [Tooltip("How fast it rises again, degrees/s.")]
        [SerializeField] float raiseSpeed = 45f;

        [Header("Safety")]
        [Tooltip("Bodies it won't push; it waits for them to move out of the way.")]
        [SerializeField] LayerMask blockingMask;

        // A bounce slower than this ends on the floor, degrees/s.
        const float SettleSpeed = 4f;

        BoxCollider shape;
        // The deck's box in the deck's own frame: its centre from the hinge, its turn and half size.
        Vector3 shapeOffset, shapeHalfExtents;
        Quaternion shapeTurn;
        Quaternion loweredRotation;
        // How far up from lowered the deck is, degrees, and how fast that's changing.
        float angle, angularSpeed;

        /// <summary>True once the buttons have sent it down (and while they keep it down).</summary>
        public bool IsLowered { get; private set; }
        /// <summary>0 raised, 1 lying flat as a bridge.</summary>
        public float Progress => raisedAngle > 0f ? 1f - angle / raisedAngle : 1f;

        void Reset() => blockingMask = LayerMask.GetMask("PlayerLiquid", "PlayerSolid", "Ship", "Pushable");

        void Awake()
        {
            if (deck == null) deck = GetComponentInChildren<Rigidbody>();
            deck.isKinematic = true;
            deck.interpolation = RigidbodyInterpolation.Interpolate;
            shape = deck.GetComponentInChildren<BoxCollider>();
            loweredRotation = deck.transform.localRotation;
            if (shape != null)
            {
                Quaternion unturn = Quaternion.Inverse(deck.transform.rotation);
                shapeOffset = unturn * (shape.transform.TransformPoint(shape.center) - deck.transform.position);
                shapeTurn = unturn * shape.transform.rotation;
                // A touch smaller, so bodies merely touching the deck don't count.
                shapeHalfExtents = Vector3.Scale(shape.size, shape.transform.lossyScale) * 0.475f;
            }

            angle = raisedAngle;
            deck.transform.localRotation = Turned(angle);
        }

        void FixedUpdate()
        {
            bool wanted = (staysDown && IsLowered) || ButtonsSatisfied();
            if (wanted != IsLowered)
            {
                IsLowered = wanted;
                GameSounds.Doors.PlayAt(deck.position);
            }

            float dt = Time.fixedDeltaTime;
            float next = angle;
            if (IsLowered)
            {
                // Falls: speeds up toward the floor, then bounces off it a little smaller each time until it lies still.
                if (angle > 0f || angularSpeed > 0f)
                {
                    angularSpeed = Mathf.Max(angularSpeed - fallAcceleration * dt, -maxFallSpeed);
                    next = angle + angularSpeed * dt;
                    if (next <= 0f)
                    {
                        next = 0f;
                        angularSpeed = -angularSpeed * bounce;
                        if (angularSpeed < SettleSpeed) angularSpeed = 0f;
                    }
                }
            }
            else
            {
                angularSpeed = 0f;
                next = Mathf.MoveTowards(angle, raisedAngle, raiseSpeed * dt);
            }

            if (Mathf.Approximately(next, angle)) return;
            if (IsBlocked(next))
            {
                angularSpeed = 0f;
                return;
            }
            angle = next;
            deck.MoveRotation(transform.rotation * Turned(angle));
        }

        Quaternion Turned(float degrees) => Quaternion.Euler(degrees, 0f, 0f) * loweredRotation;

        /// <summary>Would the deck, turned to <paramref name="next"/>, sit in the blob, the ship or a crate?</summary>
        bool IsBlocked(float next)
        {
            if (shape == null || blockingMask.value == 0) return false;

            Quaternion turn = transform.rotation * Turned(next);
            return Physics.CheckBox(deck.position + turn * shapeOffset, shapeHalfExtents, turn * shapeTurn, blockingMask,
                                    QueryTriggerInteraction.Ignore);
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
            if (deck == null) return;
            // The hinge line.
            Gizmos.color = Color.cyan;
            Transform hinge = deck.transform;
            Gizmos.DrawWireSphere(hinge.position, 0.15f);
            Gizmos.DrawLine(hinge.position - hinge.right, hinge.position + hinge.right);
        }
    }
}
