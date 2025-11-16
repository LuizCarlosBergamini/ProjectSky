using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public static class AnimUtils
{
    public static IEnumerator Move(this RectTransform rect, Vector2 from, Vector2 to, float duration, bool unscaledTime = true)
    {
        if (rect == null) yield break;
        float time = 0f;

        while (time < duration)
        {
            time += unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float t = Mathf.Clamp01(time / duration);

            float easeT = Mathf.SmoothStep(0f, 1f, t);

            if (!Application.isPlaying) yield break;
            rect.anchoredPosition = Vector2.LerpUnclamped(from, to, easeT);
            yield return null;
        }
    }

    public static IEnumerator Move(this RectTransform rect, Vector2 to, float duration, bool unscaledTime = true)
    {
        yield return Move(rect, rect.anchoredPosition, to, duration, unscaledTime);
    }

    public static IEnumerator FadeCanvas(this CanvasGroup canvas, float from, float to, float duration, bool unscaledTime = true)
    {
        if (canvas == null) yield break;
        float time = 0f;

        while (time < duration)
        {
            time += unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float t = Mathf.Clamp01(time / duration);

            if (!Application.isPlaying) yield break;
            canvas.alpha = Mathf.SmoothStep(from, to, t);
            yield return null;
        }
    }

    public static IEnumerator Fade(this AudioSource audio, float from, float to, float duration)
    {
        if (audio == null) yield break;
        float time = 0f;

        while (time < duration)
        {
            time += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(time / duration);

            if (!Application.isPlaying) yield break;
            audio.volume = Mathf.SmoothStep(from, to, t);
            yield return null;
        }
    }
}
