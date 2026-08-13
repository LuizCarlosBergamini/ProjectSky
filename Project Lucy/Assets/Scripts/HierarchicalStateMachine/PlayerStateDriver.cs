using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

namespace HierarchicalStateMachine
{
    public class PlayerStateDriver : MonoBehaviour
    {
        PlayerContext ctx = new PlayerContext();
        public Transform groundCheck;
        public float groundCheckRadius = 1f;
        public LayerMask groundMask;
        public bool drawGizmos = true;
        public Animator animator;
        public PlayerData Data;
        [Range(0f, 1f)] public float jumpCutVelocityMultiplier = 0.5f;
        public bool isFacingRightLocal;

        [Header("Collision Assists")]
        [SerializeField] private Collider2D bodyCollider;
        [SerializeField] private float groundCastDistance = 0.08f;
        [SerializeField] private float groundSnapDistance = 0.12f;
        [SerializeField] private float groundSkinWidth = 0.03f;
        [SerializeField] private float minGroundNormalY = 0.65f;
        [SerializeField] private float upperCornerCheckDistance = 0.08f;
        [SerializeField] private float upperCornerCorrectionMax = 0.18f;
        [SerializeField] private int upperCornerCorrectionSteps = 4;
        
        [SerializeField] CameraFollowObject CameraFollowObject;
        
        private string lastPath;
        private readonly RaycastHit2D[] groundHits = new RaycastHit2D[8];
        
        Rigidbody2D rb;
        StateMachine sm;
        State root;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            if (bodyCollider == null) bodyCollider = GetComponent<Collider2D>();
            ctx.rb = rb;
            // _grappleController = GetComponent<GrappleController>();
            ctx.playerActions = new PlayerInputs().InGame;
            ctx.animator = this.animator;
            ctx.Data = this.Data;
            ctx.Jump = Jump;
            ctx.JumpCut = JumpCut;
            ctx.AttackInputBufferTime = ctx.Data.attackInputBufferTime;
            ctx.ComboGravityMultiplier = ctx.Data.comboGravityMultiplier;
            ctx.MaxComboStep = ctx.Data.maxComboStep;
            this.isFacingRightLocal = ctx.isFacingRight;
            
            
            // Initialize state machine
            root = new PlayerRoot(null, ctx);
            var builder = new StateMachineBuilder(root);
            sm = builder.Build();
        }

        private void OnEnable()
        {
            // ctx.playerActions.Get().actionTriggered += OnAnyActionTriggered;
            ctx.playerActions.Enable();
        }

        private void OnDisable()
        {
            // ctx.playerActions.Get().actionTriggered -= OnAnyActionTriggered;
            ctx.playerActions.Disable();
        }

        private void Update()
        {
            ctx.MovementInput = ctx.playerActions.Movement.ReadValue<Vector2>();
            ctx.IsGrounded = CheckGrounded();
            
            if (ctx.playerActions.Jump.WasPressedThisFrame())
            {
                ctx.LastPressedJumpTime = ctx.Data.jumpInputBufferTime;
            }

            if (ctx.playerActions.Attack.WasPressedThisFrame())
            {
                ctx.LastPressedAttackTime = ctx.AttackInputBufferTime;

                if (ctx.CanReceiveComboInput)
                {
                    ctx.ComboQueued = true;
                }
            }

            if (ctx.IsGrounded) ctx.LastOnGroundTime = ctx.Data.coyoteTime;
            else ctx.LastOnGroundTime -= Time.deltaTime;
            
            sm.Tick(Time.deltaTime);

            var path = StatePath(sm.Root.Leaf());
            if (path != lastPath)
            {
                Debug.Log("path: " + path);
                lastPath = path;
            }
            
            ctx.LastPressedJumpTime = Mathf.Max(0f, ctx.LastPressedJumpTime - Time.deltaTime);
            ctx.LastPressedAttackTime = Mathf.Max(0f, ctx.LastPressedAttackTime - Time.deltaTime);
            ctx.AttackCooldownTime = Mathf.Max(0f, ctx.AttackCooldownTime - Time.deltaTime);
            ApplyGravity();
        }

        private void ApplyGravity()
        {
            if (ctx.WantsComboGravity)
            {
                SetGravityScale(ctx.Data.gravityScale * ctx.ComboGravityMultiplier);
                return;
            }
            
            if (rb.linearVelocityY < 0 && ctx.MovementInput.y < 0)
            {
                //Much higher gravity if holding down
                SetGravityScale(ctx.Data.gravityScale * ctx.Data.fastFallGravityMult);
                //Caps maximum fall speed, so when falling over large distances we don't accelerate to insanely high speeds
                rb.linearVelocity =
                    new Vector2(rb.linearVelocityX, Mathf.Max(rb.linearVelocityY, -ctx.Data.maxFastFallSpeed));
            }
            else if (ctx.isJumpCut && rb.linearVelocityY > 0)
            {
                //Higher gravity if jump button released
                SetGravityScale(ctx.Data.gravityScale * ctx.Data.jumpCutGravityMult);
            }
            else if ((ctx.IsJumping || ctx.isJumpFalling) && Mathf.Abs(rb.linearVelocityY) < ctx.Data.jumpHangTimeThreshold)
            {
                SetGravityScale(ctx.Data.gravityScale * ctx.Data.jumpHangGravityMult);
            }
            else if (rb.linearVelocityY < 0)
            {
                //Higher gravity if falling
                SetGravityScale(ctx.Data.gravityScale * ctx.Data.fallGravityMult);
                //Caps maximum fall speed, so when falling over large distances we don't accelerate to insanely high speeds
                rb.linearVelocity = new Vector2(rb.linearVelocityX, Mathf.Max(rb.linearVelocityY, -ctx.Data.maxFallSpeed));
            }
            else
            {
                //Default gravity if standing on a platform or moving upwards
                SetGravityScale(ctx.Data.gravityScale);
            }
        }
        
        public void Jump()
        {
            ctx.IsJumping = true;
            ctx.isJumpCut = false;
            ctx.isJumpFalling = false;
            //Ensures we can't call Jump multiple times from one press
            ctx.LastPressedJumpTime = 0;
            ctx.LastOnGroundTime = 0;

            float force = ctx.Data.jumpForce;
            if (ctx.rb.linearVelocityY < 0)
                force -= ctx.rb.linearVelocityY;
        
            ctx.animator.Play("Player_Jump");
            ctx.rb.AddForce(Vector2.up * force, ForceMode2D.Impulse);
        }

        public void JumpCut()
        {
            if (rb.linearVelocityY <= 0f) return;

            ctx.isJumpCut = true;
            rb.linearVelocity = new Vector2(rb.linearVelocityX, rb.linearVelocityY * jumpCutVelocityMultiplier);
        }
        
        public void SetGravityScale(float scale)
        {
            rb.gravityScale = scale;
        }

        public void OpenComboWindow()
        {
            ctx.CanReceiveComboInput = true;

            if (ctx.LastPressedAttackTime > 0f)
            {
                ctx.ComboQueued = true;
            }
        }

        public void CloseComboWindow()
        {
            ctx.CanReceiveComboInput = false;
        }

        public void FinishAttack()
        {
            ctx.AttackFinished = true;
        }

        public void FixedUpdate()
        {
            RunFunction(1);
            // ApplyGroundEdgeSnap();
            // ApplyUpperCornerCorrection();
        }

        private bool CheckGrounded()
        {
            if (bodyCollider == null) return groundCheck != null && Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundMask);

            ContactFilter2D filter = CreateGroundFilter();
            int hitCount = bodyCollider.Cast(Vector2.down, filter, groundHits, groundCastDistance);

            for (int i = 0; i < hitCount; i++)
            {
                if (groundHits[i].normal.y >= minGroundNormalY)
                    return true;
            }

            return false;
        }

        private void ApplyGroundEdgeSnap()
        {
            if (bodyCollider == null || rb.linearVelocityY > 0f || CheckGrounded()) return;

            ContactFilter2D filter = CreateGroundFilter();
            int hitCount = bodyCollider.Cast(Vector2.down, filter, groundHits, groundSnapDistance);

            for (int i = 0; i < hitCount; i++)
            {
                if (groundHits[i].normal.y < minGroundNormalY) continue;

                rb.position += Vector2.down * groundHits[i].distance;
                rb.linearVelocity = new Vector2(rb.linearVelocityX, Mathf.Max(rb.linearVelocityY, 0f));
                ctx.IsGrounded = true;
                ctx.LastOnGroundTime = ctx.Data.coyoteTime;
                return;
            }
        }

        private void ApplyUpperCornerCorrection()
        {
            if (bodyCollider == null || rb.linearVelocityY <= 0f) return;
            if (!WouldHitAtOffset(Vector2.zero, Vector2.up, upperCornerCheckDistance)) return;

            int steps = Mathf.Max(1, upperCornerCorrectionSteps);
            float step = upperCornerCorrectionMax / steps;

            for (int i = 1; i <= steps; i++)
            {
                float amount = step * i;
                Vector2 preferredDirection = ctx.MovementInput.x >= 0f ? Vector2.right : Vector2.left;
                Vector2 oppositeDirection = -preferredDirection;

                if (!WouldHitAtOffset(preferredDirection * amount, Vector2.up, upperCornerCheckDistance))
                {
                    rb.position += preferredDirection * amount;
                    return;
                }

                if (!WouldHitAtOffset(oppositeDirection * amount, Vector2.up, upperCornerCheckDistance))
                {
                    rb.position += oppositeDirection * amount;
                    return;
                }
            }
        }

        private bool WouldHitAtOffset(Vector2 offset, Vector2 direction, float distance)
        {
            Bounds bounds = bodyCollider.bounds;
            Vector2 center = (Vector2)bounds.center + offset;
            Vector2 size = new Vector2(
                Mathf.Max(0.01f, bounds.size.x - groundSkinWidth * 2f),
                Mathf.Max(0.01f, bounds.size.y - groundSkinWidth * 2f));

            RaycastHit2D hit = Physics2D.BoxCast(center, size, 0f, direction, distance, groundMask);
            return hit.collider != null;
        }

        private ContactFilter2D CreateGroundFilter()
        {
            ContactFilter2D filter = new ContactFilter2D();
            filter.SetLayerMask(groundMask);
            filter.useTriggers = false;
            return filter;
        }

        #region RUN METHODS

        private void RunFunction(float lerpAmount)
        {
            if (!ctx.canWalk) return;

            //Calculate the direction we want to move in and our desired velocity
            float targetSpeed = ctx.MovementInput.x * ctx.Data.runMaxSpeed;
            //We can reduce control using Lerp() this smooths changes to direction and speed
            targetSpeed = Mathf.Lerp(rb.linearVelocityX, targetSpeed, lerpAmount);

            #region Calculate AccelRate

            float accelRate;

            //Gets an acceleration value based on if we are accelerating (includes turning) 
            //or trying to decelerate (stop). As well as applying a multiplier if we're airborne.
            if (ctx.LastOnGroundTime > 0)
                accelRate = (Mathf.Abs(targetSpeed) > 0.01f) ? ctx.Data.runAccelAmount : ctx.Data.runDeccelAmount;
            else
                accelRate = (Mathf.Abs(targetSpeed) > 0.01f)
                    ? ctx.Data.runAccelAmount * ctx.Data.accelInAir
                    : ctx.Data.runDeccelAmount * ctx.Data.deccelInAir;

            #endregion

            #region Add Bonus Jump Apex Acceleration

            //Increase acceleration and maxSpeed when at the apex of their jump, makes the jump feel a bit more bouncy, responsive and natural
            if ((ctx.IsJumping || ctx.isJumpFalling) && Mathf.Abs(rb.linearVelocityY) < ctx.Data.jumpHangTimeThreshold)
            {
                accelRate *= ctx.Data.jumpHangAccelerationMult;
                targetSpeed *= ctx.Data.jumpHangMaxSpeedMult;
            }

            #endregion

            #region Conserve Momentum

            //We won't slow the player down if they are moving in their desired direction but at a greater speed than their maxSpeed
            if (ctx.Data.doConserveMomentum && Mathf.Abs(rb.linearVelocityX) > Mathf.Abs(targetSpeed) &&
                Mathf.Sign(rb.linearVelocityX) == Mathf.Sign(targetSpeed) && Mathf.Abs(targetSpeed) > 0.01f &&
                ctx.LastOnGroundTime < 0)
            {
                //Prevent any deceleration from happening, or in other words conserve are current momentum
                //You could experiment with allowing for the player to slightly increae their speed whilst in this "state"
                accelRate = 0;
            }

            #endregion

            //Calculate difference between current velocity and desired velocity
            float speedDif = targetSpeed - rb.linearVelocityX;
            //Calculate force along x-axis to apply to thr player
            float movementLocal = speedDif * accelRate;

            //Convert this to a vector and apply to rigidbody
            rb.AddForce(movementLocal * Vector2.right, ForceMode2D.Force);
            TurnCheck();
        }

        public void TurnCheck()
        {
            // changes direction of player based on movement
            if (ctx.MovementInput.x > 0 && ctx.isFacingRight) Turn();
            else if (ctx.MovementInput.x < 0 && !ctx.isFacingRight) Turn();
        }
        
        #endregion
        
        private void Turn()
        {
            if (ctx.isFacingRight)
            {
                ctx.isFacingRight = !ctx.isFacingRight;
                this.isFacingRightLocal = ctx.isFacingRight;
                transform.rotation = Quaternion.Euler(transform.rotation.x, 0f, transform.rotation.y);
                // Turn the camera object to smooth the movement
                CameraFollowObject.CallTurn();
            }
            else
            {
                ctx.isFacingRight = !ctx.isFacingRight;
                this.isFacingRightLocal = ctx.isFacingRight;
                transform.rotation = Quaternion.Euler(transform.rotation.x, 180f, transform.rotation.y);
                // Turn the camera object to smooth the movement
                CameraFollowObject.CallTurn();
            }
        }
        

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            
            if (groundCheck != null)
            {
                Gizmos.color = Color.gray;
                Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
            }

            Collider2D gizmoCollider = bodyCollider != null ? bodyCollider : GetComponent<Collider2D>();
            if (gizmoCollider == null) return;

            Bounds bounds = gizmoCollider.bounds;
            Vector3 castSize = new Vector3(
                Mathf.Max(0.01f, bounds.size.x - groundSkinWidth * 2f),
                Mathf.Max(0.01f, bounds.size.y - groundSkinWidth * 2f),
                0f);

            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(bounds.center + Vector3.down * groundCastDistance, castSize);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(bounds.center + Vector3.down * groundSnapDistance, castSize);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(bounds.center + Vector3.up * upperCornerCheckDistance, castSize);
        }

        static string StatePath(State s)
        {
            return string.Join(" > ", s.PathToRoot().Reverse().Select(x => x.GetType().Name));
        }
    }

    [System.Serializable]
    public class PlayerContext
    {
        public Rigidbody2D rb;
        public Animator animator;
        public bool IsGrounded;
        public PlayerInputs.InGameActions playerActions;
        public bool isDead;
        public bool IsJumping;
        public bool isJumpCut;
        public bool isJumpFalling;
        public float moveSpeed = 10f;
        public PlayerData Data;
        public Vector2 MovementInput;
        public float LastOnGroundTime;
        public float LastPressedJumpTime;
        public float health = 100f;
        public bool canWalk = true;
        public bool isFacingRight = true;
        public Action Jump;
        public Action JumpCut;
        public bool IsAttacking = false;
        public bool WantsComboGravity = false;
        public int ComboStep;
        public float LastPressedAttackTime;
        public float AttackInputBufferTime;
        public float ComboGravityMultiplier;
        public int MaxComboStep;
        public bool CanReceiveComboInput;
        public bool ComboQueued;
        public bool AttackFinished;
        public float AttackCooldownTime;
        public Action StartAttackHitbox;
        public Action StopAttackHitbox;
    }
}

