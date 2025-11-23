using UnityEngine;

public class RetractingState : GrappleState {
    public RetractingState(GrappleController owner) : base(owner) { }

    public override void Enter()
    {
        owner.isGrappling = false;
        // Disable visuals
        owner.ropeRenderer.enabled = false;

        // Reset any necessary variables
        owner.grappledObject = null;
        owner.grapplePoint = Vector2.zero;

        // Immediately transition back to ready
        owner.ChangeState(owner.readyState);
    }
}
