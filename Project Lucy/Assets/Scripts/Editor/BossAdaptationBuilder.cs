using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tools > Lucy > Build Boss Adaptation. Makes Medusa adapt to the bronze-row upgrades and adds the feedback UI:
/// - Medusa_UpgradeScaling.asset: one response per upgrade paid in Engrenagem de Bronze, balanced by the
///   "half the advantage" rule, linked on the Level1 boss data (Guardiao.asset).
/// - HUD.prefab: an "Adaptada" panel under the boss bar, listing every active adaptation.
/// - UpgradeCanvas.prefab: a warning line in the upgrade detail panel ("Medusa se adapta: +25% vida").
/// Safe to run again: it only adds what is missing and never overwrites tuning. The two UI builders call the
/// prefab steps too, so rebuilding the HUD or the upgrade UI keeps the new elements.
/// </summary>
public static class BossAdaptationBuilder
{
    private const string ScalingPath = "Assets/ObjectData/Bosses/Medusa/Medusa_UpgradeScaling.asset";
    private const string BossDataPath = "Assets/ObjectData/Bosses/Guardiao.asset";
    private const string HudPrefabPath = "Assets/Renan/Prefabs/UI/HUD.prefab";
    private const string UpgradeCanvasPrefabPath = "Assets/Renan/Prefabs/UpgradeCanvas.prefab";
    private const string BronzeItemId = "engrenagem_bronze";

    private static readonly Color WarningColor = new(1f, 0.45f, 0.25f, 1f);

    [MenuItem("Tools/Lucy/Build Boss Adaptation")]
    public static void Build()
    {
        List<string> report = new();

        BossUpgradeScaling_SO scaling = EnsureMedusaScaling(report);
        LinkBossData(scaling, report);
        report.Add(EnsureHudAdaptationPanel() ? $"{HudPrefabPath}: painel 'Adaptada' adicionado" : $"{HudPrefabPath}: painel ja existe");
        report.Add(EnsureUpgradeAdaptationLabel() ? $"{UpgradeCanvasPrefabPath}: aviso de adaptacao adicionado" : $"{UpgradeCanvasPrefabPath}: aviso ja existe");

        AssetDatabase.SaveAssets();
        Debug.Log("Adaptacao dos chefes:\n- " + string.Join("\n- ", report));
    }

    #region Data

    // Every upgrade paid in Engrenagem de Bronze is the first row of the vendor's trees; Medusa answers each one
    // with the opposite stat (damage -> health, life -> damage, speed -> speed).
    private static BossUpgradeScaling_SO EnsureMedusaScaling(List<string> report)
    {
        BossUpgradeScaling_SO scaling = AssetDatabase.LoadAssetAtPath<BossUpgradeScaling_SO>(ScalingPath);
        if (scaling != null)
        {
            report.Add($"{ScalingPath} ja existe (ajustes preservados)");
            return scaling;
        }

        scaling = ScriptableObject.CreateInstance<BossUpgradeScaling_SO>();
        scaling.compensation = 0.5f;
        foreach (UpgradeNode_SO node in FindBronzeNodes())
        {
            scaling.responses.Add(new BossUpgradeResponse
            {
                upgrade = node,
                bossStat = BossUpgradeScaling_SO.CounterFor(node.stat)
            });
        }

        if (BossUpgradeScaling_SO.TryReadPlayerBase(out float damage, out float health, out float speed))
        {
            scaling.Recalculate(damage, health, speed);
            report.Add($"base do jogador: dano {damage}, vida {health}, velocidade {speed}");
        }
        else
        {
            report.Add("AVISO: base do jogador nao encontrada, multiplicadores ficaram em 1 (use o menu de contexto)");
        }

        AssetDatabase.CreateAsset(scaling, ScalingPath);
        foreach (BossUpgradeResponse response in scaling.responses)
        {
            report.Add($"{response.upgrade.nodeName} ({response.upgrade.stat.FormatBonus(response.upgrade.bonusValue)}) -> " +
                       $"Medusa {BossUpgradeScaling_SO.Describe(response.bossStat, response.multiplier)} (x{response.multiplier})");
        }
        return scaling;
    }

    private static IEnumerable<UpgradeNode_SO> FindBronzeNodes()
    {
        return AssetDatabase.FindAssets("t:UpgradeNode_SO")
            .Select(guid => AssetDatabase.LoadAssetAtPath<UpgradeNode_SO>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(node => node != null && node.cost != null &&
                           node.cost.Any(slot => slot != null && slot.item != null && slot.item.itemId == BronzeItemId && slot.quantity > 0))
            .OrderBy(node => node.stat);
    }

    // Level1's boss data keeps everything else; only an empty scaling slot is filled.
    private static void LinkBossData(BossUpgradeScaling_SO scaling, List<string> report)
    {
        BossData_SO data = AssetDatabase.LoadAssetAtPath<BossData_SO>(BossDataPath);
        if (data == null || scaling == null)
        {
            report.Add($"AVISO: {BossDataPath} nao encontrado");
            return;
        }

        if (data.upgradeScaling != null)
        {
            report.Add($"{BossDataPath} ja tem adaptacao ({data.upgradeScaling.name})");
            return;
        }

        data.upgradeScaling = scaling;
        EditorUtility.SetDirty(data);
        report.Add($"{BossDataPath}: upgradeScaling -> {scaling.name}");
    }

    #endregion

    #region HUD

    /// <summary>
    /// Adds the "Adaptada" panel between the boss bar and its reward panel, styled like the reward panel.
    /// False when the HUD prefab is missing or already has the panel.
    /// </summary>
    public static bool EnsureHudAdaptationPanel()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath) == null) return false;

        GameObject root = PrefabUtility.LoadPrefabContents(HudPrefabPath);
        try
        {
            BossHealthBarUI bar = root.GetComponentInChildren<BossHealthBarUI>(true);
            if (bar == null) return false;

            var so = new SerializedObject(bar);
            if (so.FindProperty("adaptationPanel").objectReferenceValue != null) return false;

            var rewardPanel = so.FindProperty("rewardPanel").objectReferenceValue as GameObject;
            Transform rewardHeader = rewardPanel != null ? rewardPanel.transform.Find("Header") : null;
            if (rewardPanel == null || rewardHeader == null) return false;

            var panel = new GameObject("AdaptationPanel", typeof(RectTransform));
            panel.layer = rewardPanel.layer;
            panel.transform.SetParent(bar.transform, false);
            panel.transform.SetSiblingIndex(rewardPanel.transform.GetSiblingIndex());

            CopyImage(rewardPanel.GetComponent<Image>(), panel.AddComponent<Image>());
            CopyLayout(rewardPanel.GetComponent<VerticalLayoutGroup>(), panel.AddComponent<VerticalLayoutGroup>());

            TextMeshProUGUI header = CloneText(rewardHeader.gameObject, panel.transform, "Header");
            header.text = "Adaptada";

            TextMeshProUGUI lines = CloneText(rewardHeader.gameObject, panel.transform, "Lines");
            lines.text = "+25% vida";
            lines.color = WarningColor;

            so.FindProperty("adaptationPanel").objectReferenceValue = panel;
            so.FindProperty("adaptationText").objectReferenceValue = lines;
            so.ApplyModifiedPropertiesWithoutUndo();
            panel.SetActive(false); // BossHealthBarUI shows it when the bound boss has adapted

            PrefabUtility.SaveAsPrefabAsset(root, HudPrefabPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    #endregion

    #region Upgrade UI

    /// <summary>
    /// Adds the adaptation warning to the upgrade detail panel, just above the purchase feedback line.
    /// False when the canvas prefab is missing or already has it.
    /// </summary>
    public static bool EnsureUpgradeAdaptationLabel()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(UpgradeCanvasPrefabPath) == null) return false;

        GameObject root = PrefabUtility.LoadPrefabContents(UpgradeCanvasPrefabPath);
        try
        {
            UpgradeDetailPanel detail = root.GetComponentInChildren<UpgradeDetailPanel>(true);
            if (detail == null) return false;

            var so = new SerializedObject(detail);
            if (so.FindProperty("_adaptationText").objectReferenceValue != null) return false;

            var feedback = so.FindProperty("_feedbackText").objectReferenceValue as TextMeshProUGUI;
            var content = so.FindProperty("_content").objectReferenceValue as GameObject;
            if (feedback == null || content == null) return false;

            TextMeshProUGUI label = CloneText(feedback.gameObject, content.transform, "Adaptation");
            label.text = "Medusa se adapta: +25% vida";
            label.fontSize = 20f;
            label.color = WarningColor;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.alignment = TextAlignmentOptions.Bottom;

            // Bottom-anchored like the feedback line (bottom 76, 26 tall), stacked right above it.
            var rect = (RectTransform)label.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(0f, 48f);
            rect.anchoredPosition = new Vector2(0f, 106f);

            so.FindProperty("_adaptationText").objectReferenceValue = label;
            so.ApplyModifiedPropertiesWithoutUndo();
            label.gameObject.SetActive(false); // UpgradeDetailPanel shows it for upgrades a boss answers

            PrefabUtility.SaveAsPrefabAsset(root, UpgradeCanvasPrefabPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    #endregion

    private static TextMeshProUGUI CloneText(GameObject source, Transform parent, string objectName)
    {
        GameObject clone = Object.Instantiate(source, parent);
        clone.name = objectName;
        clone.SetActive(true);
        return clone.GetComponent<TextMeshProUGUI>();
    }

    private static void CopyImage(Image from, Image to)
    {
        if (from == null) return;
        to.sprite = from.sprite;
        to.type = from.type;
        to.color = from.color;
        to.pixelsPerUnitMultiplier = from.pixelsPerUnitMultiplier;
        to.raycastTarget = false;
    }

    private static void CopyLayout(VerticalLayoutGroup from, VerticalLayoutGroup to)
    {
        if (from == null) return;
        to.padding = new RectOffset(from.padding.left, from.padding.right, from.padding.top, from.padding.bottom);
        to.spacing = from.spacing;
        to.childAlignment = from.childAlignment;
        to.childControlWidth = from.childControlWidth;
        to.childControlHeight = from.childControlHeight;
        to.childForceExpandWidth = from.childForceExpandWidth;
        to.childForceExpandHeight = from.childForceExpandHeight;
    }
}
