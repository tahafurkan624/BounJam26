using UnityEngine;

namespace BounJam.Player
{
    /// <summary>
    /// Runs, turns and jumps the beaver with a CharacterController. Movement is relative to the camera.
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(BeaverInput), typeof(PlayerLook))]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("Running")]
        [SerializeField] float runSpeed = 5f;
        [SerializeField] float groundAcceleration = 40f;
        [SerializeField] float airAcceleration = 12f;
        [SerializeField, Tooltip("Degrees per second the beaver turns toward where it is going.")]
        float turnSpeed = 720f;

        [Header("Jumping")]
        [SerializeField] float jumpHeight = 1.3f;
        [SerializeField] float gravity = -25f;
        [SerializeField] float maxFallSpeed = 30f;
        [SerializeField, Tooltip("Seconds after running off a ledge during which a jump still works.")]
        float coyoteTime = 0.12f;
        [SerializeField, Tooltip("Seconds a jump press is remembered before landing.")]
        float jumpBuffer = 0.15f;

        CharacterController controller;
        BeaverInput input;
        PlayerLook look;
        Vector3 velocity;
        float lastGroundedTime = float.NegativeInfinity;
        float lastJumpPressTime = float.NegativeInfinity;

        public Vector3 Velocity => velocity;
        public bool IsGrounded => controller.isGrounded;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            input = GetComponent<BeaverInput>();
            look = GetComponent<PlayerLook>();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (controller.isGrounded)
                lastGroundedTime = Time.time;
            if (input.JumpPressed)
                lastJumpPressTime = Time.time;

            Vector2 stick = Vector2.ClampMagnitude(input.Move, 1f);
            Vector3 wishVelocity = look.PlanarRotation * new Vector3(stick.x, 0f, stick.y) * runSpeed;
            float acceleration = controller.isGrounded ? groundAcceleration : airAcceleration;
            Vector3 planar = Vector3.MoveTowards(new Vector3(velocity.x, 0f, velocity.z), wishVelocity, acceleration * dt);

            float vertical = velocity.y;
            if (controller.isGrounded && vertical < 0f)
                vertical = -2f; // keeps the controller pressed onto slopes so isGrounded stays reliable
            if (Time.time - lastJumpPressTime <= jumpBuffer && Time.time - lastGroundedTime <= coyoteTime)
            {
                vertical = Mathf.Sqrt(2f * jumpHeight * -gravity);
                lastJumpPressTime = lastGroundedTime = float.NegativeInfinity;
            }
            vertical = Mathf.Max(vertical + gravity * dt, -maxFallSpeed);

            velocity = new Vector3(planar.x, vertical, planar.z);
            CollisionFlags hits = controller.Move(velocity * dt);
            if ((hits & CollisionFlags.Above) != 0 && velocity.y > 0f)
                velocity.y = 0f;

            if (wishVelocity.sqrMagnitude > 0.01f)
            {
                Quaternion facing = Quaternion.LookRotation(wishVelocity);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, turnSpeed * dt);
            }
        }
    }
}
