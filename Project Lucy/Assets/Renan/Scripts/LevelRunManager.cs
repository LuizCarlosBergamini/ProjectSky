using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns one attempt at a mission level. Put a single instance in every level scene
/// (not in the hub). Unlike the other managers this one is deliberately NOT persistent:
/// entering the level again starts a fresh run.
/// </summary>
public class LevelRunManager : MonoBehaviour
{
    public static LevelRunManager instance;

    [Tooltip("Cena do hub para onde o jogador volta ao concluir a fase.")]
    [SerializeField] private string _hubSceneName;

    [Tooltip("Chamado ao morrer, antes de recarregar a fase.")]
    [SerializeField] private UnityEvent _onPlayerDied;

    [Tooltip("Chamado ao concluir a fase, antes de voltar para o hub.")]
    [SerializeField] private UnityEvent _onRunFinished;

    private bool _transitioning;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
    }

    private void Start()
    {
        // Everything picked up from here on can be taken back if the player dies.
        InventoryManager.instance?.BeginRun();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    /// <summary>Death: drop everything gathered in this attempt and restart the level.</summary>
    public void HandlePlayerDeath()
    {
        if (_transitioning) return;
        _transitioning = true;

        InventoryManager.instance?.RollbackRun();
        _onPlayerDied?.Invoke();

        LoadScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>Level cleared: keep everything gathered and go back to the hub.</summary>
    public void FinishRun()
    {
        if (_transitioning) return;
        _transitioning = true;

        InventoryManager.instance?.CommitRun();
        _onRunFinished?.Invoke();

        LoadScene(string.IsNullOrWhiteSpace(_hubSceneName)
            ? SceneManager.GetActiveScene().name
            : _hubSceneName);
    }

    private void LoadScene(string sceneName)
    {
        if (LevelManager.instance != null)
        {
            LevelManager.instance.LoadScene(sceneName);
            return;
        }

        SceneManager.LoadScene(sceneName);
    }
}
