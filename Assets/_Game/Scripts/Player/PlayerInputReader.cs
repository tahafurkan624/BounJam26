using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BounJam
{
    /// <summary>
    /// Reads the Player map of the project-wide input actions and exposes it to the blob's systems.
    /// </summary>
    public class PlayerInputReader : MonoBehaviour
    {
        InputAction move;
        InputAction toggleForm;

        /// <summary>When set, replaces the Move input (editor test tools, scripted sequences).</summary>
        public Vector2? MoveOverride { get; set; }

        /// <summary>Movement stick or keys, clamped to length 1.</summary>
        public Vector2 Move => Vector2.ClampMagnitude(MoveOverride ?? (move != null ? move.ReadValue<Vector2>() : Vector2.zero), 1f);

        public event Action ToggleFormPressed;

        void OnEnable()
        {
            InputActionAsset actions = InputSystem.actions;
            if (actions == null)
            {
                Debug.LogError("[BounJam] No project-wide input actions are assigned (Project Settings > Input System Package).", this);
                return;
            }

            move = actions.FindAction("Player/Move", throwIfNotFound: true);
            toggleForm = actions.FindAction("Player/ToggleForm", throwIfNotFound: true);
            toggleForm.performed += OnToggleForm;
            actions.FindActionMap("Player", throwIfNotFound: true).Enable();
        }

        void OnDisable()
        {
            if (toggleForm != null) toggleForm.performed -= OnToggleForm;
        }

        void OnToggleForm(InputAction.CallbackContext _) => ToggleFormPressed?.Invoke();
    }
}
