using System;
using System.Collections.Generic;
using HierarchicalStateMachine;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// One-way gate for a boss arena.
/// The barrier starts open so the player can walk in, closes behind them on the way in,
/// and reopens for good once the boss dies. Losing the boss reference (destroyed, or never
/// assigned) always resolves to "open", so the player can never be sealed in by a bug.
/// A boss beaten during this visit stays beaten when a player death reloads the scene: the gate removes it and
/// replays onBossAlreadyDefeated instead of the fight. Only entering again from the Hub brings it back.
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

    [Tooltip("Chamado ao recarregar a cena depois de o chefe ja ter sido derrotado nesta visita (ex: o jogador morreu "
             + "junto com ele). Deve deixar a cena como apos a vitoria, sem entregar a recompensa de novo.")]
    [SerializeField] private UnityEvent onBossAlreadyDefeated;

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

    private static readonly List<BossGate> activeGates = new();

    /// <summary>
    /// True while a boss already took its killing blow but has not finished dying (Died not raised yet). A player
    /// who dies in that window waits for it before the level reloads, so the win still counts.
    /// </summary>
    public static bool IsAnyBossDying
    {
        get
        {
            foreach (BossGate gate in activeGates)
            {
                if (gate != null && gate.IsBossDying) return true;
            }

            return false;
        }
    }

    private bool fightStarted;
    private bool bossDefeated;
    private bool fightAnnounced;

    private bool IsBossDying => !bossDefeated && boss != null && !boss.IsAlive();

    private void Awake()
    {
        SetBarrierClosed(false);
        RestoreDefeatFromThisVisit();
    }

    private void OnEnable()
    {
        activeGates.Add(this);
        if (boss != null) boss.Died += HandleBossDied;
    }

    private void OnDisable()
    {
        activeGates.Remove(this);
        if (boss != null) boss.Died -= HandleBossDied;
        AnnounceFightEnded();
    }

    // Reload after a player death with the boss already beaten in this visit: no boss, gate open, the scene as
    // it was after the win. Its reward was paid when it died, so onBossDefeated is not replayed.
    private void RestoreDefeatFromThisVisit()
    {
        if (boss == null || BossProgressionManager.instance == null) return;
        if (!BossProgressionManager.instance.WasDefeatedThisVisit(boss.BossData)) return;

        bossDefeated = true;
        boss.gameObject.SetActive(false);
        Destroy(boss.gameObject);
        onBossAlreadyDefeated?.Invoke();
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
