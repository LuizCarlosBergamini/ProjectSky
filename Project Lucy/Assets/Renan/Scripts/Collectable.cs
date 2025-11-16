using Unity.VisualScripting;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Collectable : MonoBehaviour
{
    [SerializeField] private InventoryManager _inventoryManager;
    public Item_SO item;

    public void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            _inventoryManager.AddItem(item);
            Destroy(gameObject);
        }
    }
}
