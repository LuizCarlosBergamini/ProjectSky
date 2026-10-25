using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The pause panel: Continuar, Configuracoes (the reusable Options menu, pause mode) and Menu principal. Lives in the
/// Resources/PauseManager prefab next to PauseManager, which shows and hides it; this only handles its buttons and
/// UI/Cancel. Does not touch Time.timeScale or the player: that is PauseManager's job.
/// Levels without an EventSystem (Level1-3, telhado) get the prefab's fallback EventSystem while the panel is open.
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Tudo que aparece com o pause aberto. Este objeto fica sempre ativo.")]
    [SerializeField] private GameObject _root;
    [SerializeField] private Canvas _canvas;

    [Tooltip("Desativa os botoes do painel enquanto as configuracoes estao abertas por cima.")]
    [SerializeField] private CanvasGroup _buttonsGroup;

    [SerializeField] private Button _resumeButton;
    [SerializeField] private Button _optionsButton;
    [SerializeField] private Button _mainMenuButton;

    [Header("Configuracoes")]
    [Tooltip("Prefab OptionsMenu. Instanciado na primeira vez que o botao Configuracoes e usado.")]
    [SerializeField] private OptionsMenuController _optionsMenuPrefab;

    [Header("Ordem de desenho")]
    [Tooltip("Acima do seletor de chefes e da tela de upgrades (50), abaixo das configuracoes (60) e do fade (100).")]
    [SerializeField] private int _sortingOrder = 55;

    [Header("Input")]
    [Tooltip("UI/Cancel (Esc, botao B do controle): continua o jogo.")]
    [SerializeField] private InputActionReference _cancelAction;

    [Tooltip("EventSystem inativo do prefab, ligado so quando a cena nao tem um.")]
    [SerializeField] private GameObject _fallbackEventSystem;

    private OptionsMenuController _options;
    private int _shownFrame = -1;

    public bool IsOpen { get; private set; }

    public bool IsOptionsOpen => _options != null && _options.IsOpen;

    /// <summary>
    /// True while the options are open and on the frame they closed. PauseManager ignores the pause key then, so one
    /// Esc closes only the options.
    /// </summary>
    public bool IsCapturingInput => _options != null && _options.IsCapturingInput;

    private void Awake()
    {
        if (_canvas == null) _canvas = GetComponent<Canvas>();
        if (_canvas != null)
        {
            if (!_canvas.isRootCanvas) _canvas.overrideSorting = true;
            _canvas.sortingOrder = _sortingOrder;
        }

        if (_root != null) _root.SetActive(false);
        if (_fallbackEventSystem != null) _fallbackEventSystem.SetActive(false);

        if (_resumeButton != null) _resumeButton.onClick.AddListener(Resume);
        if (_optionsButton != null) _optionsButton.onClick.AddListener(OpenOptions);
        if (_mainMenuButton != null) _mainMenuButton.onClick.AddListener(ReturnToMainMenu);
    }

    private void OnDestroy()
    {
        if (_resumeButton != null) _resumeButton.onClick.RemoveListener(Resume);
        if (_optionsButton != null) _optionsButton.onClick.RemoveListener(OpenOptions);
        if (_mainMenuButton != null) _mainMenuButton.onClick.RemoveListener(ReturnToMainMenu);
    }

    private void Update()
    {
        if (!IsOpen || IsCapturingInput) return;

        // The frame the panel opens on may still carry the press that opened it.
        if (Time.frameCount != _shownFrame && _cancelAction != null && _cancelAction.action != null
            && _cancelAction.action.WasPressedThisFrame())
        {
            Resume();
            return;
        }

        // Clicking empty space clears the EventSystem selection; give keyboard/gamepad a button back.
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null && eventSystem.currentSelectedGameObject == null) SelectDefault(eventSystem);
    }

    #region Show / Hide (called by PauseManager)

    public void Show()
    {
        if (IsOpen) return;

        IsOpen = true;
        _shownFrame = Time.frameCount;

        // Inactive objects are not found, so the fallback (inactive) never counts as the scene's own.
        if (_fallbackEventSystem != null && FindAnyObjectByType<EventSystem>() == null) _fallbackEventSystem.SetActive(true);

        if (_root != null) _root.SetActive(true);
        SetButtonsInteractable(true);

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null) SelectDefault(eventSystem);
    }

    public void Hide()
    {
        if (!IsOpen) return;
        IsOpen = false;

        // A scene change or a focus loss can end the pause with the options still open.
        if (IsOptionsOpen) _options.Close();

        if (_root != null) _root.SetActive(false);

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null) eventSystem.SetSelectedGameObject(null);

        if (_fallbackEventSystem != null) _fallbackEventSystem.SetActive(false);
    }

    #endregion

    #region Buttons

    public void Resume()
    {
        if (PauseManager.instance != null) PauseManager.instance.Resume();
    }

    public void OpenOptions()
    {
        if (!IsOpen || IsOptionsOpen) return;

        if (_options == null)
        {
            if (_optionsMenuPrefab == null)
            {
                Debug.LogWarning("Prefab OptionsMenu nao atribuido no PauseMenuController.", this);
                return;
            }

            // Sibling of the pause canvas, so it keeps its own root canvas and survives scene loads with it.
            _options = Instantiate(_optionsMenuPrefab, transform.parent);
            _options.name = _optionsMenuPrefab.name;
        }

        SetButtonsInteractable(false);

        // TODO(integracao): trocar por _options.Open(OptionsMenuMode.Pause, HandleOptionsClosed) quando a branch do
        // menu de configuracoes (que cria OptionsMenuMode) estiver na main. Assim o menu mostra Salvar em vez do Guia.
        // Na mesma integracao, usar OptionsMenuController.LoadPrefab() quando _optionsMenuPrefab estiver vazio.
        _options.Open(HandleOptionsClosed);
    }

    public void ReturnToMainMenu()
    {
        if (PauseManager.instance != null) PauseManager.instance.ReturnToMainMenu();
    }

    private void HandleOptionsClosed()
    {
        SetButtonsInteractable(true);

        // The options hand focus back to the Configuracoes button; make sure something is selected regardless.
        EventSystem eventSystem = EventSystem.current;
        if (IsOpen && eventSystem != null && eventSystem.currentSelectedGameObject == null)
        {
            eventSystem.SetSelectedGameObject(_optionsButton != null ? _optionsButton.gameObject : null);
        }
    }

    #endregion

    private void SetButtonsInteractable(bool interactable)
    {
        if (_buttonsGroup == null) return;
        _buttonsGroup.interactable = interactable;
        _buttonsGroup.blocksRaycasts = interactable;
    }

    private void SelectDefault(EventSystem eventSystem)
    {
        if (_resumeButton != null && _resumeButton.isActiveAndEnabled) eventSystem.SetSelectedGameObject(_resumeButton.gameObject);
    }
}
