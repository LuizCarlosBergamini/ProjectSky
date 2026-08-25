using UnityEngine;

public class SceneTeleporterByTrigger : MonoBehaviour
{
    public string onTriggerScene;
    
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(onTriggerScene);
        }
    }

    public void OnEventTriggered(string sceneName)
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
    }
}
