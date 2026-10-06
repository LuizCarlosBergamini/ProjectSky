using System.Collections;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// Opens the MainMenu scene, enters Play Mode and checks the menu as the player would meet it.
/// Each test opens the scene and enters Play Mode itself, so nothing carries over between tests.
/// </summary>
public class MainMenuSceneTests
{
    private static IEnumerator EnterMainMenu()
    {
        EditorSceneManager.OpenScene(MenuTestPaths.MainMenuScene, OpenSceneMode.Single);
        yield return new EnterPlayMode();
        // Let Awake/Start run and the layout groups and EventSystem settle.
        yield return null;
        yield return null;
        Canvas.ForceUpdateCanvases();
    }

    private static Button FindButton(string name)
    {
        var go = MenuTestUtils.FindInScenes(name);
        Assert.That(go, Is.Not.Null, $"No GameObject named {name} in the MainMenu scene.");
        var button = go.GetComponent<Button>();
        Assert.That(button, Is.Not.Null, $"{name} has no Button component.");
        return button;
    }

    [UnityTest]
    public IEnumerator Scene_HasMainMenuControllerAndEventSystem()
    {
        yield return EnterMainMenu();

        Assert.That(Object.FindAnyObjectByType<MainMenuController>(), Is.Not.Null);
        Assert.That(EventSystem.current, Is.Not.Null);

        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Continue_IsVisibleButDisabled_WhenThereIsNoSave()
    {
        yield return EnterMainMenu();

        Assert.That(SaveSystem.HasSave(), Is.False, "The placeholder save check should report no save yet.");
        var continueButton = FindButton(MenuTestPaths.ContinueButtonName);
        Assert.That(continueButton.gameObject.activeInHierarchy, Is.True, "Continue should be visible, not hidden.");
        Assert.That(continueButton.interactable, Is.False, "Continue should be non-interactable without a save.");
        Assert.That(continueButton.IsInteractable(), Is.False);

        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Continue_LooksGreyedOut_WhenDisabled()
    {
        yield return EnterMainMenu();

        var continueButton = FindButton(MenuTestPaths.ContinueButtonName);
        Assert.That(continueButton.transition, Is.Not.EqualTo(Selectable.Transition.None),
            "A disabled button with no transition looks identical to an enabled one.");
        if (continueButton.transition == Selectable.Transition.ColorTint)
            Assert.That(continueButton.colors.disabledColor, Is.Not.EqualTo(continueButton.colors.normalColor));

        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Buttons_AreStackedInOrderByAVerticalLayoutGroup()
    {
        yield return EnterMainMenu();

        var newGame = FindButton(MenuTestPaths.NewGameButtonName).transform;
        var continueButton = FindButton(MenuTestPaths.ContinueButtonName).transform;
        var options = FindButton(MenuTestPaths.OptionsButtonName).transform;

        Assert.That(continueButton.parent, Is.SameAs(newGame.parent));
        Assert.That(options.parent, Is.SameAs(newGame.parent));
        Assert.That(newGame.parent.GetComponent<VerticalLayoutGroup>(), Is.Not.Null,
            "The buttons' parent should use a VerticalLayoutGroup so new buttons need no repositioning.");
        Assert.That(newGame.GetSiblingIndex(), Is.LessThan(continueButton.GetSiblingIndex()));
        Assert.That(continueButton.GetSiblingIndex(), Is.LessThan(options.GetSiblingIndex()));

        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Buttons_HaveHoverPressedAndSelectedStates()
    {
        yield return EnterMainMenu();

        foreach (var name in new[] { MenuTestPaths.NewGameButtonName, MenuTestPaths.ContinueButtonName, MenuTestPaths.OptionsButtonName })
        {
            var button = FindButton(name);
            Assert.That(button.transition, Is.Not.EqualTo(Selectable.Transition.None), $"{name} has no visual states.");
            Assert.That(button.navigation.mode, Is.Not.EqualTo(Navigation.Mode.None), $"{name} can't be reached by keyboard or gamepad.");
            if (button.transition == Selectable.Transition.ColorTint)
            {
                var c = button.colors;
                Assert.That(c.highlightedColor, Is.Not.EqualTo(c.normalColor), $"{name}: hover looks like normal.");
                Assert.That(c.pressedColor, Is.Not.EqualTo(c.normalColor), $"{name}: pressed looks like normal.");
                Assert.That(c.selectedColor, Is.Not.EqualTo(c.normalColor), $"{name}: selected looks like normal.");
            }
        }

        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator DefaultSelection_IsAnInteractableButton()
    {
        yield return EnterMainMenu();

        var selected = EventSystem.current.currentSelectedGameObject;
        Assert.That(selected, Is.Not.Null, "Nothing is selected, so keyboard and gamepad navigation has no starting point.");
        var selectable = selected.GetComponent<Selectable>();
        Assert.That(selectable, Is.Not.Null);
        Assert.That(selectable.IsInteractable(), Is.True, "The default selection should not be the disabled Continue button.");

        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Background_IsDecorativeAndAtTheBack()
    {
        yield return EnterMainMenu();

        var background = MenuTestUtils.FindInScenes(MenuTestPaths.BackgroundImageName);
        Assert.That(background, Is.Not.Null);
        var graphic = background.GetComponent<Graphic>();
        Assert.That(graphic, Is.InstanceOf<Image>().Or.InstanceOf<RawImage>());
        Assert.That(graphic.raycastTarget, Is.False, "The background must never block raycasts.");
        Assert.That(background.GetComponentsInChildren<Selectable>(true), Is.Empty,
            "Nothing interactive should live under the background.");

        // At the back of the canvas: drawn first among its siblings, and every ancestor drawn first too, up to the canvas.
        var canvas = background.GetComponentInParent<Canvas>().rootCanvas.transform;
        for (var t = background.transform; t != canvas; t = t.parent)
            Assert.That(t.GetSiblingIndex(), Is.EqualTo(0), $"{t.name} is not the first child of {t.parent.name}.");

        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Background_DoesNotBlockRaycastsToButtons()
    {
        yield return EnterMainMenu();

        foreach (var name in new[] { MenuTestPaths.NewGameButtonName, MenuTestPaths.OptionsButtonName })
        {
            var button = FindButton(name);
            var hits = MenuTestUtils.RaycastAt(MenuTestUtils.ScreenCenter((RectTransform)button.transform));

            Assert.That(hits, Is.Not.Empty, $"A click on {name} hits nothing.");
            Assert.That(hits.Any(h => h.gameObject.name == MenuTestPaths.BackgroundImageName), Is.False,
                "The background shows up in raycasts.");
            Assert.That(hits[0].gameObject.transform.IsChildOf(button.transform), Is.True,
                $"A click on {name} lands on {hits[0].gameObject.name} instead.");
        }

        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Background_CoversTheScreenWithoutStretching()
    {
        yield return EnterMainMenu();

        var background = MenuTestUtils.FindInScenes(MenuTestPaths.BackgroundImageName);
        var rect = MenuTestUtils.ScreenRect((RectTransform)background.transform);
        const float pixel = 1f;

        // Cover: the image reaches every screen edge.
        Assert.That(rect.xMin, Is.LessThanOrEqualTo(pixel), "Gap on the left.");
        Assert.That(rect.yMin, Is.LessThanOrEqualTo(pixel), "Gap at the bottom.");
        Assert.That(rect.xMax, Is.GreaterThanOrEqualTo(Screen.width - pixel), "Gap on the right.");
        Assert.That(rect.yMax, Is.GreaterThanOrEqualTo(Screen.height - pixel), "Gap at the top.");

        // No stretching: the rect (or the RawImage's cropped uvRect) keeps the art's aspect ratio.
        // Image.preserveAspect alone would fail the edge checks above, because it letterboxes.
        var texture = background.GetComponent<Image>() is Image image && image.sprite != null
            ? new Vector2(image.sprite.rect.width, image.sprite.rect.height)
            : background.GetComponent<RawImage>() is RawImage raw && raw.texture != null
                ? new Vector2(raw.texture.width * raw.uvRect.width, raw.texture.height * raw.uvRect.height)
                : Vector2.zero;
        Assert.That(texture, Is.Not.EqualTo(Vector2.zero), "The background has no placeholder art assigned.");

        var artAspect = texture.x / texture.y;
        Assert.That(rect.width / rect.height, Is.EqualTo(artAspect).Within(0.01f),
            "The background rect does not match the art's aspect ratio, so the art is stretched.");

        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator VersionLabel_ShowsTheApplicationVersion()
    {
        yield return EnterMainMenu();

        Assert.That(Application.version, Is.Not.Empty);
        var labels = Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Select(t => t.text)
            .Concat(Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Select(t => t.text));
        Assert.That(labels.Any(text => text.Contains(Application.version)), Is.True,
            $"No label shows the version \"{Application.version}\".");

        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator OptionsButton_OpensTheOptionsMenuOverTheMainMenu()
    {
        yield return EnterMainMenu();

        FindButton(MenuTestPaths.OptionsButtonName).onClick.Invoke();
        yield return null;

        var options = Object.FindAnyObjectByType<OptionsMenuController>(FindObjectsInactive.Include);
        Assert.That(options, Is.Not.Null, "Clicking Options produced no OptionsMenuController.");
        Assert.That(options.IsOpen, Is.True);

        var optionsCanvas = options.GetComponentInParent<Canvas>(true).rootCanvas;
        var menuCanvas = FindButton(MenuTestPaths.NewGameButtonName).GetComponentInParent<Canvas>().rootCanvas;
        Assert.That(optionsCanvas.sortingOrder, Is.GreaterThan(menuCanvas.sortingOrder),
            "The Options menu should render above the main menu.");

        yield return new ExitPlayMode();
    }
}
