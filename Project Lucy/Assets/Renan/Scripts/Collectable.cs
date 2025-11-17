using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Collectable : MonoBehaviour
{
    public Item_SO item;

    public void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") && InventoryManager.instance != null)
        {
            InventoryManager.instance.AddItem(item);
            Destroy(gameObject);
        }
    }
}
