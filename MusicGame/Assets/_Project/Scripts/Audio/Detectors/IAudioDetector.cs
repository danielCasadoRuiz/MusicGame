public interface IAudioDetector
{
    bool IsEnabled { get; }
    void Initialize(AudioAnalysisConfig config);
    void SetSongProfile(SongProfile profile);
    void Analyze(AudioData data);
}
