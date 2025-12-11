using UnityEngine;

public class TeleportToSpawnOnCollision : MonoBehaviour
{
    public Transform teleportSpawn;
    private void OnTriggerEnter2D(Collider2D collider)
    {
        if (collider.gameObject.CompareTag("Player"))
        {
            collider.transform.position = teleportSpawn.position;
        }
    }
}
