using UnityEngine;
using UnityEngine.Serialization;

public class PlayerRunState : PlayerState
{
    [SerializeField] private PlayerMovement ctx;
    
    [Header("Audio")]
    [SerializeField] private float footstepSoundDelay = 1f;
    private float footstepSoundDuration = 0f;
    [SerializeField] private AudioClip footstepAudioClip;
    private bool _canPlayFootsteps;
    private AudioManager _audioManager;
    
    public override void Enter()
    {
        Debug.Log("Entering running state");
        ctx.animator.Play("Player_Waking");
        
        ctx.IsJumping = false;
        ctx._isJumpCut = false;
        ctx._isJumpFalling = false;
    }
    
    public override void HandleInput()
    {
        if (ctx.playerActions.Jump.WasPressedThisFrame())
        {
            ctx.OnJumpInput();
            ctx.ChangeState(ctx.playerAirState);
        }
        if (ctx.playerActions.Attack.WasPressedThisFrame())
        {
        }
    }

    public override void LogicUpdate()
    {
        if (!ctx.IsGrounded) ctx.ChangeState(ctx.playerAirState); 
        if (ctx.MovementInput.x == 0) ctx.ChangeState(ctx.playerIdleState);
        
        if (AudioManager.instance != null && footstepAudioClip != null)
        {
            footstepSoundDuration += Time.fixedDeltaTime;
            if (footstepSoundDuration > footstepSoundDelay)
            {
                footstepSoundDuration = 0;
                AudioManager.instance.PlayWithVariation(footstepAudioClip);
            }
        }
    }
}
