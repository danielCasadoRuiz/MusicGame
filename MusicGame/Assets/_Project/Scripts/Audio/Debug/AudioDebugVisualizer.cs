using System;
using UnityEngine;

/// <summary>
/// Lightweight debug overlay: a small top-left "event flash" (PEAK/ONSET/BEAT/KICK/SNARE/HIHAT/
/// BPM/SCAN%) — no per-band frequency bar UI anymore (removed; the underlying analysis data
/// itself — SongProfile.visualBandEnvelopes, MusicWorldManager.NormalizedBandValue — is untouched
/// and still drives the ground mesh and the Horizon World's SpectrumBars3D exactly as before).
/// </summary>
public class AudioDebugVisualizer : MonoBehaviour
{
    private AudioAnalysisConfig _config;
    private AudioSource         _audioSource;

    // Event flash
    private string _lastEvent     = "";
    private float  _lastEventTime = -99f;

    // Single white pixel — the flash label's background could use this if ever needed
    private Texture2D _whiteTex;

    // Cached delegates
    private Action<PeakDetectedEvent>        _onPeak;
    private Action<OnsetDetectedEvent>       _onOnset;
    private Action<BeatDetectedEvent>        _onBeat;
    private Action<KickDetectedEvent>        _onKick;
    private Action<SnareDetectedEvent>       _onSnare;
    private Action<HiHatDetectedEvent>       _onHiHat;
    private Action<SongProfileReadyEvent>    _onProfile;
    private Action<PreAnalysisProgressEvent> _onProgress;

    // ─── Init ─────────────────────────────────────────────────────────────

    public void Initialize(AudioAnalysisConfig config, AudioSource audioSource)
    {
        _config      = config;
        _audioSource = audioSource;
        _whiteTex = new Texture2D(1, 1);
        _whiteTex.SetPixel(0, 0, Color.white);
        _whiteTex.Apply();
    }

    // ─── EventBus subscriptions ───────────────────────────────────────────

    private void OnEnable()
    {
        _onPeak     = e => Flash($"PEAK  {e.Intensity:F4}");
        _onOnset    = e => Flash($"ONSET {e.Strength:F4}");
        _onBeat     = e => Flash($"BEAT  {e.Confidence:F2}");
        _onKick     = e => Flash($"KICK  {e.Intensity:F4}");
        _onSnare    = e => Flash($"SNARE {e.Intensity:F4}");
        _onHiHat    = e => Flash($"HIHAT {e.Intensity:F4}");
        _onProfile  = e => Flash($"BPM ~ {e.Profile.estimatedBPM:F0}");
        _onProgress = e => Flash($"SCAN  {e.Progress * 100f:F0}%");

        EventBus.Subscribe(_onPeak);
        EventBus.Subscribe(_onOnset);
        EventBus.Subscribe(_onBeat);
        EventBus.Subscribe(_onKick);
        EventBus.Subscribe(_onSnare);
        EventBus.Subscribe(_onHiHat);
        EventBus.Subscribe(_onProfile);
        EventBus.Subscribe(_onProgress);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onPeak);
        EventBus.Unsubscribe(_onOnset);
        EventBus.Unsubscribe(_onBeat);
        EventBus.Unsubscribe(_onKick);
        EventBus.Unsubscribe(_onSnare);
        EventBus.Unsubscribe(_onHiHat);
        EventBus.Unsubscribe(_onProfile);
        EventBus.Unsubscribe(_onProgress);
    }

    private void OnDestroy()
    {
        if (_whiteTex != null) Destroy(_whiteTex);
    }

    // ─── Render ───────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (_config == null || !_config.enableVisualizer) return;

        float age  = Time.time - _lastEventTime;
        float fade = Mathf.Clamp01(1f - age / 0.7f);
        if (fade <= 0f) return;

        GUI.color = new Color(1f, 0.88f, 0.08f, fade);
        GUI.Label(new Rect(8f, Screen.height - 22f, 200f, 18f), _lastEvent);
        GUI.color = Color.white;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private void Flash(string msg)
    {
        _lastEvent     = msg;
        _lastEventTime = Time.time;
        if (_config != null && _config.enableDebugLog)
            Debug.Log($"[Audio] {msg}");
    }
}
