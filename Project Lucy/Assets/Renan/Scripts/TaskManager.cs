using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
public class TasksItem
{
    public string taskId;
    public string taskDescription;
    public List<InventorySlot> requiredItems;
    public string onStartEventTrigger;
    public string onFinishEventTrigger;
}

public class TaskManager : MonoBehaviour
{
    public static TaskManager instance;
    [SerializeField] private List<TasksItem> tasks = new();
    [SerializeField] private List<string> startedTasks = new ();
    [SerializeField] private List<string> finishedTasks = new();

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
            ValidateIfTaskCompleted();
        } else
        {
            Debug.Log($"Task '{taskId}' não encontrada!");
        }
    }

    public void CompleteTask(string taskId)
    {
        if (!startedTasks.Contains(taskId) || finishedTasks.Contains(taskId)) return;
        TasksItem taskData = tasks.Find(t => t.taskId == taskId);
        startedTasks.Remove(taskId);
        finishedTasks.Add(taskId);
        EventManager.instance.Call(taskData.onFinishEventTrigger);
        Debug.Log($"Task '{taskData.taskDescription}' finalizada!");
    }

    public void ValidateIfTaskCompleted()
    {
        if (InventoryManager.instance == null) return;

        Debug.Log("foo3");
        for (int i = 0; i < startedTasks.Count; i++)
        {
            string taskId = startedTasks[i];
            if (taskId != null)
            {
                TasksItem taskData = tasks.Find(t => t.taskId == taskId);
                Debug.Log("foo4");
                if (taskData != null && taskData.requiredItems.Count > 0)
                {
                    Debug.Log("foo5");
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
                    Debug.Log("foo6");
                    if (hasAllItem)
                    {
                        Debug.Log("foo7");
                        CompleteTask(taskId);
                    }
                }
            }
        }
    }
}
