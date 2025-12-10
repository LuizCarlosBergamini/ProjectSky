using System.Collections;
using UnityEngine;

public class Enemy : MonoBehaviour, IDamageable
{
    [SerializeField] private EnemyScriptableObject enemyData;
    [SerializeField] private Animator animator;
    private Rigidbody2D rb;
    private float currentHealth;
    public bool HasTakenDamage { get; set; }
    public bool canMove = true;

    // --- Knockback handling (non-blocking) ---
    private Coroutine knockbackCoroutine;
    private bool isKnockedBack = false;
    [SerializeField] private float knockbackLandingGrace = 0.05f; // short delay to ignore immediate grounded checks

    public Transform groundCheck;      // Assign an empty GameObject positioned at the player's feet
    public float groundCheckRadius = 0.4f; // Size of the detection circle
    public LayerMask groundLayer;      // Select the layer that counts as "Ground"
    bool IsGrounded => Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

    [SerializeField] private float verticalKnockback = 0.5f;

    private void Start()
    {
        currentHealth = enemyData.maxHealth;
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
    }

    public void TakeDamage(float damage, Vector2 knockback)
    {
        HasTakenDamage = true;
        animator.SetTrigger("hitted");
        currentHealth -= damage;

        rb.AddForce(knockback, ForceMode2D.Impulse);

        // Start a coroutine to wait for landing instead.
        if (knockbackCoroutine != null)
        {
            StopCoroutine(knockbackCoroutine);
            knockbackCoroutine = null;
        }
        
        knockbackCoroutine = StartCoroutine(HandleKnockbackLanding());

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private IEnumerator HandleKnockbackLanding()
    {
        isKnockedBack = true;
        canMove = false;

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

        canMove = true;
        HasTakenDamage = false;
        isKnockedBack = false;
        knockbackCoroutine = null;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        Collider2D collider = collision.collider;
        IDamageable damageable = collider.GetComponent<IDamageable>();

        if (damageable != null && !damageable.HasTakenDamage)
        {
            Vector2 knockbackDirection = (collider.transform.position - transform.position).normalized;
            // combine horizontal direction with a fixed upward component
            // keep horizontal sign from knockbackDirection.x and force an upward Y value
            float upward = Mathf.Abs(verticalKnockback); // ensure upward is positive
            Vector2 combinedDir = new Vector2(knockbackDirection.x, upward).normalized;

            Vector2 knockback = combinedDir * enemyData.knockbackForce;
            damageable.TakeDamage(enemyData.damage, knockback);
        }
    }

    private void Die()
    {
        // Add death effects here (animations, sounds, etc.)
        Destroy(gameObject);
    }

    public bool IsAlive()
    {
        return currentHealth > 0;
    }
    
    public void Heal(int amount)
    {
        currentHealth += amount;
        if (currentHealth > enemyData.maxHealth)
        {
            currentHealth = enemyData.maxHealth;
        }
    }
}
