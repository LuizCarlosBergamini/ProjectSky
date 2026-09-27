using System;
using System.Collections.Generic;
using UnityEngine;

namespace HierarchicalStateMachine
{
    /// <summary>One step of a boss fight's escalation, entered when health drops to healthFraction.</summary>
    [Serializable]
    public class BossPhase
    {
        public string name = "Fase";

        [Tooltip("A fase comeca quando a vida cai para esta fracao (1 = desde o inicio, 0.6 = 60%).")]
        [Range(0f, 1f)] public float healthFraction = 1f;

        [Tooltip("Multiplica a recarga de cada ataque nesta fase (0.8 = 20% mais rapido).")]
        [Min(0.05f)] public float cooldownMultiplier = 1f;

        [Tooltip("Multiplica a pausa entre um ataque e outro nesta fase.")]
        [Min(0.05f)] public float globalCooldownMultiplier = 1f;

        [Tooltip("Ataque usado assim que a fase comeca (se for valido). Vazio = nenhum.")]
        public EnemyAttackDefinition openingAttack;
    }

    /// <summary>
    /// Everything one enemy's attack AI needs: which attacks it has and how the selector chooses between them.
    /// Assigned on EnemyStateDriver. Enemies without one get a runtime set built from the driver's old
    /// melee/ranged fields (CreateLegacy), so they run through the same selector.
    /// </summary>
    [CreateAssetMenu(fileName = "AttackSet", menuName = "Enemy/Attack Set")]
    public class EnemyAttackSet : ScriptableObject
    {
        [Tooltip("Ataques disponiveis. Cada um vira um estado filho de EnemyAttack.")]
        public List<EnemyAttackDefinition> attacks = new();

        [Header("Ritmo")]
        [Tooltip("Pausa minima entre o fim de um ataque e o comeco do proximo (segundos).")]
        [Min(0f)] public float globalCooldown = 0.6f;

        [Header("Selecao")]
        [Tooltip("Multiplica a nota do ultimo ataque usado, para o chefe variar.")]
        [Range(0f, 1f)] public float repeatPenalty = 0.35f;

        [Tooltip("Multiplica a nota do penultimo ataque usado.")]
        [Range(0f, 1f)] public float secondRepeatPenalty = 0.75f;

        [Tooltip("Ataques com nota ate esta fracao abaixo da melhor entram no sorteio final (0.15 = 15%).")]
        [Range(0f, 1f)] public float tiebreakWindow = 0.15f;

        [Tooltip("Nota de um ataque na borda do proprio alcance (1 = sem preferencia pelo centro).")]
        [Range(0.05f, 1f)] public float rangeEdgeScore = 0.6f;

        [Tooltip("Distancia horizontal considerada 'colado no chefe' para o bonus de proximidade.")]
        [Min(0f)] public float closeRange = 2.5f;

        [Header("Fases")]
        [Tooltip("Da primeira para a ultima; healthFraction decrescente. Vazio = uma fase so.")]
        public List<BossPhase> phases = new();

        [Header("Reposicionamento (sem ataque valido)")]
        [Tooltip("Pode recuar para entrar no alcance do proximo ataque.")]
        public bool allowRetreat = true;

        [Tooltip("Tempo maximo recuando entre dois ataques.")]
        [Min(0f)] public float maxRetreatTime = 1.2f;

        [Tooltip("Velocidade ao recuar, relativa a moveSpeed.")]
        [Min(0.1f)] public float retreatSpeedMultiplier = 0.8f;

        [Tooltip("Acima desta distancia o chefe corre (animacao Enemy_Running) em vez de andar.")]
        [Min(0f)] public float runDistance = 8f;

        [Tooltip("Velocidade correndo, relativa a moveSpeed.")]
        [Min(0.1f)] public float runSpeedMultiplier = 1.6f;

        [Tooltip("Folga antes de se mexer para corrigir a distancia.")]
        [Min(0.05f)] public float holdTolerance = 0.4f;

        [Tooltip("Margem para dentro da faixa de alcance ao se reposicionar.")]
        [Min(0f)] public float rangeMargin = 0.3f;

        /// <summary>Index of the phase for this health fraction; 0 when there are no phases.</summary>
        public int GetPhaseIndex(float healthFraction)
        {
            int index = 0;
            for (int i = 0; i < phases.Count; i++)
            {
                if (phases[i] != null && healthFraction <= phases[i].healthFraction) index = i;
            }
            return index;
        }

        public BossPhase GetPhase(int index)
        {
            return index >= 0 && index < phases.Count ? phases[index] : null;
        }

        /// <summary>
        /// The pre-attack-set behaviour as data: the cone swing within attackRange and, when a projectile is
        /// assigned, the straight shot. Built at runtime and never saved; the caller destroys it.
        /// </summary>
        public static EnemyAttackSet CreateLegacy(LegacyAttackSettings settings)
        {
            EnemyAttackSet set = CreateInstance<EnemyAttackSet>();
            set.name = "LegacyAttackSet (runtime)";
            set.hideFlags = HideFlags.DontSave;
            set.globalCooldown = settings.attackCooldown;
            set.allowRetreat = false;
            set.runDistance = float.MaxValue; // the old Chase only ever played Enemy_Running

            MeleeAttackDefinition melee = CreateInstance<MeleeAttackDefinition>();
            melee.name = "Close Attack (runtime)";
            melee.hideFlags = HideFlags.DontSave;
            melee.displayName = "Close Attack";
            melee.animationState = "Enemy_Attack";
            melee.driveAnimation = false;
            melee.minRange = 0f;
            melee.maxRange = settings.attackRange;
            melee.maxTargetHeight = float.MaxValue;
            melee.minTargetHeight = float.MinValue;
            melee.cooldown = 0f;
            float activeTime = 0.05f;
            float hitTime = Mathf.Max(0.01f, settings.attackHitTime);
            melee.segments = new List<AttackSegment>
            {
                new() { label = "wind-up", kind = AttackSegmentKind.WindUp, duration = hitTime },
                new()
                {
                    label = "hit", kind = AttackSegmentKind.Active, duration = activeTime,
                    hitbox = new AttackHitbox
                    {
                        shape = AttackHitShape.Cone, coneRadius = settings.attackRange, coneAngle = settings.attackConeAngle,
                        damage = settings.damage, knockback = settings.knockback, verticalKnockback = settings.verticalKnockback
                    }
                },
                new()
                {
                    label = "recovery", kind = AttackSegmentKind.Recovery,
                    duration = Mathf.Max(0.01f, settings.attackDuration - hitTime - activeTime)
                }
            };
            melee.telegraphColor = Color.clear; // the old enemy had no telegraph
            set.attacks.Add(melee);

            if (settings.hasProjectile)
            {
                ProjectileAttackDefinition ranged = CreateInstance<ProjectileAttackDefinition>();
                ranged.name = "Ranged Attack (runtime)";
                ranged.hideFlags = HideFlags.DontSave;
                ranged.displayName = "Ranged Attack";
                ranged.animationState = "Enemy_RangedAttack";
                ranged.driveAnimation = false;
                ranged.minRange = settings.attackRange;
                ranged.maxRange = settings.detectionRange;
                ranged.maxTargetHeight = float.MaxValue;
                ranged.minTargetHeight = float.MinValue;
                ranged.requiresLineOfSight = true;
                ranged.cooldown = 0f;
                ranged.speed = settings.projectileSpeed;
                ranged.damage = settings.damage;
                ranged.knockback = settings.knockback;
                ranged.aimAtTarget = false;
                float fireTime = Mathf.Max(0.01f, settings.rangedFireTime);
                ranged.segments = new List<AttackSegment>
                {
                    new() { label = "wind-up", kind = AttackSegmentKind.WindUp, duration = fireTime },
                    new() { label = "fire", kind = AttackSegmentKind.Active, duration = 0.05f },
                    new()
                    {
                        label = "recovery", kind = AttackSegmentKind.Recovery,
                        duration = Mathf.Max(0.01f, settings.rangedDuration - fireTime - 0.05f)
                    }
                };
                ranged.telegraphColor = Color.clear;
                set.attacks.Add(ranged);
            }

            return set;
        }

        /// <summary>Destroys a set made by CreateLegacy together with its runtime attack definitions.</summary>
        public static void DestroyRuntime(EnemyAttackSet set)
        {
            if (set == null) return;
            foreach (EnemyAttackDefinition attack in set.attacks)
            {
                if (attack != null) Destroy(attack);
            }
            Destroy(set);
        }
    }

    /// <summary>The EnemyStateDriver fields the legacy set is built from.</summary>
    public struct LegacyAttackSettings
    {
        public float attackRange;
        public float attackDuration;
        public float attackHitTime;
        public float attackCooldown;
        public float attackConeAngle;
        public float verticalKnockback;
        public float damage;
        public float knockback;
        public bool hasProjectile;
        public float projectileSpeed;
        public float rangedDuration;
        public float rangedFireTime;
        public float detectionRange;
    }
}
