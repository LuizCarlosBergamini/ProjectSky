using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>What the menu offers besides the volume sliders: the guide on the main menu, saving in the pause menu.</summary>
public enum OptionsMenuMode
{
    MainMenu,
    Pause
}

/// <summary>
/// The Options menu. Self-contained prefab (OptionsMenu) with its own canvas: drop it in any scene, or instantiate
/// it, and call Open. It knows nothing about who opened it: the caller gets the onClosed callback (or the Closed
/// event) and decides what comes next, which is how the main menu uses it now and the pause menu will later.
/// All values go through SettingsManager; this only shows them. Does not pause the game or block player input:
/// that stays with the caller (the pause menu already does both).
/// The caller picks a mode on Open: MainMenu shows the "Como jogar" button (an in-menu guide panel), Pause shows
/// "Salvar jogo", which only raises <see cref="SaveRequested"/>; whoever owns saving subscribes to it.
/// </summary>
public class OptionsMenuController : MonoBehaviour
{
    /// <summary>Resources path of the OptionsMenu prefab (Assets/Resources/OptionsMenu.prefab).</summary>
    public const string ResourcePath = "OptionsMenu";

    /// <summary>
    /// The OptionsMenu prefab from Resources, for callers whose own prefab field is empty (a scene built before the
    /// prefab existed, or a lost reference). Null, with an error, when the prefab was never generated.
    /// </summary>
    public static OptionsMenuController LoadPrefab()
    {
        OptionsMenuController prefab = Resources.Load<OptionsMenuController>(ResourcePath);
        if (prefab == null)
        {
            Debug.LogError($"Prefab do menu de opcoes nao encontrado em Resources/{ResourcePath}: rode Tools > Lucy > Build Options Menu e commite o prefab.");
        }

        return prefab;
    }

    [Header("Referencias")]
    [Tooltip("Tudo que aparece com o menu aberto. Este objeto fica sempre ativo.")]
    [SerializeField] private GameObject _root;
    [SerializeField] private Canvas _canvas;
    [SerializeField] private Button _backButton;

    [Tooltip("Recebe o foco do teclado/controle ao abrir (o primeiro slider).")]
    [SerializeField] private Selectable _firstSelected;

    [Tooltip("Titulo do menu; troca para o titulo do guia enquanto o guia esta aberto.")]
    [SerializeField] private TMP_Text _title;

    [Tooltip("Sliders de volume, de cima para baixo. A navegacao vertical do teclado/controle e montada a partir deles.")]
    [SerializeField] private Selectable[] _volumeSliders;

    [Header("Modo menu principal: guia")]
    [SerializeField] private Button _guideButton;

    [Tooltip("Painel do guia. Fica no lugar do conteudo de opcoes enquanto aberto.")]
    [SerializeField] private GameObject _guidePanel;

    [Tooltip("Conteudo de opcoes (sliders e botoes) escondido enquanto o guia esta aberto.")]
    [SerializeField] private GameObject _optionsPanel;

    [SerializeField] private Button _guideBackButton;

    [Tooltip("Recebe o foco ao abrir o guia (a barra de rolagem: cima/baixo rola o texto).")]
    [SerializeField] private Selectable _guideFirstSelected;

    [SerializeField] private ScrollRect _guideScroll;
    [SerializeField] private string _optionsTitle = "OPÇÕES";
    [SerializeField] private string _guideTitle = "COMO JOGAR";

    [Header("Modo pause: salvar")]
    [SerializeField] private Button _saveButton;

    [Tooltip("Mostra o resultado do salvamento.")]
    [SerializeField] private TMP_Text _saveStatusLabel;

    [Tooltip("Por quanto tempo o resultado fica visivel (tempo real: funciona com o jogo pausado).")]
    [SerializeField] private float _saveStatusDuration = 2.5f;

    [SerializeField] private string _savedText = "JOGO SALVO";
    [SerializeField] private string _saveFailedText = "ERRO AO SALVAR";
    [SerializeField] private Color _savedColor = new(0.95f, 0.72f, 0.3f, 1f);
    [SerializeField] private Color _saveFailedColor = new(0.9f, 0.35f, 0.3f, 1f);

    [Header("Ordem de desenho")]
    [Tooltip("Acima do seletor de chefes e da tela de upgrades (50) e abaixo do fade do LevelManager (100).")]
    [SerializeField] private int _sortingOrder = 60;

    [Header("Input")]
    [Tooltip("Voltar pelo teclado/controle: UI/Cancel (Esc, botao B do controle).")]
    [SerializeField] private InputActionReference _cancelAction;

    [Tooltip("Acao extra que fecha o menu (InGame/Pause, a mesma da tela de upgrades). Opcional.")]
    [SerializeField] private InputActionReference _closeAction;

    [Header("Eventos")]
    [SerializeField] private UnityEvent _onOpen;
    [SerializeField] private UnityEvent _onClose;

    private int _openedFrame = -1;
    private int _closedFrame = -1;
    private GameObject _previousSelection;
    private Action _onClosedCallback;
    private float _saveStatusHideTime = -1f;

    /// <summary>Fired every time the menu closes, after the onClosed callback passed to Open.</summary>
    public event Action Closed;

    /// <summary>
    /// Raised by the "Salvar jogo" button (Pause mode). Return true when the game was saved. The menu knows
    /// nothing about the save system: hook it up once, e.g. <c>OptionsMenuController.SaveRequested += SaveSystem.Save;</c>.
    /// With several handlers every one runs and the save counts as done only if all of them return true.
    /// Cleared when play mode starts, so subscribe at runtime (a RuntimeInitializeOnLoadMethod with BeforeSceneLoad
    /// or later, or a component's OnEnable), never from a static constructor.
    /// </summary>
    public static event Func<bool> SaveRequested;

    public bool IsOpen { get; private set; }

    public OptionsMenuMode Mode { get; private set; } = OptionsMenuMode.MainMenu;

    public bool IsGuideOpen => _guidePanel != null && _guidePanel.activeSelf;

    /// <summary>
    /// True while open and on the frame it closed. Callers that also react to Esc/B (a pause menu) should ignore
    /// that input while this is true, so one press does not close the options and their own menu together.
    /// </summary>
    public bool IsCapturingInput => IsOpen || _closedFrame == Time.frameCount;

    private void Awake()
    {
        if (_canvas == null) _canvas = GetComponent<Canvas>();
        ApplySortingOrder();

        if (_root != null) _root.SetActive(false);
        if (_backButton != null) _backButton.onClick.AddListener(Close);
        if (_guideButton != null) _guideButton.onClick.AddListener(OpenGuide);
        if (_guideBackButton != null) _guideBackButton.onClick.AddListener(CloseGuide);
        if (_saveButton != null) _saveButton.onClick.AddListener(RequestSave);
    }

    private void OnDisable()
    {
        // Never leave the caller waiting for a close that will not come.
        if (IsOpen) Close();
    }

    private void OnDestroy()
    {
        if (_backButton != null) _backButton.onClick.RemoveListener(Close);
        if (_guideButton != null) _guideButton.onClick.RemoveListener(OpenGuide);
        if (_guideBackButton != null) _guideBackButton.onClick.RemoveListener(CloseGuide);
        if (_saveButton != null) _saveButton.onClick.RemoveListener(RequestSave);
    }

    private void Update()
    {
        if (!IsOpen) return;

        if (_saveStatusHideTime >= 0f && Time.unscaledTime >= _saveStatusHideTime) SetSaveStatus(null, default);

        // The frame the menu opens on may still carry the press that opened it.
        if (Time.frameCount != _openedFrame && (WasPressed(_cancelAction) || WasPressed(_closeAction)))
        {
            // From the guide, Back returns to the options first.
            if (IsGuideOpen) CloseGuide();
            else Close();
            return;
        }

        // Clicking empty space clears the EventSystem selection; give keyboard/gamepad a control back.
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null && eventSystem.currentSelectedGameObject == null)
        {
            if (IsGuideOpen) SelectGuideFirst(eventSystem);
            else SelectFirst(eventSystem);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Static event: without a domain reload, handlers from the last play session would still be attached.
        SaveRequested = null;
    }

    #region Open / Close

    /// <summary>Opens in MainMenu mode (volume and the guide).</summary>
    /// <param name="onClosed">Called once, when this opening is closed.</param>
    public void Open(Action onClosed = null)
    {
        Open(OptionsMenuMode.MainMenu, onClosed);
    }

    /// <param name="mode">MainMenu shows the guide button, Pause shows the save button.</param>
    /// <param name="onClosed">Called once, when this opening is closed.</param>
    public void Open(OptionsMenuMode mode, Action onClosed = null)
    {
        if (IsOpen) return;

        IsOpen = true;
        Mode = mode;
        _openedFrame = Time.frameCount;
        _onClosedCallback = onClosed;

        if (SettingsManager.instance == null)
        {
            Debug.LogWarning("SettingsManager nao encontrado: os sliders ficam desativados.", this);
        }

        // Re-applied in case the prefab was placed under another canvas after Awake.
        ApplySortingOrder();
        ApplyMode();
        ShowGuide(false);
        SetSaveStatus(null, default);
        if (_root != null) _root.SetActive(true);

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null)
        {
            _previousSelection = eventSystem.currentSelectedGameObject;
            SelectFirst(eventSystem);
        }

        _onOpen?.Invoke();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _closedFrame = Time.frameCount;

        if (SettingsManager.instance != null) SettingsManager.instance.Save();
        ShowGuide(false);
        SetSaveStatus(null, default);
        if (_root != null) _root.SetActive(false);

        // Hand focus back to whatever had it (the caller's Options button), if it is still usable.
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null)
        {
            bool restore = _previousSelection != null && _previousSelection.activeInHierarchy;
            eventSystem.SetSelectedGameObject(restore ? _previousSelection : null);
        }

        _previousSelection = null;

        _onClose?.Invoke();

        Action callback = _onClosedCallback;
        _onClosedCallback = null;
        callback?.Invoke();
        Closed?.Invoke();
    }

    #endregion

    #region Mode

    private void ApplyMode()
    {
        bool pause = Mode == OptionsMenuMode.Pause;
        if (_guideButton != null) _guideButton.gameObject.SetActive(!pause);
        if (_saveButton != null) _saveButton.gameObject.SetActive(pause);

        // Vertical navigation: sliders top to bottom, then the mode's button, then Back, wrapping around.
        // Left/right stays free so the sliders can use it to change their value.
        Selectable modeButton = pause ? _saveButton : _guideButton;
        int sliderCount = _volumeSliders != null ? _volumeSliders.Length : 0;
        Selectable[] chain = new Selectable[sliderCount + 2];
        for (int i = 0; i < sliderCount; i++) chain[i] = _volumeSliders[i];
        chain[sliderCount] = modeButton;
        chain[sliderCount + 1] = _backButton;
        LinkVertical(chain);
    }

    private static void LinkVertical(Selectable[] chain)
    {
        Selectable[] links = Array.FindAll(chain, s => s != null);
        for (int i = 0; i < links.Length; i++)
        {
            Navigation navigation = links[i].navigation;
            navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnUp = links[i > 0 ? i - 1 : links.Length - 1];
            navigation.selectOnDown = links[i < links.Length - 1 ? i + 1 : 0];
            navigation.selectOnLeft = null;
            navigation.selectOnRight = null;
            links[i].navigation = navigation;
        }
    }

    #endregion

    #region Guide

    /// <summary>Swaps the options for the guide panel. Only offered in MainMenu mode.</summary>
    public void OpenGuide()
    {
        if (!IsOpen || IsGuideOpen || _guidePanel == null) return;

        ShowGuide(true);
        if (_guideScroll != null) _guideScroll.verticalNormalizedPosition = 1f;

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null) SelectGuideFirst(eventSystem);
    }

    /// <summary>Back from the guide to the options, with the focus on the guide button.</summary>
    public void CloseGuide()
    {
        if (!IsGuideOpen) return;

        ShowGuide(false);

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null) return;
        if (_guideButton != null && _guideButton.isActiveAndEnabled) eventSystem.SetSelectedGameObject(_guideButton.gameObject);
        else SelectFirst(eventSystem);
    }

    private void ShowGuide(bool show)
    {
        if (_guidePanel != null) _guidePanel.SetActive(show);
        if (_optionsPanel != null) _optionsPanel.SetActive(!show);
        if (_title != null) _title.text = show ? _guideTitle : _optionsTitle;
    }

    #endregion

    #region Save

    /// <summary>The "Salvar jogo" button: asks <see cref="SaveRequested"/> to save and shows the result.</summary>
    public void RequestSave()
    {
        if (!IsOpen) return;

        bool saved = InvokeSaveRequested();
        SetSaveStatus(saved ? _savedText : _saveFailedText, saved ? _savedColor : _saveFailedColor);
    }

    private bool InvokeSaveRequested()
    {
        Func<bool> handlers = SaveRequested;
        if (handlers == null)
        {
            Debug.LogWarning("Nada esta inscrito em OptionsMenuController.SaveRequested: o jogo nao foi salvo.", this);
            return false;
        }

        bool saved = true;
        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                saved &= ((Func<bool>)handler).Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                saved = false;
            }
        }

        return saved;
    }

    private void SetSaveStatus(string text, Color color)
    {
        bool show = !string.IsNullOrEmpty(text);
        _saveStatusHideTime = show ? Time.unscaledTime + _saveStatusDuration : -1f;
        if (_saveStatusLabel == null) return;

        _saveStatusLabel.text = show ? text : string.Empty;
        _saveStatusLabel.color = color;
    }

    #endregion

    private void ApplySortingOrder()
    {
        if (_canvas == null) return;

        // A nested canvas ignores its own order unless it overrides the parent's.
        if (!_canvas.isRootCanvas) _canvas.overrideSorting = true;
        _canvas.sortingOrder = _sortingOrder;
    }

    private void SelectFirst(EventSystem eventSystem)
    {
        if (_firstSelected != null && _firstSelected.isActiveAndEnabled && _firstSelected.interactable)
        {
            eventSystem.SetSelectedGameObject(_firstSelected.gameObject);
        }
        else if (_backButton != null)
        {
            eventSystem.SetSelectedGameObject(_backButton.gameObject);
        }
    }

    private void SelectGuideFirst(EventSystem eventSystem)
    {
        if (_guideFirstSelected != null && _guideFirstSelected.isActiveAndEnabled && _guideFirstSelected.interactable)
        {
            eventSystem.SetSelectedGameObject(_guideFirstSelected.gameObject);
        }
        else if (_guideBackButton != null)
        {
            eventSystem.SetSelectedGameObject(_guideBackButton.gameObject);
        }
    }

    private static bool WasPressed(InputActionReference reference)
    {
        return reference != null && reference.action != null && reference.action.WasPressedThisFrame();
    }
}
