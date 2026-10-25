using System.Collections;
using System.Collections.Generic;
using HierarchicalStateMachine;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem.Controls;

public class CameraManager : MonoBehaviour
{
    public static CameraManager instance;

    [SerializeField] private CinemachineCamera[] allVirtualCameras;
    private CinemachineCamera currentCamera;

    [Tooltip("Camera que segue a Lucy. Vazio usa o primeiro slot de allVirtualCameras.")]
    [SerializeField] private CinemachineCamera playerCamera;

    // Cameras switched off for the rest of this scene load (the arena camera once its boss is dead).
    // Lives on this instance, so leaving to the hub and coming back reloads the level with a clean set.
    private readonly HashSet<CinemachineCamera> retiredCameras = new HashSet<CinemachineCamera>();

    [Header("Controls for lerping the Y Damping during player jump/fall")]
    [SerializeField] private float fallPanAmount = 0.25f;
    [SerializeField] private float fallYPanTime = 0.35f;
    public float fallSpeedYDampingChangeThreshold = -15f;

    public bool IsLerpingYDamping {  get; private set; }
    public bool LerpedFromPlayerFalling { get; set; }

    private CinemachinePositionComposer positionComposer;

    private Coroutine lerpYPanCoroutine;
    private Coroutine panCameraCoroutine;

    private float normYPanAmount;

    private Vector2 startingTrackedObjectOffset;

    /// <summary>
    /// Takes the live virtual camera as the current one and caches its composer defaults.
    /// Empty slots are skipped, so one missing camera cannot leave the damping and panning uninitialized.
    /// </summary>
    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }

        // Scenes can ship with more than one camera enabled (the Hub has both, the player camera wins on
        // priority). Taking the last enabled one as current made SwapCamera think the other camera was live,
        // so the first pass through a swap trigger did nothing. Pick the one Cinemachine shows (highest
        // priority, first in the array on a tie) and disable the rest so the swap logic and the screen agree.
        for (int i = 0; i < allVirtualCameras.Length; i++)
        {
            CinemachineCamera cam = allVirtualCameras[i];
            if (cam == null)
            {
                Debug.LogWarning($"{name}: allVirtualCameras[{i}] esta vazio, slot ignorado.", this);
                continue;
            }

            if (cam.enabled && (currentCamera == null || cam.Priority.Value > currentCamera.Priority.Value))
            {
                currentCamera = cam;
            }
        }

        for (int i = 0; i < allVirtualCameras.Length; i++)
        {
            if (allVirtualCameras[i] != null && allVirtualCameras[i] != currentCamera)
            {
                allVirtualCameras[i].enabled = false;
            }
        }

        if (playerCamera == null && allVirtualCameras.Length > 0)
        {
            playerCamera = allVirtualCameras[0];
        }

        if (currentCamera != null)
        {
            positionComposer = currentCamera.GetComponent<CinemachinePositionComposer>();
        }

        if (positionComposer == null)
        {
            Debug.LogError($"{name}: nenhuma camera ativa com CinemachinePositionComposer; damping e pan da camera desativados.", this);
            return;
        }

        normYPanAmount = positionComposer.Damping.y;

        // set the starting posiution of the tracked object offset to be used as a reference for panning the camera up and down
        startingTrackedObjectOffset = positionComposer.TargetOffset;
    }

    private void OnEnable()
    {
        BossGate.OnBossFightWon += HandleBossFightWon;
    }

    private void OnDisable()
    {
        BossGate.OnBossFightWon -= HandleBossFightWon;
    }

    #region Lerp the Y Damping

    public void LerpYDamping(bool isPlayerFalling)
    {
        if (positionComposer == null) return;

        if (lerpYPanCoroutine != null)
        {
            StopCoroutine(lerpYPanCoroutine);
        }
        lerpYPanCoroutine = StartCoroutine(LerpYAction(isPlayerFalling));
    }

    public IEnumerator LerpYAction(bool isPlayerFalling)
    {
        IsLerpingYDamping = true;

        float startDampAmount = positionComposer.Damping.y;
        float endDampAmount = 0f;

        if (isPlayerFalling)
        {
            endDampAmount = fallPanAmount;
            LerpedFromPlayerFalling = true;
        }
        else
        {
            endDampAmount = normYPanAmount;
        }

        float elapsedTime = 0f;
        while(elapsedTime < fallYPanTime)
        {
            elapsedTime += Time.deltaTime;
            float larpedPanAmount = Mathf.Lerp(startDampAmount, endDampAmount, elapsedTime / fallYPanTime);
            positionComposer.Damping.y = larpedPanAmount;
            yield return null;

        }

        IsLerpingYDamping = false;
    }

    #endregion

    #region Pan Camera

    public void PanCameraOnContact(float panDistance, float panTime, PanDirection panDirection, bool panToStartingPos)
    {
        if (positionComposer == null) return;

        // Cancela qualquer tween ativo no TargetOffset
        LeanTween.cancel(gameObject);

        Vector2 endPos = CalculateEndPosition(panDistance, panDirection, panToStartingPos);

        // Usa LeanTween para animar o TargetOffset
        LeanTween.value(gameObject, (Vector2)positionComposer.TargetOffset, endPos, panTime)
            .setOnUpdate((Vector2 val) => {
                positionComposer.TargetOffset = val;
            })
            .setEase(LeanTweenType.easeInOutSine);
    }

    private Vector2 CalculateEndPosition(float panDistance, PanDirection panDirection, bool panToStartingPos)
    {
        if (panToStartingPos)
        {
            return startingTrackedObjectOffset;
        }

        Vector2 direction = panDirection switch
        {
            PanDirection.Up => Vector2.up,
            PanDirection.Down => Vector2.down,
            PanDirection.Left => Vector2.left,
            PanDirection.Right => Vector2.right,
            _ => Vector2.zero
        };

        return startingTrackedObjectOffset + (direction * panDistance);
    }


    //public void PanCameraOnContact(float panDistance, float panTime, PanDirection panDirection, bool panToStartingPos)
    //{
    //    if (panCameraCoroutine != null)
    //    {
    //        StopCoroutine(panCameraCoroutine);
    //    }
    //    panCameraCoroutine = StartCoroutine(PanCamera(panDistance, panTime, panDirection, panToStartingPos));
    //}

    //private IEnumerator PanCamera(float panDistance, float panTime, PanDirection panDirection, bool panToStartingPos)
    //{
    //    Vector2 endPos = Vector2.zero;
    //    Vector2 startingPos = Vector2.zero;

    //    // handle the pan from the trigger
    //    if (!panToStartingPos)
    //    {
    //        // set the diraction and distance
    //        switch (panDirection)
    //        {
    //            case PanDirection.Up:
    //                endPos = Vector2.up;
    //                break;
    //            case PanDirection.Down:
    //                endPos = Vector2.down;
    //                break;
    //            case PanDirection.Left:
    //                endPos = Vector2.left;
    //                break;
    //            case PanDirection.Right:
    //                endPos = Vector2.right;
    //                break;
    //            default:
    //                break;
    //        }

    //        endPos *= panDistance;

    //        startingPos = startingTrackedObjectOffset;

    //        endPos += startingPos;
    //    }
    //    // handle the pan back to starting
    //    else
    //    {
    //        startingPos = positionComposer.TargetOffset;
    //        endPos = startingTrackedObjectOffset;
    //    }

    //    // handle the actual panning of the camera
    //    float elapsedTime = 0f;
    //    while (elapsedTime < panTime)
    //    {
    //        elapsedTime += Time.deltaTime;
    //        Vector3 panLerp = Vector3.Lerp(startingPos, endPos, (elapsedTime / panTime));
    //        positionComposer.TargetOffset = panLerp;

    //        yield return null;
    //    }
    //}

    #endregion

    #region Swap Cameras

    public void SwapCamera(CinemachineCamera cameraFromLeft, CinemachineCamera cameraFromRight, Vector2 triggerExitDirection)
    {
        if (currentCamera == cameraFromLeft && triggerExitDirection.x < 0f)
        {
            ActivateCamera(cameraFromRight);
        }

        else if (currentCamera == cameraFromRight && triggerExitDirection.x > 0f)
        {
            ActivateCamera(cameraFromLeft);
        }
    }

    /// <summary>
    /// Turns a camera off until the scene reloads: if it is live the view falls back to the player camera,
    /// and swap triggers can no longer bring it back.
    /// </summary>
    public void RetireCamera(CinemachineCamera cam)
    {
        if (cam == null || cam == playerCamera) return;

        retiredCameras.Add(cam);
        cam.enabled = false;

        if (currentCamera == cam)
        {
            ActivateCamera(playerCamera);
        }
    }

    // A dead boss leaves the arena with only Lucy's follow camera: every other camera in the level is retired.
    private void HandleBossFightWon(EnemyStateDriver boss)
    {
        for (int i = 0; i < allVirtualCameras.Length; i++)
        {
            RetireCamera(allVirtualCameras[i]);
        }
    }

    private void ActivateCamera(CinemachineCamera next)
    {
        if (next == null || next == currentCamera || retiredCameras.Contains(next)) return;

        next.enabled = true;

        if (currentCamera != null)
        {
            currentCamera.enabled = false;
        }

        currentCamera = next;

        positionComposer = currentCamera.GetComponent<CinemachinePositionComposer>();
    }

    #endregion
}
