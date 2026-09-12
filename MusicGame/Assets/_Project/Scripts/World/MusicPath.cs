using UnityEngine;

/// <summary>
/// Immutable, arc-length-parameterized 3-D path.
/// Built once before gameplay starts; O(log n) sampling at runtime via binary search.
///
/// All coordinates are world-space.
/// The path expresses distances from 0 to TotalLength.
/// A sample at distance d has: position, tangent (forward), right (horizontal), up (surface normal), width.
/// </summary>
public class MusicPath
{
    public readonly struct Sample
    {
        public readonly Vector3    position;
        public readonly Vector3    tangent;    // normalised forward along path
        public readonly Vector3    right;      // always horizontal (XZ-plane perpendicular to tangent)
        public readonly Vector3    up;         // surface normal (cross of tangent × right)
        public readonly float      distance;   // arc-length from path start
        public readonly float      width;      // passable width at this point

        public Sample(Vector3 pos, Vector3 tan, float dist, float w)
        {
            position = pos;
            tangent  = tan.sqrMagnitude < 0.0001f ? Vector3.forward : tan.normalized;
            right    = Vector3.Cross(Vector3.up, tangent).normalized;
            up       = Vector3.Cross(tangent, right).normalized;
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            distance = dist;
            width    = w;
        }
    }

    private readonly Sample[] _samples;

    public float    TotalLength => _samples.Length > 0 ? _samples[_samples.Length - 1].distance : 0f;
    public int      SampleCount => _samples.Length;
    public Sample[] AllSamples  => _samples;    // for mesh generation / gizmos

    public MusicPath(Sample[] samples) => _samples = samples;

    /// <summary>
    /// Interpolated sample at a given arc-length distance.
    /// Clamps to [0, TotalLength].
    /// </summary>
    public Sample GetSample(float distance)
    {
        if (_samples == null || _samples.Length == 0) return default;

        distance = Mathf.Clamp(distance, 0f, TotalLength);

        // Binary search — O(log n)
        int lo = 0, hi = _samples.Length - 1;
        while (lo < hi - 1)
        {
            int mid = (lo + hi) >> 1;
            if (_samples[mid].distance <= distance) lo = mid;
            else                                    hi = mid;
        }

        var a = _samples[lo];
        var b = _samples[hi];
        float span = b.distance - a.distance;
        float t    = span < 0.0001f ? 0f : (distance - a.distance) / span;

        Vector3 pos = Vector3.Lerp(a.position, b.position, t);
        Vector3 tan = Vector3.Slerp(a.tangent,  b.tangent,  t);
        float   w   = Mathf.Lerp(a.width, b.width, t);

        return new Sample(pos, tan, distance, w);
    }

    public float GetWidth(float distance) => GetSample(distance).width;
}
