using UnityEngine;

namespace HierarchicalStateMachine
{
    /// <summary>
    /// The contract every child of EnemyAttack follows. The parent has already stopped the enemy and
    /// committed its facing; this walks the definition's segments (wind-up, active, recovery), keeps the clip
    /// in step with them, and goes back to Chase when the last one ends. OnExit runs on interruption too
    /// (death), so the per-attack cooldown, the telegraph and the animation speed are always cleaned up.
    /// Subclasses only decide what an Active segment does.
    /// </summary>
    public abstract class EnemyAttackMove : State
    {
        public readonly EnemyAttackDefinition Definition;
        protected readonly EnemyContext ctx;

        private int segmentIndex;
        private float segmentTime;
        private bool finished;
        private float readyAt;
        private int hurtboxFrame;

        protected EnemyAttackMove(StateMachine machine, State parent, EnemyContext ctx, EnemyAttackDefinition definition)
            : base(machine, parent)
        {
            this.ctx = ctx;
            Definition = definition;
        }

        /// <summary>False while this attack's own cooldown is running.</summary>
        public bool IsReady => ctx.Clock >= readyAt;

        public float CooldownRemaining => Mathf.Max(0f, readyAt - ctx.Clock);

        protected AttackSegment CurrentSegment => Definition.segments[segmentIndex];

        protected float FacingSign => ctx.FacingRight ? 1f : -1f;

        protected override void OnEnter()
        {
            finished = false;
            segmentIndex = -1;
            hurtboxFrame = -1;
            OnMoveEnter();

            if (Definition.segments.Count == 0)
            {
                finished = true;
                return;
            }

            if (!Definition.driveAnimation) ctx.PlayAttackAnimation?.Invoke(Definition.animationState, 0f, 1f);
            BeginSegment(0);
        }

        protected override void OnUpdate(float deltaTime)
        {
            if (finished) return;

            segmentTime += deltaTime;
            AttackSegment segment = CurrentSegment;
            float progress = Mathf.Clamp01(segmentTime / segment.duration);
            UpdateHurtbox(segment, progress);
            OnSegmentUpdate(segment, progress);

            if (segmentTime < segment.duration) return;

            float overflow = segmentTime - segment.duration;
            OnSegmentEnd(segment);

            if (segmentIndex + 1 >= Definition.segments.Count)
            {
                finished = true;
                return;
            }

            BeginSegment(segmentIndex + 1);
            segmentTime = overflow;
        }

        // Chase is a direct child of the root, so this exits both this state and EnemyAttack.
        protected override State GetTransition() =>
            finished ? ((EnemyRoot)Parent.Parent).Chase : null;

        protected override void OnExit()
        {
            readyAt = ctx.Clock + Definition.cooldown * ctx.PhaseCooldownMultiplier;

            ctx.SetTelegraph?.Invoke(null);
            ctx.HasForcedVelocity = false;
            ctx.ForcedVelocityX = 0f;
            ctx.SetAnimationSpeed?.Invoke(1f);

            // Only a completed attack ends where its last frame shows the body; an interrupted one never got there.
            if (finished && !Mathf.Approximately(Definition.exitForwardOffset, 0f))
                ctx.CommitForwardOffset?.Invoke(Definition.exitForwardOffset);

            // Back to the resting hurtbox, also when death interrupts the attack.
            hurtboxFrame = -1;
            ctx.SetHurtboxFrame?.Invoke(null, 0);

            OnMoveExit();
        }

        private void BeginSegment(int index)
        {
            segmentIndex = index;
            segmentTime = 0f;
            AttackSegment segment = CurrentSegment;

            // Restarting the clip at this segment's first frame, at the speed that makes its frames last
            // exactly segment.duration, keeps the visible strike on the damaging window even after retuning.
            if (Definition.driveAnimation)
            {
                float frames = Mathf.Max(1, Definition.totalFrames);
                // A hair past the frame boundary so the sample never lands on the previous frame.
                float start = Mathf.Clamp01((segment.firstFrame + 0.02f) / frames);
                float natural = segment.FrameCount / Mathf.Max(1f, Definition.sourceFrameRate);
                ctx.PlayAttackAnimation?.Invoke(Definition.animationState, start, natural / Mathf.Max(0.01f, segment.duration));
            }

            bool telegraph = segment.kind == AttackSegmentKind.WindUp && Definition.telegraphColor.a > 0f;
            ctx.SetTelegraph?.Invoke(telegraph ? Definition.telegraphColor : (Color?)null);

            ctx.HasForcedVelocity = !Mathf.Approximately(segment.advanceDistance, 0f);
            ctx.ForcedVelocityX = ctx.HasForcedVelocity
                ? FacingSign * segment.advanceDistance / Mathf.Max(0.01f, segment.duration)
                : 0f;

            UpdateHurtbox(segment, 0f);
            OnSegmentStart(segment);
        }

        // The clip shows the segment's frames evenly across its duration, so the frame on screen is known
        // here. The hurtbox follows the body drawn on that frame (see AttackFrameBody): the player can hit
        // whatever is on screen, in wind-up, active and recovery alike.
        private void UpdateHurtbox(AttackSegment segment, float progress)
        {
            if (!Definition.driveAnimation || Definition.bodyFrames == null || Definition.bodyFrames.Length == 0) return;

            int frame = segment.firstFrame + Mathf.Min(segment.FrameCount - 1, Mathf.FloorToInt(progress * segment.FrameCount));
            if (frame == hurtboxFrame) return;

            hurtboxFrame = frame;
            ctx.SetHurtboxFrame?.Invoke(Definition, frame);
        }

        private void OnSegmentEnd(AttackSegment segment)
        {
            if (!ctx.HasForcedVelocity) return;
            ctx.HasForcedVelocity = false;
            ctx.ForcedVelocityX = 0f;
        }

        protected virtual void OnMoveEnter() { }
        protected virtual void OnMoveExit() { }
        protected virtual void OnSegmentStart(AttackSegment segment) { }
        protected virtual void OnSegmentUpdate(AttackSegment segment, float progress) { }

        /// <summary>Lets a phase opener or a debug command use this attack right away.</summary>
        public void ResetCooldown() => readyAt = 0f;
    }

    // Swipes, lunges and multi-hit combos: every Active segment tests its own hitbox each frame and lands
    // at most once, so a player who walks into the swing late still gets hit, but never twice per segment.
    public class EnemyMeleeAttack : EnemyAttackMove
    {
        private bool segmentHit;

        public EnemyMeleeAttack(StateMachine machine, State parent, EnemyContext ctx, MeleeAttackDefinition definition)
            : base(machine, parent, ctx, definition) { }

        protected override void OnSegmentStart(AttackSegment segment) => segmentHit = false;

        protected override void OnSegmentUpdate(AttackSegment segment, float progress)
        {
            if (segmentHit || segment.kind != AttackSegmentKind.Active) return;
            if (ctx.ApplyHitbox != null && ctx.ApplyHitbox(segment.hitbox)) segmentHit = true;
        }
    }

    // Fires the volley on the first frame of each Active segment. The projectile carries the damage from
    // there on, so nothing here needs to know where the player ended up.
    public class EnemyProjectileAttack : EnemyAttackMove
    {
        private readonly ProjectileAttackDefinition projectile;

        public EnemyProjectileAttack(StateMachine machine, State parent, EnemyContext ctx, ProjectileAttackDefinition definition)
            : base(machine, parent, ctx, definition)
        {
            projectile = definition;
        }

        protected override void OnSegmentStart(AttackSegment segment)
        {
            if (segment.kind == AttackSegmentKind.Active) ctx.FireProjectiles?.Invoke(projectile);
        }
    }

    // Checks the gaze every frame of the Active segments and lands at most once per attack: the player
    // has the whole window to turn away, and a single success is enough.
    public class EnemyGazeAttack : EnemyAttackMove
    {
        private readonly GazeAttackDefinition gaze;
        private bool landed;

        public EnemyGazeAttack(StateMachine machine, State parent, EnemyContext ctx, GazeAttackDefinition definition)
            : base(machine, parent, ctx, definition)
        {
            gaze = definition;
        }

        protected override void OnMoveEnter() => landed = false;

        protected override void OnSegmentUpdate(AttackSegment segment, float progress)
        {
            if (landed || segment.kind != AttackSegmentKind.Active) return;
            if (ctx.ApplyGaze != null && ctx.ApplyGaze(gaze)) landed = true;
        }
    }
}
