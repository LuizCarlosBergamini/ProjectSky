using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Row under Lucy's health bar with how many of each upgrade material the player has. The list of
/// materials comes from UpgradeManager (every item a node costs), so it follows the upgrade data;
/// counts come from InventoryManager and refresh on OnInventoryChanged. Hidden when either is missing.
/// </summary>
public class ItemCounterUI : MonoBehaviour
{
    [SerializeField] private ItemCountEntryUI entryPrefab;
    [SerializeField] private Transform entryContainer;

    private readonly List<ItemCountEntryUI> entries = new();
    private bool built;

    private void OnEnable()
    {
        InventoryManager.OnInventoryChanged += Refresh;
        if (built) Refresh();
    }

    private void Start()
    {
        // The managers can wake after this HUD when both live in the Hub, so the first build waits for Start.
        Refresh();
    }

    private void OnDisable()
    {
        InventoryManager.OnInventoryChanged -= Refresh;
    }

    private void Refresh()
    {
        if (!built) Build();

        foreach (ItemCountEntryUI entry in entries)
        {
            if (entry == null || entry.Item == null) continue;
            entry.SetQuantity(InventoryManager.instance != null ? InventoryManager.instance.GetQuantity(entry.Item.itemId) : 0);
        }
    }

    private void Build()
    {
        bool available = UpgradeManager.instance != null && InventoryManager.instance != null
            && entryPrefab != null && entryContainer != null;
        gameObject.SetActive(available);
        if (!available) return;

        built = true;
        foreach (Item_SO item in UpgradeManager.instance.GetCostItems())
        {
            ItemCountEntryUI entry = Instantiate(entryPrefab, entryContainer);
            entry.Set(item, InventoryManager.instance.GetQuantity(item.itemId));
            entries.Add(entry);
        }
    }
}
