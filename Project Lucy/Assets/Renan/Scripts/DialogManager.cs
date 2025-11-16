
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using TMPro;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class DialogManager : MonoBehaviour
{
    public static DialogManager instance;

    [Header("Configurações")]
    [SerializeField] private int _typeSpeed = 20;
    [SerializeField] private int _clearTypeSpeed = 1;
    [SerializeField] private bool _instantClear = false;
    [SerializeField] private Vector2 _hiddenPosition;
    [SerializeField] private Vector2 _showPosition;

    [Header("Referências UI")]
    [SerializeField] private RectTransform _dialogContainer;
    [SerializeField] private TextMeshProUGUI _dialogContent;
    [SerializeField] private TextMeshProUGUI _dialogEntityName;
    [SerializeField] private Image _dialogEntitySprite;
    [SerializeField] private RectTransform _dialogOptionContainer;
    [SerializeField] private Button _dialogOptionPrefab;

    [Header("Keybinds")]
    [SerializeField] private InputActionReference _interactAction;

    [Header("Eventos")]
    [SerializeField] private UnityEvent _onStartDialog;
    [SerializeField] private UnityEvent _onFinishDialog;

    private Dialog_SO _dialogData;
    private DialogItem _actualDialog = null;
    private List<string> _typing = new ();
    private bool _skipRequested = false;

    private void Start()
    {
        if (EventManager.instance == null)
        {
            Debug.LogWarning("EventManager instance not found!");
        }
    }

    private void OnEnable()
    {
        _dialogContent.text = "";
        _dialogEntityName.text = "";

        instance = this;
    }

    private void OnDisable()
    {
        instance = null;
    }

    private void Update()
    {
        if (_interactAction.action.WasPressedThisFrame())
        {
            if (_typing.Count > 0)
            {
                _skipRequested = true;
                return;
            }
            
            if (_actualDialog == null || _actualDialog.dialogOptions.Count == 0) {
                NextDialog();
            }
        }
    }

    public void StartDialog(Dialog_SO dialogData)
    {
        if (_actualDialog != null)
        {
            Debug.Log("Dialogo em andamento");
            return;
        }
        Debug.Log("Dialogo iniciado");
        _dialogData = dialogData;

        _onStartDialog?.Invoke();
        StartCoroutine(_dialogContainer.Move(_showPosition, 1));
        StartCoroutine(ShowDialog(dialogData.dialogs[0]));
    }

    public void EndDialog()
    {
        Debug.Log("Dialogo finalizado");
        _actualDialog = null;

        _onFinishDialog?.Invoke();
        StartCoroutine(_dialogContainer.Move(_hiddenPosition, 1));
    }

    private IEnumerator ShowDialog(DialogItem dialog)
    {
        _actualDialog = dialog;
        if (_actualDialog == null)
        {
            EndDialog();
            yield break;
        }
        if (!_instantClear)
        {
            _skipRequested = false;
            StartCoroutine(CleanText(_dialogContent));
            StartCoroutine(CleanText(_dialogEntityName));
            yield return new WaitUntil(() => _typing.Count == 0);
        }
        if (_dialogOptionContainer != null && _dialogOptionContainer.transform.childCount > 0)
        {
            for (int i = 0; i < _dialogOptionContainer.transform.childCount; i++)
            {
                var child = _dialogOptionContainer.transform.GetChild(i);
                Destroy(child.gameObject);
            }
        }

        if (_dialogEntitySprite != null && dialog.dialogEntity != null)
            _dialogEntitySprite.sprite = dialog.dialogEntity.entitySprite;

        CallEvent(dialog.triggerEvent);

        _skipRequested = false;
        StartCoroutine(TypeText(_dialogContent, dialog.dialogContent));
        StartCoroutine(TypeText(_dialogEntityName, dialog.dialogEntity.entityName));
        if (_dialogOptionContainer != null && _dialogOptionPrefab != null && dialog.dialogOptions.Count > 0)
        {
            foreach (DialogOption option in dialog.dialogOptions)
            {
                Button optionButton = Instantiate(_dialogOptionPrefab, _dialogOptionContainer.transform);
                optionButton.transform.localScale = Vector3.one;

                DialogItem toDialogItem = GetDialogItemByName(option.toDialog);
                optionButton.onClick.AddListener(() =>
                {
                    CallEvent(option.triggerEvent);
                    StartCoroutine(ShowDialog(toDialogItem));
                });

                TextMeshProUGUI optionTextField = optionButton.GetComponentInChildren<TextMeshProUGUI>();
                StartCoroutine(TypeText(optionTextField, option.optionContent));
            }
        }
        yield return new WaitUntil(() => _typing.Count == 0);

        if (dialog.dialogDuration > 0)
        {
            yield return new WaitForSeconds(dialog.dialogDuration);
            NextDialog();
        }
    }

    private IEnumerator TypeText(TextMeshProUGUI textField, string content)
    {
        if (textField == null || string.IsNullOrWhiteSpace(content))
        {
            yield break;
        }

        textField.maxVisibleCharacters = 0;
        textField.text = content;
        _typing.Add(textField.name);
        for (int i = 0; i < content.Length; i++)
        {
            if (_skipRequested)
            {
                textField.maxVisibleCharacters = int.MaxValue;
                _typing.Remove(textField.name);
                break;
            }
            textField.maxVisibleCharacters++;
            yield return new WaitForSeconds(_typeSpeed / 1000f);
        }
        _typing.Remove(textField.name);
    }

    private IEnumerator CleanText(TextMeshProUGUI textField)
    {
        if (textField == null || string.IsNullOrWhiteSpace(textField.text))
        {
            yield break;
        }

        textField.maxVisibleCharacters = textField.text.Length;
        _typing.Add(textField.name);
        for (int i = 0; i < textField.text.Length; i++)
        {
            if (_skipRequested)
            {
                textField.maxVisibleCharacters = 0;
                _typing.Remove(textField.name);
                break;
            }
            textField.maxVisibleCharacters--;
            yield return new WaitForSeconds(_clearTypeSpeed / 1000f);
        }
        _typing.Remove(textField.name);
    }

    private DialogItem GetDialogItemByName(string dialogName)
    {
        if (string.IsNullOrWhiteSpace(dialogName)) return null;
        return _dialogData.dialogs.Find(d => d.dialogId == dialogName);
    }

    private void CallEvent(string eventName)
    {
        if (EventManager.instance == null || string.IsNullOrWhiteSpace(eventName)) return;
        EventManager.instance.Call(eventName);
    }

    private void NextDialog()
    {
        if (_actualDialog == null) return;
        DialogItem nextDialog = GetDialogItemByName(_actualDialog.nextDialog);
        StartCoroutine(ShowDialog(nextDialog));
    }
}
