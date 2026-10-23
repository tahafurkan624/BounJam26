using System;
using System.Collections.Generic;
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
        InputAction cycleLiquid;
        readonly List<InputAction> liquidSlots = new();

        public Vector2 Move => enabled ? move.ReadValue<Vector2>() : default;
        public Vector2 Look => enabled ? look.ReadValue<Vector2>() : default;
        /// <summary>True when Look comes from a mouse, whose deltas are already per frame; sticks give a rate.</summary>
        public bool LookIsMouseDelta => look.activeControl?.device is Pointer;
        public bool JumpPressed => enabled && jump.WasPressedThisFrame();
        public bool FirePressed => enabled && fire.WasPressedThisFrame();
        public bool FireHeld => enabled && fire.IsPressed();

        /// <summary>-1, 0 or +1: which way the scroll wheel or shoulder buttons cycle the liquid this frame.</summary>
        public int CycleLiquid =>
            enabled && cycleLiquid.WasPressedThisFrame() ? Math.Sign(cycleLiquid.ReadValue<float>()) : 0;

        /// <summary>Zero-based index of the liquid slot key (1, 2, 3...) pressed this frame, or -1.</summary>
        public int LiquidSlotPressed
        {
            get
            {
                if (!enabled)
                    return -1;
                for (int i = 0; i < liquidSlots.Count; i++)
                    if (liquidSlots[i].WasPressedThisFrame())
                        return i;
                return -1;
            }
        }

        void Awake()
        {
            InputActionMap map = InputSystem.actions.FindActionMap("Player", throwIfNotFound: true);
            move = map.FindAction("Move", throwIfNotFound: true);
            look = map.FindAction("Look", throwIfNotFound: true);
            jump = map.FindAction("Jump", throwIfNotFound: true);
            fire = map.FindAction("Fire", throwIfNotFound: true);
            cycleLiquid = map.FindAction("CycleLiquid", throwIfNotFound: true);
            // LiquidSlot1, LiquidSlot2, ... as many as the input asset defines.
            for (InputAction slot; (slot = map.FindAction($"LiquidSlot{liquidSlots.Count + 1}")) != null;)
                liquidSlots.Add(slot);
        }
    }
}
