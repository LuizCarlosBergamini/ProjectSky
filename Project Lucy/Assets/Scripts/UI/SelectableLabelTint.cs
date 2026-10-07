using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A Button only tints one graphic, so a disabled button's text stays bright. This fades the label along with it,
/// so a non-interactable button (e.g. Continue with no save) reads as greyed out. Covers CanvasGroups too.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Selectable))]
public class SelectableLabelTint : MonoBehaviour
{
    [Tooltip("Texto que acompanha o estado. Vazio = primeiro TMP filho.")]
    [SerializeField] private TMP_Text _label;

    [Tooltip("Alpha do texto quando o botao esta desativado.")]
    [Range(0f, 1f)]
    [SerializeField] private float _disabledAlpha = 0.35f;

    private Selectable _selectable;
    private float _enabledAlpha = 1f;
    private bool? _applied;

    private void Awake()
    {
        _selectable = GetComponent<Selectable>();
        if (_label == null) _label = GetComponentInChildren<TMP_Text>(true);
        if (_label != null) _enabledAlpha = _label.color.a;
    }

    private void OnEnable()
    {
        _applied = null;
        Apply();
    }

    private void LateUpdate()
    {
        // Selectable raises no event when it changes state; comparing one bool per frame is cheap.
        Apply();
    }

    private void Apply()
    {
        if (_label == null || _selectable == null) return;

        bool interactable = _selectable.IsInteractable();
        if (_applied == interactable) return;
        _applied = interactable;

        Color color = _label.color;
        color.a = interactable ? _enabledAlpha : _disabledAlpha;
        _label.color = color;
    }
}
