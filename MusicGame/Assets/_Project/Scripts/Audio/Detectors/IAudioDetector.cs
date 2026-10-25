public interface IAudioDetector
{
    bool IsEnabled { get; }
    void Initialize(AudioAnalysisConfig config);
    void SetSongProfile(SongProfile profile);
    void Analyze(AudioData data);
    /// <summary>Forget every history/cooldown/phase — called on seek, respawn, restart, song change
    /// and any clip-time discontinuity, so the first frame after the jump never compares against
    /// pre-jump state.</summary>
    void Reset();
}
