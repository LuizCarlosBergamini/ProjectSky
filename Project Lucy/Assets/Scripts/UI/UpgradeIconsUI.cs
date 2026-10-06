using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Row under Lucy's health bar with one slot per upgrade tree (Dano, Vida, Velocidade). Each slot shows the
/// icon of the highest level bought in that tree, so it changes as higher levels are bought; a tree with
/// nothing bought shows its first level dimmed. Refreshes on UpgradeManager.OnUpgradesChanged.
/// </summary>
public class UpgradeIconsUI : MonoBehaviour
{
    [SerializeField] private UpgradeIconSlotUI slotPrefab;
    [SerializeField] private Transform slotContainer;

    private readonly List<(UpgradeTree_SO tree, UpgradeIconSlotUI slot)> slots = new();
    private bool built;

    private void OnEnable()
    {
        UpgradeManager.OnUpgradesChanged += Refresh;
        if (built) Refresh();
    }

    private void Start()
    {
        // UpgradeManager can wake after this HUD when both live in the Hub, so the first build waits for Start.
        Refresh();
    }

    private void OnDisable()
    {
        UpgradeManager.OnUpgradesChanged -= Refresh;
    }

    private void Refresh()
    {
        if (!built) Build();

        UpgradeManager upgrades = UpgradeManager.instance;
        if (upgrades == null) return;

        foreach ((UpgradeTree_SO tree, UpgradeIconSlotUI slot) in slots)
        {
            if (tree == null || slot == null) continue;

            UpgradeNode_SO highest = upgrades.GetHighestPurchased(tree);
            UpgradeNode_SO shown = highest != null ? highest : FirstLevel(tree);
            Sprite sprite = shown != null && shown.nodeIcon != null ? shown.nodeIcon : tree.treeIcon;
            slot.Set(sprite, tree.treeColor, highest != null);
        }
    }

    private void Build()
    {
        bool available = UpgradeManager.instance != null && slotPrefab != null && slotContainer != null;
        gameObject.SetActive(available);
        if (!available) return;

        built = true;
        foreach (UpgradeTree_SO tree in UpgradeManager.instance.Trees)
        {
            if (tree == null) continue;
            UpgradeIconSlotUI slot = Instantiate(slotPrefab, slotContainer);
            slot.name = $"Slot_{tree.treeName}";
            slots.Add((tree, slot));
        }
    }

    private static UpgradeNode_SO FirstLevel(UpgradeTree_SO tree)
    {
        Dictionary<UpgradeNode_SO, int> depths = tree.GetNodeDepths();
        foreach (KeyValuePair<UpgradeNode_SO, int> pair in depths)
        {
            if (pair.Value == 0) return pair.Key;
        }

        return null;
    }
}
