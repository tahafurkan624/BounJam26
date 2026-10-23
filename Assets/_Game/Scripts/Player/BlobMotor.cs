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
        [Tooltip("Right after leaving the ground, ground within this distance below pulls the blob back down " +
                 "instead of letting it launch off bumps and crests, m. Larger drops still fall.")]
        [SerializeField] float snapDistance = 0.6f;
        [Tooltip("Above this speed the blob may leave the ground over crests, m/s.")]
        [SerializeField] float snapMaxSpeed = 9f;

        [Header("Steps")]
        [Tooltip("Edges up to this height above the bottom of the core are flowed up, m.")]
        [SerializeField] float maxStepHeight = 0.25f;
        [Tooltip("Upward speed while flowing up a step, m/s.")]
        [SerializeField] float stepClimbSpeed = 2.5f;

        // Below this gap the core counts as touching the ground rather than skimming over it.
        const float ContactGap = 0.02f;
        // Physics steps after a step climb during which hugging and snapping stay off, so they don't undo the lift.
        const int ClimbGraceSteps = 4;

        Rigidbody body;
        SphereCollider core;
        int stepsSinceGrounded;
        int stepsSinceClimb = ClimbGraceSteps;
        bool stepContact;
        Vector3 stepNormal;
        float groundGap;

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
            stepsSinceGrounded = IsGrounded ? 0 : stepsSinceGrounded + 1;
            stepsSinceClimb++;
            bool climbing = stepsSinceClimb < ClimbGraceSteps;
            if (!climbing)
            {
                if (!IsGrounded && stepsSinceGrounded <= 1) SnapToGround();
                else if (IsGrounded && groundGap > ContactGap) HugGround();
            }

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

            // Pressing into a low edge: flow up it instead of relying on a bounce off the corner.
            if (stepContact && wish.sqrMagnitude > 0.01f && Vector3.Dot(wish.normalized, -stepNormal) > 0.3f)
            {
                Vector3 velocity = body.linearVelocity;
                if (velocity.y < stepClimbSpeed) body.linearVelocity = new Vector3(velocity.x, stepClimbSpeed, velocity.z);
                stepsSinceClimb = 0;
            }
            stepContact = false;
        }

        void OnCollisionStay(Collision collision)
        {
            if ((groundMask.value & (1 << collision.gameObject.layer)) == 0) return;

            float bottom = transform.TransformPoint(core.center).y - CoreRadius;
            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                float height = contact.point.y - bottom;
                // A steep surface touched low on the core is a step edge; touched higher up it is a wall.
                if (contact.normal.y < 0.7f && height > ContactGap && height < maxStepHeight)
                {
                    stepContact = true;
                    stepNormal = Vector3.ProjectOnPlane(contact.normal, Vector3.up).normalized;
                }
            }
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
            groundGap = IsGrounded ? hit.distance - (radius - castRadius) : float.PositiveInfinity;
        }

        /// <summary>Skimming just above the ground: turn any velocity away from it along the surface, keeping speed.</summary>
        void HugGround()
        {
            Vector3 velocity = body.linearVelocity;
            float away = Vector3.Dot(velocity, GroundNormal);
            if (away > 0f) body.linearVelocity = (velocity - GroundNormal * away).normalized * velocity.magnitude;
        }

        void SnapToGround()
        {
            Vector3 velocity = body.linearVelocity;
            float speed = velocity.magnitude;
            if (speed > snapMaxSpeed) return;

            float castRadius = CoreRadius * 0.9f;
            Vector3 origin = transform.TransformPoint(core.center);
            if (!Physics.SphereCast(origin, castRadius, Vector3.down, out RaycastHit hit, snapDistance, groundMask, QueryTriggerInteraction.Ignore)
                || Vector3.Angle(hit.normal, Vector3.up) > maxGroundAngle)
                return;

            IsGrounded = true;
            GroundNormal = hit.normal;
            stepsSinceGrounded = 0;
            groundGap = hit.distance - (CoreRadius - castRadius);
            // Keep the speed but turn it along the surface, so the blob flows over the crest.
            float away = Vector3.Dot(velocity, hit.normal);
            if (away > 0f) body.linearVelocity = (velocity - hit.normal * away).normalized * speed;
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
