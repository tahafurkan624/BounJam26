using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Lets active magnets pull the crystal while it holds iron. The nearest active magnet whose field reaches the
    /// blob takes over: the motor lets go, the field carries the blob's weight and draws it in a straight line to the
    /// magnet's hold point, slowing down as it arrives, and keeps it there, tumbling settled, until that magnet is
    /// switched off. Melting drops the iron, so it also ends the pull. Runs before the motor, so the motor knows in the
    /// same physics step.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(BlobMotor), typeof(BlobCargo))]
    public class BlobMagnetism : MonoBehaviour
    {
        [Tooltip("How quickly a crystal stops tumbling while a magnet has it, rad/s².")]
        [SerializeField] float spinDamping = 15f;

        BlobMotor motor;
        BlobCargo cargo;
        Rigidbody body;

        /// <summary>The magnet pulling or holding the blob right now, if any.</summary>
        public Magnet Current { get; private set; }

        void Awake()
        {
            motor = GetComponent<BlobMotor>();
            cargo = GetComponent<BlobCargo>();
            body = GetComponent<Rigidbody>();
        }

        void OnDisable() => Release();

        void FixedUpdate()
        {
            Magnet magnet = cargo.Has<IronBall>() ? Choose() : null;
            if (magnet != Current)
            {
                Release();
                Current = magnet;
                if (magnet != null) magnet.Pulling = transform;
            }
            motor.IsDriven = magnet != null;
            if (magnet != null) Pull(magnet);
        }

        Magnet Choose()
        {
            // A magnet that has the blob keeps it for as long as it's on.
            if (Current != null && Current.IsActive) return Current;

            Magnet best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (Magnet magnet in Magnet.All)
            {
                if (!magnet.IsActive || !magnet.Reaches(body.position)) continue;
                float distance = Vector3.Distance(magnet.transform.position, body.position);
                if (distance >= bestDistance) continue;
                best = magnet;
                bestDistance = distance;
            }
            return best;
        }

        void Pull(Magnet magnet)
        {
            float dt = Time.fixedDeltaTime;
            Vector3 toHold = magnet.HoldPoint - body.position;
            float distance = toHold.magnitude;
            // Full speed until close, then slower the closer it gets, so it settles on the hold point.
            float speed = magnet.PullSpeed * Mathf.Clamp01(distance / magnet.ArriveDistance);
            Vector3 wanted = distance > 1e-4f ? toHold * (speed / distance) : Vector3.zero;
            Vector3 velocity = body.linearVelocity;
            body.AddForce(Vector3.MoveTowards(velocity, wanted, magnet.PullAcceleration * dt) - velocity, ForceMode.VelocityChange);
            if (body.useGravity) body.AddForce(-Physics.gravity, ForceMode.Acceleration);
            body.angularVelocity = Vector3.MoveTowards(body.angularVelocity, Vector3.zero, spinDamping * dt);
        }

        void Release()
        {
            if (Current != null && Current.Pulling == transform) Current.Pulling = null;
            Current = null;
            if (motor != null) motor.IsDriven = false;
        }
    }
}
