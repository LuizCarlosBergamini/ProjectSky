/// <summary>
/// Asset paths and object names the main menu tests rely on, kept in one place so a rename only touches this file.
/// </summary>
public static class MenuTestPaths
{
    public const string MainMenuScene = MainMenuBuilder.ScenePath;
    public const string OptionsMenuPrefab = "Assets/Renan/Prefabs/OptionsMenu.prefab";
    public const string MainMixer = "Assets/Main.mixer";

    /// <summary>Skips a test when the builder-generated scene does not exist yet, instead of failing it.</summary>
    public static void RequireMainMenuScene()
    {
        if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(MainMenuScene) == null)
            NUnit.Framework.Assert.Ignore("Run Tools > Lucy > Build Main Menu first.");
    }

    /// <summary>Skips a test when the builder-generated Options prefab does not exist yet, instead of failing it.</summary>
    public static void RequireOptionsPrefab()
    {
        if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(OptionsMenuPrefab) == null)
            NUnit.Framework.Assert.Ignore("Generate the Options menu prefab with its Tools > Lucy builder first.");
    }

    public const string BackgroundImageName = "BackgroundImage";
    public const string NewGameButtonName = "NewGameButton";
    public const string ContinueButtonName = "ContinueButton";
    public const string OptionsButtonName = "OptionsButton";
}
