/// <summary>
/// Offline pre-analysis module. Runs once after the FFT pass completes,
/// reading shared context and writing derived features into SongProfile.
/// Mirror of IAudioDetector but for pre-analysis (not real-time).
/// Register new modules in AudioPreAnalyzer.BuildOfflineAnalyzers().
/// </summary>
public interface IOfflineAnalyzer
{
    bool IsEnabled(AudioAnalysisConfig config);
    void Analyze(OfflineAnalysisContext ctx, SongProfile profile);
}
