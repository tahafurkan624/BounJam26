using BounJam.Player;
using TMPro;
using UnityEngine;

namespace BounJam.UI
{
    /// <summary>Shows the beaver's liquid slots, how full each is, and the name of the selected liquid.</summary>
    public class LiquidHud : MonoBehaviour
    {
        [SerializeField] LiquidInventory inventory;
        [SerializeField] TMP_Text liquidName;
        [SerializeField] LiquidSlotView[] slots;

        void LateUpdate()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                bool exists = i < inventory.SlotCount;
                if (slots[i].gameObject.activeSelf != exists)
                    slots[i].gameObject.SetActive(exists);
                if (exists)
                    slots[i].Show(inventory.LiquidAt(i), inventory.FillAt(i), i == inventory.CurrentIndex, inventory.IsUsable(i));
            }
            liquidName.text = inventory.Current != null ? inventory.Current.DisplayName : string.Empty;
        }
    }
}
