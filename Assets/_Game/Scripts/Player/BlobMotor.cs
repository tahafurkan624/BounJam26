using System;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Moves the blob's core rigidbody. Input is camera-relative on the ground plane, and the body is
    /// steered toward a target velocity (accelerate while there is input, brake when there isn't),
    /// so it reacts quickly but still carries momentum. The core never rotates; the skin does the visuals.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public class BlobMotor : MonoBehaviour
    {
        [Serializable]
        public struct MoveSettings
        {
            [Tooltip("Top speed on flat ground, m/s.")]
            public float maxSpeed;
            [Tooltip("How quickly it reaches top speed while there is input, m/s².")]
            public float acceleration;
            [Tooltip("How quickly it stops once input is released, m/s².")]
            public float braking;
        }

        [SerializeField] PlayerInputReader input;
        [Tooltip("Movement is relative to this camera's facing. Defaults to the main camera.")]
        [SerializeField] Transform cameraTransform;
        [SerializeField] MoveSettings liquid = new MoveSettings { maxSpeed = 7f, acceleration = 35f, braking = 25f };
        [Tooltip("Fraction of acceleration and braking available while airborne.")]
        [Range(0f, 1f)]
        [SerializeField] float airControl = 0.3f;

        [Header("Ground")]
        [SerializeField] LayerMask groundMask = ~0;
        [Tooltip("How far below the core the ground is still detected, m.")]
        [SerializeField] float groundProbeDistance = 0.2f;
        [SerializeField] float maxGroundAngle = 50f;
        [Tooltip("Extra pull into the ground while grounded, keeps the blob hugging ramps and crests, m/s².")]
        [SerializeField] float groundStick = 15f;

        Rigidbody body;
        SphereCollider core;

        public Rigidbody Body => body;
        public bool IsGrounded { get; private set; }
        public Vector3 GroundNormal { get; private set; } = Vector3.up;
        public Vector3 Velocity => body.linearVelocity;
        public MoveSettings ActiveSettings => liquid;
        /// <summary>Speed along the ground relative to the current top speed; 0 at rest, about 1 at full speed.</summary>
        public float NormalizedSpeed => Vector3.ProjectOnPlane(body.linearVelocity, GroundNormal).magnitude / Mathf.Max(0.01f, ActiveSettings.maxSpeed);
        /// <summary>World radius of the core collider.</summary>
        public float CoreRadius => core.radius * MaxAbsComponent(transform.lossyScale);

        void Reset()
        {
            input = GetComponent<PlayerInputReader>();
            groundMask = ~LayerMask.GetMask("PlayerLiquid", "PlayerSolid", "Triggers", "Ignore Raycast");
        }

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            core = GetComponent<SphereCollider>();
            if (input == null) input = GetComponent<PlayerInputReader>();
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        }

        void FixedUpdate()
        {
            ProbeGround();

            Vector3 up = IsGrounded ? GroundNormal : Vector3.up;
            Vector3 wish = WishDirection();
            if (IsGrounded && wish.sqrMagnitude > 0f)
                wish = Vector3.ProjectOnPlane(wish, up).normalized * wish.magnitude;

            MoveSettings settings = ActiveSettings;
            float rate = wish.sqrMagnitude > 0.0001f ? settings.acceleration : settings.braking;
            if (!IsGrounded) rate *= airControl;

            Vector3 planar = Vector3.ProjectOnPlane(body.linearVelocity, up);
            Vector3 steered = Vector3.MoveTowards(planar, wish * settings.maxSpeed, rate * Time.fixedDeltaTime);
            body.AddForce(steered - planar, ForceMode.VelocityChange);

            if (IsGrounded) body.AddForce(-GroundNormal * groundStick, ForceMode.Acceleration);
        }

        void ProbeGround()
        {
            float radius = CoreRadius;
            float castRadius = radius * 0.9f;
            Vector3 origin = transform.TransformPoint(core.center);
            float distance = radius - castRadius + groundProbeDistance;

            IsGrounded = Physics.SphereCast(origin, castRadius, Vector3.down, out RaycastHit hit, distance, groundMask, QueryTriggerInteraction.Ignore)
                         && Vector3.Angle(hit.normal, Vector3.up) <= maxGroundAngle;
            GroundNormal = IsGrounded ? hit.normal : Vector3.up;
        }

        Vector3 WishDirection()
        {
            Vector2 move = input != null ? input.Move : Vector2.zero;
            if (move.sqrMagnitude < 0.0001f) return Vector3.zero;

            Vector3 forward = Vector3.forward;
            if (cameraTransform != null)
            {
                forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
                // Looking straight down: the camera's up vector is the screen's forward.
                if (forward.sqrMagnitude < 0.01f) forward = Vector3.ProjectOnPlane(cameraTransform.up, Vector3.up);
                forward.Normalize();
            }
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            return forward * move.y + right * move.x;
        }

        static float MaxAbsComponent(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));
    }
}
