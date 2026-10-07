using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Scene = UnityEngine.SceneManagement.Scene;

/// <summary>
/// Generates the MainMenu scene so nothing has to be wired by hand: placeholder art, the canvas hierarchy, the
/// MainMenuController references, the LevelManager (fade) and EventSystem, the Options prefab link, and registers
/// the scene as build index 0.
/// Safe to run again: placeholder sprites are only written when missing (final art dropped over them is kept) and
/// an existing scene is only re-linked and re-registered. "Rebuild Main Menu Scene" is the only command that
/// recreates the scene, and it asks first.
/// </summary>
public static class MainMenuBuilder
{
    public const string ScenePath = "Assets/Scenes/MainMenu.unity";

    private const string SpriteFolder = "Assets/Sprites/UI/MainMenu";
    private const string HubScenePath = "Assets/Scenes/Hub.unity";
    private const string LevelManagerPrefabPath = "Assets/Renan/Prefabs/LevelManager.prefab";
    private const string FontPath = "Assets/Renan/Fonts/Jersey10-Regular SDF 1.asset";

    private static readonly string BackgroundSpritePath = $"{SpriteFolder}/MainMenu_Background_Placeholder.png";
    private static readonly string LogoSpritePath = $"{SpriteFolder}/MainMenu_Logo_Placeholder.png";
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
                BuildScene(scene, background, logo, button);
            }

            LinkScene(scene, optionsPrefab);

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

    private static void BuildScene(Scene scene, Sprite background, Sprite logo, Sprite buttonSprite)
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

        // The persistent scene loader with the fade. Living here, it exists from the first frame of the game.
        GameObject levelManagerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LevelManagerPrefabPath);
        if (levelManagerPrefab != null) PrefabUtility.InstantiatePrefab(levelManagerPrefab, scene);
        else Debug.LogWarning($"[Menu] {LevelManagerPrefabPath} nao encontrado: as cenas vao carregar sem fade.");

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
