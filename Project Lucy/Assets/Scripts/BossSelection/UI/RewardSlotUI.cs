using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One square of a boss panel's reward grid: the item icon and, above 1, the quantity in the corner.
/// Hovering shows the item's name and description. Draws what it is given; the reward list itself comes
/// from the boss's TaskManager task.
/// </summary>
public class RewardSlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image _frame;
    [SerializeField] private Image _icon;
    [SerializeField] private TextMeshProUGUI _quantity;

    [SerializeField] private Color _frameColor = new(1f, 1f, 1f, 0.35f);
    [SerializeField] private Color _dimmedIconColor = new(0.55f, 0.55f, 0.58f, 0.55f);

    private TooltipUI _tooltip;
    private Item_SO _item;
    private int _amount;

    public Item_SO Item => _item;

    public void Set(Item_SO item, int quantity, TooltipUI tooltip)
    {
        _item = item;
        _amount = Mathf.Max(1, quantity);
        _tooltip = tooltip;

        if (_icon != null)
        {
            _icon.sprite = item != null ? item.itemSprite : null;
            _icon.enabled = _icon.sprite != null;
        }

        if (_quantity != null)
        {
            _quantity.text = _amount.ToString();
            _quantity.gameObject.SetActive(_amount > 1);
        }

        name = item != null ? $"Reward_{item.itemId}" : "Reward";
        SetDimmed(false, null);
    }

    /// <param name="material">Material for the icon while dimmed (e.g. grayscale); null keeps the default.</param>
    public void SetDimmed(bool dimmed, Material material)
    {
        if (_icon != null)
        {
            _icon.material = dimmed ? material : null;
            _icon.color = dimmed ? _dimmedIconColor : Color.white;
        }

        if (_frame != null) _frame.color = dimmed ? _dimmedIconColor * _frameColor : _frameColor;
        if (_quantity != null) _quantity.alpha = dimmed ? 0.6f : 1f;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_tooltip == null || _item == null) return;
        string title = _amount > 1 ? $"{_item.itemName} x{_amount}" : _item.itemName;
        _tooltip.Show(title, _item.itemDescription, (RectTransform)transform);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_tooltip != null) _tooltip.Hide((RectTransform)transform);
    }

    private void OnDisable()
    {
        if (_tooltip != null) _tooltip.Hide((RectTransform)transform);
    }
}
