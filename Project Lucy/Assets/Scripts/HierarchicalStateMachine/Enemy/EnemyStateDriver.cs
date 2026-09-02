using System.Linq;
using UnityEngine;

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

        [Header("Ground Check")]
        [SerializeField] private Transform groundCheck;
        [SerializeField] private float groundCheckRadius = 0.25f;
        [SerializeField] private LayerMask groundLayer;

        [Header("Damage")]
        [SerializeField] private float invulnerabilityTime = 0.15f;
        [SerializeField] private float deathDelay = 0.4f;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private bool logStatePath;

        private readonly EnemyContext ctx = new EnemyContext();
        private Rigidbody2D rb;
        private StateMachine sm;
        private State root;
        private Transform target;
        private float invulnerabilityTimer;
        private string lastPath;

        public bool HasTakenDamage { get; set; }

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            if (animator == null) animator = GetComponent<Animator>();

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
            ctx.DeathDelay = deathDelay;

            // Rotation y == 0 faces right, 180 faces left (same convention as the old EnemyPatrol).
            ctx.FacingRight = Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, 0f)) < 90f;

            ctx.PlayAnimation = PlayAnimation;
            ctx.DealAttackDamage = DealAttackDamage;
            ctx.OnDeathFinished = () => Destroy(gameObject);

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

        // A plain distance check at the moment the hit lands - no hitbox volume to author.
        private void DealAttackDamage()
        {
            if (target == null) return;
            if (Vector2.Distance(transform.position, target.position) > attackRange) return;

            IDamageable damageable = target.GetComponent<IDamageable>();
            if (damageable == null || !damageable.IsAlive() || damageable.HasTakenDamage) return;

            Vector2 direction = new Vector2(
                ctx.FacingRight ? 1f : -1f,
                Mathf.Abs(verticalKnockback)).normalized;

            damageable.TakeDamage(data.damage, direction * data.knockbackForce);
        }

        private void PlayAnimation(string stateName)
        {
            if (animator == null || string.IsNullOrEmpty(stateName)) return;

            int hash = Animator.StringToHash(stateName);
            // The Enemy controller only has Idle / Running / Hit today. Asking for a missing
            // state would warn every frame, so unknown names are ignored and the clips can be
            // added later without touching this code.
            if (!animator.HasState(0, hash)) return;
            if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == hash) return;

            animator.Play(hash, 0);
        }

        #region IDamageable

        public void TakeDamage(float amount, Vector2 knockback)
        {
            if (HasTakenDamage || ctx.IsDead || ctx.Health <= 0f) return;

            HasTakenDamage = true;
            invulnerabilityTimer = invulnerabilityTime;
            ctx.Health -= amount;

            rb.AddForce(knockback, ForceMode2D.Impulse);

            if (ctx.Health <= 0f) ctx.DeathRequested = true;
        }

        public bool IsAlive() => ctx.Health > 0f;

        public void Heal(int amount)
        {
            ctx.Health = Mathf.Min(ctx.Health + amount, data.maxHealth);
        }

        #endregion

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;

            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, detectionRange);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);

            Gizmos.color = new Color(0f, 1f, 0.5f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, stopDistance);

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
