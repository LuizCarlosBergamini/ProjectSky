using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// A yes/no question over the current screen ("Ja existe um save, comecar de novo?"). Reusable: any menu calls
/// Open with its own text and callbacks; the dialog knows nothing about who asked. Focus starts on Cancel, so a
/// stray Submit press never confirms something destructive, and UI/Cancel (Esc, gamepad B) answers no.
/// On close the focus goes back to whatever had it before.
/// </summary>
public class ConfirmDialog : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Tudo que aparece com a pergunta aberta. Este objeto fica sempre ativo.")]
    [SerializeField] private GameObject _root;
    [SerializeField] private TextMeshProUGUI _messageLabel;
    [SerializeField] private Button _confirmButton;
    [SerializeField] private TextMeshProUGUI _confirmLabel;
    [SerializeField] private Button _cancelButton;
    [SerializeField] private TextMeshProUGUI _cancelLabel;

    [Header("Input")]
    [Tooltip("Responder 'nao' pelo teclado/controle: UI/Cancel (Esc, botao B do controle).")]
    [SerializeField] private InputActionReference _cancelAction;

    private Action _onConfirm;
    private Action _onCancel;
    private GameObject _previousSelection;
    private int _openedFrame = -1;

    public bool IsOpen { get; private set; }

    private void Awake()
    {
        if (_root != null) _root.SetActive(false);
        if (_confirmButton != null) _confirmButton.onClick.AddListener(Confirm);
        if (_cancelButton != null) _cancelButton.onClick.AddListener(Cancel);
    }

    private void OnDisable()
    {
        // Never leave the caller waiting for an answer that will not come.
        if (IsOpen) Cancel();
    }

    private void OnDestroy()
    {
        if (_confirmButton != null) _confirmButton.onClick.RemoveListener(Confirm);
        if (_cancelButton != null) _cancelButton.onClick.RemoveListener(Cancel);
    }

    private void Update()
    {
        if (!IsOpen) return;

        // The frame the dialog opens on may still carry the press that opened it.
        if (Time.frameCount != _openedFrame && _cancelAction != null && _cancelAction.action != null &&
            _cancelAction.action.WasPressedThisFrame())
        {
            Cancel();
            return;
        }

        // Clicking empty space clears the EventSystem selection; give keyboard/gamepad a button back.
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null && eventSystem.currentSelectedGameObject == null) SelectCancel(eventSystem);
    }

    /// <summary>Shows the question. Empty button texts keep the labels already in the dialog.</summary>
    public void Open(string message, Action onConfirm, Action onCancel = null, string confirmText = null, string cancelText = null)
    {
        if (IsOpen) return;

        IsOpen = true;
        _openedFrame = Time.frameCount;
        _onConfirm = onConfirm;
        _onCancel = onCancel;

        if (_messageLabel != null) _messageLabel.text = message;
        if (_confirmLabel != null && !string.IsNullOrEmpty(confirmText)) _confirmLabel.text = confirmText;
        if (_cancelLabel != null && !string.IsNullOrEmpty(cancelText)) _cancelLabel.text = cancelText;
        if (_root != null) _root.SetActive(true);

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null)
        {
            _previousSelection = eventSystem.currentSelectedGameObject;
            SelectCancel(eventSystem);
        }
    }

    public void Confirm()
    {
        Action callback = _onConfirm;
        if (Close()) callback?.Invoke();
    }

    public void Cancel()
    {
        Action callback = _onCancel;
        if (Close()) callback?.Invoke();
    }

    private bool Close()
    {
        if (!IsOpen) return false;
        IsOpen = false;
        _onConfirm = null;
        _onCancel = null;

        if (_root != null) _root.SetActive(false);

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null)
        {
            bool restore = _previousSelection != null && _previousSelection.activeInHierarchy;
            eventSystem.SetSelectedGameObject(restore ? _previousSelection : null);
        }

        _previousSelection = null;
        return true;
    }

    private void SelectCancel(EventSystem eventSystem)
    {
        if (_cancelButton != null) eventSystem.SetSelectedGameObject(_cancelButton.gameObject);
    }
}
