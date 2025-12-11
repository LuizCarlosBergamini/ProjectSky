using UnityEngine;
using UnityEngine.Rendering;

public class AttachedState : GrappleState {
    public AttachedState(GrappleController owner) : base(owner) { }

    private Rigidbody2D forceTarget;
    private bool pullTargetIsPlayer;

    public float swingForce = 100f; // Example swing force magnitude
    public float reelSpeed = 10f; // Speed at which the rope is reeled in
    public float jump = 30f; // Upward force applied when releasing the grapple

    public override void Enter()
    {
        if (owner.grapplingAudioClip != null && AudioManager.instance != null)
        {
            AudioManager.instance.PlayWithVariation(owner.grapplingAudioClip);
        }
        Debug.Log("Entered Attached State");
        // Determine the target for the force application
        Rigidbody2D grappledRigidbody = owner.grappledObject.GetComponent<Rigidbody2D>();

        if (grappledRigidbody == null || grappledRigidbody.bodyType == RigidbodyType2D.Static) // Grappled a static object
        {
            forceTarget = owner.playerRigidbody;
            pullTargetIsPlayer = true;
        }
        else
        {
            if (owner.playerRigidbody.mass < grappledRigidbody.mass)
            {
                forceTarget = owner.playerRigidbody;
                pullTargetIsPlayer = true;
            }
            else
            {
                forceTarget = grappledRigidbody;
                pullTargetIsPlayer = false;
            }
        }

        owner.ropeRestLengthFixed = Vector2.Distance(owner.playerRigidbody.position, owner.grapplePoint);
        owner.ropeRestLength = owner.ropeRestLengthFixed;
        owner.ropeRenderer.enabled = true;
        forceTarget.linearDamping = 0.5f; // Increase damping for smoother swinging
    }

    public override void Exit()
    {
        Debug.Log("Exited Attached State");
        // Restore the original drag
        forceTarget.linearDamping = owner.defaultDrag;
    }

    public override void FixedExecute()
    {
        Vector2 anchorPoint = owner.grapplePoint;
        Vector2 currentPosition = forceTarget.position;

        // If pulling an object, the "player" side of the rope is the actual player's position
        if (!pullTargetIsPlayer)
        {
            anchorPoint = owner.playerRigidbody.position;
        }

        Vector2 vectorToAnchor = currentPosition - anchorPoint;
        float currentDistance = vectorToAnchor.magnitude;

        Debug.Log($"before return, current distance:{currentDistance}, rope rest length: {owner.ropeRestLength}");
        // Do not apply force if we are at or within the rest length
        if (currentDistance > owner.ropeRestLength)
        {
            // Calculate Displacement
            float displacementMagnitude = currentDistance - owner.ropeRestLength;
            Vector2 displacementVector = displacementMagnitude * vectorToAnchor.normalized;

            // Calculate Spring Force (Hooke's Law)
            Vector2 springForce = -owner.springConstant * displacementVector;

            // Calculate Damping Force
            Vector2 dampingForce = -owner.dampingCoefficient * forceTarget.linearVelocity;

            // Safety checks + debug
            if (forceTarget.bodyType != RigidbodyType2D.Dynamic)
            {
                Debug.LogWarning($"AttachedState: target Rigidbody2D is not Dynamic (type={forceTarget.bodyType}). Forces will have no effect.");
                return;
            }

            if (springForce.sqrMagnitude > 0.0001f)
            {
                Debug.DrawLine(currentPosition, currentPosition + springForce * 0.1f, Color.cyan, 0.1f);
                Debug.Log($"AttachedState: Applying springForce {springForce} to {forceTarget.name}");
            }

            // Apply Total Force
            forceTarget.AddForce(springForce + dampingForce, ForceMode2D.Force);
        }

        // Add Swinging Force Based on Player Input
        float horizontalInput = owner.grappleActions.Swing.ReadValue<float>();
        if (horizontalInput != 0)
        {
            Vector2 perpendicularDirection = new Vector2(-vectorToAnchor.y, vectorToAnchor.x).normalized;
            Vector2 swingForceVector = perpendicularDirection * swingForce * horizontalInput;
            forceTarget.AddForce(swingForceVector, ForceMode2D.Force);
        }
    }

    public override void Execute()
    {
        if (owner.grappleActions.Fire.IsPressed())
        {
            owner.ropeRestLength -= reelSpeed * Time.deltaTime;
            owner.ropeRestLength = Mathf.Max(owner.ropeRestLength, owner.minRopeLength);
        }

        if (owner.grappleActions.Release.WasPressedThisFrame())
        {
            forceTarget.AddForce(new Vector2(0f, jump), ForceMode2D.Impulse);
            owner.ropeRestLength = owner.ropeRestLengthFixed;
            owner.ChangeState(owner.retractingState);
        }
    }

    public bool IsPullingPlayer()
    {
        return pullTargetIsPlayer;
    }
}
