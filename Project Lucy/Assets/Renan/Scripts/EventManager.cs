using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
public class Event
{
    public string eventName;
    public UnityEvent onCall;
}

public class EventManager : MonoBehaviour
{
    public static EventManager instance = null;

    [SerializeField] private List<Event> _events = new ();

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

    public void Call(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName)) return;
        Event eventCalled = _events.Find(e => e.eventName == eventName);
        if (eventCalled != null)
        {
            eventCalled.onCall?.Invoke();
            Debug.Log($"Evento '{eventName}' encontrado!");
        } else
        {
            Debug.Log($"Evento '{eventName}' não encontrado!");
        }
    }
}
