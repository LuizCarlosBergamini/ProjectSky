using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns which upgrade nodes the player bought and what they cost.
/// Persistent like InventoryManager, so bonuses survive Hub/level loads. It never touches the player
/// directly: PlayerStateDriver pulls the totals from GetBonus and listens to OnUpgradesChanged.
/// </summary>
public class UpgradeManager : MonoBehaviour
{
    public static UpgradeManager instance;

    /// <summary>Raised after a purchase, a load or a reset changes the purchased set.</summary>
    public static event Action OnUpgradesChanged;

    [SerializeField] private List<UpgradeTree_SO> _trees = new();

    [Tooltip("Quanto o custo de uma linha sobe a cada melhoria ja comprada com o mesmo item. 0 = custo fixo.")]
    [SerializeField] private int _costIncreasePerPurchase = 1;

    [Tooltip("Somente leitura em Play Mode: nos comprados nesta sessao.")]
    [SerializeField] private List<UpgradeNode_SO> _purchasedNodes = new();

    private readonly HashSet<UpgradeNode_SO> _knownNodes = new();
    private readonly Dictionary<string, UpgradeNode_SO> _nodesById = new();

    public IReadOnlyList<UpgradeTree_SO> Trees => _trees;

    private void Awake()
    {
        if (instance != null)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        IndexNodes();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    #region Queries

    public bool IsPurchased(UpgradeNode_SO node)
    {
        return node != null && _purchasedNodes.Contains(node);
    }

    public bool ArePrerequisitesMet(UpgradeNode_SO node)
    {
        if (node == null) return false;
        if (node.prerequisites == null || node.prerequisites.Count == 0) return true;

        bool anyMet = false;
        bool anyDeclared = false;
        foreach (UpgradeNode_SO prerequisite in node.prerequisites)
        {
            if (prerequisite == null) continue;
            anyDeclared = true;

            bool met = IsPurchased(prerequisite);
            if (node.requireAllPrerequisites && !met) return false;
            anyMet |= met;
        }

        return !anyDeclared || node.requireAllPrerequisites || anyMet;
    }

    public bool CanAfford(UpgradeNode_SO node)
    {
        if (node == null || InventoryManager.instance == null) return false;

        foreach (KeyValuePair<Item_SO, int> cost in GetTotalCost(node))
        {
            if (InventoryManager.instance.GetQuantity(cost.Key.itemId) < cost.Value) return false;
        }

        return true;
    }

    public UpgradeNodeState GetState(UpgradeNode_SO node)
    {
        if (IsPurchased(node)) return UpgradeNodeState.Purchased;
        if (!ArePrerequisitesMet(node)) return UpgradeNodeState.Locked;
        return CanAfford(node) ? UpgradeNodeState.Available : UpgradeNodeState.Unaffordable;
    }

    /// <summary>Sum of every purchased node's bonus for <paramref name="stat"/>.</summary>
    public float GetBonus(UpgradeStat stat)
    {
        float total = 0f;
        foreach (UpgradeNode_SO node in _purchasedNodes)
        {
            if (node != null && node.stat == stat) total += node.bonusValue;
        }

        return total;
    }

    /// <summary>
    /// Price written in the asset, with repeated items merged and empty slots skipped. InventoryManager.RemoveItem
    /// treats any quantity below 1 as 1, so zero-cost entries must never reach it.
    /// </summary>
    public Dictionary<Item_SO, int> GetBaseCost(UpgradeNode_SO node)
    {
        Dictionary<Item_SO, int> totals = new();
        if (node == null || node.cost == null) return totals;

        foreach (InventorySlot slot in node.cost)
        {
            if (slot == null || slot.item == null || slot.quantity <= 0) continue;
            totals.TryGetValue(slot.item, out int current);
            totals[slot.item] = current + slot.quantity;
        }

        return totals;
    }

    /// <summary>What the node costs right now: its base cost plus the surcharge of each item it uses.</summary>
    public Dictionary<Item_SO, int> GetTotalCost(UpgradeNode_SO node)
    {
        Dictionary<Item_SO, int> totals = GetBaseCost(node);

        List<Item_SO> items = new(totals.Keys);
        foreach (Item_SO item in items)
        {
            totals[item] += GetCostSurcharge(item, node);
        }

        return totals;
    }

    /// <summary>
    /// Every upgrade already bought with <paramref name="item"/> makes the ones still for sale cost one more
    /// of it. Each row of the trees is priced in its own item, so buying in one row never moves the others.
    /// </summary>
    /// <param name="ignoredNode">Node left out of the count, so an owned node keeps showing its own price.</param>
    public int GetCostSurcharge(Item_SO item, UpgradeNode_SO ignoredNode = null)
    {
        if (item == null || _costIncreasePerPurchase <= 0) return 0;

        int purchasedWithItem = 0;
        foreach (UpgradeNode_SO node in _purchasedNodes)
        {
            if (node == null || node == ignoredNode) continue;
            if (UsesItem(node, item)) purchasedWithItem++;
        }

        return purchasedWithItem * _costIncreasePerPurchase;
    }

    private static bool UsesItem(UpgradeNode_SO node, Item_SO item)
    {
        if (node.cost == null) return false;

        foreach (InventorySlot slot in node.cost)
        {
            if (slot != null && slot.item == item && slot.quantity > 0) return true;
        }

        return false;
    }

    #endregion

    #region Purchase

    /// <summary>
    /// Validates everything first and only then spends the items and records the node, so a refused
    /// purchase never costs anything.
    /// </summary>
    public UpgradePurchaseResult TryPurchase(UpgradeNode_SO node)
    {
        UpgradePurchaseResult validation = ValidatePurchase(node);
        if (validation != UpgradePurchaseResult.Success) return validation;

        Dictionary<Item_SO, int> totalCost = GetTotalCost(node);

        // Recorded before paying: if spending throws, the node is taken back out and nothing
        // is left half-applied.
        _purchasedNodes.Add(node);
        try
        {
            foreach (KeyValuePair<Item_SO, int> cost in totalCost)
            {
                InventoryManager.instance.RemoveItem(cost.Key.itemId, cost.Value);
            }
        }
        catch (Exception exception)
        {
            _purchasedNodes.Remove(node);
            Debug.LogException(exception, this);
            return UpgradePurchaseResult.NoInventory;
        }

        Debug.Log($"Upgrade '{node.nodeId}' comprado: {node.stat.FormatBonus(node.bonusValue)}");
        RaiseChanged();
        return UpgradePurchaseResult.Success;
    }

    public UpgradePurchaseResult ValidatePurchase(UpgradeNode_SO node)
    {
        if (node == null || !_knownNodes.Contains(node)) return UpgradePurchaseResult.InvalidNode;
        if (IsPurchased(node)) return UpgradePurchaseResult.AlreadyPurchased;
        if (!ArePrerequisitesMet(node)) return UpgradePurchaseResult.Locked;
        if (InventoryManager.instance == null) return UpgradePurchaseResult.NoInventory;
        if (!CanAfford(node)) return UpgradePurchaseResult.NotEnoughItems;
        return UpgradePurchaseResult.Success;
    }

    #endregion

    #region Persistence Hooks

    // No save system exists yet. These are the only entry points one needs: write the ids out,
    // read them back in. Loading never charges items.

    public List<string> GetPurchasedNodeIds()
    {
        List<string> ids = new();
        foreach (UpgradeNode_SO node in _purchasedNodes)
        {
            if (node != null && !string.IsNullOrWhiteSpace(node.nodeId)) ids.Add(node.nodeId);
        }

        return ids;
    }

    public void LoadPurchasedNodeIds(IEnumerable<string> nodeIds)
    {
        _purchasedNodes.Clear();
        if (nodeIds != null)
        {
            foreach (string nodeId in nodeIds)
            {
                if (nodeId == null || !_nodesById.TryGetValue(nodeId, out UpgradeNode_SO node))
                {
                    Debug.LogWarning($"Upgrade '{nodeId}' salvo nao existe mais e foi ignorado.", this);
                    continue;
                }

                if (!_purchasedNodes.Contains(node)) _purchasedNodes.Add(node);
            }
        }

        RaiseChanged();
    }

    /// <summary>Forgets every purchase without refunding items.</summary>
    public void ResetUpgrades()
    {
        _purchasedNodes.Clear();
        RaiseChanged();
    }

    #endregion

    private void IndexNodes()
    {
        _knownNodes.Clear();
        _nodesById.Clear();

        foreach (UpgradeTree_SO tree in _trees)
        {
            if (tree == null || tree.nodes == null) continue;
            foreach (UpgradeNode_SO node in tree.nodes)
            {
                if (node == null) continue;
                _knownNodes.Add(node);

                if (string.IsNullOrWhiteSpace(node.nodeId))
                {
                    Debug.LogWarning($"Upgrade '{node.name}' sem nodeId, nao podera ser salvo.", node);
                    continue;
                }

                if (_nodesById.TryGetValue(node.nodeId, out UpgradeNode_SO other) && other != node)
                {
                    Debug.LogWarning($"nodeId '{node.nodeId}' repetido em '{node.name}' e '{other.name}'.", node);
                    continue;
                }

                _nodesById[node.nodeId] = node;
            }
        }
    }

    private void RaiseChanged()
    {
        OnUpgradesChanged?.Invoke();
    }

#if UNITY_EDITOR
    [ContextMenu("Debug/Dar 10 de cada material de upgrade")]
    private void DebugGrantCostItems()
    {
        if (!Application.isPlaying || InventoryManager.instance == null) return;

        HashSet<Item_SO> items = new();
        foreach (UpgradeNode_SO node in _knownNodes)
        {
            foreach (Item_SO item in GetTotalCost(node).Keys) items.Add(item);
        }

        // Not a run pickup: dying must not take debug materials back.
        foreach (Item_SO item in items) InventoryManager.instance.AddItem(item, 10, false);
    }

    [ContextMenu("Debug/Resetar upgrades")]
    private void DebugResetUpgrades()
    {
        if (Application.isPlaying) ResetUpgrades();
    }
#endif
}
