/// <summary>
/// Detects jumps in the clip time the live audio pipeline observes (AudioSampler captures it from
/// AudioSource.timeSamples each frame it samples). Seeks published by MusicClock already reset the
/// detectors (MusicTimeDiscontinuityEvent); this catches every other jump — a seek done outside
/// the clock, a loop, an external restart — so detector state never spans a discontinuity.
///
/// Expected advance between two observations: the real frame delta when they are consecutive
/// frames, ~0 when frames were skipped (the source wasn't playing, e.g. pause/hold), plus
/// ForwardTolerance for the DSP-buffer staircase. Any backward step beyond BackwardTolerance is
/// a jump. Pure — edit-mode tested.
/// </summary>
public sealed class ClipTimeContinuity
{
    public const double ForwardTolerance  = 0.1;
    public const double BackwardTolerance = 0.02;

    private bool   _has;
    private double _last;

    public double LastClipTime => _last;

    /// <summary>Records clipTime; returns true when it is discontinuous with the previous one.</summary>
    public bool Observe(double clipTime, bool consecutiveFrame, double frameDelta)
    {
        bool jump = false;
        if (_has)
        {
            double step     = clipTime - _last;
            double expected = consecutiveFrame ? System.Math.Max(0.0, frameDelta) : 0.0;
            jump = step < -BackwardTolerance || step > expected + ForwardTolerance;
        }
        _has  = true;
        _last = clipTime;
        return jump;
    }

    public void Reset() => _has = false;
}
