using System.Collections.Generic;
using UnityEngine;

public class AudioSystemBootstrapper : MonoBehaviour
{
    [SerializeField] private AudioSource         audioSource;
    [SerializeField] private AudioAnalysisConfig config;
    [SerializeField] private bool autoPlayAfterAnalysis = true;

    private AudioContextProvider _contextProvider;

    private void Awake()
    {
        var sampler     = gameObject.AddComponent<AudioSampler>();
        var analyzer    = gameObject.AddComponent<AudioAnalyzer>();
        var preAnalyzer = gameObject.AddComponent<AudioPreAnalyzer>();

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
            visualizer.Initialize(config);
        }

        if (audioSource.clip != null)
            StartCoroutine(preAnalyzer.Analyze(audioSource.clip, config, OnProfileReady));
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
        EventBus.Clear();
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
