using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The one entry point to save data. Everything else (menus, pause, autosave) only calls these static methods.
/// A save is <see cref="SaveData"/> as plain JSON, written atomically with a backup by <see cref="SaveFileStore"/>
/// under Application.persistentDataPath/Saves. The file is not protected against hand editing (a deliberate choice).
/// Settings are not part of it: they stay in PlayerPrefs with SettingsManager.
///
/// What is kept: inventory, defeated bosses and attempts, purchased upgrades, started/finished tasks, and the scene
/// to continue from. Saving is allowed anywhere outside the main menu, but only progress that is already secured is
/// written: pickups of a level run in progress are left out and the scene is the last safe one (never a level or a
/// boss arena), so Continue always lands back in the Hub side of the game.
///
/// Loading happens in two steps because the managers live in the gameplay scenes: <see cref="TryLoadMostRecent"/>
/// reads the file and returns the scene, and the data is pushed into the managers as soon as that scene is loaded
/// (after their Awake, before any Start).
///
/// Autosave runs when a boss is defeated, an upgrade is bought and when the player arrives in a safe scene.
/// </summary>
public static class SaveSystem
{
    public const int DefaultSlot = 0;

    /// <summary>Where Continue goes when the save has no usable scene (e.g. saved before ever reaching a safe scene).</summary>
    public const string FallbackSceneName = "Hub";

    public const string MainMenuSceneName = "MainMenu";

    private const string SaveFolderName = "Saves";

    /// <summary>Raised after every successful save, manual or automatic.</summary>
    public static event Action Saved;

    /// <summary>The slot every call reads and writes. Only slot 0 is used for now.</summary>
    public static int ActiveSlot { get; private set; } = DefaultSlot;

    /// <summary>False turns off every automatic save; manual saves still work.</summary>
    public static bool AutosaveEnabled { get; set; } = true;

    /// <summary>True once a game was started or continued (or something was saved) in this session.</summary>
    public static bool IsSessionActive { get; private set; }

    /// <summary>Whether <see cref="Save"/> would run now: not in the main menu and no loaded save waiting to be applied.</summary>
    public static bool CanSaveNow =>
        SceneManager.GetActiveScene().name != MainMenuSceneName &&
        _pending == null;

    private static SaveFileStore _store;
    private static SaveData _pending;
    private static string _lastSafeScene;
    private static bool _applying;

    #region Lifecycle

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Keeps Enter Play Mode without domain reload from carrying the last session's state.
        Unsubscribe();
        Saved = null;
        ActiveSlot = DefaultSlot;
        AutosaveEnabled = true;
        IsSessionActive = false;
        _store = null;
        _pending = null;
        _lastSafeScene = null;
        _applying = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        Unsubscribe();
        SceneManager.sceneLoaded += HandleSceneLoaded;
        BossProgressionManager.OnProgressionChanged += HandleProgressMade;
        UpgradeManager.OnUpgradesChanged += HandleProgressMade;
    }

    private static void Unsubscribe()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        BossProgressionManager.OnProgressionChanged -= HandleProgressMade;
        UpgradeManager.OnUpgradesChanged -= HandleProgressMade;
    }

    private static SaveFileStore Store =>
        _store ??= new SaveFileStore(Path.Combine(Application.persistentDataPath, SaveFolderName));

    #endregion

    #region Main Menu API

    /// <summary>True when there is a valid (readable) save the Continue button can load.</summary>
    public static bool HasSave()
    {
        return Store.Exists(ActiveSlot) && TryRead(ActiveSlot, out _);
    }

    /// <summary>
    /// Reads the most recent save and returns the scene to open. The data reaches the managers once that scene has
    /// loaded. Returns false when there is no valid save; the caller then stays on the menu.
    /// </summary>
    public static bool TryLoadMostRecent(out string sceneName)
    {
        sceneName = null;
        if (!TryRead(ActiveSlot, out SaveData data)) return false;

        _pending = data;
        _lastSafeScene = data.sceneName;
        IsSessionActive = true;
        sceneName = string.IsNullOrEmpty(data.sceneName) ? FallbackSceneName : data.sceneName;
        return true;
    }

    /// <summary>
    /// Called by New Game. Starts a session on the active slot. The old file is not deleted here, but the first save
    /// of the new game (the autosave on reaching the Hub) overwrites it.
    /// </summary>
    public static void CreateNewSave()
    {
        _pending = null;
        _lastSafeScene = null;
        IsSessionActive = true;
    }

    #endregion

    #region Save

    /// <summary>
    /// Writes the current progress to the active slot. Returns false (and logs why) when it is not possible now
    /// (see <see cref="CanSaveNow"/>) or the file could not be written. Wire the pause menu's Save button here.
    /// </summary>
    public static bool Save()
    {
        if (!CanSaveNow)
        {
            Debug.LogWarning("Nao e possivel salvar agora (menu principal ou save sendo carregado).");
            return false;
        }

        try
        {
            string json = JsonUtility.ToJson(Capture(), Debug.isDebugBuild);
            Store.Write(ActiveSlot, json);
        }
        catch (Exception e)
        {
            Debug.LogError($"Erro ao salvar o jogo: {e}");
            return false;
        }

        IsSessionActive = true;
        Saved?.Invoke();
        return true;
    }

    /// <summary>Removes the slot's save and its backup.</summary>
    public static void DeleteSave(int slot = DefaultSlot)
    {
        Store.Delete(slot);
    }

    private static SaveData Capture()
    {
        SaveData data = new()
        {
            savedAtUtc = DateTime.UtcNow.ToString("o"),
            sceneName = string.IsNullOrEmpty(_lastSafeScene) ? FallbackSceneName : _lastSafeScene
        };

        if (InventoryManager.instance != null)
        {
            bool runInProgress = LevelRunManager.instance != null;
            foreach (KeyValuePair<string, int> entry in InventoryManager.instance.GetSavedQuantities(runInProgress))
            {
                data.inventory.Add(new SavedItem { itemId = entry.Key, quantity = entry.Value });
            }
        }

        if (BossProgressionManager.instance != null)
        {
            data.defeatedBossIds = BossProgressionManager.instance.GetDefeatedBossIds();
            foreach (KeyValuePair<string, int> entry in BossProgressionManager.instance.GetBossAttempts())
            {
                data.bossAttempts.Add(new SavedCounter { id = entry.Key, count = entry.Value });
            }
        }

        if (UpgradeManager.instance != null)
        {
            data.purchasedUpgradeIds = UpgradeManager.instance.GetPurchasedNodeIds();
        }

        if (TaskManager.instance != null)
        {
            data.startedTaskIds = TaskManager.instance.GetStartedTaskIds();
            data.finishedTaskIds = TaskManager.instance.GetFinishedTaskIds();
        }

        return data;
    }

    #endregion

    #region Load

    private static bool TryRead(int slot, out SaveData data)
    {
        data = null;
        foreach (string text in Store.ReadCandidates(slot))
        {
            if (TryParse(text, out data)) return true;
            Debug.LogWarning("Save corrompido ou invalido; tentando o backup.");
        }

        return false;
    }

    private static bool TryParse(string fileText, out SaveData data)
    {
        data = null;
        if (string.IsNullOrWhiteSpace(fileText)) return false;

        try
        {
            data = JsonUtility.FromJson<SaveData>(fileText);
        }
        catch (Exception)
        {
            // Not JSON at all (truncated or garbage file).
            return false;
        }

        return data != null && Migrate(data);
    }

    /// <summary>Upgrades older saves to <see cref="SaveData.CurrentVersion"/>. False for saves from a newer game version.</summary>
    private static bool Migrate(SaveData data)
    {
        if (data.version > SaveData.CurrentVersion)
        {
            Debug.LogWarning($"Save da versao {data.version}, mais nova que a do jogo ({SaveData.CurrentVersion}).");
            return false;
        }

        // Future steps go here, one per version: if (data.version == 1) { ...; data.version = 2; }
        data.version = SaveData.CurrentVersion;

        // Hand-made or partial files can miss whole lists.
        data.inventory ??= new List<SavedItem>();
        data.defeatedBossIds ??= new List<string>();
        data.bossAttempts ??= new List<SavedCounter>();
        data.purchasedUpgradeIds ??= new List<string>();
        data.startedTaskIds ??= new List<string>();
        data.finishedTaskIds ??= new List<string>();
        return true;
    }

    private static void Apply(SaveData data)
    {
        _applying = true;
        try
        {
            // Tasks before the inventory, so the inventory's refresh updates the rebuilt task cards.
            if (TaskManager.instance != null)
                TaskManager.instance.LoadTaskState(data.startedTaskIds, data.finishedTaskIds);
            else
                Debug.LogWarning("TaskManager nao encontrado ao carregar o save: tarefas nao restauradas.");

            if (InventoryManager.instance != null)
                InventoryManager.instance.LoadInventory(ResolveItems(data.inventory));
            else
                Debug.LogWarning("InventoryManager nao encontrado ao carregar o save: inventario nao restaurado.");

            if (BossProgressionManager.instance != null)
            {
                BossProgressionManager.instance.LoadDefeatedBossIds(data.defeatedBossIds);
                Dictionary<string, int> attempts = new();
                foreach (SavedCounter counter in data.bossAttempts)
                {
                    if (counter != null && !string.IsNullOrEmpty(counter.id)) attempts[counter.id] = counter.count;
                }
                BossProgressionManager.instance.LoadBossAttempts(attempts);
            }
            else
            {
                Debug.LogWarning("BossProgressionManager nao encontrado ao carregar o save: chefes nao restaurados.");
            }

            if (UpgradeManager.instance != null)
                UpgradeManager.instance.LoadPurchasedNodeIds(data.purchasedUpgradeIds);
            else
                Debug.LogWarning("UpgradeManager nao encontrado ao carregar o save: upgrades nao restaurados.");
        }
        finally
        {
            _applying = false;
        }
    }

    private static List<InventorySlot> ResolveItems(List<SavedItem> saved)
    {
        List<InventorySlot> slots = new();
        if (saved == null || saved.Count == 0) return slots;

        ItemDatabase database = ItemDatabase.Load();
        if (database == null) return slots;

        foreach (SavedItem entry in saved)
        {
            if (entry == null) continue;
            Item_SO item = database.Find(entry.itemId);
            if (item == null)
            {
                Debug.LogWarning($"Item '{entry.itemId}' salvo nao existe mais e foi ignorado.");
                continue;
            }

            slots.Add(new InventorySlot { item = item, quantity = entry.quantity });
        }

        return slots;
    }

    #endregion

    #region Scene Tracking And Autosave

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;

        if (scene.name == MainMenuSceneName)
        {
            // Back on the menu: nothing is saved until New Game or Continue starts a session again.
            IsSessionActive = false;
            return;
        }

        bool justLoaded = false;
        if (_pending != null)
        {
            SaveData data = _pending;
            _pending = null;
            Apply(data);
            justLoaded = true;
        }

        if (!IsSafeScene(scene)) return;

        _lastSafeScene = scene.name;
        if (!justLoaded) Autosave();
    }

    /// <summary>A scene Continue may return to: not a level run (LevelRunManager) and not a boss arena (BossGate).</summary>
    private static bool IsSafeScene(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.GetComponentInChildren<BossGate>(true) != null) return false;
            if (root.GetComponentInChildren<LevelRunManager>(true) != null) return false;
        }

        return true;
    }

    private static void HandleProgressMade()
    {
        // Loads and resets also raise these events; only real progress should write the file.
        if (_applying) return;
        Autosave();
    }

    private static void Autosave()
    {
        if (!AutosaveEnabled || !IsSessionActive || !CanSaveNow) return;
        Save();
    }

    #endregion
}
