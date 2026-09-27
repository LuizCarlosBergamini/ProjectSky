using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Disables this GameObject when the scene loads if a task is in one of the given states.
/// Checked once on Start only, so an object never vanishes mid-scene (e.g. during the dialog
/// that starts the task). Use a UnityEvent to hide it at a specific moment instead.
/// </summary>
public class DisableOnTaskStatus : MonoBehaviour
{
    [Tooltip("Id da task em TaskManager (ex: missao-boss).")]
    [SerializeField] private string taskId;

    [Tooltip("Se a task estiver em algum destes estados ao carregar a cena, o objeto e desativado.")]
    [SerializeField] private List<TaskManager.TaskStatus> statuses = new();

    private void Start()
    {
        // Expected to be missing when a level is played on its own instead of entered from the Hub.
        if (TaskManager.instance == null || string.IsNullOrWhiteSpace(taskId)) return;

        if (statuses.Contains(TaskManager.instance.GetStatus(taskId)))
        {
            gameObject.SetActive(false);
        }
    }
}
