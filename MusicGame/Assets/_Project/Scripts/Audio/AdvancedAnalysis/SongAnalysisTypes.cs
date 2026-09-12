using System;

/// <summary>A time window with start and end in seconds.</summary>
[Serializable]
public struct TimeRange
{
    public float start;
    public float end;
    public float Length => end - start;
}

/// <summary>A pair of song sections that sound similar.</summary>
[Serializable]
public struct SimilarSection
{
    public float timeA;
    public float timeB;
    public float length;
    public float similarity; // 0..1
}

/// <summary>A contiguous zone where voice probability exceeds the detection threshold.</summary>
[Serializable]
public struct VocalZone
{
    public float start;
    public float end;
    public float avgConfidence; // 0..1
}
