using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The Options menu. Self-contained prefab (OptionsMenu) with its own canvas: drop it in any scene, or instantiate
/// it, and call Open. It knows nothing about who opened it: the caller gets the onClosed callback (or the Closed
/// event) and decides what comes next, which is how the main menu uses it now and the pause menu will later.
/// All values go through SettingsManager; this only shows them. Does not pause the game or block player input:
/// that stays with the caller (the pause menu already does both).
/// </summary>
public class OptionsMenuController : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Tudo que aparece com o menu aberto. Este objeto fica sempre ativo.")]
    [SerializeField] private GameObject _root;
    [SerializeField] private Canvas _canvas;
    [SerializeField] private Button _backButton;

    [Tooltip("Recebe o foco do teclado/controle ao abrir (o primeiro slider).")]
    [SerializeField] private Selectable _firstSelected;

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

    // PLACEHOLDER sections (resolution/fullscreen, key bindings, language) exist only in the prefab hierarchy as
    // "Placeholder_*" objects. They have no fields here on purpose: nothing about them is implemented yet.

    private int _openedFrame = -1;
    private int _closedFrame = -1;
    private GameObject _previousSelection;
    private Action _onClosedCallback;

    /// <summary>Fired every time the menu closes, after the onClosed callback passed to Open.</summary>
    public event Action Closed;

    public bool IsOpen { get; private set; }

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
    }

    private void OnDisable()
    {
        // Never leave the caller waiting for a close that will not come.
        if (IsOpen) Close();
    }

    private void OnDestroy()
    {
        if (_backButton != null) _backButton.onClick.RemoveListener(Close);
    }

    private void Update()
    {
        if (!IsOpen) return;

        // The frame the menu opens on may still carry the press that opened it.
        if (Time.frameCount != _openedFrame && (WasPressed(_cancelAction) || WasPressed(_closeAction)))
        {
            Close();
            return;
        }

        // Clicking empty space clears the EventSystem selection; give keyboard/gamepad a control back.
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null && eventSystem.currentSelectedGameObject == null) SelectFirst(eventSystem);
    }

    #region Open / Close

    /// <param name="onClosed">Called once, when this opening is closed.</param>
    public void Open(Action onClosed = null)
    {
        if (IsOpen) return;

        IsOpen = true;
        _openedFrame = Time.frameCount;
        _onClosedCallback = onClosed;

        if (SettingsManager.instance == null)
        {
            Debug.LogWarning("SettingsManager nao encontrado: os sliders ficam desativados.", this);
        }

        // Re-applied in case the prefab was placed under another canvas after Awake.
        ApplySortingOrder();
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

    private static bool WasPressed(InputActionReference reference)
    {
        return reference != null && reference.action != null && reference.action.WasPressedThisFrame();
    }
}
