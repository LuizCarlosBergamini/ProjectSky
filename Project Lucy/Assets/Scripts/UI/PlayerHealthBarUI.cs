using System.Collections;
using HierarchicalStateMachine;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lucy's health bar, top-left of the HUD (Hub and levels). Binds to the scene's player and redraws only
/// when the player raises HealthChanged (damage, healing, max-health upgrades). The bar gets longer with
/// max health: every point of health takes the same width.
/// </summary>
public class PlayerHealthBarUI : MonoBehaviour
{
    [SerializeField] private HealthBarView bar;
    [SerializeField] private Image portrait;
    [SerializeField] private TextMeshProUGUI nameText;

    [Tooltip("Retrato e nome vem deste Entity (o mesmo asset da Lucy usado nos dialogos).")]
    [SerializeField] private Entity_SO portraitSource;

    [Tooltip("Jogador desta cena. Se vazio, e procurado uma vez ao iniciar.")]
    [SerializeField] private PlayerStateDriver player;

    [Header("Tamanho")]
    [Tooltip("Retangulo que cresce com a vida maxima (o bloco retrato + barra).")]
    [SerializeField] private RectTransform resizeTarget;
    [Tooltip("Largura da barra com a vida maxima base, sem upgrades.")]
    [SerializeField] private float baseBarWidth = 268f;
    [Tooltip("Largura ocupada pelo retrato e o espaco antes da barra.")]
    [SerializeField] private float leadingWidth = 72f;
    [Tooltip("Segundos para a barra crescer quando a vida maxima muda.")]
    [SerializeField] private float growDuration = 0.35f;

    private bool started;
    private bool bound;
    private float shownMax;
    private Coroutine growRoutine;

    private void Awake()
    {
        if (portraitSource == null) return;
        if (portrait != null && portraitSource.entitySprite != null) portrait.sprite = portraitSource.entitySprite;
        if (nameText != null) nameText.text = portraitSource.entityName;
    }

    private void OnEnable()
    {
        // Before Start the player may not have run Awake yet; Start does the first bind.
        if (started) Bind();
    }

    private void Start()
    {
        started = true;
        if (player == null) player = FindFirstObjectByType<PlayerStateDriver>();
        if (player == null) Debug.LogWarning($"{name}: nenhum PlayerStateDriver na cena.", this);
        Bind();
    }

    private void OnDisable()
    {
        Unbind();

        // A coroutine dies with the object; land on the final size instead of freezing half-way.
        if (growRoutine == null) return;
        growRoutine = null;
        SetWidth(TargetWidth(shownMax));
    }

    private void Bind()
    {
        if (bound || player == null || bar == null) return;
        bound = true;
        player.HealthChanged += HandleHealthChanged;
        bar.SetValues(player.CurrentHealth, player.MaxHealth, true);
        Resize(player.MaxHealth, true);
    }

    private void Unbind()
    {
        if (!bound) return;
        bound = false;
        // ReferenceEquals: the player can already be destroyed on scene unload, and unsubscribing
        // from its C# event is still valid (and required) then.
        if (!ReferenceEquals(player, null)) player.HealthChanged -= HandleHealthChanged;
    }

    private void HandleHealthChanged(float current, float max)
    {
        bar.SetValues(current, max, false);
        Resize(max, false);
    }

    private void Resize(float max, bool instant)
    {
        if (resizeTarget == null) return;
        if (!instant && Mathf.Approximately(max, shownMax)) return;
        shownMax = max;

        if (growRoutine != null) StopCoroutine(growRoutine);
        growRoutine = null;

        if (instant || growDuration <= 0f || !isActiveAndEnabled)
        {
            SetWidth(TargetWidth(max));
            return;
        }

        growRoutine = StartCoroutine(Grow(TargetWidth(max)));
    }

    // Proportional: the base max fills baseBarWidth, every extra point of health adds the same width.
    private float TargetWidth(float max)
    {
        float baseMax = player != null && player.BaseMaxHealth > 0f ? player.BaseMaxHealth : max;
        float barWidth = baseMax > 0f ? baseBarWidth * max / baseMax : baseBarWidth;
        return leadingWidth + Mathf.Max(0f, barWidth);
    }

    private IEnumerator Grow(float targetWidth)
    {
        float startWidth = resizeTarget.sizeDelta.x;
        for (float t = 0f; t < growDuration; t += Time.unscaledDeltaTime)
        {
            SetWidth(Mathf.Lerp(startWidth, targetWidth, Mathf.SmoothStep(0f, 1f, t / growDuration)));
            yield return null;
        }

        SetWidth(targetWidth);
        growRoutine = null;
    }

    private void SetWidth(float width)
    {
        if (resizeTarget != null) resizeTarget.sizeDelta = new Vector2(width, resizeTarget.sizeDelta.y);
    }
}
