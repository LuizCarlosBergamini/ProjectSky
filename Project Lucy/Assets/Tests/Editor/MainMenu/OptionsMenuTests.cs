using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// The Options menu is a self-contained prefab: these tests drop it into an empty scene, with no main menu
/// loaded, and drive it only through its public API.
/// </summary>
public class OptionsMenuTests
{
    [Test]
    public void Prefab_Exists_AndHasItsOwnCanvas()
    {
        MenuTestPaths.RequireOptionsPrefab();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuTestPaths.OptionsMenuPrefab);
        Assert.That(prefab, Is.Not.Null, $"No prefab at {MenuTestPaths.OptionsMenuPrefab}.");
        Assert.That(prefab.GetComponentInChildren<OptionsMenuController>(true), Is.Not.Null);
        Assert.That(prefab.GetComponentInChildren<Canvas>(true), Is.Not.Null, "The prefab should carry its own canvas.");
    }

    [Test]
    public void Prefab_DoesNotReferenceAnySceneSpecificType()
    {
        MenuTestPaths.RequireOptionsPrefab();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuTestPaths.OptionsMenuPrefab);
        Assert.That(prefab.GetComponentsInChildren<MainMenuController>(true), Is.Empty);

        // Every object the prefab references must be an asset or part of the prefab itself.
        foreach (var dependency in EditorUtility.CollectDependencies(new Object[] { prefab }))
        {
            if (dependency == null) continue;
            Assert.That(EditorUtility.IsPersistent(dependency), Is.True,
                $"The prefab references {dependency.name}, which is not an asset.");
        }
    }

    [Test]
    public void Prefab_RendersAboveOrdinaryCanvases()
    {
        MenuTestPaths.RequireOptionsPrefab();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuTestPaths.OptionsMenuPrefab);
        var canvas = prefab.GetComponentInChildren<Canvas>(true);
        Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay).Or.EqualTo(RenderMode.ScreenSpaceCamera));
        Assert.That(canvas.sortingOrder, Is.GreaterThan(0),
            "The sort order should be set explicitly so the menu draws above whatever opened it.");
    }

    [Test]
    public void Prefab_HasBackButtonAndVolumeSliders()
    {
        MenuTestPaths.RequireOptionsPrefab();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuTestPaths.OptionsMenuPrefab);
        Assert.That(prefab.GetComponentsInChildren<Slider>(true).Length, Is.GreaterThanOrEqualTo(1), "No volume slider.");
        Assert.That(prefab.GetComponentsInChildren<Button>(true), Is.Not.Empty, "No Back button.");
    }

    private static void OpenEmptyScene()
    {
        MenuTestPaths.RequireOptionsPrefab();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    private static IEnumerator SpawnOptions()
    {
        MenuTestUtils.AssertInPlayMode();

        var eventSystem = new GameObject("EventSystem", typeof(EventSystem),
            typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
        Assert.That(eventSystem, Is.Not.Null);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuTestPaths.OptionsMenuPrefab);
        Object.Instantiate(prefab);
        yield return null;
    }

    private static OptionsMenuController Options() =>
        Object.FindAnyObjectByType<OptionsMenuController>(FindObjectsInactive.Include);

    [UnityTest]
    public IEnumerator StartsClosed_InAnEmptyScene()
    {
        OpenEmptyScene();
        yield return new EnterPlayMode();
        yield return SpawnOptions();

        Assert.That(Object.FindAnyObjectByType<MainMenuController>(FindObjectsInactive.Include), Is.Null);
        Assert.That(Options(), Is.Not.Null);
        Assert.That(Options().IsOpen, Is.False);
        LogAssert.NoUnexpectedReceived();

        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Open_ShowsTheMenu_WithoutTheMainMenuLoaded()
    {
        OpenEmptyScene();
        yield return new EnterPlayMode();
        yield return SpawnOptions();

        Options().Open();
        yield return null;

        Assert.That(Options().IsOpen, Is.True);
        Assert.That(Options().GetComponentsInChildren<Slider>(false), Is.Not.Empty, "The sliders are not visible after Open().");
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.Not.Null,
            "Opening should select something so keyboard and gamepad can navigate.");
        LogAssert.NoUnexpectedReceived();

        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Close_HidesTheMenu_AndFiresTheCallbackOnce()
    {
        OpenEmptyScene();
        yield return new EnterPlayMode();
        yield return SpawnOptions();

        var closedCount = 0;
        var closedEventCount = 0;
        Options().Closed += () => closedEventCount++;
        Options().Open(() => closedCount++);
        yield return null;
        Options().Close();
        yield return null;

        Assert.That(Options().IsOpen, Is.False);
        Assert.That(closedCount, Is.EqualTo(1));
        Assert.That(closedEventCount, Is.EqualTo(1), "The Closed event did not fire exactly once.");
        Assert.That(Options().GetComponentsInChildren<Slider>(false), Is.Empty, "The sliders are still visible after Close().");

        // Closing again is harmless and does not re-fire the callback.
        Options().Close();
        yield return null;
        Assert.That(closedCount, Is.EqualTo(1));
        Assert.That(closedEventCount, Is.EqualTo(1));

        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator BackButton_ClosesTheMenu()
    {
        OpenEmptyScene();
        yield return new EnterPlayMode();
        yield return SpawnOptions();

        var closed = false;
        Options().Open(() => closed = true);
        yield return null;
        yield return null; // past the opening-frame guard

        var back = FindBackButton(Options());
        Assert.That(back, Is.Not.Null, "No Back button found (looked for a Button whose name contains \"Back\" or \"Voltar\").");
        back.onClick.Invoke();
        yield return null;

        Assert.That(closed, Is.True);
        Assert.That(Options().IsOpen, Is.False);

        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator CanBeReopened_AfterClosing()
    {
        OpenEmptyScene();
        yield return new EnterPlayMode();
        yield return SpawnOptions();

        Options().Open();
        yield return null;
        Options().Close();
        yield return null;

        var closedAgain = false;
        Options().Open(() => closedAgain = true);
        yield return null;
        Assert.That(Options().IsOpen, Is.True);
        Options().Close();
        yield return null;
        Assert.That(closedAgain, Is.True, "The second caller's callback did not fire.");

        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator CancelInput_ClosesTheMenu()
    {
        OpenEmptyScene();
        yield return new EnterPlayMode();
        yield return SpawnOptions();

        var closed = false;
        Options().Open(() => closed = true);
        yield return null;
        yield return null; // past the opening-frame guard

        // A virtual keyboard pressing Escape, which is bound to UI/Cancel and InGame/Pause.
        var keyboard = InputSystem.AddDevice<Keyboard>("TestKeyboard");
        try
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
            InputSystem.Update();
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            yield return null;

            Assert.That(closed, Is.True, "Escape did not close the Options menu.");
            Assert.That(Options().IsOpen, Is.False);
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard);
        }

        yield return new ExitPlayMode();
    }

    private static Button FindBackButton(OptionsMenuController options)
    {
        foreach (var button in options.GetComponentsInChildren<Button>(true))
        {
            var name = button.name.ToLowerInvariant();
            if (name.Contains("back") || name.Contains("voltar")) return button;
        }
        return null;
    }
}
