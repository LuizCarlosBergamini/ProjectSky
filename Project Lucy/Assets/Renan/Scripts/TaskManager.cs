using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
public class TasksItem
{
    public string taskId;
    public string taskDescription;
    public InventorySlot requiredItems;
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
        for (int i = 0; i < startedTasks.Count; i++)
        {
            string taskId = startedTasks[i];
            if (taskId != null)
            {
                TasksItem taskData = tasks.Find(t => t.taskId == taskId);
                if (taskData != null && taskData.requiredItems.item != null)
                {
                    InventorySlot item = InventoryManager.instance.GetItem(taskData.requiredItems.item.itemId);
                    if (item != null && item.quantity >= taskData.requiredItems.quantity)
                    {
                        CompleteTask(taskId);
                    }
                }
            }
        }
    }
}
