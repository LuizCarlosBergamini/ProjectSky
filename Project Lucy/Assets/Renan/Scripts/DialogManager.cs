
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[RequireComponent(typeof(AudioSource))]
public class DialogManager : MonoBehaviour
{
    public static DialogManager instance;

    private readonly float _DIALOG_ANIMATION_DURATION_IN_SECONDS = 0.25f;
    private readonly float _ENTITY_ANIMATION_DURATION_IN_SECONDS = 0.25f;
    private readonly float[] _DIALOG_AUDIO_PITCH_RANGE = { 0.8f, 1.2f };
    private readonly float[] _DIALOG_AUDIO_VOLUME_RANGE = { 0.2f, 0.4f };

    [Header("Configurações")]
    [SerializeField] private int _typeSpeed = 20;
    [SerializeField] private int _clearTypeSpeed = 1;
    [SerializeField] private bool _instantClear = false;

    [Header("Áudio")]
    [SerializeField] private AudioClip _audioEmit;
    [SerializeField] private int _audioPlayInterval = 100;

    [Header("Referências UI")]
    [SerializeField] private RectTransform _dialogContainer;
    [SerializeField] private TextMeshProUGUI _dialogContent;
    [SerializeField] private TextMeshProUGUI _dialogEntityName;
    [SerializeField] private Image _dialogEntitySprite;
    [SerializeField] private RectTransform _dialogOptionContainer;
    [SerializeField] private Button _dialogOptionPrefab;

    [Header("Animação")]
    [SerializeField] private Vector2 _hiddenContainerPosition;
    [SerializeField] private Vector2 _showContainerPosition;
    [SerializeField] private bool _useEntityAnimation = false;
    [SerializeField] private Vector2 _hiddenEntityPosition;
    [SerializeField] private Vector2 _showEntityPosition;

    [Header("Keybinds")]
    [SerializeField] private InputActionReference _interactAction;

    [Header("Eventos")]
    [SerializeField] private UnityEvent _onStartDialog;
    [SerializeField] private UnityEvent _onFinishDialog;

    private Dialog_SO _dialogData;
    private DialogItem _actualDialog = null;
    private readonly List<string> _typing = new ();
    private bool _skipRequested = false;
    private AudioSource _audioSource;

    #region Unity Functions

    private void Start()
    {
        _dialogData = null;
        _actualDialog = null;
        _audioSource = GetComponent<AudioSource>();
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
        if (_actualDialog != null && _interactAction != null && _interactAction.action.WasPressedThisFrame())
        {
            if (_typing.Count > 0)
            {
                _skipRequested = true;
                return;
            }
            
            if (_actualDialog.dialogOptions.Count == 0) {
                NextDialog();
            }
        }
    }

    #endregion

    #region Public Functions

    public void Init(Dialog_SO dialogData)
    {
        if (_actualDialog != null)
        {
            Debug.Log("Dialogo em andamento");
            return;
        }
        if (dialogData != null)
        {
            StartCoroutine(StartDialog(dialogData));
        }
    }

    public void End()
    {
        Debug.Log("Dialogo finalizado");
        _actualDialog = null;
        _dialogData = null;

        _onFinishDialog?.Invoke();
        StartCoroutine(_dialogContainer.Move(_hiddenContainerPosition, _DIALOG_ANIMATION_DURATION_IN_SECONDS));
    }

    #endregion

    #region Private Functions

    private IEnumerator StartDialog(Dialog_SO dialogData)
    {
        Debug.Log("Dialogo iniciado");
        _dialogData = dialogData;

        _dialogContent.text = "";
        _dialogEntityName.text = "";
        _dialogEntitySprite.canvasRenderer.SetAlpha(0f);
        _dialogEntitySprite.sprite = null;

        _onStartDialog?.Invoke();
        StartCoroutine(_dialogContainer.Move(_showContainerPosition, _DIALOG_ANIMATION_DURATION_IN_SECONDS));
        yield return new WaitForSeconds(_DIALOG_ANIMATION_DURATION_IN_SECONDS / 2);
        StartCoroutine(ShowDialog(dialogData.dialogs[0]));
    }

    private IEnumerator ShowDialog(DialogItem dialog)
    {
        _actualDialog = dialog;
        if (_actualDialog == null)
        {
            End();
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

        if (_useEntityAnimation)
        {
            StartCoroutine(ChangeEntityAsync(dialog.dialogEntity));
        } else
        {
            ChangeEntity(dialog.dialogEntity);
        }

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
        StartCoroutine(PlayDialogAudio());
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

    private IEnumerator PlayDialogAudio()
    {
        if (_audioSource == null || _audioEmit == null) yield break;
        do
        {
            _audioSource.pitch = UnityEngine.Random.Range(_DIALOG_AUDIO_PITCH_RANGE[0], _DIALOG_AUDIO_PITCH_RANGE[1]);
            _audioSource.volume = UnityEngine.Random.Range(_DIALOG_AUDIO_VOLUME_RANGE[0], _DIALOG_AUDIO_VOLUME_RANGE[1]);
            _audioSource.PlayOneShot(_audioEmit);
            yield return new WaitForSeconds(_audioPlayInterval / 1000f);
        } while (_typing.Count > 0);
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

    private IEnumerator ChangeEntityAsync(Entity_SO entity)
    {
        if (_dialogEntitySprite != null && _dialogEntitySprite.TryGetComponent(out RectTransform transform)) {
            if (entity != null && entity.entitySprite != null)
            {
                yield return StartCoroutine(transform.Move(_hiddenEntityPosition, _ENTITY_ANIMATION_DURATION_IN_SECONDS));
                _dialogEntitySprite.canvasRenderer.SetAlpha(1f);
                _dialogEntitySprite.sprite = entity.entitySprite;
            } else
            {
                _dialogEntitySprite.canvasRenderer.SetAlpha(0f);
                _dialogEntitySprite.sprite = null;
            }
            StartCoroutine(transform.Move(_showEntityPosition, _ENTITY_ANIMATION_DURATION_IN_SECONDS));
            yield return new WaitForSeconds(_ENTITY_ANIMATION_DURATION_IN_SECONDS / 2);
        }
    }

    private void ChangeEntity(Entity_SO entity)
    {
        if (_dialogEntitySprite != null && _dialogEntitySprite.TryGetComponent(out RectTransform transform))
        {
            if (entity != null && entity.entitySprite != null)
            {
                _dialogEntitySprite.canvasRenderer.SetAlpha(1f);
                _dialogEntitySprite.sprite = entity.entitySprite;
            }
            else
            {
                _dialogEntitySprite.canvasRenderer.SetAlpha(0f);
                _dialogEntitySprite.sprite = null;
            }
        }
    }

    #endregion
}
