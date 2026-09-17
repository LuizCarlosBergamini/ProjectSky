using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>One hexagon in a tree. Only draws its node's state and reports hover/selection upwards.</summary>
public class UpgradeNodeUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler
{
    [Header("Referencias")]
    [SerializeField] private Button _button;
    [SerializeField] private Image _glow;
    [SerializeField] private Image _selection;
    [SerializeField] private Image _fill;
    [SerializeField] private Image _frame;
    [SerializeField] private Image _icon;
    [SerializeField] private Image _lock;
    [SerializeField] private RectTransform _visual;
    [SerializeField] private RectTransform _costContainer;
    [SerializeField] private UpgradeCostEntryUI _costEntryPrefab;

    [Header("Cores")]
    [SerializeField] private Color _emptyFillColor = new(0.08f, 0.08f, 0.09f, 1f);
    [SerializeField] private Color _lockedColor = new(0.42f, 0.42f, 0.45f, 1f);
    [SerializeField] private Color _warningColor = new(1f, 0.45f, 0.25f, 1f);
    [SerializeField] private Color _costColor = new(0.92f, 0.92f, 0.92f, 1f);

    [Header("Animacao")]
    [SerializeField] private float _pulseSpeed = 3f;

    // One entry per item the node costs. The quantity is not kept here: buying anywhere in the same row
    // raises it, so it is read from the manager on every refresh.
    private readonly List<(Item_SO item, UpgradeCostEntryUI entry)> _costEntries = new();

    private Action<UpgradeNodeUI> _onHover;
    private Action<UpgradeNodeUI> _onHoverExit;
    private Action<UpgradeNodeUI> _onSelect;

    private Color _treeColor = Color.white;
    private bool _selected;
    private Coroutine _punch;

    public UpgradeNode_SO Node { get; private set; }
    public UpgradeTree_SO Tree { get; private set; }
    public UpgradeNodeState State { get; private set; }
    public RectTransform RectTransform => (RectTransform)transform;
    public Button Button => _button;

    public void Bind(UpgradeTree_SO tree, UpgradeNode_SO node,
        Action<UpgradeNodeUI> onHover, Action<UpgradeNodeUI> onHoverExit, Action<UpgradeNodeUI> onSelect)
    {
        Tree = tree;
        Node = node;
        _treeColor = tree != null ? tree.treeColor : Color.white;
        _onHover = onHover;
        _onHoverExit = onHoverExit;
        _onSelect = onSelect;

        name = $"Node_{node.nodeId}";
        if (_icon != null)
        {
            _icon.sprite = node.nodeIcon != null ? node.nodeIcon : tree != null ? tree.treeIcon : null;
            _icon.enabled = _icon.sprite != null;
        }

        if (_button != null)
        {
            _button.onClick.RemoveListener(HandleClick);
            _button.onClick.AddListener(HandleClick);
        }

        BuildCostEntries();
        SetSelected(false);
    }

    public void Refresh(UpgradeManager manager)
    {
        if (Node == null || manager == null) return;
        State = manager.GetState(Node);

        Color dimTree = Color.Lerp(_emptyFillColor, _treeColor, 0.22f);
        switch (State)
        {
            case UpgradeNodeState.Purchased:
                SetColor(_fill, _treeColor);
                SetColor(_frame, Color.Lerp(_treeColor, Color.white, 0.45f));
                SetColor(_icon, new Color(0.06f, 0.06f, 0.06f, 1f));
                SetColor(_glow, WithAlpha(_treeColor, 0.75f));
                break;
            case UpgradeNodeState.Available:
                SetColor(_fill, dimTree);
                SetColor(_frame, _treeColor);
                SetColor(_icon, _treeColor);
                SetColor(_glow, WithAlpha(_treeColor, 0.3f));
                break;
            case UpgradeNodeState.Unaffordable:
                SetColor(_fill, dimTree);
                SetColor(_frame, WithAlpha(_treeColor, 0.55f));
                SetColor(_icon, WithAlpha(_treeColor, 0.6f));
                SetColor(_glow, Color.clear);
                break;
            default:
                SetColor(_fill, _emptyFillColor);
                SetColor(_frame, WithAlpha(_lockedColor, 0.6f));
                SetColor(_icon, WithAlpha(_lockedColor, 0.45f));
                SetColor(_glow, Color.clear);
                break;
        }

        if (_glow != null) _glow.enabled = _glow.color.a > 0f;
        if (_lock != null) _lock.enabled = State == UpgradeNodeState.Locked;

        RefreshCost(manager);
    }

    public void SetSelected(bool selected)
    {
        _selected = selected;
        if (_selection != null) _selection.enabled = selected;
    }

    /// <summary>Short scale bounce played when this node is bought.</summary>
    public void PlayPurchaseEffect()
    {
        if (!isActiveAndEnabled || _visual == null) return;
        if (_punch != null) StopCoroutine(_punch);
        _punch = StartCoroutine(PunchScale());
    }

    public void OnPointerEnter(PointerEventData eventData) => _onHover?.Invoke(this);

    public void OnPointerExit(PointerEventData eventData) => _onHoverExit?.Invoke(this);

    public void OnSelect(BaseEventData eventData) => _onSelect?.Invoke(this);

    private void HandleClick() => _onSelect?.Invoke(this);

    private void Update()
    {
        // Unscaled: the menu runs with Time.timeScale at 0.
        float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * _pulseSpeed);

        if (State == UpgradeNodeState.Available && _frame != null)
        {
            _frame.color = Color.Lerp(_treeColor, Color.white, wave * 0.4f);
            if (_glow != null) _glow.color = WithAlpha(_treeColor, 0.2f + wave * 0.35f);
        }

        if (_selection != null && _selected)
        {
            _selection.color = WithAlpha(Color.white, 0.55f + wave * 0.45f);
        }
    }

    private void BuildCostEntries()
    {
        _costEntries.Clear();
        if (_costContainer == null || _costEntryPrefab == null) return;

        for (int i = _costContainer.childCount - 1; i >= 0; i--)
        {
            Destroy(_costContainer.GetChild(i).gameObject);
        }

        if (UpgradeManager.instance == null) return;
        foreach (KeyValuePair<Item_SO, int> cost in UpgradeManager.instance.GetBaseCost(Node))
        {
            UpgradeCostEntryUI entry = Instantiate(_costEntryPrefab, _costContainer);
            _costEntries.Add((cost.Key, entry));
        }
    }

    private void RefreshCost(UpgradeManager manager)
    {
        if (_costContainer == null) return;

        bool showCost = State != UpgradeNodeState.Purchased && _costEntries.Count > 0;
        _costContainer.gameObject.SetActive(showCost);
        if (!showCost) return;

        Dictionary<Item_SO, int> costs = manager.GetTotalCost(Node);
        foreach ((Item_SO item, UpgradeCostEntryUI entry) in _costEntries)
        {
            costs.TryGetValue(item, out int required);
            int owned = InventoryManager.instance != null ? InventoryManager.instance.GetQuantity(item.itemId) : 0;
            Color color = State == UpgradeNodeState.Locked ? _lockedColor
                : owned < required ? _warningColor
                : _costColor;
            entry.Set(item.itemSprite, required.ToString(), color);
        }
    }

    private IEnumerator PunchScale()
    {
        const float duration = 0.3f;
        float time = 0f;
        while (time < duration)
        {
            time += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(time / duration);
            _visual.localScale = Vector3.one * (1f + Mathf.Sin(t * Mathf.PI) * 0.25f);
            yield return null;
        }

        _visual.localScale = Vector3.one;
        _punch = null;
    }

    private void OnDisable()
    {
        if (_visual != null) _visual.localScale = Vector3.one;
        _punch = null;
    }

    private static void SetColor(Graphic graphic, Color color)
    {
        if (graphic != null) graphic.color = color;
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }
}
