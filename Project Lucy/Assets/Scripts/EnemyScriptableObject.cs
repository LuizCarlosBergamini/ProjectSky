using UnityEngine;

[CreateAssetMenu(fileName ="EnemyScriptableObject", menuName ="ScriptableObjects/Enemy")]
public class EnemyScriptableObject : ScriptableObject
{
    // Base stats enemy
    public float moveSpeed;
    public float maxHealth;
    public float damage;
    public float knockbackForce;
}
