using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour, IDamageable {

    private Vector2 movement;
    private Rigidbody2D rb;
    [SerializeField] private float moveSpeed = 10f;

    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private GrappleController GrappleController;

    public PlayerInputs.InGameActions playerActions;

    public float jump = 2f;
    // Ajuste de responsividade do controle (quanto maior, mais rápido chega à velocidade alvo)
    [SerializeField] private float velocityResponsiveness = 1f;

    public Transform groundCheck;      // Assign an empty GameObject positioned at the player's feet
    public float groundCheckRadius = 0.4f; // Size of the detection circle
    public LayerMask groundLayer;      // Select the layer that counts as "Ground"

    [SerializeField] private float footstepSoundDelay = 1f;
    private float footstepSoundDuration = 0f;
    [SerializeField] private AudioClip footstepAudioClip;

    [Header("Attack")]
    private RaycastHit2D[] hits;
    [SerializeField] private float attackDamage = 10f;
    [SerializeField] private Transform attackPoint;
    [SerializeField] private float attackRange = 1.5f;
    [SerializeField] private LayerMask attackableLayer;
    [SerializeField] private float timeBetweenAttacks = 0.5f;
    private float attackTimeCounter;
    [SerializeField] private float knockbackForce = 20f;
    public bool shouldBeDamaging { get; private set; } = false;
    private List<IDamageable> damageables = new List<IDamageable>();
    [SerializeField] private float verticalKnockback = 0.5f;

    [Header("Health and Damage Taken")]
    [SerializeField] private float health = 100f;
    public bool HasTakenDamage { get; set; } = false;
    public bool canWalk = true;

    // --- Knockback handling (non-blocking) ---
    private Coroutine knockbackCoroutine;
    private bool isKnockedBack = false;
    [SerializeField] private float knockbackLandingGrace = 0.05f; // short delay to ignore immediate grounded checks

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        GrappleController = GetComponent<GrappleController>();

        playerActions = new PlayerInputs().InGame;
    }

    private void OnEnable()
    {
        playerActions.Enable();
    }

    public void TogglePlayerInGameAction(bool toggle)
    {
        if (toggle) playerActions.Enable();
        else playerActions.Disable();
    }

    bool IsGrounded => Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

    public void Move()
    {
        if (GrappleController.isGrappling || !canWalk) return;

        movement = playerActions.Movement.ReadValue<Vector2>();

        if (IsGrounded && movement.x != 0 && AudioManager.instance != null && footstepAudioClip != null)
        {
            footstepSoundDuration += Time.fixedDeltaTime;
            if (footstepSoundDuration > footstepSoundDelay)
            {
                Debug.Log("foo");
                footstepSoundDuration = 0;
                AudioManager.instance.PlayWithVariation(footstepAudioClip);
            }
        }

        animator.SetBool("IsWalking", movement.x != 0);

        if (movement.x < 0) transform.rotation = Quaternion.Euler(new Vector3(transform.rotation.x, 180f, transform.rotation.z));
        else if (movement.x > 0) transform.rotation = Quaternion.Euler(new Vector3(transform.rotation.x, 0, transform.rotation.z)); ;

        // Converte input em velocidade alvo
        float targetVelX = movement.x * moveSpeed;

        // Calcula diferença entre velocidade alvo e velocidade atual
        float velDiff = targetVelX - rb.linearVelocity.x;

        // Força necessária aproximada para corrigir a diferença num passo de física
        float forceX = velDiff * rb.mass * velocityResponsiveness;

        // Aplica força horizontal (ForceMode2D.Force é contínuo e respeita massa)
        rb.AddForce(new Vector2(forceX, 0f), ForceMode2D.Force);
    }

    private void FixedUpdate()
    {
        if (GrappleController.isGrappling) return;
        Move();
    }

    private void Update()
    {
        if (playerActions.Jump.WasPressedThisFrame() && IsGrounded)
        {
            Jump();
        }
        if (!GrappleController.isGrappling)
        {
            if (playerActions.Attack.WasPressedThisFrame() && attackTimeCounter >= timeBetweenAttacks)
            {
                // Reset attack counter
                attackTimeCounter = 0f;
                animator.SetTrigger("attack");
                animator.SetBool("IsAttacking", true);
            }

            // Animate jump
            animator.SetFloat("yVelocity", rb.linearVelocityY);
            animator.SetBool("isGrounded", IsGrounded);
        }
        attackTimeCounter += Time.deltaTime;


    }

    public void Jump()
    {
        Debug.Log("Jump action triggered");
        rb.AddForce(new Vector2(0f, jump), ForceMode2D.Impulse);
    }


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

    #region Animation Triggers

    public void ShouldBeDamagingToTrue()
    {
        shouldBeDamaging = true;
    }

    public void ShouldBeDamagingToFalse()
    {
        shouldBeDamaging = false;
        
    }

    public void IsAttackingToFalse()
    {
        animator.SetBool("IsAttacking", false);
    }

    #endregion

    private void OnDrawGizmosSelected()
    {
        if (attackPoint == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(attackPoint.position, attackRange);
    }

    public bool IsAlive()
    {
        return health > 0;
    }

    public void Heal(int amount)
    {
        health += amount;
    }

    public void TakeDamage(float amount, Vector2 knockback)
    {
        animator.SetTrigger("hitted");
        HasTakenDamage = true;
        health -= amount;
        if (health < 0)
        {
            Die();
            return;
        }
        rb.AddForce(knockback, ForceMode2D.Impulse);
        // Start a coroutine to wait for landing instead.
        if (knockbackCoroutine != null)
        {
            StopCoroutine(knockbackCoroutine);
            knockbackCoroutine = null;
        }
        knockbackCoroutine = StartCoroutine(HandleKnockbackLanding());
        HasTakenDamage = false;
    }

    private IEnumerator HandleKnockbackLanding()
    {
        isKnockedBack = true;
        canWalk = false;

        // small grace time to avoid immediately considering already-grounded state
        if (knockbackLandingGrace > 0f)
            yield return new WaitForSeconds(knockbackLandingGrace);

        // Wait until the player becomes grounded again
        while (!IsGrounded)
        {
            yield return null;
        }

        // Optionally wait one FixedUpdate to ensure physics settled
        yield return new WaitForFixedUpdate();

        canWalk = true;
        HasTakenDamage = false;
        isKnockedBack = false;
        knockbackCoroutine = null;
    }

    public void Die()
    {
        // Add death effects here (animations, sounds, etc.)
        Destroy(gameObject);
    }
}