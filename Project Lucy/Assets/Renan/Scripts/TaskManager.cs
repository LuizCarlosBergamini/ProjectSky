using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
public class TasksItem
{
    public string taskId;
    public string taskTitle;
    [TextArea] public string taskDescription;
    public List<InventorySlot> requiredItems;
    public string onStartEventTrigger;
    public string onFinishEventTrigger;
}

public class TaskManager : MonoBehaviour
{
    public static TaskManager instance;
    [SerializeField] private List<TasksItem> tasks = new();
    [SerializeField] private List<string> startedTasks = new();
    [SerializeField] private List<string> finishedTasks = new();

    [SerializeField] private TaskItem taskItemPrefab;
    [SerializeField] private GameObject taskItemContainer;
    [SerializeField] private Dictionary<string, TaskItem> taskItems = new();


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

    public void StartTask(string taskId)
    {
        if (startedTasks.Contains(taskId) || finishedTasks.Contains(taskId)) return;
        startedTasks.Add(taskId);
        TasksItem taskData = tasks.Find(t => t.taskId == taskId);
        if (taskData != null)
        {
            EventManager.instance.Call(taskData.onStartEventTrigger);
            Debug.Log($"Task '{taskData.taskDescription}' iniciada!");
            Debug.Log($"Task '{taskId}' iniciada!");
            if (taskItems.TryGetValue(taskId, out TaskItem value)) 
            {
                taskItems.Remove(taskId);
            }
            if (taskItemContainer != null && taskItemPrefab != null)
            {
                var clone = Instantiate(taskItemPrefab, taskItemContainer.transform);
                clone.Apply(taskData.taskTitle, taskData.taskDescription);
                taskItems.Add(taskId, clone);
                if (clone.TryGetComponent(out Animator animator))
                {
                    animator.Play("ShowTask");
                }
            }
            
            ValidateIfTaskCompleted();
        } else
        {
            Debug.Log($"Task '{taskId}' não encontrada!");
        }
    }

    public void CompleteTask(string taskId)
    {
        StartCoroutine(CompleteTaskAsync(taskId));
    }

    public IEnumerator CompleteTaskAsync(string taskId)
    {
        if (!startedTasks.Contains(taskId) || finishedTasks.Contains(taskId)) yield break;
        TasksItem taskData = tasks.Find(t => t.taskId == taskId);
        startedTasks.Remove(taskId);
        finishedTasks.Add(taskId);
        EventManager.instance.Call(taskData.onFinishEventTrigger);
        if (taskItems.TryGetValue(taskId, out TaskItem value))
        {
            if (value.TryGetComponent(out Animator animator))
            {
                animator.Play("HideTask");
                yield return new WaitForSeconds(1);
            }
            Destroy(value.gameObject);
            taskItems.Remove(taskId);
        }
        Debug.Log($"Task '{taskData.taskDescription}' finalizada!");
    }

    public void ValidateIfTaskCompleted()
    {
        if (InventoryManager.instance == null) return;

        for (int i = 0; i < startedTasks.Count; i++)
        {
            string taskId = startedTasks[i];
            if (taskId != null)
            {
                TasksItem taskData = tasks.Find(t => t.taskId == taskId);

                if (taskData != null && taskData.requiredItems.Count > 0)
                {
                    bool hasAllItem = taskData.requiredItems.All((requiredItem) =>
                    {
                        if (requiredItem.item == null) return false;
                        InventorySlot item = InventoryManager.instance.GetItem(requiredItem.item.itemId);
                        Debug.Log($"required {requiredItem.item.itemName}: {requiredItem.quantity}");
                        if (item != null)
                        {
                            Debug.Log($"inventory {item.item.itemName}: {item.quantity}");
                        }
                        if (item != null && item.quantity >= requiredItem.quantity)
                        {
                            return true;
                        }
                        return false;

                    });

                    if (hasAllItem)
                    {
                        CompleteTask(taskId);
                    }
                }
            }
        }
    }
}
