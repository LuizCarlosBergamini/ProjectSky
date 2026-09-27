using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws a SceneReference as a scene picker and keeps its stored path in sync with the picked asset
/// (renames and moves included). Warns when the scene is missing from Build Settings, since loading it
/// at runtime would fail.
/// </summary>
[CustomPropertyDrawer(typeof(SceneReference))]
public class SceneReferenceDrawer : PropertyDrawer
{
    private const float WarningHeight = 20f;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        SerializedProperty asset = property.FindPropertyRelative("sceneAsset");
        SerializedProperty path = property.FindPropertyRelative("scenePath");

        EditorGUI.BeginProperty(position, label, property);

        Rect fieldRect = new(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        EditorGUI.BeginChangeCheck();
        Object picked = EditorGUI.ObjectField(fieldRect, label, asset.objectReferenceValue, typeof(SceneAsset), false);
        if (EditorGUI.EndChangeCheck()) asset.objectReferenceValue = picked;

        string currentPath = asset.objectReferenceValue != null ? AssetDatabase.GetAssetPath(asset.objectReferenceValue) : "";
        if (path.stringValue != currentPath) path.stringValue = currentPath;

        if (NeedsWarning(currentPath))
        {
            Rect warningRect = new(position.x, fieldRect.yMax + 2f, position.width, WarningHeight - 2f);
            EditorGUI.HelpBox(warningRect, "Cena fora do Build Settings: nao vai carregar no jogo.", MessageType.Warning);
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        SerializedProperty asset = property.FindPropertyRelative("sceneAsset");
        string currentPath = asset.objectReferenceValue != null ? AssetDatabase.GetAssetPath(asset.objectReferenceValue) : "";
        return EditorGUIUtility.singleLineHeight + (NeedsWarning(currentPath) ? WarningHeight : 0f);
    }

    private static bool NeedsWarning(string scenePath)
    {
        if (string.IsNullOrEmpty(scenePath)) return false;

        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled && scene.path == scenePath) return false;
        }

        return true;
    }
}
