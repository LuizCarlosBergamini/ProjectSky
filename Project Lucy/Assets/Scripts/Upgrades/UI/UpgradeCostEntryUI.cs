using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Item icon plus a label. Used for node costs, the detail panel and the material counter.</summary>
public class UpgradeCostEntryUI : MonoBehaviour
{
    [SerializeField] private Image _icon;
    [SerializeField] private TextMeshProUGUI _label;

    public void Set(Sprite icon, string text, Color color)
    {
        if (_icon != null)
        {
            _icon.sprite = icon;
            _icon.enabled = icon != null;
        }

        if (_label != null)
        {
            _label.text = text;
            _label.color = color;
        }
    }
}
