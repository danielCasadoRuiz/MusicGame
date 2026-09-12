using UnityEngine;

/// <summary>
/// Timbral analysis: spectral flatness (tonal vs noisy), aggressiveness,
/// timbral change rate, and composite intensity.
/// Output: SongProfile.aggressiveness, .timbralChange, .intensity
/// </summary>
public class TimbralAnalyzer : IOfflineAnalyzer
{
    public bool IsEnabled(AudioAnalysisConfig config)
        => config.advancedEnabled && config.advancedTimbre;

    public void Analyze(OfflineAnalysisContext ctx, SongProfile profile)
    {
        int n = ctx.NumFrames;
        if (n == 0) return;

        float[] energy  = ctx.EnergyEnvelope;
        float[] centroid = ctx.SpectralCentroid;
        float[] flux    = ctx.SpectralFlux;
        float[] flat    = ctx.SpectralFlatness; // may be null if timbre not computed per-frame

        float maxEnergy   = ctx.MaxEnergy;
        float maxCentroid = 0f, maxFlux = 0f;
        for (int f = 0; f < n; f++)
        {
            if (centroid[f] > maxCentroid) maxCentroid = centroid[f];
            if (flux[f]     > maxFlux)     maxFlux     = flux[f];
        }
        if (maxCentroid < 1f) maxCentroid = 1f;
        if (maxFlux     < 1e-6f) maxFlux = 1e-6f;
        if (maxEnergy   < 1e-6f) maxEnergy = 1e-6f;

        // ── Composite intensity (energy + flux) ───────────────────────────────
        var intensity = new float[n];
        for (int f = 0; f < n; f++)
            intensity[f] = 0.6f * (energy[f]  / maxEnergy)
                         + 0.4f * (flux[f]     / maxFlux);
        AdvancedFFTFeatures.BoxSmooth(intensity, 5);
        profile.intensity = intensity;

        // ── Aggressiveness (energy × noise × treble) ──────────────────────────
        var aggr = new float[n];
        for (int f = 0; f < n; f++)
        {
            float normE   = energy[f]   / maxEnergy;
            float normC   = centroid[f] / maxCentroid; // high centroid = bright/aggressive
            float noiseFactor = flat != null ? flat[f] : 0.5f;
            aggr[f] = normE * (0.4f + 0.3f * noiseFactor + 0.3f * normC);
        }
        AdvancedFFTFeatures.BoxSmooth(aggr, 3);
        AdvancedFFTFeatures.NormalizeInPlace(aggr);
        profile.aggressiveness = aggr;

        // ── Timbral change (rate of spectral color change) ────────────────────
        var change = new float[n];
        float invMaxC = 1f / maxCentroid;
        for (int f = 1; f < n - 1; f++)
        {
            float dCentroid = Mathf.Abs(centroid[f + 1] - centroid[f - 1]) * 0.5f * invMaxC;
            float dFlat     = flat != null
                ? Mathf.Abs(flat[f + 1] - flat[f - 1]) * 0.5f
                : 0f;
            change[f] = 0.7f * dCentroid + 0.3f * dFlat;
        }
        change[0] = change[1];
        change[n - 1] = change[n - 2];
        AdvancedFFTFeatures.BoxSmooth(change, 7);
        AdvancedFFTFeatures.NormalizeInPlace(change);
        profile.timbralChange = change;
    }
}
