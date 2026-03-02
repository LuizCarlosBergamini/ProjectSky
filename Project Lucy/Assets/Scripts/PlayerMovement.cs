using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour, IDamageable {

    [Header("PlayerData")]
    public PlayerData Data;

    private Vector2 movement;
    private Rigidbody2D rb;
    [SerializeField] private float moveSpeed = 10f;

    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private GrappleController GrappleController;

    public PlayerInputs.InGameActions playerActions;

    public bool isDead = false;

    public bool isFacingRight { get; private set; }

    // Jump Variables
    public float jump = 2f;
    private bool _isJumpCut;
    private bool _isJumpFalling;
    public bool IsJumping { get; private set; }
    public float LastOnGroundTime { get; private set; }
    public float LastPressedJumpTime { get; private set; }

    // Ajuste de responsividade do controle (quanto maior, mais rápido chega à velocidade alvo)
    [SerializeField] private float velocityResponsiveness = 1f;

    public Transform groundCheck;      // Assign an empty GameObject positioned at the player's feet
    public float groundCheckRadius = 0.4f; // Size of the detection circle
    public LayerMask groundLayer;      // Select the layer that counts as "Ground"

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
    [SerializeField] private float knockbackForce = 20f;
    public bool shouldBeDamaging { get; private set; } = false;
    private List<IDamageable> damageables = new List<IDamageable>();
    [SerializeField] private float verticalKnockback = 0.5f;

    [Header("Health and Damage Taken")]
    [SerializeField] private float health = 100f;
    public bool HasTakenDamage { get; set; } = false;
    public bool canWalk = true;

    // --- Knockback handling (non-blocking) ---
    private Coroutine knockbackCoroutine;
    private bool isKnockedBack = false;
    [SerializeField] private float knockbackLandingGrace = 0.05f; // short delay to ignore immediate grounded checks

    // --- Handling Camera ---
    [SerializeField] CameraFollowObject CameraFollowObject;
    private float fallSpeedYDampingChangeThreshold;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        GrappleController = GetComponent<GrappleController>();

        playerActions = new PlayerInputs().InGame;
    }

    private void Start()
    {
        SetGravityScale(Data.gravityScale);
        fallSpeedYDampingChangeThreshold = CameraManager.instance.fallSpeedYDampingChangeThreshold;
    }

    private void OnDisable()
    {
        playerActions.Disable();
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

    private void FixedUpdate()
    {
        if (GrappleController.isGrappling) return;
        Run(1);

        if (movement.x > 0 || movement.x < 0) TurnCheck();

        // Clamp vertical velocity to zero when grounded to avoid floating-point precision errors
        if (IsGrounded && Mathf.Abs(rb.linearVelocityY) < 0.01f)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocityX, 0f);
        }
    }

    private void Update()
    {
        #region TIMERS

        LastOnGroundTime -= Time.deltaTime;
        LastPressedJumpTime -= Time.deltaTime;

        #endregion

        #region INPUT CHECKS

        if (playerActions.Jump.WasPressedThisFrame())
        {
            OnJumpInput();
        }

        if (playerActions.Jump.WasReleasedThisFrame())
        {
            OnJumpUpInput();
        }

        #endregion



        #region JUMP CHECKS

        if (IsGrounded)
        {
            if (CanJump() && LastPressedJumpTime > 0)
            {
                IsJumping = true;
                _isJumpCut = false;
                _isJumpFalling = false;
                Jump();
            }
        }

        if (IsJumping && rb.linearVelocityY < 0)
        {
            IsJumping = false;

            _isJumpFalling = true;
        }

        if (LastOnGroundTime > 0 && !IsJumping)
        {
            _isJumpCut = false;

            _isJumpFalling = false;
        }

        #endregion

        #region GRAVITY
        //Higher gravity if we've released the jump input or are falling
        //if (IsSliding)
        //{
        //    SetGravityScale(0);
        //}
        if (rb.linearVelocityY < 0 && movement.y < 0)
        {
            //Much higher gravity if holding down
            SetGravityScale(Data.gravityScale * Data.fastFallGravityMult);
            //Caps maximum fall speed, so when falling over large distances we don't accelerate to insanely high speeds
            rb.linearVelocity = new Vector2(rb.linearVelocityX, Mathf.Max(rb.linearVelocityY, -Data.maxFastFallSpeed));
        }
        else if (_isJumpCut)
        {
            //Higher gravity if jump button released
            SetGravityScale(Data.gravityScale * Data.jumpCutGravityMult);
            rb.linearVelocity = new Vector2(rb.linearVelocityX, Mathf.Max(rb.linearVelocityY, -Data.maxFallSpeed));
        }
        else if ((IsJumping || _isJumpFalling) && Mathf.Abs(rb.linearVelocityY) < Data.jumpHangTimeThreshold)
        {
            SetGravityScale(Data.gravityScale * Data.jumpHangGravityMult);
        }
        else if (rb.linearVelocityY < 0)
        {
            //Higher gravity if falling
            SetGravityScale(Data.gravityScale * Data.fallGravityMult);
            //Caps maximum fall speed, so when falling over large distances we don't accelerate to insanely high speeds
            rb.linearVelocity = new Vector2(rb.linearVelocityX, Mathf.Max(rb.linearVelocityY, -Data.maxFallSpeed));
        }
        else
        {
            //Default gravity if standing on a platform or moving upwards
            SetGravityScale(Data.gravityScale);
        }
        #endregion

        #region COLLISION CHECKS

        if (Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer))
        {
            LastOnGroundTime = Data.coyoteTime;
        }

        #endregion

        if (rb.linearVelocityY < fallSpeedYDampingChangeThreshold && !CameraManager.instance.IsLerpingYDamping && !CameraManager.instance.LerpedFromPlayerFalling)
        {
            CameraManager.instance.LerpYDamping(true);
        }
        
        if (rb.linearVelocityY >= 0f && !CameraManager.instance.IsLerpingYDamping && CameraManager.instance.LerpedFromPlayerFalling)
        {
            CameraManager.instance.LerpedFromPlayerFalling = false;
            CameraManager.instance.LerpYDamping(false);
        }

        if (!GrappleController.isGrappling)
        {
            if (playerActions.Attack.WasPressedThisFrame() && attackTimeCounter >= timeBetweenAttacks)
            {
                // Reset attack counter
                attackTimeCounter = 0f;
                animator.SetTrigger("attack");
                animator.SetBool("IsAttacking", true);
            }

            // Animate jump
            animator.SetFloat("yVelocity", rb.linearVelocityY);
            animator.SetBool("isGrounded", IsGrounded);
        }
        attackTimeCounter += Time.deltaTime;


    }

    #region GENERAL METHODS
    public void SetGravityScale(float scale)
    {
        rb.gravityScale = scale;
    }
    #endregion

    #region CHECK METHODS

    private bool CanJump()
    {
        return LastOnGroundTime > 0 && !IsJumping;
    }

    #endregion

    #region JUMP METHODS

    public void Jump()
    {
        //Ensures we can't call Jump multiple times from one press
        LastPressedJumpTime = 0;
        LastOnGroundTime = 0;

        float force = Data.jumpForce;
        if (rb.linearVelocityY < 0)
            force -= rb.linearVelocityY;

        rb.AddForce(Vector2.up * force, ForceMode2D.Impulse);
    }

    private bool CanJumpCut()
    {
        return IsJumping && rb.linearVelocityY > 0;
    }

    #endregion

    #region INPUT CALLBACKS

    public void OnJumpInput()
    {
        LastPressedJumpTime = Data.jumpInputBufferTime;
    }

    public void OnJumpUpInput()
    {
        if (CanJumpCut())
            _isJumpCut = true;
    }

    #endregion

    #region RUN METHODS

    private void Run(float lerpAmount)
    {
        if (GrappleController.isGrappling || !canWalk) return;

        movement = playerActions.Movement.ReadValue<Vector2>();

        if (IsGrounded && movement.x != 0 && AudioManager.instance != null && footstepAudioClip != null)
        {
            footstepSoundDuration += Time.fixedDeltaTime;
            if (footstepSoundDuration > footstepSoundDelay)
            {
                footstepSoundDuration = 0;
                AudioManager.instance.PlayWithVariation(footstepAudioClip);
            }
        }

        animator.SetBool("IsWalking", movement.x != 0);

        //Calculate the direction we want to move in and our desired velocity
        float targetSpeed = movement.x * Data.runMaxSpeed;
        //We can reduce are control using Lerp() this smooths changes to are direction and speed
        targetSpeed = Mathf.Lerp(rb.linearVelocityX, targetSpeed, lerpAmount);

        #region Calculate AccelRate
        float accelRate;

        //Gets an acceleration value based on if we are accelerating (includes turning) 
        //or trying to decelerate (stop). As well as applying a multiplier if we're air borne.
        if (LastOnGroundTime > 0)
            accelRate = (Mathf.Abs(targetSpeed) > 0.01f) ? Data.runAccelAmount : Data.runDeccelAmount;
        else
            accelRate = (Mathf.Abs(targetSpeed) > 0.01f) ? Data.runAccelAmount * Data.accelInAir : Data.runDeccelAmount * Data.deccelInAir;
        #endregion

        #region Add Bonus Jump Apex Acceleration
        //Increase acceleration and maxSpeed when at the apex of their jump, makes the jump feel a bit more bouncy, responsive and natural
        if ((IsJumping || _isJumpFalling) && Mathf.Abs(rb.linearVelocityY) < Data.jumpHangTimeThreshold)
        {
            accelRate *= Data.jumpHangAccelerationMult;
            targetSpeed *= Data.jumpHangMaxSpeedMult;
        }
        #endregion

        #region Conserve Momentum
        //We won't slow the player down if they are moving in their desired direction but at a greater speed than their maxSpeed
        if (Data.doConserveMomentum && Mathf.Abs(rb.linearVelocityX) > Mathf.Abs(targetSpeed) && Mathf.Sign(rb.linearVelocityX) == Mathf.Sign(targetSpeed) && Mathf.Abs(targetSpeed) > 0.01f && LastOnGroundTime < 0)
        {
            //Prevent any deceleration from happening, or in other words conserve are current momentum
            //You could experiment with allowing for the player to slightly increae their speed whilst in this "state"
            accelRate = 0;
        }
        #endregion

        //Calculate difference between current velocity and desired velocity
        float speedDif = targetSpeed - rb.linearVelocityX;
        //Calculate force along x-axis to apply to thr player

        float movementLocal = speedDif * accelRate;

        //Convert this to a vector and apply to rigidbody
        rb.AddForce(movementLocal * Vector2.right, ForceMode2D.Force);

        /*
		 * For those interested here is what AddForce() will do
		 * RB.velocity = new Vector2(RB.velocity.x + (Time.fixedDeltaTime  * speedDif * accelRate) / RB.mass, RB.velocity.y);
		 * Time.fixedDeltaTime is by default in Unity 0.02 seconds equal to 50 FixedUpdate() calls per second
		*/
    }

    private void TurnCheck()
    {
        // changes direction of player based on movement
        if (movement.x > 0 && isFacingRight) Turn();
        else if (movement.x < 0 && !isFacingRight) Turn();
    }

    private void Turn()
    {
        if (isFacingRight)
        {
            isFacingRight = !isFacingRight;
            transform.rotation = Quaternion.Euler(transform.rotation.x, 0f, transform.rotation.y);
            // Turn the camera object to smooth the movement
            CameraFollowObject.CallTurn();
        }
        else
        {
            isFacingRight = !isFacingRight;
            transform.rotation = Quaternion.Euler(transform.rotation.x, 180f, transform.rotation.y);
            // Turn the camera object to smooth the movement
            CameraFollowObject.CallTurn();
        }
    }

    #endregion


    private void ReturnAttackablesToDamageable()
    {
        foreach (IDamageable thingThatWasHit in damageables)
        {
            thingThatWasHit.HasTakenDamage = false;
        }

        damageables.Clear();
    }

    public IEnumerator DamageWhileSlashIsActive()
    {
        shouldBeDamaging = true;

        while (shouldBeDamaging)
        {
            hits = Physics2D.CircleCastAll(attackPoint.position, attackRange, transform.right, 0f, attackableLayer);
            foreach (RaycastHit2D hit in hits)
            {
                IDamageable damageable = hit.collider.gameObject.GetComponent<IDamageable>();
                if (damageable != null && !damageable.HasTakenDamage)
                {
                    Vector2 knockbackDirection = (hit.collider.transform.position - transform.position).normalized;
                    // combine horizontal direction with a fixed upward component
                    // keep horizontal sign from knockbackDirection.x and force an upward Y value
                    float upward = Mathf.Abs(verticalKnockback); // ensure upward is positive
                    Vector2 combinedDir = new Vector2(knockbackDirection.x, upward).normalized;

                    Vector2 knockback = combinedDir * knockbackForce;
                    damageable.TakeDamage(attackDamage, knockback);
                    damageables.Add(damageable);
                }
            }

            yield return null;
        }

        ReturnAttackablesToDamageable();
    }

    #region Animation Triggers

    public void ShouldBeDamagingToTrue()
    {
        shouldBeDamaging = true;
    }

    public void ShouldBeDamagingToFalse()
    {
        shouldBeDamaging = false;
        
    }

    public void IsAttackingToFalse()
    {
        animator.SetBool("IsAttacking", false);
    }

    #endregion

    private void OnDrawGizmosSelected()
    {
        if (attackPoint == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(attackPoint.position, attackRange);
    }

    public bool IsAlive()
    {
        return health > 0;
    }

    public void Heal(int amount)
    {
        health += amount;
    }

    public void TakeDamage(float amount, Vector2 knockback)
    {
        animator.SetTrigger("hitted");
        HasTakenDamage = true;
        health -= amount;
        if (health < 0 && !isDead)
        {
            Die();
            return;
        }
        rb.AddForce(knockback, ForceMode2D.Impulse);
        // Start a coroutine to wait for landing instead.
        if (knockbackCoroutine != null)
        {
            StopCoroutine(knockbackCoroutine);
            knockbackCoroutine = null;
        }
        knockbackCoroutine = StartCoroutine(HandleKnockbackLanding());
        HasTakenDamage = false;
    }

    private IEnumerator HandleKnockbackLanding()
    {
        isKnockedBack = true;
        canWalk = false;

        // small grace time to avoid immediately considering already-grounded state
        if (knockbackLandingGrace > 0f)
            yield return new WaitForSeconds(knockbackLandingGrace);

        // Wait until the player becomes grounded again
        while (!IsGrounded)
        {
            yield return null;
        }

        // Optionally wait one FixedUpdate to ensure physics settled
        yield return new WaitForFixedUpdate();

        canWalk = true;
        HasTakenDamage = false;
        isKnockedBack = false;
        knockbackCoroutine = null;
    }

    public void Die()
    {
        // Add death effects here (animations, sounds, etc.)
        Destroy(gameObject);
        GameManager.instance.GameOver();
    }
}