using System;
using System.Collections.Generic;

/// <summary>
/// Everything one save slot keeps, as plain serializable data (JsonUtility). Holds only ids and numbers, never
/// asset references, so the file survives assets being moved or renamed. Adding a field is safe: older files
/// simply load it with its default. Removing or changing the meaning of one needs a <see cref="Version"/> bump and
/// a step in SaveSystem's migration.
/// </summary>
[Serializable]
public class SaveData
{
    public const int CurrentVersion = 1;

    public int version = CurrentVersion;

    /// <summary>When the save was written, as an ISO 8601 UTC string (for a future slot list).</summary>
    public string savedAtUtc;

    /// <summary>The scene Continue opens: the last safe scene the player was in (never a level or a boss arena).</summary>
    public string sceneName;

    public List<SavedItem> inventory = new();
    public List<string> defeatedBossIds = new();
    public List<SavedCounter> bossAttempts = new();
    public List<string> purchasedUpgradeIds = new();
    public List<string> startedTaskIds = new();
    public List<string> finishedTaskIds = new();
}

[Serializable]
public class SavedItem
{
    public string itemId;
    public int quantity;
}

[Serializable]
public class SavedCounter
{
    public string id;
    public int count;
}
