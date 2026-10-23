using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Upgrade/New Tree")]
public class UpgradeTree_SO : ScriptableObject
{
    public string treeName;
    public Color treeColor = Color.white;
    public Sprite treeIcon;

    [Tooltip("Todos os nos desta arvore. A ordem so desempata nos da mesma linha; as linhas vem dos requisitos.")]
    public List<UpgradeNode_SO> nodes = new();

    /// <summary>
    /// Level of each node, starting at 0: the longest chain of in-tree prerequisites above it. The upgrade
    /// menu lays its rows out from this, and the HUD uses it to know which purchased node is the highest.
    /// </summary>
    public Dictionary<UpgradeNode_SO, int> GetNodeDepths()
    {
        List<UpgradeNode_SO> treeNodes = new();
        if (nodes != null)
        {
            foreach (UpgradeNode_SO node in nodes)
            {
                if (node != null && !treeNodes.Contains(node)) treeNodes.Add(node);
            }
        }

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
}
