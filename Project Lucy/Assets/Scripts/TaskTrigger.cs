using UnityEngine;

/// <summary>
/// Starts or completes a task by id from a scene UnityEvent.
/// TaskManager is a DontDestroyOnLoad singleton created in the Hub, so it cannot be dragged
/// into an event slot from inside a level scene. This resolves it at call time instead.
/// </summary>
public class TaskTrigger : MonoBehaviour
{
    [Tooltip("Id da task em TaskManager (ex: missao-boss).")]
    [SerializeField] private string taskId;

    public void StartTask()
    {
        if (!EnsureManager()) return;
        TaskManager.instance.StartTask(taskId);
    }

    public void CompleteTask()
    {
        if (!EnsureManager()) return;
        TaskManager.instance.CompleteTask(taskId);
    }

    private bool EnsureManager()
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            Debug.LogWarning($"{name}: TaskTrigger sem taskId.", this);
            return false;
        }

        if (TaskManager.instance == null)
        {
            // Expected when a level is played on its own instead of entered from the Hub.
            Debug.LogWarning($"{name}: TaskManager nao encontrado, task '{taskId}' ignorada.", this);
            return false;
        }

        return true;
    }
}
