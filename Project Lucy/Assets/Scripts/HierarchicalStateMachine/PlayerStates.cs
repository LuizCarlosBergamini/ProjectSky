using UnityEngine;

namespace HierarchicalStateMachine
{
    public class Attack : State
    {
        private readonly PlayerContext ctx;
        private bool shouldExit;
        
        public Attack(StateMachine sm, State parent, PlayerContext ctx) : base(sm, parent)
        {
            this.ctx = ctx;
        }

        protected override void OnEnter()
        {
            ctx.IsAttacking = true;
            ctx.WantsComboGravity = true;
            ctx.canWalk = false;

            StartNextAttack();
        }

        protected override void OnUpdate(float deltaTime)
        {
            Debug.Log("shoudExit" + shouldExit);
            if (!ctx.AttackFinished) return;
            
            int maxComboStep = Mathf.Max(1, ctx.MaxComboStep);
            if (ctx.ComboQueued && ctx.ComboStep < maxComboStep)
            {
                StartNextAttack();
                return;
            }

            shouldExit = true;
        }

        protected override State GetTransition()
        {
            if (!shouldExit) return null;
            
            ctx.ComboStep = 0;
            var root = (PlayerRoot)Parent;
            return ctx.IsGrounded ? root.Grounded : root.Airborne;
        }

        protected override void OnExit()
        {
            ctx.IsAttacking = false;
            ctx.WantsComboGravity = false;
            ctx.canWalk = true;
            ctx.CanReceiveComboInput = false;
            ctx.ComboQueued = false;
            ctx.AttackFinished = false;
            shouldExit = false;
        }

        private void StartNextAttack()
        {
            shouldExit = false;
            ctx.AttackFinished = false;
            ctx.ComboQueued = false;
            ctx.CanReceiveComboInput = false;
            ctx.LastPressedAttackTime = 0f;

            int maxComboStep = Mathf.Max(1, ctx.MaxComboStep);
            ctx.ComboStep = Mathf.Clamp(ctx.ComboStep + 1, 1, maxComboStep);
            ctx.animator.Play("Player_Attack" + ctx.ComboStep);
        }
    }
    
    public class Run : State
    {
        PlayerContext ctx;
        Rigidbody2D rb;
        
        public Run(StateMachine sm, State parent, PlayerContext ctx) : base(sm, parent)
        {
            this.ctx = ctx;
            this.rb = ctx.rb;
        }

        protected override void OnEnter()
        {
            ctx.animator.Play("Player_Waking");
        }

        protected override State GetTransition()
        {
            if (ctx.MovementInput.x == 0) return ((Grounded)Parent).Idle;
            return null;
        }
    }

    public class Idle : State
    {
        private PlayerContext ctx;
        
        public Idle(StateMachine sm, State parent, PlayerContext ctx) : base(sm, parent)
        {
            this.ctx = ctx;
        }

        protected override void OnEnter()
        {
            ctx.animator.Play("Player_Idle");
        }

        protected override State GetTransition()
        {
            if (Mathf.Abs(ctx.MovementInput.x) > 0.01f) return ((Grounded)Parent).Run;
            return null;
        }
    }
    
    public class Grounded : State
    {
        private PlayerContext ctx;
        public readonly Run Run;
        public readonly Idle Idle;
        
        public Grounded(StateMachine sm, State parent, PlayerContext ctx) : base(sm, parent)
        {
            this.ctx = ctx;
            // Idle State
            this.Idle = new Idle(sm, this, ctx);
            // Run State
            this.Run = new Run(sm, this, ctx);
            // Dash State
            // Attack State
            // Grappling Hook State
        }

        protected override void OnEnter()
        {
            ctx.IsJumping = false;
            ctx.isJumpCut = false;
            ctx.isJumpFalling = false;
        }
        
        protected override State GetInitialState() => Idle;
        
        protected override State GetTransition() 
        {
            if (CanJump() && ctx.LastPressedJumpTime > 0f)
            {
                ctx.Jump?.Invoke();
                return ((PlayerRoot)Parent).Airborne;
            }

            if (ctx.LastPressedAttackTime > 0f)
            {
                return ((PlayerRoot)Parent).Attack;
            }
            
            return ctx.IsGrounded ? null : ((PlayerRoot)Parent).Airborne;
        }
        
        public bool CanJump()
        {
            return ctx.LastOnGroundTime > 0 && !ctx.IsJumping;
        }
            
        
    }
    
    public class Airborne : State
    {
        private PlayerContext ctx;
        private bool _isFallingLocal;
        // TODO: Add this to Player Data
        private float FallSpeedYDampingChangeThreshold = 1;
        
        public Airborne(StateMachine sm, State parent, PlayerContext ctx) : base(sm, parent)
        {
            this.ctx = ctx;
            // Grappling Hook State
            // Attack State
            // Dash State
        }

        protected override void OnEnter()
        {
            if (ctx.rb.linearVelocityY > 0.01f)
            {
                ctx.animator.Play("Player_Jump");
            }
            else
            {
                ctx.animator.Play("Player_Fall");
            }
        }

        protected override void OnUpdate(float deltaTime)
        {
            #region CameraDumping

            if (ctx.rb.linearVelocityY < FallSpeedYDampingChangeThreshold && !CameraManager.instance.IsLerpingYDamping && !CameraManager.instance.LerpedFromPlayerFalling)
            {
                CameraManager.instance.LerpYDamping(true);
            }
        
            if (ctx.rb.linearVelocityY >= 0f && !CameraManager.instance.IsLerpingYDamping && CameraManager.instance.LerpedFromPlayerFalling)
            {
                CameraManager.instance.LerpedFromPlayerFalling = false;
                CameraManager.instance.LerpYDamping(false);
            }

            #endregion
            
            if (ctx.playerActions.Jump.WasReleasedThisFrame())
            {
                Debug.Log("Jump button released");
                if (CanJumpCut()) ctx.JumpCut?.Invoke();
            }

            if (ctx.rb.linearVelocityY < 0 && !ctx.isJumpFalling)
            {
                ctx.isJumpFalling = true;
                ctx.isJumpCut = false;
                ctx.animator.Play("Player_Fall");
            }
        }

        protected override State GetTransition()
        {
            if (ctx.IsGrounded && ctx.rb.linearVelocityY <= 0f) return ((PlayerRoot)Parent).Grounded;
            if (ctx.LastPressedAttackTime > 0f) return ((PlayerRoot)Parent).Attack;
            return null;
        }
        
        private bool CanJumpCut()
        {
            return ctx.IsJumping && ctx.rb.linearVelocityY > 0;
        }
    }
    
    public class PlayerRoot : State
    {
        public readonly Grounded Grounded;
        public readonly Airborne Airborne;
        public readonly Attack Attack;
        private readonly PlayerContext ctx;
        
        public PlayerRoot(StateMachine sm, PlayerContext ctx) : base(sm, null)
        {
            this.ctx = ctx;
            Grounded = new Grounded(sm, this, ctx);
            Airborne = new Airborne(sm, this, ctx);
            Attack = new Attack(sm, this, ctx);
        }
        
        protected override State GetInitialState() => Grounded;
        // protected override State GetTransition() => ctx.IsGrounded ? null : Airborne;
    }
}
