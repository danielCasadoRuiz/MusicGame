using TMPro;
using UnityEngine;

/// <summary>The title is the preview indicator; its original font and atlas stay intact.</summary>
public sealed class SongTitlePreviewProgress : MonoBehaviour
{
    public TextMeshProUGUI title;
    public Shader progressShader;
    public bool reviewPlaying;
    [Range(0,1)] public float reviewProgress=.43f;
    public float Progress { get; private set; }
    public float GlowAmount { get; private set; }
    public float SweepShear { get; private set; }
    public Vector2 SweepBounds { get; private set; }
    Material original,instance;
    string lastId;
    bool wasPlaying;
    float fadeTime;
    const float ReturnSeconds=.9f;

    public void Render(Color glow)
    {
        if(title==null||progressShader==null)return;
        if(instance==null)
        {
            original=title.fontSharedMaterial;
            instance=new Material(original){shader=progressShader,name="Song title preview instance",hideFlags=HideFlags.DontSave};
            instance.EnableKeyword("UNDERLAY_ON");
            instance.SetColor("_UnderlayColor",Color.clear);
            instance.SetFloat("_UnderlaySoftness",.5f);
            instance.SetFloat("_UnderlayDilate",.15f);
            title.fontSharedMaterial=instance;
        }
        var player=SongPreviewPlayer.Instance;
        bool playing=player!=null&&player.IsPlaying;
        if(reviewPlaying){Progress=reviewProgress;GlowAmount=1;}
        else if(playing)
        {
            lastId=player.PlayingId;wasPlaying=true;fadeTime=0;
            Progress=player.Progress;GlowAmount=1;
        }
        else if(player!=null&&player.CompletedId!=null&&player.CompletedId==lastId)
        {
            if(wasPlaying){fadeTime=0;wasPlaying=false;}
            Progress=1;fadeTime+=Time.unscaledDeltaTime;
            GlowAmount=1-Mathf.SmoothStep(0,1,fadeTime/ReturnSeconds);
        }
        else{Progress=0;GlowAmount=0;wasPlaying=false;lastId=null;fadeTime=0;}
        title.ForceMeshUpdate();
        UpdateSweepCoordinates();
        instance.SetVector("_PreviewBounds",new Vector4(SweepBounds.x,SweepBounds.y,0,0));
        instance.SetFloat("_PreviewProgress",Progress);
        instance.SetFloat("_PreviewAmount",GlowAmount);
        instance.SetColor("_PreviewGlow",glow);
    }

    void UpdateSweepCoordinates()
    {
        // Barlow Black Italic has a native seven-degree lean; TMP may additionally shear its quads.
        float nativeShear=Mathf.Tan(7*Mathf.Deg2Rad),meshShear=0;
        var info=title.textInfo;
        for(int i=0;i<info.characterCount;i++)
        {
            var c=info.characterInfo[i];if(!c.isVisible)continue;
            float height=c.topLeft.y-c.bottomLeft.y;
            if(height>0)meshShear=(c.topLeft.x-c.bottomLeft.x)/height;
            break;
        }
        SweepShear=meshShear+nativeShear;
        float left=float.PositiveInfinity,right=float.NegativeInfinity;
        for(int i=0;i<info.characterCount;i++)
        {
            var c=info.characterInfo[i];if(!c.isVisible)continue;
            var mesh=info.meshInfo[c.materialReferenceIndex];
            for(int corner=0;corner<4;corner++)
            {
                int index=c.vertexIndex+corner;var vertex=mesh.vertices[index];
                float projected=vertex.x-SweepShear*vertex.y;
                var uv=mesh.uvs2[index];uv.x=projected;mesh.uvs2[index]=uv;
            }
            // Exclude the transparent SDF/halo padding: it must not consume preview time.
            var glyph=c.textElement.glyph;var atlas=c.fontAsset.atlasTextures[glyph.atlasIndex];
            var rect=glyph.glyphRect;int first=c.vertexIndex;
            var uvMin=mesh.uvs0[first];var uvMax=mesh.uvs0[first+2];
            float u0=(rect.x/(float)atlas.width-uvMin.x)/Mathf.Max(.000001f,uvMax.x-uvMin.x);
            float u1=((rect.x+rect.width)/(float)atlas.width-uvMin.x)/Mathf.Max(.000001f,uvMax.x-uvMin.x);
            float v0=(rect.y/(float)atlas.height-uvMin.y)/Mathf.Max(.000001f,uvMax.y-uvMin.y);
            float v1=((rect.y+rect.height)/(float)atlas.height-uvMin.y)/Mathf.Max(.000001f,uvMax.y-uvMin.y);
            for(int corner=0;corner<4;corner++)
            {
                float u=corner>=2?u1:u0,v=corner==1||corner==2?v1:v0;
                var bottom=Vector3.LerpUnclamped(mesh.vertices[first],mesh.vertices[first+3],u);
                var top=Vector3.LerpUnclamped(mesh.vertices[first+1],mesh.vertices[first+2],u);
                var ink=Vector3.LerpUnclamped(bottom,top,v);
                float projected=ink.x-SweepShear*ink.y;
                left=Mathf.Min(left,projected);right=Mathf.Max(right,projected);
            }
        }
        SweepBounds=float.IsInfinity(left)?new Vector2(0,1):new Vector2(left,right);
        if(title.canvas!=null)title.canvas.additionalShaderChannels|=AdditionalCanvasShaderChannels.TexCoord1;
        // UVs survive Canvas batching transforms; POSITION does not remain in title-local coordinates.
        title.UpdateVertexData(TMP_VertexDataUpdateFlags.Uv2);
    }

    void OnDisable(){wasPlaying=false;lastId=null;Progress=0;GlowAmount=0;if(instance!=null)instance.SetFloat("_PreviewAmount",0);}
    void OnDestroy()
    {
        if(instance==null)return;
        if(title!=null&&title.fontSharedMaterial==instance)title.fontSharedMaterial=original;
        if(Application.isPlaying)Destroy(instance);else DestroyImmediate(instance);
    }
}
