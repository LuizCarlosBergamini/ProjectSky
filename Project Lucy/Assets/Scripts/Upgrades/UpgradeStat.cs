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

    public static string FormatValue(float value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
