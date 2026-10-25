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
/// The portal art is the animated sprite in <see cref="_visual"/> (the Hub's portal, copied by Build Portals); it is
/// stood on the ground under the trigger and the label sits just above it.
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
    [Tooltip("Cor da letra da tecla. Preta como nos NPCs: a caixa e clara, entao letra branca some.")]
    [SerializeField] private Color _keyTextColor = Color.black;

    [Header("Visual")]
    [Tooltip("Sprite animado do portal. A label fica logo acima dele.")]
    [SerializeField] private SpriteRenderer _visual;
    [Tooltip("Apoia o sprite no chao embaixo do portal ao iniciar. Desligue se ele ja esta posicionado a mao.")]
    [SerializeField] private bool _snapVisualToGround = true;
    [Tooltip("Distancia entre o topo do sprite do portal e a label, em unidades.")]
    [SerializeField] private float _labelAboveVisual = 0.5f;

    [Header("Sem visual")]
    [Tooltip("Sem sprite, a label fica esta distancia acima da cabeca da Lucy.")]
    [SerializeField] private float _labelAboveHead = 1f;
    [Tooltip("Altura usada quando a Lucy (ou o collider dela) nao e encontrada na cena.")]
    [SerializeField] private float _fallbackPlayerHeight = 2f;

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
        PlaceVisuals();

        if (_keyText != null) _keyText.color = _keyTextColor;

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

    /// <summary>Stands the portal sprite on the ground and puts the label just above it.</summary>
    private void PlaceVisuals()
    {
        GameObject player = GameObject.FindWithTag(_playerTag);
        float ground = GroundBelow(player);
        float labelBottom = ground + PlayerHeight(player) + _labelAboveHead;

        if (_visual != null)
        {
            if (_snapVisualToGround)
            {
                Transform visual = _visual.transform;
                visual.position = new Vector3(transform.position.x, visual.position.y + ground - _visual.bounds.min.y,
                    visual.position.z);
            }

            labelBottom = _visual.bounds.max.y + _labelAboveVisual;
        }

        if (_prompt == null) return;

        // The prompt's pivot is its centre; lift it by half the key box so its bottom edge is the reference.
        float halfPrompt = _keyBox != null ? _keyBox.preferredHeight * _prompt.transform.lossyScale.y * 0.5f : 0f;
        Vector3 position = _prompt.transform.position;
        float x = _visual != null ? _visual.bounds.center.x : transform.position.x;
        _prompt.transform.position = new Vector3(x, labelBottom + halfPrompt, position.z);
    }

    /// <summary>
    /// Lucy's body height, from her solid collider. Not the sprite: the Martial Hero frames are 200x200 with most of
    /// it empty, so their bounds are about four times taller than she looks and pushed the label off screen.
    /// </summary>
    private float PlayerHeight(GameObject player)
    {
        if (player == null) return _fallbackPlayerHeight;

        float height = 0f;
        foreach (Collider2D body in player.GetComponents<Collider2D>())
        {
            if (!body.enabled || body.isTrigger) continue;
            height = Mathf.Max(height, body.bounds.size.y);
        }

        return height > 0f ? height : _fallbackPlayerHeight;
    }

    /// <summary>Top of the ground under the portal (Lucy's ground mask), or the bottom of the trigger without one.</summary>
    private float GroundBelow(GameObject player)
    {
        Collider2D trigger = GetComponent<Collider2D>();
        float fallback = trigger != null ? trigger.bounds.min.y : transform.position.y;

        PlayerStateDriver driver = player != null ? player.GetComponent<PlayerStateDriver>() : null;
        if (driver == null || driver.groundMask.value == 0) return fallback;

        Vector2 origin = trigger != null ? (Vector2)trigger.bounds.center : (Vector2)transform.position;
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, 50f, driver.groundMask);
        return hit.collider != null ? hit.point.y : fallback;
    }

    private void ShowPrompt(bool visible)
    {
        if (_prompt != null) _prompt.SetActive(visible);
    }
}
