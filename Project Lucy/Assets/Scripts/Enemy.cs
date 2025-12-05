using UnityEngine;

public class Enemy : MonoBehaviour, IDamageable
{
    [SerializeField] private EnemyScriptableObject enemyData;
    private float currentHealth;

    private void Start()
    {
        currentHealth = enemyData.maxHealth;
    }

    public void TakeDamage(float damage)
    {
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
