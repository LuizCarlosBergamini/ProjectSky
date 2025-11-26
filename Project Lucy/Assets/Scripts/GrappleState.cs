using UnityEngine;

public class GrappleState
{
    protected GrappleController owner;

    public GrappleState(GrappleController owner)
    {
        this.owner = owner;
    }

    public virtual void Enter() { }
    public virtual void Execute() { } // Logic for Update()
    public virtual void FixedExecute() { } // Logic for FixedUpdate()
    public virtual void Exit() { }
}
