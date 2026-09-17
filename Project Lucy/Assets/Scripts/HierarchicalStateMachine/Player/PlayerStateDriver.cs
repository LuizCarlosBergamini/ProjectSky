using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

namespace HierarchicalStateMachine
{
    public class PlayerStateDriver : MonoBehaviour, IDamageable
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

        [Header("Health")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float invulnerabilityTime = 0.4f;
        [SerializeField] private float hitAnimationTime = 0.25f;

        [Header("Attack")]
        [SerializeField] private Transform attackTransform;
        [SerializeField] private float attackRadius = 1f;
        [SerializeField] private LayerMask attackableLayer;
        [SerializeField] private float attackDamage = 10f;
        [SerializeField] private float attackKnockbackForce = 10f;
        [SerializeField] private float attackVerticalKnockback = 0.5f;

        [Header("Grappling")]
        [SerializeField] private LineRenderer ropeRenderer;
        [SerializeField] private LayerMask grappleableLayer = 1 << 6;
        [SerializeField] private AudioClip grapplingAudioClip;
        [SerializeField] private float springConstant = 150f;
        [SerializeField] private float dampingCoefficient = 5f;
        [SerializeField] private float maxGrappleDistance = 5f;
        [SerializeField] private float playerGrappleRadius = 5f;
        [SerializeField] private float minRopeLength;
        [SerializeField] private float reelSpeed = 10f;
        [SerializeField] private float swingForce = 100f;
        [SerializeField] private float releaseJumpForce = 30f;
        
        private string lastPath;
        private float invulnerabilityTimer;
        private float hitAnimationTimer;

        // Cached from UpgradeManager. The serialized fields and PlayerData stay the untouched base values:
        // PlayerData is a shared asset, writing a bonus into it would leak into the project in the editor.
        private float damageBonus;
        private float maxHealthBonus;
        private float moveSpeedBonus;

        public float AttackDamage => attackDamage + damageBonus;
        public float MaxHealth => maxHealth + maxHealthBonus;
        public float RunMaxSpeed => ctx.Data.runMaxSpeed + moveSpeedBonus;
        public float CurrentHealth => ctx.health;

        public bool HasTakenDamage { get; set; }
        private readonly RaycastHit2D[] groundHits = new RaycastHit2D[8];
        
        Rigidbody2D rb;
        StateMachine sm;
        State root;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            if (bodyCollider == null) bodyCollider = GetComponent<Collider2D>();
            if (ropeRenderer == null) ropeRenderer = GetComponent<LineRenderer>();
            ctx.rb = rb;
            ctx.playerActions = new PlayerInputs().InGame;
            ctx.animator = this.animator;
            ctx.Data = this.Data;
            ctx.Jump = Jump;
            ctx.JumpCut = JumpCut;
            ctx.PlayAnimation = PlayStateAnimation;
            ctx.AttackInputBufferTime = ctx.Data.attackInputBufferTime;
            ctx.ComboGravityMultiplier = ctx.Data.comboGravityMultiplier;
            ctx.MaxComboStep = ctx.Data.maxComboStep;
            this.isFacingRightLocal = ctx.isFacingRight;
            ctx.playerTransform = transform;
            ctx.ropeRenderer = ropeRenderer;
            ctx.grappleableLayer = grappleableLayer;
            ctx.grapplingAudioClip = grapplingAudioClip;
            ctx.SpringConstant = springConstant;
            ctx.DampingCoefficient = dampingCoefficient;
            ctx.MaxGrappleDistance = maxGrappleDistance;
            ctx.PlayerGrappleRadius = playerGrappleRadius;
            ctx.MinRopeLength = minRopeLength;
            ctx.ReelSpeed = reelSpeed;
            ctx.SwingForce = swingForce;
            ctx.ReleaseJumpForce = releaseJumpForce;
            ctx.DefaultDrag = rb.linearDamping;
            ctx.health = maxHealth;

            if (ctx.ropeRenderer != null)
            {
                ctx.ropeRenderer.positionCount = 2;
                // The rope is fed world-space points, and the player transform flips 180° on turn,
                // which would mirror the rope if the renderer used local space.
                ctx.ropeRenderer.useWorldSpace = true;
                ctx.ropeRenderer.enabled = false;
            }
            
            
            // Initialize state machine
            root = new PlayerRoot(null, ctx);
            var builder = new StateMachineBuilder(root);
            sm = builder.Build();
        }

        private void OnEnable()
        {
            // ctx.playerActions.Get().actionTriggered += OnAnyActionTriggered;
            ctx.playerActions.Enable();
            UpgradeManager.OnUpgradesChanged += RefreshUpgradeBonuses;
            RefreshUpgradeBonuses();
        }

        private void OnDisable()
        {
            // ctx.playerActions.Get().actionTriggered -= OnAnyActionTriggered;
            ctx.playerActions.Disable();
            UpgradeManager.OnUpgradesChanged -= RefreshUpgradeBonuses;
        }

        private void Start()
        {
            // UpgradeManager can wake after this player in the same scene, so read the bonuses again
            // once every Awake has run.
            RefreshUpgradeBonuses();
        }

        /// <summary>
        /// Blocks or restores gameplay input, e.g. while a menu is open. Time.timeScale = 0 alone is not
        /// enough: Update still runs and would buffer a jump or an attack for the moment the menu closes.
        /// </summary>
        public void SetInputEnabled(bool inputEnabled)
        {
            if (inputEnabled)
            {
                if (isActiveAndEnabled) ctx.playerActions.Enable();
                return;
            }

            ctx.playerActions.Disable();
            ctx.MovementInput = Vector2.zero;
            ctx.LastPressedJumpTime = 0f;
            ctx.LastPressedAttackTime = 0f;
        }

        private void RefreshUpgradeBonuses()
        {
            float previousMaxHealth = MaxHealth;

            UpgradeManager upgrades = UpgradeManager.instance;
            damageBonus = upgrades != null ? upgrades.GetBonus(UpgradeStat.Damage) : 0f;
            maxHealthBonus = upgrades != null ? upgrades.GetBonus(UpgradeStat.MaxHealth) : 0f;
            moveSpeedBonus = upgrades != null ? upgrades.GetBonus(UpgradeStat.MoveSpeed) : 0f;

            if (ctx.isDead) return;

            // Extra max health arrives filled, so buying it is felt immediately. A lower max (reset)
            // only clamps, it never heals.
            float gained = MaxHealth - previousMaxHealth;
            if (gained > 0f) ctx.health += gained;
            ctx.health = Mathf.Min(ctx.health, MaxHealth);
        }

        private void Update()
        {
            ctx.MovementInput = ctx.playerActions.Movement.ReadValue<Vector2>();
            ctx.IsGrounded = CheckGrounded();
            UpdateDamageTimers(Time.deltaTime);

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
            if (ctx.IsGrappling)
            {
                // Plain gravity while swinging: the fall multipliers and the maxFallSpeed clamp
                // would eat the momentum the player builds at the bottom of the arc.
                SetGravityScale(ctx.Data.gravityScale);
                return;
            }

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
        
            PlayStateAnimation("Player_Jump");
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

        #region IDamageable

        public void TakeDamage(float amount, Vector2 knockback)
        {
            if (HasTakenDamage || ctx.isDead || ctx.health <= 0f) return;

            HasTakenDamage = true;
            invulnerabilityTimer = invulnerabilityTime;
            ctx.health -= amount;

            Debug.Log("health " + ctx.health);

            rb.AddForce(knockback, ForceMode2D.Impulse);
            CancelAttack();
            PlayHitAnimation();

            if (ctx.health > 0f) return;
            Die();
        }

        // The hit clip replaces the attack clip, so the attack animation never reaches its
        // StopAttacking event and FinishAttack is never called. Attack.OnUpdate would then wait
        // on AttackFinished forever and the state machine would be stuck in Attack. Ending the
        // attack here is what makes a hit interrupt it cleanly.
        private void CancelAttack()
        {
            if (!ctx.IsAttacking) return;

            ctx.ComboQueued = false;       // otherwise Attack starts the next combo step instead
            ctx.CanReceiveComboInput = false;
            ctx.AttackFinished = true;     // lets Attack.OnUpdate reach shouldExit = true
        }

        public bool IsAlive() => ctx.health > 0f;

        public void Heal(int amount)
        {
            ctx.health = Mathf.Min(ctx.health + amount, MaxHealth);
        }

        private void Die()
        {
            hitAnimationTimer = 0f;
            ctx.isDead = true;
            ctx.canWalk = false;

            if (GameManager.instance != null) GameManager.instance.GameOver();
        }

        // Movement states route their clip through here. Knockback throws the player off the
        // ground, so Grounded -> Airborne fires the frame after a hit and Airborne.OnEnter would
        // otherwise replace Player_hitted with Player_Jump after a single frame.
        // The attack clip deliberately does NOT go through this: its animation events drive
        // FinishAttack, so suppressing it would leave the Attack state stuck forever.
        private void PlayStateAnimation(string stateName)
        {
            if (animator == null || string.IsNullOrEmpty(stateName)) return;
            if (hitAnimationTimer > 0f) return;

            int hash = Animator.StringToHash(stateName);
            if (!animator.HasState(0, hash)) return;
            if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == hash) return;

            animator.Play(hash, 0);
        }

        private void PlayHitAnimation()
        {
            if (ctx.animator == null) return;

            int hash = Animator.StringToHash("Player_hitted");
            if (!ctx.animator.HasState(0, hash)) return;

            hitAnimationTimer = hitAnimationTime;
            // Restarted from frame 0 so consecutive hits re-trigger the reaction.
            ctx.animator.Play(hash, 0, 0f);
        }

        private void UpdateDamageTimers(float deltaTime)
        {
            if (invulnerabilityTimer > 0f)
            {
                invulnerabilityTimer -= deltaTime;
                if (invulnerabilityTimer <= 0f) HasTakenDamage = false;
            }

            if (hitAnimationTimer <= 0f) return;

            hitAnimationTimer -= deltaTime;
            if (hitAnimationTimer <= 0f) RestoreStateAnimation();
        }

        // Player_hitted has no exit transition in the controller, and the movement states only
        // set their clip in OnEnter, so without this the player would stay stuck in the hit pose
        // until they happened to change state.
        private void RestoreStateAnimation()
        {
            if (ctx.animator == null || ctx.isDead || ctx.IsAttacking) return;
        
            if (!ctx.IsGrounded)
            {
                ctx.animator.Play(rb.linearVelocityY > 0f ? "Player_Jump" : "Player_Fall");
                return;
            }
        
            ctx.animator.Play(Mathf.Abs(ctx.MovementInput.x) > 0.01f ? "Player_Waking" : "Player_Idle");
        }

        #endregion

        // Driven by the DealDamage animation event on each Player_Attack clip, so the hit lands
        // on the frame the swing connects rather than when the state starts.
        public void DealAttackDamage()
        {
            Vector2 center = attackTransform != null ? (Vector2)attackTransform.position : rb.position;
            Collider2D[] hits = Physics2D.OverlapCircleAll(center, attackRadius, attackableLayer);

            for (int i = 0; i < hits.Length; i++)
            {
                IDamageable damageable = hits[i].GetComponentInParent<IDamageable>();
                if (damageable == null) continue;
                // HasTakenDamage is the victim's own invulnerability window, and it also stops a
                // single swing hitting the same enemy once per overlapping collider.
                if (!damageable.IsAlive() || damageable.HasTakenDamage) continue;

                Vector2 direction = new Vector2(
                    ctx.isFacingRight ? 1f : -1f,
                    Mathf.Abs(attackVerticalKnockback)).normalized;

                damageable.TakeDamage(AttackDamage, direction * attackKnockbackForce);
            }
        }

        public void FixedUpdate()
        {
            sm.FixedTick(Time.fixedDeltaTime);
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
            if (!ctx.canWalk)
            {
                // Still let the sprite face the input direction (e.g. while swinging).
                TurnCheck();
                return;
            }

            //Calculate the direction we want to move in and our desired velocity
            float targetSpeed = ctx.MovementInput.x * RunMaxSpeed;
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

            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, playerGrappleRadius);

            if (attackTransform != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(attackTransform.position, attackRadius);
            }
            
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
        public Action<string> PlayAnimation;
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
        public Transform playerTransform;
        public LineRenderer ropeRenderer;
        public LayerMask grappleableLayer;
        public AudioClip grapplingAudioClip;
        public float SpringConstant;
        public float DampingCoefficient;
        public float MaxGrappleDistance;
        public float PlayerGrappleRadius;
        public float MinRopeLength;
        public float ReelSpeed;
        public float SwingForce;
        public float ReleaseJumpForce;
        public float DefaultDrag;
        public bool IsGrappling;
        public Vector2 GrapplePoint;
        public GrappleableTarget GrappleTarget;
        public GameObject GrappledObject;
        public Rigidbody2D ForceTarget;
        public bool PullTargetIsPlayer;
        public float RopeRestLength;
        public float RopeRestLengthFixed;

        public bool TryStartGrapple()
        {
            if (!TryFindGrappleTarget(out GrappleableTarget target)) return false;

            GrappleTarget = target;
            GrappledObject = target.gameObject;
            GrapplePoint = target.GetAnchorPoint();
            IsGrappling = true;
            return true;
        }

        public void ClearGrapple()
        {
            IsGrappling = false;
            GrappleTarget = null;
            GrappledObject = null;
            GrapplePoint = Vector2.zero;
            ForceTarget = null;
            PullTargetIsPlayer = false;
            RopeRestLength = 0f;
            RopeRestLengthFixed = 0f;

            if (ropeRenderer != null) ropeRenderer.enabled = false;
        }

        public void UpdateRopeVisuals()
        {
            if (ropeRenderer == null || !ropeRenderer.enabled || playerTransform == null) return;

            ropeRenderer.SetPosition(0, playerTransform.position);
            Vector3 anchorPoint = GrapplePoint;

            if (!PullTargetIsPlayer && GrappledObject != null)
            {
                anchorPoint = GrappledObject.transform.position;
            }

            ropeRenderer.SetPosition(1, anchorPoint);
        }

        private bool TryFindGrappleTarget(out GrappleableTarget target)
        {
            target = null;
            if (rb == null) return false;

            float playerRadius = Mathf.Min(PlayerGrappleRadius, MaxGrappleDistance);
            if (playerRadius <= 0f) return false;

            var targets = GrappleableTarget.Active;
            float bestDistanceSqr = float.PositiveInfinity;

            for (int i = 0; i < targets.Count; i++)
            {
                GrappleableTarget candidate = targets[i];
                if (candidate == null) continue;
                if ((grappleableLayer.value & (1 << candidate.gameObject.layer)) == 0) continue;

                Vector2 anchorPoint = candidate.GetAnchorPoint();
                float distanceSqr = ((Vector2)rb.position - anchorPoint).sqrMagnitude;
                float allowedDistance = playerRadius + candidate.grappleRadius;
                if (distanceSqr > allowedDistance * allowedDistance) continue;

                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    target = candidate;
                }
            }

            return target != null;
        }
    }
}

