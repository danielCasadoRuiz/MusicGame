using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Song Analysis "module" — scene-local orchestration around the existing AudioPreAnalyzer/
/// SongCache/analyzer pipeline (none of that is rewritten, only WHEN it starts changes). Analysis
/// used to start unconditionally from Awake() the instant this GameObject existed; it now waits
/// for an explicit BeginAnalysis() call, so Gameplay no longer implicitly depends on this scene
/// loading, and a future SceneBootstrap can trigger it precisely when GameFlowState.SongAnalysis
/// is entered (see SceneBootstrap.cs) instead of it just happening on scene load.
/// </summary>
public class AudioSystemBootstrapper : MonoBehaviour
{
    [SerializeField] private AudioSource         audioSource;
    [SerializeField] private AudioAnalysisConfig config;
    [SerializeField] private bool autoPlayAfterAnalysis = true;

    public AudioAnalysisConfig Config      => config;
    // Exposed so SceneBootstrap can apply a Song-Selection-picked clip before BeginAnalysis() —
    // see SongSelectionService/GameSession.SelectedSong.
    public AudioSource         AudioSource => audioSource;

    private AudioPreAnalyzer     _preAnalyzer;
    private AudioContextProvider _contextProvider;

    private void Awake()
    {
        var sampler     = gameObject.AddComponent<AudioSampler>();
        var analyzer    = gameObject.AddComponent<AudioAnalyzer>();
        _preAnalyzer    = gameObject.AddComponent<AudioPreAnalyzer>();
        // Shows itself only between PreAnalysisStartedEvent and SongProfileReadyEvent (never on a
        // cache hit, which skips straight to SongProfileReadyEvent) — see its own class doc.
        gameObject.AddComponent<AnalyzingScreenController>();

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

    /// <summary>
    /// COMMAND — call this to actually start analyzing whatever clip is on the AudioSource right
    /// now (a future Song Selection phase will assign that clip beforehand). Safe to call once
    /// per song; calling it again re-analyzes/re-checks-cache for whatever clip is currently
    /// assigned. Publishes PreAnalysisStartedEvent/PreAnalysisProgressEvent/SongProfileReadyEvent
    /// exactly as before (see AudioPreAnalyzer) — this method only decides WHEN that pipeline
    /// starts, it doesn't change what it does.
    /// </summary>
    public void BeginAnalysis()
    {
        if (audioSource.clip != null)
            StartCoroutine(_preAnalyzer.Analyze(audioSource.clip, config, OnProfileReady));
        else
            Debug.LogWarning("[AudioBootstrapper] No AudioClip assigned to AudioSource.");
    }

    private void OnProfileReady(SongProfile profile)
    {
        if (autoPlayAfterAnalysis && !audioSource.isPlaying)
            audioSource.Play();
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
