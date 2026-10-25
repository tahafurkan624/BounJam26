using BlobShip.UI.Liquid3D;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BounJam
{
    /// <summary>
    /// Turns a 3D liquid button another colour while it's hovered or selected (the main menu's Exit goes red), and back.
    /// </summary>
    [RequireComponent(typeof(LiquidSlab))]
    public class LiquidHoverTint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        [ColorUsage(false, true)] [SerializeField] Color hoverCore = new Color(0.86f, 0.1f, 0.14f);
        [ColorUsage(false, true)] [SerializeField] Color hoverRim = new Color(1f, 0.55f, 0.5f);
        [SerializeField] Vector3 hoverAbsorb = new Vector3(0.25f, 2.6f, 2.4f);
        [SerializeField] float speed = 8f;

        LiquidSlab slab;
        LiquidColors normal;
        float amount, target;

        void Awake()
        {
            slab = GetComponent<LiquidSlab>();
            normal = slab.colors;
            slab.reactsToSolid = false;
        }

        void Update()
        {
            if (Mathf.Approximately(amount, target)) return;
            amount = Mathf.MoveTowards(amount, target, Time.unscaledDeltaTime * speed);
            slab.colors = new LiquidColors(Color.Lerp(normal.core, hoverCore, amount), Color.Lerp(normal.rim, hoverRim, amount),
                Vector3.Lerp(normal.absorb, hoverAbsorb, amount));
        }

        public void OnPointerEnter(PointerEventData eventData) => target = 1f;
        public void OnPointerExit(PointerEventData eventData) => target = 0f;
        public void OnSelect(BaseEventData eventData) => target = 1f;
        public void OnDeselect(BaseEventData eventData) => target = 0f;
    }
}
