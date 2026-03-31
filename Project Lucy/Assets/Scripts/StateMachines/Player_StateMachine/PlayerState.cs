using System;
using UnityEngine;

public abstract class PlayerState : MonoBehaviour
{
    protected PlayerMovement playerScript;
    
    public bool isComplete { get; protected set; }
    protected float startTime;
    public float time => Time.time - startTime;
    
    public virtual void Enter()
    {
        // Code to execute when entering the state
    }

    public virtual void HandleInput()
    {
        // Code to execute every frame while in the state
    }

    public virtual void LogicUpdate()
    {
        // Code to execute every fixed frame while in the state
    }

    public virtual void PhysicsUpdate()
    {
        // Code to execute when exiting the state
    }

    public virtual void Exit()
    {
        
    }
}
