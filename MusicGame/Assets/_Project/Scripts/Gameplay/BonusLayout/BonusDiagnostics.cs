using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// LISTENING diagnostics of the bonus selection (P4) — off in normal play. Plays short clicks over the music
/// so anyone (no musical training needed) can hear whether the bonuses fall on the music:
///   Detections       a high tick on EVERY detected attack of the profile (what the analysis heard);
///   SelectedBonuses  a mid tick on every selected musical bonus, a low double tick on synthetic fill;
///   SyntheticOnly    only the synthetic fill (to judge whether it helps or hurts);
///   ExecutedPulses   a tick when each musical pulse is actually EXECUTED (BeatPulseEvent receipt — real
///                    frame timing, lead included), to hear the visual effects' timing.
/// F7 cycles the mode while playing (logged); the starting mode is collectibles.diagnostics.clickMode.
/// Scheduled clicks use the music's clip time (AudioSource.timeSamples) + AudioSettings.dspTime, so they
/// follow pause / seek / respawn / restart (the cursor re-syncs on any clip-time jump); expect a residual
/// error of about one DSP buffer between the two clocks — it is a comparison aid, not a measurement.
/// </summary>
public class BonusDiagnostics : MonoBehaviour
{
    public static BonusClickMode Mode { get; private set; } = BonusClickMode.Off;
    /// <summary>One-line summary of the bound layout (debug HUD).</summary>
    public static string Summary { get; private set; } = "";

    private const double LookAhead = 0.12;
    private const int Voices = 8;

    private AudioSource _music;
    private float _volume = 0.6f;
    private (double time, int kind)[] _detections = Array.Empty<(double, int)>();
    private (double time, int kind)[] _selected = Array.Empty<(double, int)>();
    private (double time, int kind)[] _synthetic = Array.Empty<(double, int)>();
    private AudioClip[] _clicks;
    private AudioSource[] _voices;
    private int _voice, _cursor;
    private double _lastClip = -1;
    private Action<BeatPulseEvent> _onPulse;

    public static void Bind(GameObject host, GameplayTimeline timeline, SongProfile profile, AudioSource music, MusicRunnerGameplayConfig config)
    {
        var d = host.GetComponent<BonusDiagnostics>() ?? host.AddComponent<BonusDiagnostics>();
        d.Set(timeline, profile, music, config);
    }

    private void Set(GameplayTimeline timeline, SongProfile profile, AudioSource music, MusicRunnerGameplayConfig config)
    {
        _music = music;
        var diag = config.collectibles.diagnostics;
        _volume = diag != null ? diag.clickVolume : 0.6f;
        if (Mode == BonusClickMode.Off && diag != null) Mode = diag.clickMode; // a mode chosen with F7 survives restarts
        float warmup = config.core.warmupTime;

        var det = new List<(double, int)>();
        if (profile.onsetEvents != null) foreach (var e in profile.onsetEvents) det.Add((e.time, 0));
        var sel = new List<(double, int)>();
        var syn = new List<(double, int)>();
        int detected = 0, synthetic = 0, structural = 0, resource = 0, legacy = 0;
        foreach (var e in timeline.Events)
        {
            double clip = e.eventTime - warmup;
            switch (e.origin)
            {
                case BonusOrigin.Detected:      detected++;   sel.Add((clip, 1)); break;
                case BonusOrigin.SyntheticGrid: synthetic++;  sel.Add((clip, 2)); syn.Add((clip, 2)); break;
                case BonusOrigin.Structural:    structural++; sel.Add((clip, 1)); break;
                case BonusOrigin.Resource:      resource++;   break;
                default:                        legacy++;     sel.Add((clip, 1)); break;
            }
        }
        sel.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        _detections = det.ToArray(); _selected = sel.ToArray(); _synthetic = syn.ToArray();
        Summary = $"detections {det.Count} → bonuses: detected {detected}, synthetic {synthetic}, structural {structural}, resource {resource}" +
                  (legacy > 0 ? $", legacy {legacy}" : "") + $" | clicks: {Mode} (F7)";
        _cursor = 0; _lastClip = -1;
        EnsureAudio();
    }

    private void EnsureAudio()
    {
        if (_voices != null) return;
        _clicks = new[] { MakeClick(3200f, 1), MakeClick(1500f, 1), MakeClick(700f, 2), MakeClick(500f, 1) };
        _voices = new AudioSource[Voices];
        for (int i = 0; i < Voices; i++)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false; s.spatialBlend = 0f; s.bypassEffects = true; s.bypassReverbZones = true; s.priority = 0;
            _voices[i] = s;
        }
    }

    private void OnEnable()
    {
        _onPulse = _ => { if (Mode == BonusClickMode.ExecutedPulses) Play(3, 0.0); };
        EventBus.Subscribe(_onPulse);
    }

    private void OnDisable() => EventBus.Unsubscribe(_onPulse);

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f7Key.wasPressedThisFrame)
        {
            Mode = (BonusClickMode)(((int)Mode + 1) % Enum.GetValues(typeof(BonusClickMode)).Length);
            _lastClip = -1;
            Summary = Summary.Substring(0, Math.Max(0, Summary.IndexOf(" | clicks:", StringComparison.Ordinal))) + $" | clicks: {Mode} (F7)";
            Debug.Log($"[BonusDiagnostics] Click mode → {Mode}");
        }
        var list = Mode switch
        {
            BonusClickMode.Detections      => _detections,
            BonusClickMode.SelectedBonuses => _selected,
            BonusClickMode.SyntheticOnly   => _synthetic,
            _ => null,
        };
        if (list == null || _music == null || _music.clip == null || !_music.isPlaying) { _lastClip = -1; return; }

        double now = _music.timeSamples / (double)_music.clip.frequency;
        if (_lastClip < 0 || now < _lastClip - 0.02 || now - _lastClip > 0.5) _cursor = LowerBound(list, now); // start / seek / respawn
        _lastClip = now;
        while (_cursor < list.Length && list[_cursor].time <= now + LookAhead)
        {
            double dt = list[_cursor].time - now;
            if (dt >= -0.01) Play(list[_cursor].kind == 0 ? 0 : list[_cursor].kind, Math.Max(0.0, dt));
            _cursor++;
        }
    }

    private void Play(int kind, double delay)
    {
        if (_voices == null) return;
        var s = _voices[_voice];
        _voice = (_voice + 1) % _voices.Length;
        s.clip = _clicks[Mathf.Clamp(kind, 0, _clicks.Length - 1)];
        s.volume = _volume;
        s.PlayScheduled(AudioSettings.dspTime + delay);
    }

    private static int LowerBound((double time, int kind)[] list, double t)
    {
        int lo = 0, hi = list.Length;
        while (lo < hi) { int mid = (lo + hi) / 2; if (list[mid].time < t) lo = mid + 1; else hi = mid; }
        return lo;
    }

    // A short decaying sine blip (×2 = a double blip, for synthetic items).
    private static AudioClip MakeClick(float hz, int blips)
    {
        int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
        int one = rate * 12 / 1000, gap = rate * 30 / 1000;
        int n = blips == 1 ? one : one * 2 + gap;
        var data = new float[n];
        for (int b = 0; b < blips; b++)
            for (int i = 0; i < one; i++)
                data[b * (one + gap) + i] = Mathf.Sin(2f * Mathf.PI * hz * i / rate) * Mathf.Exp(-i / (rate * 0.003f));
        var clip = AudioClip.Create($"Click{hz:F0}x{blips}", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
