using System.Xml;
using UnityEngine;
using UnityEngine.InputSystem;

public class GrappleController : MonoBehaviour
{
    public float springConstant;
    public float dampingCoefficient;
    public float ropeRestLength = 1;
    public float minRopeLength;
    public float maxGrappleDistance;

    public LayerMask grappleableLayer;
    public Vector2 grapplePoint;
    public GameObject grappledObject;

    public Rigidbody2D playerRigidbody;
    public Camera mainCamera;
    public LineRenderer ropeRenderer;


    // State Instances
    public ReadyState readyState;
    public AttachedState attachedState;
    public RetractingState retractingState;

    private GrappleState currentState;
    public PlayerInputs.InGameActions grappleActions;

    void Awake()
    {
        // --- NEW: Instantiate and set up the input actions ---
        grappleActions = new PlayerInputs().InGame;

        // --- Assign Component References ---
        playerRigidbody = GetComponent<Rigidbody2D>();
        mainCamera = Camera.main;
        ropeRenderer = GetComponent<LineRenderer>();

        // (Any other Awake() logic...)
    }

    private void OnEnable()
    {
        grappleActions.Enable();
    }

    private void OnDisable()
    {
        grappleActions.Disable();
    }

    void Start()
    {
        Debug.Log("GrappleController Start");
        // Initialize all states, passing a reference to this controller
        readyState = new ReadyState(this);
        attachedState = new AttachedState(this);
        retractingState = new RetractingState(this);

        // Set the initial state
        currentState = readyState;
        currentState.Enter();

        ropeRenderer.positionCount = 2;
        ropeRenderer.enabled = false;
    }

    public void ChangeState(GrappleState newState)
    {
        if (currentState != null)
        {
            currentState.Exit();
        }
        currentState = newState;
        currentState.Enter();
    }

    void Update()
    {
        if (currentState != null)
        {
            currentState.Execute();
        }
        UpdateRopeVisuals();
    }

    void FixedUpdate()
    {
        if (currentState != null)
        {
            currentState.FixedExecute();
        }
    }

    private void UpdateRopeVisuals()
    {
        // Point 0 is always the player's position
        ropeRenderer.SetPosition(0, transform.position);

        Vector3 visualAnchorPoint;

        // Check if we are attached AND we are pulling an object
        // (not being pulled ourselves)
        if (currentState == attachedState && grappledObject != null && !attachedState.IsPullingPlayer())
        {
            // If we are pulling an object, the visual end of the rope
            // is the object itself, not the static 'grapplePoint'.
            visualAnchorPoint = grappledObject.transform.position;
        }
        else
        {
            // Otherwise, the rope is anchored to the static point
            // we originally hit.
            visualAnchorPoint = grapplePoint;
        }

        // Point 1 is the anchor point
        ropeRenderer.SetPosition(1, visualAnchorPoint);
}
}
