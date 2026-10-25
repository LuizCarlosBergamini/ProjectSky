using System.Collections.Generic;
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
/// The art is the Hub's animated portal (the "Portal" SpriteFlipbook): it is saved once as the PortalVisual prefab
/// and every level portal gets a copy; the Hub portal uses the original.
/// Safe to run again: anything that already has an InteractPortal is left alone, so the DeathPortal can be moved
/// by hand in each level without being reset; only a missing visual is added (and the old purple placeholder removed).
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
    private const string VisualPrefabPath = "Assets/Prefabs/Portals/PortalVisual.prefab";
    private const string HubVisualName = "Portal";
    private const string OldPlaceholderName = "Placeholder";

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

    // In the levels: behind Lucy (10), in front of the tilemaps and decoration (0 and below).
    private const int LevelVisualSortingOrder = 5;

    private class PortalAssets
    {
        public TMP_FontAsset font;
        public InputActionReference interact;
        public Sprite keySprite;
        public Image.Type keySpriteType;
        public GameObject visualPrefab;
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

        // The Hub first: its portal art becomes the prefab the levels use.
        Scene hub = EditorSceneManager.OpenScene(HubScenePath, OpenSceneMode.Single);
        BuildHub(hub, assets, changes);
        EditorSceneManager.SaveScene(hub);

        foreach (string path in LevelScenePaths)
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            BuildLevel(scene, assets, changes);
            EditorSceneManager.SaveScene(scene);
        }

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
        AddVisuals(scene, assets, changes);
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

        AssignHubVisual(scene, triggers, assets, changes);
        RemoveOldPlaceholders(scene, changes);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    /// <summary>
    /// The Hub's portal art already stands by the selector trigger: the trigger points at it and it stays where it
    /// was placed. It is also saved as the PortalVisual prefab (once) for the level portals.
    /// </summary>
    private static void AssignHubVisual(Scene scene, List<BossSelectorTrigger> triggers, PortalAssets assets, List<string> changes)
    {
        SpriteFlipbook hubVisual = null;
        foreach (SpriteFlipbook flipbook in FindAllInScene<SpriteFlipbook>(scene))
        {
            if (flipbook.name != HubVisualName) continue;
            hubVisual = flipbook;
            break;
        }

        if (hubVisual == null)
        {
            Debug.LogWarning($"[Portais] Hub: objeto '{HubVisualName}' com SpriteFlipbook nao encontrado; os portais ficam sem sprite.");
            return;
        }

        if (assets.visualPrefab == null)
        {
            EnsureFolder(System.IO.Path.GetDirectoryName(VisualPrefabPath)?.Replace('\\', '/'));
            GameObject copy = Object.Instantiate(hubVisual.gameObject);
            copy.name = "PortalVisual";
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            assets.visualPrefab = PrefabUtility.SaveAsPrefabAsset(copy, VisualPrefabPath);
            Object.DestroyImmediate(copy);
            changes.Add($"prefab {VisualPrefabPath}");
        }

        SpriteRenderer renderer = hubVisual.GetComponent<SpriteRenderer>();
        foreach (BossSelectorTrigger trigger in triggers)
        {
            if (!trigger.TryGetComponent(out InteractPortal portal)) continue;

            SerializedObject so = new(portal);
            SerializedProperty visual = so.FindProperty("_visual");
            if (visual.objectReferenceValue != null) continue;

            visual.objectReferenceValue = renderer;
            so.FindProperty("_snapVisualToGround").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            changes.Add($"Hub: {trigger.name} usa o sprite '{HubVisualName}'");
        }
    }

    /// <summary>Every level portal without art gets a copy of the Hub's portal, stood on the ground at runtime.</summary>
    private static void AddVisuals(Scene scene, PortalAssets assets, List<string> changes)
    {
        RemoveOldPlaceholders(scene, changes);

        if (assets.visualPrefab == null)
        {
            Debug.LogWarning($"[Portais] {scene.name}: prefab {VisualPrefabPath} nao existe; os portais ficam sem sprite.");
            return;
        }

        foreach (InteractPortal portal in FindAllInScene<InteractPortal>(scene))
        {
            SerializedObject so = new(portal);
            SerializedProperty visual = so.FindProperty("_visual");
            if (visual.objectReferenceValue != null) continue;

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(assets.visualPrefab, portal.transform);
            instance.transform.localPosition = Vector3.zero;
            SpriteRenderer renderer = instance.GetComponent<SpriteRenderer>();
            renderer.sortingOrder = LevelVisualSortingOrder;

            visual.objectReferenceValue = renderer;
            so.FindProperty("_snapVisualToGround").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            changes.Add($"{scene.name}: sprite do portal em {portal.name}");
        }
    }

    /// <summary>The purple placeholder from the previous version of this builder.</summary>
    private static void RemoveOldPlaceholders(Scene scene, List<string> changes)
    {
        foreach (InteractPortal portal in FindAllInScene<InteractPortal>(scene))
        {
            Transform placeholder = portal.transform.Find(OldPlaceholderName);
            if (placeholder == null) continue;

            Object.DestroyImmediate(placeholder.gameObject);
            changes.Add($"{scene.name}: placeholder roxo removido de {portal.name}");
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
        // Black like the NPCs' key: the box is light, a white letter disappears on it.
        keyText.color = Color.black;
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

        assets.visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPrefabPath);
        return assets;
    }

    #endregion

    #region Helpers

    private static void EnsureFolder(string path)
    {
        if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;

        string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

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
