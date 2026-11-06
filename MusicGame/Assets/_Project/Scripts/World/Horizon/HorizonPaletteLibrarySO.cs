using System;
using UnityEngine;

[Serializable]
public struct HorizonPaletteFrame
{
    public Color zenith, upper, lower, horizon, glow, sun;
    public Color ledLow, ledMid, ledHigh, water;
    public static HorizonPaletteFrame Lerp(HorizonPaletteFrame a, HorizonPaletteFrame b, float t) => new()
    {
        zenith=Color.Lerp(a.zenith,b.zenith,t), upper=Color.Lerp(a.upper,b.upper,t),
        lower=Color.Lerp(a.lower,b.lower,t), horizon=Color.Lerp(a.horizon,b.horizon,t),
        glow=Color.Lerp(a.glow,b.glow,t), sun=Color.Lerp(a.sun,b.sun,t),
        ledLow=Color.Lerp(a.ledLow,b.ledLow,t), ledMid=Color.Lerp(a.ledMid,b.ledMid,t),
        ledHigh=Color.Lerp(a.ledHigh,b.ledHigh,t), water=Color.Lerp(a.water,b.water,t)
    };
    public Color LED(float amplitude) => amplitude < .5f
        ? Color.Lerp(ledLow,ledMid,amplitude*2f) : Color.Lerp(ledMid,ledHigh,(amplitude-.5f)*2f);
}

[CreateAssetMenu(menuName="MusicGame/Environment/Horizon Palettes",fileName="HorizonPalettes")]
public class HorizonPaletteLibrarySO : ScriptableObject
{
    [Serializable] public class Entry
    {
        public GameMusicStyle style;
        public HorizonPaletteFrame calm, buildup, peak;
    }
    public GameMusicStyle fallbackStyle = GameMusicStyle.Cinematic;
    public Entry[] palettes;

    public bool TrySample(GameMusicStyle style, float energy, out HorizonPaletteFrame frame)
    {
        Entry selected=null, fallback=null;
        if(palettes!=null) foreach(var entry in palettes)
        {
            if(entry.style==style) selected=entry;
            if(entry.style==fallbackStyle) fallback=entry;
        }
        selected ??= fallback;
        if(selected==null) { frame=default; return false; }
        float t=Mathf.SmoothStep(0,1,Mathf.Clamp01(energy<.5f?energy*2:(energy-.5f)*2));
        frame=energy<.5f ? HorizonPaletteFrame.Lerp(selected.calm,selected.buildup,t)
            : HorizonPaletteFrame.Lerp(selected.buildup,selected.peak,t);
        return true;
    }
}

