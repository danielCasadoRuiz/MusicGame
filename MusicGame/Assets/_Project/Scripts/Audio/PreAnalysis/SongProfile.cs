using UnityEngine;

[System.Serializable]
public class SongProfile
{
    public float    duration;
    public int      sampleRate;
    public float    estimatedBPM;
    public float    averageEnergy;
    public float    maxEnergy;
    public float    analysisHopTime;

    public float[]   energyEnvelope;
    public float[]   spectralCentroid;
    public float[]   spectralFlux;
    public float[]   onsetTimes;
    public float[][] bandEnvelopes;

    public SongSegment[] segments;

    public SongSegment GetSegmentAt(float time)
    {
        if (segments == null || segments.Length == 0) return default;
        foreach (var seg in segments)
            if (time >= seg.startTime && time < seg.endTime)
                return seg;
        return segments[segments.Length - 1];
    }

    public float GetEnergyAt(float time)
    {
        if (energyEnvelope == null || energyEnvelope.Length == 0 || analysisHopTime <= 0f) return 0f;
        int idx = Mathf.Clamp(Mathf.FloorToInt(time / analysisHopTime), 0, energyEnvelope.Length - 1);
        return energyEnvelope[idx];
    }

    public float GetBandEnergyAt(int bandIndex, float time)
    {
        if (bandEnvelopes == null || bandIndex >= bandEnvelopes.Length) return 0f;
        var env = bandEnvelopes[bandIndex];
        if (env == null || env.Length == 0 || analysisHopTime <= 0f) return 0f;
        int idx = Mathf.Clamp(Mathf.FloorToInt(time / analysisHopTime), 0, env.Length - 1);
        return env[idx];
    }
}
