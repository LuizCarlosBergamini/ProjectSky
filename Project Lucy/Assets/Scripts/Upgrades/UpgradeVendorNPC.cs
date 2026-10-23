using UnityEngine;

/// <summary>
/// Turns a regular NPC into the upgrade vendor: the NPC component still owns the trigger, the prompt and
/// the dialog, and once the dialog this NPC started is over the upgrade canvas opens.
/// Only shows up in the Hub after the first boss is beaten (any boss, see BossProgressionManager.HasDefeatedAnyBoss).
/// </summary>
[RequireComponent(typeof(NPC))]
public class UpgradeVendorNPC : MonoBehaviour
{
    [Tooltip("Canvas aberto ao fim do dialogo. Vazio = o UpgradeCanvas da cena.")]
    [SerializeField] private UpgradeCanvas _upgradeCanvas;

    [Tooltip("Esconde o vendedor ao carregar a cena enquanto nenhum chefe foi derrotado.")]
    [SerializeField] private bool _requireBossDefeat = true;

    private NPC _npc;

    // The dialog this NPC started and has not seen finish yet. Matching on it (instead of on the asset alone)
    // keeps another NPC that shares the same Dialog_SO from opening the shop.
    private Dialog_SO _pendingDialog;

    private void Awake()
    {
        _npc = GetComponent<NPC>();
    }

    private void Start()
    {
        // Checked once on load, like DisableOnTaskStatus: bosses are fought in their own scenes, so the Hub is
        // always reloaded after a defeat. Without a BossProgressionManager (scene played on its own) the vendor stays.
        BossProgressionManager progression = BossProgressionManager.instance;
        if (_requireBossDefeat && progression != null && !progression.HasDefeatedAnyBoss)
        {
            gameObject.SetActive(false);
        }
    }

    private void OnEnable()
    {
        _npc.DialogStarted += HandleDialogStarted;
        DialogManager.OnDialogFinished += HandleDialogFinished;
    }

    private void OnDisable()
    {
        _npc.DialogStarted -= HandleDialogStarted;
        DialogManager.OnDialogFinished -= HandleDialogFinished;
        _pendingDialog = null;
    }

    private void HandleDialogStarted(Dialog_SO dialog)
    {
        _pendingDialog = dialog;
    }

    private void HandleDialogFinished(Dialog_SO dialog)
    {
        if (_pendingDialog == null || dialog != _pendingDialog) return;
        _pendingDialog = null;

        UpgradeCanvas canvas = _upgradeCanvas != null ? _upgradeCanvas : UpgradeCanvas.instance;
        if (canvas == null)
        {
            Debug.LogWarning($"{name}: nenhum UpgradeCanvas na cena para abrir.", this);
            return;
        }

        // The NPC would otherwise restart its dialog when E is pressed with the menu open.
        _npc.enabled = false;
        canvas.Open(HandleCanvasClosed);
    }

    private void HandleCanvasClosed()
    {
        if (this != null) _npc.enabled = true;
    }
}
