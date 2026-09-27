using UnityEngine;

namespace HierarchicalStateMachine
{
    /// <summary>
    /// A look that petrifies: during the Active segments, a player inside the cone, in line of sight and
    /// (optionally) facing the enemy is turned to stone for a moment. Turning away is the counterplay.
    /// </summary>
    [CreateAssetMenu(fileName = "GazeAttack", menuName = "Enemy/Attacks/Gaze Attack")]
    public class GazeAttackDefinition : EnemyAttackDefinition
    {
        [Header("Olhar")]
        [Tooltip("Alcance do olhar a partir dos olhos (projectileSpawnPoint).")]
        [Min(0.5f)] public float gazeRange = 12f;

        [Tooltip("Abertura total do cone do olhar (graus).")]
        [Range(10f, 180f)] public float coneAngle = 70f;

        [Tooltip("So petrifica se o jogador estiver olhando para o chefe. Virar de costas escapa.")]
        public bool requireTargetFacing = true;

        [Header("Efeito")]
        [Tooltip("Segundos petrificado (sem controle). 0 = so dano.")]
        [Min(0f)] public float petrifyDuration = 1.8f;

        [Tooltip("Segundos de imunidade depois de sair da pedra, para o efeito nao encadear.")]
        [Min(0f)] public float petrifyImmunity = 3f;

        public float damage = 6f;

        public override EnemyAttackMove CreateState(StateMachine machine, State parent, EnemyContext ctx)
        {
            return new EnemyGazeAttack(machine, parent, ctx, this);
        }
    }
}
