using UnityEngine;

// Spawned by EnemyStateDriver during the RangedAttack state. It carries its damage numbers
// rather than reading the enemy data itself, so one prefab can serve enemies of any strength.
[RequireComponent(typeof(Rigidbody2D))]
public class EnemyProjectile : MonoBehaviour
{
    [SerializeField] private float lifeTime = 4f;
    [SerializeField] private LayerMask damageableLayer; // what it can hurt (the Player)
    [SerializeField] private LayerMask blockingLayer;   // what stops it (Ground)
    [SerializeField] private float verticalKnockback = 0.5f;

    private Rigidbody2D rb;
    private float damage;
    private float knockbackForce;
    private Vector2 direction = Vector2.right;
    private GameObject owner;
    private bool launched;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        // Kinematic so it flies straight and ignores gravity; the trigger still reports hits.
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
    }

    public void Launch(Vector2 travelDirection, float speed, float damageAmount, float knockback, GameObject firedBy)
    {
        direction = travelDirection.normalized;
        damage = damageAmount;
        knockbackForce = knockback;
        owner = firedBy;
        launched = true;

        if (rb == null) rb = GetComponent<Rigidbody2D>();
        rb.linearVelocity = direction * speed;

        // Face travel direction so an asymmetric sprite points the right way.
        transform.rotation = Quaternion.Euler(0f, direction.x < 0f ? 180f : 0f, 0f);

        Destroy(gameObject, lifeTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!launched) return;
        // Never hit the enemy that fired it.
        if (owner != null && (other.gameObject == owner || other.transform.IsChildOf(owner.transform))) return;

        if (IsInLayer(other.gameObject.layer, damageableLayer))
        {
            IDamageable damageable = other.GetComponentInParent<IDamageable>();
            if (damageable != null && damageable.IsAlive() && !damageable.HasTakenDamage)
            {
                Vector2 knockDirection = new Vector2(direction.x, Mathf.Abs(verticalKnockback)).normalized;
                damageable.TakeDamage(damage, knockDirection * knockbackForce);
            }

            Destroy(gameObject);
            return;
        }

        if (IsInLayer(other.gameObject.layer, blockingLayer)) Destroy(gameObject);
    }

    private static bool IsInLayer(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }
}
