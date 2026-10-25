using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene-local, LIVE audio-reactive pipeline (Sampler → Analyzer → per-frame detectors — Onset/
/// Beat/Kick/Snare/HiHat/Energy/Bands/Peak) around whatever AudioClip `audioSource` is currently
/// playing — today only consumed by the optional AudioDebugVisualizer (see config.enableVisualizer),
/// not by any production gameplay script (Gameplay itself is driven from the pre-computed
/// SongProfile + MusicClock.SongTime, not live FFT), so it's safe to keep running unconditionally.
///
/// The OFFLINE, one-shot pre-analysis pass (AudioPreAnalyzer/SongCache) that used to live here moved
/// to SongAnalysisController (always-loaded UI Scene) — it needs no AudioSource or scene-local
/// dependency at all (AudioPreAnalyzer.Analyze operates purely on the raw clip data), so it now runs
/// entirely BEFORE Runner even loads (see SceneFlowController.ModeFor(SongAnalysis) and
/// SongAnalysisController's own doc for why). Each detector here still gets that already-resolved
/// SongProfile the moment Runner loads — RunnerSceneBootstrap re-publishes SongProfileReadyEvent
/// once this object's own SongProfileReadyEvent subscription (below) has had a chance to register.
/// </summary>
public class AudioSystemBootstrapper : MonoBehaviour
{
    [SerializeField] private AudioSource         audioSource;
    [SerializeField] private AudioAnalysisConfig config;

    public AudioAnalysisConfig Config      => config;
    // Exposed so RunnerSceneBootstrap can apply the Song-Selection-picked clip onto the same
    // AudioSource GameplayManager plays — see SongSelectionService/GameSession.SelectedSong.
    public AudioSource         AudioSource => audioSource;

    private AudioContextProvider _contextProvider;
    private IAudioDetector[]     _detectors;
    private AudioAnalyzer        _analyzer;
    // ONE stored handler (not a lambda per detector) so OnDestroy can actually unsubscribe it.
    private System.Action<SongProfileReadyEvent> _onProfile;

    private void Awake()
    {
        var sampler     = gameObject.AddComponent<AudioSampler>();
        _analyzer       = gameObject.AddComponent<AudioAnalyzer>();

        _contextProvider = new AudioContextProvider();
        _contextProvider.Initialize(audioSource);

        _detectors = BuildDetectors();
        foreach (var d in _detectors) d.Initialize(config);

        // Deliver SongProfile to each detector once pre-analysis finishes — a new profile is a
        // song change, so every detector history/cooldown is reset too.
        _onProfile = OnProfileReady;
        EventBus.Subscribe(_onProfile);

        sampler.Initialize(audioSource, config);
        _analyzer.Initialize(sampler, config, _detectors);

        if (config != null && config.enableVisualizer)
        {
            var visualizer = gameObject.AddComponent<AudioDebugVisualizer>();
            visualizer.Initialize(config, audioSource);
        }
    }

    private void OnProfileReady(SongProfileReadyEvent e)
    {
        foreach (var d in _detectors) d.SetSongProfile(e.Profile);
        if (_analyzer != null) _analyzer.ResetState("song change");
        else foreach (var d in _detectors) d.Reset();
    }

    private void OnDestroy()
    {
        _contextProvider?.Dispose();
        // Targeted unsubscribe — never EventBus.Clear(), which would also drop persistent,
        // cross-scene subscribers (GameSession, AppFlowController).
        if (_onProfile != null) EventBus.Unsubscribe(_onProfile);
    }

    private IAudioDetector[] BuildDetectors()
    {
        var list = new List<IAudioDetector>();
        if (config.enableEnergy) list.Add(new EnergyDetector());
        if (config.enableBands)  list.Add(new FrequencyBandDetector());
        if (config.enablePeaks)  list.Add(new PeakDetector());
        if (config.enableOnsets) list.Add(new OnsetDetector());
        if (config.enableBeat)   list.Add(new BeatDetector());
        if (config.enableKick)   list.Add(new KickDetector());
        if (config.enableSnare)  list.Add(new SnareDetector());
        if (config.enableHiHat)  list.Add(new HiHatDetector());
        return list.ToArray();
    }
}
