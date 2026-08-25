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

    // Items picked up since the current level run started. Rolled back when the player dies,
    // discarded when the run is finished successfully.
    private readonly Dictionary<string, int> _runPickups = new();

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

    public void AddItem(Item_SO item, int quantity, bool trackAsRunPickup)
    {
        if (item == null) return;
        quantity = Math.Max(quantity, 1);
        if (_inventory.TryGetValue(item.itemId, out InventorySlot slot))
        {
            slot.quantity += quantity;
        } else
        {
            InventorySlot newSlot = new()
            {
                quantity = quantity,
                item = item
            };
            _inventory.Add(item.itemId, newSlot);
        }

        if (trackAsRunPickup)
        {
            _runPickups.TryGetValue(item.itemId, out int picked);
            _runPickups[item.itemId] = picked + quantity;
        }

        if (TaskManager.instance != null)
        {
            TaskManager.instance.ValidateIfTaskCompleted();
        }
    }

    public void AddItem(Item_SO item, int quantity)
    {
        AddItem(item, quantity, true);
    }

    public void AddItem(Item_SO item)
    {
        AddItem(item, 1, true);
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
            }
        }

        // Anything spent no longer belongs to the run ledger, otherwise a later rollback
        // would remove it a second time.
        if (_runPickups.TryGetValue(itemId, out int picked))
        {
            int left = picked - quantity;
            if (left > 0) _runPickups[itemId] = left;
            else _runPickups.Remove(itemId);
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

    public int GetQuantity(string itemId)
    {
        InventorySlot slot = GetItem(itemId);
        return slot != null ? slot.quantity : 0;
    }

    #region Run Ledger

    /// <summary>Starts tracking pickups for a new level attempt.</summary>
    public void BeginRun()
    {
        _runPickups.Clear();
    }

    /// <summary>Gives back everything collected during the current attempt.</summary>
    public void RollbackRun()
    {
        foreach (KeyValuePair<string, int> pickup in _runPickups)
        {
            if (_inventory.TryGetValue(pickup.Key, out InventorySlot slot))
            {
                if (pickup.Value >= slot.quantity)
                {
                    _inventory.Remove(pickup.Key);
                }
                else
                {
                    slot.quantity -= pickup.Value;
                }
            }
        }

        _runPickups.Clear();

        if (TaskManager.instance != null)
        {
            TaskManager.instance.RefreshTasksProgress();
        }
    }

    /// <summary>Keeps everything collected during the current attempt.</summary>
    public void CommitRun()
    {
        _runPickups.Clear();
    }

    #endregion
}
