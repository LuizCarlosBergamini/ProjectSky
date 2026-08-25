using TMPro;
using UnityEngine;

public class TaskItem : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _titleText;
    [SerializeField] private TextMeshProUGUI _descriptionText;
    [SerializeField] private TextMeshProUGUI _progressText;

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
        _progressText.gameObject.SetActive(!string.IsNullOrWhiteSpace(newProgressText));
    }
}
