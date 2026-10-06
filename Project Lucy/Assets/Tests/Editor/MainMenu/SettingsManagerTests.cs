using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// SettingsManager without any UI or mixer, through its Initialize test seam and an in-memory store.
/// Awake does not run in Edit Mode, so nothing here touches the real PlayerPrefs settings.
/// </summary>
public class SettingsManagerTests
{
    private GameObject _go;

    [TearDown]
    public void TearDown()
    {
        if (_go != null) Object.DestroyImmediate(_go);
    }

    private SettingsManager Create(MemorySettingsStore store)
    {
        if (_go != null) Object.DestroyImmediate(_go);
        _go = new GameObject("SettingsManagerTest");
        var manager = _go.AddComponent<SettingsManager>();
        manager.Initialize(null, store);
        return manager;
    }

    [Test]
    public void NothingSaved_LoadsFullVolumeDefaults()
    {
        var manager = Create(new MemorySettingsStore());
        Assert.That(manager.GetVolume(VolumeChannel.Master), Is.EqualTo(1f));
        Assert.That(manager.GetVolume(VolumeChannel.Music), Is.EqualTo(1f));
        Assert.That(manager.GetVolume(VolumeChannel.Sfx), Is.EqualTo(1f));
    }

    [Test]
    public void SavedValues_SurviveANewSession()
    {
        var store = new MemorySettingsStore();
        var first = Create(store);
        first.SetVolume(VolumeChannel.Master, 0.5f);
        first.SetVolume(VolumeChannel.Music, 0f);
        first.SetVolume(VolumeChannel.Sfx, 0.25f);
        first.Save();

        var second = Create(new MemorySettingsStore(store.Json));
        Assert.That(second.GetVolume(VolumeChannel.Master), Is.EqualTo(0.5f));
        Assert.That(second.GetVolume(VolumeChannel.Music), Is.EqualTo(0f));
        Assert.That(second.GetVolume(VolumeChannel.Sfx), Is.EqualTo(0.25f));
    }

    [Test]
    public void Save_WritesOnlyWhenSomethingChanged()
    {
        var store = new MemorySettingsStore();
        var manager = Create(store);

        manager.Save();
        Assert.That(store.SaveCount, Is.EqualTo(0), "Saved with nothing changed.");

        manager.SetVolume(VolumeChannel.Master, 0.3f);
        Assert.That(manager.HasUnsavedChanges, Is.True);
        manager.Save();
        Assert.That(store.SaveCount, Is.EqualTo(1));
        Assert.That(manager.HasUnsavedChanges, Is.False);

        manager.Save();
        Assert.That(store.SaveCount, Is.EqualTo(1));
    }

    [Test]
    public void SetVolume_ClampsToTheSliderRange()
    {
        var manager = Create(new MemorySettingsStore());
        manager.SetVolume(VolumeChannel.Master, 3f);
        Assert.That(manager.GetVolume(VolumeChannel.Master), Is.EqualTo(1f));
        manager.SetVolume(VolumeChannel.Master, -1f);
        Assert.That(manager.GetVolume(VolumeChannel.Master), Is.EqualTo(0f));
    }

    [Test]
    public void SetVolume_RaisesOnSettingsChanged()
    {
        var manager = Create(new MemorySettingsStore());
        var raised = 0;
        void Handler() => raised++;
        SettingsManager.OnSettingsChanged += Handler;
        try
        {
            manager.SetVolume(VolumeChannel.Music, 0.4f);
            Assert.That(raised, Is.EqualTo(1));
            manager.SetVolume(VolumeChannel.Music, 0.4f);
            Assert.That(raised, Is.EqualTo(1), "Setting the same value again should not raise the event.");
        }
        finally
        {
            SettingsManager.OnSettingsChanged -= Handler;
        }
    }

    [TestCase("not json")]
    [TestCase("{\"masterVolume\":")]
    public void CorruptSave_FallsBackToDefaults(string json)
    {
        var manager = Create(new MemorySettingsStore(json));
        Assert.That(manager.GetVolume(VolumeChannel.Master), Is.EqualTo(1f));
    }

    [Test]
    public void OutOfRangeSave_IsClamped()
    {
        var manager = Create(new MemorySettingsStore("{\"masterVolume\":7,\"musicVolume\":-2,\"sfxVolume\":0.5}"));
        Assert.That(manager.GetVolume(VolumeChannel.Master), Is.EqualTo(1f));
        Assert.That(manager.GetVolume(VolumeChannel.Music), Is.EqualTo(0f));
        Assert.That(manager.GetVolume(VolumeChannel.Sfx), Is.EqualTo(0.5f));
    }

    [Test]
    public void SaveMissingAField_LoadsItsDefault()
    {
        // A save written before a setting existed.
        var manager = Create(new MemorySettingsStore("{\"masterVolume\":0.2}"));
        Assert.That(manager.GetVolume(VolumeChannel.Master), Is.EqualTo(0.2f));
        Assert.That(manager.GetVolume(VolumeChannel.Music), Is.EqualTo(1f));
    }

    [Test]
    public void Current_IsACopy()
    {
        var manager = Create(new MemorySettingsStore());
        manager.Current.masterVolume = 0f;
        Assert.That(manager.GetVolume(VolumeChannel.Master), Is.EqualTo(1f));
    }

    [Test]
    public void EveryChannel_HasADistinctMixerParameter()
    {
        var manager = Create(new MemorySettingsStore());
        var parameters = new[] { VolumeChannel.Master, VolumeChannel.Music, VolumeChannel.Sfx }.Select(manager.GetParameter).ToArray();
        Assert.That(parameters, Is.EqualTo(new[] { "MasterVolume", "MusicVolume", "SFXVolume" }));
    }

    [Test]
    public void PlayerPrefsStore_RoundTrips()
    {
        const string key = "LucyTests.SettingsRoundTrip";
        var store = new PlayerPrefsSettingsStore(key);
        try
        {
            Assert.That(store.TryLoad(out _), Is.False);
            var settings = new GameSettings { masterVolume = 0.6f, sfxVolume = 0f };
            store.Save(settings);

            Assert.That(store.TryLoad(out var loaded), Is.True);
            Assert.That(loaded.masterVolume, Is.EqualTo(0.6f));
            Assert.That(loaded.sfxVolume, Is.EqualTo(0f));
            Assert.That(loaded.musicVolume, Is.EqualTo(1f));
        }
        finally
        {
            PlayerPrefs.DeleteKey(key);
        }
    }
}
