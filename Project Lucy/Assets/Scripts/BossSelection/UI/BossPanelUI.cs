using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One boss column of the selector: name, info icons, preview, lock overlay and reward grid. Only draws the
/// state it is given and reports clicks and keyboard focus upwards; BossSelectorUI decides what they mean.
/// </summary>
public class BossPanelUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [Header("Referencias")]
    [SerializeField] private Button _button;
    [Tooltip("Tudo que treme ao negar o clique. Filho do painel, fora do controle do layout.")]
    [SerializeField] private RectTransform _visual;
    [SerializeField] private Image _background;
    [SerializeField] private Image _border;
    [SerializeField] private Image _selection;
    [SerializeField] private Image _glow;
    [SerializeField] private TextMeshProUGUI _nameText;

    [Header("Icones de informacao")]
    [SerializeField] private RectTransform _infoIconContainer;
    [SerializeField] private BossInfoIconUI _infoIconPrefab;

    [Header("Preview")]
    [SerializeField] private Image _preview;
    [SerializeField] private AspectRatioFitter _previewFitter;
    [SerializeField] private Image _bossArt;
    [SerializeField] private GameObject _lockOverlay;
    [SerializeField] private TextMeshProUGUI _lockHintText;
    [SerializeField] private GameObject _defeatedBadge;

    [Header("Recompensa")]
    [SerializeField] private GameObject _rewardSection;
    [SerializeField] private TextMeshProUGUI _rewardHeaderText;
    [SerializeField] private RectTransform _rewardContainer;
    [SerializeField] private RewardSlotUI _rewardSlotPrefab;

    [Header("Aparencia")]
    [Tooltip("Material usado no preview e nos icones quando bloqueado (ex: UI_Grayscale).")]
    [SerializeField] private Material _lockedMaterial;
    [SerializeField] private Color _lockedTint = new(0.45f, 0.45f, 0.48f, 1f);
    [SerializeField] private Color _lockedTextColor = new(0.55f, 0.55f, 0.58f, 1f);
    [SerializeField] private string _rewardHeader = "RECOMPENSA";
    [SerializeField] private string _rewardClaimedHeader = "RECOMPENSA (JÁ OBTIDA)";
    [SerializeField] private float _pulseSpeed = 3f;

    [Header("Negado")]
    [SerializeField] private float _shakeDuration = 0.35f;
    [SerializeField] private float _shakeDistance = 10f;

    private readonly List<BossInfoIconUI> _infoIcons = new();
    private readonly List<RewardSlotUI> _rewardSlots = new();
    private IReadOnlyList<InventorySlot> _shownRewards;
    private int _shownRewardCount = -1;

    private Action<BossPanelUI> _onClick;
    private Action<BossPanelUI> _onFocus;
    private TooltipUI _tooltip;

    private Color _accent = Color.white;
    private bool _selected;
    private bool _hovered;
    private bool _focused;
    private Coroutine _shake;

    public BossData_SO Boss { get; private set; }
    public BossUnlockState State { get; private set; } = BossUnlockState.Locked;
    public bool IsSelectable => State != BossUnlockState.Locked;
    public Button Button => _button;

    public void Bind(BossData_SO boss, TooltipUI tooltip, Action<BossPanelUI> onClick, Action<BossPanelUI> onFocus)
    {
        Boss = boss;
        _tooltip = tooltip;
        _onClick = onClick;
        _onFocus = onFocus;
        _accent = boss.accentColor;
        name = $"BossPanel_{boss.bossId}";

        if (_nameText != null) _nameText.text = boss.DisplayName.ToUpperInvariant();

        if (_preview != null)
        {
            _preview.sprite = boss.previewImage;
            _preview.enabled = boss.previewImage != null;
            if (_previewFitter != null && boss.previewImage != null)
            {
                Rect rect = boss.previewImage.rect;
                _previewFitter.aspectRatio = rect.height > 0f ? rect.width / rect.height : 1f;
            }
        }

        if (_bossArt != null)
        {
            _bossArt.sprite = boss.entity != null ? boss.entity.entitySprite : null;
            _bossArt.enabled = _bossArt.sprite != null;
        }

        BuildInfoIcons();

        if (_button != null)
        {
            _button.onClick.RemoveListener(HandleClick);
            _button.onClick.AddListener(HandleClick);
        }

        SetSelected(false);
    }

    /// <param name="rewards">The boss's reward list, straight from its task.</param>
    /// <param name="rewardClaimable">False when beating the boss again would pay nothing.</param>
    public void Refresh(BossUnlockState state, IReadOnlyList<InventorySlot> rewards, bool rewardClaimable, string lockedHint)
    {
        State = state;
        bool locked = state == BossUnlockState.Locked;

        // Built once per list; rebuilt only if the task's list was swapped or resized in the Inspector.
        int rewardCount = rewards != null ? rewards.Count : 0;
        if (!ReferenceEquals(rewards, _shownRewards) || rewardCount != _shownRewardCount) BuildRewards(rewards);

        if (_nameText != null) _nameText.color = locked ? _lockedTextColor : _accent;
        SetLocked(_preview, locked);
        SetLocked(_bossArt, locked);

        foreach (BossInfoIconUI icon in _infoIcons) icon.SetDimmed(locked, _lockedMaterial);

        // Locked: still visible, so the player sees what they are working towards.
        bool dimRewards = locked || !rewardClaimable;
        foreach (RewardSlotUI slot in _rewardSlots) slot.SetDimmed(dimRewards, locked ? _lockedMaterial : null);
        if (_rewardHeaderText != null)
        {
            _rewardHeaderText.text = !locked && !rewardClaimable ? _rewardClaimedHeader : _rewardHeader;
            _rewardHeaderText.color = locked ? _lockedTextColor : Color.Lerp(_accent, Color.white, 0.5f);
        }

        if (_lockOverlay != null) _lockOverlay.SetActive(locked);
        if (_lockHintText != null) _lockHintText.text = lockedHint ?? "";
        if (_defeatedBadge != null) _defeatedBadge.SetActive(state == BossUnlockState.Defeated);

        RefreshHighlight();
    }

    public void SetSelected(bool selected)
    {
        _selected = selected;
        RefreshHighlight();
    }

    /// <summary>Short sideways shake: the "no" for clicking a locked boss.</summary>
    public void PlayDeniedEffect()
    {
        if (!isActiveAndEnabled || _visual == null) return;
        if (_shake != null) StopCoroutine(_shake);
        _shake = StartCoroutine(Shake());
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _hovered = true;
        RefreshHighlight();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _hovered = false;
        RefreshHighlight();
    }

    public void OnSelect(BaseEventData eventData)
    {
        _focused = true;
        RefreshHighlight();

        // A click also selects the button (on pointer down, before onClick). Only keyboard/gamepad focus
        // counts as choosing here; clicks go through HandleClick, so the first click never confirms.
        if (eventData is PointerEventData) return;
        _onFocus?.Invoke(this);
    }

    public void OnDeselect(BaseEventData eventData)
    {
        _focused = false;
        RefreshHighlight();
    }

    private void HandleClick() => _onClick?.Invoke(this);

    private void Update()
    {
        // Unscaled: the selector runs with Time.timeScale at 0.
        float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * _pulseSpeed);

        if (_selected && _selection != null) _selection.color = WithAlpha(Color.Lerp(_accent, Color.white, wave * 0.5f), 0.7f + wave * 0.3f);
        if (_selected && _glow != null) _glow.color = WithAlpha(_accent, 0.25f + wave * 0.3f);
    }

    private void RefreshHighlight()
    {
        bool locked = State == BossUnlockState.Locked;
        bool highlighted = _hovered || _focused;

        if (_border != null)
        {
            Color borderColor = locked ? _lockedTextColor : _accent;
            _border.color = WithAlpha(borderColor, highlighted || _selected ? 1f : 0.45f);
        }

        if (_selection != null) _selection.enabled = _selected;

        if (_glow != null)
        {
            _glow.enabled = _selected || (highlighted && !locked);
            if (!_selected) _glow.color = WithAlpha(_accent, 0.18f);
        }

        if (_background != null)
        {
            _background.color = highlighted && !locked ? new Color(0.11f, 0.11f, 0.13f, 1f) : new Color(0.07f, 0.07f, 0.08f, 1f);
        }
    }

    private void SetLocked(Image image, bool locked)
    {
        if (image == null) return;
        image.material = locked ? _lockedMaterial : null;
        image.color = locked ? _lockedTint : Color.white;
    }

    private void BuildInfoIcons()
    {
        foreach (BossInfoIconUI icon in _infoIcons)
        {
            if (icon != null) Destroy(icon.gameObject);
        }
        _infoIcons.Clear();

        if (_infoIconContainer == null || _infoIconPrefab == null || Boss.infoIcons == null) return;

        foreach (BossInfoIcon info in Boss.infoIcons)
        {
            if (info == null) continue;
            BossInfoIconUI icon = Instantiate(_infoIconPrefab, _infoIconContainer);
            icon.Set(info, Color.Lerp(_accent, Color.white, 0.35f), _tooltip);
            _infoIcons.Add(icon);
        }

        _infoIconContainer.gameObject.SetActive(_infoIcons.Count > 0);
    }

    private void BuildRewards(IReadOnlyList<InventorySlot> rewards)
    {
        foreach (RewardSlotUI slot in _rewardSlots)
        {
            if (slot != null) Destroy(slot.gameObject);
        }
        _rewardSlots.Clear();
        _shownRewards = rewards;
        _shownRewardCount = rewards != null ? rewards.Count : 0;

        if (rewards != null && _rewardContainer != null && _rewardSlotPrefab != null)
        {
            foreach (InventorySlot reward in rewards)
            {
                if (reward == null || reward.item == null) continue;
                RewardSlotUI slot = Instantiate(_rewardSlotPrefab, _rewardContainer);
                slot.Set(reward.item, reward.quantity, _tooltip);
                _rewardSlots.Add(slot);
            }
        }

        if (_rewardSection != null) _rewardSection.SetActive(_rewardSlots.Count > 0);
    }

    private IEnumerator Shake()
    {
        float time = 0f;
        while (time < _shakeDuration)
        {
            time += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(time / _shakeDuration);
            float offset = Mathf.Sin(t * Mathf.PI * 6f) * _shakeDistance * (1f - t);
            _visual.anchoredPosition = new Vector2(offset, 0f);
            yield return null;
        }

        _visual.anchoredPosition = Vector2.zero;
        _shake = null;
    }

    private void OnDisable()
    {
        if (_visual != null) _visual.anchoredPosition = Vector2.zero;
        _shake = null;
        _hovered = false;
        _focused = false;
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }
}
