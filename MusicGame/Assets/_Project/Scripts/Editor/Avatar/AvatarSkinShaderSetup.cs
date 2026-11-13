using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Rebuilds the unified forward skin shader from the installed, masked URP Lit shader.</summary>
public static class AvatarSkinShaderSetup
{
    public const string Folder="Assets/_Project/Shaders/Avatar/";
    public const string ShaderPath=Folder+"AvatarUnifiedSkin.shader";
    public const string ShaderName="MusicGame/Avatar/Unified Skin";
    public const string Properties=@"
        [Header(Nail polish)]
        [Enum(Natural,0,Groups,1,Individual,2)] _NailMode(""Nails"", Float) = 0
        [Toggle] _NailGroup0Enabled(""Paint left hand"", Float) = 0
        _NailGroupColor0(""Left hand color"", Color) = (1,1,1,1)
        [Toggle] _NailGroup1Enabled(""Paint right hand"", Float) = 0
        _NailGroupColor1(""Right hand color"", Color) = (1,1,1,1)
        [Toggle] _NailGroup2Enabled(""Paint left foot"", Float) = 0
        _NailGroupColor2(""Left foot color"", Color) = (1,1,1,1)
        [Toggle] _NailGroup3Enabled(""Paint right foot"", Float) = 0
        _NailGroupColor3(""Right foot color"", Color) = (1,1,1,1)

        _NailColor0(""Left hand 1"", Color) = (0,0,0,0)
        _NailColor1(""Left hand 2"", Color) = (0,0,0,0)
        _NailColor2(""Left hand 3"", Color) = (0,0,0,0)
        _NailColor3(""Left hand 4"", Color) = (0,0,0,0)
        _NailColor4(""Left hand 5"", Color) = (0,0,0,0)
        _NailColor5(""Right hand 1"", Color) = (0,0,0,0)
        _NailColor6(""Right hand 2"", Color) = (0,0,0,0)
        _NailColor7(""Right hand 3"", Color) = (0,0,0,0)
        _NailColor8(""Right hand 4"", Color) = (0,0,0,0)
        _NailColor9(""Right hand 5"", Color) = (0,0,0,0)
        _NailColor10(""Left foot 1"", Color) = (0,0,0,0)
        _NailColor11(""Left foot 2"", Color) = (0,0,0,0)
        _NailColor12(""Left foot 3"", Color) = (0,0,0,0)
        _NailColor13(""Left foot 4"", Color) = (0,0,0,0)
        _NailColor14(""Left foot 5"", Color) = (0,0,0,0)
        _NailColor15(""Right foot 1"", Color) = (0,0,0,0)
        _NailColor16(""Right foot 2"", Color) = (0,0,0,0)
        _NailColor17(""Right foot 3"", Color) = (0,0,0,0)
        _NailColor18(""Right foot 4"", Color) = (0,0,0,0)
        _NailColor19(""Right foot 5"", Color) = (0,0,0,0)
        [Header(Skin tone)]
        [Toggle] _SkinToneEnabled(""Enable skin tone"", Float) = 1
        _SkinTone(""Skin tone (light to dark)"", Range(0,1)) = 0.33333333
        _SkinUndertone(""Undertone (cool to warm)"", Range(-1,1)) = 0
        [NoScaleOffset] _SkinRegionMap(""Skin / lip / palm regions"", 2D) = ""black"" {}
        [Header(Soft skin lighting)]
        _SkinSoftness(""Soft diffuse amount"", Range(0,0.6)) = 0.25
        _SkinWrap(""Diffuse wrap"", Range(0,0.5)) = 0.3
        _SkinScatterColor(""Diffuse warmth"", Color) = (1,0.55,0.38,1)
";
    [MenuItem("Tools/MusicGame/Avatars/Surface/Rebuild Unified Skin Shader")]
    public static void Regenerate()
    {
        AvatarSurfaceSetup.RebuildInput();
        string source=File.ReadAllText(SkinVisibilityShaderSetup.ShaderPath).Replace("\r\n","\n");
        source=source.Replace("Shader \"MusicGame/Avatar/Skin Visibility Lit\"","Shader \""+ShaderName+"\"")
            .Replace("        // Specular vs Metallic workflow",Properties+"\n        // Specular vs Metallic workflow")
            .Replace("\"AvatarSurfaceInput.hlsl\"","\"AvatarUnifiedSkinInput.hlsl\"")
            .Replace("\"LightMode\" = \"UniversalForward\"","\"LightMode\" = \"UniversalForwardOnly\"")
            .Replace("#pragma target 2.0","#pragma target 3.5");
        int label=source.IndexOf("Name \"GBuffer\"",StringComparison.Ordinal);
        if(label>=0)
        {
            int start=source.LastIndexOf("        Pass",label,StringComparison.Ordinal);
            int brace=source.IndexOf('{',start),depth=1,end=brace+1;
            while(depth>0&&end<source.Length){if(source[end]=='{')depth++;if(source[end]=='}')depth--;end++;}
            source=source.Remove(start,end-start);
        }
        const string pass="#include \"Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl\"";
        if(!source.Contains(pass))throw new InvalidOperationException("Forward shader patch point changed.");
        source=source.Replace(pass,"#include \"AvatarSkinLighting.hlsl\"\n"+
            "            #define UniversalFragmentPBR(data,surface) AvatarSkinFragmentPBR(data,surface,input.uv)\n"+
            "            "+pass+"\n            #undef UniversalFragmentPBR");
        string input=File.ReadAllText(Folder+"AvatarSurfaceInput.hlsl").Replace("\r\n","\n")
            .Replace("#include \"AvatarIrisParameters.hlsl\"","#include \"AvatarIrisParameters.hlsl\"\n#include \"AvatarSkinToneParameters.hlsl\"")
            .Replace("#include \"AvatarIrisSurface.hlsl\"","#define InitializeStandardLitSurfaceData InitializeAvatarIrisSurfaceData\n"+
                "#include \"AvatarIrisSurface.hlsl\"\n#undef InitializeStandardLitSurfaceData\n#include \"AvatarSkinToneSurface.hlsl\"");
        File.WriteAllText(Folder+"AvatarUnifiedSkinInput.hlsl",input);
        File.WriteAllText(ShaderPath,source);
        AssetDatabase.ImportAsset(Folder+"AvatarUnifiedSkinInput.hlsl",ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(ShaderPath,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
    }

    [MenuItem("Tools/MusicGame/Avatars/Surface/Calibrate Iris Textures")]
    public static void CalibrateIrisTextures()
    {
        const string path="Assets/_Project/Resources/Avatar/IrisCalibration.asset";
        var library=AssetDatabase.LoadAssetAtPath<AvatarIrisTextureLibrary>(path);
        if(library==null){library=ScriptableObject.CreateInstance<AvatarIrisTextureLibrary>();AssetDatabase.CreateAsset(library,path);}
        var entries=new List<AvatarIrisTextureLibrary.Entry>();
        foreach(var texture in Resources.LoadAll<Texture2D>("Avatar/Iris"))
        {
            var previous=RenderTexture.active;
            var rt=RenderTexture.GetTemporary(texture.width,texture.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            Texture2D copy=null;
            try
            {
                Graphics.Blit(texture,rt);RenderTexture.active=rt;
                copy=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);
                copy.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);copy.Apply();
                var pixels=copy.GetPixels32();int w=texture.width,h=texture.height,minx=w,miny=h,maxx=0,maxy=0;
                for(int y=1;y<h-1;y++)for(int x=1;x<w-1;x++)if(pixels[y*w+x].a>127)
                    {minx=Mathf.Min(minx,x);maxx=Mathf.Max(maxx,x);miny=Mathf.Min(miny,y);maxy=Mathf.Max(maxy,y);}
                double cx=(minx+maxx+1)*.5/w,cy=(miny+maxy+1)*.5/h,outerRadius=(maxx-minx+maxy-miny+2)*.25/w;
                var outer=new List<Vector2>();var pupil=new List<Vector2>();
                for(int y=1;y<h-1;y++)for(int x=1;x<w-1;x++)
                {
                    int k=y*w+x;if(pixels[k].a<=127)continue;
                    if(pixels[k-1].a>127&&pixels[k+1].a>127&&pixels[k-w].a>127&&pixels[k+w].a>127)continue;
                    var point=new Vector2((x+.5f)/w,(y+.5f)/h);
                    double distance=Math.Sqrt(Math.Pow(point.x-cx,2)+Math.Pow(point.y-cy,2));
                    (distance>outerRadius*.65?outer:pupil).Add(point);
                }
                entries.Add(new AvatarIrisTextureLibrary.Entry{texture=texture,outer=FitCircle(outer),pupil=FitCircle(pupil)});
            }
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);if(copy!=null)UnityEngine.Object.DestroyImmediate(copy);}
        }
        library.entries=entries.ToArray();EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
    }
    private static Vector4 FitCircle(List<Vector2> points)
    {
        if(points.Count<12)throw new InvalidOperationException("Iris source has no usable alpha annulus.");
        var m=new double[3,4];
        foreach(var p in points){double[] row={p.x,p.y,1};double z=p.x*p.x+p.y*p.y;
            for(int i=0;i<3;i++){for(int j=0;j<3;j++)m[i,j]+=row[i]*row[j];m[i,3]+=row[i]*z;}}
        for(int i=0;i<3;i++){int pivot=i;for(int k=i+1;k<3;k++)if(Math.Abs(m[k,i])>Math.Abs(m[pivot,i]))pivot=k;
            for(int j=i;j<4;j++){double t=m[i,j];m[i,j]=m[pivot,j];m[pivot,j]=t;}
            double scale=m[i,i];if(Math.Abs(scale)<1e-12)throw new InvalidOperationException("Degenerate iris circle.");
            for(int j=i;j<4;j++)m[i,j]/=scale;
            for(int k=0;k<3;k++)if(k!=i){double f=m[k,i];for(int j=i;j<4;j++)m[k,j]-=f*m[i,j];}}
        double x=m[0,3]*.5,y=m[1,3]*.5;
        return new Vector4((float)x,(float)y,(float)Math.Sqrt(Math.Max(0,m[2,3]+x*x+y*y)),0);
    }
}
