using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Post-processes raw per-frame voice probability:
/// - Applies temporal smoothing to reduce false positives
/// - Detects contiguous vocal zones above threshold
/// Output: SongProfile.voiceProbability (smoothed), .vocalZones
/// </summary>
public class VoiceAnalyzer : IOfflineAnalyzer
{
    public bool IsEnabled(AudioAnalysisConfig config)
        => config.advancedEnabled && config.advancedVoice;

    public void Analyze(OfflineAnalysisContext ctx, SongProfile profile)
    {
        int n = ctx.NumFrames;
        if (n == 0 || ctx.VoiceProb == null) return;

        float hopTime = ctx.HopTime;
        float thresh  = ctx.Config.voiceThreshold;
        float minDur  = ctx.Config.voiceMinDuration;

        // Smooth raw voice probability (~0.3 s window to reduce transients)
        var smoothed = (float[])ctx.VoiceProb.Clone();
        int smoothWin = Mathf.Max(3, Mathf.RoundToInt(0.3f / hopTime));
        AdvancedFFTFeatures.BoxSmooth(smoothed, smoothWin);
        profile.voiceProbability = smoothed;

        // Detect contiguous vocal zones
        int minFrames = Mathf.Max(1, Mathf.RoundToInt(minDur / hopTime));
        var zones     = new List<VocalZone>();
        int start     = -1;
        float confSum = 0f;

        for (int f = 0; f <= n; f++)
        {
            bool active = f < n && smoothed[f] >= thresh;
            if (active)
            {
                if (start < 0) { start = f; confSum = 0f; }
                confSum += smoothed[f];
            }
            else if (start >= 0)
            {
                int len = f - start;
                if (len >= minFrames)
                    zones.Add(new VocalZone
                    {
                        start         = start * hopTime,
                        end           = f     * hopTime,
                        avgConfidence = confSum / len,
                    });
                start = -1;
            }
        }
        profile.vocalZones = zones.ToArray();
    }
}
