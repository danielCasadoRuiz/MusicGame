public struct AudioContext
{
    public SongSegment currentSegment;
    public float       normalizedEnergy;
    public float       normalizedPosition;
    public float       estimatedBPM;
    public float       songTime;
    public float       songDuration;
    public bool        isValid;
}
