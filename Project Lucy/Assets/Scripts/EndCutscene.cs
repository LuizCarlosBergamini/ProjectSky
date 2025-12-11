using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif
using UnityEngine.Events;
using UnityEngine.Playables;

public class CutsceneSkipper : MonoBehaviour
{
    [Header("Director")]
    [Tooltip("PlayableDirector that controls the cutscene. If left null, skipping won't do anything.")]
    public PlayableDirector playableDirector;

    [Tooltip("If true the script will use the PlayableDirector.duration to compute the target time.")]
    public bool useAutoDuration = true;

    [Tooltip("When using auto duration, subtract this offset (in seconds) from the duration so activation tracks at the very end still run.")]
    public double endOffset = 0.1;

    [Tooltip("Fallback time to seek to (in seconds) when not using auto duration or when duration is invalid).")]
    public double timeToSkipTo = 0.0;

    [Header("Input")]
    [Tooltip("KeyCodes that will trigger a skip when pressed.")]
    public KeyCode[] skipKeys = new KeyCode[] { KeyCode.Space };

    [Tooltip("Optional Input Manager button names that will trigger a skip (e.g. 'Submit').")]
    public string[] skipButtons = new string[0];

    [Header("Callbacks")]
    [Tooltip("Invoked when a cutscene is skipped. Useful to hook game-state changes or UI updates.")]
    public UnityEvent onSkip;

    // Prevent multiple skips while processing
    bool hasSkipped = false;

    void Update()
    {
        if (hasSkipped) return;
        if (!IsSkippable()) return;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        // New Input System: support keyboard keys by converting KeyCode -> Key where possible.
        if (skipKeys != null && Keyboard.current != null)
        {
            foreach (var k in skipKeys)
            {
                if (k == KeyCode.None) continue;
                if (Enum.TryParse<UnityEngine.InputSystem.Key>(k.ToString(), out var parsedKey))
                {
                    var control = Keyboard.current[parsedKey];
                    if (control != null && control.wasPressedThisFrame)
                    {
                        SkipCutscene();
                        return;
                    }
                }
            }
        }

        // The new Input System does not provide Input.GetButtonDown by name. If you rely on
        // named Input Manager buttons consider using Input Actions or switch Player Settings to "Both".
        // As a lightweight fallback, check common gamepad buttons if available.
        if (skipButtons != null && skipButtons.Length > 0)
        {
            var gp = Gamepad.current;
            if (gp != null && gp.buttonSouth.wasPressedThisFrame)
            {
                SkipCutscene();
                return;
            }
        }
#else
        // Legacy Input system
        if (skipKeys != null)
        {
            foreach (var k in skipKeys)
            {
                if (Input.GetKeyDown(k))
                {
                    SkipCutscene();
                    return;
                }
            }
        }

        if (skipButtons != null)
        {
            foreach (var b in skipButtons)
            {
                if (string.IsNullOrEmpty(b)) continue;
                if (Input.GetButtonDown(b))
                {
                    SkipCutscene();
                    return;
                }
            }
        }
#endif
    }

    bool IsSkippable()
    {
        if (playableDirector == null) return false;
        // Only allow skipping while playing (prevents rewinding or skipping after it's already finished)
        return playableDirector.state == PlayState.Playing;
    }

    public void SkipCutscene()
    {
        if (playableDirector == null) return;
        if (hasSkipped) return;

        double targetTime = timeToSkipTo;

        if (useAutoDuration)
        {
            double dur = playableDirector.duration;
            if (!double.IsInfinity(dur) && !double.IsNaN(dur) && dur > 0.0)
            {
                targetTime = Math.Max(0.0, dur - endOffset);
            }
        }

        // Clamp target time to valid range
        if (!double.IsInfinity(playableDirector.duration) && !double.IsNaN(playableDirector.duration))
        {
            targetTime = Math.Min(targetTime, playableDirector.duration);
        }

        // Seek, evaluate and stop so activation tracks at the very end run correctly
        playableDirector.time = targetTime;
        playableDirector.Evaluate();
        playableDirector.Stop();

        hasSkipped = true;
        onSkip?.Invoke();
    }
}
