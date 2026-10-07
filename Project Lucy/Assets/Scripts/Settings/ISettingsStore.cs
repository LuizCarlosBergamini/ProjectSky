using UnityEngine;

/// <summary>Where GameSettings live between sessions. Swappable so tests (or a future save system) can supply their own.</summary>
public interface ISettingsStore
{
    /// <summary>False when nothing was saved yet or the save is unreadable.</summary>
    bool TryLoad(out GameSettings settings);

    void Save(GameSettings settings);
}

/// <summary>The settings as one JSON string in PlayerPrefs, so adding a field never needs a new key.</summary>
public class PlayerPrefsSettingsStore : ISettingsStore
{
    public const string DefaultKey = "Lucy.Settings";

    private readonly string _key;

    public PlayerPrefsSettingsStore(string key = DefaultKey)
    {
        _key = key;
    }

    public bool TryLoad(out GameSettings settings)
    {
        settings = PlayerPrefs.HasKey(_key) ? GameSettings.FromJson(PlayerPrefs.GetString(_key)) : null;
        return settings != null;
    }

    public void Save(GameSettings settings)
    {
        PlayerPrefs.SetString(_key, settings.ToJson());
        PlayerPrefs.Save();
    }
}

/// <summary>Keeps the settings in memory only. For tests and for running without persistence.</summary>
public class MemorySettingsStore : ISettingsStore
{
    public string Json { get; private set; }
    public int SaveCount { get; private set; }

    public MemorySettingsStore(string json = null)
    {
        Json = json;
    }

    public bool TryLoad(out GameSettings settings)
    {
        settings = GameSettings.FromJson(Json);
        return settings != null;
    }

    public void Save(GameSettings settings)
    {
        Json = settings.ToJson();
        SaveCount++;
    }
}
