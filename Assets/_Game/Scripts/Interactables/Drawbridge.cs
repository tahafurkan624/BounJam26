using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// A ramp hinged at its top edge that stands raised, closing the way, until its buttons are pressed; then it falls
    /// down onto the floor below and stays there as a bridge. The deck is a kinematic body turned with MoveRotation
    /// round the hinge, so it gathers speed as it falls, bounces a little where it lands, and shoves nothing on the way:
    /// if the blob or a crate is under it, it waits until they move off. The ship is the exception: a deck that would
    /// pin it, or catch it rising, lifts it straight up onto its top instead, so the bridge never hangs on the ship
    /// (unless there's no room above it, when it waits like for anything else). With Stays Down off it rises again
    /// when the buttons are released (and waits, too, while anything but the ship is standing on it).
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
        [Tooltip("The highest it will lift the ship out of its way in one step, m; past that it waits for the ship instead.")]
        [SerializeField] float maxShipLift = 3f;

        // A bounce slower than this ends on the floor, degrees/s.
        const float SettleSpeed = 4f;
        // How finely it searches upward for room for the ship, m, and the gap it leaves under the hull.
        const float LiftStep = 0.02f;
        const float LiftClearance = 0.01f;

        static readonly Collider[] overlaps = new Collider[16];
        readonly List<ShipBuoyancy> shipsInTheWay = new List<ShipBuoyancy>();

        BoxCollider shape;
        // The deck's box in the deck's own frame: its centre and its transform's pivot from the hinge, its turn and half size.
        Vector3 shapeOffset, shapePivotOffset, shapeHalfExtents;
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
                shapePivotOffset = unturn * (shape.transform.position - deck.transform.position);
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

        /// <summary>
        /// Would the deck, turned to <paramref name="next"/>, sit in the blob, the ship or a crate? A ship in the way is
        /// lifted up out of it onto the deck's top when there's room for it there, and then doesn't count.
        /// </summary>
        bool IsBlocked(float next)
        {
            if (shape == null || blockingMask.value == 0) return false;

            Quaternion turn = transform.rotation * Turned(next);
            int count = Physics.OverlapBoxNonAlloc(deck.position + turn * shapeOffset, shapeHalfExtents, overlaps,
                                                   turn * shapeTurn, blockingMask, QueryTriggerInteraction.Ignore);
            if (count == 0) return false;

            // Anything but the ship holds the deck where it is; the ship is only lifted once nothing else is in the way.
            shipsInTheWay.Clear();
            for (int i = 0; i < count; i++)
            {
                Rigidbody body = overlaps[i].attachedRigidbody;
                ShipBuoyancy ship = body != null && !body.isKinematic ? body.GetComponent<ShipBuoyancy>() : null;
                if (ship == null) return true;
                if (!shipsInTheWay.Contains(ship)) shipsInTheWay.Add(ship);
            }

            Vector3 deckPosition = deck.position + turn * shapePivotOffset;
            Quaternion deckRotation = turn * shapeTurn;
            foreach (ShipBuoyancy ship in shipsInTheWay)
            {
                Collider[] hull = ship.GetComponentsInChildren<Collider>();
                // The hull's transforms may still be at their interpolated pose; the body is where physics has it.
                Vector3 drift = ship.Body.position - ship.transform.position;
                if (!TryFindLift(hull, drift, deckPosition, deckRotation, out float lift)) return true;
                LiftShip(ship.Body, lift);
            }
            return false;
        }

        /// <summary>
        /// How far straight up the ship has to go to clear the deck at the given pose, without running into anything
        /// else it collides with there. False when no such height is within reach. <paramref name="drift"/> is how far
        /// the hull's transforms are from where its body really is.
        /// </summary>
        bool TryFindLift(Collider[] hull, Vector3 drift, Vector3 deckPosition, Quaternion deckRotation, out float lift)
        {
            for (lift = 0f; lift <= maxShipLift; lift += LiftStep)
                if (!HullOverlapsDeck(hull, drift + Vector3.up * lift, deckPosition, deckRotation)) break;
            if (lift > maxShipLift) return false;

            lift += LiftClearance;
            return !HullOverlapsSurroundings(hull, drift + Vector3.up * lift);
        }

        bool HullOverlapsDeck(Collider[] hull, Vector3 raise, Vector3 deckPosition, Quaternion deckRotation)
        {
            foreach (Collider part in hull)
            {
                if (part.isTrigger || !part.enabled) continue;
                if (Physics.ComputePenetration(part, part.transform.position + raise, part.transform.rotation,
                                               shape, deckPosition, deckRotation, out _, out _))
                    return true;
            }
            return false;
        }

        /// <summary>Would the hull, raised by <paramref name="raise"/>, sit in a wall, a ceiling or anything else solid?</summary>
        bool HullOverlapsSurroundings(Collider[] hull, Vector3 raise)
        {
            foreach (Collider part in hull)
            {
                if (part.isTrigger || !part.enabled) continue;
                Bounds bounds = part.bounds;
                // The liquid gives way to the hull; the ship floats on it anyway.
                int count = Physics.OverlapBoxNonAlloc(bounds.center + raise, bounds.extents, overlaps, Quaternion.identity,
                                                       ~LayerMask.GetMask("PlayerLiquid"), QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    Collider other = overlaps[i];
                    // The deck is checked at its next pose already.
                    if (other.attachedRigidbody == deck || other.attachedRigidbody == part.attachedRigidbody) continue;
                    if (Physics.GetIgnoreLayerCollision(part.gameObject.layer, other.gameObject.layer)) continue;
                    if (Physics.ComputePenetration(part, part.transform.position + raise, part.transform.rotation,
                                                   other, other.transform.position, other.transform.rotation, out _, out _))
                        return true;
                }
            }
            return false;
        }

        /// <summary>Sets the ship <paramref name="lift"/> higher, onto the deck, and stops it falling back into it.</summary>
        static void LiftShip(Rigidbody ship, float lift)
        {
            Vector3 raise = Vector3.up * lift;
            ship.position += raise;
            ship.transform.position += raise;
            Vector3 velocity = ship.linearVelocity;
            if (velocity.y < 0f)
            {
                velocity.y = 0f;
                ship.linearVelocity = velocity;
            }
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
