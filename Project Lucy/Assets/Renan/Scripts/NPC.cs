using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

[RequireComponent(typeof(Collider2D))]
public class NPC : MonoBehaviour
{
    [SerializeField] private Entity_SO _entity;
    [SerializeField] private Dialog_SO _dialog;
    [SerializeField] private InputActionReference _dialogAction;
    [SerializeField] private GameObject _displayActionContainer;
    [SerializeField] private TextMeshProUGUI _displayActionText;

    private SpriteRenderer _spriteRenderer;
    private Animator _animator;

    private AnimatorOverrideController _overrideController;

    private GameObject _player;

    private bool _inCollider;

    public void SetDialog(Dialog_SO dialog)
    {
        _dialog = dialog;
    }

    public void OnTriggerEnter2D(Collider2D collision)
    {
        if (_dialog == null) return;
        _inCollider = true;
        _displayActionContainer.SetActive(true);
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (_dialog == null) return;
        _inCollider = false;
        _displayActionContainer.SetActive(false);
    }

    private void Awake()
    {
        if (_animator != null && _animator.runtimeAnimatorController != null)
        {
            _overrideController = new AnimatorOverrideController(_animator.runtimeAnimatorController);
        }
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _animator = GetComponent<Animator>();
        _player = GameObject.FindWithTag("Player");
    }

    private void Start()
    {
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

        if (_entity != null && _entity.enitityAnimation != null && _animator != null)
        {
            _overrideController["BaseClip"] = _entity.enitityAnimation;
            _animator.runtimeAnimatorController = _overrideController;
            _animator.Play("BaseClip", 0, 0f);
        }
    }

    private void Update()
    {
        if (_inCollider && _dialogAction != null && _dialogAction.action.WasPressedThisFrame())
        {
            DialogManager.instance.Init(_dialog);
        }

        if (_player != null && _spriteRenderer != null)
        {
            _spriteRenderer.flipX = _player.transform.position.x < transform.position.x;
        }
    }
}
