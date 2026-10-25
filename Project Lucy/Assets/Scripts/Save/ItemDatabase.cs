using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every Item_SO in the project, so a saved itemId can be turned back into its asset. Lives at
/// Resources/ItemDatabase and is kept up to date in the Editor automatically (ItemDatabaseBuilder); there is
/// nothing to fill in by hand.
/// </summary>
public class ItemDatabase : ScriptableObject
{
    public const string ResourcePath = "ItemDatabase";

    [SerializeField] private List<Item_SO> _items = new();

    private Dictionary<string, Item_SO> _byId;

    public IReadOnlyList<Item_SO> Items => _items;

    public static ItemDatabase Load()
    {
        ItemDatabase database = Resources.Load<ItemDatabase>(ResourcePath);
        if (database == null) Debug.LogWarning("Resources/ItemDatabase nao encontrado: os itens do save nao serao restaurados.");
        return database;
    }

    public Item_SO Find(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        if (_byId == null)
        {
            _byId = new Dictionary<string, Item_SO>();
            foreach (Item_SO item in _items)
            {
                if (item != null && !string.IsNullOrEmpty(item.itemId)) _byId[item.itemId] = item;
            }
        }

        return _byId.TryGetValue(itemId, out Item_SO found) ? found : null;
    }

#if UNITY_EDITOR
    public void SetItems(IEnumerable<Item_SO> items)
    {
        _items = new List<Item_SO>(items);
        _byId = null;
    }
#endif
}
