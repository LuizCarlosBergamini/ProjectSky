using UnityEngine;

namespace HierarchicalStateMachine
{
    // Flat machine: Idle -> Chase -> Attack, plus Dead as an interrupt.
    public class EnemyRoot : State
    {
        public readonly EnemyIdle Idle;
        public readonly EnemyChase Chase;
        public readonly EnemyAttack Attack;
        public readonly EnemyDead Dead;
        private readonly EnemyContext ctx;

        public EnemyRoot(StateMachine sm, EnemyContext ctx) : base(sm, null)
        {
            this.ctx = ctx;
            Idle = new EnemyIdle(sm, this, ctx);
            Chase = new EnemyChase(sm, this, ctx);
            Attack = new EnemyAttack(sm, this, ctx);
            Dead = new EnemyDead(sm, this, ctx);
        }

        protected override State GetInitialState() => Idle;

        // Death beats whatever is running: a parent GetTransition() is evaluated before its
        // children update. The flag is one-shot so the transition cannot repeat.
        protected override State GetTransition()
        {
            if (!ctx.DeathRequested) return null;

            ctx.DeathRequested = false;
            return Dead;
        }
    }

    // Waits for the player to walk into the room. A boss also waits for its arena gate to close
    // (ctx.Activated), so it never starts the fight while the player can still walk away.
    public class EnemyIdle : State
    {
        private readonly EnemyContext ctx;

        public EnemyIdle(StateMachine sm, State parent, EnemyContext ctx) : base(sm, parent)
        {
            this.ctx = ctx;
        }

        protected override void OnEnter()
        {
            ctx.MoveDirection = 0f;
            ctx.PlayAnimation?.Invoke("Enemy_Idle");
        }

        protected override State GetTransition() =>
            ctx.Activated && ctx.TargetInDetectionRange ? ((EnemyRoot)Parent).Chase : null;
    }

    // Between attacks: asks EnemyAttack for a valid attack every frame and, while there is none,
    // repositions into the range band of the attack that comes off cooldown first - approaching
    // (walking, or running when far), backing off, or holding still.
    public class EnemyChase : State
    {
        private readonly EnemyContext ctx;
        private float retreatTime;
        private string walkAnimation;

        public EnemyChase(StateMachine sm, State parent, EnemyContext ctx) : base(sm, parent)
        {
            this.ctx = ctx;
        }

        protected override void OnEnter()
        {
            retreatTime = 0f;
            // Controllers made before Enemy_Walk existed only have the running clip.
            walkAnimation = ctx.HasAnimation != null && ctx.HasAnimation("Enemy_Walk") ? "Enemy_Walk" : "Enemy_Running";
        }

        protected override void OnUpdate(float deltaTime)
        {
            if (!ctx.HasTarget)
            {
                Hold();
                return;
            }

            EnemyAttackSet set = ctx.AttackSet;
            float dx = ctx.Target.position.x - ctx.self.position.x;
            float toward = Mathf.Sign(dx);
            float distance = Mathf.Abs(dx);

            // Never closer than StopDistance, so the enemy does not shove the player around while it
            // waits for a cooldown.
            float desired = ((EnemyRoot)Parent).Attack.PreferredDistance(distance);
            float tolerance = set != null ? set.holdTolerance : 0.1f;

            if (distance > desired + tolerance)
            {
                bool run = set == null || distance > set.runDistance;
                float speed = run && set != null ? set.runSpeedMultiplier : 1f;
                Move(toward, speed, run ? "Enemy_Running" : walkAnimation);
                ctx.FaceMoveDirection = false;
                return;
            }

            bool canRetreat = set != null && set.allowRetreat && retreatTime < set.maxRetreatTime
                              && (ctx.IsPathBlocked == null || !ctx.IsPathBlocked(-toward));
            if (distance < desired - tolerance && canRetreat)
            {
                retreatTime += deltaTime;
                ctx.FaceMoveDirection = true; // slithers away head first instead of moonwalking
                Move(-toward, set.retreatSpeedMultiplier, walkAnimation);
                return;
            }

            Hold();
        }

        private void Move(float direction, float speedMultiplier, string animation)
        {
            ctx.MoveDirection = direction;
            ctx.MoveSpeedMultiplier = speedMultiplier;
            ctx.PlayAnimation?.Invoke(animation);
        }

        private void Hold()
        {
            ctx.MoveDirection = 0f;
            ctx.MoveSpeedMultiplier = 1f;
            ctx.FaceMoveDirection = false;
            ctx.PlayAnimation?.Invoke("Enemy_Idle");
        }

        protected override State GetTransition()
        {
            EnemyRoot root = (EnemyRoot)Parent;
            if (!ctx.TargetInDetectionRange) return root.Idle;
            if (ctx.AttackCooldown <= 0f && root.Attack.TrySelect()) return root.Attack;
            return null;
        }

        protected override void OnExit()
        {
            ctx.MoveDirection = 0f;
            ctx.MoveSpeedMultiplier = 1f;
            ctx.FaceMoveDirection = false;
        }
    }

    // The AttackState: one child per attack in ctx.AttackSet, chosen by EnemyAttackSelector. It owns
    // everything the attacks share - standing still, facing the player and locking that facing, and
    // starting the pause before the next attack. OnExit runs even when a child is interrupted (death),
    // so no child has to remember to undo those.
    public class EnemyAttack : State
    {
        public readonly EnemyAttackMove[] Moves;
        private readonly EnemyAttackSelector selector;
        private readonly EnemyContext ctx;
        private EnemyAttackMove chosen;

        public EnemyAttack(StateMachine sm, State parent, EnemyContext ctx) : base(sm, parent)
        {
            this.ctx = ctx;

            var moves = new System.Collections.Generic.List<EnemyAttackMove>();
            if (ctx.AttackSet != null)
            {
                foreach (EnemyAttackDefinition definition in ctx.AttackSet.attacks)
                {
                    if (definition != null) moves.Add(definition.CreateState(sm, this, ctx));
                }
            }

            Moves = moves.ToArray();
            // The driver always provides a set (a runtime legacy one for enemies without an asset).
            selector = new EnemyAttackSelector(ctx, ctx.AttackSet);
        }

        /// <summary>Scores the attacks and remembers the winner for the next entry. False = none is valid.</summary>
        public bool TrySelect()
        {
            chosen = selector.Pick(Moves);
            return chosen != null;
        }

        /// <summary>Where to stand while no attack is valid; see EnemyAttackSelector.PreferredDistance.</summary>
        public float PreferredDistance(float currentDistance) => selector.PreferredDistance(Moves, currentDistance);

        protected override void OnEnter()
        {
            ctx.MoveDirection = 0f;
            ctx.FaceMoveDirection = false;
            ctx.FaceTarget?.Invoke();  // the player may have jumped over during the last recovery
            ctx.CanTurn = false;       // the attack commits to the direction it started in
            ctx.InAttack = true;
        }

        // Called once per entry by State.Enter: runs the attack TrySelect picked.
        protected override State GetInitialState() => chosen;

        // Safety net: an attack set with no usable attack would otherwise leave the enemy stuck here.
        protected override State GetTransition() =>
            ActiveChild == null ? ((EnemyRoot)Parent).Chase : null;

        protected override void OnExit()
        {
            ctx.CanTurn = true;
            ctx.InAttack = false;
            selector.RecordUse(chosen);
            chosen = null;

            float pause = ctx.AttackSet != null ? ctx.AttackSet.globalCooldown : 0f;
            ctx.AttackCooldown = pause * ctx.PhaseGlobalCooldownMultiplier;
        }
    }

    public class EnemyDead : State
    {
        private readonly EnemyContext ctx;
        private float timer;

        public EnemyDead(StateMachine sm, State parent, EnemyContext ctx) : base(sm, parent)
        {
            this.ctx = ctx;
        }

        protected override void OnEnter()
        {
            ctx.IsDead = true;
            ctx.MoveDirection = 0f;
            ctx.CanTurn = false;
            timer = ctx.DeathDelay;
            ctx.PlayAnimation?.Invoke("Enemy_Death");
        }

        protected override void OnUpdate(float deltaTime)
        {
            timer -= deltaTime;
            if (timer > 0f) return;

            ctx.OnDeathFinished?.Invoke();
            ctx.OnDeathFinished = null; // the callback destroys the object; never call it twice
        }

        protected override State GetTransition() => null; // terminal
    }
}
