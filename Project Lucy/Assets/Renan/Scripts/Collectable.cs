using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Collectable : MonoBehaviour
{
    public Item_SO item;
    [SerializeField] private AudioClip _collectClip;

    public void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") && InventoryManager.instance != null)
        {
            InventoryManager.instance.AddItem(item);
            Destroy(gameObject);
            if (_collectClip != null && AudioManager.instance != null)
            {
                AudioManager.instance.PlayWithVariation(_collectClip);
            }
        }
    }
}
