using System.Linq;
using NUnit.Framework;
using UnityEditor;

/// <summary>
/// The main menu must be the first scene the game loads, and the scenes it can lead to must still be in the build.
/// </summary>
public class MainMenuBuildSettingsTests
{
    [Test]
    public void MainMenu_IsEnabledAtBuildIndexZero()
    {
        MenuTestPaths.RequireMainMenuScene();
        var enabledScenes = EditorBuildSettings.scenes.Where(s => s.enabled).ToArray();

        Assert.That(enabledScenes, Is.Not.Empty, "No scenes are enabled in Build Settings.");
        Assert.That(enabledScenes[0].path, Is.EqualTo(MainMenuBuilder.ScenePath),
            "Build index 0 is the first enabled scene, and it should be the main menu.");
    }

    [Test]
    public void MainMenu_SceneAssetExists()
    {
        MenuTestPaths.RequireMainMenuScene();
        Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuBuilder.ScenePath), Is.Not.Null);
    }

    [Test]
    public void MainMenu_IsListedOnlyOnce()
    {
        MenuTestPaths.RequireMainMenuScene();
        var count = EditorBuildSettings.scenes.Count(s => s.path == MainMenuBuilder.ScenePath);
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public void EveryEnabledScene_PointsToAnExistingAsset()
    {
        foreach (var scene in EditorBuildSettings.scenes.Where(s => s.enabled))
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path), Is.Not.Null,
                $"Build Settings lists {scene.path}, which does not exist.");
    }
}
