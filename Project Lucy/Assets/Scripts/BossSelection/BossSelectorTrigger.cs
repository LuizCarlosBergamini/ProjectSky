using UnityEngine;

/// <summary>
/// The Hub's way into the boss fights. Replaces the old SceneTeleporterByTrigger that loaded Level1 directly:
/// walking into the trigger now opens the boss selector, which decides the scene. Stays inside the trigger
/// after closing without reopening; leaving and coming back opens it again.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class BossSelectorTrigger : MonoBehaviour
{
    [Tooltip("Seletor aberto ao entrar. Vazio = o BossSelectorUI da cena.")]
    [SerializeField] private BossSelectorUI _selector;

    [SerializeField] private string _playerTag = "Player";

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!collision.CompareTag(_playerTag)) return;

        BossSelectorUI selector = _selector != null ? _selector : BossSelectorUI.instance;
        if (selector == null)
        {
            Debug.LogWarning($"{name}: nenhum BossSelectorUI na cena para abrir.", this);
            return;
        }

        selector.Open();
    }
}
