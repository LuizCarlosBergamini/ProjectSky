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
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Generates everything the upgrade system needs so nothing has to be wired by hand:
/// placeholder sprites, node/tree data, UI prefabs, the vendor NPC variant and the Hub scene objects.
/// Safe to run again: data, sprites and the NPC are only created when missing, so edits made in the
/// Inspector are kept. "Rebuild Upgrade UI Prefabs" is the only command that overwrites anything.
/// </summary>
public static class UpgradeSystemBuilder
{
    private const string SpriteFolder = "Assets/Sprites/UI/Upgrades";
    private const string DataFolder = "Assets/ObjectData/Upgrades";
    private const string PrefabFolder = "Assets/Renan/Prefabs";
    private const string UiPrefabFolder = "Assets/Renan/Prefabs/UpgradeUI";

    private const string HubScenePath = "Assets/Scenes/Hub.unity";
    private const string NpcBasePrefabPath = "Assets/Renan/Prefabs/NPC.prefab";
    private const string WallacePrefabPath = "Assets/Renan/Prefabs/Wallace.prefab";
    private const string PlayerPrefabPath = "Assets/Renan/Prefabs/Player.prefab";
    private const string PlayerDataPath = "Assets/ScriptableObjects/PlayerData.asset";
    // One material per row of the trees, in order. The manager makes every purchase raise the price of the
    // other nodes priced in the same item, so each row escalates on its own.
    private static readonly string[] RowMaterialPaths =
    {
        "Assets/ObjectData/Items/Engrenagem.asset",
        "Assets/ObjectData/Items/Engrenagem_Ouro.asset",
        "Assets/ObjectData/Items/Emerald.asset"
    };
    private const string FontPath = "Assets/Renan/Fonts/Jersey10-Regular SDF 1.asset";
    private const string PlayerInputsPath = "Assets/InputS/PlayerInputs.inputactions";
    private const string VendorEntityPath = "Assets/ObjectData/Entity/Mecanico.asset";
    private const string VendorDialogPath = "Assets/ObjectData/Dialog/Upgrade-Mecanico.asset";

    // Input System's DefaultInputActions, the asset the working EventSystems (Menu, GameplayScene) use.
    private const string DefaultInputActionsGuid = "ca9f5fa95ffab41fb9a615ab714db018";

    private static readonly string VendorPrefabPath = $"{PrefabFolder}/UpgradeVendor.prefab";
    private static readonly string ManagerPrefabPath = $"{PrefabFolder}/UpgradeManager.prefab";
    private static readonly string CanvasPrefabPath = $"{PrefabFolder}/UpgradeCanvas.prefab";
    private static readonly string CostEntryPrefabPath = $"{UiPrefabFolder}/UpgradeCostEntry.prefab";
    private static readonly string LinePrefabPath = $"{UiPrefabFolder}/UpgradeLine.prefab";
    private static readonly string NodePrefabPath = $"{UiPrefabFolder}/UpgradeNode.prefab";
    private static readonly string TreePrefabPath = $"{UiPrefabFolder}/UpgradeTree.prefab";

    // Between Wallace (x 0.8) and the player spawn, on the same floor; Daisy's wide trigger ends near x 3.6.
    private static readonly Vector3 VendorPosition = new(5.2f, -0.76f, 0f);

    private static readonly Color DamageColor = new(0.93f, 0.27f, 0.2f, 1f);
    private static readonly Color LifeColor = new(0.36f, 0.82f, 0.3f, 1f);
    private static readonly Color SpeedColor = new(0.98f, 0.78f, 0.18f, 1f);
    private static readonly Color PanelColor = new(0.07f, 0.07f, 0.08f, 0.97f);
    private static readonly Color TextColor = new(0.92f, 0.92f, 0.92f, 1f);
    private static readonly Color MutedTextColor = new(0.6f, 0.6f, 0.63f, 1f);

    private const int UILayer = 5;

    private class Sprites
    {
        public Sprite hexFill, hexFrame, hexSelect, hexGlow;
        public Sprite circleFill, circleFrame, circleGlow;
        public Sprite iconDamage, iconLife, iconSpeed, iconLock;
        public Sprite panel, panelBorder;
    }

    private static TMP_FontAsset _font;

    [MenuItem("Tools/Lucy/Build Upgrade System")]
    public static void Build()
    {
        Run(overwriteUi: false);
    }

    [MenuItem("Tools/Lucy/Rebuild Upgrade UI Prefabs")]
    public static void RebuildUi()
    {
        Run(overwriteUi: true);
    }

    private static void Run(bool overwriteUi)
    {
        // Opened first: building prefabs creates temporary objects in the open scene, and that must neither
        // dirty some other scene the user has open nor hide whether the Hub had unsaved edits of its own.
        if (!OpenHubScene(out bool hubHadUnsavedChanges)) return;

        try
        {
            EnsureFolder(SpriteFolder);
            EnsureFolder(DataFolder);
            EnsureFolder(UiPrefabFolder);

            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            Sprites sprites = BuildSprites();

            List<Item_SO> rowMaterials = PrepareUpgradeMaterials();
            List<UpgradeTree_SO> trees = BuildData(sprites, rowMaterials);

            GameObject managerPrefab = BuildManagerPrefab(trees);
            UpgradeCanvas canvasPrefab = BuildUiPrefabs(sprites, overwriteUi);
            GameObject vendorPrefab = BuildVendorPrefab();

            AssetDatabase.SaveAssets();
            SetupHubScene(managerPrefab, canvasPrefab, vendorPrefab, hubHadUnsavedChanges);

            Debug.Log("[Upgrades] Sistema de upgrades gerado. Veja Tools > Lucy para reconstruir a UI.");
        }
        finally
        {
            _font = null;
        }
    }

    #region Sprites

    private static Sprites BuildSprites()
    {
        const float hexRadius = 60f;

        Sprites sprites = new()
        {
            hexFill = GenerateSprite("upgrade_hex_fill", 128, p => Fill(Hexagon(p, hexRadius))),
            hexFrame = GenerateSprite("upgrade_hex_frame", 128, p => Ring(Hexagon(p, hexRadius), 7f)),
            hexSelect = GenerateSprite("upgrade_hex_select", 128, p => Ring(Hexagon(p, 62f), 3f)),
            hexGlow = GenerateSprite("upgrade_hex_glow", 256, p => Glow(Hexagon(p, hexRadius), 64f)),
            circleFill = GenerateSprite("upgrade_circle_fill", 128, p => Fill(p.magnitude - 60f)),
            circleFrame = GenerateSprite("upgrade_circle_frame", 128, p => Ring(p.magnitude - 60f, 6f)),
            circleGlow = GenerateSprite("upgrade_circle_glow", 256, p => Glow(p.magnitude - 60f, 64f)),
            iconDamage = GenerateSprite("upgrade_icon_damage", 128, p => Fill(IconShape(p, SwordSdf))),
            iconLife = GenerateSprite("upgrade_icon_life", 128, p => Fill(IconShape(p, HeartSdf))),
            iconSpeed = GenerateSprite("upgrade_icon_speed", 128, p => Fill(IconShape(p, BoltSdf))),
            iconLock = GenerateSprite("upgrade_icon_lock", 128, p => Fill(IconShape(p, LockSdf))),
            panel = GenerateSprite("upgrade_panel", 64, p => Fill(RoundedBox(p, new Vector2(32f, 32f), 14f)), 20),
            panelBorder = GenerateSprite("upgrade_panel_border", 64, p => Ring(RoundedBox(p, new Vector2(32f, 32f), 14f), 2f), 20)
        };

        return sprites;
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

    /// <summary>Pointy-top hexagon, like the reference skill tree.</summary>
    private static float Hexagon(Vector2 p, float radius)
    {
        Vector2[] vertices = new Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            float angle = (90f + 60f * i) * Mathf.Deg2Rad;
            vertices[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        return Polygon(p, vertices);
    }

    private static float RoundedBox(Vector2 p, Vector2 halfSize, float radius)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - (halfSize - Vector2.one * radius);
        Vector2 outside = new(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
        return outside.magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
    }

    /// <summary>Evaluates an icon drawn in a -1..1 box, scaled to 128px with a small margin.</summary>
    private static float IconShape(Vector2 pixel, Func<Vector2, float> shape)
    {
        const float scale = 58f;
        return shape(pixel / scale) * scale;
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

    private static float HeartSdf(Vector2 p)
    {
        float left = (p - new Vector2(-0.36f, 0.3f)).magnitude - 0.44f;
        float right = (p - new Vector2(0.36f, 0.3f)).magnitude - 0.44f;
        float bottom = Polygon(p, new[] { new Vector2(-0.77f, 0.16f), new Vector2(0f, -0.88f), new Vector2(0.77f, 0.16f), new Vector2(0f, 0.4f) });
        return Mathf.Min(bottom, Mathf.Min(left, right));
    }

    private static float BoltSdf(Vector2 p)
    {
        return Polygon(p, new[]
        {
            new Vector2(0.28f, 0.98f), new Vector2(-0.5f, -0.06f), new Vector2(-0.02f, -0.06f),
            new Vector2(-0.28f, -0.98f), new Vector2(0.52f, 0.14f), new Vector2(0.06f, 0.14f)
        });
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

    private static float Box(Vector2 p, Vector2 centre, Vector2 halfSize) => RoundedBox(p - centre, halfSize, 0f);

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

    #endregion

    #region Data

    /// <summary>
    /// Loads the cost material of each row. These items only carry an animation, and the UI needs a still
    /// sprite, so the first frame of the clip is stored in itemSprite. Collectable only reads itemSprite when
    /// there is no clip, so this changes nothing in-world.
    /// </summary>
    private static List<Item_SO> PrepareUpgradeMaterials()
    {
        List<Item_SO> materials = new();
        foreach (string path in RowMaterialPaths)
        {
            Item_SO material = AssetDatabase.LoadAssetAtPath<Item_SO>(path);
            if (material == null)
            {
                Debug.LogWarning($"[Upgrades] Item de custo nao encontrado em {path}; a linha correspondente ficara sem custo.");
                materials.Add(null);
                continue;
            }

            if (material.itemSprite == null && material.itemAnimationClip != null)
            {
                foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(material.itemAnimationClip))
                {
                    ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(material.itemAnimationClip, binding);
                    if (keys.Length > 0 && keys[0].value is Sprite sprite)
                    {
                        Undo.RecordObject(material, "Set upgrade material sprite");
                        material.itemSprite = sprite;
                        EditorUtility.SetDirty(material);
                        break;
                    }
                }
            }

            materials.Add(material);
        }

        return materials;
    }

    private class NodeSpec
    {
        public string id;
        public string name;
        public string description;
        public float bonus;
    }

    private static List<UpgradeTree_SO> BuildData(Sprites sprites, List<Item_SO> rowMaterials)
    {
        float baseHealth = 100f;
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        PlayerStateDriver driver = playerPrefab != null ? playerPrefab.GetComponent<PlayerStateDriver>() : null;
        if (driver != null) baseHealth = new SerializedObject(driver).FindProperty("maxHealth").floatValue;

        PlayerData playerData = AssetDatabase.LoadAssetAtPath<PlayerData>(PlayerDataPath);
        float baseSpeed = playerData != null ? playerData.runMaxSpeed : 9.5f;

        // Life: ~10% / 25% / 40% of base health. Speed: ~5% / 8% / 13% of base run speed, in 0.25 steps,
        // small enough that jumps and grapple gaps tuned around the base speed stay reachable.
        float Health(float fraction) => Mathf.Max(1f, Mathf.Round(baseHealth * fraction));
        float Speed(float fraction) => Mathf.Max(0.25f, Mathf.Round(baseSpeed * fraction * 4f) / 4f);

        return new List<UpgradeTree_SO>
        {
            BuildTree("Dano", "Dano", DamageColor, sprites.iconDamage, UpgradeStat.Damage, rowMaterials, new[]
            {
                new NodeSpec { id = "dano-1", name = "Golpe Reforçado", description = "Lucy bate com mais força. Aumenta o dano de todos os ataques.", bonus = 5f },
                new NodeSpec { id = "dano-2", name = "Lâmina Estelar", description = "Uma liga forjada com engrenagens de ouro deixa cada golpe mais afiado.", bonus = 15f },
                new NodeSpec { id = "dano-3", name = "Fúria Cósmica", description = "O ápice do combate: todos os ataques atingem com força devastadora.", bonus = 25f }
            }),
            BuildTree("Vida", "Vida", LifeColor, sprites.iconLife, UpgradeStat.MaxHealth, rowMaterials, new[]
            {
                new NodeSpec { id = "vida-1", name = "Carcaça Reforçada", description = "Placas extras protegem Lucy. Aumenta a vida máxima.", bonus = Health(0.10f) },
                new NodeSpec { id = "vida-2", name = "Núcleo Vital", description = "Um núcleo de energia mais estável aguenta muito mais dano.", bonus = Health(0.25f) },
                new NodeSpec { id = "vida-3", name = "Coração de Aço", description = "Resistência lendária. Grande aumento de vida máxima.", bonus = Health(0.40f) }
            }),
            BuildTree("Velocidade", "Velocidade", SpeedColor, sprites.iconSpeed, UpgradeStat.MoveSpeed, rowMaterials, new[]
            {
                new NodeSpec { id = "velocidade-1", name = "Passo Leve", description = "Juntas lubrificadas deixam Lucy mais ágil. Aumenta a velocidade de corrida.", bonus = Speed(0.05f) },
                new NodeSpec { id = "velocidade-2", name = "Propulsores", description = "Pequenos propulsores nas botas aceleram cada passada.", bonus = Speed(0.08f) },
                new NodeSpec { id = "velocidade-3", name = "Corrida Cometa", description = "Lucy corre como um cometa cruzando o céu.", bonus = Speed(0.13f) }
            })
        };
    }

    private static UpgradeTree_SO BuildTree(string folderName, string treeName, Color color, Sprite icon,
        UpgradeStat stat, List<Item_SO> rowMaterials, NodeSpec[] specs)
    {
        string treeFolder = $"{DataFolder}/{folderName}";
        EnsureFolder(treeFolder);

        string treePath = $"{DataFolder}/Arvore_{folderName}.asset";
        UpgradeTree_SO tree = AssetDatabase.LoadAssetAtPath<UpgradeTree_SO>(treePath);
        if (tree != null) return tree; // already generated: whatever the designer changed wins

        tree = ScriptableObject.CreateInstance<UpgradeTree_SO>();
        tree.treeName = treeName;
        tree.treeColor = color;
        tree.treeIcon = icon;

        UpgradeNode_SO previous = null;
        for (int i = 0; i < specs.Length; i++)
        {
            NodeSpec spec = specs[i];
            string nodePath = $"{treeFolder}/{folderName}_{i + 1}.asset";
            UpgradeNode_SO node = AssetDatabase.LoadAssetAtPath<UpgradeNode_SO>(nodePath);
            if (node == null)
            {
                node = ScriptableObject.CreateInstance<UpgradeNode_SO>();
                node.nodeId = spec.id;
                node.nodeName = spec.name;
                node.nodeDescription = spec.description;
                node.nodeIcon = icon;
                node.stat = stat;
                node.bonusValue = spec.bonus;

                // Base price is always 1 of the row's material; the manager adds the surcharge for
                // upgrades already bought in that row.
                Item_SO material = rowMaterials.Count > 0
                    ? rowMaterials[Mathf.Min(i, rowMaterials.Count - 1)]
                    : null;
                if (material != null) node.cost.Add(new InventorySlot { item = material, quantity = 1 });

                if (previous != null) node.prerequisites.Add(previous);
                AssetDatabase.CreateAsset(node, nodePath);
            }

            tree.nodes.Add(node);
            previous = node;
        }

        AssetDatabase.CreateAsset(tree, treePath);
        return tree;
    }

    private static GameObject BuildManagerPrefab(List<UpgradeTree_SO> trees)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
        if (existing != null) return existing;

        GameObject root = new("UpgradeManager");
        UpgradeManager manager = root.AddComponent<UpgradeManager>();
        SerializedObject so = new(manager);
        SerializedProperty list = so.FindProperty("_trees");
        list.arraySize = trees.Count;
        for (int i = 0; i < trees.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = trees[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ManagerPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    #endregion

    #region UI Prefabs

    private static UpgradeCanvas BuildUiPrefabs(Sprites sprites, bool overwrite)
    {
        UpgradeCostEntryUI costEntry = LoadOrBuild(CostEntryPrefabPath, overwrite, BuildCostEntry);
        Image line = LoadOrBuild(LinePrefabPath, overwrite, BuildLine);
        UpgradeNodeUI node = LoadOrBuild(NodePrefabPath, overwrite, () => BuildNode(sprites, costEntry));
        UpgradeTreeUI tree = LoadOrBuild(TreePrefabPath, overwrite, () => BuildTreeColumn(sprites, node, line));
        return LoadOrBuild(CanvasPrefabPath, overwrite, () => BuildCanvas(sprites, tree, costEntry));
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

    private static UpgradeCostEntryUI BuildCostEntry()
    {
        RectTransform root = CreateRect("UpgradeCostEntry", null);
        root.sizeDelta = new Vector2(80f, 26f);
        HorizontalLayoutGroup layout = root.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 5f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;

        Image icon = CreateImage("Icon", root, null, Color.white);
        LayoutElement iconLayout = icon.gameObject.AddComponent<LayoutElement>();
        iconLayout.preferredWidth = iconLayout.preferredHeight = 24f;
        iconLayout.minWidth = iconLayout.minHeight = 24f;

        TextMeshProUGUI label = CreateText("Label", root, "0", 22f, TextColor, TextAlignmentOptions.MidlineLeft);

        UpgradeCostEntryUI entry = root.gameObject.AddComponent<UpgradeCostEntryUI>();
        SetRefs(entry, ("_icon", icon), ("_label", label));
        return entry;
    }

    private static Image BuildLine()
    {
        RectTransform root = CreateRect("UpgradeLine", null);
        root.sizeDelta = new Vector2(100f, 6f);
        return AddImage(root, null, Color.white);
    }

    private static UpgradeNodeUI BuildNode(Sprites sprites, UpgradeCostEntryUI costEntryPrefab)
    {
        RectTransform root = CreateRect("UpgradeNode", null);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(88f, 88f);

        RectTransform visual = CreateRect("Visual", root);
        Stretch(visual);

        Image glow = CreateImage("Glow", visual, sprites.hexGlow, Color.clear, size: new Vector2(176f, 176f));
        Image selection = CreateImage("Selection", visual, sprites.hexSelect, Color.white, size: new Vector2(106f, 106f));
        Image fill = CreateImage("Fill", visual, sprites.hexFill, new Color(0.08f, 0.08f, 0.09f, 1f), raycast: true);
        Stretch(fill.rectTransform);
        Image frame = CreateImage("Frame", visual, sprites.hexFrame, Color.white);
        Stretch(frame.rectTransform);
        Image icon = CreateImage("Icon", visual, sprites.iconDamage, Color.white, size: new Vector2(44f, 44f));
        Image lockIcon = CreateImage("Lock", visual, sprites.iconLock, new Color(0.75f, 0.75f, 0.78f, 1f), size: new Vector2(26f, 26f));
        lockIcon.rectTransform.anchoredPosition = new Vector2(26f, -24f);

        RectTransform cost = CreateRect("Cost", root);
        cost.anchorMin = cost.anchorMax = new Vector2(0.5f, 0f);
        cost.pivot = new Vector2(0.5f, 1f);
        cost.anchoredPosition = new Vector2(0f, -2f);
        AddImage(cost, sprites.panel, new Color(0f, 0f, 0f, 0.7f), Image.Type.Sliced);
        HorizontalLayoutGroup costLayout = cost.gameObject.AddComponent<HorizontalLayoutGroup>();
        costLayout.padding = new RectOffset(8, 8, 1, 1);
        costLayout.spacing = 6f;
        costLayout.childAlignment = TextAnchor.MiddleCenter;
        costLayout.childControlWidth = costLayout.childControlHeight = true;
        costLayout.childForceExpandWidth = costLayout.childForceExpandHeight = false;
        ContentSizeFitter costFitter = cost.gameObject.AddComponent<ContentSizeFitter>();
        costFitter.horizontalFit = costFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Button button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = fill;

        UpgradeNodeUI node = root.gameObject.AddComponent<UpgradeNodeUI>();
        SetRefs(node,
            ("_button", button), ("_glow", glow), ("_selection", selection), ("_fill", fill), ("_frame", frame),
            ("_icon", icon), ("_lock", lockIcon), ("_visual", visual), ("_costContainer", cost),
            ("_costEntryPrefab", costEntryPrefab));
        return node;
    }

    private static UpgradeTreeUI BuildTreeColumn(Sprites sprites, UpgradeNodeUI nodePrefab, Image linePrefab)
    {
        RectTransform root = CreateRect("UpgradeTree", null);
        root.sizeDelta = new Vector2(270f, 540f);
        LayoutElement layout = root.gameObject.AddComponent<LayoutElement>();
        layout.flexibleWidth = 1f;
        layout.minWidth = 220f;

        Image background = AddImage(root, sprites.panel, PanelColor, Image.Type.Sliced);
        Image border = CreateImage("Border", root, sprites.panelBorder, new Color(1f, 1f, 1f, 0.08f), type: Image.Type.Sliced);
        Stretch(border.rectTransform);

        RectTransform header = CreateRect("Header", root);
        AnchorTop(header, 0f, 140f);

        RectTransform iconGroup = CreateRect("HeaderIcon", header);
        iconGroup.anchorMin = iconGroup.anchorMax = new Vector2(0.5f, 1f);
        iconGroup.sizeDelta = new Vector2(72f, 72f);
        iconGroup.anchoredPosition = new Vector2(0f, -48f);
        Image headerGlow = CreateImage("Glow", iconGroup, sprites.circleGlow, Color.white, size: new Vector2(150f, 150f));
        Image headerFill = CreateImage("Fill", iconGroup, sprites.circleFill, new Color(0.06f, 0.06f, 0.07f, 1f));
        Stretch(headerFill.rectTransform);
        Image headerFrame = CreateImage("Frame", iconGroup, sprites.circleFrame, Color.white);
        Stretch(headerFrame.rectTransform);
        Image headerIcon = CreateImage("Icon", iconGroup, sprites.iconDamage, Color.white, size: new Vector2(40f, 40f));

        TextMeshProUGUI nameText = CreateText("Name", header, "Arvore", 32f, TextColor, TextAlignmentOptions.Center);
        AnchorTop(nameText.rectTransform, 88f, 32f);
        TextMeshProUGUI bonusText = CreateText("Bonus", header, "+0", 22f, MutedTextColor, TextAlignmentOptions.Center);
        AnchorTop(bonusText.rectTransform, 118f, 22f);

        Image divider = CreateImage("Divider", header, null, new Color(1f, 1f, 1f, 0.08f));
        AnchorTop(divider.rectTransform, 140f, 2f, 16f);

        // Three rows (52 + 2 x 126 + 84 = 388) fit the 388px left under the header without scrolling;
        // longer trees scroll.
        RectTransform scroll = CreateRect("Scroll", root);
        Stretch(scroll, 0f, 0f, 144f, 6f);
        ScrollRect scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30f;

        RectTransform viewport = CreateRect("Viewport", scroll);
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        AddImage(viewport, null, Color.clear, raycast: true); // lets the empty area drag and scroll

        RectTransform content = CreateRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = new Vector2(0f, 380f);

        RectTransform lines = CreateRect("Lines", content);
        Stretch(lines);
        RectTransform nodes = CreateRect("Nodes", content);
        Stretch(nodes);

        scrollRect.viewport = viewport;
        scrollRect.content = content;

        UpgradeTreeUI tree = root.gameObject.AddComponent<UpgradeTreeUI>();
        SetRefs(tree,
            ("_background", background), ("_headerGlow", headerGlow), ("_headerFrame", headerFrame),
            ("_headerIcon", headerIcon), ("_nameText", nameText), ("_bonusText", bonusText),
            ("_content", content), ("_lineContainer", lines), ("_nodeContainer", nodes),
            ("_nodePrefab", nodePrefab), ("_linePrefab", linePrefab));
        return tree;
    }

    private static UpgradeCanvas BuildCanvas(Sprites sprites, UpgradeTreeUI treePrefab, UpgradeCostEntryUI costEntryPrefab)
    {
        RectTransform canvasRect = CreateRect("UpgradeCanvas", null);
        Canvas canvas = canvasRect.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50; // above the dialog and task canvases (0)

        // Same reference resolution as the other canvases. Expand (instead of match width) keeps the whole
        // 1280x720 layout on screen at 4:3 and ultrawide too; extra space just widens the trees.
        CanvasScaler scaler = canvasRect.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        canvasRect.gameObject.AddComponent<GraphicRaycaster>();

        RectTransform root = CreateRect("Root", canvasRect);
        Stretch(root);

        Image dim = CreateImage("Dim", root, null, new Color(0f, 0f, 0f, 0.8f), raycast: true);
        Stretch(dim.rectTransform);

        Image frame = CreateImage("Frame", root, sprites.panel, new Color(0.035f, 0.035f, 0.04f, 0.98f), type: Image.Type.Sliced, raycast: true);
        Stretch(frame.rectTransform, 24f, 24f, 24f, 24f);
        Image frameBorder = CreateImage("Border", frame.rectTransform, sprites.panelBorder, new Color(1f, 1f, 1f, 0.12f), type: Image.Type.Sliced);
        Stretch(frameBorder.rectTransform);

        // Title bar
        RectTransform titleBar = CreateRect("TitleBar", frame.rectTransform);
        AnchorTop(titleBar, 0f, 68f);
        TextMeshProUGUI title = CreateText("Title", titleBar, "MELHORIAS", 44f, TextColor, TextAlignmentOptions.MidlineLeft);
        Stretch(title.rectTransform, 28f, 400f, 0f, 0f);

        RectTransform materials = CreateRect("Materials", titleBar);
        materials.anchorMin = materials.anchorMax = new Vector2(1f, 0.5f);
        materials.pivot = new Vector2(1f, 0.5f);
        materials.anchoredPosition = new Vector2(-84f, 0f);
        HorizontalLayoutGroup materialsLayout = materials.gameObject.AddComponent<HorizontalLayoutGroup>();
        materialsLayout.spacing = 20f;
        materialsLayout.childAlignment = TextAnchor.MiddleRight;
        materialsLayout.childControlWidth = materialsLayout.childControlHeight = true;
        materialsLayout.childForceExpandWidth = materialsLayout.childForceExpandHeight = false;
        ContentSizeFitter materialsFitter = materials.gameObject.AddComponent<ContentSizeFitter>();
        materialsFitter.horizontalFit = materialsFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Button closeButton = CreateButton("CloseButton", titleBar, sprites.panel, new Color(0.18f, 0.18f, 0.2f, 1f), "X", 30f, TextColor, out _, out _);
        RectTransform closeRect = (RectTransform)closeButton.transform;
        closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 0.5f);
        closeRect.pivot = new Vector2(1f, 0.5f);
        closeRect.sizeDelta = new Vector2(46f, 46f);
        closeRect.anchoredPosition = new Vector2(-18f, 0f);

        Image titleDivider = CreateImage("Divider", frame.rectTransform, null, new Color(1f, 1f, 1f, 0.1f));
        AnchorTop(titleDivider.rectTransform, 68f, 2f, 20f);

        // Body: trees on the left, details on the right
        RectTransform body = CreateRect("Body", frame.rectTransform);
        Stretch(body, 20f, 20f, 82f, 52f);

        RectTransform trees = CreateRect("Trees", body);
        Stretch(trees, 0f, 360f, 0f, 0f);
        HorizontalLayoutGroup treesLayout = trees.gameObject.AddComponent<HorizontalLayoutGroup>();
        treesLayout.spacing = 14f;
        treesLayout.childControlWidth = treesLayout.childControlHeight = true;
        treesLayout.childForceExpandWidth = treesLayout.childForceExpandHeight = true;

        UpgradeDetailPanel detail = BuildDetailPanel(sprites, body, costEntryPrefab);

        // Footer
        RectTransform footer = CreateRect("Footer", frame.rectTransform);
        footer.anchorMin = new Vector2(0f, 0f);
        footer.anchorMax = new Vector2(1f, 0f);
        footer.pivot = new Vector2(0.5f, 0f);
        footer.sizeDelta = new Vector2(0f, 50f);
        TextMeshProUGUI stats = CreateText("Stats", footer, "Vida 100/100     Dano 10     Velocidade 9.5", 24f, MutedTextColor, TextAlignmentOptions.MidlineLeft);
        Stretch(stats.rectTransform, 28f, 300f, 0f, 0f);

        InputActionReference closeAction = FindActionReference(PlayerInputsPath, "InGame", "Pause");
        string closeKey = closeAction != null && closeAction.action.bindings.Count > 0
            ? InputControlPath.ToHumanReadableString(closeAction.action.bindings[0].effectivePath, InputControlPath.HumanReadableStringOptions.OmitDevice)
            : "Esc";
        TextMeshProUGUI hint = CreateText("Hint", footer, $"[{closeKey}] Fechar", 22f, MutedTextColor, TextAlignmentOptions.MidlineRight);
        Stretch(hint.rectTransform, 300f, 28f, 0f, 0f);

        UpgradeCanvas upgradeCanvas = canvasRect.gameObject.AddComponent<UpgradeCanvas>();
        SetRefs(upgradeCanvas,
            ("_root", root.gameObject), ("_treeContainer", trees), ("_treePrefab", treePrefab),
            ("_detailPanel", detail), ("_materialContainer", materials), ("_materialEntryPrefab", costEntryPrefab),
            ("_statsText", stats), ("_closeButton", closeButton), ("_closeAction", closeAction));

        root.gameObject.SetActive(false);
        return upgradeCanvas;
    }

    private static UpgradeDetailPanel BuildDetailPanel(Sprites sprites, RectTransform parent, UpgradeCostEntryUI costEntryPrefab)
    {
        RectTransform panel = CreateRect("Details", parent);
        panel.anchorMin = new Vector2(1f, 0f);
        panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 0.5f);
        panel.sizeDelta = new Vector2(346f, 0f);
        AddImage(panel, sprites.panel, PanelColor, Image.Type.Sliced);
        Image border = CreateImage("Border", panel, sprites.panelBorder, new Color(1f, 1f, 1f, 0.08f), type: Image.Type.Sliced);
        Stretch(border.rectTransform);

        TextMeshProUGUI empty = CreateText("Empty", panel, "Passe o mouse sobre uma melhoria", 22f, MutedTextColor, TextAlignmentOptions.Center);
        Stretch(empty.rectTransform, 24f, 24f, 0f, 0f);
        empty.textWrappingMode = TextWrappingModes.Normal;

        RectTransform content = CreateRect("Content", panel);
        Stretch(content, 18f, 18f, 0f, 0f);

        RectTransform iconGroup = CreateRect("Icon", content);
        iconGroup.anchorMin = iconGroup.anchorMax = new Vector2(0.5f, 1f);
        iconGroup.sizeDelta = new Vector2(84f, 84f);
        iconGroup.anchoredPosition = new Vector2(0f, -58f);
        Image iconFill = CreateImage("Fill", iconGroup, sprites.hexFill, Color.white);
        Stretch(iconFill.rectTransform);
        Image iconFrame = CreateImage("Frame", iconGroup, sprites.hexFrame, Color.white);
        Stretch(iconFrame.rectTransform);
        Image icon = CreateImage("Icon", iconGroup, sprites.iconDamage, Color.white, size: new Vector2(42f, 42f));

        TextMeshProUGUI treeText = CreateText("Tree", content, "DANO", 20f, TextColor, TextAlignmentOptions.Center);
        AnchorTop(treeText.rectTransform, 106f, 22f);
        // Plain overflow on purpose: with Ellipsis, TMP hides the whole line when Jersey10's line height
        // is taller than the rect, and the name silently disappears.
        TextMeshProUGUI nameText = CreateText("Name", content, "Nome", 34f, TextColor, TextAlignmentOptions.Center);
        AnchorTop(nameText.rectTransform, 128f, 38f);
        TextMeshProUGUI bonusText = CreateText("Bonus", content, "+5 Dano", 28f, TextColor, TextAlignmentOptions.Center);
        AnchorTop(bonusText.rectTransform, 166f, 30f);
        TextMeshProUGUI description = CreateText("Description", content, "Descricao", 22f, TextColor, TextAlignmentOptions.Top);
        AnchorTop(description.rectTransform, 200f, 78f);
        description.textWrappingMode = TextWrappingModes.Normal;
        TextMeshProUGUI status = CreateText("Status", content, "Disponível", 22f, TextColor, TextAlignmentOptions.Center);
        AnchorTop(status.rectTransform, 282f, 26f);
        status.textWrappingMode = TextWrappingModes.Normal;

        Image costDivider = CreateImage("Divider", content, null, new Color(1f, 1f, 1f, 0.08f));
        AnchorTop(costDivider.rectTransform, 316f, 2f, 10f);
        TextMeshProUGUI costHeader = CreateText("CostHeader", content, "CUSTO", 19f, MutedTextColor, TextAlignmentOptions.Center);
        AnchorTop(costHeader.rectTransform, 324f, 22f);

        RectTransform costs = CreateRect("Costs", content);
        AnchorTop(costs, 350f, 0f);
        VerticalLayoutGroup costsLayout = costs.gameObject.AddComponent<VerticalLayoutGroup>();
        costsLayout.spacing = 4f;
        costsLayout.childAlignment = TextAnchor.UpperCenter;
        costsLayout.childControlWidth = costsLayout.childControlHeight = true;
        costsLayout.childForceExpandWidth = false;
        costsLayout.childForceExpandHeight = false;
        ContentSizeFitter costsFitter = costs.gameObject.AddComponent<ContentSizeFitter>();
        costsFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI feedback = CreateText("Feedback", content, "", 22f, TextColor, TextAlignmentOptions.Center);
        feedback.rectTransform.anchorMin = new Vector2(0f, 0f);
        feedback.rectTransform.anchorMax = new Vector2(1f, 0f);
        feedback.rectTransform.pivot = new Vector2(0.5f, 0f);
        feedback.rectTransform.sizeDelta = new Vector2(0f, 26f);
        feedback.rectTransform.anchoredPosition = new Vector2(0f, 76f);

        Button purchase = CreateButton("PurchaseButton", content, sprites.panel, DamageColor, "COMPRAR", 32f, Color.black, out Image purchaseImage, out TextMeshProUGUI purchaseLabel);
        RectTransform purchaseRect = (RectTransform)purchase.transform;
        purchaseRect.anchorMin = new Vector2(0f, 0f);
        purchaseRect.anchorMax = new Vector2(1f, 0f);
        purchaseRect.pivot = new Vector2(0.5f, 0f);
        purchaseRect.sizeDelta = new Vector2(0f, 54f);
        purchaseRect.anchoredPosition = new Vector2(0f, 16f);

        UpgradeDetailPanel detail = panel.gameObject.AddComponent<UpgradeDetailPanel>();
        SetRefs(detail,
            ("_emptyState", empty.gameObject), ("_content", content.gameObject), ("_iconFill", iconFill),
            ("_iconFrame", iconFrame), ("_icon", icon), ("_treeText", treeText), ("_nameText", nameText),
            ("_descriptionText", description), ("_bonusText", bonusText), ("_statusText", status),
            ("_costHeaderText", costHeader), ("_costContainer", costs), ("_costEntryPrefab", costEntryPrefab), ("_purchaseButton", purchase),
            ("_purchaseButtonImage", purchaseImage), ("_purchaseLabel", purchaseLabel), ("_feedbackText", feedback));
        return detail;
    }

    #endregion

    #region Vendor NPC

    /// <summary>
    /// Variant of the base NPC prefab, so later changes to NPC.prefab reach it. Wallace and Daisy are not
    /// variants themselves, so the pieces they add on top of the base (Animator, prompt height) are copied here.
    /// </summary>
    private static GameObject BuildVendorPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(VendorPrefabPath);
        if (existing != null) return existing;

        GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NpcBasePrefabPath);
        GameObject wallace = AssetDatabase.LoadAssetAtPath<GameObject>(WallacePrefabPath);
        if (basePrefab == null || wallace == null)
        {
            Debug.LogError($"[Upgrades] {NpcBasePrefabPath} ou {WallacePrefabPath} nao encontrado; NPC vendedor nao criado.");
            return null;
        }

        NPC wallaceNpc = wallace.GetComponent<NPC>();
        Entity_SO wallaceEntity = new SerializedObject(wallaceNpc).FindProperty("_entity").objectReferenceValue as Entity_SO;
        Entity_SO entity = BuildVendorEntity(wallaceEntity);
        Dialog_SO dialog = BuildVendorDialog(entity);

        GameObject vendor = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        vendor.name = "UpgradeVendor";
        try
        {
            SpriteRenderer wallaceRenderer = wallace.GetComponent<SpriteRenderer>();
            SpriteRenderer renderer = vendor.GetComponent<SpriteRenderer>();
            renderer.sprite = wallaceRenderer.sprite;
            renderer.sharedMaterial = wallaceRenderer.sharedMaterial;
            // Placeholder look: Wallace's art with a warm tint until the vendor has its own sprites.
            renderer.color = new Color(1f, 0.82f, 0.5f, 1f);

            Animator wallaceAnimator = wallace.GetComponent<Animator>();
            Animator animator = vendor.AddComponent<Animator>();
            animator.runtimeAnimatorController = wallaceAnimator.runtimeAnimatorController;
            animator.updateMode = wallaceAnimator.updateMode;
            animator.cullingMode = wallaceAnimator.cullingMode;

            BoxCollider2D wallaceCollider = wallace.GetComponent<BoxCollider2D>();
            BoxCollider2D collider = vendor.GetComponent<BoxCollider2D>();
            collider.offset = wallaceCollider.offset;
            collider.size = wallaceCollider.size;

            Transform wallaceContainer = wallace.transform.Find("Canvas/Container");
            Transform container = vendor.transform.Find("Canvas/Container");
            if (wallaceContainer != null && container != null) container.localPosition = wallaceContainer.localPosition;

            SerializedObject npc = new(vendor.GetComponent<NPC>());
            npc.FindProperty("_entity").objectReferenceValue = entity;
            npc.FindProperty("_dialog").objectReferenceValue = dialog;
            npc.ApplyModifiedPropertiesWithoutUndo();

            vendor.AddComponent<UpgradeVendorNPC>();

            return PrefabUtility.SaveAsPrefabAsset(vendor, VendorPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(vendor);
        }
    }

    private static Entity_SO BuildVendorEntity(Entity_SO lookAlike)
    {
        Entity_SO entity = AssetDatabase.LoadAssetAtPath<Entity_SO>(VendorEntityPath);
        if (entity != null) return entity;

        entity = ScriptableObject.CreateInstance<Entity_SO>();
        entity.entityName = "Mecânico";
        if (lookAlike != null)
        {
            entity.entitySprite = lookAlike.entitySprite;
            entity.enitityAnimation = lookAlike.enitityAnimation;
        }

        AssetDatabase.CreateAsset(entity, VendorEntityPath);
        return entity;
    }

    private static Dialog_SO BuildVendorDialog(Entity_SO entity)
    {
        Dialog_SO dialog = AssetDatabase.LoadAssetAtPath<Dialog_SO>(VendorDialogPath);
        if (dialog != null) return dialog;

        dialog = ScriptableObject.CreateInstance<Dialog_SO>();
        dialog.dialogs = new List<DialogItem>
        {
            new()
            {
                dialogId = "mecanico-01",
                dialogContent = "Ei, Lucy! Trouxe engrenagens de ouro? Com elas eu consigo melhorar seu equipamento.",
                nextDialog = "",
                triggerEvent = "",
                dialogDuration = 0,
                dialogOptions = new List<DialogOption>(),
                dialogEntity = entity
            }
        };

        AssetDatabase.CreateAsset(dialog, VendorDialogPath);
        return dialog;
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
            Debug.LogWarning("[Upgrades] Cancelado: a cena Hub precisa estar aberta para gerar o sistema.");
            return false;
        }

        EditorSceneManager.OpenScene(HubScenePath, OpenSceneMode.Single);
        return true;
    }

    private static void SetupHubScene(GameObject managerPrefab, UpgradeCanvas canvasPrefab, GameObject vendorPrefab,
        bool hadUnsavedChanges)
    {
        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetActiveScene();
        List<string> added = new();

        if (FindInScene<UpgradeManager>(scene) == null && managerPrefab != null)
        {
            GameObject manager = (GameObject)PrefabUtility.InstantiatePrefab(managerPrefab, scene);
            Undo.RegisterCreatedObjectUndo(manager, "Add UpgradeManager");
            added.Add(manager.name);
        }

        UpgradeCanvas canvas = FindInScene<UpgradeCanvas>(scene);
        if (canvas == null && canvasPrefab != null)
        {
            GameObject canvasObject = (GameObject)PrefabUtility.InstantiatePrefab(canvasPrefab.gameObject, scene);
            Undo.RegisterCreatedObjectUndo(canvasObject, "Add UpgradeCanvas");
            canvas = canvasObject.GetComponent<UpgradeCanvas>();
            added.Add(canvasObject.name);
        }

        if (canvas != null)
        {
            SerializedObject canvasSo = new(canvas);
            SerializedProperty player = canvasSo.FindProperty("_player");
            if (player.objectReferenceValue == null)
            {
                player.objectReferenceValue = FindInScene<PlayerStateDriver>(scene);
                canvasSo.ApplyModifiedProperties();
            }
        }

        // The Hub had no EventSystem, so nothing on a screen-space canvas could be clicked.
        if (FindInScene<EventSystem>(scene) == null)
        {
            GameObject eventSystem = CreateEventSystem();
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(eventSystem, scene);
            Undo.RegisterCreatedObjectUndo(eventSystem, "Add EventSystem");
            added.Add(eventSystem.name);
        }

        UpgradeVendorNPC vendor = FindInScene<UpgradeVendorNPC>(scene);
        if (vendor == null && vendorPrefab != null)
        {
            GameObject vendorObject = (GameObject)PrefabUtility.InstantiatePrefab(vendorPrefab, scene);
            vendorObject.transform.position = VendorPosition;
            Undo.RegisterCreatedObjectUndo(vendorObject, "Add UpgradeVendor");
            vendor = vendorObject.GetComponent<UpgradeVendorNPC>();
            added.Add($"{vendorObject.name} em {VendorPosition}");
        }

        if (vendor != null && canvas != null)
        {
            SerializedObject vendorSo = new(vendor);
            SerializedProperty canvasRef = vendorSo.FindProperty("_upgradeCanvas");
            if (canvasRef.objectReferenceValue == null)
            {
                canvasRef.objectReferenceValue = canvas;
                vendorSo.ApplyModifiedProperties();
            }
        }

        if (!scene.isDirty) return;

        if (hadUnsavedChanges)
        {
            // The scene already had unsaved edits of the user's; saving them silently is not ours to decide.
            Debug.LogWarning($"[Upgrades] Hub configurada ({string.Join(", ", added)}), mas a cena ja tinha alteracoes nao salvas: salve manualmente.");
            return;
        }

        EditorSceneManager.SaveScene(scene);
        Debug.Log(added.Count > 0
            ? $"[Upgrades] Hub atualizada: {string.Join(", ", added)}."
            : "[Upgrades] Hub atualizada (referencias).");
    }

    private static GameObject CreateEventSystem()
    {
        GameObject go = new("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        InputSystemUIInputModule module = go.GetComponent<InputSystemUIInputModule>();

        // AssignDefaultActions() would create runtime-only references that are lost on save, so the persistent
        // sub-assets of the package's DefaultInputActions are assigned instead.
        string path = AssetDatabase.GUIDToAssetPath(DefaultInputActionsGuid);
        InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
        if (asset == null)
        {
            Debug.LogWarning("[Upgrades] DefaultInputActions nao encontrado; configure o InputSystemUIInputModule manualmente.");
            return go;
        }

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

    private static T FindInScene<T>(UnityEngine.SceneManagement.Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T found = root.GetComponentInChildren<T>(true);
            if (found != null) return found;
        }

        return null;
    }

    #endregion

    #region Helpers

    private static InputActionReference FindActionReference(string assetPath, string mapName, string actionName)
    {
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

    private static Button CreateButton(string name, Transform parent, Sprite sprite, Color color, string label, float fontSize,
        Color labelColor, out Image image, out TextMeshProUGUI text)
    {
        image = CreateImage(name, parent, sprite, color, Image.Type.Sliced, raycast: true);
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = Color.white; // the panel colours disabled buttons itself
        button.colors = colors;

        text = CreateText("Label", image.transform, label, fontSize, labelColor, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        return button;
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

    private static void SetRefs(Object target, params (string field, Object value)[] references)
    {
        SerializedObject so = new(target);
        foreach ((string field, Object value) in references)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[Upgrades] Campo '{field}' nao existe em {target.GetType().Name}.");
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
