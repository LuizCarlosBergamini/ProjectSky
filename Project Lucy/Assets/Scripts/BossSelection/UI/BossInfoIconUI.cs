using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>One small icon under a boss's name (difficulty, arena type...). Data comes from BossData_SO.infoIcons.</summary>
public class BossInfoIconUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image _icon;
    [SerializeField] private TextMeshProUGUI _label;

    private TooltipUI _tooltip;
    private string _tooltipText;
    private Color _tint = Color.white;

    public void Set(BossInfoIcon info, Color tint, TooltipUI tooltip)
    {
        _tooltip = tooltip;
        _tooltipText = info != null ? info.tooltip : "";
        _tint = tint;

        if (_icon != null)
        {
            _icon.sprite = info != null ? info.icon : null;
            _icon.enabled = _icon.sprite != null;
        }

        if (_label != null)
        {
            string label = info != null ? info.label : "";
            _label.text = label ?? "";
            _label.gameObject.SetActive(!string.IsNullOrWhiteSpace(label));
        }

        SetDimmed(false, null);
    }

    public void SetDimmed(bool dimmed, Material material)
    {
        Color color = dimmed ? new Color(0.5f, 0.5f, 0.53f, 0.7f) : _tint;
        if (_icon != null)
        {
            _icon.material = dimmed ? material : null;
            _icon.color = color;
        }

        if (_label != null) _label.color = color;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_tooltip == null || string.IsNullOrWhiteSpace(_tooltipText)) return;
        _tooltip.Show(_tooltipText, null, (RectTransform)transform);
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
