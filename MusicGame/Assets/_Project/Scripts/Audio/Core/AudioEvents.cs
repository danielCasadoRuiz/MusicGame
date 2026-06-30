public struct AudioDataReadyEvent        { public AudioData Data; }
public struct SongProfileReadyEvent      { public SongProfile Profile; }
public struct PreAnalysisStartedEvent    { }
public struct PreAnalysisProgressEvent   { public float Progress; }
public struct EnergyUpdatedEvent         { public float Value; public float SmoothedValue; }
public struct FrequencyBandsUpdatedEvent { public FrequencyBand[] Bands; }
public struct PeakDetectedEvent          { public float Intensity; public float Time; }
public struct OnsetDetectedEvent         { public float Strength;  public float Time; }
public struct BeatDetectedEvent          { public float Confidence; public float Time; }
public struct KickDetectedEvent          { public float Intensity; public float Time; }
public struct SnareDetectedEvent         { public float Intensity; public float Time; }
public struct HiHatDetectedEvent         { public float Intensity; public float Time; }
