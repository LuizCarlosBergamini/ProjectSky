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

    [Header("Animacao (opcional)")]
    [Tooltip("Animator do visual. Vazio = o projetil some na hora ao acertar, como o projetil antigo.")]
    [SerializeField] private Animator animator;

    [Tooltip("Estado do Animator tocado ao acertar algo ou ao fim da vida. Vazio = some na hora.")]
    [SerializeField] private string impactState;

    [Tooltip("Segundos do impacto antes de destruir o objeto.")]
    [SerializeField] private float impactDuration = 0.4f;

    [Tooltip("Inclina o sprite na direcao do voo (tiros mirados e leques). Desmarcado = so vira para os lados.")]
    [SerializeField] private bool alignToDirection;

    private Rigidbody2D rb;
    private float damage;
    private float knockbackForce;
    private Vector2 direction = Vector2.right;
    private GameObject owner;
    private bool launched;
    private bool impacting;

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

        // Face travel direction so an asymmetric sprite points the right way. The tilt is applied before the
        // flip, so a shot aimed up-left reads as a mirrored shot aimed up-right instead of an upside-down one.
        float flip = direction.x < 0f ? 180f : 0f;
        float tilt = alignToDirection ? Mathf.Atan2(direction.y, Mathf.Abs(direction.x)) * Mathf.Rad2Deg : 0f;
        transform.rotation = Quaternion.Euler(0f, flip, 0f) * Quaternion.Euler(0f, 0f, tilt);

        Invoke(nameof(Impact), lifeTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!launched || impacting) return;
        // Never hit the enemy that fired it.
        if (owner != null && (other.gameObject == owner || other.transform.IsChildOf(owner.transform))) return;
        // A volley spawns every projectile on the same point, all on the Default layer like the player:
        // without this they hit each other on the first physics step and the whole volley vanished.
        if (other.GetComponentInParent<EnemyProjectile>() != null) return;

        if (IsInLayer(other.gameObject.layer, damageableLayer))
        {
            // The damageable mask is the Default layer, which also holds props and trigger volumes;
            // only something that can actually take the hit stops the projectile.
            IDamageable damageable = other.GetComponentInParent<IDamageable>();
            if (damageable == null) return;

            if (damageable.IsAlive() && !damageable.HasTakenDamage)
            {
                Vector2 knockDirection = new Vector2(direction.x, Mathf.Abs(verticalKnockback)).normalized;
                damageable.TakeDamage(damage, knockDirection * knockbackForce);
            }

            Impact();
            return;
        }

        if (IsInLayer(other.gameObject.layer, blockingLayer)) Impact();
    }

    // Stops where it is and plays the impact clip when the prefab has one; otherwise disappears right away.
    // Used for hits, walls and the end of its life alike, so a shot that misses fizzles out instead of popping.
    private void Impact()
    {
        if (impacting) return;
        impacting = true;
        CancelInvoke(nameof(Impact));

        int hash = string.IsNullOrEmpty(impactState) ? 0 : Animator.StringToHash(impactState);
        if (animator == null || hash == 0 || !animator.HasState(0, hash))
        {
            Destroy(gameObject);
            return;
        }

        if (rb != null) rb.linearVelocity = Vector2.zero;
        foreach (Collider2D hitCollider in GetComponentsInChildren<Collider2D>()) hitCollider.enabled = false;

        animator.Play(hash, 0, 0f);
        Destroy(gameObject, Mathf.Max(0.01f, impactDuration));
    }

    private static bool IsInLayer(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }
}
