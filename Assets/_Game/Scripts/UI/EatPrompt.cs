using BounJam.Player;
using TMPro;
using UnityEngine;

namespace BounJam.UI
{
    /// <summary>Shows "Hold E to eat" while a tree is within the beaver's reach.</summary>
    public class EatPrompt : MonoBehaviour
    {
        [SerializeField] TreeEater eater;
        [SerializeField] TMP_Text label;

        void LateUpdate()
        {
            bool show = eater.Target != null;
            if (label.enabled != show)
                label.enabled = show;
        }
    }
}
