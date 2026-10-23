using System;
using UnityEngine;

namespace HierarchicalStateMachine
{
    /// <summary>
    /// A ranged enemy that backs off: after taking <see cref="hitsToDash"/> hits (in a row or not) it gets away from
    /// the player, still facing them - sliding back, or vanishing and reappearing further back (teleport). Hits taken
    /// during an attack count too; the escape waits for the attack to end.
    /// </summary>
    [Serializable]
    public class EnemyDashSettings
    {
        [Tooltip("Liga o dash para tras depois de levar golpes.")]
        public bool enabled;

        [Tooltip("Marcado: some e reaparece mais para tras (com os raios do Teleport Sparks do inimigo). " +
                 "Desmarcado: desliza para tras tocando a animacao abaixo.")]
        public bool teleport;

        [Tooltip("Golpes recebidos (seguidos ou nao) que disparam o dash. A contagem zera a cada dash.")]
        [Min(1)] public int hitsToDash = 2;

        [Tooltip("Distancia percorrida para tras, em unidades. Para antes se encontrar uma parede ou a barreira.")]
        [Min(0f)] public float distance = 5f;

        [Tooltip("Duracao do dash, em segundos. No teleporte: quanto tempo fica sumido antes de reaparecer.")]
        [Min(0.05f)] public float duration = 0.35f;

        [Tooltip("Teleporte: segundos depois de reaparecer em que ainda nao age nem leva dano.")]
        [Min(0f)] public float reappearTime = 0.2f;

        [Tooltip("Teleporte: sem espaco para tras (encurralado na borda), reaparece do outro lado do jogador, a " +
                 "Distance dele, ainda respeitando paredes e a barreira da arena.")]
        public bool behindTargetWhenCornered = true;

        [Tooltip("Intervalo minimo entre dois dashes, em segundos, contado do fim do anterior.")]
        [Min(0f)] public float cooldown = 3f;

        [Tooltip("Nao leva dano durante o dash.")]
        public bool invulnerable = true;

        [Tooltip("Estado do Animator tocado durante o dash deslizado.")]
        public string animationState = "Enemy_Dash";

        [Tooltip("Velocidade da animacao do dash (1 = a do clipe).")]
        [Min(0.1f)] public float animationSpeed = 1f;
    }

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

        // --- Evasive dash (EnemyDashSettings on the driver) ---
        public EnemyDashSettings Dash;
        public bool DashRequested;        // set by the driver after enough hits, consumed by EnemyDash
        public bool Dashing;
        public float DashReadyTime;       // Clock time from which the next dash may start

        // --- Driver callbacks (states never touch the scene directly) ---
        public Action<string> PlayAnimation;
        public Func<string, bool> HasAnimation;
        public Action<string, float, float> PlayAttackAnimation; // state, normalized start, speed
        public Action<float> SetAnimationSpeed;
        public Action<string, float> PlaySettleAnimation;         // return-to-rest clip (missing = Enemy_Idle), seconds to fill
        public Action<Color?> SetTelegraph;                        // null = off
        public Func<AttackHitbox, bool> ApplyHitbox;               // true when damage landed
        public Action<ProjectileAttackDefinition> FireProjectiles;
        public Func<GazeAttackDefinition, bool> ApplyGaze;         // true when the gaze landed
        public Action FaceTarget;
        public Action<float> CommitForwardOffset;
        public Action<EnemyAttackDefinition, int> SetHurtboxFrame; // (null, 0) = back to the resting hurtbox
        public Func<float, bool> IsPathBlocked;                    // direction sign -> wall right there
        public Action<float, float> TeleportBy;                    // direction sign, distance (stops at walls)
        public Action<bool> SetVisible;                            // teleport: hide the body, then show it again
        public Action EmitTeleportSparks;                          // lightning where the body is right now
        public Action OnDeathFinished;

        public bool HasTarget => Target != null;
    }
}
