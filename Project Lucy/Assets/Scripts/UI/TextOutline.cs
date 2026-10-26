using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// A solid outline for a TextMeshPro text (3D or UI): eight copies of the text in the outline colour, pushed
/// out by the thickness in every direction and drawn behind it. TMP's own outline (Outline in the material)
/// cannot be thicker than the font atlas padding allows, which for the Jersey 10 asset is about a quarter of a
/// font pixel; the copies have no such limit and keep the square corners of the pixel font.
/// A 3D text puts its copies one sorting order behind. A UI text draws its children on top of itself, so it
/// also gets a ninth copy in the text's own colour, drawn last, that covers the outline copies.
/// The copies follow the text, font, size, layout and alpha of the source (so MovementTutorial's fade reaches
/// them), are hidden in the Hierarchy and are never saved with the scene.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public class TextOutline : MonoBehaviour
{
    private const string CopyName = "TextOutline Copy";
    private const string FaceName = "TextOutline Face";

    // Full diagonals, not normalised: the corners come out square like the font's pixels.
    private static readonly Vector2[] Directions =
    {
        new(1f, 0f), new(1f, 1f), new(0f, 1f), new(-1f, 1f),
        new(-1f, 0f), new(-1f, -1f), new(0f, -1f), new(1f, -1f)
    };

    [SerializeField] private Color _color = Color.black;

    [Tooltip("Espessura do contorno em unidades do mundo (a Hub tem 32 pixels por unidade).")]
    [Min(0f)]
    [SerializeField] private float _thickness = 0.05f;

    private TMP_Text _source;
    private TMP_Text[] _copies;
    private TMP_Text _face;
    private bool _dirty;

    private bool IsUI => _source is TextMeshProUGUI;

    private void OnEnable()
    {
        _source = GetComponent<TMP_Text>();
        CollectCopies();
        SetCopiesVisible(true);
        Sync();
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(HandleTextChanged);
    }

    // The copies are kept, not destroyed: MovementTutorial may have cached them, and it switches the tutorial
    // off and on.
    private void OnDisable()
    {
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(HandleTextChanged);
        SetCopiesVisible(false);
    }

    // Covers this component being removed; when the whole object goes, the copies go with it.
    private void OnDestroy()
    {
        foreach (TMP_Text copy in AllCopies())
        {
            DestroyObject(copy.gameObject);
        }
    }

    private void OnValidate()
    {
        _dirty = true;
    }

    private void LateUpdate()
    {
        if (!_dirty) return;

        _dirty = false;
        Sync();
    }

    // Fires after any text rebuilds its mesh, including after a colour change; only the source matters.
    private void HandleTextChanged(Object changed)
    {
        if (changed == _source) Sync();
    }

    private void CollectCopies()
    {
        if (_copies != null) return;

        // After a script reload the copies are still in the scene but these fields are not.
        List<TMP_Text> found = new();
        TMP_Text face = null;
        foreach (Transform child in transform)
        {
            if (!child.TryGetComponent(out TMP_Text text)) continue;
            if (child.name == CopyName) found.Add(text);
            else if (child.name == FaceName) face = text;
        }

        if (found.Count == Directions.Length && (face != null) == IsUI)
        {
            _copies = found.ToArray();
            _face = face;
            return;
        }

        foreach (TMP_Text copy in found)
        {
            DestroyObject(copy.gameObject);
        }

        if (face != null) DestroyObject(face.gameObject);

        // Created in drawing order: a UI text draws its later children on top.
        _copies = new TMP_Text[Directions.Length];
        for (int i = 0; i < Directions.Length; i++)
        {
            _copies[i] = CreateCopy(CopyName);
            _copies[i].overrideColorTags = true;
        }

        _face = IsUI ? CreateCopy(FaceName) : null;
    }

    private TMP_Text CreateCopy(string copyName)
    {
        GameObject go = new(copyName, typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
        go.layer = gameObject.layer;
        go.transform.SetParent(transform, false);

        TMP_Text copy = (TMP_Text)go.AddComponent(_source.GetType());
        copy.raycastTarget = false;
        return copy;
    }

    private void Sync()
    {
        if (_source == null || _copies == null) return;

        Color color = _color;
        color.a *= _source.color.a;

        // The thickness is in world units whatever the scale of the text (UI canvases are scaled way down).
        Vector3 scale = transform.lossyScale;
        Vector2 step = new(_thickness / NonZero(scale.x), _thickness / NonZero(scale.y));

        for (int i = 0; i < _copies.Length; i++)
        {
            if (_copies[i] == null) continue;

            CopySettings(_copies[i], Vector2.Scale(Directions[i], step), -1);
            _copies[i].color = color;
        }

        if (_face != null)
        {
            CopySettings(_face, Vector2.zero, 0);
            _face.color = _source.color;
        }
    }

    private void CopySettings(TMP_Text copy, Vector2 offset, int sortingOffset)
    {
        copy.font = _source.font;
        copy.fontSharedMaterial = _source.fontSharedMaterial;
        copy.text = _source.text;
        copy.fontSize = _source.fontSize;
        copy.fontStyle = _source.fontStyle;
        copy.alignment = _source.alignment;
        copy.characterSpacing = _source.characterSpacing;
        copy.wordSpacing = _source.wordSpacing;
        copy.lineSpacing = _source.lineSpacing;
        copy.textWrappingMode = _source.textWrappingMode;
        copy.overflowMode = _source.overflowMode;
        copy.margin = _source.margin;

        if (copy is TextMeshPro copy3D && _source is TextMeshPro source3D)
        {
            copy3D.sortingLayerID = source3D.sortingLayerID;
            copy3D.sortingOrder = source3D.sortingOrder + sortingOffset;
        }

        // Stretched over the source's rect, so it follows layout groups and size changes on its own.
        RectTransform rect = copy.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = _source.rectTransform.pivot;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = offset;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
    }

    private void SetCopiesVisible(bool visible)
    {
        foreach (TMP_Text copy in AllCopies())
        {
            if (copy.TryGetComponent(out MeshRenderer meshRenderer)) meshRenderer.enabled = visible;
            else if (copy.TryGetComponent(out CanvasRenderer canvasRenderer)) canvasRenderer.cull = !visible;
        }
    }

    private IEnumerable<TMP_Text> AllCopies()
    {
        if (_copies != null)
        {
            foreach (TMP_Text copy in _copies)
            {
                if (copy != null) yield return copy;
            }
        }

        if (_face != null) yield return _face;
    }

    private static float NonZero(float value) => Mathf.Approximately(value, 0f) ? 1f : value;

    private static void DestroyObject(GameObject target)
    {
        if (Application.isPlaying)
        {
            Destroy(target);
            return;
        }

#if UNITY_EDITOR
        // Deferred: OnDestroy can run while the parent itself is being destroyed.
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (target != null) DestroyImmediate(target);
        };
#endif
    }
}
