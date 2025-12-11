using TMPro;
using UnityEngine;

public class TaskItem : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _titleText;
    [SerializeField] private TextMeshProUGUI _descriptionText;

    public void Apply(string newTitleText, string newDescriptionText)
    {
        if (newTitleText != null && _titleText != null) _titleText.text = newTitleText;
        if (newDescriptionText != null && _descriptionText != null) _descriptionText.text = newDescriptionText;
    }
}
