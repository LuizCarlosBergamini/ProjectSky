using UnityEngine;
using UnityEngine.EventSystems;

public class SingleEventSystem : MonoBehaviour
{
    void Awake()
    {
        var objects = FindObjectsByType<EventSystem>(FindObjectsSortMode.None);

        if (objects.Length > 1)
            Destroy(gameObject);
    }
}
