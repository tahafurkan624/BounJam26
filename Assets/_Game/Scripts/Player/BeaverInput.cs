using UnityEngine;
using UnityEngine.InputSystem;

namespace BounJam.Player
{
    /// <summary>
    /// Reads the "Player" map of the project-wide input actions (BeaverControls). Disable this
    /// component to freeze the player's input, for example when the level ends.
    /// </summary>
    public class BeaverInput : MonoBehaviour
    {
        InputAction move;
        InputAction look;
        InputAction jump;
        InputAction fire;

        public Vector2 Move => enabled ? move.ReadValue<Vector2>() : default;
        public Vector2 Look => enabled ? look.ReadValue<Vector2>() : default;
        /// <summary>True when Look comes from a mouse, whose deltas are already per frame; sticks give a rate.</summary>
        public bool LookIsMouseDelta => look.activeControl?.device is Pointer;
        public bool JumpPressed => enabled && jump.WasPressedThisFrame();
        public bool FirePressed => enabled && fire.WasPressedThisFrame();

        void Awake()
        {
            InputActionMap map = InputSystem.actions.FindActionMap("Player", throwIfNotFound: true);
            move = map.FindAction("Move", throwIfNotFound: true);
            look = map.FindAction("Look", throwIfNotFound: true);
            jump = map.FindAction("Jump", throwIfNotFound: true);
            fire = map.FindAction("Fire", throwIfNotFound: true);
        }
    }
}
