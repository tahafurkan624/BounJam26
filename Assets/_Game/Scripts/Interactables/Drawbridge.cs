using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// A ramp that starts as a small stub at its foot and, once its buttons are pressed, grows up its slope to full
    /// length and stays there as a bridge. The deck is a kinematic body pivoted at the ramp's foot with its Z running up
    /// the slope; only that Z scale changes, springing out with a small overshoot. It shoves nothing on the way: if the
    /// blob, the ship or a crate is in its path, it waits until they move off. With Stays Out off it shrinks back when
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

        // Spring steps no longer than this, so the spring stays stable at low frame rates.
        const float MaxSpringStep = 1f / 240f;

        BoxCollider shape;
        Vector3 fullScale;
        // The deck's box at full length: its centre in the deck's frame, and its half size (a touch smaller, so
        // bodies merely touching it don't count). The slab lies square in the deck's frame, so both stretch along Z.
        Vector3 shapeCenter, shapeHalfExtents;
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

        /// <summary>Would the deck, grown to <paramref name="next"/>, sit in the blob, the ship or a crate?</summary>
        bool IsBlocked(float next)
        {
            if (shape == null || blockingMask.value == 0) return false;

            Transform foot = deck.transform;
            // The deck's frame is scaled to the current length, so stretch the full-length centre by next / length.
            Vector3 center = foot.TransformPoint(new Vector3(shapeCenter.x, shapeCenter.y, shapeCenter.z * next / length));
            Vector3 halfExtents = new Vector3(shapeHalfExtents.x, shapeHalfExtents.y, shapeHalfExtents.z * next);
            return Physics.CheckBox(center, halfExtents, shape.transform.rotation, blockingMask, QueryTriggerInteraction.Ignore);
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
