using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Builds the Hub's Cuphead-style movement tutorial: three generated sprites (keycap and mouse icons) and the
/// "MovementTutorial" object in the Hub (trigger zone, texts, icons and curved arrows, all in world space).
/// Safe to run again: sprites are only generated when missing and an existing tutorial is kept with its
/// tweaks. "Rebuild Movement Tutorial" is the only command that replaces it (undoable).
/// </summary>
public static class MovementTutorialBuilder
{
    private const string SpriteFolder = "Assets/Sprites/UI/Tutorial";
    private const string HubScenePath = "Assets/Scenes/Hub.unity";
    private const string FontPath = "Assets/Renan/Fonts/Jersey10-Regular SDF 1.asset";
    private const string PlayerPrefabPath = "Assets/Renan/Prefabs/Player.prefab";
    private const string RootName = "MovementTutorial";

    // The Hub's tiles are 32 pixels per unit; the generated sprites match them.
    private const float PixelsPerUnit = 32f;

    // World-space trigger: from just after the camera swap to the grapple crossing (x 8.8) up to just before
    // the swap back (x 29.5), so the prompts stay up while the player swings, falls and retries.
    private static readonly Vector2 ZoneMin = new(9.8f, -8f);
    private static readonly Vector2 ZoneMax = new(29.2f, 8f);

    private const float TitleFontSize = 10f;
    private const float DescriptionFontSize = 5.5f;
    private const float KeyLabelFontSize = 5f;
    private const float ShadowOffset = 0.0625f;

    // Behind the tilemaps (0), the NPCs (1) and Lucy (10); the sky is the camera clear colour.
    private const int ShadowOrder = -5;
    private const int BodyOrder = -4;
    private const int FrontOrder = -3;

    private static readonly Color CreamColor = new(0.98f, 0.94f, 0.82f, 1f);
    private static readonly Color DescriptionColor = new(0.80f, 0.86f, 0.94f, 1f);
    private static readonly Color KeyLabelColor = new(0.13f, 0.19f, 0.32f, 1f);
    private static readonly Color ShadowColor = new(0.08f, 0.12f, 0.22f, 0.9f);
    private static readonly Color AccentColor = new(0.76f, 0.54f, 0.21f, 1f);
    private static readonly Color ArrowColor = new(0.98f, 0.94f, 0.82f, 0.95f);

    private enum IconKind { Key, MouseLeft, MouseRight }

    private class ArrowSpec
    {
        public Vector2 start, control, end;

        public ArrowSpec(Vector2 start, Vector2 control, Vector2 end)
        {
            this.start = start;
            this.control = control;
            this.end = end;
        }
    }

    private class PromptSpec
    {
        public string name;
        public IconKind icon;
        public string keyLabel;
        public float keyWidth;
        public string title;
        public string description;
        public Vector2 iconCenter;
        public Vector2 titleCenter;
        public Vector2 descriptionCenter;
        public ArrowSpec[] arrows;
    }

    private class Sprites
    {
        public Sprite key, mouseBody, mouseButton;
    }

    // Positions come from the layout worked out against the Hub's tiles and the Jersey 10 glyph metrics:
    // everything sits in x 12.5-28.7, y -1.3-6.4, clear of the blocks, trees and ledges, and inside the
    // grapple camera's view at 16:9 and 4:3.
    private static readonly PromptSpec[] Prompts =
    {
        new()
        {
            name = "Pular", icon = IconKind.Key, keyLabel = "ESPAÇO", keyWidth = 1.8f,
            title = "PULAR", description = "TOQUE PARA PULO CURTO\nSEGURE PARA PULO ALTO",
            iconCenter = new Vector2(14.75f, 0.90f), titleCenter = new Vector2(14.75f, 0.03f),
            descriptionCenter = new Vector2(14.75f, -0.90f),
            arrows = new[] { new ArrowSpec(new Vector2(12.55f, 0.45f), new Vector2(12.95f, 2.75f), new Vector2(15.25f, 2.5f)) }
        },
        new()
        {
            name = "Agarrar", icon = IconKind.MouseLeft,
            title = "AGARRAR", description = "SEGURE PARA SE PUXAR\nA/D PARA BALANÇAR",
            iconCenter = new Vector2(20.5f, 5.975f), titleCenter = new Vector2(20.5f, 5.08f),
            descriptionCenter = new Vector2(20.5f, 4.15f),
            arrows = new[]
            {
                new ArrowSpec(new Vector2(18.7f, 5.08f), new Vector2(16.9f, 5.3f), new Vector2(16.5f, 3.4f)),
                new ArrowSpec(new Vector2(22.3f, 5.08f), new Vector2(24.1f, 5.3f), new Vector2(24.5f, 3.4f))
            }
        },
        new()
        {
            name = "Soltar", icon = IconKind.MouseRight,
            title = "SOLTAR", description = "OU ESPAÇO\nGANHE IMPULSO PARA CIMA",
            iconCenter = new Vector2(25.3f, 1.175f), titleCenter = new Vector2(25.3f, 0.28f),
            descriptionCenter = new Vector2(25.3f, -0.65f),
            arrows = new[] { new ArrowSpec(new Vector2(25.95f, 1.2f), new Vector2(28.3f, 2.7f), new Vector2(28.7f, -1f)) }
        }
    };

    private static TMP_FontAsset _font;
    private static Material _material;

    [MenuItem("Tools/Lucy/Build Movement Tutorial")]
    public static void Build()
    {
        Run(overwrite: false);
    }

    [MenuItem("Tools/Lucy/Rebuild Movement Tutorial")]
    public static void RebuildTutorial()
    {
        Run(overwrite: true);
    }

    private static void Run(bool overwrite)
    {
        if (!OpenHubScene(out bool hubHadUnsavedChanges)) return;

        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetActiveScene();
        List<MovementTutorial> existing = FindAllInScene<MovementTutorial>(scene);
        if (existing.Count > 0 && !overwrite)
        {
            Selection.activeGameObject = existing[0].gameObject;
            Debug.Log("[Tutorial] A Hub ja tem o tutorial (mantido). Use Tools > Lucy > Rebuild Movement Tutorial para refazer.");
            return;
        }

        try
        {
            EnsureFolder(SpriteFolder);

            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (_font == null) Debug.LogWarning($"[Tutorial] Fonte nao encontrada em {FontPath}; usando a fonte padrao do TMP.");

            _material = LoadLineMaterial();
            if (_material == null) Debug.LogWarning("[Tutorial] Material dos tracos nao encontrado; configure-o manualmente.");

            Sprites sprites = BuildSprites();
            AssetDatabase.SaveAssets();
            ReportMissingGlyphs();

            foreach (MovementTutorial old in existing)
            {
                Undo.DestroyObjectImmediate(old.gameObject);
            }

            GameObject root = BuildTutorial(scene, sprites);
            Undo.RegisterCreatedObjectUndo(root, "Build Movement Tutorial");
            Selection.activeGameObject = root;

            EditorSceneManager.MarkSceneDirty(scene);
            if (hubHadUnsavedChanges)
            {
                // The scene already had unsaved edits of the user's; saving them silently is not ours to decide.
                Debug.LogWarning("[Tutorial] Tutorial criado na Hub, mas a cena ja tinha alteracoes nao salvas: salve manualmente.");
                return;
            }

            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Tutorial] Tutorial criado na Hub. Arraste os pontos Start/Control/End de cada seta para ajustar a curva.");
        }
        finally
        {
            _font = null;
            _material = null;
        }
    }

    #region Sprites

    private static Sprites BuildSprites()
    {
        return new Sprites
        {
            key = GenerateSprite("tutorial_key", 32, 32, p => Fill(RoundedBox(p, new Vector2(16f, 16f), 6f)), 8),
            mouseBody = GenerateSprite("tutorial_mouse_body", 16, 24, MouseBodyAlpha),
            mouseButton = GenerateSprite("tutorial_mouse_button", 16, 24, MouseButtonAlpha)
        };
    }

    private static float MouseBody(Vector2 p) => RoundedBox(p, new Vector2(8f, 12f), 7f);

    // The two seams are cut out, so whatever is behind (the shadow or the sky) shows through as the button lines.
    private static float MouseBodyAlpha(Vector2 p)
    {
        float seam = Mathf.Min(Segment(p, new Vector2(-9f, 2f), new Vector2(9f, 2f), 1f),
            Segment(p, new Vector2(0f, 2f), new Vector2(0f, 13f), 1f));
        return Fill(MouseBody(p)) * (1f - Fill(seam));
    }

    // The top-left button; the right button is the same sprite flipped.
    private static float MouseButtonAlpha(Vector2 p)
    {
        float region = Mathf.Max(p.x + 1f, 3f - p.y);
        return Fill(Mathf.Max(MouseBody(p), region));
    }

    /// <summary>
    /// Writes a white PNG whose alpha comes from <paramref name="alpha"/> (pixel coordinates, origin in the
    /// centre, y up) and imports it as a point-filtered sprite at the Hub tiles' scale. Existing files are left
    /// alone so final art can replace them.
    /// </summary>
    private static Sprite GenerateSprite(string fileName, int width, int height, Func<Vector2, float> alpha, int sliceBorder = 0)
    {
        string assetPath = $"{SpriteFolder}/{fileName}.png";
        if (!File.Exists(ToFullPath(assetPath)))
        {
            Texture2D texture = new(width, height, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector2 point = new(x + 0.5f - width * 0.5f, y + 0.5f - height * 0.5f);
                    byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(point)) * 255f);
                    pixels[y * width + x] = new Color32(255, 255, 255, a);
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
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.spriteBorder = new Vector4(sliceBorder, sliceBorder, sliceBorder, sliceBorder);

            // Sliced SpriteRenderers need a full-rect mesh.
            TextureImporterSettings settings = new();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
    }

    // Signed distances: negative inside, in pixels. Fill gives one pixel of antialiasing.
    private static float Fill(float distance) => Mathf.Clamp01(0.5f - distance);

    private static float RoundedBox(Vector2 p, Vector2 halfSize, float radius)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - (halfSize - Vector2.one * radius);
        Vector2 outside = new(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
        return outside.magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
    }

    private static float Segment(Vector2 p, Vector2 a, Vector2 b, float radius)
    {
        Vector2 edge = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, edge) / edge.sqrMagnitude);
        return (p - (a + edge * t)).magnitude - radius;
    }

    #endregion

    #region Tutorial

    private static GameObject BuildTutorial(UnityEngine.SceneManagement.Scene scene, Sprites sprites)
    {
        GameObject root = new(RootName);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        Vector2 zoneCenter = (ZoneMin + ZoneMax) * 0.5f;
        root.transform.position = new Vector3(zoneCenter.x, zoneCenter.y, 0f);

        // Added before MovementTutorial: its RequireComponent names the abstract Collider2D.
        BoxCollider2D zone = root.AddComponent<BoxCollider2D>();
        zone.isTrigger = true;
        zone.size = ZoneMax - ZoneMin;
        MovementTutorial tutorial = root.AddComponent<MovementTutorial>();

        GameObject content = new("Content");
        content.transform.SetParent(root.transform, false);

        foreach (PromptSpec spec in Prompts)
        {
            BuildPrompt(content.transform, spec, sprites);
        }

        SetRefs(tutorial, ("_content", content));
        return root;
    }

    private static void BuildPrompt(Transform parent, PromptSpec spec, Sprites sprites)
    {
        GameObject prompt = new(spec.name);
        prompt.transform.SetParent(parent, false);

        BuildIcon(prompt.transform, spec, sprites);
        CreateText("Title", prompt.transform, spec.title, TitleFontSize, CreamColor, spec.titleCenter, new Vector2(6f, 1.2f), FrontOrder);
        CreateText("Description", prompt.transform, spec.description, DescriptionFontSize, DescriptionColor,
            spec.descriptionCenter, new Vector2(8f, 2f), FrontOrder);

        for (int i = 0; i < spec.arrows.Length; i++)
        {
            BuildArrow(prompt.transform, i == 0 ? "Arrow" : $"Arrow ({i})", spec.arrows[i]);
        }
    }

    private static void BuildIcon(Transform parent, PromptSpec spec, Sprites sprites)
    {
        GameObject icon = new("Icon");
        icon.transform.SetParent(parent, false);
        Vector3 center = new(spec.iconCenter.x, spec.iconCenter.y, 0f);
        Vector3 shadowShift = new(0f, -ShadowOffset, 0f);

        if (spec.icon == IconKind.Key)
        {
            Vector2 size = new(spec.keyWidth, 0.7f);
            CreateSprite("Shadow", icon.transform, sprites.key, ShadowColor, ShadowOrder, center + shadowShift, size, false);
            CreateSprite("Cap", icon.transform, sprites.key, CreamColor, BodyOrder, center, size, false);
            CreateText("Label", icon.transform, spec.keyLabel, KeyLabelFontSize, KeyLabelColor, spec.iconCenter, size, FrontOrder);
            return;
        }

        CreateSprite("Shadow", icon.transform, sprites.mouseBody, ShadowColor, ShadowOrder, center + shadowShift, Vector2.zero, false);
        CreateSprite("Body", icon.transform, sprites.mouseBody, CreamColor, BodyOrder, center, Vector2.zero, false);
        CreateSprite("Button", icon.transform, sprites.mouseButton, AccentColor, FrontOrder, center, Vector2.zero,
            spec.icon == IconKind.MouseRight);
    }

    private static void BuildArrow(Transform parent, string name, ArrowSpec spec)
    {
        GameObject arrow = new(name);
        arrow.transform.SetParent(parent, false);
        arrow.transform.position = new Vector3(spec.start.x, spec.start.y, 0f);

        LineRenderer shaft = AddLine(arrow);
        GameObject headObject = new("Head");
        headObject.transform.SetParent(arrow.transform, false);
        LineRenderer head = AddLine(headObject);

        Transform start = CreateMarker("Start", arrow.transform, spec.start);
        Transform control = CreateMarker("Control", arrow.transform, spec.control);
        Transform end = CreateMarker("End", arrow.transform, spec.end);

        TutorialArrow component = arrow.AddComponent<TutorialArrow>();
        SetRefs(component, ("_start", start), ("_control", control), ("_end", end), ("_shaft", shaft), ("_head", head));
        component.Rebuild();
    }

    private static Transform CreateMarker(string name, Transform parent, Vector2 position)
    {
        GameObject marker = new(name);
        marker.transform.SetParent(parent, false);
        marker.transform.position = new Vector3(position.x, position.y, 0f);
        return marker.transform;
    }

    private static LineRenderer AddLine(GameObject target)
    {
        LineRenderer line = target.AddComponent<LineRenderer>();
        line.sharedMaterial = _material;
        line.useWorldSpace = false;
        line.alignment = LineAlignment.TransformZ;
        line.textureMode = LineTextureMode.Stretch;
        line.numCapVertices = 3;
        line.numCornerVertices = 3;
        line.startColor = ArrowColor;
        line.endColor = ArrowColor;
        line.sortingOrder = FrontOrder;
        line.positionCount = 0;
        return line;
    }

    private static void CreateSprite(string name, Transform parent, Sprite sprite, Color color, int order, Vector3 position,
        Vector2 slicedSize, bool flipX)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;

        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = color;
        renderer.sortingOrder = order;
        renderer.flipX = flipX;
        if (_material != null) renderer.sharedMaterial = _material;
        if (slicedSize != Vector2.zero)
        {
            renderer.drawMode = SpriteDrawMode.Sliced;
            renderer.size = slicedSize;
        }
    }

    // A 3D TextMeshPro (like the Hub's NextLevelText), centred on its rect. Sorting goes through the TMP
    // property: the MeshRenderer's own value is overwritten when the scene loads.
    private static void CreateText(string name, Transform parent, string text, float fontSize, Color color, Vector2 center,
        Vector2 size, int order)
    {
        GameObject go = new(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        if (_font != null) tmp.font = _font;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        tmp.sortingOrder = order;

        RectTransform rect = tmp.rectTransform;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.position = new Vector3(center.x, center.y, 0f);
    }

    private static Material LoadLineMaterial()
    {
        Material builtin = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
        if (builtin != null) return builtin;

        // The player's grapple rope already draws under this renderer, so its material is a safe fallback.
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (player == null) return null;

        LineRenderer rope = player.GetComponentInChildren<LineRenderer>(true);
        return rope != null ? rope.sharedMaterial : null;
    }

    // Jersey 10 is a dynamic font whose atlas is nearly full; every glyph used by default is already in it.
    private static void ReportMissingGlyphs()
    {
        if (_font == null) return;

        HashSet<char> missing = new();
        foreach (PromptSpec spec in Prompts)
        {
            foreach (string text in new[] { spec.title, spec.description, spec.keyLabel })
            {
                if (string.IsNullOrEmpty(text)) continue;
                foreach (char c in text)
                {
                    if (char.IsWhiteSpace(c) || _font.HasCharacter(c)) continue;
                    missing.Add(c);
                }
            }
        }

        if (missing.Count > 0)
        {
            Debug.LogWarning($"[Tutorial] Glifos fora do atlas da fonte: {string.Join(" ", missing)}. Ative 'Multi Atlas Textures' na fonte ou troque o texto.");
        }
    }

    #endregion

    #region Scene

    private static bool OpenHubScene(out bool hadUnsavedChanges)
    {
        hadUnsavedChanges = false;
        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetActiveScene();
        if (scene.path == HubScenePath)
        {
            hadUnsavedChanges = scene.isDirty;
            return true;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.LogWarning("[Tutorial] Cancelado: a cena Hub precisa estar aberta para gerar o tutorial.");
            return false;
        }

        EditorSceneManager.OpenScene(HubScenePath, OpenSceneMode.Single);
        return true;
    }

    private static List<T> FindAllInScene<T>(UnityEngine.SceneManagement.Scene scene) where T : Component
    {
        List<T> found = new();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            found.AddRange(root.GetComponentsInChildren<T>(true));
        }

        return found;
    }

    #endregion

    #region Helpers

    private static void SetRefs(Object target, params (string field, Object value)[] references)
    {
        SerializedObject so = new(target);
        foreach ((string field, Object value) in references)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[Tutorial] Campo '{field}' nao existe em {target.GetType().Name}.");
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
