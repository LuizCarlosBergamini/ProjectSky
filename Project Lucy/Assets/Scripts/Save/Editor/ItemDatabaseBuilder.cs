using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps Resources/ItemDatabase listing every Item_SO in the project. Runs by itself whenever an asset is imported,
/// moved or deleted; "Tools > Lucy > Rebuild Item Database" does the same on demand. Also warns about missing or
/// repeated itemIds, which would break saved inventories.
/// </summary>
public class ItemDatabaseBuilder : AssetPostprocessor
{
    public const string AssetPath = "Assets/Resources/" + ItemDatabase.ResourcePath + ".asset";

    [MenuItem("Tools/Lucy/Rebuild Item Database")]
    public static void Rebuild()
    {
        ItemDatabase database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(AssetPath);
        if (database == null)
        {
            database = ScriptableObject.CreateInstance<ItemDatabase>();
            AssetDatabase.CreateAsset(database, AssetPath);
        }

        List<Item_SO> items = AssetDatabase.FindAssets("t:Item_SO")
            .Select(guid => AssetDatabase.LoadAssetAtPath<Item_SO>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(item => item != null)
            .OrderBy(item => item.itemId)
            .ToList();

        HashSet<string> seen = new();
        foreach (Item_SO item in items)
        {
            if (string.IsNullOrWhiteSpace(item.itemId))
                Debug.LogWarning($"[Save] Item '{item.name}' sem itemId: ele nao sera salvo.", item);
            else if (!seen.Add(item.itemId))
                Debug.LogWarning($"[Save] itemId '{item.itemId}' repetido em '{item.name}'.", item);
        }

        if (database.Items.SequenceEqual(items)) return;

        database.SetItems(items);
        EditorUtility.SetDirty(database);
        AssetDatabase.SaveAssetIfDirty(database);
        Debug.Log($"[Save] ItemDatabase atualizado: {items.Count} itens.");
    }

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        bool touchesItems = imported.Concat(deleted).Concat(moved)
            .Any(path => path.EndsWith(".asset") && path != AssetPath);
        if (!touchesItems) return;

        // Deferred: creating or loading assets inside the import callback is not allowed.
        EditorApplication.delayCall -= Rebuild;
        EditorApplication.delayCall += Rebuild;
    }
}
