using System;
using UnityEngine;

[Serializable]
public class BandEnvelope
{
    public float[] values;
}

[Serializable]
public class SongProfileData
{
    public string clipName;
    public int    clipSamples;

    public float   duration;
    public int     sampleRate;
    public float   estimatedBPM;
    public float   averageEnergy;
    public float   maxEnergy;
    public float   analysisHopTime;

    public float[]       energyEnvelope;
    public float[]       spectralCentroid;
    public float[]       spectralFlux;
    public float[]       onsetTimes;
    public BandEnvelope[] bandEnvelopes;
    public SongSegment[] segments;

    public bool Matches(AudioClip clip)
        => clipName == clip.name && clipSamples == clip.samples;

    public static SongProfileData From(AudioClip clip, SongProfile p)
    {
        int n = p.bandEnvelopes?.Length ?? 0;
        var bands = new BandEnvelope[n];
        for (int i = 0; i < n; i++)
            bands[i] = new BandEnvelope { values = p.bandEnvelopes[i] };

        return new SongProfileData
        {
            clipName         = clip.name,
            clipSamples      = clip.samples,
            duration         = p.duration,
            sampleRate       = p.sampleRate,
            estimatedBPM     = p.estimatedBPM,
            averageEnergy    = p.averageEnergy,
            maxEnergy        = p.maxEnergy,
            analysisHopTime  = p.analysisHopTime,
            energyEnvelope   = p.energyEnvelope,
            spectralCentroid = p.spectralCentroid,
            spectralFlux     = p.spectralFlux,
            onsetTimes       = p.onsetTimes,
            bandEnvelopes    = bands,
            segments         = p.segments,
        };
    }

    public SongProfile ToProfile()
    {
        int n = bandEnvelopes?.Length ?? 0;
        var bands = new float[n][];
        for (int i = 0; i < n; i++)
            bands[i] = bandEnvelopes[i]?.values;

        return new SongProfile
        {
            duration         = duration,
            sampleRate       = sampleRate,
            estimatedBPM     = estimatedBPM,
            averageEnergy    = averageEnergy,
            maxEnergy        = maxEnergy,
            analysisHopTime  = analysisHopTime,
            energyEnvelope   = energyEnvelope,
            spectralCentroid = spectralCentroid,
            spectralFlux     = spectralFlux,
            onsetTimes       = onsetTimes,
            bandEnvelopes    = bands,
            segments         = segments,
        };
    }
}
