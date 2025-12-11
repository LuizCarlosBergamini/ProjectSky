using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class InventorySlot
{
    public Item_SO item;
    public int quantity;
}

public class InventoryManager : MonoBehaviour
{
    public static InventoryManager instance;

    private readonly Dictionary<string, InventorySlot> _inventory = new();

    private void Awake()
    {
        if (instance != null)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void AddItem(Item_SO item, int quantity)
    {
        quantity = Math.Max(quantity, 1);
        if (_inventory.TryGetValue(item.itemId, out InventorySlot slot))
        {
            slot.quantity += quantity;
            _inventory.Remove(item.itemId);
            _inventory.Add(item.itemId, slot);
        } else
        {
            InventorySlot newSlot = new()
            {
                quantity = quantity,
                item = item
            };
            _inventory.Add(item.itemId, newSlot);
        }

        if (TaskManager.instance != null)
        {
            TaskManager.instance.ValidateIfTaskCompleted();
        }
    }

    public void AddItem(Item_SO item)
    {
        AddItem(item, 1);
    }

    public void RemoveItem(string itemId, int quantity)
    {
        quantity = Math.Max(quantity, 1);
        if (_inventory.TryGetValue(itemId, out InventorySlot slot))
        {
            if (quantity >= slot.quantity)
            {
                _inventory.Remove(itemId);
            }
            else {
                slot.quantity -= quantity;
                _inventory.Add(itemId, slot);
            }
        }
    }

    public void RemoveItem(string itemId)
    {
        RemoveItem(itemId, 1);
    }

    public InventorySlot GetItem(string itemId)
    {
        if (_inventory.TryGetValue(itemId, out InventorySlot slot))
        {
            return slot;
        }

        return null;
    }
}
