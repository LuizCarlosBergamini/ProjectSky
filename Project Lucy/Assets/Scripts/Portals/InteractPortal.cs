using System;
using HierarchicalStateMachine;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// A portal the player uses on purpose: standing inside the trigger shows a label with the action key (the same
/// "E" box as the NPCs) and only pressing that key fires the portal. What it does is up to whoever listens:
/// the UnityEvent for scene wiring (back to the Hub) or <see cref="Interacted"/> from code (boss selector).
/// Has no sprite of its own yet; the art goes on a SpriteRenderer added next to it.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class InteractPortal : MonoBehaviour
{
    [Tooltip("Texto mostrado acima do portal quando o jogador chega perto.")]
    [SerializeField] private string _label = "Voltar ao hub?";

    [Tooltip("Tecla de acao, a mesma dos dialogos dos NPCs (E).")]
    [SerializeField] private InputActionReference _interactAction;

    [SerializeField] private string _playerTag = "Player";

    [Header("Label")]
    [Tooltip("Objeto com o texto e a tecla. Fica escondido ate o jogador entrar no portal.")]
    [SerializeField] private GameObject _prompt;
    [SerializeField] private TextMeshProUGUI _labelText;
    [SerializeField] private TextMeshProUGUI _keyText;
    [Tooltip("Caixa da tecla; a largura acompanha o nome da tecla, como nos NPCs.")]
    [SerializeField] private LayoutElement _keyBox;

    [Header("Events")]
    [SerializeField] private UnityEvent _onInteract;

    /// <summary>Raised when the player presses the action key inside the portal, before the UnityEvent.</summary>
    public event Action Interacted;

    // A player can carry more than one collider; the portal counts them so leaving with one does not hide the label.
    private int _playerColliders;
    private PlayerStateDriver _player;

    public bool PlayerInside => _playerColliders > 0;

    private void Awake()
    {
        if (_labelText != null) _labelText.text = _label;
        ShowPrompt(false);
    }

    private void Start()
    {
        if (_interactAction == null || _interactAction.action == null || _keyText == null) return;
        if (_interactAction.action.bindings.Count == 0) return;

        _keyText.text = _interactAction.action.bindings[0].ToDisplayString();
        if (_keyBox != null) _keyBox.preferredWidth = _keyText.text.Length * 2f + 2f;
    }

    private void OnDisable()
    {
        // Disabled with the player inside (scene unload, gate closing): exit is never called for those colliders.
        _playerColliders = 0;
        _player = null;
        ShowPrompt(false);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(_playerTag)) return;

        _playerColliders++;
        if (_player == null) _player = other.GetComponentInParent<PlayerStateDriver>();
        ShowPrompt(true);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag(_playerTag)) return;

        _playerColliders = Mathf.Max(0, _playerColliders - 1);
        if (_playerColliders > 0) return;

        _player = null;
        ShowPrompt(false);
    }

    private void Update()
    {
        if (!PlayerInside || !CanInteract()) return;
        if (_interactAction == null || _interactAction.action == null || !_interactAction.action.WasPressedThisFrame()) return;

        Interacted?.Invoke();
        _onInteract?.Invoke();
    }

    private bool CanInteract()
    {
        // Paused, or a menu that freezes the game (boss selector, upgrades) is open.
        if (Time.timeScale <= 0f) return false;
        if (LevelManager.instance != null && LevelManager.instance.IsLoading) return false;
        if (_player != null && !_player.IsAlive()) return false;
        return true;
    }

    private void ShowPrompt(bool visible)
    {
        if (_prompt != null) _prompt.SetActive(visible);
    }
}
