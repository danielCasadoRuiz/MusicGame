using UnityEngine;

/// <summary>
/// Converts RMS energy to perceptual loudness in dB.
/// Output: SongProfile.loudnessDb (per-frame, 0 dB = max, negative = quiet).
/// </summary>
public class LoudnessAnalyzer : IOfflineAnalyzer
{
    public bool IsEnabled(AudioAnalysisConfig config)
        => config.advancedEnabled && config.advancedLoudness;

    public void Analyze(OfflineAnalysisContext ctx, SongProfile profile)
    {
        int n   = ctx.NumFrames;
        var db  = new float[n];
        var env = ctx.EnergyEnvelope;

        for (int f = 0; f < n; f++)
            db[f] = 20f * Mathf.Log10(Mathf.Max(env[f], 1e-9f));

        profile.loudnessDb = db;
    }
}
