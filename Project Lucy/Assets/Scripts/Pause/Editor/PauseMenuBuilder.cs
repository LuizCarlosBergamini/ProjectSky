using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Generates Resources/PauseManager: the persistent PauseManager, its panel (PauseMenuController) and a fallback
/// EventSystem for levels that have none. PauseManager loads it before the first scene, so no scene is touched.
/// Reuses the Options menu prefab and sprites (built first when missing). Safe to run again: "Build Pause Menu" only
/// creates the prefab when missing; "Rebuild Pause Menu Prefab" overwrites it.
/// </summary>
public static class PauseMenuBuilder
{
    public const string PrefabPath = "Assets/Resources/" + PauseManager.ResourcePath + ".prefab";

    private const string OptionsSpriteFolder = "Assets/Sprites/UI/Options";
    private const string FontPath = "Assets/Renan/Fonts/Jersey10-Regular SDF 1.asset";
    private const string PlayerInputsPath = "Assets/InputS/PlayerInputs.inputactions";

    // Input System's DefaultInputActions, the asset the scenes' EventSystem uses (UI/Cancel = Esc, gamepad B).
    private const string DefaultInputActionsGuid = "ca9f5fa95ffab41fb9a615ab714db018";

    // Same palette as the Options menu, boss selector and upgrade screens.
    private static readonly Color FrameColor = new(0.035f, 0.035f, 0.04f, 0.98f);
    private static readonly Color TextColor = new(0.92f, 0.92f, 0.92f, 1f);
    private static readonly Color MutedTextColor = new(0.6f, 0.6f, 0.63f, 1f);
    private static readonly Color ButtonColor = new(0.18f, 0.18f, 0.2f, 1f);

    private const int UILayer = 5;
    private const float ButtonWidth = 340f;
    private const float ButtonHeight = 56f;

    private static TMP_FontAsset _font;

    [MenuItem("Tools/Lucy/Build Pause Menu")]
    public static void Build()
    {
        BuildPrefab(overwrite: false);
        Debug.Log($"[Pause] Pause pronto em {PrefabPath}. Ele e criado sozinho ao dar Play; nao coloque em cenas.");
    }

    [MenuItem("Tools/Lucy/Rebuild Pause Menu Prefab")]
    public static void Rebuild()
    {
        BuildPrefab(overwrite: true);
        Debug.Log($"[Pause] Prefab {PrefabPath} reconstruido.");
    }

    /// <summary>The Resources/PauseManager prefab asset, built first when missing (or always, with <paramref name="overwrite"/>).</summary>
    public static PauseManager BuildPrefab(bool overwrite = false)
    {
        if (!overwrite)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null && existing.TryGetComponent(out PauseManager component)) return component;
        }

        // Also generates the panel sprites this builder borrows.
        OptionsMenuController optionsPrefab = OptionsMenuBuilder.BuildPrefab(overwrite: false);
        Sprite panel = AssetDatabase.LoadAssetAtPath<Sprite>($"{OptionsSpriteFolder}/options_panel.png");
        Sprite border = AssetDatabase.LoadAssetAtPath<Sprite>($"{OptionsSpriteFolder}/options_panel_border.png");

        EnsureFolder(Path.GetDirectoryName(PrefabPath)?.Replace('\\', '/'));

        _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        try
        {
            GameObject root = new(nameof(PauseManager));
            PauseManager manager = root.AddComponent<PauseManager>();
            PauseMenuController menu = BuildCanvas(root.transform, panel, border, optionsPrefab);
            GameObject eventSystem = CreateFallbackEventSystem(root.transform);

            SetRefs(manager, ("_pauseAction", FindActionReference(PlayerInputsPath, "InGame", "Pause")), ("_menu", menu));
            SetRefs(menu, ("_fallbackEventSystem", eventSystem));

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return prefab.GetComponent<PauseManager>();
        }
        finally
        {
            _font = null;
        }
    }

    #region Prefab

    private static PauseMenuController BuildCanvas(Transform parent, Sprite panel, Sprite border, OptionsMenuController optionsPrefab)
    {
        RectTransform canvasRect = CreateRect("PauseMenu", parent);
        Canvas canvas = canvasRect.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 55; // above the boss selector and upgrade screens (50), below the options (60) and the fade (100)

        CanvasScaler scaler = canvasRect.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        canvasRect.gameObject.AddComponent<GraphicRaycaster>();

        RectTransform root = CreateRect("Root", canvasRect);
        Stretch(root);

        // Darkens the frozen game and blocks clicks to anything under the panel.
        Image dim = CreateImage("Dim", root, null, new Color(0f, 0f, 0f, 0.65f), raycast: true);
        Stretch(dim.rectTransform);

        Image frame = CreateImage("Frame", root, panel, FrameColor, Image.Type.Sliced, raycast: true);
        RectTransform frameRect = frame.rectTransform;
        frameRect.anchorMin = frameRect.anchorMax = new Vector2(0.5f, 0.5f);
        frameRect.pivot = new Vector2(0.5f, 0.5f);
        frameRect.sizeDelta = new Vector2(460f, 430f);
        Image frameBorder = CreateImage("Border", frameRect, border, new Color(1f, 1f, 1f, 0.12f), Image.Type.Sliced);
        Stretch(frameBorder.rectTransform);

        TextMeshProUGUI title = CreateText("Title", frameRect, "PAUSADO", 56f, TextColor, TextAlignmentOptions.Center);
        AnchorTop(title.rectTransform, 18f, 72f);

        RectTransform buttons = CreateRect("Buttons", frameRect);
        Stretch(buttons, 60f, 60f, 110f, 70f);
        VerticalLayoutGroup layout = buttons.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 18f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        CanvasGroup buttonsGroup = buttons.gameObject.AddComponent<CanvasGroup>();

        Button resume = CreateButton("ResumeButton", buttons, panel, "CONTINUAR");
        Button options = CreateButton("OptionsButton", buttons, panel, "CONFIGURAÇÕES");
        Button mainMenu = CreateButton("MainMenuButton", buttons, panel, "MENU PRINCIPAL");
        LinkVertical(resume, options, mainMenu);

        TextMeshProUGUI hint = CreateText("Hint", frameRect, "[Esc / B] Continuar", 20f, MutedTextColor, TextAlignmentOptions.Center);
        hint.rectTransform.anchorMin = new Vector2(0f, 0f);
        hint.rectTransform.anchorMax = new Vector2(1f, 0f);
        hint.rectTransform.pivot = new Vector2(0.5f, 0f);
        hint.rectTransform.offsetMin = new Vector2(20f, 18f);
        hint.rectTransform.offsetMax = new Vector2(-20f, 46f);

        InputActionReference cancelAction = FindActionReference(AssetDatabase.GUIDToAssetPath(DefaultInputActionsGuid), "UI", "Cancel");

        PauseMenuController controller = canvasRect.gameObject.AddComponent<PauseMenuController>();
        SetRefs(controller,
            ("_root", root.gameObject), ("_canvas", canvas), ("_buttonsGroup", buttonsGroup),
            ("_resumeButton", resume), ("_optionsButton", options), ("_mainMenuButton", mainMenu),
            ("_optionsMenuPrefab", optionsPrefab), ("_cancelAction", cancelAction));

        root.gameObject.SetActive(false);
        return controller;
    }

    private static GameObject CreateFallbackEventSystem(Transform parent)
    {
        GameObject go = new("FallbackEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        go.transform.SetParent(parent, false);
        go.SetActive(false);

        // AssignDefaultActions() would create runtime-only references that are lost on save, so the persistent
        // sub-assets of the package's DefaultInputActions are assigned instead.
        string path = AssetDatabase.GUIDToAssetPath(DefaultInputActionsGuid);
        InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
        if (asset == null)
        {
            Debug.LogWarning("[Pause] DefaultInputActions nao encontrado; configure o InputSystemUIInputModule do FallbackEventSystem manualmente.");
            return go;
        }

        InputSystemUIInputModule module = go.GetComponent<InputSystemUIInputModule>();
        module.actionsAsset = asset;
        module.point = FindActionReference(path, "UI", "Point");
        module.leftClick = FindActionReference(path, "UI", "Click");
        module.middleClick = FindActionReference(path, "UI", "MiddleClick");
        module.rightClick = FindActionReference(path, "UI", "RightClick");
        module.scrollWheel = FindActionReference(path, "UI", "ScrollWheel");
        module.move = FindActionReference(path, "UI", "Navigate");
        module.submit = FindActionReference(path, "UI", "Submit");
        module.cancel = FindActionReference(path, "UI", "Cancel");
        module.trackedDevicePosition = FindActionReference(path, "UI", "TrackedDevicePosition");
        module.trackedDeviceOrientation = FindActionReference(path, "UI", "TrackedDeviceOrientation");
        return go;
    }

    private static Button CreateButton(string name, Transform parent, Sprite sprite, string label)
    {
        Image image = CreateImage(name, parent, sprite, ButtonColor, Image.Type.Sliced, raycast: true);
        LayoutElement element = image.gameObject.AddComponent<LayoutElement>();
        element.minHeight = element.preferredHeight = ButtonHeight;
        element.preferredWidth = ButtonWidth;

        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        colors.selectedColor = new Color(0.95f, 0.72f, 0.3f, 1f); // accent: the focused button is obvious on a gamepad
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
        button.colors = colors;

        TextMeshProUGUI text = CreateText("Label", image.transform, label, 32f, TextColor, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        return button;
    }

    private static void LinkVertical(params Selectable[] chain)
    {
        for (int i = 0; i < chain.Length; i++)
        {
            Navigation navigation = chain[i].navigation;
            navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnUp = i > 0 ? chain[i - 1] : chain[^1];
            navigation.selectOnDown = i < chain.Length - 1 ? chain[i + 1] : chain[0];
            navigation.selectOnLeft = null;
            navigation.selectOnRight = null;
            chain[i].navigation = navigation;
        }
    }

    #endregion

    #region Helpers

    private static InputActionReference FindActionReference(string assetPath, string mapName, string actionName)
    {
        if (string.IsNullOrEmpty(assetPath)) return null;
        return AssetDatabase.LoadAllAssetsAtPath(assetPath)
            .OfType<InputActionReference>()
            .FirstOrDefault(reference => reference.action != null
                && reference.action.actionMap != null
                && reference.action.actionMap.name == mapName
                && reference.action.name == actionName);
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
        image.preserveAspect = type == Image.Type.Simple && sprite != null;
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

    /// <summary>Full-width strip hanging <paramref name="top"/> pixels below the parent's top edge.</summary>
    private static void AnchorTop(RectTransform rect, float top, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(0f, -top - height);
        rect.offsetMax = new Vector2(0f, -top);
    }

    private static void SetRefs(Object target, params (string field, Object value)[] references)
    {
        SerializedObject so = new(target);
        foreach ((string field, Object value) in references)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[Pause] Campo '{field}' nao existe em {target.GetType().Name}.");
                continue;
            }

            property.objectReferenceValue = value;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureFolder(string assetFolder)
    {
        if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder)) return;

        string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(assetFolder));
    }

    #endregion
}
