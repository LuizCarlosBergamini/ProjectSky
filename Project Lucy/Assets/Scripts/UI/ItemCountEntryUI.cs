using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One counter under Lucy's health bar: an item's icon and how many the player has.</summary>
public class ItemCountEntryUI : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI count;

    public Item_SO Item { get; private set; }

    public void Set(Item_SO item, int quantity)
    {
        Item = item;

        if (icon != null)
        {
            icon.sprite = item != null ? item.itemSprite : null;
            icon.enabled = icon.sprite != null;
        }

        SetQuantity(quantity);
    }

    public void SetQuantity(int quantity)
    {
        if (count != null) count.text = Mathf.Max(0, quantity).ToString();
    }
}
