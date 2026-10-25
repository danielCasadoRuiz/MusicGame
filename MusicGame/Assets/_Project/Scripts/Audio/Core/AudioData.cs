public class AudioData
{
    public float[]        spectrum;
    public float[]        waveform;
    public float          energy;
    public float          smoothedEnergy;
    public FrequencyBand[] bands;
    public float          deltaTime;
    // CLIP time (seconds into the AudioClip) of this frame's capture, read from
    // AudioSource.timeSamples right next to GetSpectrumData — the SongProfile domain, so detectors
    // query the profile with this. Never Time.time (unrelated to the music). See MusicTime.
    public float          clipTime;
}
