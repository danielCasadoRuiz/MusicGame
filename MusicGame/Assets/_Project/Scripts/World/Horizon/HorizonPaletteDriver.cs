using UnityEngine;

// One small shared palette state, sampled by the sky, LEDs and water. No audio analysis here.
//
// Two frames per style: Anchor (the style's fixed sunset — buildup look, never moves) and Frame (the
// musically varied one: song-relative energy picks calm/buildup/peak, the harmonic centre adds a slow
// hue rotation). The sky shader blends Anchor→Frame by distance from the sun, so the sun and its
// immediate glow keep the style's sunset while the zenith, sides and back carry the variation.
public sealed class HorizonPaletteDriver : MonoBehaviour
{
    public static HorizonPaletteDriver Instance {get;private set;}
    public HorizonPaletteFrame Frame {get;private set;}
    public HorizonPaletteFrame Anchor {get;private set;}
    public bool HasPalette {get;private set;}
    private HorizonConfig _config;
    private float _energy=.5f, _hueOffset;
    public void Initialize(HorizonConfig config) { _config=config; Instance=this; Tick(.5f,0f,0f,true); }

    /// <param name="energy">Song-relative 0..1 (MusicEnvironmentController.SongRelativeEnergy).</param>
    /// <param name="hueOffset">-1..1 harmonic offset (see HarmonicOffset).</param>
    /// <param name="progress">0..1 position in the song — adds a slow hue drift start→end.</param>
    public void Tick(float energy, float hueOffset, float progress, bool immediate=false)
    {
        var clock=MusicClock.Instance;
        if(!immediate && clock!=null && (!clock.IsRunning || !clock.HasPlaybackStarted)) return;
        var style=GameSession.Instance!=null ? GameSession.Instance.DetectedGameStyle : GameMusicStyle.Unknown;
        var library=_config!=null ? _config.horizonPalettes : null;
        HorizonPaletteFrame anchor=default;
        HasPalette=library!=null && library.TrySample(style,_config.horizonPaletteAnchorEnergy,out anchor);
        if(!HasPalette) return;
        // Continue transitions while playing; freeze with the same musical clock on pause.
        float te=immediate ? 1f : 1f-Mathf.Exp(-Time.deltaTime/Mathf.Max(.01f,_config.horizonPaletteTransitionSeconds));
        float th=immediate ? 1f : 1f-Mathf.Exp(-Time.deltaTime/Mathf.Max(.01f,_config.horizonPaletteHueSeconds));
        _energy=Mathf.Lerp(_energy,Mathf.Clamp01(energy),te);
        _hueOffset=Mathf.Lerp(_hueOffset,Mathf.Clamp(hueOffset,-1f,1f),th);
        library.TrySample(style,_energy,out var varied);
        float shift=_hueOffset*_config.horizonPaletteHueShift
            +(Mathf.Clamp01(progress)*2f-1f)*_config.horizonPaletteProgressHueDrift;
        varied.zenith=Shift(varied.zenith,shift); varied.upper=Shift(varied.upper,shift);
        varied.lower=Shift(varied.lower,shift); varied.horizon=Shift(varied.horizon,shift);
        varied.water=Shift(varied.water,shift*.6f);
        // The sun never changes; its glow follows only a little.
        varied.sun=anchor.sun;
        varied.glow=Color.Lerp(anchor.glow,varied.glow,_config.horizonPaletteGlowVariation);
        Anchor=anchor;
        Frame=varied;
    }

    /// <summary>Harmonic centre (0..1 on the circle of fifths) → signed -1..1 offset.</summary>
    public static float HarmonicOffset(float hue) => Mathf.Sin(hue*Mathf.PI*2f);

    private static Color Shift(Color c, float amount)
    {
        if(Mathf.Abs(amount)<1e-4f) return c;
        Color.RGBToHSV(c,out float h,out float s,out float v);
        var r=Color.HSVToRGB(Mathf.Repeat(h+amount,1f),s,v,true);
        r.a=c.a;
        return r;
    }
    private void OnDestroy() { if(Instance==this) Instance=null; }
}
