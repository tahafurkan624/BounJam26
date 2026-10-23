using Unity.Cinemachine;
using UnityEngine;

namespace BounJam.Player
{
    /// <summary>
    /// Orbits the free-look camera around the beaver with mouse or right-stick input. The orbit is in
    /// world space, so the camera only turns when you look around, never when the beaver turns.
    /// Also keeps the cursor locked.
    /// </summary>
    [DefaultExecutionOrder(-10)] // runs before PlayerMotor reads PlanarRotation
    public class PlayerLook : MonoBehaviour
    {
        [SerializeField] CinemachineOrbitalFollow orbit;
        [SerializeField, Tooltip("Degrees turned per pixel of mouse movement.")]
        float mouseSensitivity = 0.1f;
        [SerializeField, Tooltip("Degrees per second at full stick tilt.")]
        float stickSensitivity = 200f;
        [SerializeField] bool invertY;

        BeaverInput input;
        float yaw;
        float pitch;
        int framesToIgnoreLook;

        /// <summary>The camera's heading without its pitch; movement input is relative to this.</summary>
        public Quaternion PlanarRotation => Quaternion.Euler(0f, yaw, 0f);

        void Awake()
        {
            input = GetComponent<BeaverInput>();
            yaw = transform.eulerAngles.y; // start behind the beaver
            pitch = orbit.VerticalAxis.Value;
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
            }
            else if (framesToIgnoreLook > 0)
            {
                framesToIgnoreLook--;
            }
            else
            {
                Vector2 look = input.Look;
                float scale = input.LookIsMouseDelta ? mouseSensitivity : stickSensitivity * Time.deltaTime;
                yaw = Mathf.Repeat(yaw + look.x * scale + 180f, 360f) - 180f;
                Vector2 pitchRange = orbit.VerticalAxis.Range;
                pitch = Mathf.Clamp(pitch + (invertY ? look.y : -look.y) * scale, pitchRange.x, pitchRange.y);
            }

            orbit.HorizontalAxis.Value = yaw;
            orbit.VerticalAxis.Value = pitch;
        }
    }
}
