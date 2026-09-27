using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Visuals of one health bar, shared by the player and boss bars. Displays a value, nothing else:
/// the owner pushes (current, max) in, this never reads health on its own.
/// The bar keeps a fixed on-screen width; a higher max health only changes the fill ratio.
/// Which side it empties towards comes from the fill Image's Fill Origin (Left or Right).
/// </summary>
public class HealthBarView : MonoBehaviour
{
    [Tooltip("Preenchimento principal (Image Type = Filled, Horizontal).")]
    [SerializeField] private Image fill;

    [Header("Rastro de dano")]
    [Tooltip("Mostra uma segunda barra que acompanha o preenchimento com atraso, para os golpes ficarem legiveis.")]
    [SerializeField] private bool useDamageTrail = true;
    [SerializeField] private Image trail;
    [Tooltip("Segundos parado antes do rastro comecar a descer.")]
    [SerializeField] private float trailDelay = 0.35f;
    [Tooltip("Velocidade do rastro, em barras cheias por segundo.")]
    [SerializeField] private float trailSpeed = 1.2f;

    [Header("Numeros")]
    [Tooltip("Mostra 'atual/maximo' sobre a barra.")]
    [SerializeField] private bool showNumbers;
    [SerializeField] private TextMeshProUGUI numbersText;

    private Coroutine trailRoutine;
    private float trailTarget;

    /// <summary>True while the damage trail is still catching up with the fill.</summary>
    public bool IsAnimating => trailRoutine != null;

    private void Awake()
    {
        ApplyToggles();
    }

    private void OnDisable()
    {
        // A coroutine dies with the object; settle the trail so it is not frozen half-way.
        if (trailRoutine == null) return;
        trailRoutine = null;
        if (trail != null) trail.fillAmount = trailTarget;
    }

    /// <param name="instant">Skip the trail animation, e.g. when binding to a new target.</param>
    public void SetValues(float current, float max, bool instant)
    {
        float ratio = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        if (fill != null) fill.fillAmount = ratio;

        if (showNumbers && numbersText != null)
        {
            numbersText.text = $"{Mathf.CeilToInt(Mathf.Max(0f, current))}/{Mathf.CeilToInt(max)}";
        }

        if (!useDamageTrail || trail == null) return;

        trailTarget = ratio;

        // Healing (or a fresh bind) moves the trail with the fill; only losses leave a trail behind.
        if (instant || !isActiveAndEnabled || ratio >= trail.fillAmount)
        {
            if (trailRoutine != null) StopCoroutine(trailRoutine);
            trailRoutine = null;
            trail.fillAmount = ratio;
            return;
        }

        trailRoutine ??= StartCoroutine(DrainTrail());
    }

    private IEnumerator DrainTrail()
    {
        yield return new WaitForSecondsRealtime(trailDelay);

        while (trail.fillAmount > trailTarget)
        {
            trail.fillAmount = Mathf.MoveTowards(trail.fillAmount, trailTarget, trailSpeed * Time.unscaledDeltaTime);
            yield return null;
        }

        trailRoutine = null;
    }

    private void ApplyToggles()
    {
        if (trail != null) trail.gameObject.SetActive(useDamageTrail);
        if (numbersText != null) numbersText.gameObject.SetActive(showNumbers);
    }
}
