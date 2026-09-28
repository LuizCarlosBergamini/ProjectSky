using System;
using System.IO;
using HierarchicalStateMachine;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Generates the level HUD (player and boss health bars + boss reward panel) so nothing has to be
/// wired by hand: placeholder sprites, the RewardEntry and HUD prefabs, the Guardiao boss data and
/// the Level1 scene setup. Safe to run again: sprites, data and prefabs are only created when missing,
/// so swapped art and Inspector edits are kept. "Rebuild Health Bar UI Prefabs" overwrites the prefabs.
/// </summary>
public static class HealthBarUIBuilder
{
    private const string SpriteFolder = "Assets/Sprites/UI/HUD";
    private const string PrefabFolder = "Assets/Renan/Prefabs/UI";
    private const string BossDataFolder = "Assets/ObjectData/Bosses";

    private const string Level1ScenePath = "Assets/Scenes/Level1.unity";
    private const string FontPath = "Assets/Renan/Fonts/Jersey10-Regular SDF 1.asset";
    private const string LucyEntityPath = "Assets/ObjectData/Entity/Lucy.asset";
    private const string GuardianEntityPath = "Assets/ObjectData/Entity/Guardiao.asset";
    private const string GuardianRewardTaskId = "missao-boss";

    private static readonly string HudPrefabPath = $"{PrefabFolder}/HUD.prefab";
    private static readonly string RewardEntryPrefabPath = $"{PrefabFolder}/RewardEntry.prefab";
    private static readonly string GuardianBossDataPath = $"{BossDataFolder}/Guardiao.asset";

    // Same palette as the upgrade UI: life green for Lucy, damage red for the boss.
    private static readonly Color PlayerFillColor = new(0.36f, 0.82f, 0.3f, 1f);
    private static readonly Color BossFillColor = new(0.93f, 0.27f, 0.2f, 1f);
    private static readonly Color TrailColor = new(1f, 0.87f, 0.55f, 1f);
    private static readonly Color BarBackColor = new(0.07f, 0.07f, 0.08f, 0.9f);
    private static readonly Color FrameColor = new(0.92f, 0.92f, 0.92f, 1f);
    private static readonly Color PortraitBackColor = new(0.12f, 0.12f, 0.15f, 0.9f);
    private static readonly Color PanelColor = new(0.07f, 0.07f, 0.08f, 0.75f);
    private static readonly Color TextColor = new(0.95f, 0.95f, 0.95f, 1f);
    private static readonly Color MutedTextColor = new(0.72f, 0.72f, 0.75f, 1f);

    // Canvas units at the 1280x720 reference resolution.
    private const float ScreenMargin = 16f;
    private const float BlockWidth = 340f;
    private const float PortraitSize = 64f;
    private const float PortraitGap = 8f;

    private const int UILayer = 5;

    private class Sprites
    {
        public Sprite square, frame, panel;
    }

    private static TMP_FontAsset _font;

    [MenuItem("Tools/Lucy/Build Health Bar UI")]
    public static void Build()
    {
        Run(overwritePrefabs: false);
    }

    [MenuItem("Tools/Lucy/Rebuild Health Bar UI Prefabs")]
    public static void RebuildPrefabs()
    {
        Run(overwritePrefabs: true);
    }

    private static void Run(bool overwritePrefabs)
    {
        // Opened first: the boss portrait placeholder is read from the Level1 boss, and building prefabs
        // creates temporary objects in the open scene.
        if (!OpenLevel1(out bool hadUnsavedChanges)) return;

        try
        {
            EnsureFolder(SpriteFolder);
            EnsureFolder(PrefabFolder);
            EnsureFolder(BossDataFolder);

            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            Sprites sprites = BuildSprites();

            RewardEntryUI rewardEntry = BuildRewardEntryPrefab(sprites, overwritePrefabs);
            GameObject hud = BuildHudPrefab(sprites, rewardEntry, overwritePrefabs);
            // The boss-adaptation panel belongs to BossAdaptationBuilder; re-added here so a rebuild keeps it.
            if (BossAdaptationBuilder.EnsureHudAdaptationPanel()) hud = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
            BossData_SO guardian = BuildGuardianData();

            AssetDatabase.SaveAssets();
            SetupLevel1(hud, guardian, hadUnsavedChanges);

            Debug.Log("[HUD] Barras de vida geradas. Veja Tools > Lucy para reconstruir os prefabs.");
        }
        finally
        {
            _font = null;
        }
    }

    #region Sprites

    private static Sprites BuildSprites()
    {
        return new Sprites
        {
            square = GenerateSprite("hud_square", 8, _ => 1f),
            frame = GenerateSprite("hud_frame", 32, p => Ring(Box(p, 16f), 3f), 6),
            panel = GenerateSprite("hud_panel", 32, p => Fill(Box(p, 16f)), 6)
        };
    }

    /// <summary>
    /// Writes a white PNG whose alpha comes from <paramref name="alpha"/> (pixel coordinates, origin in the
    /// centre) and imports it as a UI sprite. Existing files are left alone so final art can replace them.
    /// </summary>
    private static Sprite GenerateSprite(string fileName, int size, Func<Vector2, float> alpha, int sliceBorder = 0)
    {
        string assetPath = $"{SpriteFolder}/{fileName}.png";
        if (!File.Exists(ToFullPath(assetPath)))
        {
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 point = new(x + 0.5f - half, y + 0.5f - half);
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
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.spritePixelsPerUnit = 100f;
            importer.spriteBorder = new Vector4(sliceBorder, sliceBorder, sliceBorder, sliceBorder);
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
    }

    // Signed distance to a square of half-size `half`: negative inside, in pixels.
    private static float Box(Vector2 p, float half) => Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half;

    private static float Fill(float distance) => Mathf.Clamp01(0.5f - distance);

    private static float Ring(float distance, float thickness) =>
        Mathf.Clamp01(0.5f - distance) * Mathf.Clamp01(0.5f + distance + thickness);

    #endregion

    #region Prefabs

    private static RewardEntryUI BuildRewardEntryPrefab(Sprites sprites, bool overwrite)
    {
        RewardEntryUI existing = AssetDatabase.LoadAssetAtPath<RewardEntryUI>(RewardEntryPrefabPath);
        if (existing != null && !overwrite) return existing;

        GameObject root = CreateUI("RewardEntry", null);
        HorizontalLayoutGroup row = root.AddComponent<HorizontalLayoutGroup>();
        row.childAlignment = TextAnchor.MiddleRight;
        row.spacing = 6f;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;

        GameObject iconObject = CreateUI("Icon", root.transform);
        Image icon = AddImage(iconObject, sprites.square, Color.white);
        icon.preserveAspect = true;
        LayoutElement iconLayout = iconObject.AddComponent<LayoutElement>();
        iconLayout.preferredWidth = 28f;
        iconLayout.preferredHeight = 28f;

        TextMeshProUGUI label = AddText(CreateUI("Label", root.transform), "Item", 22f, TextAlignmentOptions.MidlineRight, TextColor);

        RewardEntryUI entry = root.AddComponent<RewardEntryUI>();
        SetRefs(entry, ("icon", icon), ("label", label));

        return SavePrefab(root, RewardEntryPrefabPath).GetComponent<RewardEntryUI>();
    }

    private static GameObject BuildHudPrefab(Sprites sprites, RewardEntryUI rewardEntryPrefab, bool overwrite)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
        if (existing != null && !overwrite) return existing;

        GameObject root = CreateUI("HUD", null);
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Below every other screen canvas (task list, dialogs, menus): the HUD never covers them.
        canvas.sortingOrder = -1;

        // Same settings as the TaskManager canvas, so both use the same units (the task list is pushed
        // down by a distance measured here).
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Shrink;
        scaler.referencePixelsPerUnit = 100f;

        // --- Player: portrait on the far left, bar growing to the right ---
        GameObject player = CreateUI("PlayerHealthBar", root.transform);
        RectTransform playerRect = (RectTransform)player.transform;
        playerRect.anchorMin = playerRect.anchorMax = new Vector2(0f, 1f);
        playerRect.pivot = new Vector2(0f, 1f);
        playerRect.anchoredPosition = new Vector2(ScreenMargin, -ScreenMargin);
        playerRect.sizeDelta = new Vector2(BlockWidth, PortraitSize);

        BarParts playerParts = BuildBarBlock(player.transform, sprites, mirrored: false, PlayerFillColor);
        Entity_SO lucy = AssetDatabase.LoadAssetAtPath<Entity_SO>(LucyEntityPath);
        if (lucy != null)
        {
            // Also set at runtime from portraitSource; baked here so the prefab previews correctly.
            playerParts.portrait.sprite = lucy.entitySprite;
            playerParts.name.text = lucy.entityName;
        }

        PlayerHealthBarUI playerUi = player.AddComponent<PlayerHealthBarUI>();
        SetRefs(playerUi,
            ("bar", playerParts.view),
            ("portrait", playerParts.portrait),
            ("nameText", playerParts.name),
            ("portraitSource", lucy));

        // --- Boss: mirrored, portrait on the far right, bar emptying towards the right ---
        GameObject bossBlock = CreateUI("BossHealthBar", root.transform);
        RectTransform bossRect = (RectTransform)bossBlock.transform;
        bossRect.anchorMin = bossRect.anchorMax = new Vector2(1f, 1f);
        bossRect.pivot = new Vector2(1f, 1f);
        bossRect.anchoredPosition = new Vector2(-ScreenMargin, -ScreenMargin);
        bossRect.sizeDelta = new Vector2(BlockWidth, PortraitSize);

        VerticalLayoutGroup stack = bossBlock.AddComponent<VerticalLayoutGroup>();
        stack.childAlignment = TextAnchor.UpperRight;
        stack.spacing = 6f;
        stack.childControlWidth = true;
        stack.childControlHeight = true;
        stack.childForceExpandWidth = false;
        stack.childForceExpandHeight = false;
        ContentSizeFitter stackFitter = bossBlock.AddComponent<ContentSizeFitter>();
        stackFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject bossBar = CreateUI("Bar", bossBlock.transform);
        LayoutElement bossBarLayout = bossBar.AddComponent<LayoutElement>();
        bossBarLayout.preferredWidth = BlockWidth;
        bossBarLayout.preferredHeight = PortraitSize;
        BarParts bossParts = BuildBarBlock(bossBar.transform, sprites, mirrored: true, BossFillColor);

        // Reward panel, right-aligned under the bar.
        GameObject rewardPanel = CreateUI("RewardPanel", bossBlock.transform);
        Image panelImage = AddImage(rewardPanel, sprites.panel, PanelColor);
        panelImage.type = Image.Type.Sliced;
        VerticalLayoutGroup rewardList = rewardPanel.AddComponent<VerticalLayoutGroup>();
        rewardList.childAlignment = TextAnchor.UpperRight;
        rewardList.padding = new RectOffset(10, 10, 6, 8);
        rewardList.spacing = 4f;
        rewardList.childControlWidth = true;
        rewardList.childControlHeight = true;
        rewardList.childForceExpandWidth = false;
        rewardList.childForceExpandHeight = false;
        AddText(CreateUI("Header", rewardPanel.transform), "Recompensa", 18f, TextAlignmentOptions.MidlineRight, MutedTextColor);

        CanvasGroup group = bossBlock.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        BossHealthBarUI bossUi = bossBlock.AddComponent<BossHealthBarUI>();
        SetRefs(bossUi,
            ("bar", bossParts.view),
            ("portrait", bossParts.portrait),
            ("nameText", bossParts.name),
            ("rewardPanel", rewardPanel),
            ("rewardEntryContainer", rewardPanel.transform),
            ("rewardEntryPrefab", rewardEntryPrefab));

        BossFightUIController controller = root.AddComponent<BossFightUIController>();
        SetRefs(controller, ("bossBar", bossUi), ("bossBlock", bossRect));

        return SavePrefab(root, HudPrefabPath);
    }

    private struct BarParts
    {
        public HealthBarView view;
        public Image portrait;
        public TextMeshProUGUI name;
    }

    /// <summary>
    /// Portrait + name + bar inside <paramref name="parent"/> (PortraitSize tall). Mirrored puts the
    /// portrait on the right and makes the fill empty towards the right-hand portrait's side.
    /// </summary>
    private static BarParts BuildBarBlock(Transform parent, Sprites sprites, bool mirrored, Color fillColor)
    {
        float side = mirrored ? 1f : 0f;
        float inset = PortraitSize + PortraitGap;

        // Portrait: backing, face, frame on top.
        GameObject portraitFrame = CreateUI("Portrait", parent);
        RectTransform portraitRect = (RectTransform)portraitFrame.transform;
        portraitRect.anchorMin = new Vector2(side, 0f);
        portraitRect.anchorMax = new Vector2(side, 1f);
        portraitRect.pivot = new Vector2(side, 0.5f);
        portraitRect.anchoredPosition = Vector2.zero;
        portraitRect.sizeDelta = new Vector2(PortraitSize, 0f);
        Image backing = AddImage(portraitFrame, sprites.panel, PortraitBackColor);
        backing.type = Image.Type.Sliced;

        GameObject face = CreateUI("Face", portraitFrame.transform);
        Stretch(face, 5f);
        Image faceImage = AddImage(face, null, Color.white);
        faceImage.preserveAspect = true;

        Image portraitBorder = AddImage(Stretched(CreateUI("Frame", portraitFrame.transform)), sprites.frame, FrameColor);
        portraitBorder.type = Image.Type.Sliced;

        // Name, over the bar on the portrait's side.
        GameObject nameObject = CreateUI("Name", parent);
        RectTransform nameRect = (RectTransform)nameObject.transform;
        nameRect.anchorMin = new Vector2(0f, 1f);
        nameRect.anchorMax = new Vector2(1f, 1f);
        nameRect.pivot = new Vector2(0.5f, 1f);
        nameRect.offsetMin = new Vector2(mirrored ? 0f : inset, -28f);
        nameRect.offsetMax = new Vector2(mirrored ? -inset : 0f, -2f);
        TextMeshProUGUI nameText = AddText(nameObject, "Nome", 24f,
            mirrored ? TextAlignmentOptions.BottomRight : TextAlignmentOptions.BottomLeft, TextColor);

        // Bar: background, damage trail, fill, frame, optional numbers.
        GameObject barObject = CreateUI("Bar", parent);
        RectTransform barRect = (RectTransform)barObject.transform;
        barRect.anchorMin = new Vector2(0f, 1f);
        barRect.anchorMax = new Vector2(1f, 1f);
        barRect.pivot = new Vector2(0.5f, 1f);
        barRect.offsetMin = new Vector2(mirrored ? 0f : inset, -56f);
        barRect.offsetMax = new Vector2(mirrored ? -inset : 0f, -32f);

        AddImage(Stretched(CreateUI("Background", barObject.transform)), sprites.square, BarBackColor);
        Image trail = AddFilledImage(Stretched(CreateUI("Trail", barObject.transform)), sprites.square, TrailColor, mirrored);
        Image fill = AddFilledImage(Stretched(CreateUI("Fill", barObject.transform)), sprites.square, fillColor, mirrored);
        Image barBorder = AddImage(Stretched(CreateUI("Frame", barObject.transform)), sprites.frame, FrameColor);
        barBorder.type = Image.Type.Sliced;

        GameObject numbers = Stretched(CreateUI("Numbers", barObject.transform));
        TextMeshProUGUI numbersText = AddText(numbers, "100/100", 18f, TextAlignmentOptions.Center, TextColor);
        numbers.SetActive(false);

        HealthBarView view = barObject.AddComponent<HealthBarView>();
        SetRefs(view, ("fill", fill), ("trail", trail), ("numbersText", numbersText));

        return new BarParts { view = view, portrait = faceImage, name = nameText };
    }

    #endregion

    #region Data

    private static BossData_SO BuildGuardianData()
    {
        Entity_SO entity = AssetDatabase.LoadAssetAtPath<Entity_SO>(GuardianEntityPath);
        if (entity == null)
        {
            entity = ScriptableObject.CreateInstance<Entity_SO>();
            entity.entityName = "Guardião";
            // Placeholder portrait: the boss's own sprite, until real portrait art exists.
            entity.entitySprite = FindLevel1BossSprite();
            AssetDatabase.CreateAsset(entity, GuardianEntityPath);
        }

        BossData_SO data = AssetDatabase.LoadAssetAtPath<BossData_SO>(GuardianBossDataPath);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<BossData_SO>();
            data.entity = entity;
            data.rewardTaskId = GuardianRewardTaskId;
            AssetDatabase.CreateAsset(data, GuardianBossDataPath);
        }

        return data;
    }

    private static Sprite FindLevel1BossSprite()
    {
        foreach (BossGate gate in Object.FindObjectsByType<BossGate>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            EnemyStateDriver boss = GetGateBoss(gate);
            if (boss == null) continue;

            foreach (SpriteRenderer renderer in boss.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer.enabled && renderer.sprite != null) return renderer.sprite;
            }
        }

        return null;
    }

    #endregion

    #region Scene

    private static bool OpenLevel1(out bool hadUnsavedChanges)
    {
        hadUnsavedChanges = false;
        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetActiveScene();
        if (scene.path == Level1ScenePath)
        {
            hadUnsavedChanges = scene.isDirty;
            return true;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.LogWarning("[HUD] Cancelado: a cena Level1 precisa estar aberta para gerar o HUD.");
            return false;
        }

        EditorSceneManager.OpenScene(Level1ScenePath, OpenSceneMode.Single);
        return true;
    }

    private static void SetupLevel1(GameObject hudPrefab, BossData_SO guardian, bool hadUnsavedChanges)
    {
        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetActiveScene();

        PlayerHealthBarUI playerBar = Object.FindFirstObjectByType<PlayerHealthBarUI>(FindObjectsInactive.Include);
        if (playerBar == null && hudPrefab != null)
        {
            GameObject hud = (GameObject)PrefabUtility.InstantiatePrefab(hudPrefab, scene);
            Undo.RegisterCreatedObjectUndo(hud, "Add HUD");
            playerBar = hud.GetComponentInChildren<PlayerHealthBarUI>(true);
        }

        PlayerStateDriver player = Object.FindFirstObjectByType<PlayerStateDriver>(FindObjectsInactive.Include);
        if (playerBar != null && player != null) SetRefs(playerBar, ("player", player));

        foreach (BossGate gate in Object.FindObjectsByType<BossGate>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            EnemyStateDriver boss = GetGateBoss(gate);
            if (boss == null) continue;

            SerializedObject so = new(boss);
            SerializedProperty bossData = so.FindProperty("bossData");
            if (bossData.objectReferenceValue != null) continue;
            bossData.objectReferenceValue = guardian;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorSceneManager.MarkSceneDirty(scene);
        if (hadUnsavedChanges)
        {
            Debug.LogWarning("[HUD] Level1 ja tinha alteracoes nao salvas; salve a cena manualmente.");
            return;
        }

        EditorSceneManager.SaveScene(scene);
    }

    private static EnemyStateDriver GetGateBoss(BossGate gate)
    {
        return new SerializedObject(gate).FindProperty("boss").objectReferenceValue as EnemyStateDriver;
    }

    #endregion

    #region Helpers

    private static GameObject CreateUI(string name, Transform parent)
    {
        GameObject go = new(name, typeof(RectTransform)) { layer = UILayer };
        if (parent != null) go.transform.SetParent(parent, false);
        return go;
    }

    private static GameObject Stretched(GameObject go)
    {
        Stretch(go, 0f);
        return go;
    }

    private static void Stretch(GameObject go, float inset)
    {
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static Image AddImage(GameObject go, Sprite sprite, Color color)
    {
        Image image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Image AddFilledImage(GameObject go, Sprite sprite, Color color, bool fromRight)
    {
        Image image = AddImage(go, sprite, color);
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Horizontal;
        // The bar empties towards its own portrait side: Lucy's fill is anchored left, the boss's right.
        image.fillOrigin = (int)(fromRight ? Image.OriginHorizontal.Right : Image.OriginHorizontal.Left);
        image.fillAmount = 1f;
        return image;
    }

    private static TextMeshProUGUI AddText(GameObject go, string text, float size, TextAlignmentOptions alignment, Color color)
    {
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        if (_font != null) tmp.font = _font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        return tmp;
    }

    private static GameObject SavePrefab(GameObject root, string path)
    {
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static void SetRefs(Object target, params (string field, Object value)[] references)
    {
        SerializedObject so = new(target);
        foreach ((string field, Object value) in references)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[HUD] Campo '{field}' nao existe em {target.GetType().Name}.");
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
