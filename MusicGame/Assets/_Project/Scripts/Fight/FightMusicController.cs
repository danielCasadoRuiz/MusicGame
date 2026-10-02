using UnityEngine;

/// <summary>
/// The single responsible owner of Fight's roulette/match music — plain AudioSources on its own
/// persistent GameObject, NOT AudioSystemBootstrapper's scene-local live-FFT pipeline (that exists
/// to drive Runner's onset/beat/kick detectors off the song actually being played for gameplay;
/// this only ever needs to play a clip, nothing analyzes it). OpponentSelectionController's grid
/// cells never touch an AudioSource directly — they only report which opponent is highlighted;
/// this is the one place that turns that into actual playback.
///
/// Lives in the always-loaded UI Scene (added by UIFlowController) — its AudioSources are never
/// destroyed by a Mode Scene load/unload, which is exactly what lets the final, locked-in song
/// keep playing unbroken across Opponent Selection -> Versus -> Round Intro -> Countdown -> Fight
/// (Fight.unity loading underneath it touches none of this).
///
/// Match playlist: when Lock() is given the opponent's level config, a song that runs out before the
/// match does cross-fades into another of that opponent's songs (never the one just played, when it
/// has another; with a single song it cross-fades into itself) — no silence or hard cut mid-fight.
/// Two sources alternate: the playing one fades out while the incoming one fades in.
/// </summary>
public class FightMusicController : MonoBehaviour
{
    public static FightMusicController Instance { get; private set; }

    [Tooltip("Seconds of overlap when one match song hands over to the next (equal-power cross-fade).")]
    [SerializeField, Min(0f)] private float crossfadeSeconds = 4f;

    private AudioSource _current;
    private AudioSource _incoming;
    private bool        _locked;
    private OpponentLevelConfig _playlist; // songs to continue with once the locked one ends (null = loop it)
    private float       _fadeElapsed = -1f;  // < 0 = no cross-fade running
    private float       _fadeDuration;
    private Coroutine   _fadeOut;
    private AudioSource _fx;            // roulette radio-static bursts only
    private float       _fxStopAt = -1f;
    private float       _fxVolume;
    private static AudioClip s_staticClip; // generated ONCE, reused for every burst
    private float       _fadeOutSeconds = 1.5f;

    public AudioClip CurrentClip => _current != null ? _current.clip : null;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        _current  = gameObject.AddComponent<AudioSource>();
        _incoming = gameObject.AddComponent<AudioSource>();
        _fx       = gameObject.AddComponent<AudioSource>();
        _fx.playOnAwake = false;
        _fx.loop = true;
        var flow = Resources.Load<AppConfigSO>("AppConfig")?.fightFlow;
        if (flow != null) _fadeOutSeconds = flow.combatMusicFadeOutSeconds;
        _current.loop = true; // a short snippet/song looping is the right default for "radio" music
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Called for every roulette highlight, including the final one. A no-op if `clip` is already
    /// what's currently loaded and playing — this is what avoids restarting the final opponent's
    /// song the instant it's locked in, when the step that highlighted it already happened to be
    /// playing that exact clip. Does nothing once Lock() has been called, until Reset().
    /// </summary>
    public void PlaySnippet(AudioClip clip) => PlaySnippet(clip, 0f);

    /// <summary>Same as PlaySnippet(clip), starting `startTime` seconds in (the rival roulette starts
    /// each fragment at the song's preview region — SongPreviewConfigSO).</summary>
    public void PlaySnippet(AudioClip clip, float startTime)
    {
        if (_locked) return;
        CancelFadeOut();
        CancelCrossfade();
        _current.loop = true;
        if (clip == null) { _current.Stop(); return; }
        if (_current.clip == clip && _current.isPlaying) return;

        if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData(); // preload is off on music clips
        _current.clip = clip;
        _current.Play();
        _current.time = Mathf.Clamp(startTime, 0f, Mathf.Max(0f, clip.length - 0.1f)); // seek after Play: reliable once data is loaded
    }

    /// <summary>
    /// The definitive match song — plays it (same no-restart guard as PlaySnippet) and stops
    /// responding to PlaySnippet until Reset(). Call this exactly once, right after the roulette
    /// settles on its final opponent. With `playlist` (the opponent's level config) the match keeps
    /// going through that opponent's other songs instead of looping this one; a repeated Lock with
    /// the same playlist while it's already playing (Fight Again) leaves the music flowing untouched.
    /// </summary>
    public void Lock(AudioClip clip, OpponentLevelConfig playlist = null)
    {
        if (_locked && playlist != null && playlist == _playlist && _current.isPlaying) return;
        _locked = false; // let PlaySnippet's own guard decide whether a restart is actually needed
        PlaySnippet(clip);
        _playlist = playlist;
        _current.loop = playlist == null;
        _locked = true;
    }

    /// <summary>Call when a fresh match is about to begin (e.g. FightFlowController re-entering
    /// OpponentSelection) so a new roulette can actually change the music again.</summary>
    public void Reset()
    {
        _locked = false;
        _playlist = null;
    }

    /// <summary>Call when leaving Fight for good — Continue (into the Next Song transition/Song
    /// Analysis), Replay Song, or Main Menu — so the locked-in match song doesn't keep looping over
    /// whatever comes next. Also clears the lock, same as Reset(), so a later Opponent Selection
    /// roulette can play music again without a separate call.
    /// Fades out over FightFlowConfig.combatMusicFadeOutSeconds (unscaled time; this object lives in
    /// the persistent UI scene, so the fade survives Fight.unity unloading) — never an abrupt cut. A
    /// new roulette (PlaySnippet/Lock) cancels a fade still running.</summary>
    public void Stop()
    {
        Reset();
        CancelCrossfade();
        CancelFadeOut();
        if (_fadeOutSeconds <= 0f || !_current.isPlaying) { _current.Stop(); return; }
        _fadeOut = StartCoroutine(FadeOutRoutine(_current, _fadeOutSeconds));
    }

    private System.Collections.IEnumerator FadeOutRoutine(AudioSource source, float seconds)
    {
        float start = source.volume;
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            source.volume = start * (1f - t / seconds);
            yield return null;
        }
        source.Stop();
        source.volume = 1f;
        _fadeOut = null;
    }

    private void CancelFadeOut()
    {
        if (_fadeOut == null) return;
        StopCoroutine(_fadeOut);
        _fadeOut = null;
        _current.Stop();
        _current.volume = 1f;
    }

    /// <summary>A very short, quiet radio-tuning burst (roulette between fake rivals). The noise clip
    /// is procedural and built once; each burst just plays it from a random offset.</summary>
    public void PlayRadioStatic(float duration, float volume)
    {
        if (duration <= 0f || volume <= 0f) return;
        s_staticClip ??= BuildStaticClip();
        _fx.clip = s_staticClip;
        _fxVolume = volume;
        _fx.volume = volume;
        _fx.time = Random.Range(0f, s_staticClip.length * 0.8f);
        _fx.Play();
        _fxStopAt = Time.unscaledTime + duration;
    }

    // 1 s of band-limited noise with slow "tuning" sweeps and sparse crackles.
    private static AudioClip BuildStaticClip()
    {
        const int rate = 44100;
        var data = new float[rate];
        var rng = new System.Random(4242);
        float lp = 0f, hp = 0f, prev = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            float sweep = 0.25f + 0.2f * Mathf.Sin(i * 0.00031f) * Mathf.Sin(i * 0.0021f);
            lp += sweep * (white - lp);           // moving low-pass = the "tuning" wobble
            hp = 0.97f * (hp + lp - prev);        // remove rumble
            prev = lp;
            float crackle = rng.NextDouble() < 0.0015 ? (float)(rng.NextDouble() * 2.0 - 1.0) : 0f;
            data[i] = Mathf.Clamp(hp * 0.8f + crackle * 0.6f, -1f, 1f);
        }
        var clip = AudioClip.Create("RadioStatic", data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private void Update()
    {
        if (_fxStopAt >= 0f)
        {
            float left = _fxStopAt - Time.unscaledTime;
            if (left <= 0f) { _fx.Stop(); _fxStopAt = -1f; }
            else _fx.volume = _fxVolume * Mathf.Clamp01(left / 0.05f); // tiny tail, no click
        }

        if (!_locked || _playlist == null || _current.clip == null) return;

        if (_fadeElapsed >= 0f)
        {
            // Unscaled: a paused/slowed match (timeScale) must not freeze the hand-over halfway.
            _fadeElapsed += Time.unscaledDeltaTime;
            float k = _fadeDuration > 0f ? Mathf.Clamp01(_fadeElapsed / _fadeDuration) : 1f;
            _current.volume  = Mathf.Cos(k * Mathf.PI * 0.5f);
            _incoming.volume = Mathf.Sin(k * Mathf.PI * 0.5f);
            if (k >= 1f) FinishCrossfade();
            return;
        }

        float fade = Mathf.Min(crossfadeSeconds, _current.clip.length / 3f);
        bool ending = _current.isPlaying ? _current.clip.length - _current.time <= fade : _current.time <= 0f;
        if (ending) BeginCrossfade(_playlist.GetRandomSong(_current.clip) ?? _current.clip, fade);
    }

    private void BeginCrossfade(AudioClip next, float duration)
    {
        _incoming.clip   = next;
        _incoming.loop   = false;
        _incoming.volume = 0f;
        _incoming.time   = 0f;
        _incoming.Play();
        _fadeDuration = duration;
        _fadeElapsed  = 0f;
    }

    private void FinishCrossfade()
    {
        _current.Stop();
        _current.volume = 1f;
        (_current, _incoming) = (_incoming, _current);
        _current.volume = 1f;
        _fadeElapsed = -1f;
    }

    private void CancelCrossfade()
    {
        if (_fadeElapsed >= 0f) _incoming.Stop();
        _fadeElapsed = -1f;
        _current.volume = 1f;
    }
}
