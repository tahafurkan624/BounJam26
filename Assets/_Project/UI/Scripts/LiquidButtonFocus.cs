using BlobShip.UI.Liquid3D;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BounJam
{
    /// <summary>
    /// Gives a 3D liquid button (<see cref="LiquidSlab"/>) the same feedback for keyboard and gamepad as for the
    /// mouse: it lifts while selected, as on hover, and ripples when submitted, as on a click.
    /// </summary>
    [RequireComponent(typeof(LiquidSlab))]
    public class LiquidButtonFocus : MonoBehaviour, ISelectHandler, IDeselectHandler, ISubmitHandler
    {
        LiquidSlab slab;

        void Awake() => slab = GetComponent<LiquidSlab>();

        public void OnSelect(BaseEventData eventData) => slab.OnPointerEnter(null);

        public void OnDeselect(BaseEventData eventData) => slab.OnPointerExit(null);

        public void OnSubmit(BaseEventData eventData) => slab.Poke();
    }
}
