using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace HierarchicalStateMachine
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class EnemyStateDriver : MonoBehaviour, IDamageable
    {
        [SerializeField] private EnemyScriptableObject data;
        [SerializeField] private Animator animator;

        [Tooltip("Preencha apenas em chefes: nome, retrato e recompensa mostrados na barra de vida do chefe.")]
        [SerializeField] private BossData_SO bossData;

        [Header("Attacks")]
        [Tooltip("Ataques e regras de escolha (alcance, recarga, pesos, fases). Vazio = o ataque corpo a corpo " +
                 "e o projetil antigos, montados a partir dos campos Attack e Ranged Attack abaixo.")]
        [SerializeField] private EnemyAttackSet attackSet;

        [Header("Awareness")]
        [SerializeField] private string targetTag = "Player";
        [SerializeField] private float detectionRange = 12f; // the boss room radius
        [SerializeField] private float attackRange = 2f;
        [SerializeField] private float stopDistance = 1.6f;

        [Tooltip("Chefes: fica parado ate o BossGate fechar a arena (BossGate.OnBossFightStarted).")]
        [SerializeField] private bool waitForActivation;

        [Header("Attack (sem Attack Set)")]
        [SerializeField] private float attackDuration = 0.8f;
        [SerializeField] private float attackHitTime = 0.45f; // when inside the attack damage lands
        [SerializeField] private float attackCooldown = 1.2f;
        [SerializeField] private float verticalKnockback = 0.5f;
        // Total cone width in front of the enemy. Facing is locked when the attack starts, so a
        // player who jumps over mid-swing leaves the cone and is missed.
        [Range(10f, 360f)] [SerializeField] private float attackConeAngle = 100f;

        [Header("Ranged Attack")]
        [SerializeField] private EnemyProjectile projectilePrefab;
        [Tooltip("Origem dos projeteis e do olhar; tambem e o ponto de onde a linha de visao e testada.")]
        [SerializeField] private Transform projectileSpawnPoint;
        [SerializeField] private float projectileSpeed = 12f;
        [SerializeField] private float rangedAttackDuration = 0.9f;
        [SerializeField] private float rangedAttackFireTime = 0.45f; // when the projectile leaves

        [Header("Ground Check")]
        [SerializeField] private Transform groundCheck;
        [SerializeField] private float groundCheckRadius = 0.25f;
        [SerializeField] private LayerMask groundLayer;

        [Header("Damage")]
        [SerializeField] private float invulnerabilityTime = 0.15f;
        [SerializeField] private float hitAnimationTime = 0.2f;
        [SerializeField] private float deathDelay = 0.4f;

        [Tooltip("Durante um ataque, levar dano so pisca o sprite em vez de tocar Enemy_Hit, que apagaria a " +
                 "animacao do golpe. O ataque nunca e interrompido, com ou sem esta opcao.")]
        [SerializeField] private bool superArmorWhileAttacking;

        [Header("Feedback")]
        [Tooltip("Sprite que recebe o piscar de dano e o telegrafo. Vazio = o do Animator.")]
        [SerializeField] private SpriteRenderer tintRenderer;
        [SerializeField] private Color hitFlashColor = new Color(1f, 0.35f, 0.35f, 1f);
        [SerializeField] private float hitFlashTime = 0.1f;
        [Tooltip("Pulsos por segundo da cor de telegrafo.")]
        [SerializeField] private float telegraphPulseRate = 6f;

        [Header("Events")]
        [Tooltip("Disparado quando a animacao de morte termina, logo antes do objeto ser destruido.")]
        [SerializeField] private UnityEvent onDeath;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private bool logStatePath;
        [Tooltip("Mostra no Console a nota de cada ataque sempre que um e escolhido.")]
        [SerializeField] private bool logAttackDecisions;

        private readonly EnemyContext ctx = new EnemyContext();
        private readonly RaycastHit2D[] castHits = new RaycastHit2D[8];
        private Rigidbody2D rb;
        private Collider2D bodyCollider;
        private StateMachine sm;
        private State root;
        private EnemyAttackSet runtimeSet; // legacy set built in Awake, destroyed with this enemy
        private float invulnerabilityTimer;
        private float hitAnimationTimer;
        private string lastPath;

        private Transform target;
        private Collider2D targetCollider;
        private Rigidbody2D targetBody;
        private IDamageable targetDamageable;
        private PlayerStateDriver targetPlayer;

        private Color baseTint = Color.white;
        private Color? telegraphColor;
        private float telegraphTime;
        private float hitFlashTimer;
        private bool tinted;

        public bool HasTakenDamage { get; set; }

        /// <summary>
        /// Raised once when this enemy finishes dying, just before the object is destroyed.
        /// Code-side counterpart of <see cref="onDeath"/>, so listeners can subscribe without
        /// being wired in the Inspector (a scene object cannot reference a spawned enemy).
        /// </summary>
        public event Action Died;

        /// <summary>Raised with (current, max) whenever this enemy takes damage or heals.</summary>
        public event Action<float, float> HealthChanged;

        /// <summary>True from the moment the death animation starts.</summary>
        public bool IsDead => ctx.IsDead;

        public float CurrentHealth => ctx.Health;
        public float MaxHealth => data != null ? data.maxHealth : 0f;

        /// <summary>Display data when this enemy is a boss; null for regular enemies.</summary>
        public BossData_SO BossData => bossData;

        /// <summary>The attacks this enemy actually runs (the asset, or the runtime legacy set).</summary>
        public EnemyAttackSet AttackSet => ctx.AttackSet;

        /// <summary>Current fight phase (index into AttackSet.phases).</summary>
        public int Phase => ctx.Phase;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            bodyCollider = GetComponent<Collider2D>();
            animator = ResolveAnimator();
            if (tintRenderer == null && animator != null) tintRenderer = animator.GetComponent<SpriteRenderer>();
            if (tintRenderer != null) baseTint = tintRenderer.color;

            ctx.rb = rb;
            ctx.animator = animator;
            ctx.self = transform;
            ctx.Data = data;
            ctx.Health = data.maxHealth;
            ctx.MaxHealth = data.maxHealth;
            ctx.MoveSpeed = data.moveSpeed;
            ctx.StopDistance = stopDistance;
            ctx.DeathDelay = deathDelay;
            ctx.Activated = !waitForActivation;
            ctx.LogDecisions = logAttackDecisions;
            if (attackSet == null) runtimeSet = EnemyAttackSet.CreateLegacy(LegacySettings());
            ctx.AttackSet = attackSet != null ? attackSet : runtimeSet;
            ApplyPhase(0, announce: false);

            // Rotation y == 0 faces right, 180 faces left (same convention as the old EnemyPatrol).
            ctx.FacingRight = Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, 0f)) < 90f;

            ctx.PlayAnimation = PlayAnimation;
            ctx.HasAnimation = HasAnimation;
            ctx.PlayAttackAnimation = PlayAttackAnimation;
            ctx.SetAnimationSpeed = SetAnimationSpeed;
            ctx.SetTelegraph = SetTelegraph;
            ctx.ApplyHitbox = ApplyHitbox;
            ctx.FireProjectiles = FireProjectiles;
            ctx.ApplyGaze = ApplyGaze;
            ctx.FaceTarget = FaceTarget;
            ctx.CommitForwardOffset = CommitForwardOffset;
            ctx.IsPathBlocked = IsPathBlocked;
            ctx.OnDeathFinished = HandleDeathFinished;

            // Same bootstrap as PlayerStateDriver: the builder back-fills every State.Machine by
            // reflection, which is why the root can be constructed with a null machine.
            root = new EnemyRoot(null, ctx);
            sm = new StateMachineBuilder(root).Build();
        }

        private void OnEnable()
        {
            BossGate.OnBossFightStarted += HandleBossFightStarted;
        }

        private void OnDisable()
        {
            BossGate.OnBossFightStarted -= HandleBossFightStarted;
        }

        private void OnDestroy()
        {
            EnemyAttackSet.DestroyRuntime(runtimeSet);
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            ctx.Clock += deltaTime;
            ctx.LogDecisions = logAttackDecisions; // toggleable during Play Mode
            UpdateAwareness(deltaTime);
            UpdatePhase();
            ctx.IsGrounded = CheckGrounded();
            ctx.AttackCooldown = Mathf.Max(0f, ctx.AttackCooldown - deltaTime);

            if (invulnerabilityTimer > 0f)
            {
                invulnerabilityTimer -= deltaTime;
                if (invulnerabilityTimer <= 0f) HasTakenDamage = false;
            }

            if (hitAnimationTimer > 0f) hitAnimationTimer -= deltaTime;

            sm.Tick(deltaTime);
            TurnCheck();
            UpdateTint(deltaTime);

            if (!logStatePath) return;

            string path = StatePath(sm.Root.Leaf());
            if (path == lastPath) return;
            Debug.Log(name + ": " + path);
            lastPath = path;
        }

        private void FixedUpdate()
        {
            sm.FixedTick(Time.fixedDeltaTime);
            ApplyMovement();
        }

        /// <summary>Starts the fight for an enemy that waits for activation. Safe to call repeatedly.</summary>
        public void Activate()
        {
            ctx.Activated = true;
        }

        private void HandleBossFightStarted(EnemyStateDriver boss)
        {
            if (boss == this) Activate();
        }

        // The visible sprite can live on a child (as it does on the Player), and a disabled
        // Animator silently swallows every Play call - the child keeps showing its controller
        // default instead. Prefer whichever Animator is actually enabled.
        private Animator ResolveAnimator()
        {
            if (animator != null && animator.enabled) return animator;

            Animator[] candidates = GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (!candidates[i].enabled) continue;
                if (animator != null)
                    Debug.LogWarning(name + ": assigned Animator is disabled, using '" + candidates[i].name + "' instead", this);
                return candidates[i];
            }

            return animator;
        }

        private LegacyAttackSettings LegacySettings()
        {
            return new LegacyAttackSettings
            {
                attackRange = attackRange,
                attackDuration = attackDuration,
                attackHitTime = attackHitTime,
                attackCooldown = attackCooldown,
                attackConeAngle = attackConeAngle,
                verticalKnockback = verticalKnockback,
                damage = data.damage,
                knockback = data.knockbackForce,
                hasProjectile = projectilePrefab != null,
                projectileSpeed = projectileSpeed,
                rangedDuration = rangedAttackDuration,
                rangedFireTime = rangedAttackFireTime,
                detectionRange = detectionRange
            };
        }

        #region Awareness

        private void UpdateAwareness(float deltaTime)
        {
            if (target == null) FindTarget();

            ctx.Target = target;

            if (target == null)
            {
                ctx.TargetInDetectionRange = false;
                ctx.TimeTargetClose = 0f;
                return;
            }

            ctx.DistanceToTarget = Vector2.Distance(transform.position, target.position);
            ctx.TargetInDetectionRange = ctx.DistanceToTarget <= detectionRange;

            float dx = target.position.x - transform.position.x;
            ctx.HorizontalDistance = Mathf.Abs(dx);
            // Feet to feet, so standing on the same floor reads 0 whatever the two pivots are.
            ctx.TargetHeight = TargetFeetY() - SelfFeetY();

            ctx.TargetVelocity = targetBody != null ? targetBody.linearVelocity : Vector2.zero;
            ctx.TargetRetreating = Mathf.Abs(ctx.TargetVelocity.x) > 0.5f && Mathf.Sign(ctx.TargetVelocity.x) == Mathf.Sign(dx);

            if (targetPlayer != null)
            {
                ctx.TargetGrounded = targetPlayer.IsGrounded;
                ctx.TargetAttacking = targetPlayer.IsAttacking;
                ctx.TargetGrappling = targetPlayer.IsGrappling;
                ctx.TargetFacingSelf = targetPlayer.IsFacingRight == (dx < 0f);
            }
            else
            {
                ctx.TargetGrounded = true;
                ctx.TargetAttacking = false;
                ctx.TargetGrappling = false;
                ctx.TargetFacingSelf = true;
            }

            ctx.TargetPetrified = PetrifyStatus.IsActiveOn(target.gameObject);
            ctx.TargetInLineOfSight = HasLineOfSight(EyePosition());

            float closeRange = ctx.AttackSet != null ? ctx.AttackSet.closeRange : attackRange;
            ctx.TimeTargetClose = ctx.HorizontalDistance <= closeRange ? ctx.TimeTargetClose + deltaTime : 0f;
        }

        private void FindTarget()
        {
            GameObject found = GameObject.FindGameObjectWithTag(targetTag);
            target = found != null ? found.transform : null;
            targetCollider = found != null ? found.GetComponent<Collider2D>() : null;
            targetBody = found != null ? found.GetComponent<Rigidbody2D>() : null;
            targetDamageable = found != null ? found.GetComponent<IDamageable>() : null;
            targetPlayer = found != null ? found.GetComponent<PlayerStateDriver>() : null;
        }

        private Vector2 TargetCenter()
        {
            return targetCollider != null ? (Vector2)targetCollider.bounds.center : (Vector2)target.position;
        }

        private float TargetFeetY()
        {
            return targetCollider != null ? targetCollider.bounds.min.y : target.position.y;
        }

        private float SelfFeetY()
        {
            return bodyCollider != null ? bodyCollider.bounds.min.y : transform.position.y;
        }

        private Vector2 EyePosition()
        {
            if (projectileSpawnPoint != null) return projectileSpawnPoint.position;
            return bodyCollider != null ? (Vector2)bodyCollider.bounds.center : rb.position;
        }

        // Only level geometry (groundLayer) blocks the view; the player and the arena barrier do not.
        private bool HasLineOfSight(Vector2 from)
        {
            if (target == null) return false;
            return Physics2D.Linecast(from, TargetCenter(), groundLayer).collider == null;
        }

        private void UpdatePhase()
        {
            EnemyAttackSet set = ctx.AttackSet;
            if (set == null || set.phases.Count == 0 || ctx.MaxHealth <= 0f) return;

            int phase = set.GetPhaseIndex(Mathf.Clamp01(ctx.Health / ctx.MaxHealth));
            if (phase != ctx.Phase) ApplyPhase(phase, announce: true);
        }

        private void ApplyPhase(int phase, bool announce)
        {
            bool escalated = phase > ctx.Phase;
            ctx.Phase = phase;

            BossPhase phaseData = ctx.AttackSet != null ? ctx.AttackSet.GetPhase(phase) : null;
            ctx.PhaseCooldownMultiplier = phaseData != null ? phaseData.cooldownMultiplier : 1f;
            ctx.PhaseGlobalCooldownMultiplier = phaseData != null ? phaseData.globalCooldownMultiplier : 1f;

            if (!announce || !escalated || phaseData == null) return;

            ctx.PendingOpener = phaseData.openingAttack;
            if (logAttackDecisions) Debug.Log(name + ": entrou na fase " + phase + " (" + phaseData.name + ")", this);
        }

        private bool CheckGrounded()
        {
            Vector2 point = groundCheck != null ? (Vector2)groundCheck.position : rb.position;
            return Physics2D.OverlapCircle(point, groundCheckRadius, groundLayer) != null;
        }

        #endregion

        #region Movement and facing

        private void ApplyMovement()
        {
            if (ctx.IsDead) return;
            // In the air gravity and any knockback impulse own the body.
            if (!ctx.IsGrounded) return;

            float speed = ctx.HasForcedVelocity
                ? ctx.ForcedVelocityX
                : ctx.MoveDirection * ctx.MoveSpeed * ctx.MoveSpeedMultiplier;
            rb.linearVelocity = new Vector2(speed, rb.linearVelocityY);
        }

        private void TurnCheck()
        {
            if (!ctx.CanTurn || ctx.IsDead || ctx.Target == null) return;

            bool wantsRight = ctx.FaceMoveDirection && !Mathf.Approximately(ctx.MoveDirection, 0f)
                ? ctx.MoveDirection > 0f
                : ctx.Target.position.x > transform.position.x;
            SetFacing(wantsRight);
        }

        // Called as an attack starts, before the facing is locked: whatever the enemy was looking at
        // during the last recovery, the attack goes toward the player.
        private void FaceTarget()
        {
            if (ctx.IsDead || ctx.Target == null) return;
            SetFacing(ctx.Target.position.x > transform.position.x);
        }

        private void SetFacing(bool right)
        {
            if (right == ctx.FacingRight) return;

            ctx.FacingRight = right;
            transform.rotation = Quaternion.Euler(0f, ctx.FacingRight ? 0f : 180f, 0f);
        }

        // Some attack sheets end with the body further forward inside the frame than where the next
        // clip starts. Moving the body by that much when the attack ends keeps the switch seamless;
        // the cast stops it at walls, the arena barrier and the player (never teleports through them).
        private void CommitForwardOffset(float distance)
        {
            if (ctx.IsDead || Mathf.Approximately(distance, 0f)) return;

            Vector2 direction = new Vector2(ctx.FacingRight ? 1f : -1f, 0f) * Mathf.Sign(distance);
            float allowed = Mathf.Abs(distance);
            float blocked = CastDistance(direction, allowed);
            allowed = Mathf.Min(allowed, blocked);
            if (allowed <= 0f) return;

            Vector2 next = rb.position + direction * allowed;
            rb.position = next;
            transform.position = new Vector3(next.x, next.y, transform.position.z);
        }

        private bool IsPathBlocked(float directionSign)
        {
            const float probe = 0.5f;
            return CastDistance(new Vector2(Mathf.Sign(directionSign), 0f), probe) < probe;
        }

        // Free distance along direction before the body collider touches something solid (triggers ignored).
        private float CastDistance(Vector2 direction, float distance)
        {
            if (bodyCollider == null) return distance;

            const float skin = 0.02f;
            ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer));

            int count = bodyCollider.Cast(direction, filter, castHits, distance + skin);
            float free = distance;
            for (int i = 0; i < count; i++)
            {
                Collider2D other = castHits[i].collider;
                if (other == null || other.isTrigger) continue;
                free =Mathf.Min(free, Mathf.Max(0f, castHits[i].distance - skin));
            }

            return free;
        }

        #endregion

        #region Attack callbacks

        // A box or cone in front of the enemy, tested against the player's collider on this frame.
        // Facing is locked for the whole attack, so jumping over the enemy leaves the shape.
        private bool ApplyHitbox(AttackHitbox hitbox)
        {
            if (target == null || targetDamageable == null) return false;
            if (!targetDamageable.IsAlive() || targetDamageable.HasTakenDamage) return false;

            bool inside = hitbox.shape == AttackHitShape.Cone
                ? IsInsideCone(target.position, hitbox.coneRadius, hitbox.coneAngle)
                : OverlapsTarget(HitboxWorldRect(hitbox));
            if (!inside) return false;

            Vector2 direction = new Vector2(
                ctx.FacingRight ? 1f : -1f,
                Mathf.Abs(hitbox.verticalKnockback)).normalized;

            targetDamageable.TakeDamage(hitbox.damage, direction * hitbox.knockback);
            return true;
        }

        private Rect HitboxWorldRect(AttackHitbox hitbox)
        {
            float sign = ctx.FacingRight ? 1f : -1f;
            Vector2 center = new Vector2(
                transform.position.x + hitbox.offset.x * sign,
                SelfFeetY() + hitbox.offset.y);
            Vector2 size = new Vector2(Mathf.Abs(hitbox.size.x), Mathf.Abs(hitbox.size.y));
            return new Rect(center - size * 0.5f, size);
        }

        private bool OverlapsTarget(Rect area)
        {
            if (targetCollider == null) return area.Contains(target.position);

            Bounds b = targetCollider.bounds;
            return area.Overlaps(new Rect(b.min.x, b.min.y, b.size.x, b.size.y));
        }

        private bool IsInsideCone(Vector2 point, float radius, float angle)
        {
            Vector2 toTarget = point - (Vector2)transform.position;
            if (toTarget.sqrMagnitude > radius * radius) return false;

            Vector2 forward = ctx.FacingRight ? Vector2.right : Vector2.left;
            return Vector2.Angle(forward, toTarget) <= angle * 0.5f;
        }

        // The projectiles carry the damage numbers, so the attack asset stays the single source of
        // truth for how hard this attack hits.
        private void FireProjectiles(ProjectileAttackDefinition attack)
        {
            EnemyProjectile prefab = attack.projectilePrefab != null ? attack.projectilePrefab : projectilePrefab;
            if (prefab == null)
            {
                Debug.LogWarning(name + ": no projectile prefab for '" + attack.DisplayName + "', ranged attack does nothing", this);
                return;
            }

            Vector2 origin = projectileSpawnPoint != null ? (Vector2)projectileSpawnPoint.position : rb.position;
            Vector2 forward = ctx.FacingRight ? Vector2.right : Vector2.left;
            Vector2 aim = forward;

            if (attack.aimAtTarget && target != null)
            {
                Vector2 toTarget = TargetCenter() - origin;
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    float angle = Mathf.Clamp(Vector2.SignedAngle(forward, toTarget), -attack.maxAimAngle, attack.maxAimAngle);
                    aim = Rotate(forward, angle);
                }
            }

            int count = Mathf.Max(1, attack.count + attack.extraProjectilesPerPhase * ctx.Phase);
            for (int i = 0; i < count; i++)
            {
                float offset = count == 1 ? 0f : Mathf.Lerp(-attack.spreadAngle * 0.5f, attack.spreadAngle * 0.5f, i / (count - 1f));
                EnemyProjectile projectile = Instantiate(prefab, origin, Quaternion.identity);
                projectile.Launch(Rotate(aim, offset), attack.speed, attack.damage, attack.knockback, gameObject);
            }
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(r);
            float sin = Mathf.Sin(r);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }

        // True once the gaze landed (petrified and/or damaged), so the attack stops checking.
        private bool ApplyGaze(GazeAttackDefinition gaze)
        {
            if (target == null || targetDamageable == null || !targetDamageable.IsAlive()) return false;

            Vector2 eye = EyePosition();
            Vector2 toTarget = TargetCenter() - eye;
            if (toTarget.magnitude > gaze.gazeRange) return false;

            Vector2 forward = ctx.FacingRight ? Vector2.right : Vector2.left;
            if (Vector2.Angle(forward, toTarget) > gaze.coneAngle * 0.5f) return false;
            if (!HasLineOfSight(eye)) return false;
            if (gaze.requireTargetFacing && !ctx.TargetFacingSelf) return false;

            bool damaged = false;
            if (gaze.damage > 0f && !targetDamageable.HasTakenDamage)
            {
                targetDamageable.TakeDamage(gaze.damage, Vector2.zero);
                damaged = true;
            }

            bool petrified = PetrifyStatus.TryApply(target.gameObject, gaze.petrifyDuration, gaze.petrifyImmunity);
            return damaged || petrified;
        }

        #endregion

        #region Animation and tint

        // States route their clip through here rather than touching the Animator directly.
        // Chase asks for its clip every single frame, so a raw Play call from the hit reaction
        // would be overwritten on the very next one and the hit would never be visible.
        private void PlayAnimation(string stateName)
        {
            if (animator == null || string.IsNullOrEmpty(stateName)) return;

            // The hit reaction owns the animator for hitAnimationTime.
            if (hitAnimationTimer > 0f) return;

            int hash = Animator.StringToHash(stateName);
            // Enemy_Death has no state in the controller yet; asking for a missing one warns
            // every frame, so unknown names are skipped and the clips can be added later
            // without touching this code.
            if (!animator.HasState(0, hash)) return;
            if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == hash) return;

            animator.Play(hash, 0);
        }

        private bool HasAnimation(string stateName)
        {
            return animator != null && !string.IsNullOrEmpty(stateName) && animator.HasState(0, Animator.StringToHash(stateName));
        }

        // Attacks restart their clip at an exact frame and speed on every segment, and always win over
        // a hit reaction that is still showing.
        private void PlayAttackAnimation(string stateName, float normalizedTime, float speed)
        {
            if (!HasAnimation(stateName)) return;

            hitAnimationTimer = 0f;
            animator.speed = speed;
            animator.Play(Animator.StringToHash(stateName), 0, normalizedTime);
        }

        private void SetAnimationSpeed(float speed)
        {
            if (animator != null) animator.speed = speed;
        }

        private void PlayHitAnimation()
        {
            if (animator == null) return;

            int hash = Animator.StringToHash("Enemy_Hit");
            if (!animator.HasState(0, hash)) return;

            hitAnimationTimer = hitAnimationTime;
            // Restarted from frame 0 so rapid hits re-trigger the flash instead of being
            // swallowed by the already-playing check in PlayAnimation.
            animator.Play(hash, 0, 0f);
        }

        private void SetTelegraph(Color? color)
        {
            if (color.HasValue && !telegraphColor.HasValue) telegraphTime = 0f;
            telegraphColor = color;
        }

        // Hit flash beats telegraph beats the sprite's own colour. The renderer is only written while
        // a tint is showing (and once to restore it), so other scripts can still colour the sprite.
        private void UpdateTint(float deltaTime)
        {
            if (tintRenderer == null) return;

            if (hitFlashTimer > 0f) hitFlashTimer -= deltaTime;

            Color? tint = null;
            if (hitFlashTimer > 0f)
            {
                tint = hitFlashColor * baseTint;
            }
            else if (telegraphColor.HasValue && !ctx.IsDead)
            {
                telegraphTime += deltaTime;
                float pulse = 0.5f + 0.5f * Mathf.Sin(telegraphTime * telegraphPulseRate * Mathf.PI * 2f);
                tint = Color.Lerp(baseTint, telegraphColor.Value * baseTint, 0.35f + 0.65f * pulse);
            }

            if (tint.HasValue)
            {
                tintRenderer.color = tint.Value;
                tinted = true;
            }
            else if (tinted)
            {
                tintRenderer.color = baseTint;
                tinted = false;
            }
        }

        #endregion

        #region IDamageable

        public void TakeDamage(float amount, Vector2 knockback)
        {
            if (HasTakenDamage || ctx.IsDead || ctx.Health <= 0f) return;

            HasTakenDamage = true;
            invulnerabilityTimer = invulnerabilityTime;
            ctx.Health -= amount;
            HealthChanged?.Invoke(ctx.Health, MaxHealth);

            // rb.AddForce(knockback, ForceMode2D.Impulse);
            // Hits never interrupt the state machine. With super armor an attack keeps its clip and
            // only the sprite flashes; otherwise the hit reaction borrows the animator for a moment.
            if (superArmorWhileAttacking && ctx.InAttack) hitFlashTimer = hitFlashTime;
            else PlayHitAnimation();

            if (ctx.Health > 0f) return;

            hitAnimationTimer = 0f; // do not let the hit flash block the death clip
            ctx.DeathRequested = true;
        }

        public bool IsAlive() => ctx.Health > 0f;

        public void Heal(int amount)
        {
            ctx.Health = Mathf.Min(ctx.Health + amount, data.maxHealth);
            HealthChanged?.Invoke(ctx.Health, MaxHealth);
        }

        /// <summary>
        /// End of the death animation. EnemyDead clears ctx.OnDeathFinished right after calling
        /// this, so the notifications below run exactly once.
        /// </summary>
        private void HandleDeathFinished()
        {
            // Notify before destroying: listeners that open a gate or complete a quest must run
            // while this object is still alive enough to be a valid sender.
            Died?.Invoke();
            onDeath?.Invoke();
            Destroy(gameObject);
        }

        #endregion

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;

            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, detectionRange);

            bool facingRight = Application.isPlaying
                ? ctx.FacingRight
                : Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, 0f)) < 90f;
            Vector3 forward = facingRight ? Vector3.right : Vector3.left;

            Gizmos.color = new Color(0f, 1f, 0.5f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, stopDistance);

            if (attackSet != null) DrawAttackSetGizmos(facingRight);
            else DrawLegacyConeGizmo(forward);

            if (projectileSpawnPoint != null)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.9f);
                Gizmos.DrawWireSphere(projectileSpawnPoint.position, 0.15f);
                Gizmos.DrawRay(projectileSpawnPoint.position, forward * 2f);
            }

            if (groundCheck == null) return;
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }

        // One horizontal bar per attack showing its range band (stacked above the feet), plus the
        // hitbox of every Active segment of the melee attacks.
        private void DrawAttackSetGizmos(bool facingRight)
        {
            float sign = facingRight ? 1f : -1f;
            float feet = GetComponent<Collider2D>() is Collider2D c ? c.bounds.min.y : transform.position.y;
            Vector3 origin = transform.position;

            for (int i = 0; i < attackSet.attacks.Count; i++)
            {
                EnemyAttackDefinition attack = attackSet.attacks[i];
                if (attack == null) continue;

                Color color = Color.HSVToRGB((i * 0.17f) % 1f, 0.8f, 1f);
                float y = feet + 0.15f + i * 0.2f;
                Gizmos.color = color;
                Vector3 a = new Vector3(origin.x + attack.minRange * sign, y, 0f);
                Vector3 b = new Vector3(origin.x + attack.maxRange * sign, y, 0f);
                Gizmos.DrawLine(a, b);
                Gizmos.DrawLine(a + Vector3.up * 0.08f, a - Vector3.up * 0.08f);
                Gizmos.DrawLine(b + Vector3.up * 0.08f, b - Vector3.up * 0.08f);

                if (attack is not MeleeAttackDefinition) continue;

                color.a = 0.6f;
                Gizmos.color = color;
                foreach (AttackSegment segment in attack.segments)
                {
                    if (segment.kind != AttackSegmentKind.Active || segment.hitbox.shape != AttackHitShape.Box) continue;
                    Vector3 center = new Vector3(origin.x + segment.hitbox.offset.x * sign, feet + segment.hitbox.offset.y, 0f);
                    Gizmos.DrawWireCube(center, segment.hitbox.size);
                }
            }
        }

        // Attack cone, not a circle: this is the shape the legacy melee hit actually tests.
        private void DrawLegacyConeGizmo(Vector3 forward)
        {
            float half = attackConeAngle * 0.5f;

            Gizmos.color = Color.red;
            Vector3 edgeA = Quaternion.Euler(0f, 0f, half) * forward * attackRange;
            Vector3 edgeB = Quaternion.Euler(0f, 0f, -half) * forward * attackRange;
            Gizmos.DrawRay(transform.position, edgeA);
            Gizmos.DrawRay(transform.position, edgeB);

            const int arcSteps = 12;
            Vector3 previous = transform.position + edgeB;
            for (int i = 1; i <= arcSteps; i++)
            {
                float angle = Mathf.Lerp(-half, half, i / (float)arcSteps);
                Vector3 next = transform.position + Quaternion.Euler(0f, 0f, angle) * forward * attackRange;
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }

        private static string StatePath(State state)
        {
            return string.Join(" > ", state.PathToRoot().Reverse().Select(x => x.GetType().Name));
        }
    }
}
