public struct AudioDataReadyEvent        { public AudioData Data; }
/// <summary>A complete profile for the song selection with this generation (GameSession.SelectionGeneration).
/// Published once per analysis request (AudioPreAnalyzer) — never for a stale/cancelled request — and
/// re-published by RunnerSceneBootstrap for the Runner scene's listeners.</summary>
public struct SongProfileReadyEvent      { public SongProfile Profile; public int SelectionGeneration; }
/// <summary>The current request's cache probe finished (IsCacheHit = no audio processing needed).</summary>
public struct PreAnalysisStartedEvent    { public bool IsCacheHit; public int RequestId; }
/// <summary>REAL, monotonic 0..1 progress of the current request, published on the main thread.</summary>
public struct PreAnalysisProgressEvent   { public float Progress; public int RequestId; }
public struct EnergyUpdatedEvent         { public float Value; public float SmoothedValue; }
public struct FrequencyBandsUpdatedEvent { public FrequencyBand[] Bands; }
public struct PeakDetectedEvent          { public float Intensity; public float Time; }
public struct OnsetDetectedEvent         { public float Strength;  public float Time; }
public struct BeatDetectedEvent          { public float Confidence; public float Time; }
public struct KickDetectedEvent          { public float Intensity; public float Time; }
public struct SnareDetectedEvent         { public float Intensity; public float Time; }
public struct HiHatDetectedEvent         { public float Intensity; public float Time; }
