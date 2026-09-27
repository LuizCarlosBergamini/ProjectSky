using System;
using HierarchicalStateMachine;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// One-way gate for a boss arena.
/// The barrier starts open so the player can walk in, closes behind them on the way in,
/// and reopens for good once the boss dies. Losing the boss reference (destroyed, or never
/// assigned) always resolves to "open", so the player can never be sealed in by a bug.
/// </summary>
public class BossGate : MonoBehaviour
{
    [Header("Barrier")]
    [Tooltip("Objeto que bloqueia a saida da arena. Fica desativado ate o jogador entrar.")]
    [SerializeField] private GameObject barrier;

    [Header("Boss")]
    [Tooltip("Boss que precisa morrer para o portao reabrir.")]
    [SerializeField] private EnemyStateDriver boss;

    [Header("Timing")]
    [Tooltip("Espera antes de fechar, para o jogador terminar de atravessar a passagem.")]
    [SerializeField] private float closeDelay = 0.25f;

    [Header("Events")]
    [SerializeField] private UnityEvent onFightStarted;
    [SerializeField] private UnityEvent onBossDefeated;

    /// <summary>
    /// Raised the frame the barrier closes behind the player. Code-side hook for UI that lives
    /// outside the scene and cannot be wired into the UnityEvents above.
    /// </summary>
    public static event Action<EnemyStateDriver> OnBossFightStarted;

    /// <summary>
    /// Raised once after <see cref="OnBossFightStarted"/>: when the boss dies, or when the gate is
    /// unloaded mid-fight (player death reload, leaving the level). Never raised without a start.
    /// </summary>
    public static event Action<EnemyStateDriver> OnBossFightEnded;

    /// <summary>
    /// Raised once when the boss actually dies (never on unload), before <see cref="onBossDefeated"/> hands
    /// out the reward. The code-side "this boss was beaten" signal: boss progression records it.
    /// </summary>
    public static event Action<EnemyStateDriver> OnBossFightWon;

    private bool fightStarted;
    private bool bossDefeated;
    private bool fightAnnounced;

    private void Awake()
    {
        SetBarrierClosed(false);
    }

    private void OnEnable()
    {
        if (boss != null) boss.Died += HandleBossDied;
    }

    private void OnDisable()
    {
        if (boss != null) boss.Died -= HandleBossDied;
        AnnounceFightEnded();
    }

    /// <summary>Wire this to the arena entrance trigger. Safe to call repeatedly.</summary>
    public void CloseGate()
    {
        if (fightStarted || bossDefeated) return;

        // The boss can already be gone if the player re-enters the arena after winning.
        if (boss == null || boss.IsDead)
        {
            bossDefeated = true;
            return;
        }

        fightStarted = true;

        if (closeDelay > 0f) Invoke(nameof(ApplyClose), closeDelay);
        else ApplyClose();

        onFightStarted?.Invoke();
    }

    /// <summary>Reopens the gate without waiting for the boss. For cutscenes or debugging.</summary>
    public void OpenGate()
    {
        CancelInvoke(nameof(ApplyClose));
        SetBarrierClosed(false);
    }

    private void ApplyClose()
    {
        // The boss can die inside the delay window; do not slam the gate shut afterwards.
        if (bossDefeated) return;
        SetBarrierClosed(true);

        fightAnnounced = true;
        OnBossFightStarted?.Invoke(boss);
    }

    private void HandleBossDied()
    {
        if (bossDefeated) return;
        bossDefeated = true;

        CancelInvoke(nameof(ApplyClose));
        SetBarrierClosed(false);
        AnnounceFightEnded();
        OnBossFightWon?.Invoke(boss);
        onBossDefeated?.Invoke();
    }

    private void AnnounceFightEnded()
    {
        if (!fightAnnounced) return;
        fightAnnounced = false;
        OnBossFightEnded?.Invoke(boss);
    }

    private void SetBarrierClosed(bool closed)
    {
        if (barrier == null) return;
        barrier.SetActive(closed);
    }
}
