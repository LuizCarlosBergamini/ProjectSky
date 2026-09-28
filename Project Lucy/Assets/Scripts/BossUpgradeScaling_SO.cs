using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>What a boss can get stronger in when it adapts to the player's upgrades.</summary>
public enum BossScalingStat
{
    MaxHealth, // answers player damage: the fight does not get shorter by as much
    Damage,    // answers player life: hits still hurt, a little more
    Speed      // answers player speed: moves and shoots a little faster
}

/// <summary>One combined adaptation, e.g. MaxHealth x1.25.</summary>
[Serializable]
public struct BossAdaptation
{
    public BossScalingStat stat;
    public float multiplier;

    public BossAdaptation(BossScalingStat stat, float multiplier)
    {
        this.stat = stat;
        this.multiplier = multiplier;
    }

    public override string ToString() => BossUpgradeScaling_SO.Describe(stat, multiplier);
}

/// <summary>"When the player owns this upgrade, the boss gets this much stronger in that stat."</summary>
[Serializable]
public class BossUpgradeResponse
{
    [Tooltip("Upgrade do jogador ao qual o chefe responde.")]
    public UpgradeNode_SO upgrade;

    [Tooltip("O que o chefe ganha quando esse upgrade foi comprado.")]
    public BossScalingStat bossStat;

    [Tooltip("Multiplicador aplicado ao chefe (1.25 = +25%). Regra usada: o chefe recupera metade da vantagem " +
             "percentual que o upgrade deu ao jogador. Use o menu de contexto para recalcular.")]
    [Min(1f)] public float multiplier = 1f;
}

/// <summary>
/// How one boss adapts to the player's upgrades. Assigned on the boss's BossData_SO: EnemyStateDriver applies the
/// multipliers when the boss spawns, the boss bar lists them, and the upgrade panel warns before a purchase.
/// Several responses to the same stat multiply together.
/// </summary>
[CreateAssetMenu(menuName = "Boss/Upgrade Scaling")]
public class BossUpgradeScaling_SO : ScriptableObject
{
    [Tooltip("Fracao da vantagem de cada upgrade que o chefe recupera ao recalcular (0.5 = metade). " +
             "Abaixo de 1 o jogador sempre fica mais forte do que antes da compra.")]
    [Range(0f, 1f)] public float compensation = 0.5f;

    public List<BossUpgradeResponse> responses = new();

    /// <summary>Product of the multipliers of every purchased upgrade that feeds <paramref name="stat"/>.</summary>
    public float GetMultiplier(BossScalingStat stat, UpgradeManager upgrades)
    {
        if (upgrades == null) return 1f;

        float total = 1f;
        foreach (BossUpgradeResponse response in responses)
        {
            if (response == null || response.bossStat != stat || response.upgrade == null) continue;
            if (upgrades.IsPurchased(response.upgrade)) total *= Mathf.Max(1f, response.multiplier);
        }

        return total;
    }

    /// <summary>The adaptations currently in effect, one per stat, in a fixed order.</summary>
    public List<BossAdaptation> GetActive(UpgradeManager upgrades)
    {
        List<BossAdaptation> active = new();
        foreach (BossScalingStat stat in (BossScalingStat[])Enum.GetValues(typeof(BossScalingStat)))
        {
            float multiplier = GetMultiplier(stat, upgrades);
            if (multiplier > 1.0001f) active.Add(new BossAdaptation(stat, multiplier));
        }

        return active;
    }

    /// <summary>The responses triggered by <paramref name="node"/>, for the upgrade panel's warning line.</summary>
    public IEnumerable<BossUpgradeResponse> ResponsesTo(UpgradeNode_SO node)
    {
        if (node == null) yield break;
        foreach (BossUpgradeResponse response in responses)
        {
            if (response != null && response.upgrade == node && response.multiplier > 1.0001f) yield return response;
        }
    }

    /// <summary>"+25% vida", "+5% dano", "+3% velocidade".</summary>
    public static string Describe(BossScalingStat stat, float multiplier)
    {
        int percent = Mathf.RoundToInt((multiplier - 1f) * 100f);
        return $"+{percent}% {StatName(stat)}";
    }

    public static string StatName(BossScalingStat stat)
    {
        return stat switch
        {
            BossScalingStat.MaxHealth => "vida",
            BossScalingStat.Damage => "dano",
            BossScalingStat.Speed => "velocidade",
            _ => stat.ToString()
        };
    }

    /// <summary>
    /// The balancing rule: the boss wins back <see cref="compensation"/> of the percentage each upgrade gives the
    /// player over their base stat. Damage upgrades feed MaxHealth, life upgrades feed Damage, speed feeds Speed.
    /// </summary>
    public void Recalculate(float playerBaseDamage, float playerBaseHealth, float playerBaseSpeed)
    {
        foreach (BossUpgradeResponse response in responses)
        {
            if (response == null || response.upgrade == null) continue;

            float playerBase = response.upgrade.stat switch
            {
                UpgradeStat.Damage => playerBaseDamage,
                UpgradeStat.MaxHealth => playerBaseHealth,
                UpgradeStat.MoveSpeed => playerBaseSpeed,
                _ => 0f
            };
            if (playerBase <= 0f) continue;

            float gain = response.upgrade.bonusValue / playerBase;
            response.multiplier = Mathf.Round((1f + compensation * gain) * 100f) / 100f;
        }
    }

    /// <summary>Which boss stat answers a player upgrade stat.</summary>
    public static BossScalingStat CounterFor(UpgradeStat stat)
    {
        return stat switch
        {
            UpgradeStat.Damage => BossScalingStat.MaxHealth,
            UpgradeStat.MaxHealth => BossScalingStat.Damage,
            _ => BossScalingStat.Speed
        };
    }

#if UNITY_EDITOR
    private const string PlayerPrefabPath = "Assets/Renan/Prefabs/Player.prefab";

    // Reads the player's real base stats so the numbers follow the Player prefab and PlayerData.
    [ContextMenu("Recalcular pela regra da metade")]
    private void RecalculateFromPlayerAssets()
    {
        if (!TryReadPlayerBase(out float damage, out float health, out float speed))
        {
            Debug.LogWarning($"{name}: nao encontrei os atributos base do jogador em {PlayerPrefabPath}.", this);
            return;
        }

        UnityEditor.Undo.RecordObject(this, "Recalcular adaptacao do chefe");
        Recalculate(damage, health, speed);
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"{name}: recalculado com dano {damage}, vida {health}, velocidade {speed}.", this);
    }

    /// <summary>Base attack damage and max health from the Player prefab, run speed from its PlayerData.</summary>
    public static bool TryReadPlayerBase(out float damage, out float health, out float speed)
    {
        damage = health = speed = 0f;
        GameObject player = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        var driver = player != null ? player.GetComponent<HierarchicalStateMachine.PlayerStateDriver>() : null;
        if (driver == null) return false;

        var so = new UnityEditor.SerializedObject(driver);
        damage = so.FindProperty("attackDamage").floatValue;
        health = so.FindProperty("maxHealth").floatValue;
        speed = driver.Data != null ? driver.Data.runMaxSpeed : 0f;
        return damage > 0f && health > 0f && speed > 0f;
    }
#endif
}
