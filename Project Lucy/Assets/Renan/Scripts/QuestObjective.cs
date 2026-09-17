using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Marks a place in the world a quest can point to (a level entrance, an NPC, a door).
/// Registers itself by id so the arrow can find it again after any scene load.
/// </summary>
public class QuestObjective : MonoBehaviour
{
    [Tooltip("Id usado pelas tasks (TasksItem.objectiveId) para apontar a seta para este objeto.")]
    [SerializeField] private string _objectiveId;

    [Tooltip("Ponto exato para onde a seta aponta. Vazio = este transform.")]
    [SerializeField] private Transform _anchor;

    private static readonly Dictionary<string, QuestObjective> _registry = new();

    public bool shouldEndMission = false;

    [Tooltip("Evento chamado no EventManager quando o Player entra no trigger.")]
    public string endMissionEventName;

    public string ObjectiveId => _objectiveId;
    public Transform Anchor => _anchor != null ? _anchor : transform;

    private void OnEnable()
    {
        if (string.IsNullOrWhiteSpace(_objectiveId)) return;
        _registry[_objectiveId] = this;
    }

    private void OnDisable()
    {
        if (string.IsNullOrWhiteSpace(_objectiveId)) return;
        if (_registry.TryGetValue(_objectiveId, out QuestObjective registered) && registered == this)
        {
            _registry.Remove(_objectiveId);
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!shouldEndMission || string.IsNullOrWhiteSpace(endMissionEventName)) return;
        if (!collision.CompareTag("Player")) return;

        EventManager.instance?.Call(endMissionEventName);
    }

    public static QuestObjective Get(string objectiveId)
    {
        if (string.IsNullOrWhiteSpace(objectiveId)) return null;
        if (_registry.TryGetValue(objectiveId, out QuestObjective objective) && objective != null)
        {
            return objective;
        }

        // The entry survived a scene unload as a destroyed reference.
        _registry.Remove(objectiveId);
        return null;
    }
}
