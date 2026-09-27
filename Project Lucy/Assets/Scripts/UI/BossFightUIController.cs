using HierarchicalStateMachine;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Connects the boss-fight lifecycle to the HUD. Listens to BossGate's static fight events
/// (the gate lives in the level, the HUD cannot be wired to it in the Inspector) and tells the
/// boss bar to bind/show/hide. Also moves the task list below the boss bar while it is up.
/// </summary>
public class BossFightUIController : MonoBehaviour
{
    [SerializeField] private BossHealthBarUI bossBar;

    [Tooltip("Retangulo da barra do chefe + recompensa; a lista de tasks desce ate abaixo dele.")]
    [SerializeField] private RectTransform bossBlock;

    [Tooltip("Espaco extra entre a barra do chefe e a lista de tasks.")]
    [SerializeField] private float taskListMargin = 12f;

    private bool taskListMoved;

    private void OnEnable()
    {
        BossGate.OnBossFightStarted += HandleFightStarted;
        BossGate.OnBossFightEnded += HandleFightEnded;
    }

    private void OnDisable()
    {
        BossGate.OnBossFightStarted -= HandleFightStarted;
        BossGate.OnBossFightEnded -= HandleFightEnded;

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
