using System.Collections.Generic;
using System.IO;
using HierarchicalStateMachine;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Puts the action-key portals in the scenes:
/// - Level1-3: a "DeathPortal" behind the start point (back to the Hub, only shown after a death) and the
///   boss's HubReturnTrigger turned from walk-in into press-E.
/// - Hub: the boss selector portal opens on press-E with "Escolha a fase".
/// Every portal gets a purple placeholder until its art exists.
/// Safe to run again: anything that already has an InteractPortal is left alone, so the DeathPortal can be moved
/// by hand in each level without being reset; only a missing placeholder is added.
/// </summary>
public static class PortalBuilder
{
    private static readonly string[] LevelScenePaths =
    {
        "Assets/Scenes/Level1.unity",
        "Assets/Scenes/Level2.unity",
        "Assets/Scenes/Level3.unity"
    };

    private const string HubScenePath = "Assets/Scenes/Hub.unity";
    private const string FontPath = "Assets/Renan/Fonts/Jersey10-Regular SDF 1.asset";
    private const string InputActionsPath = "Assets/InputS/PlayerInputs.inputactions";
    private const string NpcPrefabPath = "Assets/Renan/Prefabs/NPC.prefab";
    private const string InteractActionName = "Dialog/Interact";
    private const string PlaceholderSpritePath = "Assets/Sprites/Portals/portal_placeholder.png";

    private const string DeathPortalName = "DeathPortal";
    private const string BossPortalName = "HubReturnTrigger";

    private const string HubLabel = "Voltar ao hub?";
    private const string SelectorLabel = "Escolha a fase";

    // Distance from the player's start point; the portal goes on the side Lucy is not facing.
    private const float DeathPortalDistance = 2f;
    private static readonly Vector2 DeathPortalSize = new(1.5f, 3f);

    // Same world size as the NPCs' "E" box: their canvas is 0.0166 inside an NPC scaled by 5.
    private const float PromptCanvasScale = 0.083f;
    private const float PromptFontSize = 5f;
    private const float KeyBoxSize = 4f;
    private const float PromptGap = 0.5f;
    private const int PromptSortingOrder = 20;

    // Behind Lucy (10), in front of the tilemaps (0).
    private const int PlaceholderSortingOrder = 5;
    private static readonly Color PlaceholderColor = new(0.55f, 0.25f, 0.85f, 0.7f);

    private class PortalAssets
    {
        public TMP_FontAsset font;
        public InputActionReference interact;
        public Sprite keySprite;
        public Image.Type keySpriteType;
        public Sprite placeholder;
    }

    [MenuItem("Tools/Lucy/Build Portals")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.LogWarning("[Portais] Cancelado: salve ou descarte as alteracoes da cena aberta primeiro.");
            return;
        }

        string previousScene = SceneManager.GetActiveScene().path;
        PortalAssets assets = LoadAssets();
        List<string> changes = new();

        foreach (string path in LevelScenePaths)
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            BuildLevel(scene, assets, changes);
            EditorSceneManager.SaveScene(scene);
        }

        Scene hub = EditorSceneManager.OpenScene(HubScenePath, OpenSceneMode.Single);
        BuildHub(hub, assets, changes);
        EditorSceneManager.SaveScene(hub);

        if (!string.IsNullOrEmpty(previousScene) && previousScene != HubScenePath)
        {
            EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);
        }

        Debug.Log(changes.Count > 0
            ? $"[Portais] Atualizado: {string.Join(", ", changes)}."
            : "[Portais] Tudo ja estava configurado.");
    }

    #region Scenes

    private static void BuildLevel(Scene scene, PortalAssets assets, List<string> changes)
    {
        LevelRunManager runManager = FindInScene<LevelRunManager>(scene);
        if (runManager == null)
        {
            Debug.LogWarning($"[Portais] {scene.name}: sem LevelRunManager; fase ignorada.");
            return;
        }

        BuildDeathPortal(scene, runManager, assets, changes);
        ConvertBossPortal(scene, runManager, assets, changes);
        AddPlaceholders(scene, assets, changes);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static void BuildDeathPortal(Scene scene, LevelRunManager runManager, PortalAssets assets, List<string> changes)
    {
        SerializedObject managerSo = new(runManager);
        SerializedProperty deathPortalProperty = managerSo.FindProperty("_deathPortal");
        if (deathPortalProperty.objectReferenceValue != null) return;

        Vector3 position = Vector3.zero;
        PlayerStateDriver player = FindInScene<PlayerStateDriver>(scene);
        if (player != null)
        {
            float behind = player.IsFacingRight ? -1f : 1f;
            position = player.transform.position + new Vector3(behind * DeathPortalDistance, 0f, 0f);
            position.z = 0f;
        }
        else
        {
            Debug.LogWarning($"[Portais] {scene.name}: jogador nao encontrado; DeathPortal criado na origem, posicione-o.");
        }

        GameObject portal = new(DeathPortalName);
        SceneManager.MoveGameObjectToScene(portal, scene);
        portal.transform.position = position;

        BoxCollider2D collider = portal.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        collider.size = DeathPortalSize;

        InteractPortal interact = AddInteractPortal(portal, HubLabel, DeathPortalSize.y, assets);
        UnityEventTools.AddPersistentListener(GetEvent(interact), runManager.ReturnToHub);

        deathPortalProperty.objectReferenceValue = portal;
        managerSo.ApplyModifiedPropertiesWithoutUndo();

        // LevelRunManager turns it on only when the level restarts after a death.
        portal.SetActive(false);
        changes.Add($"{scene.name}: {DeathPortalName} em ({position.x:0.##}, {position.y:0.##})");
    }

    /// <summary>The portal that appears when the boss dies: walk-in CallWhenTrigger becomes press-E.</summary>
    private static void ConvertBossPortal(Scene scene, LevelRunManager runManager, PortalAssets assets, List<string> changes)
    {
        GameObject portal = FindByName(scene, BossPortalName);
        if (portal == null)
        {
            Debug.LogWarning($"[Portais] {scene.name}: '{BossPortalName}' nao encontrado; portal do chefe nao convertido.");
            return;
        }

        if (portal.GetComponent<InteractPortal>() != null) return;

        foreach (CallWhenTrigger walkIn in portal.GetComponents<CallWhenTrigger>())
        {
            Object.DestroyImmediate(walkIn);
        }

        float height = 2f;
        if (portal.TryGetComponent(out Collider2D collider))
        {
            collider.isTrigger = true;
            if (collider is BoxCollider2D box) height = box.size.y;
        }

        InteractPortal interact = AddInteractPortal(portal, HubLabel, height, assets);
        UnityEventTools.AddPersistentListener(GetEvent(interact), runManager.FinishRun);
        changes.Add($"{scene.name}: {BossPortalName} com tecla de acao");
    }

    private static void BuildHub(Scene scene, PortalAssets assets, List<string> changes)
    {
        List<BossSelectorTrigger> triggers = FindAllInScene<BossSelectorTrigger>(scene);
        if (triggers.Count == 0)
        {
            Debug.LogWarning("[Portais] Hub: nenhum BossSelectorTrigger; rode Tools > Lucy > Build Boss Selector antes.");
            return;
        }

        foreach (BossSelectorTrigger trigger in triggers)
        {
            if (trigger.GetComponent<InteractPortal>() != null) continue;

            float height = 2f;
            if (trigger.TryGetComponent(out BoxCollider2D box)) height = box.size.y;

            // BossSelectorTrigger listens to the portal from code; no UnityEvent wiring needed.
            AddInteractPortal(trigger.gameObject, SelectorLabel, height, assets);
            changes.Add($"Hub: {trigger.name} com tecla de acao");
        }

        AddPlaceholders(scene, assets, changes);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    /// <summary>Purple stand-in for the portal art; InteractPortal sizes it to Lucy and puts it on the ground.</summary>
    private static void AddPlaceholders(Scene scene, PortalAssets assets, List<string> changes)
    {
        foreach (InteractPortal portal in FindAllInScene<InteractPortal>(scene))
        {
            SerializedObject so = new(portal);
            SerializedProperty placeholderProperty = so.FindProperty("_placeholder");
            if (placeholderProperty.objectReferenceValue != null) continue;

            GameObject placeholder = new("Placeholder");
            placeholder.transform.SetParent(portal.transform, false);
            placeholder.layer = portal.gameObject.layer;

            SpriteRenderer renderer = placeholder.AddComponent<SpriteRenderer>();
            renderer.sprite = assets.placeholder;
            renderer.color = PlaceholderColor;
            renderer.sortingOrder = PlaceholderSortingOrder;

            placeholderProperty.objectReferenceValue = renderer;
            so.ApplyModifiedPropertiesWithoutUndo();
            changes.Add($"{scene.name}: placeholder em {portal.name}");
        }
    }

    #endregion

    #region Portal

    private static InteractPortal AddInteractPortal(GameObject portal, string label, float colliderHeight, PortalAssets assets)
    {
        InteractPortal interact = portal.AddComponent<InteractPortal>();
        Prompt prompt = BuildPrompt(portal.transform, label, colliderHeight, assets);

        SerializedObject so = new(interact);
        so.FindProperty("_label").stringValue = label;
        so.FindProperty("_interactAction").objectReferenceValue = assets.interact;
        so.FindProperty("_prompt").objectReferenceValue = prompt.root;
        so.FindProperty("_labelText").objectReferenceValue = prompt.label;
        so.FindProperty("_keyText").objectReferenceValue = prompt.key;
        so.FindProperty("_keyBox").objectReferenceValue = prompt.keyBox;
        so.ApplyModifiedPropertiesWithoutUndo();
        return interact;
    }

    private static UnityEvent GetEvent(InteractPortal portal)
    {
        // The event is private; reflection is how the editor reaches it for persistent listeners.
        return (UnityEvent)typeof(InteractPortal)
            .GetField("_onInteract", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .GetValue(portal);
    }

    private struct Prompt
    {
        public GameObject root;
        public TextMeshProUGUI label;
        public TextMeshProUGUI key;
        public LayoutElement keyBox;
    }

    /// <summary>World-space "label [E]" above the portal, in the NPCs' font and key box.</summary>
    private static Prompt BuildPrompt(Transform portal, string label, float colliderHeight, PortalAssets assets)
    {
        Vector3 lossy = portal.lossyScale;
        float scaleX = Mathf.Approximately(lossy.x, 0f) ? 1f : Mathf.Abs(lossy.x);
        float scaleY = Mathf.Approximately(lossy.y, 0f) ? 1f : Mathf.Abs(lossy.y);

        GameObject canvasObject = new("Prompt", typeof(RectTransform));
        canvasObject.transform.SetParent(portal, false);
        canvasObject.layer = portal.gameObject.layer;

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = PromptSortingOrder;

        RectTransform canvasRect = (RectTransform)canvasObject.transform;
        canvasRect.sizeDelta = new Vector2(80f, 10f);
        canvasRect.localScale = new Vector3(PromptCanvasScale / scaleX, PromptCanvasScale / scaleY, 1f);
        // Collider offsets are ignored on purpose: the portals are all centred boxes.
        canvasRect.localPosition = new Vector3(0f, colliderHeight * 0.5f + PromptGap / scaleY, 0f);

        GameObject container = new("Container", typeof(RectTransform));
        container.transform.SetParent(canvasObject.transform, false);
        RectTransform containerRect = (RectTransform)container.transform;
        containerRect.anchorMin = containerRect.anchorMax = new Vector2(0.5f, 0.5f);
        containerRect.pivot = new Vector2(0.5f, 0.5f);
        containerRect.sizeDelta = new Vector2(40f, KeyBoxSize);

        HorizontalLayoutGroup layout = container.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 1f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = container.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI labelText = CreateText("Label", container.transform, label, assets.font);
        labelText.alignment = TextAlignmentOptions.Midline;
        LayoutElement labelLayout = labelText.gameObject.AddComponent<LayoutElement>();
        labelLayout.preferredHeight = KeyBoxSize;

        GameObject keyObject = new("Key", typeof(RectTransform));
        keyObject.transform.SetParent(container.transform, false);
        Image keyImage = keyObject.AddComponent<Image>();
        keyImage.sprite = assets.keySprite;
        keyImage.type = assets.keySpriteType;
        LayoutElement keyBox = keyObject.AddComponent<LayoutElement>();
        keyBox.preferredWidth = KeyBoxSize;
        keyBox.preferredHeight = KeyBoxSize;

        TextMeshProUGUI keyText = CreateText("Text", keyObject.transform, "E", assets.font);
        keyText.alignment = TextAlignmentOptions.Center;
        RectTransform keyTextRect = keyText.rectTransform;
        keyTextRect.anchorMin = Vector2.zero;
        keyTextRect.anchorMax = Vector2.one;
        keyTextRect.offsetMin = Vector2.zero;
        keyTextRect.offsetMax = Vector2.zero;

        canvasObject.SetActive(false);
        return new Prompt { root = canvasObject, label = labelText, key = keyText, keyBox = keyBox };
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, string text, TMP_FontAsset font)
    {
        GameObject go = new(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = PromptFontSize;
        tmp.color = Color.white;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        return tmp;
    }

    #endregion

    #region Assets

    private static PortalAssets LoadAssets()
    {
        PortalAssets assets = new()
        {
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath),
            keySpriteType = Image.Type.Sliced
        };
        if (assets.font == null) Debug.LogWarning($"[Portais] Fonte nao encontrada em {FontPath}; usando a padrao do TMP.");

        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(InputActionsPath))
        {
            if (asset is not InputActionReference reference || reference.action == null) continue;
            if ($"{reference.action.actionMap?.name}/{reference.action.name}" != InteractActionName) continue;

            assets.interact = reference;
            break;
        }

        if (assets.interact == null)
        {
            Debug.LogWarning($"[Portais] Acao '{InteractActionName}' nao encontrada em {InputActionsPath}; configure a tecla nos portais.");
        }

        // The NPCs' key box, so the portal prompt looks the same.
        GameObject npc = AssetDatabase.LoadAssetAtPath<GameObject>(NpcPrefabPath);
        if (npc != null)
        {
            foreach (Image image in npc.GetComponentsInChildren<Image>(true))
            {
                if (image.name != "Background") continue;
                assets.keySprite = image.sprite;
                assets.keySpriteType = image.type;
                break;
            }
        }

        if (assets.keySprite == null) Debug.LogWarning("[Portais] Caixa da tecla do NPC nao encontrada; a tecla fica sem fundo.");

        assets.placeholder = LoadPlaceholderSprite();
        return assets;
    }

    /// <summary>A plain white square (one unit), tinted purple on each portal. Generated once, then left alone.</summary>
    private static Sprite LoadPlaceholderSprite()
    {
        string fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, PlaceholderSpritePath);
        if (!File.Exists(fullPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? string.Empty);

            const int size = 4;
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            File.WriteAllBytes(fullPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(PlaceholderSpritePath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(PlaceholderSpritePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = size;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PlaceholderSpritePath);
        if (sprite == null) Debug.LogWarning($"[Portais] Sprite do placeholder nao encontrado em {PlaceholderSpritePath}.");
        return sprite;
    }

    #endregion

    #region Helpers

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        List<T> found = FindAllInScene<T>(scene);
        return found.Count > 0 ? found[0] : null;
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

    private static GameObject FindByName(Scene scene, string name)
    {
        foreach (Transform transform in FindAllInScene<Transform>(scene))
        {
            if (transform.name == name) return transform.gameObject;
        }

        return null;
    }

    #endregion
}
