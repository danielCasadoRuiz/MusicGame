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

    private void Awake()
    {
        var sampler     = gameObject.AddComponent<AudioSampler>();
        var analyzer    = gameObject.AddComponent<AudioAnalyzer>();

        _contextProvider = new AudioContextProvider();
        _contextProvider.Initialize(audioSource);

        IAudioDetector[] detectors = BuildDetectors();

        foreach (var d in detectors)
        {
            d.Initialize(config);
            // Deliver SongProfile to each detector once pre-analysis finishes
            EventBus.Subscribe<SongProfileReadyEvent>(e => d.SetSongProfile(e.Profile));
        }

        sampler.Initialize(audioSource, config);
        analyzer.Initialize(sampler, config, detectors);

        if (config != null && config.enableVisualizer)
        {
            var visualizer = gameObject.AddComponent<AudioDebugVisualizer>();
            visualizer.Initialize(config, audioSource);
        }
    }

    private void OnDestroy()
    {
        _contextProvider?.Dispose();
        // NOTE: no longer calls EventBus.Clear() here. That used to be a blunt way to clean up the
        // per-detector lambda subscriptions added in Awake() (see BuildDetectors' subscribe loop
        // above), but a global wipe now also silently drops persistent, cross-scene subscribers
        // (GameSession, AppFlowController) the moment this scene-local object is destroyed — which
        // would have been a latent bug the instant a real scene transition exists. The detector
        // lambdas leaking on repeated Awake() calls is pre-existing, narrower technical debt to
        // revisit when Song Analysis becomes an independently re-triggerable service.
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
