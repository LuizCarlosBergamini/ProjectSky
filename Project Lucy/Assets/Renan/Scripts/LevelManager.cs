using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;


public class LevelManager : MonoBehaviour
{
    public static LevelManager instance;
    [SerializeField] Animator canvasTransition;
    //[SerializeField] FadeUI gameOverScreen;
    [SerializeField] UnityEvent onLoad;
    public bool IsLoading { get; private set; } = false;

    private void Awake()
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

    void Start()
    {
        onLoad?.Invoke();
        //FadeOut();
    }

    public IEnumerator LoadAsync(string sceneName)
    {
        if (IsLoading) yield break;
        var scene = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        scene.allowSceneActivation = false;
        canvasTransition.SetTrigger("End");

        IsLoading = true;
        yield return new WaitForSecondsRealtime(2f);

        yield return new WaitWhile(() => scene.progress < 0.9f);

        scene.allowSceneActivation = true;
        yield return new WaitForSecondsRealtime(2f);

        canvasTransition.SetTrigger("Start");
        onLoad?.Invoke();
        IsLoading = false;
    }

    public void FadeIn()
    {
        canvasTransition.SetTrigger("End");
    }

    public void FadeOut()
    {
        canvasTransition.SetTrigger("Start");
    }

    public void LoadScene(string sceneName)
    {
        StartCoroutine(LoadAsync(sceneName));
    }

    //async public void GameOver(float delay)
    //{
    //    if (gameOverScreen == null) return;
    //    if (IsLoading) return;

    //    await Task.Delay((int)(delay * 1000f));

    //    var scene = SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().name, LoadSceneMode.Single);
    //    scene.allowSceneActivation = false;
    //    canvasTransition.SetTrigger("End");
    //    IsLoading = true;
    //    await Task.Delay(3000);

    //    await gameOverScreen.ShowAsync();
    //    await Task.Delay(5000);
    //    await gameOverScreen.HideAsync();

    //    while (scene.progress < 0.9f)
    //    {
    //        await Task.Yield();
    //    }

    //    await Task.Delay(1000);

    //    scene.allowSceneActivation = true;
    //    canvasTransition.SetTrigger("Start");
    //    IsLoading = false;
    //}

    public void ReloadScene()
    {
        LoadScene(SceneManager.GetActiveScene().name);
    }

    public void CloseGame()
    {
    #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
    #else
        Application.Quit();
    #endif
    }
}
