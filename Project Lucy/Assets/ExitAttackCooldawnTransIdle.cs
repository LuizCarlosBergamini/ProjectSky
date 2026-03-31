using UnityEngine;

public class ExitAttackCooldawnTransIdle : StateMachineBehaviour
{
    private static readonly int AttackToIdleTransition = Animator.StringToHash("Base Layer.Attack -> Base Layer.Idle");
    // Or use userNameHash if you set a transition name in Animator.

    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        CombatManager.Instance.InitiateAttackTimer();
    }
}
