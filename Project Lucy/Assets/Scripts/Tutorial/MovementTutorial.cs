using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The Cuphead-style movement prompts drawn in the Hub sky (built by Tools > Lucy > Build Movement Tutorial).
/// They fade in while the player is inside this trigger and fade out when the player leaves it, as many times
/// as the player comes and goes. They only exist during the first Hub visit of the session: once any other
/// scene has been loaded they are gone for good. No save system exists yet, so "first visit" lasts until the
/// game is closed (same situation as the boss progression and the upgrades).
/// </summary>
[RequireComponent(typeof(Collider2D))]
[DisallowMultipleComponent]
public class MovementTutorial : MonoBehaviour
{
    [Tooltip("Tudo o que aparece e some (textos, icones, setas). Fica visivel no Editor para ajustar o layout.")]
    [SerializeField] private GameObject _content;

    [SerializeField] private string _playerTag = "Player";

    [Header("Aparencia")]
    [Tooltip("Opacidade maxima do tutorial quando totalmente visivel (1 = como foi gerado, 0 = invisivel).")]
    [Range(0f, 1f)]
    [SerializeField] private float _maxOpacity = 1f;

    [Header("Transicao")]
    [Tooltip("Duracao do fade ao aparecer (tempo real: funciona com o jogo pausado).")]
    [SerializeField] private float _fadeInDuration = 0.35f;

    [Tooltip("Duracao do fade ao sumir.")]
    [SerializeField] private float _fadeOutDuration = 0.5f;

    // Static so the answer survives the Hub being unloaded; reset on every launch (no save system).
    private static bool _hasLeftHub;
    private static bool _watchingForExit;
    private static string _hubSceneName;

    private TMP_Text[] _texts;
    private SpriteRenderer[] _sprites;
    private LineRenderer[] _lines;
    private Color[] _textColors;
    private Color[] _spriteColors;
    private Color[] _lineStartColors;
    private Color[] _lineEndColors;

    private Coroutine _fadeRoutine;
    private float _visibility;
    private int _playerColliders;

    /// <summary>True once the player has gone from the Hub to another scene this session.</summary>
    public static bool HasLeftHub => _hasLeftHub;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        _watchingForExit = false;
        _hasLeftHub = false;
        _hubSceneName = null;
    }

    private void Awake()
    {
        // Later visits: gone for good. Done here, not in Start, so it never shows for a single frame.
        if (_hasLeftHub)
        {
            gameObject.SetActive(false);
            return;
        }

        if (_content == null || _content == gameObject)
        {
            Debug.LogWarning($"{name}: Content nao configurado; tutorial desativado.", this);
            gameObject.SetActive(false);
            return;
        }

        _hubSceneName = gameObject.scene.name;
        if (!_watchingForExit)
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            _watchingForExit = true;
        }

        // Deactivated before touching the colours: an inactive text does not queue a mesh rebuild.
        _content.SetActive(false);
        CacheColors();
        ApplyAlpha(0f);
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Fires for the Hub itself right after its Awake (same name, ignored). Additive scenes do not unload
        // the Hub, so only a Single load of a different scene counts as leaving.
        if (mode != LoadSceneMode.Single || scene.name == _hubSceneName) return;

        _hasLeftHub = true;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        _watchingForExit = false;
    }

    private void OnDisable()
    {
        _fadeRoutine = null;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(_playerTag)) return;

        _playerColliders++;
        if (_playerColliders == 1) StartFade(1f, _fadeInDuration);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag(_playerTag)) return;

        _playerColliders = Mathf.Max(0, _playerColliders - 1);
        if (_playerColliders == 0) StartFade(0f, _fadeOutDuration);
    }

    private void StartFade(float target, float duration)
    {
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(Fade(target, duration));
    }

    private IEnumerator Fade(float target, float duration)
    {
        if (target > 0f) _content.SetActive(true);

        // Continues from the current visibility, so walking back and forth reverses smoothly.
        while (!Mathf.Approximately(_visibility, target))
        {
            float step = duration > 0f ? Time.unscaledDeltaTime / duration : 1f;
            _visibility = Mathf.MoveTowards(_visibility, target, step);
            ApplyAlpha(Mathf.SmoothStep(0f, 1f, _visibility));
            yield return null;
        }

        ApplyAlpha(target);
        if (target <= 0f) _content.SetActive(false);
        _fadeRoutine = null;
    }

    private void CacheColors()
    {
        _texts = _content.GetComponentsInChildren<TMP_Text>(true);
        _sprites = _content.GetComponentsInChildren<SpriteRenderer>(true);
        _lines = _content.GetComponentsInChildren<LineRenderer>(true);

        _textColors = new Color[_texts.Length];
        for (int i = 0; i < _texts.Length; i++) _textColors[i] = _texts[i].color;

        _spriteColors = new Color[_sprites.Length];
        for (int i = 0; i < _sprites.Length; i++) _spriteColors[i] = _sprites[i].color;

        _lineStartColors = new Color[_lines.Length];
        _lineEndColors = new Color[_lines.Length];
        for (int i = 0; i < _lines.Length; i++)
        {
            _lineStartColors[i] = _lines[i].startColor;
            _lineEndColors[i] = _lines[i].endColor;
        }
    }

    private void ApplyAlpha(float alpha)
    {
        alpha *= _maxOpacity;
        for (int i = 0; i < _texts.Length; i++) _texts[i].color = WithAlpha(_textColors[i], alpha);
        for (int i = 0; i < _sprites.Length; i++) _sprites[i].color = WithAlpha(_spriteColors[i], alpha);
        for (int i = 0; i < _lines.Length; i++)
        {
            _lines[i].startColor = WithAlpha(_lineStartColors[i], alpha);
            _lines[i].endColor = WithAlpha(_lineEndColors[i], alpha);
        }
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a *= alpha;
        return color;
    }

#if UNITY_EDITOR
    [ContextMenu("Debug/Simular volta a Hub")]
    private void DebugSimulateReturn()
    {
        if (!Application.isPlaying) return;

        _hasLeftHub = true;
        SceneManager.LoadScene(gameObject.scene.name);
    }
#endif
}
