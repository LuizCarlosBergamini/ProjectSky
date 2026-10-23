using UnityEngine;

/// <summary>
/// A hand-drawn-style arrow for the movement tutorial: a curved shaft (quadratic Bezier through the Start,
/// Control and End child transforms, thinner at the tail like a pen stroke) plus an open V head. Drag the three
/// points in the Scene view to reshape it. The lines are rebuilt in the Editor only and saved with the scene,
/// so nothing runs in a build. It never touches colours or active state: MovementTutorial owns those at runtime.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class TutorialArrow : MonoBehaviour
{
    [Header("Pontos (arraste no Scene view)")]
    [SerializeField] private Transform _start;
    [SerializeField] private Transform _control;
    [SerializeField] private Transform _end;

    [Header("Linhas")]
    [SerializeField] private LineRenderer _shaft;
    [SerializeField] private LineRenderer _head;

    [Header("Traco")]
    [Range(4, 64)]
    [SerializeField] private int _segments = 24;

    [SerializeField] private float _width = 0.075f;

    [Tooltip("Largura da cauda em relacao ao resto do traco (menor que 1 afina como um traco a mao).")]
    [Range(0.05f, 1f)]
    [SerializeField] private float _tailWidth = 0.35f;

    [SerializeField] private float _headLength = 0.32f;

    [Range(10f, 80f)]
    [SerializeField] private float _headAngle = 35f;

    private Vector3[] _points;

    // Last inputs, so an idle Editor does not rewrite the lines (and dirty the scene) on every update.
    private Vector3 _lastStart;
    private Vector3 _lastControl;
    private Vector3 _lastEnd;
    private bool _built;

    /// <summary>Rewrites the shaft and head from the three points.</summary>
    public void Rebuild()
    {
        if (_start == null || _control == null || _end == null || _shaft == null || _head == null) return;

        Vector3 p0 = Flat(_start.position);
        Vector3 p1 = Flat(_control.position);
        Vector3 p2 = Flat(_end.position);

        int count = Mathf.Max(2, _segments) + 1;
        if (_points == null || _points.Length != count) _points = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float u = 1f - t;
            Vector3 world = u * u * p0 + 2f * u * t * p1 + t * t * p2;
            _points[i] = _shaft.transform.InverseTransformPoint(world);
        }

        _shaft.useWorldSpace = false;
        _shaft.positionCount = count;
        _shaft.SetPositions(_points);
        _shaft.widthMultiplier = _width;
        _shaft.widthCurve = new AnimationCurve(new Keyframe(0f, _tailWidth), new Keyframe(0.3f, 1f), new Keyframe(1f, 1f));

        // The head points along the last stretch of the curve.
        Vector3 direction = p2 - p1;
        if (direction.sqrMagnitude < 0.0001f) direction = p2 - p0;
        if (direction.sqrMagnitude < 0.0001f) direction = Vector3.right;
        Vector3 back = -direction.normalized * _headLength;

        Vector3 wingA = p2 + Quaternion.Euler(0f, 0f, _headAngle) * back;
        Vector3 wingB = p2 + Quaternion.Euler(0f, 0f, -_headAngle) * back;

        _head.useWorldSpace = false;
        _head.positionCount = 3;
        _head.SetPosition(0, _head.transform.InverseTransformPoint(wingA));
        _head.SetPosition(1, _head.transform.InverseTransformPoint(p2));
        _head.SetPosition(2, _head.transform.InverseTransformPoint(wingB));
        _head.widthMultiplier = _width;
        _head.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);

        _lastStart = p0;
        _lastControl = p1;
        _lastEnd = p2;
        _built = true;
    }

    private static Vector3 Flat(Vector3 position)
    {
        position.z = 0f;
        return position;
    }

#if UNITY_EDITOR
    private void Update()
    {
        if (Application.isPlaying) return;
        if (_start == null || _control == null || _end == null) return;

        if (_built
            && Flat(_start.position) == _lastStart
            && Flat(_control.position) == _lastControl
            && Flat(_end.position) == _lastEnd)
        {
            return;
        }

        Rebuild();
    }

    private void OnDrawGizmosSelected()
    {
        if (_start == null || _control == null || _end == null) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(_start.position, _control.position);
        Gizmos.DrawLine(_control.position, _end.position);
        Gizmos.DrawWireSphere(_start.position, 0.08f);
        Gizmos.DrawWireSphere(_control.position, 0.08f);
        Gizmos.DrawWireSphere(_end.position, 0.08f);
    }
#endif
}
