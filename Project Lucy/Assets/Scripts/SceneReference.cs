using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Inspector-friendly reference to a scene: the scene asset is picked in the editor and its path is stored
/// next to it, so nobody types a scene name by hand. Loading still goes by name (like the rest of the
/// project), but <see cref="IsInBuild"/> checks the scene is actually in Build Settings first.
/// The path is synced by SceneReferenceDrawer whenever the field is drawn, which also picks up renames.
/// </summary>
[Serializable]
public class SceneReference
{
#if UNITY_EDITOR
    // Editor-only: SceneAsset does not exist in a player build. The drawer restricts it to scenes.
    [SerializeField] private UnityEngine.Object sceneAsset;
#endif

    [SerializeField] private string scenePath = "";

    /// <summary>Project-relative path, e.g. "Assets/Scenes/Level1.unity". Empty when unassigned.</summary>
    public string ScenePath => scenePath;

    public string SceneName => string.IsNullOrEmpty(scenePath) ? "" : Path.GetFileNameWithoutExtension(scenePath);

    /// <summary>Index among the enabled Build Settings scenes, or -1 when the scene is not in the build.</summary>
    public int BuildIndex => string.IsNullOrEmpty(scenePath) ? -1 : SceneUtility.GetBuildIndexByScenePath(scenePath);

    public bool IsAssigned => !string.IsNullOrEmpty(scenePath);

    public bool IsInBuild => BuildIndex >= 0;

    public override string ToString() => IsAssigned ? scenePath : "(nenhuma cena)";
}
