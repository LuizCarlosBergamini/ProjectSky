using UnityEditor;
using UnityEngine;

/// <summary>
/// Backs up and restores the real saved settings around tests that write them. The backup lives in SessionState
/// so it survives the domain reload of entering Play Mode.
/// </summary>
public static class SettingsBackup
{
    private const string PendingKey = "LucyTests.SettingsBackup.Pending";
    private const string HadValueKey = "LucyTests.SettingsBackup.HadValue";
    private const string ValueKey = "LucyTests.SettingsBackup.Value";

    public static void Backup()
    {
        if (SessionState.GetBool(PendingKey, false)) return; // already backed up before a domain reload
        var key = PlayerPrefsSettingsStore.DefaultKey;
        SessionState.SetBool(HadValueKey, PlayerPrefs.HasKey(key));
        SessionState.SetString(ValueKey, PlayerPrefs.GetString(key, ""));
        SessionState.SetBool(PendingKey, true);
    }

    public static void Restore()
    {
        if (!SessionState.GetBool(PendingKey, false)) return;
        var key = PlayerPrefsSettingsStore.DefaultKey;
        if (SessionState.GetBool(HadValueKey, false)) PlayerPrefs.SetString(key, SessionState.GetString(ValueKey, ""));
        else PlayerPrefs.DeleteKey(key);
        PlayerPrefs.Save();
        SessionState.EraseBool(PendingKey);
        SessionState.EraseBool(HadValueKey);
        SessionState.EraseString(ValueKey);
    }
}
