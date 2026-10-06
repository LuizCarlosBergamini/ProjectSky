using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// Saved volumes reach the real Main.mixer as soon as Play Mode starts, with no menu opened and no scene-placed
/// manager, and the Options sliders drive the mixer live. The player's real saved settings are backed up and
/// restored around every test.
/// </summary>
public class SettingsStartupTests
{
    private const float Tolerance = 0.05f;

    [SetUp]
    public void SetUp() => SettingsBackup.Backup();

    [TearDown]
    public void TearDown() => SettingsBackup.Restore();

    private static void WriteSaved(float master, float music, float sfx)
    {
        var settings = new GameSettings { masterVolume = master, musicVolume = music, sfxVolume = sfx };
        new PlayerPrefsSettingsStore().Save(settings);
    }

    private static float MixerDecibels(string parameter)
    {
        var mixer = SettingsManager.instance.Mixer;
        Assert.That(mixer, Is.Not.Null, "SettingsManager has no mixer assigned.");
        Assert.That(mixer.GetFloat(parameter, out var value), Is.True, $"{parameter} is not exposed on {mixer.name}.");
        return value;
    }

    [Test]
    public void MainMixer_ExposesTheThreeVolumeParameters()
    {
        var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MenuTestPaths.MainMixer);
        Assert.That(mixer, Is.Not.Null);
        Assert.That(mixer.FindMatchingGroups("Master"), Is.Not.Empty);
        Assert.That(mixer.FindMatchingGroups("Music"), Is.Not.Empty);
        Assert.That(mixer.FindMatchingGroups("SFX"), Is.Not.Empty);
    }

    [Test]
    public void SettingsManagerPrefab_IsInResources_WithTheMainMixer()
    {
        var prefab = Resources.Load<GameObject>(SettingsManager.ResourcePath);
        Assert.That(prefab, Is.Not.Null, "Resources/SettingsManager is missing, so nothing is applied at startup.");
        var manager = prefab.GetComponent<SettingsManager>();
        Assert.That(manager, Is.Not.Null);
        Assert.That(manager.Mixer, Is.EqualTo(AssetDatabase.LoadAssetAtPath<AudioMixer>(MenuTestPaths.MainMixer)));
    }

    [UnityTest]
    public IEnumerator SavedVolumes_AreOnTheMixerAtStartup_WithoutOpeningTheMenu()
    {
        WriteSaved(0.5f, 0f, 0.1f);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        yield return new EnterPlayMode();
        yield return null; // past SettingsManager.Start, which re-applies after the start snapshot

        Assert.That(SettingsManager.instance, Is.Not.Null, "No SettingsManager was created at startup.");
        Assert.That(Object.FindAnyObjectByType<OptionsMenuController>(FindObjectsInactive.Include), Is.Null,
            "The test scene should have no Options menu.");
        Assert.That(SettingsManager.instance.GetVolume(VolumeChannel.Master), Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(MixerDecibels("MasterVolume"), Is.EqualTo(-6.02f).Within(Tolerance));
        Assert.That(MixerDecibels("MusicVolume"), Is.LessThanOrEqualTo(-80f + Tolerance), "Music at 0 should be silent.");
        Assert.That(MixerDecibels("SFXVolume"), Is.EqualTo(-20f).Within(Tolerance));

        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator SettingsManager_SurvivesSceneLoads_AndThereIsOnlyOne()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        yield return null;

        var first = SettingsManager.instance;
        Assert.That(first, Is.Not.Null);
        // Swap the only scene for another, as a scene load would.
        var original = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var next = UnityEngine.SceneManagement.SceneManager.CreateScene("SettingsTestNext");
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(next);
        yield return UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(original);
        yield return null;

        Assert.That(SettingsManager.instance, Is.SameAs(first));
        Assert.That(Object.FindObjectsByType<SettingsManager>(FindObjectsSortMode.None).Length, Is.EqualTo(1));

        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator SetVolume_ReachesTheMixerImmediately()
    {
        WriteSaved(1f, 1f, 1f);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        yield return null;

        SettingsManager.instance.SetVolume(VolumeChannel.Master, 0.1f);
        Assert.That(MixerDecibels("MasterVolume"), Is.EqualTo(-20f).Within(Tolerance), "Not applied in the same frame.");
        SettingsManager.instance.SetVolume(VolumeChannel.Master, 0f);
        Assert.That(MixerDecibels("MasterVolume"), Is.LessThanOrEqualTo(-80f + Tolerance));

        // Drop the unsaved change so leaving Play Mode (OnApplicationQuit saves) doesn't write it.
        SettingsManager.instance.Load();
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator OptionsSlider_DrivesTheMixerLive_AndIsSavedOnClose()
    {
        MenuTestPaths.RequireOptionsPrefab();
        WriteSaved(1f, 1f, 1f);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();

        new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem),
            typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
        var options = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(MenuTestPaths.OptionsMenuPrefab))
            .GetComponentInChildren<OptionsMenuController>(true);
        yield return null;
        options.Open();
        yield return null;

        var masterRow = MenuTestUtils.FindInScenes("Row_MasterVolume");
        Assert.That(masterRow, Is.Not.Null, "No Row_MasterVolume in the Options menu.");
        var slider = masterRow.GetComponentInChildren<Slider>();
        Assert.That(slider, Is.Not.Null);

        // A drag is a series of value changes; each should reach the mixer straight away.
        slider.normalizedValue = 0.5f;
        Assert.That(SettingsManager.instance.GetVolume(VolumeChannel.Master), Is.EqualTo(0.5f).Within(0.01f));
        Assert.That(MixerDecibels("MasterVolume"), Is.EqualTo(-6.02f).Within(0.2f));
        slider.normalizedValue = 0f;
        Assert.That(MixerDecibels("MasterVolume"), Is.LessThanOrEqualTo(-80f + Tolerance), "Slider at 0 is not silent.");
        slider.normalizedValue = 0.1f;

        options.Close();
        yield return null;

        Assert.That(new PlayerPrefsSettingsStore().TryLoad(out var saved), Is.True);
        Assert.That(saved.masterVolume, Is.EqualTo(0.1f).Within(0.01f), "Closing the menu did not save the new volume.");

        yield return new ExitPlayMode();
    }
}
