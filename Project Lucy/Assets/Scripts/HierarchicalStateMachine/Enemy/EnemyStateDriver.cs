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

        [Header("Awareness")]
        [SerializeField] private string targetTag = "Player";
        [SerializeField] private float detectionRange = 12f; // the boss room radius
        [SerializeField] private float attackRange = 2f;
        [SerializeField] private float stopDistance = 1.6f;

        [Header("Attack")]
        [SerializeField] private float attackDuration = 0.8f;
        [SerializeField] private float attackHitTime = 0.45f; // when inside the attack damage lands
        [SerializeField] private float attackCooldown = 1.2f;
        [SerializeField] private float verticalKnockback = 0.5f;
        // Total cone width in front of the enemy. Facing is locked when the attack starts, so a
        // player who jumps over mid-swing leaves the cone and is missed.
        [Range(10f, 360f)] [SerializeField] private float attackConeAngle = 100f;

        [Header("Ranged Attack")]
        [SerializeField] private EnemyProjectile projectilePrefab;
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

        [Header("Events")]
        [Tooltip("Disparado quando a animacao de morte termina, logo antes do objeto ser destruido.")]
        [SerializeField] private UnityEvent onDeath;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private bool logStatePath;

        private readonly EnemyContext ctx = new EnemyContext();
        private Rigidbody2D rb;
        private StateMachine sm;
        private State root;
        private Transform target;
        private float invulnerabilityTimer;
        private float hitAnimationTimer;
        private string lastPath;

        public bool HasTakenDamage { get; set; }

        /// <summary>
        /// Raised once when this enemy finishes dying, just before the object is destroyed.
        /// Code-side counterpart of <see cref="onDeath"/>, so listeners can subscribe without
        /// being wired in the Inspector (a scene object cannot reference a spawned enemy).
        /// </summary>
        public event Action Died;

        /// <summary>True from the moment the death animation starts.</summary>
        public bool IsDead => ctx.IsDead;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            animator = ResolveAnimator();

            ctx.rb = rb;
            ctx.animator = animator;
            ctx.self = transform;
            ctx.Data = data;
            ctx.Health = data.maxHealth;
            ctx.MoveSpeed = data.moveSpeed;
            ctx.StopDistance = stopDistance;
            ctx.AttackDuration = attackDuration;
            ctx.AttackHitTime = attackHitTime;
            ctx.AttackCooldownDuration = attackCooldown;
            ctx.RangedAttackDuration = rangedAttackDuration;
            ctx.RangedAttackFireTime = rangedAttackFireTime;
            ctx.DeathDelay = deathDelay;

            // Rotation y == 0 faces right, 180 faces left (same convention as the old EnemyPatrol).
            ctx.FacingRight = Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, 0f)) < 90f;

            ctx.PlayAnimation = PlayAnimation;
            ctx.DealAttackDamage = DealAttackDamage;
            ctx.SpawnProjectile = SpawnProjectile;
            ctx.OnDeathFinished = HandleDeathFinished;

            // Same bootstrap as PlayerStateDriver: the builder back-fills every State.Machine by
            // reflection, which is why the root can be constructed with a null machine.
            root = new EnemyRoot(null, ctx);
            sm = new StateMachineBuilder(root).Build();
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            UpdateAwareness();
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

        private void UpdateAwareness()
        {
            if (target == null)
            {
                GameObject found = GameObject.FindGameObjectWithTag(targetTag);
                target = found != null ? found.transform : null;
            }

            ctx.Target = target;

            if (target == null)
            {
                ctx.TargetInDetectionRange = false;
                ctx.TargetInAttackRange = false;
                return;
            }

            ctx.DistanceToTarget = Vector2.Distance(transform.position, target.position);
            ctx.TargetInDetectionRange = ctx.DistanceToTarget <= detectionRange;
            ctx.TargetInAttackRange = ctx.DistanceToTarget <= attackRange;
        }

        private bool CheckGrounded()
        {
            Vector2 point = groundCheck != null ? (Vector2)groundCheck.position : rb.position;
            return Physics2D.OverlapCircle(point, groundCheckRadius, groundLayer) != null;
        }

        private void ApplyMovement()
        {
            if (ctx.IsDead) return;
            // In the air gravity and any knockback impulse own the body.
            if (!ctx.IsGrounded) return;

            rb.linearVelocity = new Vector2(ctx.MoveDirection * ctx.MoveSpeed, rb.linearVelocityY);
        }

        private void TurnCheck()
        {
            if (!ctx.CanTurn || ctx.IsDead || ctx.Target == null) return;

            bool wantsRight = ctx.Target.position.x > transform.position.x;
            if (wantsRight == ctx.FacingRight) return;

            ctx.FacingRight = wantsRight;
            transform.rotation = Quaternion.Euler(0f, ctx.FacingRight ? 0f : 180f, 0f);
        }

        // A cone in front of the enemy at the moment the hit lands - no hitbox volume to author.
        // Facing is locked for the whole attack, so jumping over the enemy leaves the cone.
        private void DealAttackDamage()
        {
            if (target == null) return;
            if (!IsInsideAttackCone(target.position)) return;

            IDamageable damageable = target.GetComponent<IDamageable>();
            if (damageable == null || !damageable.IsAlive() || damageable.HasTakenDamage) return;

            Vector2 direction = new Vector2(
                ctx.FacingRight ? 1f : -1f,
                Mathf.Abs(verticalKnockback)).normalized;

            damageable.TakeDamage(data.damage, direction * data.knockbackForce);
        }

        private bool IsInsideAttackCone(Vector2 point)
        {
            Vector2 toTarget = point - (Vector2)transform.position;
            if (toTarget.sqrMagnitude > attackRange * attackRange) return false;

            Vector2 forward = ctx.FacingRight ? Vector2.right : Vector2.left;
            return Vector2.Angle(forward, toTarget) <= attackConeAngle * 0.5f;
        }

        // Fired from the RangedAttack state. The projectile carries the damage numbers so the
        // enemy data stays the single source of truth for how hard this enemy hits.
        private void SpawnProjectile()
        {
            if (projectilePrefab == null)
            {
                Debug.LogWarning(name + ": no projectilePrefab assigned, ranged attack does nothing", this);
                return;
            }

            Vector2 origin = projectileSpawnPoint != null ? (Vector2)projectileSpawnPoint.position : rb.position;
            Vector2 direction = ctx.FacingRight ? Vector2.right : Vector2.left;

            EnemyProjectile projectile = Instantiate(projectilePrefab, origin, Quaternion.identity);
            projectile.Launch(direction, projectileSpeed, data.damage, data.knockbackForce, gameObject);
        }

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

        private void PlayHitAnimation()
        {
            if (animator == null) return;

            int hash = Animator.StringToHash("Enemy_Hit");
            if (!animator.HasState(0, hash)) return;

            Debug.Log("hit animation");
            hitAnimationTimer = hitAnimationTime;
            // Restarted from frame 0 so rapid hits re-trigger the flash instead of being
            // swallowed by the already-playing check in PlayAnimation.
            animator.Play(hash, 0, 0f);
        }

        #region IDamageable

        public void TakeDamage(float amount, Vector2 knockback)
        {
            if (HasTakenDamage || ctx.IsDead || ctx.Health <= 0f) return;

            HasTakenDamage = true;
            invulnerabilityTimer = invulnerabilityTime;
            ctx.Health -= amount;

            // rb.AddForce(knockback, ForceMode2D.Impulse);
            PlayHitAnimation();

            if (ctx.Health > 0f) return;

            hitAnimationTimer = 0f; // do not let the hit flash block the death clip
            ctx.DeathRequested = true;
        }

        public bool IsAlive() => ctx.Health > 0f;

        public void Heal(int amount)
        {
            ctx.Health = Mathf.Min(ctx.Health + amount, data.maxHealth);
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

            // Attack cone, not a circle: this is the shape the melee hit actually tests.
            bool facingRight = Application.isPlaying
                ? ctx.FacingRight
                : Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, 0f)) < 90f;
            Vector3 forward = facingRight ? Vector3.right : Vector3.left;
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

            Gizmos.color = new Color(0f, 1f, 0.5f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, stopDistance);

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

        private static string StatePath(State state)
        {
            return string.Join(" > ", state.PathToRoot().Reverse().Select(x => x.GetType().Name));
        }
    }
}
