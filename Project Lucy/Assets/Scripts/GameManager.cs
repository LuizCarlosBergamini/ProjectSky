using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public GameObject gameOverUI;

    public static GameManager instance;

    public void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);

        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void GameOver()
    {
        if (gameOverUI != null) gameOverUI.SetActive(true);
    }

    public void RestartGame()
    {
        if (gameOverUI != null) gameOverUI.SetActive(false);

        // Restarting always means losing whatever was gathered in the current attempt.
        if (InventoryManager.instance != null) InventoryManager.instance.RollbackRun();

        LevelManager.Load(SceneManager.GetActiveScene().name);
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}
