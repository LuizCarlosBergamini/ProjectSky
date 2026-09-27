using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HierarchicalStateMachine;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Scene = UnityEngine.SceneManagement.Scene;

/// <summary>
/// Generates everything the boss selector needs so nothing has to be wired by hand: placeholder sprites, the
/// grayscale material, the boss data (extending the existing Guardiao asset), the UI prefabs, the Level2 and
/// Level3 scenes (copies of Level1), their Build Settings entries and the Hub setup (manager, canvas, the
/// portal trigger and the two new reward tasks).
/// Safe to run again: data, sprites, scenes and scene objects are only created when missing, so art swaps and
/// Inspector edits are kept. "Rebuild Boss Selector UI Prefabs" is the only command that overwrites anything,
/// and only the UI prefabs.
/// </summary>
public static class BossSelectorBuilder
{
    private const string SpriteFolder = "Assets/Sprites/UI/BossSelector";
    private const string MaterialFolder = "Assets/Materials/UI";
    private const string BossDataFolder = "Assets/ObjectData/Bosses";
    private const string EntityFolder = "Assets/ObjectData/Entity";
    private const string PrefabFolder = "Assets/Renan/Prefabs";
    private const string UiPrefabFolder = "Assets/Renan/Prefabs/BossSelectorUI";

    private const string HubScenePath = "Assets/Scenes/Hub.unity";
    private const string Level1ScenePath = "Assets/Scenes/Level1.unity";
    private const string FontPath = "Assets/Renan/Fonts/Jersey10-Regular SDF 1.asset";
    private const string PlayerInputsPath = "Assets/InputS/PlayerInputs.inputactions";
    private const string BackgroundFolder = "Assets/Sprites/Terrain Tileset/GandalfHardcore Background layers";
    private const string GrayscaleShaderName = "Lucy/UI/Grayscale";

    // Input System's DefaultInputActions, the asset the Hub's EventSystem uses (UI/Cancel = Esc, gamepad B).
    private const string DefaultInputActionsGuid = "ca9f5fa95ffab41fb9a615ab714db018";

    // Level1's boss task: the old teleport's fight. Its Daisy intro is not copied into the new scenes.
    private const string Level1BossTaskId = "missao-boss";

    private static readonly string GrayscaleMaterialPath = $"{MaterialFolder}/UI_Grayscale.mat";
    private static readonly string ManagerPrefabPath = $"{PrefabFolder}/BossProgressionManager.prefab";
    private static readonly string CanvasPrefabPath = $"{PrefabFolder}/BossSelectorCanvas.prefab";
    private static readonly string PanelPrefabPath = $"{UiPrefabFolder}/BossPanel.prefab";
    private static readonly string RewardSlotPrefabPath = $"{UiPrefabFolder}/RewardSlot.prefab";
    private static readonly string InfoIconPrefabPath = $"{UiPrefabFolder}/BossInfoIcon.prefab";

    private static readonly Color PanelColor = new(0.07f, 0.07f, 0.08f, 1f);
    private static readonly Color FrameColor = new(0.035f, 0.035f, 0.04f, 0.98f);
    private static readonly Color TextColor = new(0.92f, 0.92f, 0.92f, 1f);
    private static readonly Color MutedTextColor = new(0.6f, 0.6f, 0.63f, 1f);
    private static readonly Color ConfirmColor = new(0.95f, 0.72f, 0.3f, 1f);
    private static readonly Color DefeatedColor = new(0.36f, 0.82f, 0.3f, 1f);

    private const int UILayer = 5;

    /// <summary>One boss of the roster. The first one is the existing Level1 fight and is only extended.</summary>
    private class BossSpec
    {
        public string assetName;
        public string displayName;
        public string taskId;
        public string taskTitle;
        public string taskDescription;
        public string rewardItemPath;
        public string scenePath;
        public string backgroundVariant;
        public string previewFile;
        public Color accent;
        public Color bossTint;
        public float bossX;
        public int difficulty;
        public int prerequisiteIndex = -1;

        // Filled while building.
        public BossData_SO data;

        public bool IsNewScene => scenePath != Level1ScenePath;
    }

    // Placeholder names, colours and positions: see the report. Replace freely in the assets afterwards.
    private static readonly BossSpec[] Specs =
    {
        new()
        {
            assetName = "Guardiao", displayName = "Guardião", taskId = Level1BossTaskId,
            scenePath = Level1ScenePath, backgroundVariant = "Normal BG", previewFile = "Background Castle .png",
            accent = new Color(0.86f, 0.56f, 0.3f, 1f), difficulty = 1
        },
        new()
        {
            assetName = "Sentinela", displayName = "Sentinela", taskId = "missao-boss-2",
            taskTitle = "Derrotar a Sentinela", taskDescription = "Derrote a Sentinela da arena do Level 2",
            rewardItemPath = "Assets/ObjectData/Items/Engrenagem_Ouro.asset",
            scenePath = "Assets/Scenes/Level2.unity", backgroundVariant = "Autumn BG", previewFile = "Background Castle Autumn.png",
            accent = new Color(0.98f, 0.78f, 0.18f, 1f), bossTint = new Color(1f, 0.85f, 0.35f, 1f), bossX = 28f,
            difficulty = 2, prerequisiteIndex = 0
        },
        new()
        {
            assetName = "Colosso", displayName = "Colosso", taskId = "missao-boss-3",
            taskTitle = "Derrotar o Colosso", taskDescription = "Derrote o Colosso da arena do Level 3",
            rewardItemPath = "Assets/ObjectData/Items/Emerald.asset",
            scenePath = "Assets/Scenes/Level3.unity", backgroundVariant = "Winter BG", previewFile = "Background Castle  Winter.png",
            accent = new Color(0.3f, 0.85f, 0.5f, 1f), bossTint = new Color(0.5f, 1f, 0.65f, 1f), bossX = 20f,
            difficulty = 3, prerequisiteIndex = 1
        }
    };

    private class Sprites
    {
        public Sprite panel, panelBorder, panelSelect, panelGlow;
        public Sprite circleFill, circleRing;
        public Sprite iconLock, iconCheck, iconSword, iconArena;
    }

    private static TMP_FontAsset _font;

    [MenuItem("Tools/Lucy/Build Boss Selector")]
    public static void Build()
    {
        Run(overwriteUi: false);
    }

    [MenuItem("Tools/Lucy/Rebuild Boss Selector UI Prefabs")]
    public static void RebuildUi()
    {
        Run(overwriteUi: true);
    }

    private static void Run(bool overwriteUi)
    {
        // Three scenes are opened one after the other, so every open scene has to be saved (or knowingly
        // discarded) first. Nothing of the user's is ever saved silently.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.LogWarning("[Chefes] Cancelado: salve ou descarte as cenas abertas para gerar o seletor.");
            return;
        }

        // Scratch scene: building prefabs creates temporary objects in the active scene.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        try
        {
            EnsureFolder(SpriteFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(BossDataFolder);
            EnsureFolder(UiPrefabFolder);

            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            Sprites sprites = BuildSprites();
            Material grayscale = BuildGrayscaleMaterial();

            BuildBossData(sprites);
            BuildManagerPrefab();
            BuildUiPrefabs(sprites, grayscale, overwriteUi);
            AssetDatabase.SaveAssets();

            // From here on scenes are opened, and opening one unloads assets only this code still holds:
            // everything below loads what it needs again by path.
            BuildBossScenes();
            RegisterScenesInBuild();
            AssignSceneReferences();
            AssetDatabase.SaveAssets();

            SetupHubScene();
            LogBuildIndices();

            Debug.Log("[Chefes] Seletor de chefes gerado. Veja Tools > Lucy para reconstruir a UI.");
        }
        finally
        {
            _font = null;
        }
    }

    #region Sprites

    private static Sprites BuildSprites()
    {
        Vector2 half = new(32f, 32f);
        return new Sprites
        {
            panel = GenerateSprite("boss_panel", 64, p => Fill(RoundedBox(p, half, 14f)), 20),
            panelBorder = GenerateSprite("boss_panel_border", 64, p => Ring(RoundedBox(p, half, 14f), 2f), 20),
            panelSelect = GenerateSprite("boss_panel_select", 64, p => Ring(RoundedBox(p, half, 14f), 4f), 20),
            // 128px with the box in the middle half: the outer 32px are the glow falling off.
            panelGlow = GenerateSprite("boss_panel_glow", 128, p => Glow(RoundedBox(p, half, 14f), 30f), 48),
            circleFill = GenerateSprite("boss_circle_fill", 128, p => Fill(p.magnitude - 60f)),
            circleRing = GenerateSprite("boss_circle_ring", 128, p => Ring(p.magnitude - 60f, 8f)),
            iconLock = GenerateSprite("boss_icon_lock", 128, p => Fill(IconShape(p, LockSdf))),
            iconCheck = GenerateSprite("boss_icon_check", 128, p => Fill(IconShape(p, CheckSdf))),
            iconSword = GenerateSprite("boss_icon_difficulty", 128, p => Fill(IconShape(p, SwordSdf))),
            iconArena = GenerateSprite("boss_icon_arena", 128, p => Fill(IconShape(p, CastleSdf)))
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

    private static float Glow(float distance, float spread) =>
        distance <= 0f ? 1f : Mathf.Pow(1f - Mathf.Clamp01(distance / spread), 2.2f);

    private static float RoundedBox(Vector2 p, Vector2 halfSize, float radius)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - (halfSize - Vector2.one * radius);
        Vector2 outside = new(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
        return outside.magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
    }

    private static float Box(Vector2 p, Vector2 centre, Vector2 halfSize) => RoundedBox(p - centre, halfSize, 0f);

    /// <summary>Evaluates an icon drawn in a -1..1 box, scaled to 128px with a small margin.</summary>
    private static float IconShape(Vector2 pixel, Func<Vector2, float> shape)
    {
        const float scale = 58f;
        return shape(pixel / scale) * scale;
    }

    private static float Segment(Vector2 p, Vector2 a, Vector2 b, float radius)
    {
        Vector2 edge = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, edge) / edge.sqrMagnitude);
        return (p - (a + edge * t)).magnitude - radius;
    }

    private static float CheckSdf(Vector2 p)
    {
        Vector2 corner = new(-0.18f, -0.5f);
        return Mathf.Min(Segment(p, new Vector2(-0.68f, 0f), corner, 0.15f), Segment(p, corner, new Vector2(0.7f, 0.56f), 0.15f));
    }

    private static float SwordSdf(Vector2 p)
    {
        float angle = -45f * Mathf.Deg2Rad;
        p = new Vector2(p.x * Mathf.Cos(angle) - p.y * Mathf.Sin(angle), p.x * Mathf.Sin(angle) + p.y * Mathf.Cos(angle));

        float blade = Polygon(p, new[] { new Vector2(0f, 0.98f), new Vector2(0.15f, 0.72f), new Vector2(0.15f, -0.3f), new Vector2(-0.15f, -0.3f), new Vector2(-0.15f, 0.72f) });
        float guard = Box(p, new Vector2(0f, -0.4f), new Vector2(0.42f, 0.08f));
        float grip = Box(p, new Vector2(0f, -0.66f), new Vector2(0.08f, 0.2f));
        float pommel = (p - new Vector2(0f, -0.9f)).magnitude - 0.12f;
        return Mathf.Min(blade, Mathf.Min(guard, Mathf.Min(grip, pommel)));
    }

    private static float LockSdf(Vector2 p)
    {
        float body = Box(p, new Vector2(0f, -0.38f), new Vector2(0.58f, 0.46f));
        Vector2 shackleCentre = new(0f, 0.2f);
        float ring = Mathf.Abs((p - shackleCentre).magnitude - 0.36f) - 0.09f;
        float upperHalf = Mathf.Max(ring, shackleCentre.y - p.y);
        float legs = Mathf.Min(Box(p, new Vector2(-0.36f, 0.1f), new Vector2(0.09f, 0.12f)), Box(p, new Vector2(0.36f, 0.1f), new Vector2(0.09f, 0.12f)));
        float keyhole = (p - new Vector2(0f, -0.32f)).magnitude - 0.13f;
        return Mathf.Max(Mathf.Min(body, Mathf.Min(upperHalf, legs)), -keyhole);
    }

    /// <summary>Small keep with three merlons and an arched gate: the "closed arena" icon.</summary>
    private static float CastleSdf(Vector2 p)
    {
        float body = Box(p, new Vector2(0f, -0.28f), new Vector2(0.62f, 0.6f));
        float merlons = Mathf.Min(Box(p, new Vector2(-0.47f, 0.44f), new Vector2(0.15f, 0.16f)),
            Mathf.Min(Box(p, new Vector2(0f, 0.44f), new Vector2(0.15f, 0.16f)), Box(p, new Vector2(0.47f, 0.44f), new Vector2(0.15f, 0.16f))));
        float gate = Mathf.Min(Box(p, new Vector2(0f, -0.62f), new Vector2(0.2f, 0.28f)), (p - new Vector2(0f, -0.34f)).magnitude - 0.2f);
        return Mathf.Max(Mathf.Min(body, merlons), -gate);
    }

    /// <summary>Exact signed distance to any simple polygon (convex or not).</summary>
    private static float Polygon(Vector2 p, Vector2[] vertices)
    {
        float distance = float.MaxValue;
        bool inside = false;
        for (int i = 0, j = vertices.Length - 1; i < vertices.Length; j = i++)
        {
            Vector2 a = vertices[j];
            Vector2 b = vertices[i];
            Vector2 edge = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, edge) / edge.sqrMagnitude);
            distance = Mathf.Min(distance, (p - (a + edge * t)).magnitude);

            if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
            {
                inside = !inside;
            }
        }

        return inside ? -distance : distance;
    }

    private static Material BuildGrayscaleMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(GrayscaleMaterialPath);
        if (existing != null) return existing;

        Shader shader = Shader.Find(GrayscaleShaderName);
        if (shader == null)
        {
            Debug.LogWarning($"[Chefes] Shader '{GrayscaleShaderName}' nao encontrado; paineis bloqueados ficam so escurecidos.");
            return null;
        }

        Material material = new(shader) { name = "UI_Grayscale" };
        AssetDatabase.CreateAsset(material, GrayscaleMaterialPath);
        return material;
    }

    #endregion

    #region Data

    private static void BuildBossData(Sprites sprites)
    {
        Entity_SO guardian = AssetDatabase.LoadAssetAtPath<Entity_SO>($"{EntityFolder}/Guardiao.asset");
        Sprite placeholderPortrait = guardian != null ? guardian.entitySprite : null;

        foreach (BossSpec spec in Specs)
        {
            string entityPath = $"{EntityFolder}/{spec.assetName}.asset";
            Entity_SO entity = AssetDatabase.LoadAssetAtPath<Entity_SO>(entityPath);
            if (entity == null)
            {
                entity = ScriptableObject.CreateInstance<Entity_SO>();
                entity.entityName = spec.displayName;
                // Placeholder portrait: the Guardiao's sprite, until the boss has art of its own.
                entity.entitySprite = placeholderPortrait;
                AssetDatabase.CreateAsset(entity, entityPath);
            }

            string dataPath = $"{BossDataFolder}/{spec.assetName}.asset";
            BossData_SO data = AssetDatabase.LoadAssetAtPath<BossData_SO>(dataPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<BossData_SO>();
                data.entity = entity;
                AssetDatabase.CreateAsset(data, dataPath);
            }

            spec.data = data;
        }

        // Second pass: prerequisites point at the other assets, which all exist now. Only empty fields are
        // filled, so anything edited in the Inspector survives a rebuild.
        foreach (BossSpec spec in Specs)
        {
            BossData_SO data = spec.data;
            // Same seed as BossData_SO.OnValidate: the asset name.
            if (string.IsNullOrWhiteSpace(data.bossId)) data.bossId = spec.assetName;
            if (data.entity == null) data.entity = AssetDatabase.LoadAssetAtPath<Entity_SO>($"{EntityFolder}/{spec.assetName}.asset");
            if (string.IsNullOrWhiteSpace(data.rewardTaskId)) data.rewardTaskId = spec.taskId;
            if (data.previewImage == null) data.previewImage = LoadFirstSprite($"{BackgroundFolder}/{spec.backgroundVariant}/{spec.previewFile}");
            if (data.accentColor == Color.white) data.accentColor = spec.accent;

            if (data.infoIcons == null || data.infoIcons.Count == 0)
            {
                data.infoIcons = new List<BossInfoIcon>
                {
                    new() { icon = sprites.iconSword, label = spec.difficulty.ToString(), tooltip = $"Dificuldade {spec.difficulty}" },
                    new() { icon = sprites.iconArena, label = "", tooltip = "Arena fechada: a saída só abre quando o chefe cair" }
                };
            }

            if ((data.prerequisites == null || data.prerequisites.Count == 0) && spec.prerequisiteIndex >= 0)
            {
                data.prerequisites = new List<BossData_SO> { Specs[spec.prerequisiteIndex].data };
            }

            EditorUtility.SetDirty(data);
        }
    }

    private static BossData_SO LoadData(BossSpec spec)
    {
        return AssetDatabase.LoadAssetAtPath<BossData_SO>($"{BossDataFolder}/{spec.assetName}.asset");
    }

    private static void AssignSceneReferences()
    {
        foreach (BossSpec spec in Specs)
        {
            SceneAsset scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(spec.scenePath);
            BossData_SO data = LoadData(spec);
            if (scene == null || data == null) continue;

            SerializedObject so = new(data);
            SerializedProperty asset = so.FindProperty("scene.sceneAsset");
            SerializedProperty path = so.FindProperty("scene.scenePath");
            if (asset.objectReferenceValue != null) continue;

            asset.objectReferenceValue = scene;
            path.stringValue = spec.scenePath;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void BuildManagerPrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
        if (prefab == null)
        {
            GameObject root = new("BossProgressionManager");
            root.AddComponent<BossProgressionManager>();
            prefab = PrefabUtility.SaveAsPrefabAsset(root, ManagerPrefabPath);
            Object.DestroyImmediate(root);
        }

        // Missing bosses are appended; the order the user set in the Inspector is kept.
        BossProgressionManager manager = prefab.GetComponent<BossProgressionManager>();
        SerializedObject so = new(manager);
        SerializedProperty roster = so.FindProperty("_bosses");
        foreach (BossSpec spec in Specs)
        {
            bool listed = false;
            for (int i = 0; i < roster.arraySize; i++)
            {
                if (roster.GetArrayElementAtIndex(i).objectReferenceValue == spec.data) listed = true;
            }

            if (listed) continue;
            roster.arraySize++;
            roster.GetArrayElementAtIndex(roster.arraySize - 1).objectReferenceValue = spec.data;
        }

        if (so.ApplyModifiedPropertiesWithoutUndo()) PrefabUtility.SavePrefabAsset(prefab);
    }

    #endregion

    #region UI Prefabs

    private static void BuildUiPrefabs(Sprites sprites, Material grayscale, bool overwrite)
    {
        RewardSlotUI slot = LoadOrBuild(RewardSlotPrefabPath, overwrite, () => BuildRewardSlot(sprites));
        BossInfoIconUI infoIcon = LoadOrBuild(InfoIconPrefabPath, overwrite, () => BuildInfoIcon(sprites));
        BossPanelUI panel = LoadOrBuild(PanelPrefabPath, overwrite, () => BuildPanel(sprites, grayscale, slot, infoIcon));
        LoadOrBuild(CanvasPrefabPath, overwrite, () => BuildCanvas(sprites, panel));
    }

    private static T LoadOrBuild<T>(string path, bool overwrite, Func<T> build) where T : Component
    {
        if (!overwrite)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && existing.TryGetComponent(out T component)) return component;
        }

        T built = build();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(built.gameObject, path);
        Object.DestroyImmediate(built.gameObject);
        return prefab.GetComponent<T>();
    }

    private static RewardSlotUI BuildRewardSlot(Sprites sprites)
    {
        RectTransform root = CreateRect("RewardSlot", null);
        root.sizeDelta = new Vector2(52f, 52f);
        // Raycast target: hovering the slot shows the item's tooltip.
        Image background = AddImage(root, sprites.panel, new Color(0.03f, 0.03f, 0.035f, 1f), Image.Type.Sliced, raycast: true);
        background.pixelsPerUnitMultiplier = 2f;

        Image frame = CreateImage("Frame", root, sprites.panelBorder, new Color(1f, 1f, 1f, 0.35f), Image.Type.Sliced);
        frame.pixelsPerUnitMultiplier = 2f;
        Stretch(frame.rectTransform);

        Image icon = CreateImage("Icon", root, null, Color.white);
        icon.preserveAspect = true;
        Stretch(icon.rectTransform, 7f, 7f, 7f, 7f);

        TextMeshProUGUI quantity = CreateText("Quantity", root, "2", 20f, TextColor, TextAlignmentOptions.BottomRight);
        Stretch(quantity.rectTransform, 2f, 4f, 2f, 1f);
        quantity.gameObject.SetActive(false);

        RewardSlotUI slot = root.gameObject.AddComponent<RewardSlotUI>();
        SetRefs(slot, ("_frame", frame), ("_icon", icon), ("_quantity", quantity));
        return slot;
    }

    private static BossInfoIconUI BuildInfoIcon(Sprites sprites)
    {
        RectTransform root = CreateRect("BossInfoIcon", null);
        root.sizeDelta = new Vector2(40f, 30f);
        AddImage(root, null, Color.clear, raycast: true); // hover target for the tooltip
        HorizontalLayoutGroup layout = root.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 4f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;

        // Round badge like the reference's icon row: dark disc, thin ring, the icon on top.
        Image badge = CreateImage("Badge", root, sprites.circleFill, new Color(0.03f, 0.03f, 0.035f, 1f));
        LayoutElement badgeLayout = badge.gameObject.AddComponent<LayoutElement>();
        badgeLayout.preferredWidth = badgeLayout.preferredHeight = 30f;
        badgeLayout.minWidth = badgeLayout.minHeight = 30f;
        Image ring = CreateImage("Ring", badge.rectTransform, sprites.circleRing, new Color(1f, 1f, 1f, 0.25f));
        Stretch(ring.rectTransform);
        Image icon = CreateImage("Icon", badge.rectTransform, sprites.iconSword, Color.white);
        Stretch(icon.rectTransform, 6f, 6f, 6f, 6f);

        TextMeshProUGUI label = CreateText("Label", root, "1", 22f, TextColor, TextAlignmentOptions.MidlineLeft);

        BossInfoIconUI info = root.gameObject.AddComponent<BossInfoIconUI>();
        SetRefs(info, ("_icon", icon), ("_label", label));
        return info;
    }

    private static BossPanelUI BuildPanel(Sprites sprites, Material grayscale, RewardSlotUI slotPrefab, BossInfoIconUI infoIconPrefab)
    {
        RectTransform root = CreateRect("BossPanel", null);
        root.sizeDelta = new Vector2(380f, 500f);
        LayoutElement rootLayout = root.gameObject.AddComponent<LayoutElement>();
        rootLayout.flexibleWidth = 1f;
        rootLayout.flexibleHeight = 1f;
        rootLayout.minWidth = 240f;
        // Invisible hit area over the whole panel; the visible parts live under Visual so they can shake
        // without fighting the layout group that places this root.
        Image hit = AddImage(root, null, Color.clear, raycast: true);

        RectTransform visual = CreateRect("Visual", root);
        Stretch(visual);

        Image glow = CreateImage("Glow", visual, sprites.panelGlow, Color.clear, Image.Type.Sliced);
        Stretch(glow.rectTransform, -30f, -30f, -30f, -30f);
        glow.enabled = false;

        Image background = CreateImage("Background", visual, sprites.panel, PanelColor, Image.Type.Sliced);
        Stretch(background.rectTransform);

        RectTransform content = CreateRect("Content", visual);
        Stretch(content, 14f, 14f, 12f, 14f);
        VerticalLayoutGroup contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 8f;
        contentLayout.childAlignment = TextAnchor.UpperCenter;
        contentLayout.childControlWidth = contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        // 1. Name
        TextMeshProUGUI nameText = CreateText("Name", content, "CHEFE", 32f, TextColor, TextAlignmentOptions.Center);
        nameText.overflowMode = TextOverflowModes.Ellipsis;
        SetLayout(nameText.gameObject, minHeight: 36f);

        // 2. Info icons
        RectTransform infoIcons = CreateRect("InfoIcons", content);
        HorizontalLayoutGroup infoLayout = infoIcons.gameObject.AddComponent<HorizontalLayoutGroup>();
        infoLayout.spacing = 14f;
        infoLayout.childAlignment = TextAnchor.MiddleCenter;
        infoLayout.childControlWidth = infoLayout.childControlHeight = true;
        infoLayout.childForceExpandWidth = infoLayout.childForceExpandHeight = false;
        SetLayout(infoIcons.gameObject, minHeight: 30f);

        // 3. Preview: stage art cropped to fill, boss art over it, lock overlay on top.
        RectTransform preview = CreateRect("Preview", content);
        SetLayout(preview.gameObject, minHeight: 120f, flexibleHeight: 1f);
        AddImage(preview, null, new Color(0.02f, 0.02f, 0.025f, 1f));
        preview.gameObject.AddComponent<RectMask2D>();

        Image previewImage = CreateImage("Image", preview, null, Color.white);
        previewImage.rectTransform.anchorMin = previewImage.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        AspectRatioFitter previewFitter = previewImage.gameObject.AddComponent<AspectRatioFitter>();
        previewFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        previewFitter.aspectRatio = 16f / 9f;

        Image bossArt = CreateImage("BossArt", preview, null, Color.white);
        bossArt.preserveAspect = true;
        bossArt.rectTransform.anchorMin = new Vector2(0.15f, 0.04f);
        bossArt.rectTransform.anchorMax = new Vector2(0.85f, 0.8f);
        bossArt.rectTransform.offsetMin = bossArt.rectTransform.offsetMax = Vector2.zero;

        Image previewFrame = CreateImage("Frame", preview, sprites.panelBorder, new Color(1f, 1f, 1f, 0.12f), Image.Type.Sliced);
        previewFrame.pixelsPerUnitMultiplier = 2f;
        Stretch(previewFrame.rectTransform);

        Image lockOverlay = CreateImage("LockOverlay", preview, null, new Color(0f, 0f, 0f, 0.55f));
        Stretch(lockOverlay.rectTransform);
        Image lockIcon = CreateImage("LockIcon", lockOverlay.rectTransform, sprites.iconLock, new Color(0.85f, 0.85f, 0.88f, 1f), size: new Vector2(64f, 64f));
        lockIcon.rectTransform.anchoredPosition = new Vector2(0f, 18f);
        TextMeshProUGUI lockHint = CreateText("Hint", lockOverlay.rectTransform, "Derrote o chefe anterior para desbloquear", 22f, TextColor, TextAlignmentOptions.Center);
        lockHint.textWrappingMode = TextWrappingModes.Normal;
        lockHint.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        lockHint.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        lockHint.rectTransform.pivot = new Vector2(0.5f, 1f);
        lockHint.rectTransform.offsetMin = new Vector2(14f, -84f);
        lockHint.rectTransform.offsetMax = new Vector2(-14f, -20f);
        lockOverlay.gameObject.SetActive(false);

        // 4. Reward section: header row, then the item grid.
        RectTransform rewardSection = CreateRect("Rewards", content);
        VerticalLayoutGroup rewardLayout = rewardSection.gameObject.AddComponent<VerticalLayoutGroup>();
        rewardLayout.spacing = 6f;
        rewardLayout.childAlignment = TextAnchor.UpperCenter;
        rewardLayout.childControlWidth = rewardLayout.childControlHeight = true;
        rewardLayout.childForceExpandWidth = true;
        rewardLayout.childForceExpandHeight = false;

        RectTransform header = CreateRect("Header", rewardSection);
        HorizontalLayoutGroup headerLayout = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 10f;
        headerLayout.childAlignment = TextAnchor.MiddleCenter;
        headerLayout.childControlWidth = headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = headerLayout.childForceExpandHeight = false;
        SetLayout(header.gameObject, minHeight: 22f);
        Image lineLeft = CreateImage("Line", header, null, new Color(1f, 1f, 1f, 0.12f));
        SetLayout(lineLeft.gameObject, minHeight: 2f, preferredHeight: 2f, flexibleWidth: 1f);
        TextMeshProUGUI rewardHeader = CreateText("Label", header, "RECOMPENSA", 20f, MutedTextColor, TextAlignmentOptions.Center);
        Image lineRight = CreateImage("Line", header, null, new Color(1f, 1f, 1f, 0.12f));
        SetLayout(lineRight.gameObject, minHeight: 2f, preferredHeight: 2f, flexibleWidth: 1f);

        RectTransform grid = CreateRect("Grid", rewardSection);
        GridLayoutGroup gridLayout = grid.gameObject.AddComponent<GridLayoutGroup>();
        gridLayout.cellSize = new Vector2(52f, 52f);
        gridLayout.spacing = new Vector2(6f, 6f);
        gridLayout.childAlignment = TextAnchor.UpperCenter;
        gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
        gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
        gridLayout.constraint = GridLayoutGroup.Constraint.Flexible;

        // Frame and selection over everything, the defeated badge in the corner.
        Image border = CreateImage("Border", visual, sprites.panelBorder, Color.white, Image.Type.Sliced);
        Stretch(border.rectTransform);
        Image selection = CreateImage("Selection", visual, sprites.panelSelect, Color.white, Image.Type.Sliced);
        Stretch(selection.rectTransform, -5f, -5f, -5f, -5f);
        selection.enabled = false;

        Image badge = CreateImage("DefeatedBadge", visual, sprites.circleFill, DefeatedColor, size: new Vector2(40f, 40f));
        badge.rectTransform.anchorMin = badge.rectTransform.anchorMax = new Vector2(1f, 1f);
        badge.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        badge.rectTransform.anchoredPosition = new Vector2(-12f, -12f);
        Image badgeRing = CreateImage("Ring", badge.rectTransform, sprites.circleRing, new Color(0f, 0f, 0f, 0.5f));
        Stretch(badgeRing.rectTransform);
        Image check = CreateImage("Check", badge.rectTransform, sprites.iconCheck, new Color(0.05f, 0.05f, 0.05f, 1f));
        Stretch(check.rectTransform, 8f, 8f, 8f, 8f);
        badge.gameObject.SetActive(false);

        Button button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None; // the panel draws its own hover/selection states
        button.targetGraphic = hit;

        BossPanelUI panel = root.gameObject.AddComponent<BossPanelUI>();
        SetRefs(panel,
            ("_button", button), ("_visual", visual), ("_background", background), ("_border", border),
            ("_selection", selection), ("_glow", glow), ("_nameText", nameText),
            ("_infoIconContainer", infoIcons), ("_infoIconPrefab", infoIconPrefab),
            ("_preview", previewImage), ("_previewFitter", previewFitter), ("_bossArt", bossArt),
            ("_lockOverlay", lockOverlay.gameObject), ("_lockHintText", lockHint), ("_defeatedBadge", badge.gameObject),
            ("_rewardSection", rewardSection.gameObject), ("_rewardHeaderText", rewardHeader),
            ("_rewardContainer", grid), ("_rewardSlotPrefab", slotPrefab), ("_lockedMaterial", grayscale));
        return panel;
    }

    private static BossSelectorUI BuildCanvas(Sprites sprites, BossPanelUI panelPrefab)
    {
        RectTransform canvasRect = CreateRect("BossSelectorCanvas", null);
        Canvas canvas = canvasRect.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50; // same layer as the upgrade screen: above the dialog and task canvases (0)

        // Same settings as UpgradeCanvas: Expand keeps the whole 1280x720 layout on screen at 4:3 and
        // ultrawide; extra space just widens (or heightens) the three panels.
        CanvasScaler scaler = canvasRect.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        canvasRect.gameObject.AddComponent<GraphicRaycaster>();

        AudioSource audio = canvasRect.gameObject.AddComponent<AudioSource>();
        audio.playOnAwake = false;

        RectTransform root = CreateRect("Root", canvasRect);
        Stretch(root);

        Image dim = CreateImage("Dim", root, null, new Color(0f, 0f, 0f, 0.8f), raycast: true);
        Stretch(dim.rectTransform);

        Image frame = CreateImage("Frame", root, sprites.panel, FrameColor, Image.Type.Sliced, raycast: true);
        Stretch(frame.rectTransform, 24f, 24f, 24f, 24f);
        Image frameBorder = CreateImage("Border", frame.rectTransform, sprites.panelBorder, new Color(1f, 1f, 1f, 0.12f), Image.Type.Sliced);
        Stretch(frameBorder.rectTransform);

        // Title bar, centred like the reference's "GAME FINDER" tab, close button in the corner.
        RectTransform titleBar = CreateRect("TitleBar", frame.rectTransform);
        AnchorTop(titleBar, 0f, 64f);
        TextMeshProUGUI title = CreateText("Title", titleBar, "SELEÇÃO DE CHEFE", 44f, TextColor, TextAlignmentOptions.Center);
        Stretch(title.rectTransform, 80f, 80f, 0f, 0f);

        Button closeButton = CreateButton("CloseButton", titleBar, sprites.panel, new Color(0.18f, 0.18f, 0.2f, 1f), "X", 30f, TextColor);
        RectTransform closeRect = (RectTransform)closeButton.transform;
        closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 0.5f);
        closeRect.pivot = new Vector2(1f, 0.5f);
        closeRect.sizeDelta = new Vector2(46f, 46f);
        closeRect.anchoredPosition = new Vector2(-16f, 0f);

        // Subtitle strip: the chosen boss, like the reference's "CHAMPION - DIFFICULTY 1" band.
        Image subtitleBand = CreateImage("Subtitle", frame.rectTransform, null, new Color(1f, 1f, 1f, 0.05f));
        AnchorTop(subtitleBand.rectTransform, 64f, 34f, 20f);
        TextMeshProUGUI subtitle = CreateText("Label", subtitleBand.rectTransform, "ESCOLHA UM CHEFE", 26f, ConfirmColor, TextAlignmentOptions.Center);
        Stretch(subtitle.rectTransform);

        // Three panels side by side, evenly spaced.
        RectTransform panels = CreateRect("Panels", frame.rectTransform);
        Stretch(panels, 20f, 20f, 112f, 80f);
        HorizontalLayoutGroup panelsLayout = panels.gameObject.AddComponent<HorizontalLayoutGroup>();
        panelsLayout.spacing = 18f;
        panelsLayout.childAlignment = TextAnchor.MiddleCenter;
        panelsLayout.childControlWidth = panelsLayout.childControlHeight = true;
        panelsLayout.childForceExpandWidth = panelsLayout.childForceExpandHeight = true;

        // Bottom bar: back + confirm in the middle, input hint on the right.
        RectTransform bottomBar = CreateRect("BottomBar", frame.rectTransform);
        bottomBar.anchorMin = new Vector2(0f, 0f);
        bottomBar.anchorMax = new Vector2(1f, 0f);
        bottomBar.pivot = new Vector2(0.5f, 0f);
        bottomBar.offsetMin = new Vector2(20f, 12f);
        bottomBar.offsetMax = new Vector2(-20f, 68f);

        RectTransform buttons = CreateRect("Buttons", bottomBar);
        buttons.anchorMin = buttons.anchorMax = new Vector2(0.5f, 0.5f);
        buttons.pivot = new Vector2(0.5f, 0.5f);
        HorizontalLayoutGroup buttonsLayout = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
        buttonsLayout.spacing = 16f;
        buttonsLayout.childAlignment = TextAnchor.MiddleCenter;
        buttonsLayout.childControlWidth = buttonsLayout.childControlHeight = true;
        buttonsLayout.childForceExpandWidth = buttonsLayout.childForceExpandHeight = false;
        ContentSizeFitter buttonsFitter = buttons.gameObject.AddComponent<ContentSizeFitter>();
        buttonsFitter.horizontalFit = buttonsFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Button backButton = CreateButton("BackButton", buttons, sprites.panel, new Color(0.18f, 0.18f, 0.2f, 1f), "VOLTAR", 28f, TextColor);
        SetLayout(backButton.gameObject, minHeight: 48f, preferredWidth: 180f);
        Button confirmButton = CreateButton("ConfirmButton", buttons, sprites.panel, ConfirmColor, "ENTRAR", 28f, new Color(0.08f, 0.06f, 0.03f, 1f));
        SetLayout(confirmButton.gameObject, minHeight: 48f, preferredWidth: 220f);

        InputActionReference closeAction = FindActionReference(PlayerInputsPath, "InGame", "Pause");
        InputActionReference cancelAction = FindActionReference(AssetDatabase.GUIDToAssetPath(DefaultInputActionsGuid), "UI", "Cancel");
        string closeKey = closeAction != null && closeAction.action.bindings.Count > 0
            ? InputControlPath.ToHumanReadableString(closeAction.action.bindings[0].effectivePath, InputControlPath.HumanReadableStringOptions.OmitDevice)
            : "Esc";
        TextMeshProUGUI hint = CreateText("Hint", bottomBar, $"[Setas] Escolher   [Enter] Entrar   [{closeKey}] Voltar", 20f, MutedTextColor, TextAlignmentOptions.MidlineRight);
        Stretch(hint.rectTransform, 820f, 8f, 0f, 0f);
        // Right of the centred buttons; shrinks instead of running into them on narrow screens.
        hint.enableAutoSizing = true;
        hint.fontSizeMin = 12f;
        hint.fontSizeMax = 20f;

        TooltipUI tooltip = BuildTooltip(sprites, root);

        BossSelectorUI selector = canvasRect.gameObject.AddComponent<BossSelectorUI>();
        SetRefs(selector,
            ("_root", root.gameObject), ("_panelContainer", panels), ("_panelPrefab", panelPrefab),
            ("_subtitleText", subtitle), ("_confirmButton", confirmButton), ("_backButton", backButton),
            ("_closeButton", closeButton), ("_tooltip", tooltip), ("_closeAction", closeAction),
            ("_cancelAction", cancelAction), ("_audioSource", audio));

        root.gameObject.SetActive(false);
        return selector;
    }

    private static TooltipUI BuildTooltip(Sprites sprites, RectTransform parent)
    {
        RectTransform root = CreateRect("Tooltip", parent);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0f);
        root.sizeDelta = new Vector2(300f, 60f);
        AddImage(root, sprites.panel, new Color(0.04f, 0.04f, 0.05f, 0.97f), Image.Type.Sliced);
        Image border = CreateImage("Border", root, sprites.panelBorder, new Color(1f, 1f, 1f, 0.2f), Image.Type.Sliced);
        Stretch(border.rectTransform);
        border.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        VerticalLayoutGroup layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 8, 10);
        layout.spacing = 2f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = root.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize; // fixed width, body wraps

        TextMeshProUGUI title = CreateText("Title", root, "Item", 24f, ConfirmColor, TextAlignmentOptions.TopLeft);
        title.overflowMode = TextOverflowModes.Ellipsis;
        TextMeshProUGUI body = CreateText("Body", root, "Descricao", 20f, TextColor, TextAlignmentOptions.TopLeft);
        body.textWrappingMode = TextWrappingModes.Normal;

        CanvasGroup group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        TooltipUI tooltip = root.gameObject.AddComponent<TooltipUI>();
        SetRefs(tooltip, ("_title", title), ("_body", body));
        return tooltip;
    }

    #endregion

    #region Scenes

    /// <summary>
    /// Copies Level1 into each missing boss scene and turns it into that boss's fight. An existing scene is
    /// left untouched: once created, it belongs to whoever edits it.
    /// </summary>
    private static void BuildBossScenes()
    {
        foreach (BossSpec spec in Specs)
        {
            if (!spec.IsNewScene) continue;

            if (File.Exists(ToFullPath(spec.scenePath)))
            {
                Debug.Log($"[Chefes] {spec.scenePath} ja existe; cena mantida como esta.");
                continue;
            }

            if (!AssetDatabase.CopyAsset(Level1ScenePath, spec.scenePath))
            {
                Debug.LogError($"[Chefes] Nao foi possivel copiar {Level1ScenePath} para {spec.scenePath}.");
                continue;
            }

            Scene scene = EditorSceneManager.OpenScene(spec.scenePath, OpenSceneMode.Single);
            ConfigureBossScene(scene, spec);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Chefes] {spec.scenePath} criada a partir de Level1 ({spec.displayName}).");
        }
    }

    private static void ConfigureBossScene(Scene scene, BossSpec spec)
    {
        BossGate gate = FindInScene<BossGate>(scene);
        if (gate == null)
        {
            Debug.LogError($"[Chefes] {spec.scenePath}: nenhum BossGate encontrado; configure a luta manualmente.");
            return;
        }

        // The Daisy intro belongs to the first fight only; her gate call goes with her.
        RemoveLevel1Intro(scene, gate);

        EnemyStateDriver boss = new SerializedObject(gate).FindProperty("boss").objectReferenceValue as EnemyStateDriver;
        if (boss != null)
        {
            SetSerialized(boss, "bossData", LoadData(spec));

            SerializedObject transformSo = new(boss.transform);
            SerializedProperty position = transformSo.FindProperty("m_LocalPosition");
            Vector3 local = position.vector3Value;
            position.vector3Value = new Vector3(spec.bossX, local.y, local.z);
            transformSo.ApplyModifiedPropertiesWithoutUndo();

            // Placeholder look until the boss has art of its own: same sprite, tinted.
            foreach (SpriteRenderer renderer in boss.GetComponentsInChildren<SpriteRenderer>(true))
            {
                SerializedObject rendererSo = new(renderer);
                rendererSo.FindProperty("m_Color").colorValue = spec.bossTint;
                rendererSo.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        else
        {
            Debug.LogWarning($"[Chefes] {spec.scenePath}: BossGate sem chefe; bossData nao atribuido.");
        }

        TaskTrigger taskTrigger = gate.GetComponent<TaskTrigger>();
        if (taskTrigger == null) taskTrigger = FindInScene<TaskTrigger>(scene);
        if (taskTrigger != null) SetSerialized(taskTrigger, "taskId", spec.taskId);
        else Debug.LogWarning($"[Chefes] {spec.scenePath}: nenhum TaskTrigger; a recompensa '{spec.taskId}' nao sera entregue.");

        SwapBackground(scene, spec.backgroundVariant);
    }

    private static void RemoveLevel1Intro(Scene scene, BossGate gate)
    {
        foreach (DisableOnTaskStatus marker in FindAllInScene<DisableOnTaskStatus>(scene))
        {
            // Already gone with an earlier intro object.
            if (marker == null) continue;
            if (new SerializedObject(marker).FindProperty("taskId").stringValue != Level1BossTaskId) continue;

            GameObject intro = PrefabUtility.GetOutermostPrefabInstanceRoot(marker.gameObject);
            if (intro == null) intro = marker.gameObject;

            SerializedObject gateSo = new(gate);
            RemoveCallsTargeting(gateSo.FindProperty("onFightStarted.m_PersistentCalls.m_Calls"), intro.transform);
            RemoveCallsTargeting(gateSo.FindProperty("onBossDefeated.m_PersistentCalls.m_Calls"), intro.transform);
            gateSo.ApplyModifiedPropertiesWithoutUndo();

            Object.DestroyImmediate(intro);
        }
    }

    private static void RemoveCallsTargeting(SerializedProperty calls, Transform root)
    {
        if (calls == null) return;

        for (int i = calls.arraySize - 1; i >= 0; i--)
        {
            Object target = calls.GetArrayElementAtIndex(i).FindPropertyRelative("m_Target").objectReferenceValue;
            Transform targetTransform = target switch
            {
                GameObject go => go.transform,
                Component component => component.transform,
                _ => null
            };

            if (targetTransform != null && targetTransform.IsChildOf(root)) calls.DeleteArrayElementAtIndex(i);
        }
    }

    /// <summary>Points every background layer from Normal BG at the same layer of another variant (Autumn, Winter).</summary>
    private static void SwapBackground(Scene scene, string variant)
    {
        const string normalFolder = "/Normal BG/";
        string variantFolder = $"/{variant}/";
        if (variantFolder == normalFolder) return;

        foreach (SpriteRenderer renderer in FindAllInScene<SpriteRenderer>(scene))
        {
            if (renderer.sprite == null) continue;
            string path = AssetDatabase.GetAssetPath(renderer.sprite);
            if (!path.Contains(normalFolder)) continue;

            string variantPath = path.Replace(normalFolder, variantFolder);
            Sprite replacement = AssetDatabase.LoadAllAssetsAtPath(variantPath).OfType<Sprite>()
                .FirstOrDefault(sprite => sprite.name == renderer.sprite.name);
            if (replacement == null) continue;

            SetSerialized(renderer, "m_Sprite", replacement);
        }
    }

    private static void RegisterScenesInBuild()
    {
        List<EditorBuildSettingsScene> scenes = new(EditorBuildSettings.scenes);
        bool changed = false;

        foreach (BossSpec spec in Specs)
        {
            if (!File.Exists(ToFullPath(spec.scenePath))) continue;

            int index = scenes.FindIndex(scene => scene.path == spec.scenePath);
            if (index < 0)
            {
                scenes.Add(new EditorBuildSettingsScene(spec.scenePath, true));
                changed = true;
            }
            else if (!scenes[index].enabled)
            {
                scenes[index].enabled = true;
                changed = true;
            }
        }

        if (changed) EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void LogBuildIndices()
    {
        foreach (BossSpec spec in Specs)
        {
            int index = UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath(spec.scenePath);
            Debug.Log($"[Chefes] {spec.displayName}: {spec.scenePath} -> build index {index}");
        }
    }

    #endregion

    #region Hub

    private static void SetupHubScene()
    {
        Scene scene = EditorSceneManager.OpenScene(HubScenePath, OpenSceneMode.Single);
        List<string> changes = new();

        GameObject managerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
        BossSelectorUI canvasPrefab = AssetDatabase.LoadAssetAtPath<BossSelectorUI>(CanvasPrefabPath);

        if (FindInScene<BossProgressionManager>(scene) == null && managerPrefab != null)
        {
            GameObject manager = (GameObject)PrefabUtility.InstantiatePrefab(managerPrefab, scene);
            changes.Add(manager.name);
        }

        BossSelectorUI selector = FindInScene<BossSelectorUI>(scene);
        if (selector == null && canvasPrefab != null)
        {
            GameObject canvasObject = (GameObject)PrefabUtility.InstantiatePrefab(canvasPrefab.gameObject, scene);
            selector = canvasObject.GetComponent<BossSelectorUI>();
            changes.Add(canvasObject.name);
        }

        if (selector != null)
        {
            SerializedObject selectorSo = new(selector);
            SerializedProperty player = selectorSo.FindProperty("_player");
            if (player.objectReferenceValue == null)
            {
                player.objectReferenceValue = FindInScene<PlayerStateDriver>(scene);
                selectorSo.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        ReplaceLevel1Teleporter(scene, selector, changes);

        // Triggers left without a selector (e.g. placed by hand) get the Hub's one.
        foreach (BossSelectorTrigger trigger in FindAllInScene<BossSelectorTrigger>(scene))
        {
            if (selector == null || new SerializedObject(trigger).FindProperty("_selector").objectReferenceValue != null) continue;
            SetSerialized(trigger, "_selector", selector);
            changes.Add($"{trigger.name} -> {selector.name}");
        }

        AddBossTasks(scene, changes);

        if (FindInScene<EventSystem>(scene) == null)
        {
            Debug.LogWarning("[Chefes] A Hub nao tem EventSystem: rode Tools > Lucy > Build Upgrade System ou adicione um.");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(changes.Count > 0
            ? $"[Chefes] Hub atualizada: {string.Join(", ", changes)}."
            : "[Chefes] Hub ja estava configurada.");
    }

    /// <summary>The portal that used to load Level1 directly now opens the selector instead.</summary>
    private static void ReplaceLevel1Teleporter(Scene scene, BossSelectorUI selector, List<string> changes)
    {
        string level1Name = Path.GetFileNameWithoutExtension(Level1ScenePath);
        foreach (SceneTeleporterByTrigger teleporter in FindAllInScene<SceneTeleporterByTrigger>(scene))
        {
            if (teleporter.onTriggerScene != level1Name) continue;

            GameObject portal = teleporter.gameObject;
            if (!portal.TryGetComponent(out BossSelectorTrigger trigger)) trigger = portal.AddComponent<BossSelectorTrigger>();
            if (selector != null) SetSerialized(trigger, "_selector", selector);

            Object.DestroyImmediate(teleporter);
            changes.Add($"{portal.name}: SceneTeleporterByTrigger(Level1) -> BossSelectorTrigger");
        }
    }

    /// <summary>
    /// The reward of each new boss is a TaskManager task, like missao-boss. The Hub's TaskManager overrides the
    /// prefab's task list, so the tasks are appended to that instance.
    /// </summary>
    private static void AddBossTasks(Scene scene, List<string> changes)
    {
        TaskManager taskManager = FindInScene<TaskManager>(scene);
        if (taskManager == null)
        {
            Debug.LogWarning("[Chefes] TaskManager nao encontrado na Hub; as recompensas dos novos chefes nao foram criadas.");
            return;
        }

        SerializedObject so = new(taskManager);
        SerializedProperty tasks = so.FindProperty("tasks");

        foreach (BossSpec spec in Specs)
        {
            if (string.IsNullOrEmpty(spec.rewardItemPath) || HasTask(tasks, spec.taskId)) continue;

            Item_SO reward = AssetDatabase.LoadAssetAtPath<Item_SO>(spec.rewardItemPath);
            if (reward == null)
            {
                Debug.LogWarning($"[Chefes] Item {spec.rewardItemPath} nao encontrado; task '{spec.taskId}' nao criada.");
                continue;
            }

            // A new array element starts as a copy of the last one, so every field is written.
            tasks.arraySize++;
            SerializedProperty task = tasks.GetArrayElementAtIndex(tasks.arraySize - 1);
            task.FindPropertyRelative("taskId").stringValue = spec.taskId;
            task.FindPropertyRelative("taskTitle").stringValue = spec.taskTitle;
            task.FindPropertyRelative("taskDescription").stringValue = spec.taskDescription;
            task.FindPropertyRelative("requiredItems").arraySize = 0;
            task.FindPropertyRelative("consumeItems").arraySize = 0;
            SerializedProperty rewards = task.FindPropertyRelative("rewardItems");
            rewards.arraySize = 1;
            rewards.GetArrayElementAtIndex(0).FindPropertyRelative("item").objectReferenceValue = reward;
            rewards.GetArrayElementAtIndex(0).FindPropertyRelative("quantity").intValue = 1;
            // Same rule as missao-boss: every clear pays again.
            task.FindPropertyRelative("repeatableReward").boolValue = true;
            task.FindPropertyRelative("objectiveId").stringValue = "";
            task.FindPropertyRelative("onStartEventTrigger").stringValue = "";
            task.FindPropertyRelative("onFinishEventTrigger").stringValue = "";

            changes.Add($"task {spec.taskId} ({reward.itemName} x1)");
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static bool HasTask(SerializedProperty tasks, string taskId)
    {
        for (int i = 0; i < tasks.arraySize; i++)
        {
            if (tasks.GetArrayElementAtIndex(i).FindPropertyRelative("taskId").stringValue == taskId) return true;
        }

        return false;
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

    private static List<T> FindAllInScene<T>(Scene scene) where T : Component
    {
        List<T> found = new();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            found.AddRange(root.GetComponentsInChildren<T>(true));
        }

        return found;
    }

    private static Sprite LoadFirstSprite(string assetPath)
    {
        Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>().FirstOrDefault();
        if (sprite == null) Debug.LogWarning($"[Chefes] Sprite de preview nao encontrado em {assetPath}.");
        return sprite;
    }

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

    private static Image AddImage(RectTransform rect, Sprite sprite, Color color, Image.Type type = Image.Type.Simple, bool raycast = false)
    {
        rect.gameObject.AddComponent<CanvasRenderer>();
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.type = type;
        image.preserveAspect = type == Image.Type.Simple && sprite != null;
        image.raycastTarget = raycast;
        return image;
    }

    private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color,
        Image.Type type = Image.Type.Simple, bool raycast = false, Vector2? size = null)
    {
        RectTransform rect = CreateRect(name, parent);
        if (size.HasValue) rect.sizeDelta = size.Value;
        return AddImage(rect, sprite, color, type, raycast);
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
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        colors.selectedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
        button.colors = colors;

        TextMeshProUGUI text = CreateText("Label", image.transform, label, fontSize, labelColor, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        return button;
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
    private static void AnchorTop(RectTransform rect, float top, float height, float horizontalInset = 0f)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(horizontalInset, -top - height);
        rect.offsetMax = new Vector2(-horizontalInset, -top);
    }

    private static void SetSerialized(Object target, string field, Object value)
    {
        SerializedObject so = new(target);
        SerializedProperty property = so.FindProperty(field);
        if (property == null)
        {
            Debug.LogError($"[Chefes] Campo '{field}' nao existe em {target.GetType().Name}.");
            return;
        }

        property.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetSerialized(Object target, string field, string value)
    {
        SerializedObject so = new(target);
        SerializedProperty property = so.FindProperty(field);
        if (property == null)
        {
            Debug.LogError($"[Chefes] Campo '{field}' nao existe em {target.GetType().Name}.");
            return;
        }

        property.stringValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetRefs(Object target, params (string field, Object value)[] references)
    {
        SerializedObject so = new(target);
        foreach ((string field, Object value) in references)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[Chefes] Campo '{field}' nao existe em {target.GetType().Name}.");
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
