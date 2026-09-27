using UnityEngine;

namespace HierarchicalStateMachine
{
    /// <summary>
    /// Fires a volley of EnemyProjectile when each Active segment starts. The projectile carries the damage
    /// from there on, exactly like the original ranged attack.
    /// </summary>
    [CreateAssetMenu(fileName = "ProjectileAttack", menuName = "Enemy/Attacks/Projectile Attack")]
    public class ProjectileAttackDefinition : EnemyAttackDefinition
    {
        [Header("Projetil")]
        [Tooltip("Vazio = usa o projectilePrefab do EnemyStateDriver.")]
        public EnemyProjectile projectilePrefab;

        [Min(0.1f)] public float speed = 12f;
        public float damage = 10f;
        public float knockback = 20f;

        [Tooltip("Projeteis por disparo.")]
        [Min(1)] public int count = 1;

        [Tooltip("Projeteis extras por fase do chefe (fase 1 = +1x, fase 2 = +2x...).")]
        [Min(0)] public int extraProjectilesPerPhase;

        [Tooltip("Abertura total do leque quando ha mais de um projetil (graus).")]
        [Range(0f, 90f)] public float spreadAngle = 14f;

        [Tooltip("Mirar no jogador. Desmarcado = reto para frente.")]
        public bool aimAtTarget = true;

        [Tooltip("Angulo maximo de mira acima/abaixo da horizontal (graus).")]
        [Range(0f, 80f)] public float maxAimAngle = 35f;

        public override EnemyAttackMove CreateState(StateMachine machine, State parent, EnemyContext ctx)
        {
            return new EnemyProjectileAttack(machine, parent, ctx, this);
        }
    }
}
