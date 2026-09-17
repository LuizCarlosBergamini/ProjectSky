using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Details of the hovered or selected node, and the only place a purchase is requested from.</summary>
public class UpgradeDetailPanel : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private GameObject _emptyState;
    [SerializeField] private GameObject _content;
    [SerializeField] private Image _iconFill;
    [SerializeField] private Image _iconFrame;
    [SerializeField] private Image _icon;
    [SerializeField] private TextMeshProUGUI _treeText;
    [SerializeField] private TextMeshProUGUI _nameText;
    [SerializeField] private TextMeshProUGUI _descriptionText;
    [SerializeField] private TextMeshProUGUI _bonusText;
    [SerializeField] private TextMeshProUGUI _statusText;
    [SerializeField] private TextMeshProUGUI _costHeaderText;
    [SerializeField] private RectTransform _costContainer;
    [SerializeField] private UpgradeCostEntryUI _costEntryPrefab;
    [SerializeField] private Button _purchaseButton;
    [SerializeField] private Image _purchaseButtonImage;
    [SerializeField] private TextMeshProUGUI _purchaseLabel;
    [SerializeField] private TextMeshProUGUI _feedbackText;

    [Header("Cores")]
    [SerializeField] private Color _textColor = new(0.92f, 0.92f, 0.92f, 1f);
    [SerializeField] private Color _warningColor = new(1f, 0.45f, 0.25f, 1f);
    [SerializeField] private Color _lockedColor = new(0.55f, 0.55f, 0.58f, 1f);
    [SerializeField] private Color _disabledButtonColor = new(0.2f, 0.2f, 0.22f, 1f);

    private UpgradeTree_SO _tree;
    private UpgradeNode_SO _feedbackNode;

    /// <summary>Raised after a purchase attempt, successful or not.</summary>
    public event Action<UpgradeNode_SO, UpgradePurchaseResult> PurchaseAttempted;

    public UpgradeNode_SO Node { get; private set; }

    private void Awake()
    {
        if (_purchaseButton != null) _purchaseButton.onClick.AddListener(Purchase);
    }

    private void OnDestroy()
    {
        if (_purchaseButton != null) _purchaseButton.onClick.RemoveListener(Purchase);
    }

    public void Show(UpgradeTree_SO tree, UpgradeNode_SO node)
    {
        if (node != Node) _feedbackNode = null;
        _tree = tree;
        Node = node;
        Refresh();
    }

    public void Refresh()
    {
        UpgradeManager manager = UpgradeManager.instance;
        bool hasNode = Node != null && manager != null;
        if (_emptyState != null) _emptyState.SetActive(!hasNode);
        if (_content != null) _content.SetActive(hasNode);
        if (!hasNode) return;

        Color treeColor = _tree != null ? _tree.treeColor : Color.white;
        UpgradeNodeState state = manager.GetState(Node);

        if (_icon != null)
        {
            _icon.sprite = Node.nodeIcon != null ? Node.nodeIcon : _tree != null ? _tree.treeIcon : null;
            _icon.enabled = _icon.sprite != null;
            _icon.color = state == UpgradeNodeState.Purchased ? new Color(0.06f, 0.06f, 0.06f, 1f) : treeColor;
        }
        if (_iconFill != null)
        {
            _iconFill.color = state == UpgradeNodeState.Purchased
                ? treeColor
                : Color.Lerp(new Color(0.08f, 0.08f, 0.09f), treeColor, 0.22f);
        }
        if (_iconFrame != null) _iconFrame.color = treeColor;

        SetText(_treeText, _tree != null ? _tree.treeName.ToUpperInvariant() : "", treeColor);
        SetText(_nameText, Node.nodeName, _textColor);
        SetText(_descriptionText, Node.nodeDescription, new Color(_textColor.r, _textColor.g, _textColor.b, 0.75f));
        SetText(_bonusText, Node.stat.FormatBonus(Node.bonusValue), treeColor);

        switch (state)
        {
            case UpgradeNodeState.Purchased:
                SetText(_statusText, "Adquirido", treeColor);
                break;
            case UpgradeNodeState.Available:
                SetText(_statusText, "Disponível para compra", _textColor);
                break;
            case UpgradeNodeState.Unaffordable:
                SetText(_statusText, "Materiais insuficientes", _warningColor);
                break;
            default:
                SetText(_statusText, $"Requer: {PrerequisiteNames()}", _lockedColor);
                break;
        }

        RefreshCost(manager, state);
        RefreshButton(state, treeColor);

        if (_feedbackText != null && _feedbackNode != Node) _feedbackText.text = "";
    }

    private void Purchase()
    {
        UpgradeManager manager = UpgradeManager.instance;
        if (Node == null || manager == null) return;

        UpgradeNode_SO node = Node;
        UpgradePurchaseResult result = manager.TryPurchase(node);

        // The purchase already refreshed everything through OnUpgradesChanged; this only adds the message.
        _feedbackNode = node;
        if (_feedbackText != null)
        {
            Color treeColor = _tree != null ? _tree.treeColor : Color.white;
            _feedbackText.color = result == UpgradePurchaseResult.Success ? treeColor : _warningColor;
            _feedbackText.text = result switch
            {
                UpgradePurchaseResult.Success => "Melhoria adquirida!",
                UpgradePurchaseResult.NotEnoughItems => "Você não tem materiais suficientes.",
                UpgradePurchaseResult.Locked => "Compre a melhoria anterior primeiro.",
                UpgradePurchaseResult.AlreadyPurchased => "Você já possui esta melhoria.",
                UpgradePurchaseResult.NoInventory => "Inventário indisponível.",
                _ => "Melhoria inválida."
            };
        }

        Refresh();
        PurchaseAttempted?.Invoke(node, result);
    }

    private void RefreshCost(UpgradeManager manager, UpgradeNodeState state)
    {
        if (_costContainer == null || _costEntryPrefab == null) return;

        for (int i = _costContainer.childCount - 1; i >= 0; i--)
        {
            // Deactivated first: Destroy is deferred and the layout group would still count the old rows.
            GameObject child = _costContainer.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }

        Dictionary<Item_SO, int> baseCost = manager.GetBaseCost(Node);
        int surcharge = 0;

        // An owned node shows its base price: the live one keeps climbing as the rest of its row is bought,
        // which would read as if it still had to be paid.
        Dictionary<Item_SO, int> shownCost = state == UpgradeNodeState.Purchased ? baseCost : manager.GetTotalCost(Node);

        foreach (KeyValuePair<Item_SO, int> cost in shownCost)
        {
            int owned = InventoryManager.instance != null ? InventoryManager.instance.GetQuantity(cost.Key.itemId) : 0;
            Color color = state == UpgradeNodeState.Purchased ? _lockedColor
                : owned >= cost.Value ? _textColor
                : _warningColor;

            baseCost.TryGetValue(cost.Key, out int original);
            surcharge = Mathf.Max(surcharge, cost.Value - original);

            UpgradeCostEntryUI entry = Instantiate(_costEntryPrefab, _costContainer);
            entry.Set(cost.Key.itemSprite, $"{cost.Key.itemName}  {owned}/{cost.Value}", color);
        }

        // Says out loud why this node got more expensive than the asset says.
        if (_costHeaderText != null)
        {
            bool raised = surcharge > 0 && state != UpgradeNodeState.Purchased;
            SetText(_costHeaderText, raised ? $"CUSTO (+{surcharge} PELA LINHA)" : "CUSTO",
                raised ? _warningColor : _lockedColor);
        }
    }

    private void RefreshButton(UpgradeNodeState state, Color treeColor)
    {
        if (_purchaseButton != null) _purchaseButton.interactable = state == UpgradeNodeState.Available;
        if (_purchaseButtonImage != null)
        {
            _purchaseButtonImage.color = state == UpgradeNodeState.Available ? treeColor : _disabledButtonColor;
        }

        string label = state switch
        {
            UpgradeNodeState.Purchased => "ADQUIRIDO",
            UpgradeNodeState.Available => "COMPRAR",
            UpgradeNodeState.Unaffordable => "SEM MATERIAIS",
            _ => "BLOQUEADO"
        };
        Color labelColor = state == UpgradeNodeState.Available ? new Color(0.06f, 0.06f, 0.06f, 1f) : _lockedColor;
        SetText(_purchaseLabel, label, labelColor);
    }

    private string PrerequisiteNames()
    {
        List<string> names = new();
        UpgradeManager manager = UpgradeManager.instance;
        foreach (UpgradeNode_SO prerequisite in Node.prerequisites)
        {
            if (prerequisite == null || (manager != null && manager.IsPurchased(prerequisite))) continue;
            names.Add(prerequisite.nodeName);
        }

        string separator = Node.requireAllPrerequisites ? ", " : " ou ";
        return string.Join(separator, names);
    }

    private static void SetText(TextMeshProUGUI field, string text, Color color)
    {
        if (field == null) return;
        field.text = text ?? "";
        field.color = color;
    }
}
