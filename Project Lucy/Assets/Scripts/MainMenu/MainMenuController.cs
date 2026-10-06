using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The main menu of the MainMenu scene (build index 0, the game's entry point). Scene-specific: owns the New Game,
/// Continue, Options and Quit buttons and the art slots. Scenes load through LevelManager (the fade), falling back to
/// SceneManager like the rest of the project. Options are a separate reusable prefab: this menu only opens it and
/// decides what happens when it closes. Save data is only reached through SaveSystem.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("Botoes")]
    [SerializeField] private Button _newGameButton;
    [SerializeField] private Button _continueButton;
    [SerializeField] private Button _optionsButton;
    [Tooltip("Opcional. Escondido em builds WebGL, onde sair do jogo nao faz sentido.")]
    [SerializeField] private Button _quitButton;

    [Tooltip("Grupo dos botoes: fica nao-interativo enquanto as opcoes estao abertas ou uma cena carrega.")]
    [SerializeField] private CanvasGroup _buttonsGroup;

    [Header("Arte")]
    [Tooltip("Image de fundo em tela cheia (com CoverImage).")]
    [SerializeField] private Image _backgroundImage;

    [Tooltip("Arte final do fundo. Vazio = mantem o sprite que ja esta na BackgroundImage.")]
    [SerializeField] private Sprite _backgroundSprite;

    [Tooltip("Image do titulo/logo.")]
    [SerializeField] private Image _logoImage;

    [Tooltip("Arte final do logo. Vazio = mantem o sprite que ja esta na LogoImage.")]
    [SerializeField] private Sprite _logoSprite;

    [Tooltip("Texto no canto com a versao do jogo (Application.version).")]
    [SerializeField] private TextMeshProUGUI _versionLabel;

    [Header("Cenas")]
    [Tooltip("Primeira cena de gameplay, carregada pelo Novo Jogo.")]
    [SerializeField] private SceneReference _newGameScene = new();

    [Header("Opcoes")]
    [Tooltip("Prefab reutilizavel do menu de opcoes. E instanciado na primeira vez que for aberto.")]
    [SerializeField] private OptionsMenuController _optionsMenuPrefab;

    private OptionsMenuController _optionsMenu;
    private bool _loading;

    /// <summary>"v" + the version set in Player Settings.</summary>
    public static string VersionText => "v" + Application.version;

    public bool CanContinue => SaveSystem.HasSave();

    public bool IsLoading => _loading;

    public bool IsOptionsOpen => _optionsMenu != null && _optionsMenu.IsOpen;

    private void Awake()
    {
        // Coming back from a paused game must never leave the menu frozen.
        Time.timeScale = 1f;

        ApplyArt();

        if (_newGameButton != null) _newGameButton.onClick.AddListener(NewGame);
        if (_continueButton != null) _continueButton.onClick.AddListener(Continue);
        if (_optionsButton != null) _optionsButton.onClick.AddListener(OpenOptions);
        if (_quitButton != null)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            _quitButton.gameObject.SetActive(false);
#else
            _quitButton.onClick.AddListener(QuitGame);
#endif
        }

        if (_versionLabel != null) _versionLabel.text = VersionText;
    }

    private void Start()
    {
        RefreshButtons();
        SelectDefault();
    }

    private void OnDestroy()
    {
        if (_newGameButton != null) _newGameButton.onClick.RemoveListener(NewGame);
        if (_continueButton != null) _continueButton.onClick.RemoveListener(Continue);
        if (_optionsButton != null) _optionsButton.onClick.RemoveListener(OpenOptions);
        if (_quitButton != null) _quitButton.onClick.RemoveListener(QuitGame);
        if (_optionsMenu != null) _optionsMenu.Closed -= HandleOptionsClosed;
    }

    private void Update()
    {
        if (_loading || IsOptionsOpen) return;

        // Clicking empty space clears the EventSystem selection; give keyboard/gamepad a button back.
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null && eventSystem.currentSelectedGameObject == null) SelectDefault();
    }

    private void OnValidate()
    {
        // Lets the Inspector field preview the art in the Editor right away.
        ApplyArt();
    }

    #region Buttons

    /// <summary>Fresh run: resets the persistent run state, opens a new save slot and loads the first gameplay scene.</summary>
    public void NewGame()
    {
        if (_loading || IsOptionsOpen) return;

        string sceneName = _newGameScene != null ? _newGameScene.SceneName : "";
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("Novo Jogo sem cena: escolha a primeira cena de gameplay no MainMenuController.", this);
            return;
        }

        ResetRunState();

        // Hook for the save system: the new game's slot is created here, before anything can be saved into it.
        SaveSystem.CreateNewSave();

        LoadScene(sceneName);
    }

    /// <summary>Loads the most recent save. Does nothing (and stays disabled) while no save exists.</summary>
    public void Continue()
    {
        if (_loading || IsOptionsOpen || !CanContinue) return;

        // TODO(save-system): SaveSystem.TryLoadMostRecent is a placeholder that always fails. Once it restores the
        // managers and returns the saved scene, this path is complete and needs no other change.
        if (!SaveSystem.TryLoadMostRecent(out string sceneName) || string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("Nao foi possivel carregar o save mais recente.", this);
            RefreshButtons();
            return;
        }

        LoadScene(sceneName);
    }

    /// <summary>Opens the reusable Options menu over this one; focus comes back to the Options button on close.</summary>
    public void OpenOptions()
    {
        if (_loading || IsOptionsOpen) return;

        OptionsMenuController options = GetOptionsMenu();
        if (options == null)
        {
            Debug.LogWarning("Prefab do menu de opcoes nao configurado no MainMenuController.", this);
            return;
        }

        SetButtonsInteractable(false);
        options.Open();
    }

    public void QuitGame()
    {
        if (_loading) return;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    #endregion

    /// <summary>Enables Continue only when a save exists. It stays visible (greyed out) so the player knows it is there.</summary>
    public void RefreshButtons()
    {
        if (_continueButton != null) _continueButton.interactable = CanContinue;
    }

    private void SelectDefault()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null) return;

        Button target = _continueButton != null && _continueButton.interactable && _continueButton.gameObject.activeInHierarchy
            ? _continueButton
            : _newGameButton;
        if (target != null) eventSystem.SetSelectedGameObject(target.gameObject);
    }

    private void HandleOptionsClosed()
    {
        SetButtonsInteractable(true);
        RefreshButtons();

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null && _optionsButton != null) eventSystem.SetSelectedGameObject(_optionsButton.gameObject);
    }

    private OptionsMenuController GetOptionsMenu()
    {
        if (_optionsMenu != null) return _optionsMenu;
        if (_optionsMenuPrefab == null) return null;

        // Its own canvas and sort order: it needs no parent and draws above this menu wherever it lives.
        _optionsMenu = Instantiate(_optionsMenuPrefab);
        _optionsMenu.Closed += HandleOptionsClosed;
        return _optionsMenu;
    }

    /// <summary>
    /// Persistent managers survive scene loads, so a new game started after returning to the menu must not inherit
    /// the previous one's progress. On a cold start they do not exist yet and this does nothing.
    /// </summary>
    private static void ResetRunState()
    {
        if (UpgradeManager.instance != null) UpgradeManager.instance.ResetUpgrades();
        if (BossProgressionManager.instance != null) BossProgressionManager.instance.ResetProgression();
    }

    private void LoadScene(string sceneName)
    {
        // Set first: every later click is ignored, so the scene loads exactly once.
        _loading = true;
        SetButtonsInteractable(false);

        if (LevelManager.instance != null)
        {
            LevelManager.instance.LoadScene(sceneName);
            return;
        }

        SceneManager.LoadScene(sceneName);
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (_buttonsGroup == null) return;
        _buttonsGroup.interactable = interactable && !_loading;
    }

    /// <summary>The Inspector fields win when assigned; left empty, whatever sprite is on the Image stays.</summary>
    private void ApplyArt()
    {
        if (_backgroundImage != null && _backgroundSprite != null && _backgroundImage.sprite != _backgroundSprite)
        {
            _backgroundImage.sprite = _backgroundSprite;
        }

        if (_logoImage != null && _logoSprite != null && _logoImage.sprite != _logoSprite)
        {
            _logoImage.sprite = _logoSprite;
        }
    }
}
