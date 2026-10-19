using TMPro;
using UnityEngine;

public class TaskItem : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _titleText;
    [SerializeField] private TextMeshProUGUI _descriptionText;
    [SerializeField] private TextMeshProUGUI _progressText;

    [Tooltip("Faixa escura atras do texto. Fica presa na direita do card e acompanha a largura do texto.")]
    [SerializeField] private RectTransform _background;
    [SerializeField] private float _backgroundPadding = 12f;

    private bool _titleOnly;
    private readonly Vector3[] _corners = new Vector3[4];

    public void Apply(string newTitleText, string newDescriptionText)
    {
        if (newTitleText != null && _titleText != null) _titleText.text = newTitleText;
        if (newDescriptionText != null && _descriptionText != null) _descriptionText.text = newDescriptionText;
    }

    /// <summary>Shows how many of each required item the player already has ("Engrenagem: 2/5").</summary>
    public void ApplyProgress(string newProgressText)
    {
        if (_progressText == null) return;
        _progressText.text = newProgressText ?? "";
        RefreshVisibility();
    }

    /// <summary>
    /// Collapses the card to its title, e.g. while the boss bar needs the space above it.
    /// The texts are kept, so switching back restores the full card.
    /// </summary>
    public void SetTitleOnly(bool titleOnly)
    {
        _titleOnly = titleOnly;
        RefreshVisibility();
    }

    private void RefreshVisibility()
    {
        if (_descriptionText != null) _descriptionText.gameObject.SetActive(!_titleOnly);
        if (_progressText != null)
        {
            _progressText.gameObject.SetActive(!_titleOnly && !string.IsNullOrWhiteSpace(_progressText.text));
        }
    }

    // Texts are right aligned, so the panel only needs to reach the leftmost rendered character.
    // Runs every frame because TMP updates its mesh (and textBounds) after layout, a frame after any change.
    private void LateUpdate()
    {
        if (_background == null) return;

        RectTransform card = (RectTransform)transform;
        float textLeft = float.MaxValue;
        IncludeText(_titleText, card, ref textLeft);
        IncludeText(_descriptionText, card, ref textLeft);
        IncludeText(_progressText, card, ref textLeft);
        if (textLeft == float.MaxValue) return;

        float width = Mathf.Clamp(card.rect.xMax - textLeft + _backgroundPadding, 0f, card.rect.width);
        if (!Mathf.Approximately(_background.sizeDelta.x, width))
        {
            _background.sizeDelta = new Vector2(width, _background.sizeDelta.y);
        }
    }

    private void IncludeText(TextMeshProUGUI text, RectTransform card, ref float textLeft)
    {
        if (text == null || !text.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(text.text)) return;

        Bounds bounds = text.textBounds;
        if (bounds.size.x <= 0f) return;

        Vector3 left = card.InverseTransformPoint(text.rectTransform.TransformPoint(bounds.min));
        textLeft = Mathf.Min(textLeft, left.x);
    }
}
