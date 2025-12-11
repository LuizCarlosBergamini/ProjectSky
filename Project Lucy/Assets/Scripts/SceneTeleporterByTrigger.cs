using UnityEngine;

public class SceneTeleporterByTrigger : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("GameplayScene");
        }
    }

    public void OnEventTriggered(string sceneName)
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
    }
}
