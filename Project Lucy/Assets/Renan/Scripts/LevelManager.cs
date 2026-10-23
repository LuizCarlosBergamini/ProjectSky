using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;


/// <summary>
/// Persistent scene loader with the fade. Every scene change in the game goes through <see cref="Load"/>, so it
/// always fades out and back in. One instance is created from Resources/LevelManager before the first scene
/// loads (whatever scene Play starts in); a copy placed in a scene destroys itself.
/// </summary>
public class LevelManager : MonoBehaviour
{
    public const string ResourcePath = "LevelManager";

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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Keeps Enter Play Mode without domain reload from seeing the last session's instance.
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    /// <summary>The loader, created from Resources/LevelManager when none exists yet. Null only if the prefab is missing.</summary>
    public static LevelManager EnsureInstance()
    {
        if (instance != null) return instance;

        LevelManager prefab = Resources.Load<LevelManager>(ResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning("Prefab Resources/LevelManager nao encontrado: as cenas vao carregar sem fade.");
            return null;
        }

        LevelManager created = Instantiate(prefab);
        created.name = prefab.name;
        return instance;
    }

    /// <summary>
    /// The one way to change scene: fades out, loads <paramref name="sceneName"/> and fades back in. Calls made while
    /// a load is running are ignored, so triggers that fire twice load once. Without the prefab it loads directly.
    /// </summary>
    public static void Load(string sceneName)
    {
        LevelManager manager = EnsureInstance();
        if (manager != null)
        {
            manager.LoadScene(sceneName);
            return;
        }

        SceneManager.LoadScene(sceneName);
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
