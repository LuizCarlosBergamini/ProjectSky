using System;
using HierarchicalStateMachine;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

/// <summary>
/// Boss fight music, played in three parts of one track:
/// Intro - loops while the player is in a boss level but has not entered the arena yet;
/// Fight - loops from the moment BossGate closes until the boss dies;
/// Outro - the track's last seconds, crossfaded in as soon as the boss takes its killing blow.
/// The Intro and Fight loops are contiguous in the file, so the switch waits for the end of the current Intro pass
/// and joins with no seam. Loops are scheduled on the audio clock (PlayScheduled), never in Update, so they are
/// sample accurate. Lives on the AudioManager prefab, persistent; BossGate drives it.
/// </summary>
public class BossMusic : MonoBehaviour
{
    [Serializable]
    public class Track
    {
        public AudioClip clip;
        [Tooltip("Fim da introducao (s). De 0 ate aqui toca em loop antes da luta.")]
        [Min(0f)] public float introEnd = 23f;
        [Tooltip("Fim do loop da luta (s). De Intro End ate aqui toca em loop durante a luta.")]
        [Min(0f)] public float fightEnd = 120f;
        [Tooltip("Duracao do final (s): os ultimos segundos da faixa, tocados quando o chefe morre.")]
        [Min(0f)] public float outroLength = 8f;
    }

    private enum Part { None, Intro, Fight, Outro }

    public static BossMusic instance;

    [Tooltip("Grupo Music do Main.mixer, controlado pelo slider de Musica das Opcoes.")]
    [SerializeField] private AudioMixerGroup _musicGroup;
    [SerializeField] private Track _track = new Track();
    [SerializeField, Range(0f, 1f)] private float _volume = 0.7f;

    [Tooltip("Tempo (s) da transicao suave entre o loop da luta e o final.")]
    [SerializeField, Min(0.05f)] private float _outroCrossfade = 1.5f;
    [Tooltip("Tempo (s) para a musica sumir ao sair da fase ou recarregar a cena.")]
    [SerializeField, Min(0.05f)] private float _stopFade = 1f;
    [Tooltip("Tempo (s) para a introducao aparecer quando a fase comeca.")]
    [SerializeField, Min(0f)] private float _introFadeIn = 0.5f;

    // How long before a loop pass ends the next one is queued on the audio clock.
    private const double ScheduleAhead = 1.0;
    private const double StartDelay = 0.1;

    // Two sources take turns for the looping parts (one plays while the other waits for its scheduled start);
    // the third plays the outro while the loop fades out under it.
    private AudioSource[] _loopSources;
    private AudioSource _outroSource;
    private int _nextLoopSource;

    private Part _part = Part.None;
    private Part _loopPart = Part.None; // what the next scheduled pass plays
    private double _nextPassDsp;        // audio-clock time the scheduled pass ends
    private EnemyStateDriver _boss;

    private float _loopGain;            // fade multiplier on the loop sources
    private float _loopGainTarget;
    private float _loopFadeSpeed;
    private float _outroGain;
    private float _outroFadeSpeed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        _loopSources = new[] { CreateSource(), CreateSource() };
        _outroSource = CreateSource();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private AudioSource CreateSource()
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.outputAudioMixerGroup = _musicGroup;
        source.volume = 0f;
        return source;
    }

    /// <summary>Starts the pre-fight loop. Called by BossGate when a level with a living boss starts.</summary>
    public static void PlayIntro()
    {
        if (instance != null) instance.StartIntro();
    }

    /// <summary>Moves to the fight loop at the end of the current intro pass. Called when the arena closes.</summary>
    public static void StartFight(EnemyStateDriver boss)
    {
        if (instance != null) instance.BeginFight(boss);
    }

    private bool HasTrack => _track != null && _track.clip != null
                             && _track.introEnd > 0f && _track.fightEnd > _track.introEnd;

    private void StartIntro()
    {
        if (!HasTrack) return;

        StopAllImmediately();
        _part = Part.Intro;
        _loopPart = Part.Intro;
        FadeLoop(0f, 1f, _introFadeIn);
        SchedulePass(AudioSettings.dspTime + StartDelay);
    }

    private void BeginFight(EnemyStateDriver boss)
    {
        if (!HasTrack) return;

        _boss = boss;
        if (_part == Part.Intro)
        {
            // The pass already playing finishes the intro; the next one is the fight, right where it ends.
            _part = Part.Fight;
            _loopPart = Part.Fight;
            return;
        }

        // No intro running (e.g. it was faded out): start the fight loop on its own.
        StopAllImmediately();
        _part = Part.Fight;
        _loopPart = Part.Fight;
        FadeLoop(0f, 1f, _introFadeIn);
        SchedulePass(AudioSettings.dspTime + StartDelay);
    }

    private void Update()
    {
        if (_part == Part.Intro || _part == Part.Fight)
        {
            if (AudioSettings.dspTime > _nextPassDsp - ScheduleAhead) SchedulePass(_nextPassDsp);

            // The killing blow, not the end of the death animation, starts the outro.
            if (_part == Part.Fight && _boss != null && !_boss.IsAlive()) BeginOutro();
        }

        UpdateFades(Time.unscaledDeltaTime);
    }

    // Queues one pass of _loopPart on the idle loop source, starting exactly when the previous pass ends.
    private void SchedulePass(double startDsp)
    {
        float from = _loopPart == Part.Intro ? 0f : _track.introEnd;
        float to = _loopPart == Part.Intro ? _track.introEnd : _track.fightEnd;

        AudioSource source = _loopSources[_nextLoopSource];
        _nextLoopSource = 1 - _nextLoopSource;

        source.clip = _track.clip;
        source.timeSamples = Mathf.Clamp(Mathf.RoundToInt(from * _track.clip.frequency), 0, _track.clip.samples - 1);
        source.PlayScheduled(startDsp);
        source.SetScheduledEndTime(startDsp + (to - from));

        _nextPassDsp = startDsp + (to - from);
    }

    private void BeginOutro()
    {
        _part = Part.Outro;
        _boss = null;

        // No more passes are queued. The loop fades out under the outro; a pass already queued may still start
        // during the fade, which keeps the music continuous until it is silent.
        FadeLoop(_loopGain, 0f, _outroCrossfade);

        float outroStart = Mathf.Max(0f, _track.clip.length - _track.outroLength);
        _outroSource.clip = _track.clip;
        _outroSource.timeSamples = Mathf.Clamp(Mathf.RoundToInt(outroStart * _track.clip.frequency), 0, _track.clip.samples - 1);
        _outroGain = 0f;
        _outroFadeSpeed = 1f / _outroCrossfade;
        _outroSource.volume = 0f;
        _outroSource.Play();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single || _part == Part.None) return;

        // Leaving the level, or a player death reloading it. A boss level's gate restarts the intro in Start.
        _part = Part.None;
        _loopPart = Part.None;
        _boss = null;
        FadeLoop(_loopGain, 0f, _stopFade);
        _outroFadeSpeed = -1f / _stopFade;
    }

    private void FadeLoop(float from, float to, float duration)
    {
        _loopGain = from;
        _loopGainTarget = to;
        _loopFadeSpeed = duration > 0f ? Mathf.Abs(to - from) / duration : float.PositiveInfinity;
    }

    private void UpdateFades(float deltaTime)
    {
        _loopGain = Mathf.MoveTowards(_loopGain, _loopGainTarget, _loopFadeSpeed * deltaTime);
        foreach (AudioSource source in _loopSources)
        {
            source.volume = _volume * _loopGain;
            if (_loopGain <= 0f && _loopGainTarget <= 0f && source.isPlaying) source.Stop();
        }

        if (_outroSource.isPlaying)
        {
            _outroGain = Mathf.Clamp01(_outroGain + _outroFadeSpeed * deltaTime);
            _outroSource.volume = _volume * _outroGain;
            if (_outroGain <= 0f) _outroSource.Stop();
        }
        else if (_part == Part.Outro)
        {
            _part = Part.None; // the track played to its end
        }
    }

    private void StopAllImmediately()
    {
        foreach (AudioSource source in _loopSources) source.Stop();
        _outroSource.Stop();
        _outroGain = 0f;
        _loopGain = 0f;
        _boss = null;
    }
}
