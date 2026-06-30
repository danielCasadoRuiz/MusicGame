public class FrequencyBandDetector : IAudioDetector
{
    public bool IsEnabled => true;

    public void Initialize(AudioAnalysisConfig config) { }
    public void SetSongProfile(SongProfile profile) { }

    public void Analyze(AudioData data)
    {
        EventBus.Publish(new FrequencyBandsUpdatedEvent { Bands = data.bands });
    }
}
