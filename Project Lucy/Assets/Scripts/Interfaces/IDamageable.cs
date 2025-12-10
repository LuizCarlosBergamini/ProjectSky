using UnityEngine;

public interface IDamageable 
{ 
    void TakeDamage(float amount, Vector2 knockback);
    bool IsAlive();
    void Heal(int amount);

    public bool HasTakenDamage { get; set; }
}
