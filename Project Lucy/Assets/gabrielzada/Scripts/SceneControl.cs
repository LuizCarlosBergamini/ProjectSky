using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneControl : MonoBehaviour
{
    public static SceneControl instance;

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

    public void NextLevel()
    {
        int currentscene = SceneManager.GetActiveScene().buildIndex;
        int nextscene = currentscene + 1;

        if (nextscene >= SceneManager.sceneCountInBuildSettings)
        {
            Debug.LogWarning("Não há próxima cena!");
            return;
        }
        else
        {
            SceneManager.LoadScene(nextscene);
        }
    }

    public void LoadScene(string cena)
    { 
        if(Application.CanStreamedLevelBeLoaded(cena))
        {
            SceneManager.LoadScene(cena);
        }
        else
        {
            Debug.LogError($"A cena '{cena}' não está no Build Settings!");
        }
    }

}
