using UnityEngine;

public sealed class PlayerStateMachine: MonoBehaviour
{
    public PlayerState CurrentState { get; private set; }

    public void Initialize(PlayerState startingState)
    {
        CurrentState = startingState;
        CurrentState.Enter();
    }

    public void ChangeState(PlayerState newState)
    {
        if (CurrentState == newState) return;
        CurrentState.Exit();
        CurrentState = newState;
        CurrentState.Enter();
    }

    public void LogicUpdateState()
    {
        CurrentState.LogicUpdate();
    }

    public void PhysicsUpdateState()
    {
        CurrentState.PhysicsUpdate();
    }

    public void HandleInput()
    {
        CurrentState.HandleInput();
    }
}
