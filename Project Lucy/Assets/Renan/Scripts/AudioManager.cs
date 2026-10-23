using System;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>Every gameplay sound the AudioManager knows by name. The clip for each one is set on the AudioManager prefab.</summary>
public enum SoundEffect
{
    PlayerWalk,
    EnemyWalk,
    Jump,
    Hit,
    Grapple,
}

/// <summary>
/// Plays the game's sound effects. Gameplay code only says which sound it wants (AudioManager.Play(SoundEffect.Jump));
/// the clip, its volume and its pitch variation live on Resources/AudioManager, so a sound can be swapped without
/// touching code. Created before the first scene loads, like SettingsManager, so every level has it no matter which
/// scene the game (or the Editor) starts in. Persistent; a copy found in a scene removes itself.
/// All sounds go out through the SFX group of Main.mixer, which is what the Options menu's SFX slider controls.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public const string ResourcePath = "AudioManager";

    [Serializable]
    public class Sound
    {
        public SoundEffect id;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 0.8f;
        [Tooltip("Cada vez que toca, o pitch e sorteado entre estes dois valores, para o som nao ficar repetitivo.")]
        public Vector2 pitchRange = new Vector2(0.9f, 1.1f);
    }

    public static AudioManager instance;

    [Tooltip("Grupo SFX do Main.mixer. Vazio = os sons saem direto, sem passar pelo volume das Opcoes.")]
    [SerializeField] private AudioMixerGroup _sfxGroup;

    [SerializeField] private Sound[] _sounds = Array.Empty<Sound>();

    [Tooltip("Quantos sons podem tocar ao mesmo tempo. Cada um tem o seu proprio pitch.")]
    [SerializeField, Min(1)] private int _voices = 8;

    [Header("Sons posicionados (inimigos)")]
    [Tooltip("Ate esta distancia da camera o som toca no volume cheio.")]
    [SerializeField, Min(0f)] private float _fullVolumeDistance = 6f;
    [Tooltip("A partir desta distancia da camera o som nao toca.")]
    [SerializeField, Min(0f)] private float _silentDistance = 18f;

    private AudioSource[] _sources;
    private int _nextSource;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Keeps Enter Play Mode without domain reload from seeing the last session's instance.
        instance = null;
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

        Debug.LogWarning("Prefab Resources/AudioManager nao encontrado: os efeitos sonoros nao vao tocar.");
        new GameObject(nameof(AudioManager)).AddComponent<AudioManager>();
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
        CreateSources();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    // One source per voice: pitch is a property of the source, so sounds sharing one would all
    // change pitch together.
    private void CreateSources()
    {
        _sources = new AudioSource[_voices];
        for (int i = 0; i < _voices; i++)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f; // 2D: distance is handled by PlayAt, the camera is far from the action in z
            source.outputAudioMixerGroup = _sfxGroup;
            _sources[i] = source;
        }
    }

    /// <summary>Plays a sound at full volume, wherever it happens. For the player's own sounds.</summary>
    public static void Play(SoundEffect id)
    {
        if (instance != null) instance.PlayInternal(id, 1f);
    }

    /// <summary>Plays a sound that comes from a place in the level (an enemy): quieter the further it is from the camera.</summary>
    public static void PlayAt(SoundEffect id, Vector3 position)
    {
        if (instance == null) return;

        float attenuation = instance.Attenuation(position);
        if (attenuation > 0f) instance.PlayInternal(id, attenuation);
    }

    /// <summary>Plays any clip with a small random pitch and volume. Kept for sounds set on the object itself (Collectable).</summary>
    public void PlayWithVariation(AudioClip clip)
    {
        if (clip == null) return;
        PlayClip(clip, UnityEngine.Random.Range(0.3f, 0.4f), UnityEngine.Random.Range(0.8f, 1.2f));
    }

    private void PlayInternal(SoundEffect id, float volumeScale)
    {
        Sound sound = Find(id);
        if (sound == null || sound.clip == null) return;

        float pitch = UnityEngine.Random.Range(sound.pitchRange.x, sound.pitchRange.y);
        PlayClip(sound.clip, sound.volume * volumeScale, pitch);
    }

    private void PlayClip(AudioClip clip, float volume, float pitch)
    {
        AudioSource source = NextSource();
        source.pitch = pitch;
        source.PlayOneShot(clip, volume);
    }

    // Prefers a free voice; when all are busy the oldest one is reused.
    private AudioSource NextSource()
    {
        for (int i = 0; i < _sources.Length; i++)
        {
            int index = (_nextSource + i) % _sources.Length;
            if (_sources[index].isPlaying) continue;

            _nextSource = (index + 1) % _sources.Length;
            return _sources[index];
        }

        AudioSource oldest = _sources[_nextSource];
        oldest.Stop();
        _nextSource = (_nextSource + 1) % _sources.Length;
        return oldest;
    }

    private Sound Find(SoundEffect id)
    {
        for (int i = 0; i < _sounds.Length; i++)
        {
            if (_sounds[i] != null && _sounds[i].id == id) return _sounds[i];
        }

        return null;
    }

    // 1 up to _fullVolumeDistance from the camera, fading to 0 at _silentDistance. Measured in x/y only.
    private float Attenuation(Vector3 position)
    {
        Camera cam = Camera.main;
        if (cam == null) return 1f;

        float distance = Vector2.Distance(cam.transform.position, position);
        if (_silentDistance <= _fullVolumeDistance) return distance <= _fullVolumeDistance ? 1f : 0f;
        return 1f - Mathf.Clamp01((distance - _fullVolumeDistance) / (_silentDistance - _fullVolumeDistance));
    }
}

/// <summary>
/// Footstep rhythm for anything that walks: call Tick every frame with whether it is walking, and play a step when it
/// returns true. The first step sounds as soon as the walk starts; stopping resets the rhythm.
/// </summary>
[Serializable]
public class FootstepTimer
{
    [Tooltip("Segundos entre um passo e outro, andando na velocidade normal.")]
    [Min(0.05f)] public float interval = 0.35f;

    private float timer;

    /// <param name="speedMultiplier">Above 1 the steps come faster (running).</param>
    public bool Tick(bool walking, float deltaTime, float speedMultiplier = 1f)
    {
        if (!walking)
        {
            timer = 0f;
            return false;
        }

        timer -= deltaTime * Mathf.Max(0.01f, speedMultiplier);
        if (timer > 0f) return false;

        timer += interval;
        if (timer <= 0f) timer = interval; // a long frame must not queue several steps
        return true;
    }
}
