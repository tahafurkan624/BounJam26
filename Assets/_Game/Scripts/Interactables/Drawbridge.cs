using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// A ramp that starts as a small stub at its foot and, once its buttons are pressed, grows up its slope to full
    /// length and stays there as a bridge. The deck is a kinematic body pivoted at the ramp's foot with its Z running up
    /// the slope; only that Z scale changes, springing out with a small overshoot. It shoves nothing on the way: if the
    /// blob or a crate is in its path, it waits until they move off. The ship is the exception: one in its path is
    /// lifted straight up onto the deck, so the bridge never hangs on it (unless there's no room above it, when it
    /// waits like for anything else). With Stays Out off it shrinks back when
    /// the buttons are released. Growing and shrinking each play event:/Doors once. A deck lying flat works the same
    /// way: the Grate Bridge grows one across a grate.
    /// </summary>
    public class Drawbridge : MonoBehaviour
    {
        [SerializeField] PressureButton[] buttons;
        [Tooltip("Needs every button pressed, instead of any one of them.")]
        [SerializeField] bool requireAll;
        [Tooltip("Once grown out, stays out for good.")]
        [SerializeField] bool staysOut = true;

        [Tooltip("The deck: a child kinematic body pivoted at the ramp's foot, its Z up the slope, at full length.")]
        [SerializeField] Rigidbody deck;
        [Tooltip("How long the deck is before it grows, as a share of its full length.")]
        [SerializeField, Range(0.01f, 1f)] float startLength = 0.05f;

        [Header("Motion")]
        [Tooltip("How fast it springs out to full length, Hz.")]
        [SerializeField] float springFrequency = 1.2f;
        [Tooltip("Below 1 it overshoots a little and settles back; 1 settles without overshoot.")]
        [SerializeField, Range(0.1f, 1f)] float springDamping = 0.7f;

        [Header("Safety")]
        [Tooltip("Bodies it won't push; it waits for them to move out of the way.")]
        [SerializeField] LayerMask blockingMask;
        [Tooltip("The highest it will lift the ship onto the deck in one step, m; past that it waits for the ship instead.")]
        [SerializeField] float maxShipLift = 3f;

        // Spring steps no longer than this, so the spring stays stable at low frame rates.
        const float MaxSpringStep = 1f / 240f;
        // How finely it searches upward for room for the ship, m, and the gap it leaves under the hull.
        const float LiftStep = 0.02f;
        const float LiftClearance = 0.01f;

        static readonly Collider[] overlaps = new Collider[16];
        readonly List<ShipBuoyancy> shipsInTheWay = new List<ShipBuoyancy>();

        BoxCollider shape;
        Vector3 fullScale;
        // The deck's box at full length: its centre in the deck's frame, and its half size (a touch smaller, so
        // bodies merely touching it don't count), and its true half size. The slab lies square in the deck's frame,
        // so all of them stretch along Z.
        Vector3 shapeCenter, shapeHalfExtents, shapeFullHalfExtents;
        // How long the deck is now, as a share of full length, and how fast that's changing.
        float length = 1f, lengthSpeed;

        /// <summary>True once the buttons have sent it out (and while they keep it out).</summary>
        public bool IsOut { get; private set; }
        /// <summary>0 as a stub, 1 at full length.</summary>
        public float Progress => Mathf.InverseLerp(startLength, 1f, length);

        void Reset() => blockingMask = LayerMask.GetMask("PlayerLiquid", "PlayerSolid", "Ship", "Pushable");

        void Awake()
        {
            if (deck == null) deck = GetComponentInChildren<Rigidbody>();
            deck.isKinematic = true;
            deck.interpolation = RigidbodyInterpolation.Interpolate;
            fullScale = deck.transform.localScale;
            shape = deck.GetComponentInChildren<BoxCollider>();
            if (shape != null)
            {
                shapeCenter = deck.transform.InverseTransformPoint(shape.transform.TransformPoint(shape.center));
                shapeHalfExtents = Vector3.Scale(shape.size, shape.transform.lossyScale) * 0.475f;
                shapeFullHalfExtents = Vector3.Scale(shape.size, shape.transform.lossyScale) * 0.5f;
            }

            length = startLength;
            ApplyLength();
        }

        void FixedUpdate()
        {
            bool wanted = (staysOut && IsOut) || ButtonsSatisfied();
            if (wanted != IsOut)
            {
                IsOut = wanted;
                GameSounds.Doors.PlayAt(deck.position);
            }

            float target = IsOut ? 1f : startLength;
            float omega = 2f * Mathf.PI * springFrequency;
            float next = length, speed = lengthSpeed;
            for (float left = Time.fixedDeltaTime; left > 0f; left -= MaxSpringStep)
            {
                float dt = Mathf.Min(left, MaxSpringStep);
                speed += (-(next - target) * omega * omega - 2f * springDamping * omega * speed) * dt;
                next += speed * dt;
            }
            // Never shorter than the stub, so shrinking back doesn't bounce through it.
            if (next < startLength) { next = startLength; speed = 0f; }

            if (Mathf.Abs(next - length) < 1e-5f && Mathf.Abs(speed) < 1e-4f) return;
            if (next > length && IsBlocked(next))
            {
                lengthSpeed = 0f;
                return;
            }
            length = next;
            lengthSpeed = speed;
            ApplyLength();
        }

        void ApplyLength() => deck.transform.localScale = new Vector3(fullScale.x, fullScale.y, fullScale.z * length);

        /// <summary>
        /// Would the deck, grown to <paramref name="next"/>, sit in the blob, the ship or a crate? A ship in the way is
        /// lifted up out of it onto the deck's top when there's room for it there, and then doesn't count.
        /// </summary>
        bool IsBlocked(float next)
        {
            if (shape == null || blockingMask.value == 0) return false;

            Transform foot = deck.transform;
            // The deck's frame is scaled to the current length, so stretch the full-length centre by next / length.
            Vector3 center = foot.TransformPoint(new Vector3(shapeCenter.x, shapeCenter.y, shapeCenter.z * next / length));
            Vector3 halfExtents = new Vector3(shapeHalfExtents.x, shapeHalfExtents.y, shapeHalfExtents.z * next);
            Quaternion rotation = shape.transform.rotation;
            int count = Physics.OverlapBoxNonAlloc(center, halfExtents, overlaps, rotation, blockingMask,
                                                   QueryTriggerInteraction.Ignore);
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

            Vector3 fullHalfExtents = new Vector3(shapeFullHalfExtents.x, shapeFullHalfExtents.y, shapeFullHalfExtents.z * next);
            foreach (ShipBuoyancy ship in shipsInTheWay)
            {
                if (!TryFindLift(ship, center, fullHalfExtents, rotation, out float lift)) return true;
                LiftShip(ship.Body, lift);
            }
            return false;
        }

        /// <summary>
        /// How far straight up the ship has to go to clear the deck's box, without running into anything else it
        /// collides with there. False when no such height is within reach.
        /// </summary>
        bool TryFindLift(ShipBuoyancy ship, Vector3 center, Vector3 halfExtents, Quaternion rotation, out float lift)
        {
            // Raising the ship through the box is the same as lowering the box through the ship.
            int shipLayers = 1 << ship.gameObject.layer;
            for (lift = 0f; lift <= maxShipLift; lift += LiftStep)
                if (!BoxTouches(ship.Body, center - Vector3.up * lift, halfExtents, rotation, shipLayers)) break;
            if (lift > maxShipLift) return false;

            lift += LiftClearance;
            return !HullOverlapsSurroundings(ship, Vector3.up * lift);
        }

        static bool BoxTouches(Rigidbody body, Vector3 center, Vector3 halfExtents, Quaternion rotation, int layers)
        {
            int count = Physics.OverlapBoxNonAlloc(center, halfExtents, overlaps, rotation, layers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (overlaps[i].attachedRigidbody == body) return true;
            return false;
        }

        /// <summary>Would the hull, raised by <paramref name="raise"/>, sit in a wall, a ceiling or anything else solid?</summary>
        bool HullOverlapsSurroundings(ShipBuoyancy ship, Vector3 raise)
        {
            // The hull's transforms may still be at their interpolated pose; the body is where physics has it.
            Vector3 drift = ship.Body.position - ship.transform.position;
            foreach (Collider part in ship.GetComponentsInChildren<Collider>())
            {
                if (part.isTrigger || !part.enabled) continue;
                Vector3 position = part.transform.position + drift + raise;
                Bounds bounds = part.bounds;
                // The liquid gives way to the hull; the ship floats on it anyway.
                int count = Physics.OverlapBoxNonAlloc(bounds.center + raise, bounds.extents, overlaps, Quaternion.identity,
                                                       ~LayerMask.GetMask("PlayerLiquid"), QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    Collider other = overlaps[i];
                    // The deck's box was checked at its next length already.
                    if (other.attachedRigidbody == deck || other.attachedRigidbody == ship.Body) continue;
                    if (Physics.GetIgnoreLayerCollision(part.gameObject.layer, other.gameObject.layer)) continue;
                    if (Physics.ComputePenetration(part, position, part.transform.rotation,
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
            // The foot it grows from, and the way it grows.
            Gizmos.color = Color.cyan;
            Transform foot = deck.transform;
            Gizmos.DrawWireSphere(foot.position, 0.15f);
            Gizmos.DrawLine(foot.position, foot.position + foot.forward);
        }
    }
}
