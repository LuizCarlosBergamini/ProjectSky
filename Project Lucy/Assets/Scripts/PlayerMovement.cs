using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{

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
    public float groundCheckRadius = 0.2f; // Size of the detection circle
    public LayerMask groundLayer;      // Select the layer that counts as "Ground"
    //private bool isGrounded;

    [SerializeField] private float footstepSoundDelay = 1f;
    private float footstepSoundDuration = 0f;
    [SerializeField] private AudioClip footstepAudioClip;

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

        if (movement.x < 0) spriteRenderer.flipX = true;
        else if (movement.x > 0) spriteRenderer.flipX = false;

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
    }

    public void Jump()
    {
        Debug.Log("Jump action triggered");
        rb.AddForce(new Vector2(0f, jump), ForceMode2D.Impulse);
    }
}
