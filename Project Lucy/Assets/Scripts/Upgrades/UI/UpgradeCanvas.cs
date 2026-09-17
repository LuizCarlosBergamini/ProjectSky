using System;
using System.Collections.Generic;
using HierarchicalStateMachine;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The upgrade screen. Scene object (not persistent): builds one column per tree in UpgradeManager,
/// pauses like PauseManager while open and hands gameplay input back when closed.
/// </summary>
public class UpgradeCanvas : MonoBehaviour
{
    public static UpgradeCanvas instance;

    [Header("Referencias")]
    [Tooltip("Tudo que aparece com o menu aberto. Este objeto fica sempre ativo para registrar a instancia.")]
    [SerializeField] private GameObject _root;
    [SerializeField] private RectTransform _treeContainer;
    [SerializeField] private UpgradeTreeUI _treePrefab;
    [SerializeField] private UpgradeDetailPanel _detailPanel;
    [SerializeField] private RectTransform _materialContainer;
    [SerializeField] private UpgradeCostEntryUI _materialEntryPrefab;
    [SerializeField] private TextMeshProUGUI _statsText;
    [SerializeField] private Button _closeButton;

    [Header("Input")]
    [Tooltip("Acao que fecha o menu (a mesma do PauseManager: InGame/Pause).")]
    [SerializeField] private InputActionReference _closeAction;

    [Tooltip("Jogador cujo input e bloqueado. Vazio = objeto com a tag Player ao abrir.")]
    [SerializeField] private PlayerStateDriver _player;

    [Tooltip("Congela o jogo com Time.timeScale = 0 enquanto aberto, como o PauseManager.")]
    [SerializeField] private bool _pauseTime = true;

    [Header("Eventos")]
    [SerializeField] private UnityEvent _onOpen;
    [SerializeField] private UnityEvent _onClose;

    private readonly List<UpgradeTreeUI> _trees = new();
    private readonly List<(Item_SO item, UpgradeCostEntryUI entry)> _materials = new();

    private bool _built;
    private int _openedFrame = -1;
    private float _previousTimeScale = 1f;
    private Action _onClosedCallback;

    private UpgradeNodeUI _selected;
    private UpgradeNodeUI _hovered;

    public bool IsOpen { get; private set; }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Mais de um UpgradeCanvas na cena; o segundo foi ignorado.", this);
            return;
        }

        instance = this;
        if (_root != null) _root.SetActive(false);
        if (_closeButton != null) _closeButton.onClick.AddListener(Close);
    }

    private void OnDisable()
    {
        // Never leave the game frozen or the player without input because the menu went away while open.
        if (IsOpen) Close();
    }

    private void OnDestroy()
    {
        if (_closeButton != null) _closeButton.onClick.RemoveListener(Close);
        if (_detailPanel != null) _detailPanel.PurchaseAttempted -= HandlePurchaseAttempted;
        if (instance == this) instance = null;
    }

    private void Update()
    {
        if (!IsOpen || _closeAction == null) return;

        // The frame the menu opens on may still carry the press that opened it.
        if (Time.frameCount != _openedFrame && _closeAction.action.WasPressedThisFrame())
        {
            Close();
        }
    }

    #region Open / Close

    /// <param name="onClosed">Called once, when this opening is closed.</param>
    public void Open(Action onClosed = null)
    {
        if (IsOpen) return;

        UpgradeManager manager = UpgradeManager.instance;
        if (manager == null)
        {
            Debug.LogWarning("UpgradeManager nao encontrado: coloque o prefab UpgradeManager na cena inicial.", this);
            onClosed?.Invoke();
            return;
        }

        EnsureBuilt(manager);

        IsOpen = true;
        _openedFrame = Time.frameCount;
        _onClosedCallback = onClosed;

        if (_root != null) _root.SetActive(true);

        if (_pauseTime)
        {
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        PlayerStateDriver player = ResolvePlayer();
        if (player != null) player.SetInputEnabled(false);

        UpgradeManager.OnUpgradesChanged += RefreshAll;
        InventoryManager.OnInventoryChanged += RefreshAll;

        _hovered = null;
        RefreshAll();
        SelectInitialNode();

        _onOpen?.Invoke();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;

        UpgradeManager.OnUpgradesChanged -= RefreshAll;
        InventoryManager.OnInventoryChanged -= RefreshAll;

        _hovered = null;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        if (_root != null) _root.SetActive(false);

        if (_pauseTime) Time.timeScale = _previousTimeScale;

        PlayerStateDriver player = ResolvePlayer();
        if (player != null) player.SetInputEnabled(true);

        _onClose?.Invoke();

        Action callback = _onClosedCallback;
        _onClosedCallback = null;
        callback?.Invoke();
    }

    #endregion

    #region Build / Refresh

    private void EnsureBuilt(UpgradeManager manager)
    {
        if (_built) return;
        _built = true;

        foreach (UpgradeTree_SO tree in manager.Trees)
        {
            if (tree == null) continue;
            UpgradeTreeUI treeUI = Instantiate(_treePrefab, _treeContainer);
            treeUI.Build(tree, HandleNodeHover, HandleNodeHoverExit, HandleNodeSelected);
            _trees.Add(treeUI);
        }

        BuildMaterials(manager);

        if (_detailPanel != null) _detailPanel.PurchaseAttempted += HandlePurchaseAttempted;
    }

    /// <summary>One counter per distinct item any node costs.</summary>
    private void BuildMaterials(UpgradeManager manager)
    {
        if (_materialContainer == null || _materialEntryPrefab == null) return;

        HashSet<Item_SO> seen = new();
        foreach (UpgradeTree_SO tree in manager.Trees)
        {
            if (tree == null || tree.nodes == null) continue;
            foreach (UpgradeNode_SO node in tree.nodes)
            {
                foreach (Item_SO item in manager.GetTotalCost(node).Keys)
                {
                    if (!seen.Add(item)) continue;
                    _materials.Add((item, Instantiate(_materialEntryPrefab, _materialContainer)));
                }
            }
        }
    }

    private void RefreshAll()
    {
        UpgradeManager manager = UpgradeManager.instance;
        if (manager == null) return;

        foreach (UpgradeTreeUI tree in _trees) tree.Refresh(manager);
        if (_detailPanel != null) _detailPanel.Refresh();

        foreach ((Item_SO item, UpgradeCostEntryUI entry) in _materials)
        {
            int owned = InventoryManager.instance != null ? InventoryManager.instance.GetQuantity(item.itemId) : 0;
            entry.Set(item.itemSprite, $"{item.itemName}  x{owned}", Color.white);
        }

        RefreshStats();
    }

    private void RefreshStats()
    {
        if (_statsText == null) return;

        PlayerStateDriver player = ResolvePlayer();
        if (player == null)
        {
            _statsText.text = "";
            return;
        }

        _statsText.text =
            $"{UpgradeStat.MaxHealth.DisplayName()} {UpgradeStatText.FormatValue(player.CurrentHealth)}/{UpgradeStatText.FormatValue(player.MaxHealth)}" +
            $"     {UpgradeStat.Damage.DisplayName()} {UpgradeStatText.FormatValue(player.AttackDamage)}" +
            $"     {UpgradeStat.MoveSpeed.DisplayName()} {UpgradeStatText.FormatValue(player.RunMaxSpeed)}";
    }

    #endregion

    #region Selection

    private void SelectInitialNode()
    {
        UpgradeNodeUI target = _selected != null ? _selected : FindFirstNode(UpgradeNodeState.Available);
        if (target == null) target = FindFirstNode(UpgradeNodeState.Unaffordable);
        if (target == null && _trees.Count > 0 && _trees[0].Nodes.Count > 0) target = _trees[0].Nodes[0];
        if (target == null)
        {
            if (_detailPanel != null) _detailPanel.Show(null, null);
            return;
        }

        HandleNodeSelected(target);

        // Gives keyboard navigation a starting point; it calls back into HandleNodeSelected harmlessly.
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(target.gameObject);
    }

    private UpgradeNodeUI FindFirstNode(UpgradeNodeState state)
    {
        foreach (UpgradeTreeUI tree in _trees)
        {
            foreach (UpgradeNodeUI node in tree.Nodes)
            {
                if (node.State == state) return node;
            }
        }

        return null;
    }

    private void HandleNodeHover(UpgradeNodeUI node)
    {
        _hovered = node;
        ShowDetails(node);
    }

    private void HandleNodeHoverExit(UpgradeNodeUI node)
    {
        if (_hovered != node) return;

        // Falls back to the selected node, so the Purchase button always acts on what was clicked.
        _hovered = null;
        ShowDetails(_selected);
    }

    private void HandleNodeSelected(UpgradeNodeUI node)
    {
        if (_selected != null) _selected.SetSelected(false);
        _selected = node;
        if (_selected != null) _selected.SetSelected(true);

        if (_hovered == null || _hovered == node) ShowDetails(node);
    }

    private void ShowDetails(UpgradeNodeUI node)
    {
        if (_detailPanel == null) return;
        if (node == null) _detailPanel.Show(null, null);
        else _detailPanel.Show(node.Tree, node.Node);
    }

    private void HandlePurchaseAttempted(UpgradeNode_SO node, UpgradePurchaseResult result)
    {
        if (result != UpgradePurchaseResult.Success) return;

        foreach (UpgradeTreeUI tree in _trees)
        {
            UpgradeNodeUI nodeUI = tree.Find(node);
            if (nodeUI != null) nodeUI.PlayPurchaseEffect();
        }
    }

    #endregion

    private PlayerStateDriver ResolvePlayer()
    {
        // Only runs on open/close/refresh, never per frame. The player is rebuilt with every scene load,
        // so a lost reference is looked up again.
        if (_player == null)
        {
            GameObject playerObject = GameObject.FindWithTag("Player");
            if (playerObject != null) _player = playerObject.GetComponent<PlayerStateDriver>();
        }

        return _player;
    }
}
