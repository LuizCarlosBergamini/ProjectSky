using System.Globalization;

/// <summary>Player stat an upgrade node adds to. New values also need a hook in PlayerStateDriver.</summary>
public enum UpgradeStat
{
    Damage,
    MaxHealth,
    MoveSpeed
}

/// <summary>State of a node from the player's point of view, in the order the UI cares about.</summary>
public enum UpgradeNodeState
{
    Locked,
    Unaffordable,
    Available,
    Purchased
}

public enum UpgradePurchaseResult
{
    Success,
    InvalidNode,
    AlreadyPurchased,
    Locked,
    NotEnoughItems,
    NoInventory
}

public static class UpgradeStatText
{
    public static string DisplayName(this UpgradeStat stat)
    {
        return stat switch
        {
            UpgradeStat.Damage => "Dano",
            UpgradeStat.MaxHealth => "Vida",
            UpgradeStat.MoveSpeed => "Velocidade",
            _ => stat.ToString()
        };
    }

    /// <summary>"+5 Dano", "+0.75 Velocidade".</summary>
    public static string FormatBonus(this UpgradeStat stat, float value)
    {
        string sign = value >= 0f ? "+" : "";
        return $"{sign}{FormatValue(value)} {stat.DisplayName()}";
    }

    /// <summary>"+10% Vel. Ataque" for a 0.1 attack speed bonus.</summary>
    public static string FormatAttackSpeedBonus(float value)
    {
        string sign = value >= 0f ? "+" : "";
        return $"{sign}{FormatValue(value * 100f)}% Vel. Ataque";
    }

    /// <summary>The stat bonus, followed by the attack speed bonus when there is one.</summary>
    public static string FormatBonuses(UpgradeStat stat, float value, float attackSpeed)
    {
        string text = stat.FormatBonus(value);
        return attackSpeed != 0f ? $"{text}  {FormatAttackSpeedBonus(attackSpeed)}" : text;
    }

    public static string FormatValue(float value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
