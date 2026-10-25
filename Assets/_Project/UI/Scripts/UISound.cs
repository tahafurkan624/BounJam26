using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BounJam
{
    /// <summary>
    /// event:/UI for a button or slider: Hover as the pointer comes over it or keyboard/gamepad navigation moves onto it,
    /// Click when a button is pressed (mouse or Submit). Selecting it from code (a menu opening) stays quiet, and so does
    /// one that can't be used.
    /// </summary>
    [RequireComponent(typeof(Selectable))]
    public class UISound : MonoBehaviour, IPointerEnterHandler, ISelectHandler
    {
        Selectable selectable;

        /// <summary>Gives each control the sounds, unless it has them already.</summary>
        public static void AddTo(params Selectable[] controls)
        {
            foreach (Selectable control in controls)
                if (control != null && control.GetComponent<UISound>() == null) control.gameObject.AddComponent<UISound>();
        }

        void Awake()
        {
            selectable = GetComponent<Selectable>();
            if (selectable is Button button) button.onClick.AddListener(OnClick);
        }

        public void OnPointerEnter(PointerEventData eventData) => Hover();

        // Navigation passes its move along; a selection made from code doesn't.
        public void OnSelect(BaseEventData eventData)
        {
            if (eventData is AxisEventData) Hover();
        }

        void Hover()
        {
            if (selectable.IsInteractable()) GameSounds.UI.Play(GameSounds.UIParameter, GameSounds.UIHover);
        }

        static void OnClick() => GameSounds.UI.Play(GameSounds.UIParameter, GameSounds.UIClick);
    }
}
