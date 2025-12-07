using System.Collections;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class PauseManager : MonoBehaviour
{

    public InputActionReference pauseAction;
    public UnityEvent onPause;
    public UnityEvent onUnpause;

    public bool isPaused = false;

    private bool _disablePause = false;
    private readonly float _disableDuration = .5f; // Seconds


    void Update()
    {
        HandlePause();
    }

    private void HandlePause()
    {
        if (pauseAction.action.WasPressedThisFrame() && !_disablePause)
        {
            if (isPaused)
            {
                StartCoroutine(UnpauseAsync());
            }
            else
            {
                StartCoroutine(PauseAsync());
            }
        }
    }

    public void Pause()
    {
        StartCoroutine(PauseAsync());
    }

    public IEnumerator PauseAsync()
    {
        isPaused = true;
        _disablePause = true;
        onPause?.Invoke();
        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(_disableDuration);
        _disablePause = false;
    }

    public void Unpause()
    {
        StartCoroutine(UnpauseAsync());
    }

    public IEnumerator UnpauseAsync()
    {
        _disablePause = true;
        onUnpause?.Invoke();

        Time.timeScale = 1f;
        yield return new WaitForSecondsRealtime(_disableDuration);
        _disablePause = false;
        isPaused = false;
    }
}
