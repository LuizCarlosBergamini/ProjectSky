using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A solid outline for a SpriteRenderer, the sprite counterpart of <see cref="TextOutline"/>: eight copies of the
/// sprite in the outline colour, pushed out by the thickness in every direction and drawn behind it. Used on the
/// key and mouse icons of the movement tutorial so they read against the sky like the texts.
/// The copies follow the sprite, size, flip and alpha of the source (so MovementTutorial's fade reaches them),
/// are hidden in the Hierarchy and are never saved with the scene.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteOutline : MonoBehaviour
{
    private const string CopyName = "SpriteOutline Copy";

    // Full diagonals, not normalised: the corners come out square like the pixel art.
    private static readonly Vector2[] Directions =
    {
        new(1f, 0f), new(1f, 1f), new(0f, 1f), new(-1f, 1f),
        new(-1f, 0f), new(-1f, -1f), new(0f, -1f), new(1f, -1f)
    };

    [SerializeField] private Color _color = Color.black;

    [Tooltip("Espessura do contorno em unidades do mundo (a Hub tem 32 pixels por unidade).")]
    [Min(0f)]
    [SerializeField] private float _thickness = 0.05f;

    [Tooltip("Ordem das copias em relacao ao sprite. Negativo desenha atras; use o mesmo valor final em sprites " +
             "empilhados (ex.: sombra e tecla) para o contorno envolver os dois.")]
    [SerializeField] private int _sortingOrderOffset = -1;

    private SpriteRenderer _source;
    private SpriteRenderer[] _copies;
    private bool _dirty;

    // What the copies were last synced to; sprites have no change event, so LateUpdate compares.
    private Sprite _lastSprite;
    private Color _lastColor;
    private Vector2 _lastSize;
    private bool _lastFlipX;
    private bool _lastFlipY;
    private int _lastOrder;

    private void OnEnable()
    {
        _source = GetComponent<SpriteRenderer>();
        CollectCopies();
        SetCopiesVisible(true);
        Sync();
    }

    // The copies are kept, not destroyed: MovementTutorial may have cached them, and it switches the tutorial
    // off and on.
    private void OnDisable()
    {
        SetCopiesVisible(false);
    }

    // Covers this component being removed; when the whole object goes, the copies go with it.
    private void OnDestroy()
    {
        if (_copies == null) return;

        foreach (SpriteRenderer copy in _copies)
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
        if (_source == null) return;

        bool changed = _source.sprite != _lastSprite || _source.color != _lastColor || _source.size != _lastSize ||
                       _source.flipX != _lastFlipX || _source.flipY != _lastFlipY ||
                       _source.sortingOrder != _lastOrder;
        if (!_dirty && !changed) return;

        _dirty = false;
        Sync();
    }

    private void CollectCopies()
    {
        if (_copies != null) return;

        // After a script reload the copies are still in the scene but this array is not.
        List<SpriteRenderer> found = new();
        foreach (Transform child in transform)
        {
            if (child.name == CopyName && child.TryGetComponent(out SpriteRenderer copy)) found.Add(copy);
        }

        if (found.Count == Directions.Length)
        {
            _copies = found.ToArray();
            return;
        }

        foreach (SpriteRenderer copy in found)
        {
            DestroyObject(copy.gameObject);
        }

        _copies = new SpriteRenderer[Directions.Length];
        for (int i = 0; i < Directions.Length; i++)
        {
            GameObject go = new(CopyName) { hideFlags = HideFlags.HideAndDontSave };
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            _copies[i] = go.AddComponent<SpriteRenderer>();
        }
    }

    private void Sync()
    {
        if (_source == null || _copies == null) return;

        _lastSprite = _source.sprite;
        _lastColor = _source.color;
        _lastSize = _source.size;
        _lastFlipX = _source.flipX;
        _lastFlipY = _source.flipY;
        _lastOrder = _source.sortingOrder;

        Color color = _color;
        color.a *= _source.color.a;

        Vector3 scale = transform.lossyScale;
        Vector2 step = new(_thickness / NonZero(scale.x), _thickness / NonZero(scale.y));

        for (int i = 0; i < _copies.Length; i++)
        {
            SpriteRenderer copy = _copies[i];
            if (copy == null) continue;

            copy.sprite = _source.sprite;
            copy.sharedMaterial = _source.sharedMaterial;
            copy.drawMode = _source.drawMode;
            if (_source.drawMode != SpriteDrawMode.Simple) copy.size = _source.size;
            copy.flipX = _source.flipX;
            copy.flipY = _source.flipY;
            copy.color = color;
            copy.sortingLayerID = _source.sortingLayerID;
            copy.sortingOrder = _source.sortingOrder + _sortingOrderOffset;

            Transform copyTransform = copy.transform;
            copyTransform.localPosition = Vector2.Scale(Directions[i], step);
            copyTransform.localRotation = Quaternion.identity;
            copyTransform.localScale = Vector3.one;
        }
    }

    private void SetCopiesVisible(bool visible)
    {
        if (_copies == null) return;

        foreach (SpriteRenderer copy in _copies)
        {
            if (copy != null) copy.enabled = visible;
        }
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
