using UnityEngine;
using UnityEngine.Serialization;

public class PlayerIdleState : PlayerState
{
    [SerializeField] private PlayerMovement ctx;

    public override void Enter()
    {
        Debug.Log("Entering Idle State");
        ctx.animator.Play("Player_Idle");
        
        ctx.IsJumping = false;
        ctx._isJumpCut = false;
        ctx._isJumpFalling = false;
    }
    public override void Exit() { }

    public override void HandleInput()
    {
        if (ctx.playerActions.Jump.WasPressedThisFrame())
        {
            ctx.OnJumpInput();
            ctx.ChangeState(ctx.playerAirState);
        }
        if (ctx.playerActions.Attack.WasPressedThisFrame())
        {
            ctx.OnAttackInput();
        }
    }

    public override void LogicUpdate()
    {
        if (!ctx.IsGrounded) ctx.ChangeState(ctx.playerAirState); 
        if (Mathf.Abs(ctx.MovementInput.x) > 0.01f) ctx.ChangeState(ctx.playerRunState);

        
    }

    public override void PhysicsUpdate() { }
}