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
            LevelManager.Load(System.IO.Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(nextscene)));
        }
    }

    public void LoadScene(string cena)
    { 
        if(Application.CanStreamedLevelBeLoaded(cena))
        {
            LevelManager.Load(cena);
        }
        else
        {
            Debug.LogError($"A cena '{cena}' não está no Build Settings!");
        }
    }

}
