using UnityEngine;

public class SceneTeleporterByTrigger : MonoBehaviour
{
    public string onTriggerScene;
    
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            LevelManager.Load(onTriggerScene);
        }
    }

    public void OnEventTriggered(string sceneName)
    {
        LevelManager.Load(sceneName);
    }
}
