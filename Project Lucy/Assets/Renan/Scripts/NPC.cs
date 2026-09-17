using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Picks which dialog to show based on the state of a task.
/// Evaluated top-down: the first rule that matches wins.
/// </summary>
[Serializable]
public class NPCDialogRule
{
    [Tooltip("Task avaliada por esta regra. Vazio = sempre combina (use como fallback no fim da lista).")]
    public string taskId;

    public TaskManager.TaskStatus status = TaskManager.TaskStatus.NotStarted;

    [Tooltip("So combina se o jogador ja tiver todos os requiredItems da task.")]
    public bool requiresItems;

    public Dialog_SO dialog;
}

[RequireComponent(typeof(Collider2D))]
public class NPC : MonoBehaviour
{
    [SerializeField] private Entity_SO _entity;
    [SerializeField] private Dialog_SO _dialog;

    [Tooltip("Dialogos escolhidos pelo estado das tasks. Tem prioridade sobre o campo acima.")]
    [SerializeField] private List<NPCDialogRule> _dialogRules = new();

    [SerializeField] private InputActionReference _dialogAction;
    [SerializeField] private GameObject _displayActionContainer;
    [SerializeField] private TextMeshProUGUI _displayActionText;

    private SpriteRenderer _spriteRenderer;
    private Animator _animator;

    private AnimatorOverrideController _overrideController;

    private GameObject _player;

    private bool _inCollider;

    /// <summary>Raised when the player starts this NPC's dialog, so companions know which dialog is theirs.</summary>
    public event Action<Dialog_SO> DialogStarted;

    public void SetDialog(Dialog_SO dialog)
    {
        _dialog = dialog;
    }

    public void OnTriggerEnter2D(Collider2D collision)
    {
        if (_dialog == null) return;
        _inCollider = true;
        if (_displayActionContainer != null) _displayActionContainer.SetActive(true);
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        _inCollider = false;
        if (_displayActionContainer != null) _displayActionContainer.SetActive(false);
    }

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _animator = GetComponent<Animator>();

        if (_animator != null && _animator.runtimeAnimatorController != null)
        {
            _overrideController = new AnimatorOverrideController(_animator.runtimeAnimatorController);
        }

        _player = GameObject.FindWithTag("Player");
    }

    private void OnEnable()
    {
        // Rebuilt from scratch every time the scene loads, so returning to the hub
        // always lands on the right line of dialog.
        TaskManager.OnTaskStateChanged += ResolveDialog;
        ResolveDialog();
    }

    private void OnDisable()
    {
        TaskManager.OnTaskStateChanged -= ResolveDialog;
    }

    private void Start()
    {
        ResolveDialog();

        if (_dialogAction && _displayActionText != null)
        {
            _displayActionText.text = _dialogAction.action.bindings[0].ToDisplayString();

            if (_displayActionText.TryGetComponent(out RectTransform textTransform))
            {
                var size = textTransform.sizeDelta;

                size.x = _displayActionText.text.Length * 2f + 2f;
                textTransform.sizeDelta = size;
            }
        }

        if (_entity != null && _entity.enitityAnimation != null && _overrideController != null)
        {
            _overrideController["BaseClip"] = _entity.enitityAnimation;
            _animator.runtimeAnimatorController = _overrideController;
            _animator.Play("BaseClip", 0, 0f);
        }
    }

    private void Update()
    {
        if (_inCollider && _dialog != null && _dialogAction != null && _dialogAction.action.WasPressedThisFrame()
            && DialogManager.instance != null)
        {
            DialogManager.instance.Init(_dialog);
            DialogStarted?.Invoke(_dialog);
        }

        if (_player == null) _player = GameObject.FindWithTag("Player");

        if (_player != null && _spriteRenderer != null)
        {
            _spriteRenderer.flipX = _player.transform.position.x < transform.position.x;
        }
    }

    /// <summary>Picks the dialog matching the current task state.</summary>
    private void ResolveDialog()
    {
        if (_dialogRules == null || _dialogRules.Count == 0) return;
        if (TaskManager.instance == null) return;

        foreach (NPCDialogRule rule in _dialogRules)
        {
            if (rule == null || rule.dialog == null) continue;

            if (!string.IsNullOrWhiteSpace(rule.taskId))
            {
                if (TaskManager.instance.GetStatus(rule.taskId) != rule.status) continue;
                if (rule.requiresItems && !TaskManager.instance.HasAllRequiredItems(rule.taskId)) continue;
            }

            _dialog = rule.dialog;

            // Keep the interaction prompt honest if the dialog appeared while the player is standing here.
            if (_inCollider && _displayActionContainer != null) _displayActionContainer.SetActive(true);
            return;
        }
    }
}
