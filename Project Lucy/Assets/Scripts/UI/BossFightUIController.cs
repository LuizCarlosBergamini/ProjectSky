using System.Collections;
using HierarchicalStateMachine;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Connects the boss-fight lifecycle to the HUD. Listens to BossGate's static fight events
/// (the gate lives in the level, the HUD cannot be wired to it in the Inspector) and tells the
/// boss bar to bind/show/hide. Also moves the task list below the boss bar while it is up, and plays the
/// boss's defeat dialog (BossData_SO.defeatDialog) once it dies.
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

    private bool taskListMoved;

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
        if (data == null || data.defeatDialog == null) return;

        // OnBossFightWon fires before the gate's onBossDefeated completes the reward task, so a task that is
        // not finished yet means this is the first win. Without a TaskManager (level played on its own) it plays.
        if (data.defeatDialogOnlyOnFirstWin && TaskManager.instance != null
            && TaskManager.instance.GetStatus(data.rewardTaskId) == TaskManager.TaskStatus.Finished)
        {
            return;
        }

        StartCoroutine(PlayDefeatDialog(data.defeatDialog));
    }

    private IEnumerator PlayDefeatDialog(Dialog_SO dialog)
    {
        if (defeatDialogDelay > 0f) yield return new WaitForSeconds(defeatDialogDelay);
        if (DialogManager.instance != null) DialogManager.instance.Init(dialog);
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
