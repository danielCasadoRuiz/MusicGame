/// <summary>
/// Offline pre-analysis module. Runs once after the FFT pass completes,
/// reading shared context and writing derived features into SongProfile.
/// Mirror of IAudioDetector but for pre-analysis (not real-time).
/// Register new modules in SongProfileDerived.Analyzers. Must only WRITE derived SongProfile fields
/// (never mutate or replace the raw arrays it reads) — it may run on a worker thread.
/// </summary>
public interface IOfflineAnalyzer
{
    bool IsEnabled(DerivedAnalysisSettings config);
    void Analyze(OfflineAnalysisContext ctx, SongProfile profile);
}
