using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Collider2D))]
public class CallWhenTrigger : MonoBehaviour
{
    [SerializeField] private string requiredTag = "Player";

    public UnityEvent OnTriggerEnterAction;
    public UnityEvent OnTriggerExitAction;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!string.IsNullOrEmpty(requiredTag) && !other.CompareTag(requiredTag)) return;
        Debug.Log($"Trigger entered by {other.name} with tag {other.tag}");
        OnTriggerEnterAction.Invoke();
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!string.IsNullOrEmpty(requiredTag) && !other.CompareTag(requiredTag)) return;
        OnTriggerExitAction.Invoke();
    }
}