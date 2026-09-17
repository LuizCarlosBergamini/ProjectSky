using UnityEngine;

namespace HierarchicalStateMachine
{
    public class Attack : State
    {
        private const float AttackCooldownDuration = 0.35f;
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
            // ctx.Data.runMaxSpeed = 6.5f;

            StartNextAttack();
        }

        protected override void OnUpdate(float deltaTime)
        {
                Debug.Log("shoudExit" + shouldExit);
            if (!ctx.AttackFinished) return;
            
            // int maxComboStep = Mathf.Max(1, ctx.MaxComboStep);
            if (ctx.ComboQueued && ctx.ComboStep < ctx.MaxComboStep)
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
            ctx.AttackCooldownTime = AttackCooldownDuration;
            var root = (PlayerRoot)Parent;
            return ctx.IsGrounded ? root.Grounded : root.Airborne;
        }

        protected override void OnExit()
        {
            // ctx.Data.runMaxSpeed = ctx.Data.runMaxSpeed;
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

            // int maxComboStep = Mathf.Max(1, ctx.MaxComboStep);
            ctx.ComboStep = Mathf.Clamp(ctx.ComboStep + 1, 1, ctx.MaxComboStep);
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
            ctx.PlayAnimation?.Invoke("Player_Waking");
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
            ctx.PlayAnimation?.Invoke("Player_Idle");
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
            if (ctx.playerActions.Fire.WasPressedThisFrame() && ctx.TryStartGrapple())
            {
                return ((PlayerRoot)Parent).Grappling;
            }

            if (CanJump() && ctx.LastPressedJumpTime > 0f)
            {
                ctx.Jump?.Invoke();
                return ((PlayerRoot)Parent).Airborne;
            }

            if (ctx.LastPressedAttackTime > 0f && ctx.AttackCooldownTime <= 0f)
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
            // A jump impulse only shows up in linearVelocity after the next physics step, so on the
            // frame we jump the velocity is still ~0. Trust the IsJumping flag first.
            if (ctx.IsJumping || ctx.rb.linearVelocityY > 0.01f)
            {
                ctx.PlayAnimation?.Invoke("Player_Jump");
            }
            else
            {
                ctx.PlayAnimation?.Invoke("Player_Fall");
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
                ctx.PlayAnimation?.Invoke("Player_Fall");
            }
        }

        protected override State GetTransition()
        {
            if (ctx.playerActions.Fire.WasPressedThisFrame() && ctx.TryStartGrapple())
            {
                return ((PlayerRoot)Parent).Grappling;
            }

            // Coyote jump: Grounded is left the instant IsGrounded goes false, so the grace window
            // can only ever be consumed from here.
            if (ctx.LastOnGroundTime > 0f && ctx.LastPressedJumpTime > 0f && !ctx.IsJumping)
            {
                ctx.Jump?.Invoke();
                return null;
            }

            if (ctx.IsGrounded && ctx.rb.linearVelocityY <= 0f) return ((PlayerRoot)Parent).Grounded;
            if (ctx.LastPressedAttackTime > 0f && ctx.AttackCooldownTime <= 0f) return ((PlayerRoot)Parent).Attack;
            return null;
        }
        
        private bool CanJumpCut()
        {
            return ctx.IsJumping && ctx.rb.linearVelocityY > 0;
        }
    }

    public class Grappling : State
    {
        // Hard floor for the reel-in, so the rope can never collapse to zero length and blow the
        // spring up (MinRopeLength is left at 0 in the inspector by default).
        private const float MinRopeLengthFloor = 0.25f;
        private const float SwingDamping = 0.5f;

        private readonly PlayerContext ctx;
        private bool shouldExit;
        private bool dampingOverridden;
        private float originalTargetDamping;

        public Grappling(StateMachine sm, State parent, PlayerContext ctx) : base(sm, parent)
        {
            this.ctx = ctx;
        }

        protected override void OnEnter()
        {
            shouldExit = false;
            dampingOverridden = false;

            if (ctx.GrappleTarget == null || ctx.GrappledObject == null)
            {
                shouldExit = true;
                return;
            }

            // The driver's run force and fall-speed clamps would fight the pendulum, so the
            // grapple owns horizontal movement and gravity while it is active.
            ctx.canWalk = false;

            if (ctx.grapplingAudioClip != null && global::AudioManager.instance != null)
            {
                global::AudioManager.instance.PlayWithVariation(ctx.grapplingAudioClip);
            }

            Rigidbody2D grappledRigidbody = ctx.GrappledObject.GetComponent<Rigidbody2D>();
            if (grappledRigidbody == null) grappledRigidbody = ctx.GrappledObject.GetComponentInParent<Rigidbody2D>();

            if (grappledRigidbody == null || grappledRigidbody.bodyType == RigidbodyType2D.Static)
            {
                ctx.ForceTarget = ctx.rb;
                ctx.PullTargetIsPlayer = true;
            }
            else if (ctx.rb.mass < grappledRigidbody.mass)
            {
                ctx.ForceTarget = ctx.rb;
                ctx.PullTargetIsPlayer = true;
            }
            else
            {
                ctx.ForceTarget = grappledRigidbody;
                ctx.PullTargetIsPlayer = false;
            }

            ctx.GrapplePoint = ctx.GrappleTarget.GetAnchorPoint();
            ctx.RopeRestLengthFixed = Vector2.Distance(ctx.rb.position, ctx.GrapplePoint);
            ctx.RopeRestLength = ctx.RopeRestLengthFixed;

            if (ctx.ropeRenderer != null)
            {
                ctx.ropeRenderer.positionCount = 2;
                ctx.ropeRenderer.enabled = true;
                ctx.UpdateRopeVisuals();
            }

            if (ctx.ForceTarget != null)
            {
                originalTargetDamping = ctx.ForceTarget.linearDamping;
                ctx.ForceTarget.linearDamping = SwingDamping;
                dampingOverridden = true;
            }
        }

        protected override State GetTransition()
        {
            if (!shouldExit) return null;

            PlayerRoot root = (PlayerRoot)Parent;
            return ctx.IsGrounded ? root.Grounded : root.Airborne;
        }

        protected override void OnUpdate(float deltaTime)
        {
            if (ctx.GrappledObject == null || ctx.ForceTarget == null)
            {
                shouldExit = true;
                return;
            }

            if (ctx.PullTargetIsPlayer && ctx.GrappleTarget != null)
            {
                ctx.GrapplePoint = ctx.GrappleTarget.GetAnchorPoint();
            }

            if (ctx.playerActions.Fire.IsPressed())
            {
                ctx.RopeRestLength -= ctx.ReelSpeed * deltaTime;
                ctx.RopeRestLength = Mathf.Max(ctx.RopeRestLength, Mathf.Max(MinRopeLengthFloor, ctx.MinRopeLength));
            }

            if (ctx.playerActions.Release.WasPressedThisFrame())
            {
                if (ctx.ForceTarget.bodyType == RigidbodyType2D.Dynamic)
                {
                    ctx.ForceTarget.AddForce(Vector2.up * ctx.ReleaseJumpForce, ForceMode2D.Impulse);
                }

                // Release shares the Space binding with Jump, so the press also filled the jump
                // buffer. Drop it, otherwise the player auto-jumps the moment they land.
                ctx.LastPressedJumpTime = 0f;
                shouldExit = true;
            }

            ctx.UpdateRopeVisuals();
        }

        protected override void OnFixedUpdate(float fixedDeltaTime)
        {
            if (ctx.ForceTarget == null)
            {
                shouldExit = true;
                return;
            }

            Vector2 anchorPoint = ctx.GrapplePoint;
            Vector2 currentPosition = ctx.ForceTarget.position;

            if (!ctx.PullTargetIsPlayer)
            {
                anchorPoint = ctx.rb.position;
            }

            Vector2 vectorToAnchor = currentPosition - anchorPoint;
            float currentDistance = vectorToAnchor.magnitude;

            if (currentDistance > ctx.RopeRestLength && currentDistance > 0.001f)
            {
                if (ctx.ForceTarget.bodyType != RigidbodyType2D.Dynamic)
                {
                    shouldExit = true;
                    return;
                }

                float displacementMagnitude = currentDistance - ctx.RopeRestLength;
                Vector2 displacementVector = displacementMagnitude * vectorToAnchor.normalized;
                Vector2 springForce = -ctx.SpringConstant * displacementVector;
                Vector2 dampingForce = -ctx.DampingCoefficient * ctx.ForceTarget.linearVelocity;

                ctx.ForceTarget.AddForce(springForce + dampingForce, ForceMode2D.Force);
            }

            float horizontalInput = ctx.playerActions.Swing.ReadValue<float>();
            if (Mathf.Abs(horizontalInput) > 0.01f && vectorToAnchor.sqrMagnitude > 0.001f)
            {
                Vector2 perpendicularDirection = new Vector2(-vectorToAnchor.y, vectorToAnchor.x).normalized;
                Vector2 swingForceVector = perpendicularDirection * ctx.SwingForce * horizontalInput;
                ctx.ForceTarget.AddForce(swingForceVector, ForceMode2D.Force);
            }
        }

        protected override void OnExit()
        {
            if (dampingOverridden && ctx.ForceTarget != null)
            {
                ctx.ForceTarget.linearDamping = originalTargetDamping;
            }

            // Hand the airborne state a clean slate: the swing is not a jump, so the jump-hang and
            // jump-cut branches in ApplyGravity must not pick it up.
            ctx.IsJumping = false;
            ctx.isJumpCut = false;
            ctx.isJumpFalling = false;
            ctx.canWalk = true;
            dampingOverridden = false;

            ctx.ClearGrapple();
            shouldExit = false;
        }
    }
    
    public class PlayerRoot : State
    {
        public readonly Grounded Grounded;
        public readonly Airborne Airborne;
        public readonly Attack Attack;
        public readonly Grappling Grappling;
        private readonly PlayerContext ctx;
        
        public PlayerRoot(StateMachine sm, PlayerContext ctx) : base(sm, null)
        {
            this.ctx = ctx;
            Grounded = new Grounded(sm, this, ctx);
            Airborne = new Airborne(sm, this, ctx);
            Attack = new Attack(sm, this, ctx);
            Grappling = new Grappling(sm, this, ctx);
        }
        
        protected override State GetInitialState() => Grounded;
        // protected override State GetTransition() => ctx.IsGrounded ? null : Airborne;
    }
}
