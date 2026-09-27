using HierarchicalStateMachine;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lucy's health bar, top-left of the level HUD. Binds to the scene's player and redraws only
/// when the player raises HealthChanged (damage, healing, max-health upgrades).
/// </summary>
public class PlayerHealthBarUI : MonoBehaviour
{
    [SerializeField] private HealthBarView bar;
    [SerializeField] private Image portrait;
    [SerializeField] private TextMeshProUGUI nameText;

    [Tooltip("Retrato e nome vem deste Entity (o mesmo asset da Lucy usado nos dialogos).")]
    [SerializeField] private Entity_SO portraitSource;

    [Tooltip("Jogador desta cena. Se vazio, e procurado uma vez ao iniciar.")]
    [SerializeField] private PlayerStateDriver player;

    private bool started;
    private bool bound;

    private void Awake()
    {
        if (portraitSource == null) return;
        if (portrait != null && portraitSource.entitySprite != null) portrait.sprite = portraitSource.entitySprite;
        if (nameText != null) nameText.text = portraitSource.entityName;
    }

    private void OnEnable()
    {
        // Before Start the player may not have run Awake yet; Start does the first bind.
        if (started) Bind();
    }

    private void Start()
    {
        started = true;
        if (player == null) player = FindFirstObjectByType<PlayerStateDriver>();
        if (player == null) Debug.LogWarning($"{name}: nenhum PlayerStateDriver na cena.", this);
        Bind();
    }

    private void OnDisable()
    {
        Unbind();
    }

    private void Bind()
    {
        if (bound || player == null || bar == null) return;
        bound = true;
        player.HealthChanged += HandleHealthChanged;
        bar.SetValues(player.CurrentHealth, player.MaxHealth, true);
    }

    private void Unbind()
    {
        if (!bound) return;
        bound = false;
        // ReferenceEquals: the player can already be destroyed on scene unload, and unsubscribing
        // from its C# event is still valid (and required) then.
        if (!ReferenceEquals(player, null)) player.HealthChanged -= HandleHealthChanged;
    }

    private void HandleHealthChanged(float current, float max)
    {
        bar.SetValues(current, max, false);
    }
}
