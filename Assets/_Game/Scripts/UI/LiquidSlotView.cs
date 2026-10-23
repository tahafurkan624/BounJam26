using BounJam.Liquids;
using UnityEngine;
using UnityEngine.UI;

namespace BounJam.UI
{
    /// <summary>One liquid slot on the HUD: a small tank that fills with the liquid's colour.</summary>
    public class LiquidSlotView : MonoBehaviour
    {
        static readonly Color SelectedFrame = new(1f, 1f, 1f, 0.95f);
        static readonly Color IdleFrame = new(1f, 1f, 1f, 0.25f);

        [SerializeField] Image frame;
        [SerializeField] Image fill;

        public void Show(LiquidType liquid, float fullness, bool selected, bool usable)
        {
            frame.color = selected ? SelectedFrame : IdleFrame;
            Color color = liquid.Color;
            color.a = usable ? 1f : 0.3f;
            fill.color = color;
            fill.rectTransform.anchorMax = new Vector2(1f, Mathf.Clamp01(fullness));
        }
    }
}
