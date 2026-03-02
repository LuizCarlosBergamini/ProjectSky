using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem.Controls;

public class CameraManager : MonoBehaviour
{
    public static CameraManager instance;

    [SerializeField] private CinemachineCamera[] allVirtualCameras;
    private CinemachineCamera currentCamera;

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

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }

        for (int i = 0; i < allVirtualCameras.Length; i++)
        {
            if (allVirtualCameras[i].enabled)
            {
                currentCamera = allVirtualCameras[i];
                positionComposer = currentCamera.GetComponent<CinemachinePositionComposer>();
            }
        }

        normYPanAmount = positionComposer.Damping.y;

        // set the starting posiution of the tracked object offset to be used as a reference for panning the camera up and down
        startingTrackedObjectOffset = positionComposer.TargetOffset;
    }

    #region Lerp the Y Damping

    public void LerpYDamping(bool isPlayerFalling)
    {
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
            cameraFromRight.enabled = true;

            cameraFromLeft.enabled = false;

            currentCamera = cameraFromRight;

            positionComposer = currentCamera.GetComponent<CinemachinePositionComposer>();
        }

        else if (currentCamera == cameraFromRight && triggerExitDirection.x > 0f)
        {
            cameraFromLeft.enabled = true;

            cameraFromRight.enabled = false;

            currentCamera = cameraFromLeft;

            positionComposer = currentCamera.GetComponent<CinemachinePositionComposer>();
        }
    }

    #endregion
}
