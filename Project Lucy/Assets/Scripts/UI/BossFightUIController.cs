using System.Collections;
using HierarchicalStateMachine;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Connects the boss-fight lifecycle to the HUD. Listens to BossGate's static fight events
/// (the gate lives in the level, the HUD cannot be wired to it in the Inspector) and tells the
/// boss bar to bind/show/hide. Also moves the task list below the boss bar while it is up, and plays the
/// boss's defeat dialog (BossData_SO.defeatDialog) once it dies, followed by its end screen message
/// (BossData_SO.endScreenMessage, e.g. the end of the demo after the mage).
/// </summary>
public class BossFightUIController : MonoBehaviour
{
    [SerializeField] private BossHealthBarUI bossBar;

    [Tooltip("Retangulo da barra do chefe + recompensa; a lista de tasks desce ate abaixo dele.")]
    [SerializeField] private RectTransform bossBlock;

    [Tooltip("Espaco extra entre a barra do chefe e a lista de tasks.")]
    [SerializeField] private float taskListMargin = 12f;

    [Tooltip("Espera depois da morte do chefe antes do dialogo de fim de luta, para a animacao de morte aparecer.")]
    [SerializeField] private float defeatDialogDelay = 1f;

    [Tooltip("Tela com a mensagem de fim (BossData_SO.endScreenMessage) e o botao Continuar.")]
    [SerializeField] private BossEndScreen endScreen;

    private bool taskListMoved;
    private bool defeatDialogDone;

    private void OnEnable()
    {
        BossGate.OnBossFightStarted += HandleFightStarted;
        BossGate.OnBossFightEnded += HandleFightEnded;
        BossGate.OnBossFightWon += HandleFightWon;
    }

    private void OnDisable()
    {
        BossGate.OnBossFightStarted -= HandleFightStarted;
        BossGate.OnBossFightEnded -= HandleFightEnded;
        BossGate.OnBossFightWon -= HandleFightWon;
        DialogManager.OnDialogFinished -= HandleDefeatDialogFinished;

        // TaskManager survives scene loads: never leave the Hub with a pushed-down task list.
        RestoreTaskList();
    }

    private void HandleFightStarted(EnemyStateDriver boss)
    {
        if (bossBar == null || boss == null) return;

        // Killed during the gate's close delay: its Died event (and the fight end) are still on the
        // way, but there is no fight left to show.
        if (!boss.IsAlive()) return;

        bossBar.Bind(boss);
        bossBar.Show();
        MoveTaskListBelowBossBar();
    }

    private void HandleFightEnded(EnemyStateDriver boss)
    {
        if (bossBar != null) bossBar.Hide();
        RestoreTaskList();
    }

    private void HandleFightWon(EnemyStateDriver boss)
    {
        if (ReferenceEquals(boss, null)) return;
        BossData_SO data = boss.BossData;
        if (data == null) return;

        // OnBossFightWon fires before the gate's onBossDefeated completes the reward task, so a task that is
        // not finished yet means this is the first win. Without a TaskManager (level played on its own) it counts as one.
        bool firstWin = TaskManager.instance == null
            || TaskManager.instance.GetStatus(data.rewardTaskId) != TaskManager.TaskStatus.Finished;

        Dialog_SO dialog = data.defeatDialog != null && (firstWin || !data.defeatDialogOnlyOnFirstWin) ? data.defeatDialog : null;
        string message = endScreen != null && !string.IsNullOrWhiteSpace(data.endScreenMessage)
            && (firstWin || !data.endScreenOnlyOnFirstWin) ? data.endScreenMessage : null;
        if (dialog == null && message == null) return;

        StartCoroutine(PlayDefeatSequence(dialog, message));
    }

    private IEnumerator PlayDefeatSequence(Dialog_SO dialog, string message)
    {
        if (defeatDialogDelay > 0f) yield return new WaitForSeconds(defeatDialogDelay);

        if (dialog != null && DialogManager.instance != null)
        {
            // Any dialog closing releases the wait: if another one was open, Init ignored ours and that one ends it.
            defeatDialogDone = false;
            DialogManager.OnDialogFinished += HandleDefeatDialogFinished;
            DialogManager.instance.Init(dialog);
            yield return new WaitUntil(() => defeatDialogDone);
            DialogManager.OnDialogFinished -= HandleDefeatDialogFinished;
        }

        if (message == null) yield break;

        // Never on top of the pause, the game over or a scene change (the player may already be leaving).
        yield return new WaitUntil(() => !PauseManager.IsGamePaused);
        GameManager gameManager = GameManager.instance;
        if (gameManager != null && gameManager.gameOverUI != null && gameManager.gameOverUI.activeInHierarchy) yield break;
        if (LevelManager.instance != null && LevelManager.instance.IsLoading) yield break;

        endScreen.Open(message);
    }

    private void HandleDefeatDialogFinished(Dialog_SO dialog)
    {
        defeatDialogDone = true;
    }

    private void MoveTaskListBelowBossBar()
    {
        if (TaskManager.instance == null || bossBlock == null) return;

        // Rewards were just (re)built; measure the block with its final size.
        LayoutRebuilder.ForceRebuildLayoutImmediate(bossBlock);

        Canvas canvas = bossBlock.GetComponentInParent<Canvas>();
        float scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
        Vector3[] corners = new Vector3[4];
        bossBlock.GetWorldCorners(corners);

        // Overlay canvas: world units are screen pixels. The TaskManager canvas uses the same
        // CanvasScaler settings, so dividing by this scale gives its units too.
        float distanceFromTop = (Screen.height - corners[0].y) / Mathf.Max(scale, 0.0001f);

        TaskManager.instance.SetFightLayout(true, distanceFromTop + taskListMargin);
        taskListMoved = true;
    }

    private void RestoreTaskList()
    {
        if (!taskListMoved) return;
        taskListMoved = false;
        if (TaskManager.instance != null) TaskManager.instance.SetFightLayout(false, 0f);
    }
}
