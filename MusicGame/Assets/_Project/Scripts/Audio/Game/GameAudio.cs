using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// The game's sound effects and voice playback (persistent, on [App Bootstrap]).
///   Channels — Music / Sfx / Voice, each its own mixer group with an independent volume (PlayerPrefs
///     "MusicGame.Volume.{channel}", 0..1). Music sources are routed with RouteMusic.
///   Cues — SfxCue: variants without immediate repeats, pitch / volume spread, maxInstances + cooldown.
///   Preload — every library clip's data is loaded at boot (IsReady); the Runner barrier and the fight wait for it.
///   Scopes — every sound is tagged UI / Runner / Fight; leaving that part of the game (or a new match starting)
///     stops its sounds, and a cue requested for a scope that is no longer current is refused — so nothing plays
///     late after a screen change or a cancellation.
///   Voice — one dialogue line at a time (PlayVoice / StopVoice with a token); efforts use the pool on Voice.
///   Announcer — ONE of the two announcer sets is picked at random when the app starts and kept for the whole session
///     (scenes, runs, fights); its clips are preloaded. PlayAnnouncer can schedule a word so its attack (measured onset)
///     lands exactly on a given moment — the song's start / the FIGHT! banner — without touching any clock.
/// Sound effects stay short and moderate so they never mask the song's own musical hits.
/// </summary>
public class GameAudio : MonoBehaviour
{
    public static GameAudio Instance { get; private set; }

    private const int PoolSize = 16;
    private const string PrefPrefix = "MusicGame.Volume.";
    private static readonly string[] ExposedNames = { "MusicVolume", "SfxVolume", "VoiceVolume" };

    public GameSfxLibrary Library { get; private set; }

    /// <summary>Diagnostics / tests: a cue actually started (cue, clip) and a dialogue voice line actually started.</summary>
    public static event System.Action<SfxCue, AudioClip> CuePlayed;
    public static event System.Action<AudioClip> VoicePlayed;

    private readonly List<AudioSource> _pool = new();
    private readonly Dictionary<AudioSource, (SfxCue cue, GameAudioScope scope, double startDsp)> _playing = new();
    private readonly Dictionary<SfxCue, float> _lastStart = new();
    private readonly Dictionary<SfxCue, int> _lastClip = new();
    private readonly float[] _volume = { 1f, 1f, 1f };
    private AudioSource _voice;
    private readonly AudioSource[] _announcer = new AudioSource[2]; // alternate: a word never cuts the previous word's tail
    private readonly double[] _announcerStart = new double[2];
    private readonly double[] _announcerHeld = { -1, -1 };          // while paused: delay left of a scheduled word, -2 = mid-word
    private int _announcerNext;
    private GameAudioScope _announcerScope;
    private readonly Dictionary<AudioClip, float> _onsets = new();

    /// <summary>The session's announcer (null when no complete set exists). Picked once at boot.</summary>
    public AnnouncerSet Announcer { get; private set; }

    /// <summary>The session announcer's countdown number n (3, 2, 1) in the game language (English when it has no
    /// recording in that language).</summary>
    public AudioClip AnnouncerNumber(int n)
    {
        if (Announcer == null) return null;
        var init = UnityEngine.Localization.Settings.LocalizationSettings.InitializationOperation;
        var locale = init.IsValid() && init.IsDone ? UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale : null;
        return Announcer.Number(n, locale != null ? locale.Identifier.Code : "en");
    }
    private int _voiceToken;
    private float _voiceFadeSpeed;
    private readonly HashSet<GameAudioScope> _closedScopes = new();

    private System.Action<GameFlowStateChangedEvent> _onFlow;
    private System.Action<FightFlowStateChangedEvent> _onFightFlow;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        Library = Resources.Load<GameSfxLibrary>(GameSfxLibrary.ResourcePath);
        if (Library == null) Debug.LogWarning("[GameAudio] No GameSfxLibrary in Resources — the game runs without sound effects.");
        for (int i = 0; i < PoolSize; i++) _pool.Add(NewSource($"Sfx{i}"));
        _voice = NewSource("Voice");
        _announcer[0] = NewSource("Announcer0");
        _announcer[1] = NewSource("Announcer1");
        PickAnnouncer();
        for (int c = 0; c < 3; c++) _volume[c] = PlayerPrefs.GetFloat(PrefPrefix + (GameAudioChannel)c, 1f);
        Preload();
    }

    private void Start() { for (int c = 0; c < 3; c++) ApplyVolume((GameAudioChannel)c); } // mixer params apply after Awake

    private void OnEnable()
    {
        _onFlow = e =>
        {
            if (e.Previous == GameFlowState.Gameplay && e.Current != GameFlowState.Gameplay) StopScope(GameAudioScope.Runner);
            if (e.Previous == GameFlowState.Fight && e.Current != GameFlowState.Fight) StopScope(GameAudioScope.Fight);
            _closedScopes.Clear();
            if (e.Current != GameFlowState.Gameplay) _closedScopes.Add(GameAudioScope.Runner);
            if (e.Current != GameFlowState.Fight) _closedScopes.Add(GameAudioScope.Fight);
        };
        // A new match (fight again / next stage) never inherits the previous one's sounds.
        _onFightFlow = e => { if (e.Current == FightFlowState.OpponentSelection || e.Current == FightFlowState.VersusIntro) StopScope(GameAudioScope.Fight, keepVoice: true); };
        EventBus.Subscribe(_onFlow);
        EventBus.Subscribe(_onFightFlow);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFlow);
        EventBus.Unsubscribe(_onFightFlow);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private AudioSource NewSource(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        return s;
    }

    private void PickAnnouncer()
    {
        var sets = Library != null && Library.announcers != null ? System.Array.FindAll(Library.announcers, a => a != null && a.IsComplete && a.inRotation) : new AnnouncerSet[0];
        Announcer = sets.Length > 0 ? sets[new System.Random(System.Environment.TickCount).Next(sets.Length)] : null;
        Debug.Log($"[GameAudio] Announcer for this session: {(Announcer != null ? Announcer.voiceId : "(none)")}");
    }

    // ── Preload ─────────────────────────────────────────────────────────────────

    private void Preload()
    {
        if (Library == null) return;
        foreach (var cue in Library.AllCues())
            if (cue.clips != null)
                foreach (var c in cue.clips)
                    if (c != null && c.loadState == AudioDataLoadState.Unloaded) c.LoadAudioData();
        if (Announcer != null)
            foreach (var c in Announcer.All()) if (c != null && c.loadState == AudioDataLoadState.Unloaded) c.LoadAudioData();
    }

    /// <summary>Every library clip loaded (or failed — a failed clip is skipped, never awaited forever).</summary>
    public bool IsReady
    {
        get
        {
            if (Library == null) return true;
            foreach (var cue in Library.AllCues())
                if (cue.clips != null)
                    foreach (var c in cue.clips)
                        if (c != null && c.loadState == AudioDataLoadState.Loading) return false;
            if (Announcer != null) foreach (var c in Announcer.All()) if (c != null && c.loadState == AudioDataLoadState.Loading) return false;
            return true;
        }
    }

    // ── Channels ────────────────────────────────────────────────────────────────

    public float GetVolume(GameAudioChannel channel) => _volume[(int)channel];

    public void SetVolume(GameAudioChannel channel, float value)
    {
        _volume[(int)channel] = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(PrefPrefix + channel, _volume[(int)channel]);
        ApplyVolume(channel);
    }

    private void ApplyVolume(GameAudioChannel channel)
    {
        var mixer = Library != null ? Library.mixer : null;
        float v = _volume[(int)channel];
        if (mixer != null) mixer.SetFloat(ExposedNames[(int)channel], v > 0.0001f ? 20f * Mathf.Log10(v) : -80f);
    }

    private AudioMixerGroup Group(GameAudioChannel channel) => Library == null ? null : channel switch
    {
        GameAudioChannel.Music => Library.musicGroup,
        GameAudioChannel.Voice => Library.voiceGroup,
        _ => Library.sfxGroup,
    };

    // Without a mixer the channel volume is applied on our own sources instead.
    private float ChannelGain(GameAudioChannel channel) => Library != null && Library.mixer != null ? 1f : _volume[(int)channel];

    /// <summary>Routes a music source (Runner song, fight music, previews) to the Music channel.</summary>
    public static void RouteMusic(AudioSource source)
    {
        if (source == null || Instance == null || Instance.Library == null) return;
        source.outputAudioMixerGroup = Instance.Library.musicGroup;
    }

    // ── Cues ────────────────────────────────────────────────────────────────────

    /// <summary>Plays one variant of `cue` for `scope`. False = skipped (no clip, limit, cooldown, scope not
    /// current, clip not loaded — never waits and never plays later).</summary>
    public bool Play(SfxCue cue, GameAudioScope scope, float volumeScale = 1f) => Play(cue, scope, volumeScale, out _);

    /// <summary>Same as Play, starting `delay` seconds from now (dsp-scheduled; cancelled with its scope).</summary>
    public bool PlayDelayed(SfxCue cue, GameAudioScope scope, float delay, float volumeScale = 1f) => Play(cue, scope, volumeScale, out _, delay);

    public bool Play(SfxCue cue, GameAudioScope scope, float volumeScale, out AudioClip played, float delay = 0f)
    {
        played = null;
        if (cue == null || !cue.HasClips || _closedScopes.Contains(scope)) return false;
        float now = Time.unscaledTime;
        if (_lastStart.TryGetValue(cue, out float last) && now - last < cue.cooldown) return false;
        int count = 0;
        foreach (var kv in _playing) if (kv.Key.isPlaying && kv.Value.cue == cue) count++;
        if (count >= cue.maxInstances) return false;

        int n = cue.clips.Length, pick = Random.Range(0, n);
        if (n > 1 && _lastClip.TryGetValue(cue, out int prev) && pick == prev) pick = (pick + 1 + Random.Range(0, n - 1)) % n;
        var clip = cue.clips[pick];
        if (clip == null || clip.loadState != AudioDataLoadState.Loaded) return false;

        var src = FreeSource();
        if (src == null) return false;
        src.outputAudioMixerGroup = Group(cue.channel);
        src.clip = clip;
        src.pitch = Random.Range(cue.pitch.x, cue.pitch.y);
        src.volume = cue.volume * (1f - Random.Range(0f, cue.volumeJitter)) * Mathf.Clamp01(volumeScale) * ChannelGain(cue.channel);
        if (delay > 0f) src.PlayScheduled(AudioSettings.dspTime + delay); else src.Play();
        _playing[src] = (cue, scope, AudioSettings.dspTime + delay + 0.05);
        _lastStart[cue] = now;
        _lastClip[cue] = pick;
        played = clip;
        CuePlayed?.Invoke(cue, clip);
        return true;
    }

    /// <summary>A loose clip (e.g. a bonus pickup sound) on the Sfx channel, same scope rules.</summary>
    public bool PlayClip(AudioClip clip, float volume, GameAudioScope scope)
    {
        if (clip == null || _closedScopes.Contains(scope)) return false;
        var src = FreeSource();
        if (src == null) return false;
        src.outputAudioMixerGroup = Group(GameAudioChannel.Sfx);
        src.clip = clip; src.pitch = 1f; src.volume = volume * ChannelGain(GameAudioChannel.Sfx);
        src.Play();
        _playing[src] = (null, scope, AudioSettings.dspTime + 0.05);
        return true;
    }

    // ── Announcer ───────────────────────────────────────────────────────────────

    /// <summary>Plays an announcer word (the session's set) on the Voice channel. `attackAt` &gt; 0 schedules it so that
    /// its measured onset (first audible sample) lands `attackAt` seconds from now — the word's accent on the real start.
    /// Two alternating sources, so a word lets the previous one finish its tail; refused / cancelled when its scope
    /// is not (or no longer) current.</summary>
    public bool PlayAnnouncer(AudioClip clip, GameAudioScope scope, float attackAt = 0f)
    {
        if (clip == null || _closedScopes.Contains(scope) || clip.loadState != AudioDataLoadState.Loaded) return false;
        int index = _announcerNext;
        var src = _announcer[index];
        _announcerNext = 1 - _announcerNext;
        src.Stop();
        src.outputAudioMixerGroup = Group(GameAudioChannel.Voice);
        src.clip = clip;
        src.volume = ChannelGain(GameAudioChannel.Voice);
        _announcerScope = scope;
        double start = AudioSettings.dspTime + Mathf.Max(0f, attackAt - Onset(clip));
        _announcerStart[index] = start;
        _announcerHeld[index] = -1;
        src.PlayScheduled(start);
        AnnouncerPlayed?.Invoke(clip);
        return true;
    }

    /// <summary>Pause freezes the countdown (Time.timeScale = 0) — this freezes its voice with it: a word being said
    /// is paused mid-word, a word scheduled for a later beat keeps the delay it still had and is rescheduled on
    /// resume (a dspTime schedule would otherwise fire during the pause).</summary>
    public void SetAnnouncerPaused(bool paused)
    {
        double now = AudioSettings.dspTime;
        for (int i = 0; i < _announcer.Length; i++)
        {
            var s = _announcer[i];
            if (s == null) continue;
            if (paused)
            {
                if (_announcerHeld[i] != -1 || !s.isPlaying) continue;
                if (_announcerStart[i] > now) { _announcerHeld[i] = _announcerStart[i] - now; s.Stop(); }
                else { _announcerHeld[i] = -2; s.Pause(); }
            }
            else
            {
                if (_announcerHeld[i] >= 0) { _announcerStart[i] = now + _announcerHeld[i]; s.PlayScheduled(_announcerStart[i]); }
                else if (_announcerHeld[i] == -2) s.UnPause();
                _announcerHeld[i] = -1;
            }
        }
    }

    /// <summary>Diagnostics / tests: an announcer word was started (or scheduled).</summary>
    public static event System.Action<AudioClip> AnnouncerPlayed;

    /// <summary>Seconds from the clip's start to its first sample above -30 dBFS (cached).</summary>
    public float Onset(AudioClip clip)
    {
        if (clip == null) return 0f;
        if (_onsets.TryGetValue(clip, out float o)) return o;
        o = 0f;
        var data = new float[Mathf.Min(clip.samples, clip.frequency) * clip.channels];
        if (data.Length > 0 && clip.GetData(data, 0))
            for (int i = 0; i < data.Length; i++)
                if (Mathf.Abs(data[i]) > 0.0316f) { o = (float)(i / clip.channels) / clip.frequency; break; }
        _onsets[clip] = o;
        return o;
    }

    private AudioSource FreeSource()
    {
        foreach (var s in _pool) if (!s.isPlaying && !_playing.ContainsKey(s)) return s;   // a scheduled source is not free yet
        foreach (var s in _pool) if (!s.isPlaying && _playing.TryGetValue(s, out var p) && p.startDsp <= AudioSettings.dspTime) { _playing.Remove(s); return s; }
        return null; // all busy: skip rather than cut something
    }

    public void StopScope(GameAudioScope scope, bool keepVoice = false)
    {
        foreach (var s in _pool)
            if (_playing.TryGetValue(s, out var p) && p.scope == scope) { s.Stop(); _playing.Remove(s); }   // also cancels a scheduled one
        if (_announcerScope == scope) { _announcer[0].Stop(); _announcer[1].Stop(); _announcerHeld[0] = _announcerHeld[1] = -1; }
        if (!keepVoice) StopVoice(0f);
    }

    // ── Dialogue voice ──────────────────────────────────────────────────────────

    /// <summary>Plays a dialogue line (cutting any previous one). Returns the token to stop exactly this line.</summary>
    public int PlayVoice(AudioClip clip, GameAudioScope scope)
    {
        if (clip == null || _closedScopes.Contains(scope)) return 0;
        _voice.Stop();
        // The line has priority over any vocalisation still sounding on the Voice channel (no two voices at once).
        foreach (var s in _pool)
            if (_playing.TryGetValue(s, out var p) && p.cue != null && p.cue.channel == GameAudioChannel.Voice && s.isPlaying) s.Stop();
        _voice.outputAudioMixerGroup = Group(GameAudioChannel.Voice);
        _voice.clip = clip;
        _voice.volume = ChannelGain(GameAudioChannel.Voice);
        _voice.pitch = 1f;
        _voiceFadeSpeed = 0f;
        _voice.Play();
        VoicePlayed?.Invoke(clip);
        return ++_voiceToken;
    }

    /// <summary>Remaining seconds of the voice line `token` (0 when it is not the one playing / finished).</summary>
    public float VoiceRemaining(int token) =>
        token != 0 && token == _voiceToken && _voice.isPlaying && _voice.clip != null ? Mathf.Max(0f, _voice.clip.length - _voice.time) : 0f;

    public void PauseVoice(bool paused, int token)
    {
        if (token != _voiceToken) return;
        if (paused) _voice.Pause(); else _voice.UnPause();
    }

    /// <summary>Stops the line `token` (0 = whatever is playing), optionally fading over `fade` seconds.</summary>
    public void StopVoice(float fade, int token = 0)
    {
        if (token != 0 && token != _voiceToken) return;
        if (fade <= 0f || !_voice.isPlaying) { _voice.Stop(); _voiceFadeSpeed = 0f; return; }
        _voiceFadeSpeed = _voice.volume / fade;
    }

    private void Update()
    {
        if (_voiceFadeSpeed > 0f && _voice.isPlaying)
        {
            _voice.volume = Mathf.MoveTowards(_voice.volume, 0f, _voiceFadeSpeed * Time.unscaledDeltaTime);
            if (_voice.volume <= 0f) { _voice.Stop(); _voiceFadeSpeed = 0f; }
        }
    }
}
