using UnityEngine;

public interface IDamageable 
{ 
    void TakeDamage(float amount);
    bool IsAlive();
    void Heal(int amount);
}
