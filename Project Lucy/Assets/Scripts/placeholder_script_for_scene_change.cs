using UnityEngine;

public class placeholder_script_for_scene_change : MonoBehaviour
{
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            SceneControl.instance.NextLevel();
        }
    }
}
