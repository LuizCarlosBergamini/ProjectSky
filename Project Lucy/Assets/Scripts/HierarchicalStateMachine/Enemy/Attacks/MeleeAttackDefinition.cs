using UnityEngine;

namespace HierarchicalStateMachine
{
    /// <summary>
    /// Close-range hit: each Active segment tests its own hitbox and can land once. Covers swipes, multi-hit
    /// combos (one Active segment per hit) and lunges (advanceDistance on the Active segment).
    /// </summary>
    [CreateAssetMenu(fileName = "MeleeAttack", menuName = "Enemy/Attacks/Melee Attack")]
    public class MeleeAttackDefinition : EnemyAttackDefinition
    {
        public override EnemyAttackMove CreateState(StateMachine machine, State parent, EnemyContext ctx)
        {
            return new EnemyMeleeAttack(machine, parent, ctx, this);
        }
    }
}
