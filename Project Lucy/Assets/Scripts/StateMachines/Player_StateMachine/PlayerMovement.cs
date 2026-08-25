using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour, IDamageable
{
    public PlayerStateMachine playerStateMachine;
    public PlayerRunState playerRunState;
    public PlayerIdleState playerIdleState;
    public PlayerAirState playerAirState;
    public PlayerAttackState playerAttackState;

    private Rigidbody2D _rb;
    public Rigidbody2D body => _rb;

    [SerializeField] private Animator _animator;
    public Animator animator => _animator;

    public bool IsGrounded => Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
    public void ChangeState(PlayerState state) => playerStateMachine.ChangeState(state);
    public Vector2 MovementInput { get; private set; }

    [Header("PlayerData")] public PlayerData Data;



    [SerializeField] private float moveSpeed = 10f;


    public PlayerInputs.InGameActions playerActions;

    public bool isDead = false;

    public bool isFacingRight { get; private set; }

    // Jump Variables
    public float jump = 2f;
    public bool _isJumpCut;
    public bool _isJumpFalling;
    public bool IsJumping;
    public float LastOnGroundTime { get; set; }
    public float LastPressedJumpTime { get; set; }

    // Ajuste de responsividade do controle (quanto maior, mais r�pido chega � velocidade alvo)
    [SerializeField] private float velocityResponsiveness = 1f;

    public Transform groundCheck; // Assign an empty GameObject positioned at the player's feet
    public float groundCheckRadius = 0.4f; // Size of the detection circle
    public LayerMask groundLayer; // Select the layer that counts as "Ground"

    [Header("Health and Damage Taken")] [SerializeField]
    private float health = 100f;

    public bool HasTakenDamage { get; set; } = false;
    public bool canWalk = true;

    // --- Knockback handling (non-blocking) ---
    private Coroutine knockbackCoroutine;
    private bool isKnockedBack = false;
    [SerializeField] private float knockbackLandingGrace = 0.05f; // short delay to ignore immediate grounded checks

    // --- Handling Camera ---
    [SerializeField] CameraFollowObject CameraFollowObject;
    
    [SerializeField] private float timeBetweenAttacks = 10f;
    private float attackTimeCounter;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        playerActions = new PlayerInputs().InGame;
    }

    

    private void Start()
    {
        playerStateMachine.Initialize(playerIdleState);
    }

    public void TogglePlayerInGameAction(bool toggle)
    {
        if (toggle) playerActions.Enable();
        else playerActions.Disable();
    }


    private void Update()
    {
        if (IsGrounded) LastOnGroundTime = Data.coyoteTime;
        else LastOnGroundTime -= Time.deltaTime;

        LastPressedJumpTime -= Time.deltaTime;

        if (attackTimeCounter > 0) attackTimeCounter -= Time.deltaTime;

        playerStateMachine.LogicUpdateState();

        #region GRAVITY

        //Higher gravity if we've released the jump input or are falling
        //if (IsSliding)
        //{
        //    SetGravityScale(0);
        //}
        if (_rb.linearVelocityY < 0 && MovementInput.y < 0)
        {
            //Much higher gravity if holding down
            SetGravityScale(Data.gravityScale * Data.fastFallGravityMult);
            //Caps maximum fall speed, so when falling over large distances we don't accelerate to insanely high speeds
            _rb.linearVelocity =
                new Vector2(_rb.linearVelocityX, Mathf.Max(_rb.linearVelocityY, -Data.maxFastFallSpeed));
        }
        else if (_isJumpCut)
        {
            //Higher gravity if jump button released
            SetGravityScale(Data.gravityScale * Data.jumpCutGravityMult);
            _rb.linearVelocity = new Vector2(_rb.linearVelocityX, Mathf.Max(_rb.linearVelocityY, -Data.maxFallSpeed));
        }
        else if ((IsJumping || _isJumpFalling) && Mathf.Abs(_rb.linearVelocityY) < Data.jumpHangTimeThreshold)
        {
            SetGravityScale(Data.gravityScale * Data.jumpHangGravityMult);
        }
        else if (_rb.linearVelocityY < 0)
        {
            //Higher gravity if falling
            SetGravityScale(Data.gravityScale * Data.fallGravityMult);
            //Caps maximum fall speed, so when falling over large distances we don't accelerate to insanely high speeds
            _rb.linearVelocity = new Vector2(_rb.linearVelocityX, Mathf.Max(_rb.linearVelocityY, -Data.maxFallSpeed));
        }
        else
        {
            //Default gravity if standing on a platform or moving upwards
            SetGravityScale(Data.gravityScale);
        }

        #endregion

    }

    private void FixedUpdate()
    {
        MovementInput = playerActions.Movement.ReadValue<Vector2>();

        playerStateMachine.PhysicsUpdateState();

        Run(1);
    }

    public void SetGravityScale(float scale)
    {
        _rb.gravityScale = scale;
    }

    #region INPUT CALLBACKS

    public void OnJumpInput()
    {
        LastPressedJumpTime = Data.jumpInputBufferTime;
    }

    private void OnAnyActionTriggered(InputAction.CallbackContext context)
    {
        if (context.started || context.performed) playerStateMachine.HandleInput();
    }

    #endregion

    #region RUN METHODS

    private void Run(float lerpAmount)
    {
        if (!canWalk) return;

        //Calculate the direction we want to move in and our desired velocity
        float targetSpeed = MovementInput.x * Data.runMaxSpeed;
        //We can reduce control using Lerp() this smooths changes to direction and speed
        targetSpeed = Mathf.Lerp(_rb.linearVelocityX, targetSpeed, lerpAmount);

        #region Calculate AccelRate

        float accelRate;

        //Gets an acceleration value based on if we are accelerating (includes turning) 
        //or trying to decelerate (stop). As well as applying a multiplier if we're airborne.
        if (LastOnGroundTime > 0)
            accelRate = (Mathf.Abs(targetSpeed) > 0.01f) ? Data.runAccelAmount : Data.runDeccelAmount;
        else
            accelRate = (Mathf.Abs(targetSpeed) > 0.01f)
                ? Data.runAccelAmount * Data.accelInAir
                : Data.runDeccelAmount * Data.deccelInAir;

        #endregion

        #region Add Bonus Jump Apex Acceleration

        //Increase acceleration and maxSpeed when at the apex of their jump, makes the jump feel a bit more bouncy, responsive and natural
        if ((IsJumping || _isJumpFalling) && Mathf.Abs(_rb.linearVelocityY) < Data.jumpHangTimeThreshold)
        {
            accelRate *= Data.jumpHangAccelerationMult;
            targetSpeed *= Data.jumpHangMaxSpeedMult;
        }

        #endregion

        #region Conserve Momentum

        //We won't slow the player down if they are moving in their desired direction but at a greater speed than their maxSpeed
        if (Data.doConserveMomentum && Mathf.Abs(_rb.linearVelocityX) > Mathf.Abs(targetSpeed) &&
            Mathf.Sign(_rb.linearVelocityX) == Mathf.Sign(targetSpeed) && Mathf.Abs(targetSpeed) > 0.01f &&
            LastOnGroundTime < 0)
        {
            //Prevent any deceleration from happening, or in other words conserve are current momentum
            //You could experiment with allowing for the player to slightly increae their speed whilst in this "state"
            accelRate = 0;
        }

        #endregion

        //Calculate difference between current velocity and desired velocity
        float speedDif = targetSpeed - _rb.linearVelocityX;
        //Calculate force along x-axis to apply to thr player
        float movementLocal = speedDif * accelRate;

        //Convert this to a vector and apply to rigidbody
        _rb.AddForce(movementLocal * Vector2.right, ForceMode2D.Force);
        TurnCheck();
    }

    public void TurnCheck()
    {
        // changes direction of player based on movement
        if (MovementInput.x > 0 && isFacingRight) Turn();
        else if (MovementInput.x < 0 && !isFacingRight) Turn();
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

    #region ATTACK METHODS

    public void ResetAttackCooldown()
    {
        Debug.Log("Starting Attack Cooldown");
        attackTimeCounter = timeBetweenAttacks;
    }

    public bool CanAttack()
    {
        return attackTimeCounter <= 0;
    }

    

    #endregion

    #region PLAYER LIFE METHODS

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
        //animator.SetTrigger("hitted");
        HasTakenDamage = true;
        health -= amount;
        if (health <= 0 && !isDead)
        {
            Die();
            return;
        }

        _rb.AddForce(knockback, ForceMode2D.Impulse);
        // Start a coroutine to wait for landing instead.
        if (knockbackCoroutine != null)
        {
            StopCoroutine(knockbackCoroutine);
            knockbackCoroutine = null;
        }

        knockbackCoroutine = StartCoroutine(HandleKnockbackLanding());
        HasTakenDamage = false;
    }

    // TODO: Transform this function in a separate file for using in all objects that need knockback
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
        if (isDead) return;
        isDead = true;
        canWalk = false;

        // Add death effects here (animations, sounds, etc.)

        // Inside a mission level the run manager restarts the level and takes back
        // everything gathered during this attempt.
        if (LevelRunManager.instance != null)
        {
            LevelRunManager.instance.HandlePlayerDeath();
            return;
        }

        // Anywhere else, fall back to the game over screen.
        if (GameManager.instance != null)
        {
            GameManager.instance.GameOver();
        }
    }

    #endregion
}
