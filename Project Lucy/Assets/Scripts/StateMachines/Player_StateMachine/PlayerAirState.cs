using System;
using UnityEngine;
using UnityEngine.Serialization;

public class PlayerAirState : PlayerState
{
    [SerializeField] private PlayerMovement ctx;
    
    public float fallSpeedYDampingChangeThreshold;
    private bool _isFallingLocal;
    
    public override void Enter()
    {
        Debug.Log("Entering airborne state");
        if (CanJump() && ctx.LastPressedJumpTime > 0f)
        {
            Jump();
        }
    }

    public override void LogicUpdate()
    {
        if (ctx.IsGrounded && Mathf.Abs(ctx.body.linearVelocityY) <= 0.01f)
        {
            _isFallingLocal = false;
            
            if (ctx.LastPressedJumpTime > 0f)
            {
                Jump();
                return;
            }

            if (Mathf.Abs(ctx.MovementInput.x) > 0.01f)
                ctx.ChangeState(ctx.playerRunState);
            else
                ctx.ChangeState(ctx.playerIdleState);

            return; 
        }
        
        if (ctx.body.linearVelocityY < 0)
        {
            ctx._isJumpFalling = true;
        }
        
        if (ctx.body.linearVelocityY < fallSpeedYDampingChangeThreshold && !CameraManager.instance.IsLerpingYDamping && !CameraManager.instance.LerpedFromPlayerFalling)
        {
            CameraManager.instance.LerpYDamping(true);
        }
        
        if (ctx.body.linearVelocityY >= 0f && !CameraManager.instance.IsLerpingYDamping && CameraManager.instance.LerpedFromPlayerFalling)
        {
            CameraManager.instance.LerpedFromPlayerFalling = false;
            CameraManager.instance.LerpYDamping(false);
        }
        
        if (ctx.playerActions.Jump.WasReleasedThisFrame())
        {
            Debug.Log("Jump button released");
            if (CanJumpCut()) ctx._isJumpCut = true;
        }

        if (!ctx.IsGrounded && ctx.body.linearVelocityY < 0 && !_isFallingLocal)
        {
            _isFallingLocal = true;
            ctx.animator.Play("Player_Fall");
        }
    }

    public override void HandleInput()
    {
        if (ctx.playerActions.Jump.WasPressedThisFrame())
        {
            ctx.OnJumpInput();
            if (CanJump())
            {
                Jump();
            }
        }
        if (ctx.playerActions.Attack.WasPressedThisFrame())
        {
            ctx.OnAttackInput();
        }
    }

    public void Jump()
    {
        ctx.IsJumping = true;
        ctx._isJumpCut = false;
        ctx._isJumpFalling = false;
        //Ensures we can't call Jump multiple times from one press
        ctx.LastPressedJumpTime = 0;
        ctx.LastOnGroundTime = 0;

        float force = ctx.Data.jumpForce;
        if (ctx.body.linearVelocityY < 0)
            force -= ctx.body.linearVelocityY;
        
        ctx.animator.Play("Player_Jump");
        ctx.body.AddForce(Vector2.up * force, ForceMode2D.Impulse);
    }
    
    public bool CanJump()
    {
        return ctx.LastOnGroundTime > 0 && !ctx.IsJumping;
    }
    
    private bool CanJumpCut()
    {
        return ctx.IsJumping && ctx.body.linearVelocityY > 0;
    }
}
