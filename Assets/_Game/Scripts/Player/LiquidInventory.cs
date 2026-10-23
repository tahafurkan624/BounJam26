using System;
using System.Collections.Generic;
using BounJam.Liquids;
using UnityEngine;

namespace BounJam.Player
{
    /// <summary>
    /// The liquids the beaver carries and which one is selected. Slot 1 is key 1, and so on; the mouse
    /// wheel cycles through the slots that have something left. Infinite liquids never run out.
    /// </summary>
    [RequireComponent(typeof(BeaverInput))]
    public class LiquidInventory : MonoBehaviour
    {
        [Serializable]
        public class Slot
        {
            public LiquidType liquid;
            [Min(0f), Tooltip("Units carried at the start, clamped to the liquid's capacity.")]
            public float startingAmount;
        }

        [SerializeField] List<Slot> slots = new();

        readonly List<float> amounts = new();
        BeaverInput input;

        public int SlotCount => slots.Count;
        public int CurrentIndex { get; private set; }
        public LiquidType Current => CurrentIndex < slots.Count ? slots[CurrentIndex].liquid : null;
        public bool CanFire => Current != null && IsUsable(CurrentIndex);

        public LiquidType LiquidAt(int index) => slots[index].liquid;
        public bool IsUsable(int index) => slots[index].liquid.IsInfinite || amounts[index] > 0f;
        /// <summary>How full the slot is, from 0 to 1. Infinite liquids are always full.</summary>
        public float FillAt(int index) =>
            slots[index].liquid.IsInfinite ? 1f : amounts[index] / Mathf.Max(0.01f, slots[index].liquid.Capacity);

        void Awake()
        {
            input = GetComponent<BeaverInput>();
            if (slots.RemoveAll(slot => slot.liquid == null) > 0)
                Debug.LogWarning($"{name}: ignoring liquid slots with no liquid assigned.", this);
            foreach (Slot slot in slots)
                amounts.Add(Mathf.Clamp(slot.startingAmount, 0f, slot.liquid.Capacity));
            SelectFallback();
        }

        void Update()
        {
            int pressed = input.LiquidSlotPressed;
            if (pressed >= 0)
                Select(pressed);
            else if (input.CycleLiquid != 0)
                Cycle(input.CycleLiquid);
        }

        /// <summary>Selects a slot if it exists and has liquid left.</summary>
        public bool Select(int index)
        {
            if (index < 0 || index >= slots.Count || !IsUsable(index))
                return false;
            CurrentIndex = index;
            return true;
        }

        /// <summary>Moves the selection to the next (+1) or previous (-1) slot that has liquid left.</summary>
        public void Cycle(int direction)
        {
            for (int step = 1; step < slots.Count; step++)
            {
                int index = ((CurrentIndex + direction * step) % slots.Count + slots.Count) % slots.Count;
                if (Select(index))
                    return;
            }
        }

        /// <summary>Uses up units of the selected liquid, switching back to an infinite one when it runs dry.</summary>
        public void Consume(float units)
        {
            if (Current == null || Current.IsInfinite)
                return;
            amounts[CurrentIndex] = Mathf.Max(0f, amounts[CurrentIndex] - units);
            if (amounts[CurrentIndex] <= 0f)
                SelectFallback();
        }

        /// <summary>Tops up a liquid the beaver has a slot for, up to its capacity. Returns the units that fit.</summary>
        public float Add(LiquidType liquid, float units)
        {
            int index = slots.FindIndex(slot => slot.liquid == liquid);
            if (index < 0)
            {
                Debug.LogWarning($"{name} has no slot for {(liquid != null ? liquid.DisplayName : "a missing liquid")}.", this);
                return 0f;
            }
            if (liquid.IsInfinite)
                return 0f;
            float accepted = Mathf.Clamp(units, 0f, liquid.Capacity - amounts[index]);
            amounts[index] += accepted;
            return accepted;
        }

        void SelectFallback()
        {
            int infinite = slots.FindIndex(slot => slot.liquid.IsInfinite);
            CurrentIndex = infinite >= 0 ? infinite : 0;
        }
    }
}
