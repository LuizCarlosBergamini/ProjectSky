using UnityEngine;

public class ReadyState : GrappleState {
    public ReadyState(GrappleController owner) : base(owner) { }

    public override void Enter()
    {
        Debug.Log("Entered Ready State");
    }

    public override void Execute()
    {
        // Aiming logic can be placed here (e.g., updating a crosshair)

        if (owner.grappleActions.Fire.WasPressedThisFrame())
        {
            Debug.Log("Firing Grapple");
            AimAndFire();
        }
    }

    private void AimAndFire()
    {
        Vector2 mousePosition = owner.mainCamera.ScreenToWorldPoint(
            owner.grappleActions.Aim.ReadValue<Vector2>()
        );
        Vector2 fireDirection = (mousePosition - (Vector2)owner.transform.position).normalized;

        RaycastHit2D hit = Physics2D.Raycast(owner.transform.position, fireDirection, owner.maxGrappleDistance, owner.grappleableLayer);

        Debug.DrawRay(owner.transform.position, fireDirection * owner.maxGrappleDistance, Color.green, 2f);

        if (hit.collider != null)
        {
            owner.isGrappling = true;
            Debug.Log("Raycast Fired - Hit: " + hit.collider.name);

            if (hit.collider.CompareTag("Grappleable"))
            {
                owner.grapplePoint = hit.point;
                owner.grappledObject = hit.collider.gameObject;
                owner.ChangeState(owner.attachedState);
            }
        }
        else
        {
            Debug.Log("Raycast Fired - No Hit");
        }
    }
}
