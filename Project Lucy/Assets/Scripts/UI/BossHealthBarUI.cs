using System.Collections;
using System.Collections.Generic;
using HierarchicalStateMachine;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The boss's health bar, top-right of the level HUD, plus the reward panel under it.
/// Works for any EnemyStateDriver: name, portrait and reward all come from its BossData_SO.
/// Hidden by default; BossFightUIController decides when it binds, shows and hides.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class BossHealthBarUI : MonoBehaviour
{
    [SerializeField] private HealthBarView bar;
    [SerializeField] private Image portrait;
    [SerializeField] private TextMeshProUGUI nameText;

    [Header("Recompensa")]
    [Tooltip("Painel inteiro da recompensa; escondido quando o chefe nao tem recompensa a entregar.")]
    [SerializeField] private GameObject rewardPanel;
    [Tooltip("Onde as linhas de recompensa sao criadas (VerticalLayoutGroup).")]
    [SerializeField] private Transform rewardEntryContainer;
    [SerializeField] private RewardEntryUI rewardEntryPrefab;

    [Header("Transicao")]
    [SerializeField] private float fadeInDuration = 0.2f;
    [Tooltip("Duracao do fade ao sumir, depois que a barra termina de esvaziar.")]
    [SerializeField] private float fadeOutDuration = 0.35f;
    [Tooltip("Tempo maximo esperando o rastro de dano esvaziar antes do fade.")]
    [SerializeField] private float maxDrainWait = 1f;

    private readonly List<RewardEntryUI> rewardEntries = new();
    private CanvasGroup group;
    private EnemyStateDriver boss;
    private Coroutine fadeRoutine;
    private bool visible;

    public bool IsVisible => visible;

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        SetAlpha(0f);
    }

    private void OnDisable()
    {
        Unbind();
        fadeRoutine = null;
        visible = false;
        SetAlpha(0f);
    }

    /// <summary>Starts following this boss and fills in its name, portrait and reward.</summary>
    public void Bind(EnemyStateDriver newBoss)
    {
        Unbind();
        boss = newBoss;
        if (boss == null) return;

        boss.HealthChanged += HandleHealthChanged;

        BossData_SO data = boss.BossData;
        Entity_SO entity = data != null ? data.entity : null;
        if (data == null) Debug.LogWarning($"{boss.name}: chefe sem BossData_SO, usando o nome do objeto.", boss);

        if (nameText != null) nameText.text = entity != null ? entity.entityName : boss.name;
        if (portrait != null)
        {
            portrait.sprite = entity != null ? entity.entitySprite : null;
            portrait.enabled = portrait.sprite != null;
        }

        BuildRewards(data);
        bar.SetValues(boss.CurrentHealth, boss.MaxHealth, true);
    }

    public void Show()
    {
        if (visible) return;
        visible = true;
        StartFade(1f, fadeInDuration, false);
    }

    /// <summary>Waits for the bar to finish draining, fades out, then lets go of the boss. Safe to call repeatedly.</summary>
    public void Hide()
    {
        if (!visible) return;
        visible = false;
        if (!isActiveAndEnabled)
        {
            SetAlpha(0f);
            Unbind();
            return;
        }

        StartFade(0f, fadeOutDuration, true);
    }

    private void Unbind()
    {
        // ReferenceEquals: after its death the boss is destroyed (Unity-null) but must still be
        // unsubscribed, or this bar would keep a reference to it.
        if (!ReferenceEquals(boss, null)) boss.HealthChanged -= HandleHealthChanged;
        boss = null;
    }

    private void HandleHealthChanged(float current, float max)
    {
        bar.SetValues(current, max, false);

        // Leave on the killing blow; the fight-ended event only arrives after the death animation.
        if (current <= 0f) Hide();
    }

    private void BuildRewards(BossData_SO data)
    {
        foreach (RewardEntryUI entry in rewardEntries)
        {
            if (entry != null) Destroy(entry.gameObject);
        }
        rewardEntries.Clear();

        List<InventorySlot> rewards = GetPendingRewards(data);
        if (rewards != null && rewardEntryPrefab != null && rewardEntryContainer != null)
        {
            foreach (InventorySlot reward in rewards)
            {
                if (reward == null || reward.item == null) continue;
                RewardEntryUI entry = Instantiate(rewardEntryPrefab, rewardEntryContainer);
                entry.Set(reward.item, reward.quantity);
                rewardEntries.Add(entry);
            }
        }

        if (rewardPanel != null) rewardPanel.SetActive(rewardEntries.Count > 0);
    }

    // The reward is the task's reward list, read live from TaskManager so it is never duplicated.
    // A finished task only pays out again when it is marked repeatableReward; otherwise show nothing.
    private static List<InventorySlot> GetPendingRewards(BossData_SO data)
    {
        if (data == null || string.IsNullOrWhiteSpace(data.rewardTaskId) || TaskManager.instance == null) return null;

        if (!TaskManager.instance.WillGrantRewards(data.rewardTaskId)) return null;
        return TaskManager.instance.GetTask(data.rewardTaskId).rewardItems;
    }

    private void StartFade(float targetAlpha, float duration, bool unbindWhenDone)
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(Fade(targetAlpha, duration, unbindWhenDone));
    }

    private IEnumerator Fade(float targetAlpha, float duration, bool unbindWhenDone)
    {
        if (unbindWhenDone)
        {
            float waited = 0f;
            while (bar.IsAnimating && waited < maxDrainWait)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        float start = group.alpha;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            SetAlpha(Mathf.Lerp(start, targetAlpha, t / duration));
            yield return null;
        }

        SetAlpha(targetAlpha);
        if (unbindWhenDone) Unbind();
        fadeRoutine = null;
    }

    private void SetAlpha(float alpha)
    {
        if (group != null) group.alpha = alpha;
    }
}
