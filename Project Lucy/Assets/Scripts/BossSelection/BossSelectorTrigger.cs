using UnityEngine;

/// <summary>
/// The Hub's way into the boss fights. Replaces the old SceneTeleporterByTrigger that loaded Level1 directly:
/// the portal now opens the boss selector, which decides the scene. With an <see cref="InteractPortal"/> on the
/// same object the selector opens on the action key ("Escolha a fase"); without one it falls back to the old
/// behaviour of opening as soon as the player walks in.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class BossSelectorTrigger : MonoBehaviour
{
    [Tooltip("Seletor aberto pelo portal. Vazio = o BossSelectorUI da cena.")]
    [SerializeField] private BossSelectorUI _selector;

    [SerializeField] private string _playerTag = "Player";

    private InteractPortal _portal;

    private void Awake()
    {
        TryGetComponent(out _portal);
    }

    private void OnEnable()
    {
        if (_portal != null) _portal.Interacted += OpenSelector;
    }

    private void OnDisable()
    {
        if (_portal != null) _portal.Interacted -= OpenSelector;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (_portal != null) return;
        if (!collision.CompareTag(_playerTag)) return;

        OpenSelector();
    }

    public void OpenSelector()
    {
        BossSelectorUI selector = _selector != null ? _selector : BossSelectorUI.instance;
        if (selector == null)
        {
            Debug.LogWarning($"{name}: nenhum BossSelectorUI na cena para abrir.", this);
            return;
        }

        selector.Open();
    }
}
