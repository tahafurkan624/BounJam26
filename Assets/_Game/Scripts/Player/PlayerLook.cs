using UnityEngine;

namespace BounJam.Player
{
    /// <summary>
    /// Turns the camera pivot with mouse or right-stick input. The Cinemachine third-person camera copies
    /// the pivot's rotation, so this decides where the camera looks. Also keeps the cursor locked.
    /// </summary>
    [DefaultExecutionOrder(-10)] // Update runs before PlayerMotor reads PlanarRotation, LateUpdate before Cinemachine
    public class PlayerLook : MonoBehaviour
    {
        [SerializeField] Transform pivot;
        [SerializeField, Tooltip("Degrees turned per pixel of mouse movement.")]
        float mouseSensitivity = 0.1f;
        [SerializeField, Tooltip("Degrees per second at full stick tilt.")]
        float stickSensitivity = 200f;
        [SerializeField] bool invertY;
        [SerializeField] float minPitch = -40f;
        [SerializeField] float maxPitch = 70f;

        BeaverInput input;
        float yaw;
        float pitch = 10f;
        int framesToIgnoreLook;

        /// <summary>The camera's heading without its pitch; movement input is relative to this.</summary>
        public Quaternion PlanarRotation => Quaternion.Euler(0f, yaw, 0f);

        void Awake()
        {
            input = GetComponent<BeaverInput>();
            yaw = transform.eulerAngles.y;
        }

        void OnEnable() => LockCursor();

        void OnDisable() => Cursor.lockState = CursorLockMode.None;

        void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            // Locking warps the pointer to the screen centre, which can arrive as one huge mouse delta.
            framesToIgnoreLook = 3;
        }

        void Update()
        {
            // Esc frees the cursor in the editor; clicking back into the game captures it again.
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                if (input.FirePressed)
                    LockCursor();
                return;
            }
            if (framesToIgnoreLook > 0)
            {
                framesToIgnoreLook--;
                return;
            }

            Vector2 look = input.Look;
            float scale = input.LookIsMouseDelta ? mouseSensitivity : stickSensitivity * Time.deltaTime;
            yaw += look.x * scale;
            pitch = Mathf.Clamp(pitch + (invertY ? look.y : -look.y) * scale, minPitch, maxPitch);
        }

        // The pivot is a child of the beaver, so it is re-aimed after PlayerMotor has turned the body.
        void LateUpdate() => pivot.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }
}
