using HierarchicalStateMachine;
using UnityEngine;

public class LinkToFunctions : MonoBehaviour
{
    [SerializeField] private PlayerStateDriver playerStateDriver;

    void StartAttacking()
    {
        playerStateDriver.OpenComboWindow();
    }

    void StopAttacking()
    {
        playerStateDriver.CloseComboWindow();
        playerStateDriver.FinishAttack();
    }
}
