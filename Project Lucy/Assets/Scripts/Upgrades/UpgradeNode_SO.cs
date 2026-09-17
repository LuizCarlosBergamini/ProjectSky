using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Upgrade/New Node")]
public class UpgradeNode_SO : ScriptableObject
{
    [Tooltip("Id estavel usado apenas para salvar/carregar. Nao mude depois de publicado.")]
    public string nodeId;
    public string nodeName;
    [TextArea] public string nodeDescription;
    public Sprite nodeIcon;

    [Header("Bonus")]
    public UpgradeStat stat;
    [Tooltip("Valor somado ao atributo base do jogador.")]
    public float bonusValue;

    [Header("Custo")]
    [Tooltip("Itens removidos do inventario na compra.")]
    public List<InventorySlot> cost = new();

    [Header("Requisitos")]
    [Tooltip("Nos que precisam ser comprados antes deste.")]
    public List<UpgradeNode_SO> prerequisites = new();

    [Tooltip("Marcado: exige todos os requisitos. Desmarcado: basta um (bifurcacoes).")]
    public bool requireAllPrerequisites = true;

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Seeded once from the asset name; after that the id is left alone so saves stay valid on rename.
        if (string.IsNullOrWhiteSpace(nodeId)) nodeId = name;
    }
#endif
}
