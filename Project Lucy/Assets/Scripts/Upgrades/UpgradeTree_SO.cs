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
}
