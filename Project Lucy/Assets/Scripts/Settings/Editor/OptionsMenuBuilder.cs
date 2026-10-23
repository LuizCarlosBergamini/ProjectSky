using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Generates the reusable Options menu prefab (OptionsMenu), its placeholder sprites and the SettingsManager prefab
/// in Resources that is loaded at startup. Opens no scene, so other builders (the main menu's) can call
/// <see cref="BuildPrefab"/> in the middle of their own run and then place the prefab wherever they need it.
/// Safe to run again: sprites and prefabs are only created when missing; "Rebuild Options Menu Prefab" is the
/// only command that overwrites anything, and only the OptionsMenu prefab.
/// </summary>
public static class OptionsMenuBuilder
{
    public const string PrefabPath = "Assets/Renan/Prefabs/OptionsMenu.prefab";
    public const string SettingsManagerPrefabPath = "Assets/Resources/" + SettingsManager.ResourcePath + ".prefab";
    public const string MixerPath = "Assets/Main.mixer";

    private const string SpriteFolder = "Assets/Sprites/UI/Options";
    private const string FontPath = "Assets/Renan/Fonts/Jersey10-Regular SDF 1.asset";
    private const string PlayerInputsPath = "Assets/InputS/PlayerInputs.inputactions";

    // Input System's DefaultInputActions, the asset the scenes' EventSystem uses (UI/Cancel = Esc, gamepad B).
    private const string DefaultInputActionsGuid = "ca9f5fa95ffab41fb9a615ab714db018";

    // Same palette as the boss selector and upgrade screens.
    private static readonly Color FrameColor = new(0.035f, 0.035f, 0.04f, 0.98f);
    private static readonly Color TextColor = new(0.92f, 0.92f, 0.92f, 1f);
    private static readonly Color MutedTextColor = new(0.6f, 0.6f, 0.63f, 1f);
    private static readonly Color AccentColor = new(0.95f, 0.72f, 0.3f, 1f);
    private static readonly Color ButtonColor = new(0.18f, 0.18f, 0.2f, 1f);
    private static readonly Color TrackColor = new(1f, 1f, 1f, 0.12f);

    private const int UILayer = 5;
    private const float RowHeight = 40f;
    private const float HeaderHeight = 30f;
    private const float LabelWidth = 250f;
    private const float ModeButtonWidth = 300f;

    // Default text of the "Como jogar" panel (TMP rich text). Edit it in the prefab afterwards; only "Rebuild
    // Options Menu Prefab" writes it again.
    private const string Accent = "#F2B84D";
    private const string GuideText =
        "<size=34><color=" + Accent + ">CONTROLES</color></size>\n" +
        "<b>A / D</b>: andar\n" +
        "<b>W</b> ou <b>Espaço</b>: pular\n" +
        "<b>Mouse</b>: mirar\n" +
        "<b>Clique esquerdo</b>: atacar (clique de novo durante o golpe para emendar o combo)\n" +
        "<b>F</b>: lançar o gancho no ponto de agarrar mais perto da mira\n" +
        "<b>A / D</b> pendurada no gancho: balançar\n" +
        "<b>Espaço</b> pendurada no gancho: soltar e ganhar impulso\n" +
        "<b>E</b>: conversar e interagir\n" +
        "<b>Espaço</b> ou <b>clique</b>: avançar o diálogo\n" +
        "<b>Esc</b> ou <b>P</b>: pausar\n\n" +
        "<size=34><color=" + Accent + ">O HUB</color></size>\n" +
        "O Hub é a base da Lucy. Converse com os personagens para receber missões e entre no portal para " +
        "abrir a seleção de chefes.\n\n" +
        "<size=34><color=" + Accent + ">CHEFES</color></size>\n" +
        "Cada chefe tem a sua fase. Chefes bloqueados aparecem em cinza: derrote os chefes liberados para " +
        "abrir os próximos. A arena se fecha quando você entra e só abre de novo quando o chefe cai.\n" +
        "Cuidado com o olhar da Medusa: ele transforma a Lucy em pedra por alguns segundos.\n\n" +
        "<size=34><color=" + Accent + ">ITENS E UPGRADES</color></size>\n" +
        "Vencer um chefe dá itens de recompensa. Se a Lucy morrer, a fase recomeça e os itens coletados " +
        "naquela tentativa são perdidos; ao vencer, você volta ao Hub com tudo.\n" +
        "Depois do primeiro chefe derrotado, o vendedor de upgrades aparece no Hub. Troque seus itens por " +
        "melhorias: cada fileira da árvore custa um tipo de item.\n\n" +
        "<size=34><color=" + Accent + ">DICAS</color></size>\n" +
        "Solte o gancho no ponto mais alto do balanço para ir mais longe.\n" +
        "Ajuste o volume da música e dos efeitos aqui nas opções, no menu principal ou com o jogo pausado.";

    private class Sprites
    {
        public Sprite panel, panelBorder, knob;
    }

    private static TMP_FontAsset _font;

    [MenuItem("Tools/Lucy/Build Options Menu")]
    public static void Build()
    {
        EnsureSettingsManagerPrefab();
        BuildPrefab(overwrite: false);
        Debug.Log($"[Opcoes] Menu de opcoes pronto em {PrefabPath}; SettingsManager em {SettingsManagerPrefabPath}.");
    }

    [MenuItem("Tools/Lucy/Rebuild Options Menu Prefab")]
    public static void Rebuild()
    {
        EnsureSettingsManagerPrefab();
        BuildPrefab(overwrite: true);
        Debug.Log($"[Opcoes] Prefab {PrefabPath} reconstruido.");
    }

    /// <summary>The OptionsMenu prefab asset (and the SettingsManager prefab), created only when missing.</summary>
    public static GameObject EnsurePrefab()
    {
        EnsureSettingsManagerPrefab();
        return BuildPrefab(overwrite: false).gameObject;
    }

    /// <summary>The OptionsMenu prefab asset, built first when missing (or always, with <paramref name="overwrite"/>).</summary>
    public static OptionsMenuController BuildPrefab(bool overwrite = false)
    {
        if (!overwrite)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null && existing.TryGetComponent(out OptionsMenuController component)) return component;
        }

        EnsureFolder(Path.GetDirectoryName(PrefabPath)?.Replace('\\', '/'));
        EnsureFolder(SpriteFolder);

        _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        try
        {
            OptionsMenuController built = BuildCanvas(BuildSprites());
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(built.gameObject, PrefabPath);
            Object.DestroyImmediate(built.gameObject);
            AssetDatabase.SaveAssets();
            return prefab.GetComponent<OptionsMenuController>();
        }
        finally
        {
            _font = null;
        }
    }

    /// <summary>Resources/SettingsManager with Main.mixer assigned. Created only when missing.</summary>
    public static SettingsManager EnsureSettingsManagerPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(SettingsManagerPrefabPath);
        if (existing != null && existing.TryGetComponent(out SettingsManager component)) return component;

        EnsureFolder(Path.GetDirectoryName(SettingsManagerPrefabPath)?.Replace('\\', '/'));

        GameObject go = new(nameof(SettingsManager));
        SettingsManager manager = go.AddComponent<SettingsManager>();
        SetRefs(manager, ("_mixer", AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath)));

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, SettingsManagerPrefabPath);
        Object.DestroyImmediate(go);
        return prefab.GetComponent<SettingsManager>();
    }

    /// <summary>A group of Main.mixer by name ("Master", "Music", "SFX"), for builders that create AudioSources.</summary>
    public static AudioMixerGroup FindMixerGroup(string groupName)
    {
        AudioMixerGroup group = AssetDatabase.LoadAllAssetsAtPath(MixerPath)
            .OfType<AudioMixerGroup>()
            .FirstOrDefault(g => g.name == groupName);
        if (group == null) Debug.LogWarning($"[Opcoes] Grupo '{groupName}' nao encontrado em {MixerPath}.");
        return group;
    }

    #region Sprites

    private static Sprites BuildSprites()
    {
        Vector2 half = new(32f, 32f);
        return new Sprites
        {
            panel = GenerateSprite("options_panel", 64, p => Fill(RoundedBox(p, half, 14f)), 20),
            panelBorder = GenerateSprite("options_panel_border", 64, p => Ring(RoundedBox(p, half, 14f), 2f), 20),
            knob = GenerateSprite("options_slider_knob", 64, p => Fill(p.magnitude - 28f))
        };
    }

    /// <summary>
    /// Writes a white PNG whose alpha comes from <paramref name="alpha"/> (pixel coordinates, origin in the
    /// centre, y up) and imports it as a UI sprite. Existing files are left alone so final art can replace them.
    /// </summary>
    private static Sprite GenerateSprite(string fileName, int size, Func<Vector2, float> alpha, int sliceBorder = 0)
    {
        string assetPath = $"{SpriteFolder}/{fileName}.png";
        if (!File.Exists(ToFullPath(assetPath)))
        {
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[size * size];
            float halfSize = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 point = new(x + 0.5f - halfSize, y + 0.5f - halfSize);
                    byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(point)) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
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
            importer.spritePixelsPerUnit = 100f;
            importer.spriteBorder = new Vector4(sliceBorder, sliceBorder, sliceBorder, sliceBorder);
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
    }

    // Signed distances: negative inside, in pixels. Alpha helpers give one pixel of antialiasing.
    private static float Fill(float distance) => Mathf.Clamp01(0.5f - distance);

    private static float Ring(float distance, float thickness) =>
        Mathf.Clamp01(0.5f - distance) * Mathf.Clamp01(0.5f + distance + thickness);

    private static float RoundedBox(Vector2 p, Vector2 halfSize, float radius)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - (halfSize - Vector2.one * radius);
        Vector2 outside = new(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
        return outside.magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
    }

    #endregion

    #region Prefab

    private static OptionsMenuController BuildCanvas(Sprites sprites)
    {
        RectTransform canvasRect = CreateRect("OptionsMenu", null);
        Canvas canvas = canvasRect.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60; // above the boss selector and upgrade screens (50), below the LevelManager fade (100)

        // Same settings as the boss selector and upgrade screens: Expand keeps the whole 1280x720 layout on screen.
        CanvasScaler scaler = canvasRect.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        canvasRect.gameObject.AddComponent<GraphicRaycaster>();

        RectTransform root = CreateRect("Root", canvasRect);
        Stretch(root);

        // Blocks clicks to whatever opened the menu.
        Image dim = CreateImage("Dim", root, null, new Color(0f, 0f, 0f, 0.8f), raycast: true);
        Stretch(dim.rectTransform);

        // Fixed width column, full height minus margins: anchored, so it fits any aspect ratio.
        Image frame = CreateImage("Frame", root, sprites.panel, FrameColor, Image.Type.Sliced, raycast: true);
        RectTransform frameRect = frame.rectTransform;
        frameRect.anchorMin = new Vector2(0.5f, 0f);
        frameRect.anchorMax = new Vector2(0.5f, 1f);
        frameRect.pivot = new Vector2(0.5f, 0.5f);
        frameRect.offsetMin = new Vector2(-380f, 24f);
        frameRect.offsetMax = new Vector2(380f, -24f);
        Image frameBorder = CreateImage("Border", frameRect, sprites.panelBorder, new Color(1f, 1f, 1f, 0.12f), Image.Type.Sliced);
        Stretch(frameBorder.rectTransform);

        RectTransform titleBar = CreateRect("TitleBar", frameRect);
        AnchorTop(titleBar, 0f, 72f);
        TextMeshProUGUI title = CreateText("Title", titleBar, "OPÇÕES", 48f, TextColor, TextAlignmentOptions.Center);
        Stretch(title.rectTransform);

        // Everything of the options proper; hidden while the guide panel takes its place.
        RectTransform optionsPanel = CreateRect("OptionsPanel", frameRect);
        Stretch(optionsPanel);

        RectTransform content = CreateRect("Content", optionsPanel);
        Stretch(content, 48f, 48f, 76f, 76f);
        VerticalLayoutGroup contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 14f;
        contentLayout.childAlignment = TextAnchor.UpperCenter;
        contentLayout.childControlWidth = contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        RectTransform audio = CreateSection("Section_Audio", content, "ÁUDIO");
        Slider master = CreateVolumeRow("Row_MasterVolume", audio, "VOLUME GERAL", VolumeChannel.Master, sprites);
        Slider music = CreateVolumeRow("Row_MusicVolume", audio, "MÚSICA", VolumeChannel.Music, sprites);
        Slider sfx = CreateVolumeRow("Row_SfxVolume", audio, "EFEITOS", VolumeChannel.Sfx, sprites);

        // The mode's button: the guide on the main menu, saving in the pause menu. Only one is shown at a time.
        RectTransform modeRow = CreateRect("Row_ModeButtons", content);
        SetLayout(modeRow.gameObject, minHeight: 64f, preferredHeight: 64f);
        HorizontalLayoutGroup modeLayout = modeRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        modeLayout.childAlignment = TextAnchor.LowerCenter;
        modeLayout.childControlWidth = modeLayout.childControlHeight = true;
        modeLayout.childForceExpandWidth = modeLayout.childForceExpandHeight = false;

        Button guideButton = CreateButton("GuideButton", modeRow, sprites.panel, ButtonColor, "COMO JOGAR", 28f, TextColor);
        SetLayout(guideButton.gameObject, preferredWidth: ModeButtonWidth, minHeight: 48f, preferredHeight: 48f);
        Button saveButton = CreateButton("SaveButton", modeRow, sprites.panel, ButtonColor, "SALVAR JOGO", 28f, TextColor);
        SetLayout(saveButton.gameObject, preferredWidth: ModeButtonWidth, minHeight: 48f, preferredHeight: 48f);
        saveButton.gameObject.SetActive(false);

        TextMeshProUGUI saveStatus = CreateText("SaveStatus", content, string.Empty, 26f, AccentColor, TextAlignmentOptions.Center);
        SetLayout(saveStatus.gameObject, minHeight: 30f, preferredHeight: 30f);

        // Bottom bar: back button in the middle, input hint on the right.
        RectTransform bottomBar = CreateRect("BottomBar", optionsPanel);
        bottomBar.anchorMin = new Vector2(0f, 0f);
        bottomBar.anchorMax = new Vector2(1f, 0f);
        bottomBar.pivot = new Vector2(0.5f, 0f);
        bottomBar.offsetMin = new Vector2(20f, 14f);
        bottomBar.offsetMax = new Vector2(-20f, 66f);

        Button backButton = CreateButton("BackButton", bottomBar, sprites.panel, ButtonColor, "VOLTAR", 28f, TextColor);
        RectTransform backRect = (RectTransform)backButton.transform;
        backRect.anchorMin = backRect.anchorMax = new Vector2(0.5f, 0.5f);
        backRect.pivot = new Vector2(0.5f, 0.5f);
        backRect.sizeDelta = new Vector2(200f, 48f);

        InputActionReference cancelAction = FindActionReference(AssetDatabase.GUIDToAssetPath(DefaultInputActionsGuid), "UI", "Cancel");
        InputActionReference closeAction = FindActionReference(PlayerInputsPath, "InGame", "Pause");
        TextMeshProUGUI hint = CreateText("Hint", bottomBar, "[Esc / B] Voltar", 20f, MutedTextColor, TextAlignmentOptions.MidlineRight);
        Stretch(hint.rectTransform, 500f, 8f, 0f, 0f);
        hint.enableAutoSizing = true;
        hint.fontSizeMin = 12f;
        hint.fontSizeMax = 20f;

        GuidePanel guide = CreateGuidePanel(frameRect, sprites);

        // Vertical navigation (sliders, the mode's button, Back) is linked by the controller on Open, since it
        // depends on the mode.
        OptionsMenuController controller = canvasRect.gameObject.AddComponent<OptionsMenuController>();
        SetRefs(controller,
            ("_root", root.gameObject), ("_canvas", canvas), ("_backButton", backButton), ("_title", title),
            ("_firstSelected", master), ("_cancelAction", cancelAction), ("_closeAction", closeAction),
            ("_guideButton", guideButton), ("_guidePanel", guide.panel), ("_optionsPanel", optionsPanel.gameObject),
            ("_guideBackButton", guide.backButton), ("_guideFirstSelected", guide.scrollbar), ("_guideScroll", guide.scroll),
            ("_saveButton", saveButton), ("_saveStatusLabel", saveStatus));
        SetRefArray(controller, "_volumeSliders", master, music, sfx);

        root.gameObject.SetActive(false);
        return controller;
    }

    private static RectTransform CreateSection(string name, Transform parent, string header)
    {
        RectTransform section = CreateRect(name, parent);
        VerticalLayoutGroup layout = section.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 4f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI headerText = CreateText("Header", section, header, 30f, AccentColor, TextAlignmentOptions.MidlineLeft);
        SetLayout(headerText.gameObject, minHeight: HeaderHeight, preferredHeight: HeaderHeight);
        return section;
    }

    private static RectTransform CreateRow(string name, Transform parent, string label, float height)
    {
        RectTransform row = CreateRect(name, parent);
        SetLayout(row.gameObject, minHeight: height, preferredHeight: height);
        HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 16f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;

        TextMeshProUGUI labelText = CreateText("Label", row, label, 28f, TextColor, TextAlignmentOptions.MidlineLeft);
        SetLayout(labelText.gameObject, preferredWidth: LabelWidth, flexibleHeight: 1f);
        return row;
    }

    private static Slider CreateVolumeRow(string name, Transform section, string label, VolumeChannel channel, Sprites sprites)
    {
        RectTransform row = CreateRow(name, section, label, RowHeight);

        RectTransform sliderRect = CreateRect("Slider", row);
        SetLayout(sliderRect.gameObject, flexibleWidth: 1f, flexibleHeight: 1f);

        Image track = CreateImage("Background", sliderRect, sprites.panel, TrackColor, Image.Type.Sliced);
        track.rectTransform.anchorMin = new Vector2(0f, 0.35f);
        track.rectTransform.anchorMax = new Vector2(1f, 0.65f);
        track.rectTransform.offsetMin = track.rectTransform.offsetMax = Vector2.zero;

        RectTransform fillArea = CreateRect("Fill Area", sliderRect);
        fillArea.anchorMin = new Vector2(0f, 0.35f);
        fillArea.anchorMax = new Vector2(1f, 0.65f);
        fillArea.offsetMin = new Vector2(4f, 0f);
        fillArea.offsetMax = new Vector2(-14f, 0f);
        Image fill = CreateImage("Fill", fillArea, sprites.panel, AccentColor, Image.Type.Sliced);
        fill.rectTransform.sizeDelta = new Vector2(10f, 0f);

        RectTransform handleArea = CreateRect("Handle Slide Area", sliderRect);
        Stretch(handleArea, 12f, 12f, 0f, 0f);
        Image handle = CreateImage("Handle", handleArea, sprites.knob, Color.white);
        handle.rectTransform.sizeDelta = new Vector2(26f, 0f);
        handle.raycastTarget = true;

        Slider slider = sliderRect.gameObject.AddComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.value = 1f;
        // Selected is the accent colour so the focused slider is obvious with keyboard or gamepad.
        slider.colors = MakeColors(selected: AccentColor);

        // The track takes the clicks for the whole slider width.
        track.raycastTarget = true;

        TextMeshProUGUI value = CreateText("Value", row, "100%", 26f, MutedTextColor, TextAlignmentOptions.MidlineRight);
        SetLayout(value.gameObject, preferredWidth: 72f, flexibleHeight: 1f);

        VolumeSliderUI binding = sliderRect.gameObject.AddComponent<VolumeSliderUI>();
        SetRefs(binding, ("_slider", slider), ("_valueLabel", value));
        SetEnum(binding, "_channel", (int)channel);

        return slider;
    }

    private class GuidePanel
    {
        public GameObject panel;
        public ScrollRect scroll;
        public Scrollbar scrollbar;
        public Button backButton;
    }

    /// <summary>
    /// The "Como jogar" panel: scrolling text over the same area as the options, with its own Back button.
    /// Mouse wheel scrolls the text; with keyboard/gamepad the scrollbar takes the focus and up/down scrolls.
    /// </summary>
    private static GuidePanel CreateGuidePanel(RectTransform frame, Sprites sprites)
    {
        RectTransform panel = CreateRect("GuidePanel", frame);
        Stretch(panel);

        RectTransform scrollRect = CreateRect("GuideScroll", panel);
        Stretch(scrollRect, 48f, 48f, 76f, 76f);
        ScrollRect scroll = scrollRect.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;

        RectTransform viewport = CreateRect("Viewport", scrollRect);
        Stretch(viewport, 0f, 28f, 0f, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();
        // Transparent but raycastable, so the wheel and drags over the text reach the ScrollRect.
        Image viewportHit = viewport.gameObject.AddComponent<Image>();
        viewportHit.color = new Color(0f, 0f, 0f, 0f);

        RectTransform textContent = CreateRect("Content", viewport);
        textContent.anchorMin = new Vector2(0f, 1f);
        textContent.anchorMax = new Vector2(1f, 1f);
        textContent.pivot = new Vector2(0.5f, 1f);
        textContent.offsetMin = textContent.offsetMax = Vector2.zero;
        VerticalLayoutGroup textLayout = textContent.gameObject.AddComponent<VerticalLayoutGroup>();
        textLayout.padding = new RectOffset(4, 12, 4, 16);
        textLayout.childControlWidth = textLayout.childControlHeight = true;
        textLayout.childForceExpandWidth = true;
        textLayout.childForceExpandHeight = false;
        ContentSizeFitter fitter = textContent.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI text = CreateText("GuideText", textContent, GuideText, 26f, TextColor, TextAlignmentOptions.TopLeft);
        text.textWrappingMode = TextWrappingModes.Normal;
        text.richText = true;
        text.lineSpacing = 8f;
        text.paragraphSpacing = 4f;

        Image scrollbarTrack = CreateImage("Scrollbar", scrollRect, sprites.panel, TrackColor, Image.Type.Sliced, raycast: true);
        RectTransform scrollbarRect = scrollbarTrack.rectTransform;
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = new Vector2(1f, 1f);
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.offsetMin = new Vector2(-12f, 0f);
        scrollbarRect.offsetMax = Vector2.zero;
        RectTransform slidingArea = CreateRect("Sliding Area", scrollbarRect);
        Stretch(slidingArea);
        Image handle = CreateImage("Handle", slidingArea, sprites.panel, Color.white, Image.Type.Sliced, raycast: true);
        handle.rectTransform.offsetMin = handle.rectTransform.offsetMax = Vector2.zero;

        Scrollbar scrollbar = scrollbarTrack.gameObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle.rectTransform;
        scrollbar.targetGraphic = handle;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        // Selected is the accent colour, like the sliders, so the focus is visible with keyboard or gamepad.
        scrollbar.colors = MakeColors(selected: AccentColor);

        scroll.content = textContent;
        scroll.viewport = viewport;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        RectTransform bottomBar = CreateRect("BottomBar", panel);
        bottomBar.anchorMin = new Vector2(0f, 0f);
        bottomBar.anchorMax = new Vector2(1f, 0f);
        bottomBar.pivot = new Vector2(0.5f, 0f);
        bottomBar.offsetMin = new Vector2(20f, 14f);
        bottomBar.offsetMax = new Vector2(-20f, 66f);

        Button backButton = CreateButton("GuideBackButton", bottomBar, sprites.panel, ButtonColor, "VOLTAR", 28f, TextColor);
        RectTransform backRect = (RectTransform)backButton.transform;
        backRect.anchorMin = backRect.anchorMax = new Vector2(0.5f, 0.5f);
        backRect.pivot = new Vector2(0.5f, 0.5f);
        backRect.sizeDelta = new Vector2(200f, 48f);

        TextMeshProUGUI hint = CreateText("Hint", bottomBar, "[Cima / Baixo] Rolar   [Esc / B] Voltar", 20f, MutedTextColor, TextAlignmentOptions.MidlineRight);
        Stretch(hint.rectTransform, 480f, 8f, 0f, 0f);
        hint.enableAutoSizing = true;
        hint.fontSizeMin = 12f;
        hint.fontSizeMax = 20f;

        // Up/down on the scrollbar scrolls (a Scrollbar only moves its value when there is nothing to navigate
        // to in that direction); left/right goes to Back, and up from Back returns to the scrollbar.
        Navigation scrollbarNavigation = new() { mode = Navigation.Mode.Explicit, selectOnLeft = backButton, selectOnRight = backButton };
        scrollbar.navigation = scrollbarNavigation;
        Navigation backNavigation = new() { mode = Navigation.Mode.Explicit, selectOnUp = scrollbar };
        backButton.navigation = backNavigation;

        panel.gameObject.SetActive(false);
        return new GuidePanel { panel = panel.gameObject, scroll = scroll, scrollbar = scrollbar, backButton = backButton };
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

    private static Button CreateButton(string name, Transform parent, Sprite sprite, Color color, string label, float fontSize, Color labelColor)
    {
        Image image = CreateImage(name, parent, sprite, color, Image.Type.Sliced, raycast: true);
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.colors = MakeColors(selected: new Color(0.9f, 0.9f, 0.9f, 1f));

        TextMeshProUGUI text = CreateText("Label", image.transform, label, fontSize, labelColor, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        return button;
    }

    private static ColorBlock MakeColors(Color selected)
    {
        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        colors.selectedColor = selected;
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
        return colors;
    }

    private static void SetLayout(GameObject go, float minHeight = -1f, float preferredHeight = -1f, float preferredWidth = -1f,
        float flexibleWidth = -1f, float flexibleHeight = -1f)
    {
        LayoutElement layout = go.GetComponent<LayoutElement>();
        if (layout == null) layout = go.AddComponent<LayoutElement>();
        layout.minHeight = minHeight;
        layout.preferredHeight = preferredHeight;
        layout.preferredWidth = preferredWidth;
        layout.flexibleWidth = flexibleWidth;
        layout.flexibleHeight = flexibleHeight;
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
                Debug.LogError($"[Opcoes] Campo '{field}' nao existe em {target.GetType().Name}.");
                continue;
            }

            property.objectReferenceValue = value;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetRefArray(Object target, string field, params Object[] values)
    {
        SerializedObject so = new(target);
        SerializedProperty property = so.FindProperty(field);
        if (property == null || !property.isArray)
        {
            Debug.LogError($"[Opcoes] Campo '{field}' nao existe em {target.GetType().Name}.");
            return;
        }

        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetEnum(Object target, string field, int index)
    {
        SerializedObject so = new(target);
        SerializedProperty property = so.FindProperty(field);
        if (property == null)
        {
            Debug.LogError($"[Opcoes] Campo '{field}' nao existe em {target.GetType().Name}.");
            return;
        }

        property.enumValueIndex = index;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureFolder(string assetFolder)
    {
        if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder)) return;

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
