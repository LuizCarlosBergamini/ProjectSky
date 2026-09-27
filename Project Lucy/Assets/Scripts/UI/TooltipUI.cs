using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small title + text box that floats above (or below, near the top edge) whatever is hovered, kept inside
/// its parent rect. Never blocks the pointer. One per screen; hover handlers call Show/Hide.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class TooltipUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _title;
    [SerializeField] private TextMeshProUGUI _body;

    [Tooltip("Distancia entre o tooltip e o objeto apontado (unidades do canvas).")]
    [SerializeField] private float _gap = 10f;

    private readonly Vector3[] _corners = new Vector3[4];
    private readonly Vector3[] _ownCorners = new Vector3[4];
    private RectTransform _rect;
    private CanvasGroup _group;
    private RectTransform _anchor;

    private void Awake()
    {
        // No hiding here: Awake can run inside the first Show, when the object is activated.
        EnsureInitialized();
    }

    private void EnsureInitialized()
    {
        if (_group != null) return;
        _rect = (RectTransform)transform;
        _group = GetComponent<CanvasGroup>();
        _group.interactable = false;
        _group.blocksRaycasts = false;
    }

    public void Show(string title, string body, RectTransform anchor)
    {
        if (anchor == null || (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body)))
        {
            Hide();
            return;
        }

        EnsureInitialized();
        gameObject.SetActive(true);
        _anchor = anchor;
        SetText(_title, title);
        SetText(_body, body);

        _group.alpha = 1f;
        LayoutRebuilder.ForceRebuildLayoutImmediate(_rect);
        Place(anchor);
    }

    /// <summary>Hides only if the tooltip still belongs to <paramref name="anchor"/>, so exit/enter order never matters.</summary>
    public void Hide(RectTransform anchor)
    {
        if (anchor == _anchor) Hide();
    }

    public void Hide()
    {
        _anchor = null;
        if (_group != null) _group.alpha = 0f;
        gameObject.SetActive(false);
    }

    private void Place(RectTransform anchor)
    {
        RectTransform bounds = _rect.parent as RectTransform;
        float scale = _rect.lossyScale.y;
        float gap = _gap * scale;

        anchor.GetWorldCorners(_corners);
        Vector3 top = (_corners[1] + _corners[2]) * 0.5f;
        Vector3 bottom = (_corners[0] + _corners[3]) * 0.5f;

        // Above the anchor by default.
        _rect.pivot = new Vector2(0.5f, 0f);
        _rect.position = top + Vector3.up * gap;

        if (bounds == null) return;

        bounds.GetWorldCorners(_corners);
        Vector3 boundsMin = _corners[0];
        Vector3 boundsMax = _corners[2];

        Vector3[] own = _ownCorners;
        _rect.GetWorldCorners(own);

        // No room above: flip below the anchor.
        if (own[1].y > boundsMax.y)
        {
            _rect.pivot = new Vector2(0.5f, 1f);
            _rect.position = bottom + Vector3.down * gap;
            _rect.GetWorldCorners(own);
        }

        Vector3 shift = Vector3.zero;
        if (own[0].x < boundsMin.x) shift.x = boundsMin.x - own[0].x;
        else if (own[2].x > boundsMax.x) shift.x = boundsMax.x - own[2].x;
        if (own[0].y < boundsMin.y) shift.y = boundsMin.y - own[0].y;
        _rect.position += shift;
    }

    private static void SetText(TextMeshProUGUI text, string value)
    {
        if (text == null) return;
        text.text = value ?? "";
        text.gameObject.SetActive(!string.IsNullOrWhiteSpace(value));
    }
}
