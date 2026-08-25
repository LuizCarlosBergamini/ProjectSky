using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Screen-edge arrow that points at the objective of the active task.
/// Lives on the persistent task canvas, so it keeps working across scene loads.
/// Hides itself while the objective is already visible on screen.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class QuestArrowIndicator : MonoBehaviour
{
    [Tooltip("Imagem da seta. Vazio = o proprio RectTransform deste objeto.")]
    [SerializeField] private RectTransform _arrow;

    [Tooltip("Distancia em pixels entre a seta e a borda da tela.")]
    [SerializeField] private float _edgePadding = 64f;

    [Tooltip("Margem em pixels para considerar o objetivo 'visivel' na tela.")]
    [SerializeField] private float _onScreenMargin = 0f;

    [Tooltip("Correcao em graus caso a arte da seta nao aponte para a direita.")]
    [SerializeField] private float _spriteAngleOffset = 0f;

    private RectTransform _parentRect;
    private Canvas _canvas;
    private Camera _worldCamera;
    private CanvasGroup _arrowGroup;

    private string _objectiveId;
    private QuestObjective _objective;

    private void Awake()
    {
        if (_arrow == null) _arrow = (RectTransform)transform;
        _parentRect = _arrow.parent as RectTransform;
        _canvas = GetComponentInParent<Canvas>();

        // Fading instead of deactivating: the arrow is often this very object,
        // and deactivating it would stop LateUpdate from ever running again.
        if (!_arrow.TryGetComponent(out _arrowGroup))
        {
            _arrowGroup = _arrow.gameObject.AddComponent<CanvasGroup>();
        }
    }

    private void OnEnable()
    {
        TaskManager.OnTaskStateChanged += InvalidateObjective;
        SceneManager.sceneLoaded += OnSceneLoaded;
        InvalidateObjective();
    }

    private void OnDisable()
    {
        TaskManager.OnTaskStateChanged -= InvalidateObjective;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _worldCamera = null;
        InvalidateObjective();
    }

    private void InvalidateObjective()
    {
        _objectiveId = null;
        _objective = null;
    }

    private void LateUpdate()
    {
        QuestObjective objective = ResolveObjective();
        Camera cam = ResolveCamera();

        if (objective == null || cam == null || _parentRect == null)
        {
            SetArrowVisible(false);
            return;
        }

        Vector3 screenPoint = cam.WorldToScreenPoint(objective.Anchor.position);
        bool behindCamera = screenPoint.z < 0f;
        if (behindCamera)
        {
            // Points behind the camera mirror through the centre, flip them back.
            screenPoint.x = Screen.width - screenPoint.x;
            screenPoint.y = Screen.height - screenPoint.y;
        }

        if (!behindCamera && IsOnScreen(screenPoint))
        {
            SetArrowVisible(false);
            return;
        }

        Vector2 screenCentre = new(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 direction = (Vector2)screenPoint - screenCentre;
        if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;

        Vector2 edgePoint = screenCentre + ClampToEdge(direction);

        SetArrowVisible(true);

        Camera uiCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? _canvas.worldCamera
            : null;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_parentRect, edgePoint, uiCamera, out Vector2 localPoint))
        {
            _arrow.anchoredPosition = localPoint;
        }

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        _arrow.localRotation = Quaternion.Euler(0f, 0f, angle + _spriteAngleOffset);
    }

    private bool IsOnScreen(Vector3 screenPoint)
    {
        return screenPoint.x >= _onScreenMargin
            && screenPoint.x <= Screen.width - _onScreenMargin
            && screenPoint.y >= _onScreenMargin
            && screenPoint.y <= Screen.height - _onScreenMargin;
    }

    /// <summary>Walks <paramref name="direction"/> out from the screen centre until it hits the padded border.</summary>
    private Vector2 ClampToEdge(Vector2 direction)
    {
        float halfWidth = Mathf.Max(1f, Screen.width * 0.5f - _edgePadding);
        float halfHeight = Mathf.Max(1f, Screen.height * 0.5f - _edgePadding);

        float scaleX = Mathf.Abs(direction.x) > 0.0001f ? halfWidth / Mathf.Abs(direction.x) : float.MaxValue;
        float scaleY = Mathf.Abs(direction.y) > 0.0001f ? halfHeight / Mathf.Abs(direction.y) : float.MaxValue;

        return direction * Mathf.Min(scaleX, scaleY);
    }

    private QuestObjective ResolveObjective()
    {
        string activeId = TaskManager.instance != null ? TaskManager.instance.GetActiveObjectiveId() : null;

        if (activeId != _objectiveId)
        {
            _objectiveId = activeId;
            _objective = null;
        }

        // The objective may only spawn once its scene finishes loading, so keep trying.
        if (_objective == null && !string.IsNullOrWhiteSpace(_objectiveId))
        {
            _objective = QuestObjective.Get(_objectiveId);
        }

        return _objective;
    }

    private Camera ResolveCamera()
    {
        if (_worldCamera == null) _worldCamera = Camera.main;
        return _worldCamera;
    }

    private void SetArrowVisible(bool visible)
    {
        if (_arrowGroup == null) return;
        _arrowGroup.alpha = visible ? 1f : 0f;
    }
}
