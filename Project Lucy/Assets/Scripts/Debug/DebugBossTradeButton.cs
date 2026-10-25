#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using HierarchicalStateMachine;
using UnityEngine;

/// <summary>
/// TEMPORARIO, so para teste: apagar este arquivo (e o .meta) antes do merge.
/// Mostra um botao durante a luta contra um chefe que mata o chefe e, logo em seguida, o jogador, para testar a
/// morte simultanea. Se cria sozinho ao abrir qualquer cena, entao nao precisa de nada nas cenas.
/// </summary>
public class DebugBossTradeButton : MonoBehaviour
{
    private const float KillDamage = 1e9f;
    private const int MaxBossKillFrames = 120;

    private bool running;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        GameObject go = new("[DEBUG] BossTradeButton");
        DontDestroyOnLoad(go);
        go.AddComponent<DebugBossTradeButton>();
    }

    private void OnGUI()
    {
        if (running) return;

        EnemyStateDriver boss = FindBoss();
        PlayerStateDriver player = FindFirstObjectByType<PlayerStateDriver>();
        if (boss == null || player == null || !player.IsAlive()) return;

        if (GUI.Button(new Rect(10f, Screen.height - 50f, 260f, 40f), "[DEBUG] Matar chefe + jogador"))
            StartCoroutine(KillBossThenPlayer(boss, player));
    }

    private IEnumerator KillBossThenPlayer(EnemyStateDriver boss, PlayerStateDriver player)
    {
        running = true;

        // TakeDamage ignores hits during invulnerability, the intro dialog or a dash: keep trying for a moment.
        for (int frame = 0; frame < MaxBossKillFrames && boss != null && boss.IsAlive(); frame++)
        {
            boss.HasTakenDamage = false;
            boss.TakeDamage(KillDamage, Vector2.zero);
            if (boss.IsAlive()) yield return null;
        }

        if (boss != null && boss.IsAlive())
        {
            Debug.LogWarning("[DEBUG] O chefe nao aceitou o dano (dialogo de intro ainda aberto?). Jogador poupado.");
            running = false;
            yield break;
        }

        if (player != null)
        {
            player.HasTakenDamage = false;
            player.TakeDamage(KillDamage, Vector2.zero);
        }

        Debug.Log("[DEBUG] Chefe e jogador mortos juntos.");
        running = false;
    }

    private static EnemyStateDriver FindBoss()
    {
        foreach (EnemyStateDriver enemy in FindObjectsByType<EnemyStateDriver>(FindObjectsSortMode.None))
        {
            if (enemy.BossData != null && enemy.IsAlive()) return enemy;
        }

        return null;
    }
}
#endif
