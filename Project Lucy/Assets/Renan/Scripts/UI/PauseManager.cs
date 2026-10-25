using System;
using System.Collections.Generic;
using HierarchicalStateMachine;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns the game pause: Time.timeScale, the player's input, the paused audio and the InGame/Pause input (Esc, P,
/// gamepad Start). Created before the first scene loads from Resources/PauseManager (Tools > Lucy > Build Pause Menu)
/// and persistent, like SettingsManager, so no scene needs a copy. The screen is PauseMenuController, a child of the
/// same prefab; this class only shows and hides it.
/// Pausing is refused in the scenes listed in _disabledScenes (main menu, cutscene), during a scene fade, while the
/// upgrade screen, the boss selector or the game over screen is open, and once the player is dead.
/// </summary>
public class PauseManager : MonoBehaviour
{
    public const string ResourcePath = "PauseManager";
    private const string PauseActionPath = "InGame/Pause";

    public static PauseManager instance;

    /// <summary>Fired after the game froze (time, input and audio already paused).</summary>
    public static event Action OnPaused;

    /// <summary>Fired after the game is running again, also when a scene change ends the pause.</summary>
    public static event Action OnResumed;

    [Header("Input")]
    [Tooltip("InGame/Pause (Esc, P, Start do controle). Vazio = a acao InGame/Pause das acoes do projeto.")]
    [SerializeField] private InputActionReference _pauseAction;

    [Header("Tela")]
    [Tooltip("Painel de pause (filho deste prefab). Sem ele o jogo ainda pausa, so nao mostra nada.")]
    [SerializeField] private PauseMenuController _menu;

    [Header("Cenas")]
    [Tooltip("Cenas onde o pause nunca abre.")]
    [SerializeField] private string[] _disabledScenes = { "MainMenu", "cutscene" };

    [Tooltip("Cena carregada pelo botao Menu principal.")]
    [SerializeField] private string _mainMenuScene = "MainMenu";

    [Header("Comportamento")]
    [Tooltip("Pausa sozinho quando a janela do jogo perde o foco (Alt+Tab).")]
    [SerializeField] private bool _pauseOnFocusLost = true;

    [Tooltip("Mantem tocando os AudioSources do grupo de musica enquanto os efeitos ficam pausados.")]
    [SerializeField] private bool _keepMusicPlaying = true;

    [Tooltip("Grupo do Main.mixer que conta como musica.")]
    [SerializeField] private string _musicGroupName = "Music";

    [Header("Eventos")]
    public UnityEvent onPause;
    public UnityEvent onUnpause;

    private readonly List<AudioSource> _musicSources = new();

    private InputAction _action;
    private PlayerStateDriver _player;
    private bool _playerInputWasEnabled;
    private float _previousTimeScale = 1f;
    private int _toggledFrame = -1;
    private bool _leaving;

    public bool IsPaused { get; private set; }

    /// <summary>Shortcut for scripts that read input outside the player (dialogs, NPCs) and must ignore it while paused.</summary>
    public static bool IsGamePaused => instance != null && instance.IsPaused;

    /// <summary>The pause panel, for anyone that needs to know whether a submenu (the options) is open.</summary>
    public PauseMenuController Menu => _menu;

    /// <summary>Whether <see cref="Pause"/> would freeze the game right now.</summary>
    public bool CanPause => !IsPaused && !_leaving && !IsBlocked();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Keeps Enter Play Mode without domain reload from seeing the last session's instance and listeners.
        instance = null;
        OnPaused = null;
        OnResumed = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null) return;

        GameObject prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab != null)
        {
            Instantiate(prefab).name = prefab.name;
            return;
        }

        Debug.LogWarning("Prefab Resources/PauseManager nao encontrado: rode Tools > Lucy > Build Pause Menu. " +
            "O jogo ainda pausa, mas sem painel.");
        new GameObject(nameof(PauseManager)).AddComponent<PauseManager>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        _action = _pauseAction != null ? _pauseAction.action : InputSystem.actions?.FindAction(PauseActionPath);
        if (_action == null) Debug.LogWarning($"Acao {PauseActionPath} nao encontrada: o pause nao abre pelo teclado.", this);

        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDestroy()
    {
        if (instance != this) return;

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        if (IsPaused) Resume();
        instance = null;
    }

    private void Update()
    {
        if (_action == null || !_action.WasPressedThisFrame()) return;

        // The panel can resume on the same press (UI/Cancel is also Esc); one press toggles once.
        if (_toggledFrame == Time.frameCount) return;

        if (!IsPaused)
        {
            Pause();
            return;
        }

        // While the options are open (and on the frame they closed) Esc belongs to them.
        if (_menu != null && _menu.IsCapturingInput) return;
        Resume();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus && _pauseOnFocusLost) Pause();
    }

    #region Pause / Resume

    /// <summary>Freezes the game and shows the panel. Returns false when pausing is not allowed right now.</summary>
    public bool Pause()
    {
        if (!CanPause) return false;

        IsPaused = true;
        _toggledFrame = Time.frameCount;

        _previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        // Remembered so a pause over a dialog (input already locked) does not hand control back on resume.
        _player = ResolvePlayer();
        _playerInputWasEnabled = _player != null && _player.IsInputEnabled;
        if (_player != null) _player.SetInputEnabled(false);

        PauseAudio();

        if (_menu != null) _menu.Show();

        onPause?.Invoke();
        OnPaused?.Invoke();
        return true;
    }

    /// <summary>Hides the panel and puts time, input and audio back as they were before the pause.</summary>
    public void Resume()
    {
        if (!IsPaused) return;

        IsPaused = false;
        _toggledFrame = Time.frameCount;

        if (_menu != null) _menu.Hide();

        Time.timeScale = _previousTimeScale;
        if (_player != null && _playerInputWasEnabled) _player.SetInputEnabled(true);
        _player = null;

        ResumeAudio();

        onUnpause?.Invoke();
        OnResumed?.Invoke();
    }

    public void TogglePause()
    {
        if (IsPaused) Resume();
        else Pause();
    }

    /// <summary>
    /// Loads the main menu with the fade. The game stays frozen behind the fade; the pause ends when the scene
    /// loads (see <see cref="HandleSceneLoaded"/>).
    /// </summary>
    public void ReturnToMainMenu()
    {
        if (!IsPaused || _leaving) return;

        _leaving = true;
        if (_menu != null) _menu.Hide();
        LevelManager.Load(_mainMenuScene);
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;

        _leaving = false;
        if (!IsPaused) return;

        // The new scene starts unpaused; the time scale saved before the pause belonged to the old one.
        _previousTimeScale = 1f;
        _player = null;
        _musicSources.Clear();
        Resume();
    }

    #endregion

    #region Rules

    private bool IsBlocked()
    {
        if (Array.IndexOf(_disabledScenes, SceneManager.GetActiveScene().name) >= 0) return true;

        if (LevelManager.instance != null && LevelManager.instance.IsLoading) return true;

        // These screens already freeze the game and own Esc while open.
        if (UpgradeCanvas.instance != null && UpgradeCanvas.instance.IsOpen) return true;
        if (BossSelectorUI.instance != null && BossSelectorUI.instance.IsOpen) return true;

        GameManager gameManager = GameManager.instance;
        if (gameManager != null && gameManager.gameOverUI != null && gameManager.gameOverUI.activeInHierarchy) return true;

        PlayerStateDriver player = ResolvePlayer();
        if (player != null && player.IsDead) return true;

        // Someone else froze the game (a screen not listed above); pausing on top would unfreeze it on resume.
        return Time.timeScale <= 0f;
    }

    private PlayerStateDriver ResolvePlayer()
    {
        // Only runs on a pause press, never per frame. The player is rebuilt with every scene load.
        if (_player == null)
        {
            GameObject playerObject = GameObject.FindWithTag("Player");
            if (playerObject != null) _player = playerObject.GetComponent<PlayerStateDriver>();
        }

        return _player;
    }

    #endregion

    #region Audio

    private void PauseAudio()
    {
        _musicSources.Clear();
        if (_keepMusicPlaying)
        {
            // AudioListener.pause stops every source except the ones that ignore it: the music keeps going.
            foreach (AudioSource source in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            {
                if (source.ignoreListenerPause || !IsMusic(source)) continue;
                source.ignoreListenerPause = true;
                _musicSources.Add(source);
            }
        }

        AudioListener.pause = true;
    }

    private void ResumeAudio()
    {
        AudioListener.pause = false;

        foreach (AudioSource source in _musicSources)
        {
            if (source != null) source.ignoreListenerPause = false;
        }

        _musicSources.Clear();
    }

    private bool IsMusic(AudioSource source)
    {
        return source.outputAudioMixerGroup != null && source.outputAudioMixerGroup.name == _musicGroupName;
    }

    #endregion
}
