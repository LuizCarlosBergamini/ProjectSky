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

    public float jump = 5f;
    // Ajuste de responsividade do controle (quanto maior, mais rápido chega à velocidade alvo)
    [SerializeField] private float velocityResponsiveness = 1f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public void Move(InputAction.CallbackContext context)
    {
        movement = context.ReadValue<Vector2>();

        animator.SetBool("IsWalking", movement.x != 0);

        if (movement.x < 0) spriteRenderer.flipX = true;
        else if (movement.x > 0) spriteRenderer.flipX = false;
    }

    private void FixedUpdate()
    {
        //rb.MovePosition(rb.position + movement * Time.fixedDeltaTime * moveSpeed);

        //if (movement.x != 0) rb.linearVelocity = new Vector2(movement.x * moveSpeed, rb.linearVelocityY);

        // Converte input em velocidade alvo
        float targetVelX = movement.x * moveSpeed;

        // Calcula diferença entre velocidade alvo e velocidade atual
        float velDiff = targetVelX - rb.linearVelocity.x;

        // Força necessária aproximada para corrigir a diferença num passo de física
        float forceX = velDiff * rb.mass * velocityResponsiveness;

        // Aplica força horizontal (ForceMode2D.Force é contínuo e respeita massa)
        rb.AddForce(new Vector2(forceX, 0f), ForceMode2D.Force);
    }

    public void Jump(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jump);
        }
    }
}
