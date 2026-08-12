using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerAttackState : PlayerState
{
    [SerializeField] private PlayerMovement ctx;
    
    [Header("Attack")] private RaycastHit2D[] hits;
    [SerializeField] private float attackDamage = 10f;
    [SerializeField] private Transform attackPoint;
    [SerializeField] private float attackRange = 1.5f;
    [SerializeField] private LayerMask attackableLayer;
    [SerializeField] private float knockbackForce = 20f;
    public bool shouldBeDamaging { get; private set; } = false;
    private List<IDamageable> damageables = new List<IDamageable>();
    [SerializeField] private float verticalKnockback = 0.5f;

    public override void Enter()
    {
        Debug.Log("Entering Attack State");
        ctx.animator.Play("Player_Attack");
    }

    public override void Exit() { }

    public override void HandleInput()
    {
        if (ctx.playerActions.Attack.WasPressedThisFrame())
        {
            AnimatorStateInfo stateInfo = ctx.animator.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.IsName("TransitionAttack1"))
            {
                ctx.animator.Play("Player_Attack2");
            }
        }
    }

    public override void LogicUpdate()
    {
        // Here time > 0 ensures that this is only checked after Player_Attack animation is playing
        if (time > 0.1f)
        {
            AnimatorStateInfo stateInfo = ctx.animator.GetCurrentAnimatorStateInfo(0);
            
            if (stateInfo.IsName("Player_Idle"))
            {
                ctx.ResetAttackCooldown();
                ctx.ChangeState(ctx.playerIdleState);
            }
        }
    }

    public override void PhysicsUpdate() { }
    
    private void ReturnAttackablesToDamageable()
    {
        foreach (IDamageable thingThatWasHit in damageables)
        {
            thingThatWasHit.HasTakenDamage = false;
        }

        damageables.Clear();
    }

    public IEnumerator DamageWhileSlashIsActive()
    {
        shouldBeDamaging = true;

        while (shouldBeDamaging)
        {
            hits = Physics2D.CircleCastAll(attackPoint.position, attackRange, transform.right, 0f, attackableLayer);
            foreach (RaycastHit2D hit in hits)
            {
                IDamageable damageable = hit.collider.gameObject.GetComponent<IDamageable>();
                if (damageable != null && !damageable.HasTakenDamage)
                {
                    Vector2 knockbackDirection = (hit.collider.transform.position - transform.position).normalized;
                    // combine horizontal direction with a fixed upward component
                    // keep horizontal sign from knockbackDirection.x and force an upward Y value
                    float upward = Mathf.Abs(verticalKnockback); // ensure upward is positive
                    Vector2 combinedDir = new Vector2(knockbackDirection.x, upward).normalized;

                    Vector2 knockback = combinedDir * knockbackForce;
                    damageable.TakeDamage(attackDamage, knockback);
                    damageables.Add(damageable);
                }
            }

            yield return null;
        }

        ReturnAttackablesToDamageable();
    }

    public void ShouldBeDamagingToFalse()
    {
        shouldBeDamaging = false;

    }

    private void OnDrawGizmosSelected()
    {
        if (attackPoint == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(attackPoint.position, attackRange);
    }
}
