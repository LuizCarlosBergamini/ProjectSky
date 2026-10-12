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
        private const float RopeTautTolerance = 0.02f;

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
                ctx.ForceTarget.linearDamping = ctx.SwingDamping;
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

            if (ctx.playerActions.Release.WasPressedThisFrame())
            {
                if (ctx.ForceTarget.bodyType == RigidbodyType2D.Dynamic)
                {
                    // Same as an upward impulse, but applied to the velocity right away so the
                    // release speed can be capped together with the swing momentum it carries.
                    Vector2 releaseVelocity = ctx.ForceTarget.linearVelocity
                                              + Vector2.up * (ctx.ReleaseJumpForce / ctx.ForceTarget.mass);
                    ctx.ForceTarget.linearVelocity = Vector2.ClampMagnitude(releaseVelocity, ctx.MaxReleaseSpeed);
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
            Rigidbody2D body = ctx.ForceTarget;
            if (body == null)
            {
                shouldExit = true;
                return;
            }

            Vector2 anchorPoint = ctx.PullTargetIsPlayer ? ctx.GrapplePoint : ctx.rb.position;
            Vector2 toBody = body.position - anchorPoint;
            float distance = toBody.magnitude;
            if (distance < 0.001f) return;

            if (body.bodyType != RigidbodyType2D.Dynamic)
            {
                if (distance > ctx.RopeRestLength) shouldExit = true;
                return;
            }

            // Small slack tolerance so float noise after the position correction below does not
            // flicker the rope between taut and slack every step.
            bool ropeTaut = distance >= ctx.RopeRestLength - RopeTautTolerance;

            Vector2 radial = toBody / distance;
            Vector2 velocity = body.linearVelocity;

            // Inextensible rope instead of a spring: a taut rope cancels any velocity that would
            // stretch it. The old spring stored energy and gave it back as random jolts, and its
            // damping acted on the whole velocity, which also ate the swing itself.
            if (ropeTaut)
            {
                float radialSpeed = Vector2.Dot(velocity, radial);
                if (radialSpeed > 0f) velocity -= radial * radialSpeed;
            }

            float horizontalInput = ctx.playerActions.Swing.ReadValue<float>();
            // Only pump while hanging below the anchor. Above it the old perpendicular force kept
            // pushing along the circle, so holding D spun the player around the object forever.
            if (ropeTaut && Mathf.Abs(horizontalInput) > 0.01f && radial.y < 0f)
            {
                // Below the anchor this tangent always points right, so D always pushes right.
                Vector2 tangent = new Vector2(-radial.y, radial.x);
                float tangentSpeed = Vector2.Dot(velocity, tangent);
                bool braking = tangentSpeed * horizontalInput < 0f;

                if (braking || SwingEnergyBelowLimit(body, velocity, toBody.y))
                {
                    velocity += tangent * (horizontalInput * ctx.SwingAcceleration * fixedDeltaTime);
                }
            }

            body.linearVelocity = Vector2.ClampMagnitude(velocity, ctx.MaxSwingSpeed);

            // Pull the body back onto the rope. The length is fixed at attach time: holding Fire
            // no longer reels the rope in.
            if (distance > ctx.RopeRestLength)
            {
                body.position = anchorPoint + radial * ctx.RopeRestLength;
            }
        }

        /// <summary>
        /// True while the swing's energy is below what it takes to reach MaxSwingAngle, so input can
        /// build the swing up to that angle and never past it (and never into a full loop).
        /// </summary>
        private bool SwingEnergyBelowLimit(Rigidbody2D body, Vector2 velocity, float heightBelowAnchor)
        {
            float gravity = Mathf.Abs(Physics2D.gravity.y * body.gravityScale);
            float ropeLength = ctx.RopeRestLength;
            float heightAboveLowestPoint = ropeLength + heightBelowAnchor;
            float energy = 0.5f * velocity.sqrMagnitude + gravity * heightAboveLowestPoint;
            float maxEnergy = gravity * ropeLength * (1f - Mathf.Cos(ctx.MaxSwingAngle * Mathf.Deg2Rad));
            return energy < maxEnergy;
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
