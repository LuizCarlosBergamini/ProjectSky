using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Everything about one boss that is not the fight itself. Assigned on the boss's EnemyStateDriver; the boss
/// health bar reads its name, portrait and reward from here, and the boss selector builds one panel per asset
/// listed in BossProgressionManager. Name and portrait reuse Entity_SO so the same asset can also speak in dialogs.
/// </summary>
[CreateAssetMenu(menuName = "Boss/New Boss")]
public class BossData_SO : ScriptableObject
{
    [Tooltip("Id estavel usado apenas para salvar o progresso. Nao mude depois de publicado.")]
    public string bossId;

    [Tooltip("Nome e retrato do chefe (mesmo asset usado nos dialogos).")]
    public Entity_SO entity;

    [Tooltip("Task do TaskManager cuja recompensa e entregue ao derrotar o chefe (ex: missao-boss).")]
    public string rewardTaskId;

    [Header("Selecao de chefe")]
    [Tooltip("Imagem grande do painel (cenario ou chefe).")]
    public Sprite previewImage;

    [Tooltip("Cor de destaque do painel: nome, borda e brilho.")]
    public Color accentColor = Color.white;

    [Tooltip("Icones pequenos abaixo do nome (dificuldade, tipo de arena...).")]
    public List<BossInfoIcon> infoIcons = new();

    [Tooltip("Cena da luta. Precisa estar no Build Settings.")]
    public SceneReference scene = new();

    [Header("Requisitos")]
    [Tooltip("Chefes que precisam ser derrotados antes deste. Vazio = sempre liberado.")]
    public List<BossData_SO> prerequisites = new();

    [Tooltip("Marcado: exige todos os requisitos. Desmarcado: basta um (bifurcacoes).")]
    public bool requireAllPrerequisites = true;

    [Tooltip("Texto do painel bloqueado. Vazio = 'Derrote <requisitos> para desbloquear'.")]
    public string lockedHint;

    [Header("Adaptacao")]
    [Tooltip("Como o chefe fica mais forte conforme os upgrades do jogador. Vazio = nao se adapta.")]
    public BossUpgradeScaling_SO upgradeScaling;

    /// <summary>Name shown in the UI: the entity's name, or the asset name when there is none.</summary>
    public string DisplayName => entity != null && !string.IsNullOrWhiteSpace(entity.entityName) ? entity.entityName : name;

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Seeded once from the asset name; after that the id is left alone so saves stay valid on rename.
        if (string.IsNullOrWhiteSpace(bossId)) bossId = name;
    }
#endif
}

/// <summary>One small icon in a boss panel's info row. Label is drawn next to it, tooltip on hover.</summary>
[Serializable]
public class BossInfoIcon
{
    public Sprite icon;

    [Tooltip("Texto curto ao lado do icone (ex: '2'). Pode ficar vazio.")]
    public string label;

    [Tooltip("Texto mostrado ao passar o mouse.")]
    public string tooltip;
}
