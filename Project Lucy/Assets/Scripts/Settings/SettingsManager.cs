using System;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Owns the player's settings: loads them at startup, applies them (volumes go to Main.mixer) and persists them.
/// Needs no UI: it is created before the first scene loads from Resources/SettingsManager, so the saved volume is
/// in effect from the first frame, whatever scene the game (or the Editor) starts in. Persistent like the other
/// managers. The Options menu only reads and writes through it.
/// </summary>
public class SettingsManager : MonoBehaviour
{
    public const string ResourcePath = "SettingsManager";

    // Default names of the parameters exposed in Main.mixer.
    public const string MasterParameter = "MasterVolume";
    public const string MusicParameter = "MusicVolume";
    public const string SfxParameter = "SFXVolume";

    public static SettingsManager instance;

    /// <summary>Fired after any setting changes (and after a load or reset), with the values already applied.</summary>
    public static event Action OnSettingsChanged;

    [Header("Audio")]
    [Tooltip("Main.mixer. Sem mixer as configuracoes ainda sao salvas, so nao sao aplicadas ao audio.")]
    [SerializeField] private AudioMixer _mixer;

    [Tooltip("Parametros expostos no mixer, um por slider.")]
    [SerializeField] private string _masterParameter = MasterParameter;
    [SerializeField] private string _musicParameter = MusicParameter;
    [SerializeField] private string _sfxParameter = SfxParameter;

    private GameSettings _settings = new();
    private ISettingsStore _store;
    private bool _initialized;
    private bool _dirty;
    private bool _warnedMissingParameter;

    public AudioMixer Mixer => _mixer;
    public bool HasUnsavedChanges => _dirty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Keeps Enter Play Mode without domain reload from seeing the last session's instance and listeners.
        instance = null;
        OnSettingsChanged = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null) return;

        GameObject prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab != null)
        {
            Instantiate(prefab).name = prefab.name;
            return;
        }

        Debug.LogWarning("Prefab Resources/SettingsManager nao encontrado: rode Tools > Lucy > Build Options Menu. " +
            "Configuracoes serao salvas mas o volume nao sera aplicado.");
        new GameObject(nameof(SettingsManager)).AddComponent<SettingsManager>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        if (!_initialized) Initialize(_mixer, new PlayerPrefsSettingsStore());
    }

    private void Start()
    {
        // AudioMixer.SetFloat can be overridden by the start snapshot when called during Awake; apply once more.
        if (instance == this) ApplyAll();
    }

    private void OnApplicationPause(bool paused)
    {
        // Mobile and WebGL can be killed while in the background without a quit message.
        if (paused) Save();
    }

    private void OnApplicationQuit()
    {
        Save();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    /// <summary>
    /// Loads from <paramref name="store"/> and applies to <paramref name="mixer"/>. Called by Awake with
    /// PlayerPrefs; call it yourself to use another store, or in Edit Mode tests where Awake does not run.
    /// </summary>
    public void Initialize(AudioMixer mixer, ISettingsStore store)
    {
        _mixer = mixer;
        _store = store;
        _initialized = true;
        Load();
    }

    #region Read

    /// <summary>A copy of the current values. Change settings through the setters, not on the copy.</summary>
    public GameSettings Current => _settings.Clone();

    public float GetVolume(VolumeChannel channel)
    {
        return _settings.GetVolume(channel);
    }

    #endregion

    #region Write

    /// <summary>Applies at once (call it every frame while dragging); persisted on Save, menu close or quit.</summary>
    public void SetVolume(VolumeChannel channel, float linear)
    {
        float previous = _settings.GetVolume(channel);
        _settings.SetVolume(channel, linear);
        if (Mathf.Approximately(previous, _settings.GetVolume(channel))) return;

        _dirty = true;
        ApplyVolume(channel);
        OnSettingsChanged?.Invoke();
    }

    /// <summary>Writes to the store if anything changed since the last save or load.</summary>
    public void Save()
    {
        if (!_dirty || _store == null) return;
        _store.Save(_settings);
        _dirty = false;
    }

    /// <summary>Replaces the current values with the stored ones (defaults when nothing is stored) and applies them.</summary>
    public void Load()
    {
        _settings = _store != null && _store.TryLoad(out GameSettings loaded) ? loaded : new GameSettings();
        _dirty = false;
        ApplyAll();
        OnSettingsChanged?.Invoke();
    }

    public void ResetToDefaults()
    {
        _settings = new GameSettings();
        _dirty = true;
        ApplyAll();
        OnSettingsChanged?.Invoke();
    }

    #endregion

    #region Apply

    public void ApplyAll()
    {
        ApplyVolume(VolumeChannel.Master);
        ApplyVolume(VolumeChannel.Music);
        ApplyVolume(VolumeChannel.Sfx);

        // PLACEHOLDER: resolution, fullscreen, key bindings and language get applied here once they exist.
    }

    private void ApplyVolume(VolumeChannel channel)
    {
        if (_mixer == null) return;

        string parameter = GetParameter(channel);
        if (_mixer.SetFloat(parameter, VolumeMath.LinearToDecibels(_settings.GetVolume(channel)))) return;

        if (_warnedMissingParameter) return;
        _warnedMissingParameter = true;
        Debug.LogWarning($"Parametro '{parameter}' nao esta exposto no mixer {_mixer.name}.", this);
    }

    public string GetParameter(VolumeChannel channel)
    {
        return channel switch
        {
            VolumeChannel.Music => _musicParameter,
            VolumeChannel.Sfx => _sfxParameter,
            _ => _masterParameter
        };
    }

    #endregion
}
