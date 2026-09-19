using UnityEngine;

/// <summary>
/// The single responsible owner of Fight's roulette/match music — a plain AudioSource on its own
/// persistent GameObject, NOT AudioSystemBootstrapper's scene-local live-FFT pipeline (that exists
/// to drive Runner's onset/beat/kick detectors off the song actually being played for gameplay;
/// this only ever needs to play a clip, nothing analyzes it). OpponentSelectionController's grid
/// cells never touch an AudioSource directly — they only report which opponent is highlighted;
/// this is the one place that turns that into actual playback.
///
/// Lives in the always-loaded UI Scene (added by UIFlowController) — its AudioSource is never
/// destroyed by a Mode Scene load/unload, which is exactly what lets the final, locked-in song
/// keep playing unbroken across Opponent Selection -> Versus -> Round Intro -> Countdown -> Fight
/// (Fight.unity loading underneath it touches none of this).
/// </summary>
public class FightMusicController : MonoBehaviour
{
    public static FightMusicController Instance { get; private set; }

    private AudioSource _source;
    private bool        _locked;

    public AudioClip CurrentClip => _source != null ? _source.clip : null;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        _source = gameObject.AddComponent<AudioSource>();
        _source.loop = true; // a short snippet/song looping is the right default for "radio" music
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
    public void PlaySnippet(AudioClip clip)
    {
        if (_locked) return;
        if (clip == null) { _source.Stop(); return; }
        if (_source.clip == clip && _source.isPlaying) return;

        _source.clip = clip;
        _source.time = 0f;
        _source.Play();
    }

    /// <summary>
    /// The definitive match song — plays it (same no-restart guard as PlaySnippet) and stops
    /// responding to PlaySnippet until Reset(). Call this exactly once, right after the roulette
    /// settles on its final opponent.
    /// </summary>
    public void Lock(AudioClip clip)
    {
        _locked = false; // let PlaySnippet's own guard decide whether a restart is actually needed
        PlaySnippet(clip);
        _locked = true;
    }

    /// <summary>Call when a fresh match is about to begin (e.g. FightFlowController re-entering
    /// OpponentSelection) so a new roulette can actually change the music again.</summary>
    public void Reset()
    {
        _locked = false;
    }

    /// <summary>Call when leaving Fight for good — Continue (into the Next Song transition/Song
    /// Analysis), Replay Song, or Main Menu — so the locked-in match song doesn't keep looping over
    /// whatever comes next. Also clears the lock, same as Reset(), so a later Opponent Selection
    /// roulette can play music again without a separate call.</summary>
    public void Stop()
    {
        _locked = false;
        _source.Stop();
    }
}
