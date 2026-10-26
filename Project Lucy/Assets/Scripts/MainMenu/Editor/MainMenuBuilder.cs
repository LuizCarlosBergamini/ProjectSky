using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Scene = UnityEngine.SceneManagement.Scene;

/// <summary>
/// Generates the MainMenu scene so nothing has to be wired by hand: placeholder art, the canvas hierarchy, the
/// MainMenuController references, the EventSystem, the Options prefab link, the Credits screen, and registers the scene as build index 0.
/// The LevelManager (fade) is not placed here: it creates itself from Resources before the first scene loads.
/// Safe to run again: placeholder sprites are only written when missing (final art dropped over them is kept) and
/// an existing scene is only re-linked and re-registered. "Rebuild Main Menu Scene" is the only command that
/// recreates the scene, and it asks first.
/// </summary>
public static class MainMenuBuilder
{
    public const string ScenePath = "Assets/Scenes/MainMenu.unity";

    private const string SpriteFolder = "Assets/Sprites/UI/MainMenu";
    private const string HubScenePath = "Assets/Scenes/Hub.unity";
    private const string FontPath = "Assets/Renan/Fonts/Jersey10-Regular SDF 1.asset";

    // Input System's DefaultInputActions, the asset the scenes' EventSystem uses (UI/Cancel = Esc, gamepad B).
    private const string DefaultInputActionsGuid = "ca9f5fa95ffab41fb9a615ab714db018";

    private static readonly string BackgroundSpritePath = $"{SpriteFolder}/MainMenu_Background_Placeholder.png";
    private static readonly string LogoSpritePath = $"{SpriteFolder}/MainMenu_Logo_Placeholder.png";
    private static readonly string StarSpritePath = $"{SpriteFolder}/MainMenu_Star.png";
    private static readonly string ButtonSpritePath = $"{SpriteFolder}/MainMenu_Button.png";

    // Same palette as the boss selector and upgrade screens.
    private static readonly Color TextColor = new(0.92f, 0.92f, 0.92f, 1f);
    private static readonly Color MutedTextColor = new(0.6f, 0.6f, 0.63f, 1f);
    private static readonly Color AccentColor = new(0.95f, 0.72f, 0.3f, 1f);
    private static readonly Color ButtonNormal = new(0.07f, 0.07f, 0.08f, 0.92f);
    private static readonly Color ButtonHighlighted = new(0.18f, 0.18f, 0.2f, 1f);
    private static readonly Color ButtonSelected = new(0.36f, 0.27f, 0.12f, 1f);
    private static readonly Color ButtonPressed = new(0.95f, 0.72f, 0.3f, 1f);
    private static readonly Color ButtonDisabled = new(0.07f, 0.07f, 0.08f, 0.45f);
    private static readonly Color OverlayColor = new(0f, 0f, 0f, 0.7f);
    private static readonly Color PanelColor = new(0.035f, 0.035f, 0.04f, 0.98f);

    private const int UILayer = 5;
    private const float ReferenceWidth = 1280f;
    private const float ReferenceHeight = 720f;

    private static TMP_FontAsset _font;

    [MenuItem("Tools/Lucy/Build Main Menu")]
    public static void Build()
    {
        Run(recreateScene: false);
    }

    [MenuItem("Tools/Lucy/Rebuild Main Menu Scene")]
    public static void Rebuild()
    {
        if (File.Exists(ToFullPath(ScenePath)) && !EditorUtility.DisplayDialog("Recriar menu principal",
                $"{ScenePath} sera apagada e gerada de novo. Mudancas feitas a mao nela serao perdidas.", "Recriar", "Cancelar"))
        {
            return;
        }

        Run(recreateScene: true);
    }

    private static void Run(bool recreateScene)
    {
        // The menu scene gets opened, so every open scene has to be saved (or knowingly discarded) first.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.LogWarning("[Menu] Cancelado: salve ou descarte as cenas abertas para gerar o menu.");
            return;
        }

        try
        {
            EnsureFolder(SpriteFolder);
            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

            Sprite background = GenerateSprite(BackgroundSpritePath, 1920, 1080, BackgroundPixel);
            Sprite logo = GenerateSprite(LogoSpritePath, 1024, 384, LogoPixel);
            Sprite button = GenerateSprite(ButtonSpritePath, 64, 64, ButtonPixel, sliceBorder: 20);
            Sprite star = GenerateSprite(StarSpritePath, 256, 256, StarPixel);

            // Built before the scene opens: building a prefab may create temporary objects in the active scene.
            OptionsMenuController optionsPrefab = BuildOptionsPrefab();

            bool exists = File.Exists(ToFullPath(ScenePath));
            Scene scene;
            if (exists && !recreateScene)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            else
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                BuildScene(scene, background, logo, star, button);
            }

            LinkScene(scene, optionsPrefab);
            EnsureCredits(scene, button);
            EnsureConfirmDialog(scene, button);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            RegisterSceneFirst();
            LogBuildIndices();

            Debug.Log(exists && !recreateScene
                ? "[Menu] MainMenu ja existia: referencias e Build Settings atualizados."
                : "[Menu] MainMenu gerada. Troque a arte em BackgroundImage / LogoImage (ou nos campos do MainMenuController).");
        }
        finally
        {
            _font = null;
        }
    }

    private static OptionsMenuController BuildOptionsPrefab()
    {
        GameObject prefab = OptionsMenuBuilder.EnsurePrefab();
        OptionsMenuController options = prefab != null ? prefab.GetComponent<OptionsMenuController>() : null;
        if (options == null) Debug.LogWarning("[Menu] Prefab de opcoes nao encontrado: o botao Opcoes nao vai abrir nada.");
        return options;
    }

    #region Scene

    private static void BuildScene(Scene scene, Sprite background, Sprite logo, Sprite star, Sprite buttonSprite)
    {
        // Camera: the canvas is Screen Space Overlay, the camera only clears the screen behind it.
        GameObject cameraObject = new("Main Camera") { tag = "MainCamera" };
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        cameraObject.AddComponent<AudioListener>();
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);

        // Same input module setup as the Hub: Input System UI module with the package's default UI actions
        // (Navigate, Submit, Cancel = Esc / gamepad B).
        GameObject eventSystemObject = new("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        InputSystemUIInputModule module = eventSystemObject.AddComponent<InputSystemUIInputModule>();
        module.AssignDefaultActions();
        eventSystemObject.AddComponent<SingleEventSystem>();

        // Canvas
        GameObject canvasObject = new("MainMenuCanvas", typeof(RectTransform)) { layer = UILayer };
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        scaler.matchWidthOrHeight = 0f;
        canvasObject.AddComponent<GraphicRaycaster>();
        MainMenuController controller = canvasObject.AddComponent<MainMenuController>();
        RectTransform canvasRect = (RectTransform)canvasObject.transform;

        // Back of the hierarchy: the background, decorative, covering the whole canvas.
        Image backgroundImage = CreateImage("BackgroundImage", canvasRect, background, Color.white);
        backgroundImage.preserveAspect = false;
        backgroundImage.gameObject.AddComponent<CoverImage>();

        // Everything else lives in a padded, anchored content area so it survives any art or resolution.
        RectTransform content = CreateRect("Content", canvasRect);
        Stretch(content, 40f, 40f, 32f, 24f);

        Image logoImage = CreateImage("LogoImage", content, logo, Color.white);
        logoImage.preserveAspect = true;
        SetAnchors(logoImage.rectTransform, new Vector2(0.2f, 0.62f), new Vector2(0.8f, 1f));
        // Hidden until the final logo art arrives: the game's name is written as text in the same area meanwhile.
        logoImage.gameObject.SetActive(false);

        // The star from the placeholder logo, a little bigger and lighter than "STARS", behind the title.
        Image starImage = CreateImage("StarImage", content, star, new Color(1f, 0.835f, 0.5f, 1f));
        starImage.preserveAspect = true;
        RectTransform starRect = starImage.rectTransform;
        starRect.anchorMin = starRect.anchorMax = new Vector2(0.5f, 0.81f);
        starRect.pivot = new Vector2(0.5f, 0.5f);
        starRect.sizeDelta = new Vector2(190f, 190f);
        starRect.anchoredPosition = Vector2.zero;

        TextMeshProUGUI titleText = CreateText("TitleText", content,
            "LUCY FROM THE\n<size=160%><color=#F2B84D>STARS</color></size>", 64f, TextColor, TextAlignmentOptions.Center);
        SetAnchors(titleText.rectTransform, new Vector2(0.2f, 0.62f), new Vector2(0.8f, 1f));
        titleText.richText = true;
        titleText.enableAutoSizing = true;
        titleText.fontSizeMin = 24f;
        titleText.fontSizeMax = 64f;

        RectTransform stack = CreateRect("ButtonStack", content);
        stack.anchorMin = new Vector2(0.5f, 0.06f);
        stack.anchorMax = new Vector2(0.5f, 0.58f);
        stack.pivot = new Vector2(0.5f, 1f);
        stack.sizeDelta = new Vector2(380f, 0f);
        stack.anchoredPosition = Vector2.zero;
        VerticalLayoutGroup layout = stack.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.spacing = 14f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        CanvasGroup buttonsGroup = stack.gameObject.AddComponent<CanvasGroup>();

        Button newGame = CreateMenuButton("NewGameButton", stack, buttonSprite, "NOVO JOGO");
        Button continueButton = CreateMenuButton("ContinueButton", stack, buttonSprite, "CONTINUAR");
        Button options = CreateMenuButton("OptionsButton", stack, buttonSprite, "OPÇÕES");
        Button credits = CreateMenuButton("CreditsButton", stack, buttonSprite, "CRÉDITOS");
        Button quit = CreateMenuButton("QuitButton", stack, buttonSprite, "SAIR");

        TextMeshProUGUI version = CreateText("VersionLabel", content, "v" + Application.version, 22f, MutedTextColor,
            TextAlignmentOptions.BottomRight);
        RectTransform versionRect = version.rectTransform;
        versionRect.anchorMin = versionRect.anchorMax = versionRect.pivot = new Vector2(1f, 0f);
        versionRect.sizeDelta = new Vector2(240f, 30f);
        versionRect.anchoredPosition = Vector2.zero;

        SetRefs(controller,
            ("_newGameButton", newGame),
            ("_continueButton", continueButton),
            ("_optionsButton", options),
            ("_creditsButton", credits),
            ("_quitButton", quit),
            ("_buttonsGroup", buttonsGroup),
            ("_backgroundImage", backgroundImage),
            ("_logoImage", logoImage),
            ("_versionLabel", version));

        SceneAsset hub = AssetDatabase.LoadAssetAtPath<SceneAsset>(HubScenePath);
        SerializedObject so = new(controller);
        so.FindProperty("_newGameScene.sceneAsset").objectReferenceValue = hub;
        so.FindProperty("_newGameScene.scenePath").stringValue = hub != null ? HubScenePath : "";
        so.ApplyModifiedPropertiesWithoutUndo();
        if (hub == null) Debug.LogWarning($"[Menu] {HubScenePath} nao encontrada: escolha a cena do Novo Jogo no MainMenuController.");
    }

    /// <summary>Links what may have been created after the scene: the options prefab.</summary>
    private static void LinkScene(Scene scene, OptionsMenuController optionsPrefab)
    {
        MainMenuController controller = FindInScene<MainMenuController>(scene);
        if (controller == null)
        {
            Debug.LogWarning("[Menu] MainMenuController nao encontrado na cena: use Tools > Lucy > Rebuild Main Menu Scene.");
            return;
        }

        SerializedObject so = new(controller);
        SerializedProperty options = so.FindProperty("_optionsMenuPrefab");
        if (options.objectReferenceValue == null && optionsPrefab != null)
        {
            options.objectReferenceValue = optionsPrefab;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>
    /// Adds the New Game confirmation ("a save already exists") to a scene that has none, as the last child of the
    /// menu canvas so it draws over everything, and links it to the MainMenuController. Existing dialogs are kept.
    /// </summary>
    private static void EnsureConfirmDialog(Scene scene, Sprite buttonSprite)
    {
        MainMenuController controller = FindInScene<MainMenuController>(scene);
        if (controller == null) return;

        SerializedObject so = new(controller);
        SerializedProperty dialogProperty = so.FindProperty("_confirmDialog");
        if (dialogProperty.objectReferenceValue != null) return;

        ConfirmDialog dialog = FindInScene<ConfirmDialog>(scene);
        if (dialog == null) dialog = BuildConfirmDialog(controller.transform, buttonSprite);

        dialogProperty.objectReferenceValue = dialog;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Adds what the credits need to a scene that lacks it: the Credits button (between Options and Quit) and the
    /// credits screen, then links both to the MainMenuController. Whatever already exists is kept.
    /// </summary>
    private static void EnsureCredits(Scene scene, Sprite buttonSprite)
    {
        MainMenuController controller = FindInScene<MainMenuController>(scene);
        if (controller == null) return;

        SerializedObject so = new(controller);
        SerializedProperty buttonProperty = so.FindProperty("_creditsButton");
        SerializedProperty screenProperty = so.FindProperty("_creditsScreen");

        if (buttonProperty.objectReferenceValue == null)
        {
            Button credits = FindCreditsButton(controller, so) ?? CreateCreditsButton(controller, so, buttonSprite);
            buttonProperty.objectReferenceValue = credits;
        }

        if (screenProperty.objectReferenceValue == null)
        {
            CreditsScreen screen = FindInScene<CreditsScreen>(scene);
            if (screen == null) screen = BuildCreditsScreen(controller.transform, buttonSprite);
            screenProperty.objectReferenceValue = screen;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Button FindCreditsButton(MainMenuController controller, SerializedObject so)
    {
        Transform stack = FindButtonStack(controller, so);
        Transform existing = stack != null ? stack.Find("CreditsButton") : null;
        return existing != null ? existing.GetComponent<Button>() : null;
    }

    private static Button CreateCreditsButton(MainMenuController controller, SerializedObject so, Sprite buttonSprite)
    {
        Transform stack = FindButtonStack(controller, so);
        if (stack == null)
        {
            Debug.LogWarning("[Menu] ButtonStack nao encontrado: o botao Creditos nao foi criado. Use Rebuild Main Menu Scene.");
            return null;
        }

        Button credits = CreateMenuButton("CreditsButton", stack, buttonSprite, "CRÉDITOS");

        // Right below Options, so Quit stays last.
        Object options = so.FindProperty("_optionsButton").objectReferenceValue;
        Transform anchor = options is Button optionsButton && optionsButton.transform.parent == stack
            ? optionsButton.transform
            : null;
        if (anchor != null) credits.transform.SetSiblingIndex(anchor.GetSiblingIndex() + 1);
        return credits;
    }

    /// <summary>The parent of the existing menu buttons; falls back to the object named ButtonStack.</summary>
    private static Transform FindButtonStack(MainMenuController controller, SerializedObject so)
    {
        foreach (string field in new[] { "_optionsButton", "_newGameButton", "_quitButton" })
        {
            if (so.FindProperty(field).objectReferenceValue is Button button && button.transform.parent != null)
            {
                return button.transform.parent;
            }
        }

        return controller.transform
            .GetComponentsInChildren<RectTransform>(true)
            .FirstOrDefault(rect => rect.name == "ButtonStack");
    }

    private static CreditsScreen BuildCreditsScreen(Transform canvas, Sprite buttonSprite)
    {
        RectTransform screenRect = CreateRect("CreditsScreen", canvas);
        Stretch(screenRect);

        // Over the menu, under the New Game question if the scene already has one.
        ConfirmDialog dialog = canvas.GetComponentInChildren<ConfirmDialog>(true);
        if (dialog != null && dialog.transform.parent == canvas) screenRect.SetSiblingIndex(dialog.transform.GetSiblingIndex());
        else screenRect.SetAsLastSibling();

        CreditsScreen screen = screenRect.gameObject.AddComponent<CreditsScreen>();

        // Dims the menu and swallows clicks so nothing behind the credits can be pressed.
        Image overlay = CreateImage("Root", screenRect, null, OverlayColor, raycast: true);
        Stretch(overlay.rectTransform);

        Image panel = CreateImage("Panel", overlay.rectTransform, buttonSprite, PanelColor, Image.Type.Sliced);
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(720f, 620f);
        panelRect.anchoredPosition = Vector2.zero;

        // Same title as the menu, smaller.
        TextMeshProUGUI title = CreateText("TitleText", panelRect,
            "LUCY FROM THE\n<size=160%><color=#F2B84D>STARS</color></size>", 44f, TextColor, TextAlignmentOptions.Center);
        title.richText = true;
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.sizeDelta = new Vector2(-72f, 150f);
        titleRect.anchoredPosition = new Vector2(0f, -28f);

        TextMeshProUGUI heading = CreateText("HeadingText", panelRect, "CRÉDITOS", 30f, MutedTextColor, TextAlignmentOptions.Center);
        RectTransform headingRect = heading.rectTransform;
        headingRect.anchorMin = new Vector2(0f, 1f);
        headingRect.anchorMax = new Vector2(1f, 1f);
        headingRect.pivot = new Vector2(0.5f, 1f);
        headingRect.sizeDelta = new Vector2(-72f, 40f);
        headingRect.anchoredPosition = new Vector2(0f, -186f);

        // Filled by CreditsScreen from its Inspector list.
        TextMeshProUGUI entries = CreateText("EntriesText", panelRect, "", 34f, TextColor, TextAlignmentOptions.Top);
        entries.richText = true;
        entries.textWrappingMode = TextWrappingModes.Normal;
        entries.enableAutoSizing = true;
        entries.fontSizeMin = 18f;
        entries.fontSizeMax = 34f;
        Stretch(entries.rectTransform, 36f, 36f, 240f, 110f);

        Button back = CreateMenuButton("BackButton", panelRect, buttonSprite, "VOLTAR");
        back.navigation = new Navigation { mode = Navigation.Mode.None };
        RectTransform backRect = (RectTransform)back.transform;
        backRect.anchorMin = backRect.anchorMax = backRect.pivot = new Vector2(0.5f, 0f);
        backRect.sizeDelta = new Vector2(280f, 58f);
        backRect.anchoredPosition = new Vector2(0f, 32f);

        InputActionReference cancelAction = FindCancelAction();
        if (cancelAction == null) Debug.LogWarning("[Menu] UI/Cancel nao encontrado: Esc/B nao vao fechar os creditos.");

        SetRefs(screen,
            ("_root", overlay.gameObject),
            ("_entriesLabel", entries),
            ("_backButton", back),
            ("_cancelAction", cancelAction));

        // Fills the label now, so the list already shows in the saved scene.
        screen.RefreshEntries();
        overlay.gameObject.SetActive(false);
        return screen;
    }

    private static ConfirmDialog BuildConfirmDialog(Transform canvas, Sprite buttonSprite)
    {
        RectTransform dialogRect = CreateRect("ConfirmDialog", canvas);
        Stretch(dialogRect);
        dialogRect.SetAsLastSibling();
        ConfirmDialog dialog = dialogRect.gameObject.AddComponent<ConfirmDialog>();

        // Dims the menu and swallows clicks so nothing behind the question can be pressed.
        Image overlay = CreateImage("Root", dialogRect, null, OverlayColor, raycast: true);
        Stretch(overlay.rectTransform);

        Image panel = CreateImage("Panel", overlay.rectTransform, buttonSprite, PanelColor, Image.Type.Sliced);
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(620f, 300f);
        panelRect.anchoredPosition = Vector2.zero;

        TextMeshProUGUI message = CreateText("Message", panelRect, "", 32f, TextColor, TextAlignmentOptions.Center);
        message.textWrappingMode = TextWrappingModes.Normal;
        Stretch(message.rectTransform, 36f, 36f, 32f, 110f);

        RectTransform row = CreateRect("Buttons", panelRect);
        row.anchorMin = new Vector2(0f, 0f);
        row.anchorMax = new Vector2(1f, 0f);
        row.pivot = new Vector2(0.5f, 0f);
        row.sizeDelta = new Vector2(-72f, 58f);
        row.anchoredPosition = new Vector2(0f, 32f);
        HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 20f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        Button cancel = CreateMenuButton("CancelButton", row, buttonSprite, "VOLTAR");
        Button confirm = CreateMenuButton("ConfirmButton", row, buttonSprite, "NOVO JOGO");
        cancel.navigation = new Navigation { mode = Navigation.Mode.Horizontal };
        confirm.navigation = new Navigation { mode = Navigation.Mode.Horizontal };

        InputActionReference cancelAction = FindCancelAction();
        if (cancelAction == null) Debug.LogWarning("[Menu] UI/Cancel nao encontrado: Esc/B nao vao fechar a confirmacao.");

        SetRefs(dialog,
            ("_root", overlay.gameObject),
            ("_messageLabel", message),
            ("_confirmButton", confirm),
            ("_confirmLabel", confirm.GetComponentInChildren<TextMeshProUGUI>()),
            ("_cancelButton", cancel),
            ("_cancelLabel", cancel.GetComponentInChildren<TextMeshProUGUI>()),
            ("_cancelAction", cancelAction));

        overlay.gameObject.SetActive(false);
        return dialog;
    }

    private static Button CreateMenuButton(string name, Transform parent, Sprite sprite, string label)
    {
        Image image = CreateImage(name, parent, sprite, Color.white, Image.Type.Sliced, raycast: true);
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;

        ColorBlock colors = button.colors;
        colors.normalColor = ButtonNormal;
        colors.highlightedColor = ButtonHighlighted;
        colors.pressedColor = ButtonPressed;
        colors.selectedColor = ButtonSelected;
        colors.disabledColor = ButtonDisabled;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        // Up/down only, skipping disabled buttons: a greyed-out Continue is never focused.
        button.navigation = new Navigation { mode = Navigation.Mode.Vertical };

        LayoutElement element = button.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = 58f;
        element.minHeight = 48f;

        TextMeshProUGUI text = CreateText("Label", image.transform, label, 34f, TextColor, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);

        // Dims the label with the button so a disabled Continue reads as greyed out, not just its frame.
        button.gameObject.AddComponent<SelectableLabelTint>();
        return button;
    }

    #endregion

    #region Build Settings

    /// <summary>Puts MainMenu first (index 0, enabled) and leaves every other entry in its order.</summary>
    private static void RegisterSceneFirst()
    {
        List<EditorBuildSettingsScene> scenes = new(EditorBuildSettings.scenes);
        int index = scenes.FindIndex(scene => scene.path == ScenePath);
        if (index == 0 && scenes[0].enabled) return;

        if (index >= 0) scenes.RemoveAt(index);
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void LogBuildIndices()
    {
        List<string> lines = new();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (!scene.enabled) continue;
            lines.Add($"{UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath(scene.path)}: {scene.path}");
        }

        Debug.Log("[Menu] Ordem do build:\n" + string.Join("\n", lines));
    }

    #endregion

    #region Sprites

    /// <summary>
    /// Writes a placeholder PNG and imports it as a UI sprite. Existing files are left alone, so final art saved
    /// over the placeholder (same path) is never overwritten.
    /// </summary>
    private static Sprite GenerateSprite(string assetPath, int width, int height, Func<int, int, int, int, Color32> pixel, int sliceBorder = 0)
    {
        if (!File.Exists(ToFullPath(assetPath)))
        {
            Texture2D texture = new(width, height, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    pixels[y * width + x] = pixel(x, y, width, height);
                }
            }

            texture.SetPixels32(pixels);
            File.WriteAllBytes(ToFullPath(assetPath), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.spritePixelsPerUnit = 100f;
            importer.spriteBorder = new Vector4(sliceBorder, sliceBorder, sliceBorder, sliceBorder);
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
    }

    /// <summary>
    /// Night-sky gradient with a grid and a centre ring: the grid shows the image is never stretched and the ring
    /// shows it stays centred while the edges are cropped.
    /// </summary>
    private static Color32 BackgroundPixel(int x, int y, int width, int height)
    {
        float t = y / (float)(height - 1);
        Color color = Color.Lerp(new Color(0.03f, 0.03f, 0.06f), new Color(0.12f, 0.1f, 0.22f), t);

        bool gridLine = x % 120 < 2 || y % 120 < 2;
        if (gridLine) color = Color.Lerp(color, Color.white, 0.08f);

        Vector2 centre = new(width * 0.5f, height * 0.5f);
        float ring = Mathf.Abs(Vector2.Distance(new Vector2(x, y), centre) - height * 0.3f);
        if (ring < 3f) color = Color.Lerp(color, AccentColor, 0.5f);

        bool border = x < 8 || y < 8 || x >= width - 8 || y >= height - 8;
        if (border) color = AccentColor;

        return color;
    }

    /// <summary>Rounded frame with a four-point star: marks the logo area until the real logo is dropped in.</summary>
    private static Color32 LogoPixel(int x, int y, int width, int height)
    {
        Vector2 p = new(x + 0.5f - width * 0.5f, y + 0.5f - height * 0.5f);
        float frame = RoundedBox(p, new Vector2(width * 0.5f - 4f, height * 0.5f - 4f), 32f);
        float frameAlpha = Mathf.Clamp01(0.5f - frame) * Mathf.Clamp01(0.5f + frame + 6f);

        // Star: |x|^0.5 + |y|^0.5 <= r^0.5 (astroid), centred.
        float r = height * 0.32f;
        float ax = Mathf.Abs(p.x) / r;
        float ay = Mathf.Abs(p.y) / r;
        float star = Mathf.Sqrt(ax) + Mathf.Sqrt(ay);
        float starAlpha = Mathf.Clamp01((1f - star) * r * 0.25f);

        float alpha = Mathf.Max(frameAlpha * 0.6f, starAlpha);
        Color color = Color.Lerp(TextColor, AccentColor, starAlpha);
        return new Color(color.r, color.g, color.b, alpha);
    }

    /// <summary>White four-point star (astroid) filling the square; the Image colour tints it.</summary>
    private static Color32 StarPixel(int x, int y, int width, int height)
    {
        Vector2 p = new(x + 0.5f - width * 0.5f, y + 0.5f - height * 0.5f);
        float r = width * 0.49f;
        float star = Mathf.Sqrt(Mathf.Abs(p.x) / r) + Mathf.Sqrt(Mathf.Abs(p.y) / r);
        return new Color(1f, 1f, 1f, Mathf.Clamp01((1f - star) * r * 0.25f));
    }

    private static Color32 ButtonPixel(int x, int y, int width, int height)
    {
        Vector2 p = new(x + 0.5f - width * 0.5f, y + 0.5f - height * 0.5f);
        float distance = RoundedBox(p, new Vector2(width * 0.5f, height * 0.5f), 14f);
        return new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - distance));
    }

    private static float RoundedBox(Vector2 p, Vector2 halfSize, float radius)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - (halfSize - Vector2.one * radius);
        Vector2 outside = new(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
        return outside.magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
    }

    #endregion

    #region Helpers

    /// <summary>UI/Cancel from the Input System's DefaultInputActions (Esc, gamepad B).</summary>
    private static InputActionReference FindCancelAction()
    {
        return AssetDatabase
            .LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(DefaultInputActionsGuid))
            .OfType<InputActionReference>()
            .FirstOrDefault(reference => reference.action != null
                && reference.action.actionMap != null
                && reference.action.actionMap.name == "UI"
                && reference.action.name == "Cancel");
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T found = root.GetComponentInChildren<T>(true);
            if (found != null) return found;
        }

        return null;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new(name, typeof(RectTransform)) { layer = UILayer };
        if (parent != null) go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color,
        Image.Type type = Image.Type.Simple, bool raycast = false)
    {
        RectTransform rect = CreateRect(name, parent);
        rect.gameObject.AddComponent<CanvasRenderer>();
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.type = type;
        image.raycastTarget = raycast;
        return image;
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions alignment)
    {
        RectTransform rect = CreateRect(name, parent);
        TextMeshProUGUI tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (_font != null) tmp.font = _font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void Stretch(RectTransform rect, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetRefs(Object target, params (string field, Object value)[] references)
    {
        SerializedObject so = new(target);
        foreach ((string field, Object value) in references)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[Menu] Campo '{field}' nao existe em {target.GetType().Name}.");
                continue;
            }

            property.objectReferenceValue = value;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureFolder(string assetFolder)
    {
        if (AssetDatabase.IsValidFolder(assetFolder)) return;

        string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(assetFolder));
    }

    private static string ToFullPath(string assetPath)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
    }

    #endregion
}
