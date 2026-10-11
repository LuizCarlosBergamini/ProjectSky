using UnityEngine;
using UnityEngine.UI;

/// <summary>One upgrade slot under Lucy's health bar: the icon of the highest level bought in one tree.</summary>
public class UpgradeIconSlotUI : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private Image frame;

    [Tooltip("Opacidade do icone e da moldura quando nada da arvore foi comprado.")]
    [Range(0f, 1f)] [SerializeField] private float emptyAlpha = 0.35f;

    public void Set(Sprite sprite, Color treeColor, bool purchased)
    {
        float alpha = purchased ? 1f : emptyAlpha;

        if (icon != null)
        {
            icon.sprite = sprite;
            icon.enabled = sprite != null;
            icon.color = new Color(treeColor.r, treeColor.g, treeColor.b, alpha);
        }

        if (frame != null)
        {
            Color frameColor = purchased ? treeColor : Color.white;
            frame.color = new Color(frameColor.r, frameColor.g, frameColor.b, alpha);
        }
    }
}
