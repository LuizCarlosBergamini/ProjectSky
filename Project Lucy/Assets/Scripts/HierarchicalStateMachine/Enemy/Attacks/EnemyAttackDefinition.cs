using System;
using System.Collections.Generic;
using UnityEngine;

namespace HierarchicalStateMachine
{
    public enum AttackSegmentKind
    {
        WindUp,   // telegraph on, no damage
        Active,   // the damaging window
        Recovery  // punish window, no damage
    }

    public enum AttackHitShape
    {
        Box,  // axis-aligned box in front of the enemy, mirrored with its facing
        Cone  // the original melee test: a slice of circle in front of the enemy
    }

    /// <summary>Where and how hard one Active segment of a melee attack hits.</summary>
    [Serializable]
    public class AttackHitbox
    {
        public AttackHitShape shape = AttackHitShape.Box;

        [Tooltip("Centro da caixa em unidades do mundo. X positivo = para onde o inimigo olha; Y a partir dos pes.")]
        public Vector2 offset = new Vector2(1f, 1f);
        public Vector2 size = new Vector2(2f, 2f);

        [Tooltip("Cone: raio a partir do centro do inimigo.")]
        public float coneRadius = 2f;
        [Range(10f, 360f)] public float coneAngle = 100f;

        public float damage = 10f;
        public float knockback = 20f;
        [Tooltip("Componente vertical do empurrao (0 = so horizontal).")]
        public float verticalKnockback = 0.5f;
    }

    /// <summary>
    /// One slice of an attack's timeline: which frames of the clip it shows and for how long. The attack
    /// state stretches the clip so these frames fill exactly this duration, which is what keeps the damage
    /// window on the frames the player sees.
    /// </summary>
    [Serializable]
    public class AttackSegment
    {
        public string label;
        public AttackSegmentKind kind;

        [Tooltip("Primeiro quadro do clipe mostrado neste trecho (0 = primeiro quadro).")]
        [Min(0)] public int firstFrame;

        [Tooltip("Ultimo quadro do clipe mostrado neste trecho (inclusivo).")]
        [Min(0)] public int lastFrame;

        [Tooltip("Duracao do trecho em segundos. A animacao acelera ou desacelera para caber exatamente.")]
        [Min(0.01f)] public float duration = 0.3f;

        [Tooltip("Distancia que o corpo avanca para frente durante o trecho (investida). 0 = parado.")]
        public float advanceDistance;

        [Tooltip("Usado apenas em trechos Active de ataques corpo a corpo.")]
        public AttackHitbox hitbox = new AttackHitbox();

        public int FrameCount => Mathf.Max(1, lastFrame - firstFrame + 1);
    }

    /// <summary>
    /// How much the selector likes this attack in a given situation. Every value multiplies the score;
    /// 1 means "does not care".
    /// </summary>
    [Serializable]
    public class AttackScoring
    {
        [Tooltip("Peso base. Maior = escolhido com mais frequencia quando valido.")]
        public float weight = 1f;

        [Tooltip("Multiplicador a partir da fase 1 do chefe (fases ficam no Attack Set).")]
        public float enragedMultiplier = 1f;

        [Tooltip("Jogador no ar (pulando, caindo).")]
        public float targetAirborne = 1f;

        [Tooltip("Jogador no meio do proprio ataque.")]
        public float targetAttacking = 1f;

        [Tooltip("Jogador se afastando do chefe.")]
        public float targetRetreating = 1f;

        [Tooltip("Jogador pendurado no gancho.")]
        public float targetGrappling = 1f;

        [Tooltip("Jogador petrificado (olhar da Medusa).")]
        public float targetPetrified = 1f;

        [Tooltip("Jogador olhando para o chefe.")]
        public float targetFacing = 1f;

        [Tooltip("Jogador de costas para o chefe.")]
        public float targetFacingAway = 1f;

        [Tooltip("Bonus por segundo que o jogador passa colado no chefe (0.35 = +35% por segundo). Pune quem fica perto.")]
        public float lingerBonusPerSecond;

        [Tooltip("Teto do bonus de proximidade.")]
        [Min(1f)] public float maxLingerMultiplier = 2f;
    }

    /// <summary>
    /// Tuning for one attack. Each asset becomes one child state of EnemyAttack; the subclasses decide what
    /// kind of state (melee, projectile, gaze). Everything here can be changed in the Inspector during Play
    /// Mode, since the states read the asset live.
    /// </summary>
    public abstract class EnemyAttackDefinition : ScriptableObject
    {
        [Header("Identidade")]
        public string displayName;

        [Tooltip("Nome do estado no Animator Controller do inimigo.")]
        public string animationState;

        [Tooltip("Marcado: a animacao e esticada trecho a trecho para bater com as duracoes abaixo. " +
                 "Desmarcado: toca o clipe uma vez na velocidade normal (inimigos antigos).")]
        public bool driveAnimation = true;

        [Tooltip("Quadros por segundo do clipe (preenchido pelo builder).")]
        [Min(1f)] public float sourceFrameRate = 10f;

        [Tooltip("Total de quadros do clipe (preenchido pelo builder).")]
        [Min(1)] public int totalFrames = 1;

        [Header("Linha do tempo (wind-up / active / recovery)")]
        public List<AttackSegment> segments = new();

        [Header("Alcance")]
        [Tooltip("Distancia horizontal minima ate o jogador para o ataque ser valido.")]
        [Min(0f)] public float minRange;

        [Tooltip("Distancia horizontal maxima ate o jogador para o ataque ser valido.")]
        [Min(0f)] public float maxRange = 2f;

        [Tooltip("Altura maxima dos pes do jogador acima dos pes do chefe.")]
        public float maxTargetHeight = 3f;

        [Tooltip("Altura minima (negativa = jogador abaixo do chefe).")]
        public float minTargetHeight = -2f;

        [Tooltip("Nao escolher se houver parede/chao entre o chefe e o jogador.")]
        public bool requiresLineOfSight;

        [Header("Recarga e fase")]
        [Tooltip("Segundos ate este ataque poder ser usado de novo.")]
        [Min(0f)] public float cooldown = 2f;

        [Tooltip("Fase minima do chefe para liberar o ataque (0 = desde o inicio).")]
        [Min(0)] public int minPhase;

        [Header("Selecao")]
        public AttackScoring scoring = new();

        [Header("Telegrafo")]
        [Tooltip("Cor que pulsa no sprite durante os trechos WindUp. Alfa 0 = sem telegrafo.")]
        public Color telegraphColor = new Color(1f, 0.55f, 0.55f, 1f);

        [Header("Deslocamento do sprite")]
        [Tooltip("Quanto o corpo termina a frente dentro do proprio sprite. Aplicado ao fim do ataque para a " +
                 "troca de volta ao Idle nao puxar o chefe para tras.")]
        public float exitForwardOffset;

        public abstract EnemyAttackMove CreateState(StateMachine machine, State parent, EnemyContext ctx);

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public float MidRange => (minRange + maxRange) * 0.5f;

        /// <summary>Seconds from the start of the attack to its first damaging frame.</summary>
        public float TimeToFirstActive
        {
            get
            {
                float t = 0f;
                foreach (AttackSegment segment in segments)
                {
                    if (segment.kind == AttackSegmentKind.Active) return t;
                    t += segment.duration;
                }
                return t;
            }
        }

        public float TotalDuration
        {
            get
            {
                float t = 0f;
                foreach (AttackSegment segment in segments) t += segment.duration;
                return t;
            }
        }

        public float TotalOf(AttackSegmentKind kind)
        {
            float t = 0f;
            foreach (AttackSegment segment in segments)
            {
                if (segment.kind == kind) t += segment.duration;
            }
            return t;
        }

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            if (maxRange < minRange) maxRange = minRange;
            if (maxTargetHeight < minTargetHeight) maxTargetHeight = minTargetHeight;
            foreach (AttackSegment segment in segments)
            {
                if (segment == null) continue;
                segment.firstFrame = Mathf.Clamp(segment.firstFrame, 0, Mathf.Max(0, totalFrames - 1));
                segment.lastFrame = Mathf.Clamp(segment.lastFrame, segment.firstFrame, Mathf.Max(0, totalFrames - 1));
            }
        }
#endif
    }
}
