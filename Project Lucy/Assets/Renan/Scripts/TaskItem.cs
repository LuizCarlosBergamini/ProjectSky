using TMPro;
using UnityEngine;

public class TaskItem : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _titleText;
    [SerializeField] private TextMeshProUGUI _descriptionText;
    [SerializeField] private TextMeshProUGUI _progressText;

    private bool _titleOnly;

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
}
