using System;
using UnityEngine;

/// <summary>The volume sliders, one per exposed group of Main.mixer.</summary>
public enum VolumeChannel
{
    Master,
    Music,
    Sfx
}

/// <summary>
/// Every player setting, as plain data. SettingsManager owns the live copy, applies it and persists it as JSON.
/// To add a setting: add a public field with its default here, apply it in SettingsManager.ApplyAll and give it
/// a control in the Options menu. Saves written before the field existed simply load the default.
/// </summary>
[Serializable]
public class GameSettings
{
    public const int CurrentVersion = 1;

    // Bumped when a field changes meaning, so a future migration can tell old saves apart.
    public int version = CurrentVersion;

    // Linear slider values, 0 (mute) to 1 (full). SettingsManager converts them to decibels for the mixer.
    [Range(0f, 1f)] public float masterVolume = 1f;
    [Range(0f, 1f)] public float musicVolume = 1f;
    [Range(0f, 1f)] public float sfxVolume = 1f;

    // PLACEHOLDER: resolution, fullscreen, key bindings and language are not implemented yet. They will be new
    // fields here (see the placeholder sections of the Options menu).

    public float GetVolume(VolumeChannel channel)
    {
        return channel switch
        {
            VolumeChannel.Music => musicVolume,
            VolumeChannel.Sfx => sfxVolume,
            _ => masterVolume
        };
    }

    public void SetVolume(VolumeChannel channel, float value)
    {
        value = SanitizeVolume(value);
        switch (channel)
        {
            case VolumeChannel.Music: musicVolume = value; break;
            case VolumeChannel.Sfx: sfxVolume = value; break;
            default: masterVolume = value; break;
        }
    }

    /// <summary>Clamps values a hand-edited or corrupted save could carry.</summary>
    public void Sanitize()
    {
        masterVolume = SanitizeVolume(masterVolume);
        musicVolume = SanitizeVolume(musicVolume);
        sfxVolume = SanitizeVolume(sfxVolume);
    }

    public GameSettings Clone()
    {
        return (GameSettings)MemberwiseClone();
    }

    public string ToJson()
    {
        return JsonUtility.ToJson(this);
    }

    /// <summary>Defaults for anything the JSON leaves out; null when it is not valid JSON.</summary>
    public static GameSettings FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        GameSettings settings = new();
        try
        {
            JsonUtility.FromJsonOverwrite(json, settings);
        }
        catch (ArgumentException)
        {
            return null;
        }

        settings.Sanitize();
        return settings;
    }

    private static float SanitizeVolume(float value)
    {
        return float.IsNaN(value) ? 1f : Mathf.Clamp01(value);
    }
}
