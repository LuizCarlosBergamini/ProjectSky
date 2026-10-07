/// <summary>
/// Placeholder entry point for the save system, which does not exist yet. The main menu only talks to the save
/// data through these three methods, so wiring the real system in means replacing their bodies and nothing else.
/// The manager-side hooks the real implementation will need already exist:
/// UpgradeManager.GetPurchasedNodeIds/LoadPurchasedNodeIds and BossProgressionManager.GetDefeatedBossIds/LoadDefeatedBossIds.
/// </summary>
public static class SaveSystem
{
    /// <summary>
    /// True when there is a save the Continue button can load.
    /// TODO(save-system): return whether the most recent save slot exists (file on disk, PlayerPrefs key, cloud...).
    /// </summary>
    public static bool HasSave()
    {
        return false;
    }

    /// <summary>
    /// Restores the most recent save into the persistent managers and tells the caller which scene to open.
    /// Returns false when nothing could be loaded; the caller then stays on the menu.
    /// TODO(save-system): read the most recent slot, push its data into UpgradeManager.LoadPurchasedNodeIds,
    /// BossProgressionManager.LoadDefeatedBossIds and the inventory, and return the scene the player saved in.
    /// </summary>
    public static bool TryLoadMostRecent(out string sceneName)
    {
        sceneName = null;
        return false;
    }

    /// <summary>
    /// Called by New Game after the run state has been reset, before the first gameplay scene loads.
    /// TODO(save-system): create (or pick and clear) the slot the new game will save into.
    /// </summary>
    public static void CreateNewSave()
    {
    }
}
