using UnityEngine;
using UnityEngine.UI;

/// <summary>Lightweight mirrored spectrum for the selected song; one UI mesh and a reused FFT buffer.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SongMusicBars : MaskableGraphic
{
    public bool reviewPlaying;
    public Color glowColor=new Color(1,.12f,.7f,1);
    public Material neonMaterial;
    public int BarCount=>levels.Length*2;
    readonly float[] spectrum=new float[256];
    readonly float[] levels=new float[16];
    protected override void OnEnable(){base.OnEnable();if(neonMaterial!=null)material=neonMaterial;SongNeonStroke.EnableChannels(this);}
    void Update()
    {
        var player=SongPreviewPlayer.Instance;
        bool playing=player!=null&&player.IsPlaying&&player.ReadSpectrum(spectrum);
        // Hold the last audible spectrum through the fade and after completion.
        if(!reviewPlaying&&(!playing||player.RemainingSeconds<.8f))return;
        for(int i=0;i<levels.Length;i++)
        {
            int band=Mathf.Clamp((int)Mathf.Pow(1.40f,i),1,255);
            float strength=playing?Mathf.Sqrt(spectrum[band])*5:0;
            float target=strength/(1+strength);
            if(reviewPlaying)target=.12f+.7f*Mathf.Abs(Mathf.Sin(i*.7f+Time.realtimeSinceStartup*3))*Mathf.Sin((i+1f)/(levels.Length+1)*Mathf.PI);
            levels[i]=Mathf.Lerp(levels[i],target,1-Mathf.Exp(-16*Time.unscaledDeltaTime));
        }
        SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();var r=GetPixelAdjustedRect();int count=levels.Length*2;
        float pitch=r.width/count;
        for(int i=0;i<count;i++)
        {
            int index=i<levels.Length?levels.Length-1-i:i-levels.Length;
            float height=3+levels[index]*(r.height-3);
            if(reviewPlaying&&height<5)
            {
                float centre=(i-count*.5f)/(count*.5f);
                height=4+(Mathf.Pow(.5f+.5f*Mathf.Sin(i*.34f),2)*.65f+.22f)*r.height*Mathf.Exp(-centre*centre*3);
            }
            float width=pitch*.36f,left=r.xMin+(i+.5f)*pitch-width*.5f,bottom=r.center.y-height*.5f;
            SongNeonStroke.Quad(mesh,new Rect(left,bottom,width,height),16,0,glowColor);
        }
    }
}
