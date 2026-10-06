using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Makes a full-screen Image behave like "background-size: cover": it keeps the sprite's aspect ratio, always fills
/// its parent rect and crops whatever sticks out, at any resolution. Swapping the sprite (on the Image or through
/// MainMenuController) is picked up on the next frame, in Play Mode and in the Editor, so new art needs no layout
/// changes. The image is forced to be purely decorative: it never takes raycasts.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public class CoverImage : MonoBehaviour
{
    private Image _image;
    private Sprite _appliedSprite;
    private Vector2 _appliedParentSize = new(-1f, -1f);

    private void OnEnable()
    {
        Refresh();
    }

    private void LateUpdate()
    {
        // Cheap per-frame check: only touches the layout when the sprite or the parent size actually changed.
        RectTransform parent = transform.parent as RectTransform;
        if (parent == null) return;

        Sprite sprite = Image.sprite;
        if (sprite != _appliedSprite || parent.rect.size != _appliedParentSize) Refresh();
    }

    /// <summary>Re-fits the image to its parent now.</summary>
    public void Refresh()
    {
        RectTransform rect = (RectTransform)transform;
        RectTransform parent = transform.parent as RectTransform;
        if (parent == null) return;

        Image.raycastTarget = false;
        Image.type = Image.Type.Simple;
        Image.preserveAspect = false;

        Sprite sprite = Image.sprite;
        Vector2 parentSize = parent.rect.size;
        Vector2 spriteSize = sprite != null ? sprite.rect.size : Vector2.zero;

        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = CoverLayout.ComputeCoverSize(parentSize, spriteSize);

        _appliedSprite = sprite;
        _appliedParentSize = parentSize;
    }

    private Image Image
    {
        get
        {
            if (_image == null) _image = GetComponent<Image>();
            return _image;
        }
    }
}
