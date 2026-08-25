using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

[Serializable]
public class TasksItem
{
    public string taskId;
    public string taskTitle;
    [TextArea] public string taskDescription;

    [Tooltip("Itens que o jogador precisa ter para a task ser concluida.")]
    public List<InventorySlot> requiredItems;

    [Tooltip("Itens removidos do inventario quando a task e concluida (entrega).")]
    public List<InventorySlot> consumeItems;

    [Tooltip("Itens entregues ao jogador quando a task e concluida (recompensa).")]
    public List<InventorySlot> rewardItems;

    [Tooltip("Id do QuestObjective para onde a seta deve apontar enquanto a task estiver ativa.")]
    public string objectiveId;

    public string onStartEventTrigger;
    public string onFinishEventTrigger;
}

public class TaskManager : MonoBehaviour
{
    // Nested so it does not collide with System.Threading.Tasks.TaskStatus.
    public enum TaskStatus
    {
        NotStarted,
        Started,
        Finished
    }

    public static TaskManager instance;

    /// <summary>Raised whenever a task is started, completed or has its progress updated.</summary>
    public static event Action OnTaskStateChanged;

    [SerializeField] private List<TasksItem> tasks = new();
    [SerializeField] private List<string> startedTasks = new();
    [SerializeField] private List<string> finishedTasks = new();

    [SerializeField] private TaskItem taskItemPrefab;
    [SerializeField] private GameObject taskItemContainer;

    private readonly Dictionary<string, TaskItem> taskItems = new();

    // Tasks whose completion routine is already running, so rewards are never granted twice.
    private readonly HashSet<string> completingTasks = new();

    private void Start()
    {
        if (EventManager.instance == null)
        {
            Debug.LogWarning("EventManager instance not found!");
        }
    }

    private void Awake()
    {
        if (instance != null)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    #region Task Flow

    public void StartTask(string taskId)
    {
        if (startedTasks.Contains(taskId) || finishedTasks.Contains(taskId)) return;
        TasksItem taskData = GetTask(taskId);
        if (taskData == null)
        {
            Debug.Log($"Task '{taskId}' nao encontrada!");
            return;
        }

        startedTasks.Add(taskId);
        EventManager.instance?.Call(taskData.onStartEventTrigger);
        Debug.Log($"Task '{taskId}' iniciada!");

        if (taskItems.TryGetValue(taskId, out TaskItem previous))
        {
            if (previous != null) Destroy(previous.gameObject);
            taskItems.Remove(taskId);
        }

        if (taskItemContainer != null && taskItemPrefab != null)
        {
            var clone = Instantiate(taskItemPrefab, taskItemContainer.transform);
            clone.Apply(taskData.taskTitle, taskData.taskDescription);
            clone.ApplyProgress(GetProgressText(taskData));
            taskItems.Add(taskId, clone);
            if (clone.TryGetComponent(out Animator animator))
            {
                animator.Play("ShowTask");
            }
        }

        OnTaskStateChanged?.Invoke();
        ValidateIfTaskCompleted();
    }

    public void CompleteTask(string taskId)
    {
        if (!startedTasks.Contains(taskId) || finishedTasks.Contains(taskId)) return;
        if (!completingTasks.Add(taskId)) return;
        StartCoroutine(CompleteTaskAsync(taskId));
    }

    private IEnumerator CompleteTaskAsync(string taskId)
    {
        TasksItem taskData = GetTask(taskId);
        if (taskData == null)
        {
            Debug.Log($"Task '{taskId}' nao encontrada!");
            completingTasks.Remove(taskId);
            yield break;
        }

        // Flip the state before touching the inventory: granting rewards runs
        // ValidateIfTaskCompleted again and this task must no longer be a candidate.
        startedTasks.Remove(taskId);
        finishedTasks.Add(taskId);

        ConsumeItems(taskData);
        GrantRewards(taskData);

        EventManager.instance?.Call(taskData.onFinishEventTrigger);
        Debug.Log($"Task '{taskId}' finalizada!");

        // Consuming items can invalidate the counters shown by any other open task.
        RefreshTasksProgress();

        if (taskItems.TryGetValue(taskId, out TaskItem value) && value != null)
        {
            taskItems.Remove(taskId);
            if (value.TryGetComponent(out Animator animator))
            {
                animator.Play("HideTask");
                yield return new WaitForSeconds(1);
            }
            Destroy(value.gameObject);
        }
        else
        {
            taskItems.Remove(taskId);
        }

        completingTasks.Remove(taskId);
    }

    public void ValidateIfTaskCompleted()
    {
        if (InventoryManager.instance == null) return;

        // Copy: completing a task mutates startedTasks while we walk it.
        foreach (string taskId in startedTasks.ToArray())
        {
            if (string.IsNullOrWhiteSpace(taskId)) continue;

            TasksItem taskData = GetTask(taskId);
            if (taskData == null || taskData.requiredItems == null || taskData.requiredItems.Count == 0) continue;

            if (taskItems.TryGetValue(taskId, out TaskItem card) && card != null)
            {
                card.ApplyProgress(GetProgressText(taskData));
            }

            if (HasAllRequiredItems(taskData))
            {
                CompleteTask(taskId);
            }
        }
    }

    /// <summary>Re-renders the progress line of every visible task card without completing anything.</summary>
    public void RefreshTasksProgress()
    {
        foreach (string taskId in startedTasks)
        {
            TasksItem taskData = GetTask(taskId);
            if (taskData == null) continue;
            if (taskItems.TryGetValue(taskId, out TaskItem card) && card != null)
            {
                card.ApplyProgress(GetProgressText(taskData));
            }
        }

        OnTaskStateChanged?.Invoke();
    }

    #endregion

    #region Queries

    public TasksItem GetTask(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId)) return null;
        return tasks.Find(t => t.taskId == taskId);
    }

    public TaskStatus GetStatus(string taskId)
    {
        if (finishedTasks.Contains(taskId)) return TaskStatus.Finished;
        if (startedTasks.Contains(taskId)) return TaskStatus.Started;
        return TaskStatus.NotStarted;
    }

    public bool HasAllRequiredItems(string taskId)
    {
        return HasAllRequiredItems(GetTask(taskId));
    }

    public bool HasAllRequiredItems(TasksItem taskData)
    {
        if (taskData == null || InventoryManager.instance == null) return false;
        if (taskData.requiredItems == null || taskData.requiredItems.Count == 0) return false;

        return taskData.requiredItems.All(required =>
            required.item != null &&
            InventoryManager.instance.GetQuantity(required.item.itemId) >= required.quantity);
    }

    /// <summary>Objective the arrow should point at: the first active task that declares one.</summary>
    public string GetActiveObjectiveId()
    {
        foreach (string taskId in startedTasks)
        {
            TasksItem taskData = GetTask(taskId);
            if (taskData != null && !string.IsNullOrWhiteSpace(taskData.objectiveId))
            {
                return taskData.objectiveId;
            }
        }

        return null;
    }

    public string GetProgressText(TasksItem taskData)
    {
        if (taskData == null || taskData.requiredItems == null || taskData.requiredItems.Count == 0) return "";

        StringBuilder builder = new();
        foreach (InventorySlot required in taskData.requiredItems)
        {
            if (required.item == null) continue;
            int owned = InventoryManager.instance != null
                ? InventoryManager.instance.GetQuantity(required.item.itemId)
                : 0;
            if (builder.Length > 0) builder.Append('\n');
            builder.Append($"{required.item.itemName}: {Mathf.Min(owned, required.quantity)}/{required.quantity}");
        }

        return builder.ToString();
    }

    #endregion

    #region Rewards

    private void ConsumeItems(TasksItem taskData)
    {
        if (taskData.consumeItems == null || InventoryManager.instance == null) return;

        foreach (InventorySlot cost in taskData.consumeItems)
        {
            if (cost.item == null) continue;
            InventoryManager.instance.RemoveItem(cost.item.itemId, cost.quantity);
        }
    }

    private void GrantRewards(TasksItem taskData)
    {
        if (taskData.rewardItems == null || InventoryManager.instance == null) return;

        foreach (InventorySlot reward in taskData.rewardItems)
        {
            if (reward.item == null) continue;
            // Rewards are not run pickups: dying later must never take them back.
            InventoryManager.instance.AddItem(reward.item, reward.quantity, false);
            Debug.Log($"Recompensa recebida: {reward.item.itemName} x{reward.quantity}");
        }
    }

    #endregion
}
