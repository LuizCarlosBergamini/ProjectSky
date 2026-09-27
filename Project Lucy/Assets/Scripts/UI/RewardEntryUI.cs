using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One line of the boss reward panel: item icon, item name and quantity when above 1.</summary>
public class RewardEntryUI : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI label;

    public void Set(Item_SO item, int quantity)
    {
        if (item == null) return;

        if (icon != null)
        {
            icon.sprite = item.itemSprite;
            icon.enabled = item.itemSprite != null;
        }

        if (label != null)
        {
            label.text = quantity > 1 ? $"{item.itemName} x{quantity}" : item.itemName;
        }
    }
}
