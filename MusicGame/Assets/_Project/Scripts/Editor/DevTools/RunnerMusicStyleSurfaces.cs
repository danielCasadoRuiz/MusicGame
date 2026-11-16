using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class RunnerMusicStyleSurfaces
{
    const string Root="Assets/_Project/Art/RunnerSurfaces/MusicStyles";
    [Serializable] public class Catalog { public Style[] styles; }
    [Serializable] public class Style { public string style;public int styleId;public string atlas;public int firstCell;public string[] variants;public int atlasSize;public string[] layerNames;public string[] variantAtlases; }
    public static string AtlasName(Style item,int variant) => item.variantAtlases!=null && variant<item.variantAtlases.Length && !string.IsNullOrEmpty(item.variantAtlases[variant])?item.variantAtlases[variant]:item.atlas;
    public static System.Collections.Generic.IEnumerable<string> AtlasNames(Style item) => (item.variantAtlases??Array.Empty<string>()).Append(item.atlas).Where(x=>!string.IsNullOrEmpty(x)).Distinct();
    [MenuItem("MusicGame/Runner/Build Musical Style Paths")]
    public static void Build()
    {
        var catalog=JsonUtility.FromJson<Catalog>(File.ReadAllText(Root+"/MusicStyleSurfaces.json"));
        var expected=Enum.GetValues(typeof(GameMusicStyle)).Cast<GameMusicStyle>().Where(x=>x!=GameMusicStyle.Unknown).ToArray();
        if(catalog.styles.Length!=expected.Length || catalog.styles.Select(x=>x.styleId).Distinct().Count()!=expected.Length) throw new Exception("Music style coverage mismatch");
        foreach(var item in catalog.styles) if(!Enum.TryParse<GameMusicStyle>(item.style,out var style) || (int)style!=item.styleId || item.variants.Length!=3) throw new Exception("Invalid musical style: "+item.style);
        AssetDatabase.Refresh();
        foreach(var atlas in catalog.styles.SelectMany(AtlasNames).Distinct())
        {
            var importer=AssetImporter.GetAtPath(Root+"/Atlas_"+atlas+".png") as TextureImporter;
            if(importer==null) throw new Exception("Missing style atlas: "+atlas);
            importer.wrapMode=TextureWrapMode.Clamp;importer.mipmapEnabled=true;importer.anisoLevel=4;importer.maxTextureSize=4096;importer.textureCompression=TextureImporterCompression.Compressed;importer.SaveAndReimport();
        }
        var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,Application.isBatchMode?NewSceneMode.Single:NewSceneMode.Additive);UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        int count=0;
        bool previousAsync=ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation=false;
        try
        {
            var sun=new GameObject("Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.transform.rotation=Quaternion.Euler(45,-25,0);
            var camera=new GameObject("Music Style Camera").AddComponent<Camera>();camera.cullingMask=1<<31;camera.allowHDR=true;camera.orthographic=true;camera.orthographicSize=12.5f;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.075f,.09f,.115f);
            var volume=new GameObject("Preview Bloom").AddComponent<Volume>();volume.isGlobal=true;volume.priority=100;volume.sharedProfile=AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/_Project/Art/RunnerSurfaces/PreviewBloom.asset");
            var skins=AssetDatabase.LoadAssetAtPath<StyleSkinLibrarySO>("Assets/_Project/Configs/UI/StyleSkinLibrary.asset");
            if(skins==null)throw new Exception("Missing actual game skin library");
            var shader=Shader.Find("MusicGame/VertexColorLit");if(shader==null || ShaderUtil.ShaderHasError(shader))throw new Exception("Surface shader failed compilation");
            var html=new StringBuilder("<!doctype html><html lang='ca'><meta charset='utf-8'><title>Camins per estil musical · Tempo Fury</title><style>body{margin:32px;background:#141b25;color:#ecf1f8;font:16px system-ui}a{color:#87cfff}section{margin:44px 0}h2{font-size:24px}.hud{width:100%;max-width:1100px;display:block;margin:16px 0}article{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:16px}figure{margin:0;background:#222e3e;border-radius:10px;overflow:hidden}figure img{width:100%;display:block}figcaption{padding:15px}p{color:#b8c8da;max-width:1000px}nav{display:flex;flex-wrap:wrap;gap:14px;position:sticky;top:0;padding:14px;background:#141b25ef}@media(max-width:850px){article{grid-template-columns:1fr}}</style><h1>Camins per estil musical</h1><p>17 estils del joc · tres combinacions de superfícies per alçada per estil. Els conceptes renovats tenen una identitat pròpia, independent del HUD. Cada camí barreja tres textures amb màscares irregulars segons l’alçada, sobre el mateix camí i amb la mateixa càmera. Propostes de prototip per triar i refinar.</p><nav>");
            foreach(var item in catalog.styles)html.Append("<a href='#"+item.style+"'>"+item.style+"</a>");html.Append("</nav>");
            Directory.CreateDirectory(Root+"/Previews");
            foreach(var item in catalog.styles)
            {
                var style=(GameMusicStyle)item.styleId;Color accent=skins.HudAccent(style);
                // The texture carries the identity; the optional altitude tint is intentionally restrained.
                html.Append("<section id='"+item.style+"'><h2>"+item.style+"</h2>");
                if(item.atlasSize!=2)html.Append("<img class='hud' src='../../UI/RunnerHud/"+item.style+"/bar.png' alt='Referència original "+item.style+"'>");
                html.Append("<article>");
                string directory=Root+"/"+item.style;Directory.CreateDirectory(directory);AssetDatabase.Refresh();
                var atlas=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Atlas_"+item.atlas+".png");
                if(atlas==null)throw new Exception("Atlas missing: "+item.atlas);
                for(int v=0;v<3;v++)
                {
                    string name=item.style+"_"+(v+1);var mat=new Material(shader){name=name};
                    mat.SetTexture("_SurfaceAtlas",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Atlas_"+AtlasName(item,v)+".png"));mat.SetFloat("_SurfaceMode",1);mat.SetVector("_AtlasGrid",new Vector4(item.atlasSize>0?item.atlasSize:3,item.atlasSize>0?item.atlasSize:3,0,0));mat.SetVector("_LayerIndices",new Vector4(item.firstCell+v,0,0,0));
                    mat.SetFloat("_Padding",3);mat.SetFloat("_Meters",v==0?5.2f:v==1?4.6f:5.7f);mat.SetFloat("_AmbientFloor",.78f);mat.SetFloat("_TintStrength",.12f);
                    mat.SetFloat("_CustomHeightPalette",1);mat.SetColor("_LowHeightColor",Color.Lerp(Color.white,accent,.25f));mat.SetColor("_MidHeightColor",Color.white);mat.SetColor("_HighHeightColor",new Color(1,.96f,.87f));
                    mat.SetFloat("_WeatherStrength",.12f);
                    bool glow=style==GameMusicStyle.Electronic || style==GameMusicStyle.Dance || style==GameMusicStyle.Experimental || (style==GameMusicStyle.Energetic && v==1);
                    if(glow){mat.SetFloat("_EmissionPattern",4);mat.SetColor("_SurfaceEmissionColor",accent);mat.SetFloat("_SurfaceEmissionStrength",.8f);}
                    RunnerHeightSurfaceUpgrade.Configure(mat,item,v);
                    string materialPath=directory+"/"+name+".mat";var existing=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if(existing!=null){EditorUtility.CopySerialized(mat,existing);UnityEngine.Object.DestroyImmediate(mat);mat=existing;}else AssetDatabase.CreateAsset(mat,materialPath);
                    var go=new GameObject(name);go.layer=31;go.transform.position=new Vector3(count*22,0,0);go.AddComponent<MeshFilter>().sharedMesh=RunnerSurfaceFamilies.PathMesh();go.AddComponent<MeshRenderer>().sharedMaterial=mat;
                    camera.transform.position=go.transform.position+new Vector3(12,19,-19);camera.transform.LookAt(go.transform.position+Vector3.up*.5f);camera.GetUniversalAdditionalCameraData().renderPostProcessing=glow;
                    var rt=RenderTexture.GetTemporary(1100,900,24,RenderTextureFormat.DefaultHDR);var previousRT=RenderTexture.active;
                    try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var tex=new Texture2D(1100,900,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1100,900),0,0);
                        // HDR camera targets contain linear values; PNG viewers expect sRGB.
                        if(QualitySettings.activeColorSpace==ColorSpace.Linear){var pixels=tex.GetPixels();for(int p=0;p<pixels.Length;p++)pixels[p]=pixels[p].gamma;tex.SetPixels(pixels);}
                        tex.Apply();File.WriteAllBytes(Root+"/Previews/"+name+".png",tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);}
                    finally{camera.targetTexture=null;RenderTexture.active=previousRT;RenderTexture.ReleaseTemporary(rt);go.layer=0;}
                    var used=mat.GetVector("_LayerIndices");
                    string legend="Baix: "+(item.layerNames??item.variants)[(int)used.x-item.firstCell]+" · Mig: "+(item.layerNames??item.variants)[(int)used.y-item.firstCell]+" · Dalt: "+(item.layerNames??item.variants)[(int)used.z-item.firstCell];
                    html.Append("<figure><a href='Previews/"+name+".png'><img loading='lazy' src='Previews/"+name+".png' alt='"+legend+"'></a><figcaption>Variant "+(v+1)+"<br>"+legend+"</figcaption></figure>");count++;
                }
                html.Append("</article></section>");
            }
            html.Append("</html>");File.WriteAllText(Root+"/Galeria.html",html.ToString(),Encoding.UTF8);camera.cullingMask=-1;AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene,Root+"/MusicalStylePaths.unity");
            File.WriteAllText(Root+"/Validation.txt","Validated all "+expected.Length+" GameMusicStyle entries (excluding Unknown), compiled shader and rendered "+count+" distinct textured path proposals. "+DateTime.UtcNow.ToString("O"));
        }
        finally{ShaderUtil.allowAsyncCompilation=previousAsync;if(!Application.isBatchMode){EditorSceneManager.CloseScene(scene,true);UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);}}
    }
}
