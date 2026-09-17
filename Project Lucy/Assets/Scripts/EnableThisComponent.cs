using System;
using UnityEngine;

public class EnableThisComponent : MonoBehaviour
{
    [SerializeField] private Collider2D barrier;

    private void Awake()
    {
        barrier = GetComponent<Collider2D>();
        barrier.enabled = false;
    }

    public void EnableThisComponentMethod()
    {
        if (barrier != null) barrier.enabled = true;
    }

    public void DisableThisComponentMethod()
    {
        if (barrier != null) barrier.enabled = false;
    }
}
