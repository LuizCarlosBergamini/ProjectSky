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
