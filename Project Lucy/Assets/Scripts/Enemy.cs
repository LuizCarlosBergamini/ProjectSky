using UnityEngine;

public class Enemy : MonoBehaviour, IDamageable
{
    [SerializeField] private EnemyScriptableObject enemyData;
    [SerializeField] private Animator animator;
    private float currentHealth;
    public bool HasTakenDamage { get; set; }

    private void Start()
    {
        currentHealth = enemyData.maxHealth;
        animator = GetComponent<Animator>();
    }

    public void TakeDamage(float damage)
    {
        HasTakenDamage = true;
        animator.SetTrigger("hitted");
        currentHealth -= damage;
        if (currentHealth <= 0)
        {
            Die();
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
