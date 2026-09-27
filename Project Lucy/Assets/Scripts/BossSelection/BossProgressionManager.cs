using System;
using System.Collections.Generic;
using System.Text;
using HierarchicalStateMachine;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>A boss as the selector sees it. Defeated bosses are always replayable.</summary>
public enum BossUnlockState
{
    Locked,
    Unlocked,
    Defeated
}

public enum BossEnterResult
{
    Success,
    InvalidBoss,
    Locked,
    SceneNotInBuild,
    AlreadyLoading
}

/// <summary>
/// Owns which bosses were beaten and answers which ones can be fought. Persistent like UpgradeManager, so it
/// survives Hub/level loads, and knows nothing about UI: the selector only reads from it and asks it to enter.
/// Unlocking follows actual defeats (BossGate.OnBossFightWon), never the reward items, so spending a reward
/// never locks a boss again. The reward itself stays with TaskManager, wired in each boss scene.
/// </summary>
public class BossProgressionManager : MonoBehaviour
{
    public static BossProgressionManager instance;

    /// <summary>Raised after a defeat, a load or a reset changes the defeated set.</summary>
    public static event Action OnProgressionChanged;

    [Tooltip("Chefes na ordem em que aparecem no seletor. Adicionar um chefe = adicionar o asset aqui.")]
    [SerializeField] private List<BossData_SO> _bosses = new();

    [Tooltip("Somente leitura em Play Mode: bossIds derrotados nesta sessao.")]
    [SerializeField] private List<string> _defeatedBossIds = new();

    // Set while a boss scene is loading, cleared when any scene finishes loading.
    private bool _entering;

    public IReadOnlyList<BossData_SO> Bosses => _bosses;

    private void Awake()
    {
        if (instance != null)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        ValidateRoster();
    }

    private void OnEnable()
    {
        // The Hub's copy that is about to destroy itself must not listen.
        if (instance != this) return;
        BossGate.OnBossFightWon += HandleBossFightWon;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        BossGate.OnBossFightWon -= HandleBossFightWon;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    #region Queries

    public bool IsDefeated(BossData_SO boss)
    {
        return boss != null && !string.IsNullOrWhiteSpace(boss.bossId) && _defeatedBossIds.Contains(boss.bossId);
    }

    /// <summary>Same rule as UpgradeManager.ArePrerequisitesMet: all or any, empty list = always met.</summary>
    public bool ArePrerequisitesMet(BossData_SO boss)
    {
        if (boss == null) return false;
        if (boss.prerequisites == null || boss.prerequisites.Count == 0) return true;

        bool anyMet = false;
        bool anyDeclared = false;
        foreach (BossData_SO prerequisite in boss.prerequisites)
        {
            if (prerequisite == null) continue;
            anyDeclared = true;

            bool met = IsDefeated(prerequisite);
            if (boss.requireAllPrerequisites && !met) return false;
            anyMet |= met;
        }

        return !anyDeclared || boss.requireAllPrerequisites || anyMet;
    }

    public bool IsUnlocked(BossData_SO boss)
    {
        return GetState(boss) != BossUnlockState.Locked;
    }

    public BossUnlockState GetState(BossData_SO boss)
    {
        if (boss == null) return BossUnlockState.Locked;
        // Checked first: a beaten boss stays replayable even if its requirements change later.
        if (IsDefeated(boss)) return BossUnlockState.Defeated;
        return ArePrerequisitesMet(boss) ? BossUnlockState.Unlocked : BossUnlockState.Locked;
    }

    /// <summary>
    /// The boss's reward, read live from its TaskManager task so it is never duplicated. Empty when the task
    /// or the TaskManager is missing (e.g. a level played on its own).
    /// </summary>
    public IReadOnlyList<InventorySlot> GetRewards(BossData_SO boss)
    {
        if (boss == null || TaskManager.instance == null) return Array.Empty<InventorySlot>();
        TasksItem task = TaskManager.instance.GetTask(boss.rewardTaskId);
        return task != null && task.rewardItems != null ? task.rewardItems : Array.Empty<InventorySlot>();
    }

    /// <summary>Whether beating this boss now would pay its reward (first clear, or a repeatable task).</summary>
    public bool WillGrantReward(BossData_SO boss)
    {
        return boss != null && TaskManager.instance != null && TaskManager.instance.WillGrantRewards(boss.rewardTaskId);
    }

    /// <summary>The line a locked panel shows: the asset's own text, or one built from its requirements.</summary>
    public string GetLockedHint(BossData_SO boss)
    {
        if (boss == null) return "";
        if (!string.IsNullOrWhiteSpace(boss.lockedHint)) return boss.lockedHint;

        List<string> missing = new();
        if (boss.prerequisites != null)
        {
            foreach (BossData_SO prerequisite in boss.prerequisites)
            {
                if (prerequisite != null && !IsDefeated(prerequisite)) missing.Add(prerequisite.DisplayName);
            }
        }

        if (missing.Count == 0) return "Derrote o chefe anterior para desbloquear";

        StringBuilder names = new();
        string joiner = boss.requireAllPrerequisites ? " e " : " ou ";
        for (int i = 0; i < missing.Count; i++)
        {
            if (i > 0) names.Append(i == missing.Count - 1 ? joiner : ", ");
            names.Append(missing[i]);
        }

        return $"Derrote {names} para desbloquear";
    }

    #endregion

    #region Progress

    /// <summary>Marks a boss as beaten. Called by the boss fight; public for cutscenes and debugging.</summary>
    public void RecordDefeat(BossData_SO boss)
    {
        if (boss == null || string.IsNullOrWhiteSpace(boss.bossId)) return;
        if (!_bosses.Contains(boss))
        {
            Debug.LogWarning($"Chefe '{boss.name}' derrotado, mas nao esta na lista do BossProgressionManager.", this);
            return;
        }

        if (_defeatedBossIds.Contains(boss.bossId)) return;

        _defeatedBossIds.Add(boss.bossId);
        Debug.Log($"Chefe '{boss.bossId}' derrotado pela primeira vez.");
        RaiseChanged();
    }

    private void HandleBossFightWon(EnemyStateDriver boss)
    {
        // ReferenceEquals: the boss is still alive when Died fires, but guard against a destroyed sender anyway.
        if (ReferenceEquals(boss, null)) return;

        BossData_SO data = boss.BossData;
        if (data == null)
        {
            Debug.LogWarning($"{boss.name}: chefe sem BossData_SO, progresso nao registrado.", boss);
            return;
        }

        RecordDefeat(data);
    }

    #endregion

    #region Enter

    /// <summary>
    /// Loads the boss's scene through the project's usual path (LevelManager's fade when it exists, a plain
    /// load otherwise). Refuses locked bosses, scenes missing from Build Settings and a second request while
    /// one is already loading, so the UI can never start two loads or a locked fight.
    /// </summary>
    public BossEnterResult TryEnterBoss(BossData_SO boss)
    {
        if (boss == null || !_bosses.Contains(boss)) return BossEnterResult.InvalidBoss;
        if (_entering || (LevelManager.instance != null && LevelManager.instance.IsLoading)) return BossEnterResult.AlreadyLoading;
        if (!IsUnlocked(boss)) return BossEnterResult.Locked;

        if (boss.scene == null || !boss.scene.IsInBuild)
        {
            Debug.LogError($"Chefe '{boss.name}': cena '{boss.scene}' nao esta no Build Settings.", boss);
            return BossEnterResult.SceneNotInBuild;
        }

        _entering = true;
        LoadScene(boss.scene.SceneName);
        return BossEnterResult.Success;
    }

    // Same fallback as LevelRunManager: LevelManager only exists when the game starts from the menu.
    private static void LoadScene(string sceneName)
    {
        if (LevelManager.instance != null)
        {
            LevelManager.instance.LoadScene(sceneName);
            return;
        }

        SceneManager.LoadScene(sceneName);
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _entering = false;
    }

    #endregion

    #region Persistence Hooks

    // No save system exists yet (same situation as UpgradeManager). These are the only entry points one
    // needs: write the ids out, read them back in.

    public List<string> GetDefeatedBossIds()
    {
        return new List<string>(_defeatedBossIds);
    }

    public void LoadDefeatedBossIds(IEnumerable<string> bossIds)
    {
        _defeatedBossIds.Clear();
        if (bossIds != null)
        {
            foreach (string bossId in bossIds)
            {
                if (FindById(bossId) == null)
                {
                    Debug.LogWarning($"Chefe '{bossId}' salvo nao existe mais e foi ignorado.", this);
                    continue;
                }

                if (!_defeatedBossIds.Contains(bossId)) _defeatedBossIds.Add(bossId);
            }
        }

        RaiseChanged();
    }

    /// <summary>Forgets every defeat: only bosses without requirements stay unlocked.</summary>
    public void ResetProgression()
    {
        _defeatedBossIds.Clear();
        RaiseChanged();
    }

    #endregion

    private BossData_SO FindById(string bossId)
    {
        if (string.IsNullOrWhiteSpace(bossId)) return null;
        foreach (BossData_SO boss in _bosses)
        {
            if (boss != null && boss.bossId == bossId) return boss;
        }

        return null;
    }

    private void ValidateRoster()
    {
        HashSet<string> seen = new();
        foreach (BossData_SO boss in _bosses)
        {
            if (boss == null) continue;

            if (string.IsNullOrWhiteSpace(boss.bossId))
                Debug.LogWarning($"Chefe '{boss.name}' sem bossId, o progresso dele nao sera salvo.", boss);
            else if (!seen.Add(boss.bossId))
                Debug.LogWarning($"bossId '{boss.bossId}' repetido em '{boss.name}'.", boss);

            if (boss.scene == null || !boss.scene.IsInBuild)
                Debug.LogWarning($"Chefe '{boss.name}': cena '{boss.scene}' nao esta no Build Settings.", boss);
        }
    }

    private static void RaiseChanged()
    {
        OnProgressionChanged?.Invoke();
    }

#if UNITY_EDITOR
    [ContextMenu("Debug/Derrotar proximo chefe")]
    private void DebugDefeatNext()
    {
        if (!Application.isPlaying) return;
        foreach (BossData_SO boss in _bosses)
        {
            if (GetState(boss) != BossUnlockState.Unlocked) continue;
            RecordDefeat(boss);
            return;
        }
    }

    [ContextMenu("Debug/Derrotar todos")]
    private void DebugDefeatAll()
    {
        if (!Application.isPlaying) return;
        List<string> ids = new();
        foreach (BossData_SO boss in _bosses)
        {
            if (boss != null && !string.IsNullOrWhiteSpace(boss.bossId)) ids.Add(boss.bossId);
        }

        LoadDefeatedBossIds(ids);
    }

    [ContextMenu("Debug/Resetar progresso")]
    private void DebugResetProgression()
    {
        if (Application.isPlaying) ResetProgression();
    }
#endif
}
