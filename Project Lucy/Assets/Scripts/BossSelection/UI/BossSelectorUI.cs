using System;
using System.Collections.Generic;
using HierarchicalStateMachine;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The boss selection screen. Scene object in the Hub (not persistent), opened by BossSelectorTrigger.
/// Builds one panel per boss in BossProgressionManager, pauses like UpgradeCanvas while open and hands
/// gameplay input back when closed. Holds no progression rules: states, rewards and the scene load all
/// come from the manager.
/// </summary>
public class BossSelectorUI : MonoBehaviour
{
    public static BossSelectorUI instance;

    [Header("Referencias")]
    [Tooltip("Tudo que aparece com o menu aberto. Este objeto fica sempre ativo para registrar a instancia.")]
    [SerializeField] private GameObject _root;
    [SerializeField] private RectTransform _panelContainer;
    [SerializeField] private BossPanelUI _panelPrefab;
    [SerializeField] private TextMeshProUGUI _subtitleText;
    [SerializeField] private Button _confirmButton;
    [SerializeField] private Button _backButton;
    [SerializeField] private Button _closeButton;
    [SerializeField] private TooltipUI _tooltip;

    [Header("Input")]
    [Tooltip("Acao que fecha o menu (a mesma do UpgradeCanvas: InGame/Pause).")]
    [SerializeField] private InputActionReference _closeAction;

    [Tooltip("Acao extra de voltar (ex: UI/Cancel, botao B do controle). Opcional.")]
    [SerializeField] private InputActionReference _cancelAction;

    [Tooltip("Jogador cujo input e bloqueado. Vazio = objeto com a tag Player ao abrir.")]
    [SerializeField] private PlayerStateDriver _player;

    [Tooltip("Congela o jogo com Time.timeScale = 0 enquanto aberto, como o UpgradeCanvas.")]
    [SerializeField] private bool _pauseTime = true;

    [Header("Feedback")]
    [SerializeField] private AudioSource _audioSource;
    [Tooltip("Som ao clicar num chefe bloqueado. Vazio = so o tremor do painel.")]
    [SerializeField] private AudioClip _deniedClip;

    [Header("Textos")]
    [SerializeField] private string _noSelectionText = "ESCOLHA UM CHEFE";
    [SerializeField] private string _defeatedSuffix = "  -  DERROTADO";

    [Header("Eventos")]
    [SerializeField] private UnityEvent _onOpen;
    [SerializeField] private UnityEvent _onClose;

    private readonly List<BossPanelUI> _panels = new();

    private bool _built;
    private bool _loading;
    private int _openedFrame = -1;
    private float _previousTimeScale = 1f;
    private Action _onClosedCallback;
    private BossPanelUI _selected;

    public bool IsOpen { get; private set; }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Mais de um BossSelectorUI na cena; o segundo foi ignorado.", this);
            return;
        }

        instance = this;
        if (_root != null) _root.SetActive(false);
        if (_tooltip != null) _tooltip.Hide();
        if (_closeButton != null) _closeButton.onClick.AddListener(Close);
        if (_backButton != null) _backButton.onClick.AddListener(Close);
        if (_confirmButton != null) _confirmButton.onClick.AddListener(Confirm);
    }

    private void OnDisable()
    {
        // Never leave the game frozen or the player without input because the menu went away while open.
        if (IsOpen) Close();
    }

    private void OnDestroy()
    {
        if (_closeButton != null) _closeButton.onClick.RemoveListener(Close);
        if (_backButton != null) _backButton.onClick.RemoveListener(Close);
        if (_confirmButton != null) _confirmButton.onClick.RemoveListener(Confirm);
        Unsubscribe();
        if (instance == this) instance = null;
    }

    private void Update()
    {
        if (!IsOpen || _loading) return;

        // The frame the menu opens on may still carry the press that opened it.
        if (Time.frameCount != _openedFrame && (WasPressed(_closeAction) || WasPressed(_cancelAction)))
        {
            Close();
            return;
        }

        // Clicking empty space clears the EventSystem selection; give keyboard/gamepad a panel back.
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null && eventSystem.currentSelectedGameObject == null && _panels.Count > 0)
        {
            eventSystem.SetSelectedGameObject((_selected != null ? _selected : _panels[0]).gameObject);
        }
    }

    #region Open / Close

    /// <param name="onClosed">Called once, when this opening is closed or a boss scene starts loading.</param>
    public void Open(Action onClosed = null)
    {
        if (IsOpen || _loading) return;

        BossProgressionManager manager = BossProgressionManager.instance;
        if (manager == null)
        {
            Debug.LogWarning("BossProgressionManager nao encontrado: coloque o prefab BossProgressionManager no Hub.", this);
            onClosed?.Invoke();
            return;
        }

        EnsureBuilt(manager);

        IsOpen = true;
        _openedFrame = Time.frameCount;
        _onClosedCallback = onClosed;

        if (_root != null) _root.SetActive(true);

        if (_pauseTime)
        {
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        PlayerStateDriver player = ResolvePlayer();
        if (player != null) player.SetInputEnabled(false);

        BossProgressionManager.OnProgressionChanged += RefreshAll;
        TaskManager.OnTaskStateChanged += RefreshAll;

        RefreshAll();
        SelectInitialPanel();

        _onOpen?.Invoke();
    }

    public void Close()
    {
        if (!IsOpen) return;
        Shutdown(restoreInput: true);
        _onClose?.Invoke();
        InvokeClosedCallback();
    }

    /// <summary>
    /// Leaves the screen: hides it, stops listening and unfreezes time. Player input is only handed back when
    /// closing; while a boss scene loads the player stays still until the scene swaps it out.
    /// </summary>
    private void Shutdown(bool restoreInput)
    {
        IsOpen = false;
        Unsubscribe();

        if (_tooltip != null) _tooltip.Hide();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        if (_root != null) _root.SetActive(false);

        if (_pauseTime) Time.timeScale = _previousTimeScale;

        if (!restoreInput) return;
        PlayerStateDriver player = ResolvePlayer();
        if (player != null) player.SetInputEnabled(true);
    }

    private void Unsubscribe()
    {
        BossProgressionManager.OnProgressionChanged -= RefreshAll;
        TaskManager.OnTaskStateChanged -= RefreshAll;
    }

    private void InvokeClosedCallback()
    {
        Action callback = _onClosedCallback;
        _onClosedCallback = null;
        callback?.Invoke();
    }

    #endregion

    #region Build / Refresh

    private void EnsureBuilt(BossProgressionManager manager)
    {
        if (_built) return;
        _built = true;

        foreach (BossData_SO boss in manager.Bosses)
        {
            if (boss == null) continue;
            BossPanelUI panel = Instantiate(_panelPrefab, _panelContainer);
            panel.Bind(boss, _tooltip, HandlePanelClicked, HandlePanelFocused);
            _panels.Add(panel);
        }

        WireNavigation();
    }

    private void RefreshAll()
    {
        BossProgressionManager manager = BossProgressionManager.instance;
        if (manager == null) return;

        foreach (BossPanelUI panel in _panels)
        {
            BossData_SO boss = panel.Boss;
            panel.Refresh(manager.GetState(boss), manager.GetRewards(boss), manager.WillGrantReward(boss), manager.GetLockedHint(boss));
        }

        // Progress can only unlock more, but a debug reset can lock the current choice again.
        if (_selected != null && !_selected.IsSelectable) Choose(null);
        RefreshFooter();
    }

    private void RefreshFooter()
    {
        if (_confirmButton != null) _confirmButton.interactable = _selected != null && !_loading;

        if (_subtitleText == null) return;
        _subtitleText.text = _selected == null
            ? _noSelectionText
            : _selected.Boss.DisplayName.ToUpperInvariant() + (_selected.State == BossUnlockState.Defeated ? _defeatedSuffix : "");
    }

    /// <summary>Left/right between panels, down to the buttons, up back to the chosen panel.</summary>
    private void WireNavigation()
    {
        for (int i = 0; i < _panels.Count; i++)
        {
            Navigation navigation = new()
            {
                mode = Navigation.Mode.Explicit,
                selectOnLeft = i > 0 ? _panels[i - 1].Button : null,
                selectOnRight = i < _panels.Count - 1 ? _panels[i + 1].Button : null,
                selectOnDown = _confirmButton != null ? _confirmButton : _backButton
            };
            if (_panels[i].Button != null) _panels[i].Button.navigation = navigation;
        }

        RefreshButtonNavigation();
    }

    private void RefreshButtonNavigation()
    {
        Selectable up = _selected != null ? _selected.Button : _panels.Count > 0 ? _panels[0].Button : null;

        if (_backButton != null)
        {
            _backButton.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = up,
                selectOnRight = _confirmButton
            };
        }

        if (_confirmButton != null)
        {
            _confirmButton.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = up,
                selectOnLeft = _backButton
            };
        }

        // The X in the corner is for the mouse; Esc / B already close.
        if (_closeButton != null) _closeButton.navigation = new Navigation { mode = Navigation.Mode.None };
    }

    #endregion

    #region Selection

    /// <summary>Starts on the next boss to beat; with everything beaten, on the last one.</summary>
    private void SelectInitialPanel()
    {
        BossPanelUI target = null;
        foreach (BossPanelUI panel in _panels)
        {
            if (panel.State == BossUnlockState.Unlocked)
            {
                target = panel;
                break;
            }

            if (panel.State == BossUnlockState.Defeated) target = panel;
        }

        Choose(target);

        // Gives keyboard/gamepad a starting point; it calls back into HandlePanelFocused harmlessly.
        GameObject focus = target != null ? target.gameObject : _panels.Count > 0 ? _panels[0].gameObject : null;
        if (EventSystem.current != null && focus != null) EventSystem.current.SetSelectedGameObject(focus);
    }

    private void HandlePanelClicked(BossPanelUI panel)
    {
        if (!IsOpen || _loading) return;

        if (!panel.IsSelectable)
        {
            Deny(panel);
            return;
        }

        // First click chooses, a second click on the chosen boss enters (Enter/A on a focused panel too).
        if (panel == _selected) Confirm();
        else Choose(panel);
    }

    /// <summary>Keyboard/gamepad focus moved here. Locked panels can be looked at but never chosen.</summary>
    private void HandlePanelFocused(BossPanelUI panel)
    {
        if (!IsOpen || _loading || !panel.IsSelectable) return;
        Choose(panel);
    }

    private void Choose(BossPanelUI panel)
    {
        if (_selected != null) _selected.SetSelected(false);
        _selected = panel;
        if (_selected != null) _selected.SetSelected(true);

        RefreshButtonNavigation();
        RefreshFooter();
    }

    private void Confirm()
    {
        if (!IsOpen || _loading || _selected == null) return;

        BossProgressionManager manager = BossProgressionManager.instance;
        if (manager == null) return;

        BossEnterResult result = manager.TryEnterBoss(_selected.Boss);
        switch (result)
        {
            case BossEnterResult.Success:
                BeginLoading();
                break;
            case BossEnterResult.AlreadyLoading:
                break;
            default:
                Deny(_selected);
                break;
        }
    }

    private void BeginLoading()
    {
        // Set first: every click, key and refresh from here on is ignored, so the scene loads exactly once.
        _loading = true;
        if (_confirmButton != null) _confirmButton.interactable = false;

        Shutdown(restoreInput: false);
        InvokeClosedCallback();
    }

    private void Deny(BossPanelUI panel)
    {
        if (panel != null) panel.PlayDeniedEffect();
        if (_audioSource != null && _deniedClip != null) _audioSource.PlayOneShot(_deniedClip);
    }

    #endregion

    private static bool WasPressed(InputActionReference reference)
    {
        return reference != null && reference.action != null && reference.action.WasPressedThisFrame();
    }

    private PlayerStateDriver ResolvePlayer()
    {
        // Only runs on open/close, never per frame. The player is rebuilt with every scene load, so a lost
        // reference is looked up again.
        if (_player == null)
        {
            GameObject playerObject = GameObject.FindWithTag("Player");
            if (playerObject != null) _player = playerObject.GetComponent<PlayerStateDriver>();
        }

        return _player;
    }
}
