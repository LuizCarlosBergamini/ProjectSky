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

    // Waits for the player to walk into the room.
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
            ctx.TargetInDetectionRange ? ((EnemyRoot)Parent).Chase : null;
    }

    // Walks toward the player and holds at StopDistance.
    public class EnemyChase : State
    {
        private readonly EnemyContext ctx;

        public EnemyChase(StateMachine sm, State parent, EnemyContext ctx) : base(sm, parent)
        {
            this.ctx = ctx;
        }

        protected override void OnEnter()
        {
            ctx.PlayAnimation?.Invoke("Enemy_Running");
        }

        protected override void OnUpdate(float deltaTime)
        {
            if (!ctx.HasTarget)
            {
                ctx.MoveDirection = 0f;
                return;
            }

            // Stopping short of the player keeps the enemy from shoving them around while it
            // waits for the attack cooldown.
            float dx = ctx.Target.position.x - ctx.self.position.x;
            ctx.MoveDirection = Mathf.Abs(dx) > ctx.StopDistance ? Mathf.Sign(dx) : 0f;

            ctx.PlayAnimation?.Invoke(
                Mathf.Approximately(ctx.MoveDirection, 0f) ? "Enemy_Idle" : "Enemy_Running");
        }

        protected override State GetTransition()
        {
            EnemyRoot root = (EnemyRoot)Parent;
            if (!ctx.TargetInDetectionRange) return root.Idle;
            if (ctx.TargetInAttackRange && ctx.AttackCooldown <= 0f) return root.Attack;
            return null;
        }

        protected override void OnExit() => ctx.MoveDirection = 0f;
    }

    // Picks which kind of attack to run and owns everything the two share: standing still,
    // committing the facing, and starting the cooldown on the way out. OnExit runs even when a
    // child is interrupted, so neither child has to remember to undo those.
    public class EnemyAttack : State
    {
        public readonly EnemyCloseAttack CloseAttack;
        public readonly EnemyRangedAttack RangedAttack;
        private readonly EnemyContext ctx;

        // TESTING ONLY: alternates so both branches are easy to observe.
        private bool useRangedNext;

        public EnemyAttack(StateMachine sm, State parent, EnemyContext ctx) : base(sm, parent)
        {
            this.ctx = ctx;
            CloseAttack = new EnemyCloseAttack(sm, this, ctx);
            RangedAttack = new EnemyRangedAttack(sm, this, ctx);
        }

        protected override void OnEnter()
        {
            ctx.MoveDirection = 0f;
            ctx.CanTurn = false; // the attack commits to the direction it started in
        }

        // Called once per entry by State.Enter, which is what makes the alternation advance
        // exactly one step per attack.
        protected override State GetInitialState()
        {
            // --- TESTING: strict alternation, close then ranged then close... ---
            State chosen = useRangedNext ? (State)RangedAttack : CloseAttack;
            useRangedNext = !useRangedNext;
            return chosen;

            // --- FINAL VERSION: pick at random. Swap this in and delete the block above. ---
            // return Random.value < 0.5f ? (State)RangedAttack : CloseAttack;
        }

        protected override void OnExit()
        {
            ctx.CanTurn = true;
            ctx.AttackCooldown = ctx.AttackCooldownDuration;
        }
    }

    // The original melee swing: damage lands in a cone in front of the enemy partway through.
    public class EnemyCloseAttack : State
    {
        private readonly EnemyContext ctx;
        private float timer;
        private bool hasHit;

        public EnemyCloseAttack(StateMachine sm, State parent, EnemyContext ctx) : base(sm, parent)
        {
            this.ctx = ctx;
        }

        protected override void OnEnter()
        {
            timer = ctx.AttackDuration;
            hasHit = false;
            ctx.PlayAnimation?.Invoke("Enemy_Attack");
        }

        protected override void OnUpdate(float deltaTime)
        {
            timer -= deltaTime;

            // Damage lands partway through the animation rather than on the frame the state
            // begins, so the player has a moment to back out of the cone.
            if (hasHit || timer > ctx.AttackDuration - ctx.AttackHitTime) return;

            hasHit = true;
            ctx.DealAttackDamage?.Invoke();
        }

        // Chase is a direct child of the root, so this exits both this state and EnemyAttack.
        protected override State GetTransition() =>
            timer <= 0f ? ((EnemyRoot)Parent.Parent).Chase : null;
    }

    // Fires a projectile forward partway through the animation. The projectile carries the
    // damage from there on, so nothing here needs to know where the player ended up.
    public class EnemyRangedAttack : State
    {
        private readonly EnemyContext ctx;
        private float timer;
        private bool hasFired;

        public EnemyRangedAttack(StateMachine sm, State parent, EnemyContext ctx) : base(sm, parent)
        {
            this.ctx = ctx;
        }

        protected override void OnEnter()
        {
            timer = ctx.RangedAttackDuration;
            hasFired = false;
            // Falls back silently if the controller has no such state yet.
            ctx.PlayAnimation?.Invoke("Enemy_RangedAttack");
        }

        protected override void OnUpdate(float deltaTime)
        {
            timer -= deltaTime;

            if (hasFired || timer > ctx.RangedAttackDuration - ctx.RangedAttackFireTime) return;

            hasFired = true;
            ctx.SpawnProjectile?.Invoke();
        }

        protected override State GetTransition() =>
            timer <= 0f ? ((EnemyRoot)Parent.Parent).Chase : null;
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
