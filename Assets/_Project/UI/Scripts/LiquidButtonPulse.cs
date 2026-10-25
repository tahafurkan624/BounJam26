using UnityEngine;
using UnityEngine.EventSystems;

namespace BounJam
{
    /// <summary>
    /// Makes a button breathe while it waits and swell when the pointer is on it (the main menu's Play).
    /// </summary>
    public class LiquidButtonPulse : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        [SerializeField] float breath = 0.035f;
        [SerializeField] float period = 2.2f;
        [SerializeField] float hoverScale = 1.08f;

        float hover, hoverVelocity;
        bool over;

        void Update()
        {
            hover = Mathf.SmoothDamp(hover, over ? 1f : 0f, ref hoverVelocity, 0.08f, Mathf.Infinity, Time.unscaledDeltaTime);
            float wave = Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / period) * 0.5f + 0.5f;
            float scale = Mathf.Lerp(1f + breath * wave, hoverScale, hover);
            transform.localScale = new Vector3(scale, scale, 1f);
        }

        public void OnPointerEnter(PointerEventData eventData) => over = true;
        public void OnPointerExit(PointerEventData eventData) => over = false;
        public void OnSelect(BaseEventData eventData) => over = true;
        public void OnDeselect(BaseEventData eventData) => over = false;
    }
}
