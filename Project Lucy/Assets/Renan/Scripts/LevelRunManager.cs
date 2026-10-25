using HierarchicalStateMachine;
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

    [Tooltip("Portal de volta ao hub atras do ponto de inicio. So aparece quando a fase recomeca depois de uma morte.")]
    [SerializeField] private GameObject _deathPortal;

    // Scene that is being reloaded because the player died in it. Survives the reload (this manager does not),
    // and is consumed by the next LevelRunManager so entering the level from the Hub never shows the portal.
    private static string _diedInScene;

    private bool _transitioning;
    private bool _bossDefeated;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;

        string sceneName = SceneManager.GetActiveScene().name;
        bool restartedAfterDeath = _diedInScene == sceneName;
        _diedInScene = null;

        if (_deathPortal != null) _deathPortal.SetActive(restartedAfterDeath);
    }

    private void OnEnable()
    {
        BossGate.OnBossFightWon += HandleBossFightWon;
    }

    private void OnDisable()
    {
        BossGate.OnBossFightWon -= HandleBossFightWon;
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

        string sceneName = SceneManager.GetActiveScene().name;
        _diedInScene = sceneName;
        LoadScene(sceneName);
    }

    /// <summary>
    /// The death portal: giving up and going back to the hub. What was gathered since the restart is dropped,
    /// unless the boss has already been beaten in this attempt; then it counts as finishing the level.
    /// </summary>
    public void ReturnToHub()
    {
        if (_bossDefeated)
        {
            FinishRun();
            return;
        }

        if (_transitioning) return;
        _transitioning = true;

        InventoryManager.instance?.RollbackRun();
        LoadScene(HubSceneOrCurrent());
    }

    /// <summary>Level cleared: keep everything gathered and go back to the hub.</summary>
    public void FinishRun()
    {
        if (_transitioning) return;
        _transitioning = true;

        InventoryManager.instance?.CommitRun();
        _onRunFinished?.Invoke();

        LoadScene(HubSceneOrCurrent());
    }

    private void HandleBossFightWon(EnemyStateDriver boss)
    {
        _bossDefeated = true;
    }

    private string HubSceneOrCurrent()
    {
        return string.IsNullOrWhiteSpace(_hubSceneName) ? SceneManager.GetActiveScene().name : _hubSceneName;
    }

    private void LoadScene(string sceneName)
    {
        LevelManager.Load(sceneName);
    }
}
