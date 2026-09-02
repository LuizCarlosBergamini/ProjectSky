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

        // --- Awareness (rewritten by the driver every frame) ---
        public Transform Target;
        public bool TargetInDetectionRange;
        public bool TargetInAttackRange;
        public float DistanceToTarget;

        // --- Ground ---
        public bool IsGrounded;

        // --- Movement intent (written by states, executed by the driver) ---
        public float MoveDirection; // -1, 0, 1
        public float MoveSpeed;
        public float StopDistance;
        public bool CanTurn = true;
        public bool FacingRight = true;

        // --- Combat ---
        public float Health;
        public float AttackDuration;
        public float AttackHitTime;
        public float AttackCooldownDuration;
        public float AttackCooldown;
        public float DeathDelay;
        public bool DeathRequested;
        public bool IsDead;

        // --- Driver callbacks (states never touch the scene directly) ---
        public Action<string> PlayAnimation;
        public Action DealAttackDamage;
        public Action OnDeathFinished;

        public bool HasTarget => Target != null;
    }
}
