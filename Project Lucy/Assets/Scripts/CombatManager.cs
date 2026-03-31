using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class CombatManager : MonoBehaviour
{
    public static CombatManager Instance;
    
    public bool canReceiveInput;
    public bool inputReceived;
    public float timeBetweenAttacks = 5f;
    private Coroutine _attackTimerCoroutine;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
        }
        Instance = this;
    }

    private void Update()
    {
        
    }

    public void InitiateAttackTimer()
    {
        if (_attackTimerCoroutine != null)
        {
            StopCoroutine(TimerBeforeAttack());
        }
        _attackTimerCoroutine = StartCoroutine(TimerBeforeAttack());
    }

    private IEnumerator TimerBeforeAttack()
    {
        Debug.Log("canReceiveInput: " + canReceiveInput);
        yield return new WaitForSeconds(timeBetweenAttacks);
        canReceiveInput = true;
        Debug.Log("canReceiveInput: " + canReceiveInput);
    } 

    public void Attack(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            if (canReceiveInput)
            {
                Debug.Log("changing input to true");
                inputReceived = true;
                canReceiveInput = false;
            }
            else
            {
                return;
            }
        }
    }

    public void InputManager()
    {
        if (!canReceiveInput)
        {
            canReceiveInput = true;
        }
        else
        {
            canReceiveInput = false;
        }
    }
}
