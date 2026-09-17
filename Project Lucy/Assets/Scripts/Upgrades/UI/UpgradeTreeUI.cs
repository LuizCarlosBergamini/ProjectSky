using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One tree column. Lays its nodes out from the data alone: a node's row is the length of its longest
/// prerequisite chain, so adding nodes or branches in the asset needs no UI changes.
/// </summary>
public class UpgradeTreeUI : MonoBehaviour
{
    [Header("Cabecalho")]
    [SerializeField] private Image _background;
    [SerializeField] private Image _headerGlow;
    [SerializeField] private Image _headerFrame;
    [SerializeField] private Image _headerIcon;
    [SerializeField] private TextMeshProUGUI _nameText;
    [SerializeField] private TextMeshProUGUI _bonusText;

    [Header("Nos")]
    [SerializeField] private RectTransform _content;
    [SerializeField] private RectTransform _lineContainer;
    [SerializeField] private RectTransform _nodeContainer;
    [SerializeField] private UpgradeNodeUI _nodePrefab;
    [SerializeField] private Image _linePrefab;

    [Header("Layout")]
    [Tooltip("Distancia vertical entre linhas. Abaixo de ~125 o custo de um no encosta no no seguinte.")]
    [SerializeField] private float _rowSpacing = 126f;
    [SerializeField] private float _columnSpacing = 100f;
    [SerializeField] private float _topPadding = 52f;
    [SerializeField] private float _bottomPadding = 84f;
    [SerializeField] private float _lineThickness = 6f;
    [SerializeField] private Color _dimLineColor = new(0.4f, 0.4f, 0.43f, 1f);

    private class Link
    {
        public UpgradeNode_SO from;
        public UpgradeNode_SO to;
        public Image image;
    }

    private readonly List<UpgradeNodeUI> _nodes = new();
    private readonly Dictionary<UpgradeNode_SO, UpgradeNodeUI> _nodesByData = new();
    private readonly List<Link> _links = new();

    public UpgradeTree_SO Tree { get; private set; }
    public IReadOnlyList<UpgradeNodeUI> Nodes => _nodes;

    public void Build(UpgradeTree_SO tree,
        Action<UpgradeNodeUI> onHover, Action<UpgradeNodeUI> onHoverExit, Action<UpgradeNodeUI> onSelect)
    {
        Tree = tree;
        name = $"Tree_{tree.name}";

        if (_nameText != null) _nameText.text = tree.treeName;
        if (_headerIcon != null)
        {
            _headerIcon.sprite = tree.treeIcon;
            _headerIcon.enabled = tree.treeIcon != null;
            _headerIcon.color = tree.treeColor;
        }
        if (_headerFrame != null) _headerFrame.color = tree.treeColor;
        if (_headerGlow != null) _headerGlow.color = new Color(tree.treeColor.r, tree.treeColor.g, tree.treeColor.b, 0.45f);
        if (_background != null)
        {
            Color background = Color.Lerp(new Color(0.07f, 0.07f, 0.08f), tree.treeColor, 0.12f);
            background.a = 0.92f;
            _background.color = background;
        }

        ClearChildren(_nodeContainer);
        ClearChildren(_lineContainer);
        _nodes.Clear();
        _nodesByData.Clear();
        _links.Clear();

        List<UpgradeNode_SO> treeNodes = new();
        if (tree.nodes != null)
        {
            foreach (UpgradeNode_SO node in tree.nodes)
            {
                if (node != null && !treeNodes.Contains(node)) treeNodes.Add(node);
            }
        }

        Dictionary<UpgradeNode_SO, int> depths = ComputeDepths(treeNodes);
        int maxDepth = 0;
        foreach (int depth in depths.Values) maxDepth = Mathf.Max(maxDepth, depth);

        if (_content != null)
        {
            _content.sizeDelta = new Vector2(_content.sizeDelta.x, _topPadding + maxDepth * _rowSpacing + _bottomPadding);
        }

        for (int row = 0; row <= maxDepth; row++)
        {
            List<UpgradeNode_SO> rowNodes = treeNodes.FindAll(n => depths[n] == row);
            for (int column = 0; column < rowNodes.Count; column++)
            {
                UpgradeNodeUI nodeUI = Instantiate(_nodePrefab, _nodeContainer);
                nodeUI.Bind(tree, rowNodes[column], onHover, onHoverExit, onSelect);
                nodeUI.RectTransform.anchoredPosition = new Vector2(
                    (column - (rowNodes.Count - 1) * 0.5f) * _columnSpacing,
                    -(_topPadding + row * _rowSpacing));

                _nodes.Add(nodeUI);
                _nodesByData[rowNodes[column]] = nodeUI;
            }
        }

        // Nodes are added row by row, so reorder the list to match the asset for predictable navigation.
        _nodes.Sort((a, b) => treeNodes.IndexOf(a.Node).CompareTo(treeNodes.IndexOf(b.Node)));

        foreach (UpgradeNodeUI nodeUI in _nodes)
        {
            if (nodeUI.Node.prerequisites == null) continue;
            foreach (UpgradeNode_SO prerequisite in nodeUI.Node.prerequisites)
            {
                // Prerequisites from another tree cannot be drawn inside this column.
                if (prerequisite == null || !_nodesByData.TryGetValue(prerequisite, out UpgradeNodeUI fromUI)) continue;
                _links.Add(new Link { from = prerequisite, to = nodeUI.Node, image = CreateLine(fromUI, nodeUI) });
            }
        }
    }

    public void Refresh(UpgradeManager manager)
    {
        if (manager == null || Tree == null) return;

        foreach (UpgradeNodeUI node in _nodes) node.Refresh(manager);

        foreach (Link link in _links)
        {
            if (link.image == null) continue;
            bool fromBought = manager.IsPurchased(link.from);
            bool toBought = manager.IsPurchased(link.to);

            Color color = _dimLineColor;
            if (fromBought && toBought) color = Color.Lerp(Tree.treeColor, Color.white, 0.2f);
            else if (fromBought) color = new Color(Tree.treeColor.r, Tree.treeColor.g, Tree.treeColor.b, 0.55f);
            link.image.color = color;
        }

        if (_bonusText != null)
        {
            // Grouped per stat: a tree is free to mix stats, and "+5 Dano +10 Vida" must not add up to 15.
            List<UpgradeStat> stats = new();
            Dictionary<UpgradeStat, float> totals = new();
            foreach (UpgradeNodeUI node in _nodes)
            {
                if (!totals.ContainsKey(node.Node.stat))
                {
                    stats.Add(node.Node.stat);
                    totals[node.Node.stat] = 0f;
                }
                if (node.State == UpgradeNodeState.Purchased) totals[node.Node.stat] += node.Node.bonusValue;
            }

            List<string> parts = new();
            bool anyBonus = false;
            foreach (UpgradeStat stat in stats)
            {
                parts.Add(stat.FormatBonus(totals[stat]));
                anyBonus |= totals[stat] != 0f;
            }

            _bonusText.text = string.Join("  ", parts);
            _bonusText.color = anyBonus ? Tree.treeColor : _dimLineColor;
        }
    }

    public UpgradeNodeUI Find(UpgradeNode_SO node)
    {
        return node != null && _nodesByData.TryGetValue(node, out UpgradeNodeUI nodeUI) ? nodeUI : null;
    }

    private Image CreateLine(UpgradeNodeUI from, UpgradeNodeUI to)
    {
        Image line = Instantiate(_linePrefab, _lineContainer);
        line.name = $"Line_{from.Node.nodeId}_{to.Node.nodeId}";
        line.raycastTarget = false;

        Vector2 start = from.RectTransform.anchoredPosition;
        Vector2 end = to.RectTransform.anchoredPosition;
        Vector2 delta = end - start;

        RectTransform rect = line.rectTransform;
        rect.anchorMin = rect.anchorMax = _nodePrefab.RectTransform.anchorMin;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = (start + end) * 0.5f;
        rect.sizeDelta = new Vector2(delta.magnitude, _lineThickness);
        rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        return line;
    }

    /// <summary>Row of each node = longest chain of in-tree prerequisites above it.</summary>
    private static Dictionary<UpgradeNode_SO, int> ComputeDepths(List<UpgradeNode_SO> treeNodes)
    {
        Dictionary<UpgradeNode_SO, int> depths = new();
        HashSet<UpgradeNode_SO> visiting = new();

        int Depth(UpgradeNode_SO node)
        {
            if (depths.TryGetValue(node, out int known)) return known;
            if (!visiting.Add(node))
            {
                Debug.LogWarning($"Ciclo de requisitos envolvendo o upgrade '{node.name}'.", node);
                return 0;
            }

            int depth = 0;
            if (node.prerequisites != null)
            {
                foreach (UpgradeNode_SO prerequisite in node.prerequisites)
                {
                    if (prerequisite == null || !treeNodes.Contains(prerequisite)) continue;
                    depth = Mathf.Max(depth, Depth(prerequisite) + 1);
                }
            }

            visiting.Remove(node);
            depths[node] = depth;
            return depth;
        }

        foreach (UpgradeNode_SO node in treeNodes) Depth(node);
        return depths;
    }

    private static void ClearChildren(RectTransform container)
    {
        if (container == null) return;
        for (int i = container.childCount - 1; i >= 0; i--)
        {
            Destroy(container.GetChild(i).gameObject);
        }
    }
}
