using System;
using UnityEngine;

namespace HierarchicalStateMachine
{
    [Serializable]
    public class EnemyContext
    {
        // --- References (filled by the driver) ---
        public Rigidbody2D rb;
        public Animator animator;
        public Transform self;
        public EnemyScriptableObject Data;
        public EnemyAttackSet AttackSet;

        // --- Awareness (rewritten by the driver every frame) ---
        public Transform Target;
        public bool TargetInDetectionRange;
        public float DistanceToTarget;
        public float HorizontalDistance;  // |dx|, what the attack range bands are measured in
        public float TargetHeight;        // player's feet above this enemy's feet
        public Vector2 TargetVelocity;
        public bool TargetGrounded = true;
        public bool TargetAttacking;
        public bool TargetGrappling;
        public bool TargetPetrified;
        public bool TargetFacingSelf;
        public bool TargetRetreating;
        public bool TargetInLineOfSight;
        public float TimeTargetClose;     // seconds the player has stayed within AttackSet.closeRange

        // --- Activation ---
        public bool Activated = true;     // bosses wait for their arena gate to close

        // --- Ground ---
        public bool IsGrounded;

        // --- Movement intent (written by states, executed by the driver) ---
        public float MoveDirection; // -1, 0, 1
        public float MoveSpeed;
        public float MoveSpeedMultiplier = 1f;
        public float StopDistance;
        public bool CanTurn = true;
        public bool FacingRight = true;
        public bool FaceMoveDirection;    // retreating: look where it goes instead of at the player
        public bool HasForcedVelocity;    // lunges: the attack owns the horizontal speed
        public float ForcedVelocityX;

        // --- Combat ---
        public float Health;
        public float MaxHealth;
        public float Clock;               // the driver's own time, for per-attack cooldowns
        public float AttackCooldown;      // pause after any attack before the next one
        public int Phase;
        public float PhaseCooldownMultiplier = 1f;
        public float PhaseGlobalCooldownMultiplier = 1f;
        public EnemyAttackDefinition PendingOpener;
        public bool InAttack;
        public float DeathDelay;
        public bool DeathRequested;
        public bool IsDead;
        public bool LogDecisions;

        // --- Driver callbacks (states never touch the scene directly) ---
        public Action<string> PlayAnimation;
        public Func<string, bool> HasAnimation;
        public Action<string, float, float> PlayAttackAnimation; // state, normalized start, speed
        public Action<float> SetAnimationSpeed;
        public Action<Color?> SetTelegraph;                        // null = off
        public Func<AttackHitbox, bool> ApplyHitbox;               // true when damage landed
        public Action<ProjectileAttackDefinition> FireProjectiles;
        public Func<GazeAttackDefinition, bool> ApplyGaze;         // true when the gaze landed
        public Action FaceTarget;
        public Action<float> CommitForwardOffset;
        public Func<float, bool> IsPathBlocked;                    // direction sign -> wall right there
        public Action OnDeathFinished;

        public bool HasTarget => Target != null;
    }
}
