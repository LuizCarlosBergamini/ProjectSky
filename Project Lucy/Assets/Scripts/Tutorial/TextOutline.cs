using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// A solid outline for a world-space TextMeshPro: eight copies of the text in the outline colour, pushed out by
/// the thickness in every direction and drawn one sorting order behind it. TMP's own outline (Outline in the
/// material) cannot be thicker than the font atlas padding allows, which for the Jersey 10 asset is about a
/// quarter of a font pixel; the copies have no such limit and keep the square corners of the pixel font.
/// The copies follow the text, font, size, layout and alpha of the source (so MovementTutorial's fade reaches
/// them), are hidden in the Hierarchy and are never saved with the scene.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(TextMeshPro))]
public class TextOutline : MonoBehaviour
{
    private const string CopyName = "TextOutline Copy";

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

    private TextMeshPro _source;
    private TextMeshPro[] _copies;
    private bool _dirty;

    private void OnEnable()
    {
        _source = GetComponent<TextMeshPro>();
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
        if (_copies == null) return;

        foreach (TextMeshPro copy in _copies)
        {
            if (copy != null) DestroyObject(copy.gameObject);
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

        // After a script reload the copies are still in the scene but this array is not.
        List<TextMeshPro> found = new();
        foreach (Transform child in transform)
        {
            if (child.name == CopyName && child.TryGetComponent(out TextMeshPro copy)) found.Add(copy);
        }

        if (found.Count == Directions.Length)
        {
            _copies = found.ToArray();
            return;
        }

        foreach (TextMeshPro copy in found)
        {
            DestroyObject(copy.gameObject);
        }

        _copies = new TextMeshPro[Directions.Length];
        for (int i = 0; i < Directions.Length; i++)
        {
            GameObject go = new(CopyName, typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);

            TextMeshPro copy = go.AddComponent<TextMeshPro>();
            copy.raycastTarget = false;
            copy.overrideColorTags = true;
            _copies[i] = copy;
        }
    }

    private void Sync()
    {
        if (_source == null || _copies == null) return;

        Color color = _color;
        color.a *= _source.color.a;
        RectTransform sourceRect = _source.rectTransform;

        for (int i = 0; i < _copies.Length; i++)
        {
            TextMeshPro copy = _copies[i];
            if (copy == null) continue;

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
            copy.color = color;
            copy.sortingLayerID = _source.sortingLayerID;
            copy.sortingOrder = _source.sortingOrder - 1;

            RectTransform rect = copy.rectTransform;
            rect.pivot = sourceRect.pivot;
            rect.sizeDelta = sourceRect.sizeDelta;
            rect.localPosition = Directions[i] * _thickness;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
        }
    }

    private void SetCopiesVisible(bool visible)
    {
        if (_copies == null) return;

        foreach (TextMeshPro copy in _copies)
        {
            if (copy != null && copy.TryGetComponent(out MeshRenderer meshRenderer)) meshRenderer.enabled = visible;
        }
    }

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
