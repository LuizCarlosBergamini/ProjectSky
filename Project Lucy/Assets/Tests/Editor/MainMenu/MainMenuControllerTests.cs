using NUnit.Framework;
using UnityEngine;

/// <summary>
/// MainMenuController and SaveSystem behaviour that needs no scene.
/// </summary>
public class MainMenuControllerTests
{
    [Test]
    public void SaveSystem_HasNoSaveYet()
    {
        Assert.That(SaveSystem.HasSave(), Is.False, "The placeholder should report no save until the save system exists.");
    }

    [Test]
    public void VersionText_ShowsTheRealApplicationVersion()
    {
        Assert.That(Application.version, Is.Not.Empty);
        Assert.That(MainMenuController.VersionText, Does.Contain(Application.version));
    }

    [Test]
    public void CanContinue_FollowsSaveSystem()
    {
        var go = new GameObject("MainMenuControllerTest");
        try
        {
            var controller = go.AddComponent<MainMenuController>();
            Assert.That(controller.CanContinue, Is.EqualTo(SaveSystem.HasSave()));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
