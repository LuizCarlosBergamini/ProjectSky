using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour {

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
    //private bool isGrounded;

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
        if (GrappleController.isGrappling) return;

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
        if (playerActions.Attack.WasPressedThisFrame() && !GrappleController.isGrappling && attackTimeCounter >= timeBetweenAttacks)
        {
            // Reset attack counter
            attackTimeCounter = 0f;
            Attack();
        }
        attackTimeCounter += Time.deltaTime;
    }

    public void Jump()
    {
        Debug.Log("Jump action triggered");
        rb.AddForce(new Vector2(0f, jump), ForceMode2D.Impulse);
    }

    public void Attack()
    {
        animator.SetTrigger("attack");
        hits = Physics2D.CircleCastAll(attackPoint.position, attackRange, transform.right, 0f, attackableLayer);
        foreach (RaycastHit2D hit in hits)
        {
            IDamageable damageable = hit.collider.gameObject.GetComponent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(attackDamage);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (attackPoint == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(attackPoint.position, attackRange);
    }
}