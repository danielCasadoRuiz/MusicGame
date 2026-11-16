using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class RunnerSurfaceFamilies
{
    const string Root="Assets/_Project/Art/RunnerSurfaces";
    [MenuItem("MusicGame/Runner/Build Path Families")]
    public static void Build()
    {
        var sources=Directory.GetFiles(Root,"*.mat").OrderBy(p=>p).ToArray();
        if(sources.Length==0) throw new Exception("Build Surface Examples first");
        var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,Application.isBatchMode?NewSceneMode.Single:NewSceneMode.Additive);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        string folder=Root+"/Families";Directory.CreateDirectory(folder);Directory.CreateDirectory(folder+"/Previews");AssetDatabase.Refresh();
        int count=0;
        try
        {
            var sun=new GameObject("Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.transform.rotation=Quaternion.Euler(45,-25,0);
            var camera=new GameObject("Path Preview Camera").AddComponent<Camera>();camera.cullingMask=1<<31;camera.allowHDR=true;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.045f,.055f,.07f);camera.orthographic=true;camera.orthographicSize=12.5f;
            var volume=new GameObject("Preview Bloom").AddComponent<Volume>();volume.isGlobal=true;volume.priority=100;volume.sharedProfile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root+"/PreviewBloom.asset");
            string[] variants={"A_Continu","B_Recuperat","C_Expressiu"};
            string[] descriptions={"Continu · cobertura densa i detall contingut","Recuperat · desgast, clapes i irregularitat","Expressiu · contrast, escala i llum"};
            var html=new StringBuilder("<!doctype html><html lang='ca'><meta charset='utf-8'><title>Famílies de camins</title><style>body{margin:32px;background:#131922;color:#e8eef6;font:16px system-ui}a{color:#87cfff}section{margin:40px 0}h2{font-size:21px}article{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:16px}figure{margin:0;background:#202b38;border-radius:12px;overflow:hidden}img{width:100%;display:block}figcaption{padding:14px}p{color:#b8c6d8}nav{display:flex;flex-wrap:wrap;gap:12px}@media(max-width:850px){article{grid-template-columns:1fr}}</style><h1>Proves de superfície</h1><p>Aquesta col·lecció agrupa acabats genèrics. <a href='../MusicStyles/Galeria.html'>Obre els camins per estil musical del joc.</a></p><p>Tres interpretacions per cada acabat. Mateix camí corbat i mateixa càmera per comparar superfícies. Presets de revisió; no s’activen tots alhora al joc.</p><nav>");
            foreach(var file in sources){string name=Path.GetFileNameWithoutExtension(file);html.Append("<a href='#"+name+"'>"+name+"</a>");}html.Append("</nav>");
            foreach(var file in sources)
            {
                string family=Path.GetFileNameWithoutExtension(file);var source=AssetDatabase.LoadAssetAtPath<Material>(file.Replace('\\','/'));
                if(source==null) continue;
                html.Append("<section id='"+family+"'><h2>"+family+"</h2><article>");
                for(int variant=0;variant<3;variant++)
                {
                    var mat=new Material(source);Configure(mat,variant);
                    string name=family+"_"+variants[variant];string path=folder+"/"+name+".mat";
                    var existing=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(existing!=null){EditorUtility.CopySerialized(mat,existing);UnityEngine.Object.DestroyImmediate(mat);mat=existing;}else AssetDatabase.CreateAsset(mat,path);
                    if(ShaderUtil.ShaderHasError(mat.shader)) throw new Exception("Shader error: "+name);
                    var go=new GameObject(name);go.layer=31;go.transform.position=new Vector3(count*22,0,0);
                    go.AddComponent<MeshFilter>().sharedMesh=PathMesh();go.AddComponent<MeshRenderer>().sharedMaterial=mat;
                    camera.transform.position=go.transform.position+new Vector3(12,19,-19);camera.transform.LookAt(go.transform.position+Vector3.up*.5f);
                    camera.GetUniversalAdditionalCameraData().renderPostProcessing=mat.GetFloat("_SurfaceEmissionStrength")>0;
                    var rt=RenderTexture.GetTemporary(1100,900,24,RenderTextureFormat.DefaultHDR);var before=RenderTexture.active;
                    try
                    {
                        camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                        var tex=new Texture2D(1100,900,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1100,900),0,0);tex.Apply();File.WriteAllBytes(folder+"/Previews/"+name+".png",tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);
                    }
                    finally{camera.targetTexture=null;RenderTexture.active=before;RenderTexture.ReleaseTemporary(rt);go.layer=0;}
                    html.Append("<figure><a href='Previews/"+name+".png'><img loading='lazy' src='Previews/"+name+".png'></a><figcaption>"+descriptions[variant]+"</figcaption></figure>");count++;
                }
                html.Append("</article></section>");
            }
            html.Append("</html>");File.WriteAllText(folder+"/Galeria.html",html.ToString(),Encoding.UTF8);
            camera.cullingMask=-1;AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene,folder+"/RunnerPathFamilies.unity");
            File.WriteAllText(folder+"/Validation.txt","Compiled and rendered "+count+" path variants from "+sources.Length+" families. "+DateTime.UtcNow.ToString("O"));
        }
        finally{if(!Application.isBatchMode){EditorSceneManager.CloseScene(scene,true);UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);}}
    }
    static void Configure(Material m,int variant)
    {
        int mode=Mathf.RoundToInt(m.GetFloat("_SurfaceMode"));bool emissive=m.GetFloat("_SurfaceEmissionStrength")>0;
        m.SetFloat("_Meters",m.GetFloat("_Meters")*(variant==0?.85f:variant==1?1.05f:1.4f));
        m.SetFloat("_WeatherStrength",variant==0?.15f:variant==1?.85f:.45f);
        if(mode==3)
        {
            m.SetVector("_PieceScale",variant==0?new Vector4(.98f,1.02f,0,0):variant==1?new Vector4(.82f,1.13f,0,0):new Vector4(.9f,1.3f,0,0));
            m.SetFloat("_MissingPieces",variant==0?0:variant==1?.1f:.035f);m.SetFloat("_PieceJitter",variant==0?.015f:variant==1?.11f:.07f);
            m.SetFloat("_EdgeRoughness",variant==0?.01f:.055f);m.SetFloat("_ColorVariation",variant==0?.07f:.22f);
        }
        if(mode==2)
        {
            m.SetFloat("_PatchSize",variant==0?3.5f:variant==1?1.1f:2.2f);m.SetFloat("_BlendWidth",variant==0?.25f:variant==1?.055f:.12f);
            m.SetFloat("_PatchAmount",variant==0?.08f:variant==1?.32f:.2f);m.SetFloat("_UVWarp",variant==0?.01f:.09f);
            if(variant==1){m.SetVector("_MidWeights",new Vector4(.25f,.5f,.25f,0));m.SetVector("_HighWeights",new Vector4(.55f,.25f,.2f,0));}
        }
        if(variant==2 && !emissive)
        {
            bool hasCustomPalette=m.GetFloat("_CustomHeightPalette")>.5f;
            m.SetFloat("_CustomHeightPalette",1);m.SetFloat("_TintStrength",.55f);
            if(!hasCustomPalette){m.SetColor("_LowHeightColor",new Color(.25f,.39f,.37f));m.SetColor("_MidHeightColor",new Color(.85f,.61f,.37f));m.SetColor("_HighHeightColor",new Color(.98f,.9f,.7f));}
            m.SetVector("_TintTransitions",new Vector4(.28f,.22f,.72f,.28f));m.SetFloat("_TintNoise",.16f);
        }
        if(emissive)
        {
            m.SetFloat("_SurfaceEmissionStrength",variant==0?1.8f:variant==1?3:6);
            m.SetFloat("_EmissionPattern",variant==0?1:variant==1?4:m.GetFloat("_EmissionPattern"));
            m.SetFloat("_EmissionScale",variant==2?2.5f:1.4f);m.SetFloat("_EmissionWidth",variant==2?.022f:.009f);
            m.SetFloat("_EmissionPulse",variant==2?.4f:0);
        }
    }
    public static Mesh PathMesh()
    {
        string path=Root+"/Families/CurvedPath.asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing!=null)return existing;
        const int rows=161,cols=33;var vertices=new Vector3[rows*cols];var uv=new Vector2[vertices.Length];var surface=new Vector3[vertices.Length];var colors=new Color[vertices.Length];var triangles=new int[(rows-1)*(cols-1)*6];int t=0;float distance=0;Vector3 previous=Vector3.zero;
        for(int r=0;r<rows;r++)
        {
            float b=r/(float)(rows-1);float z=(b-.5f)*27;float x=Mathf.Sin(b*Mathf.PI*2)*2.2f;
            var centre=new Vector3(x,0,z);if(r>0)distance+=Vector3.Distance(centre,previous);previous=centre;
            var tangent=new Vector3(2.2f*2*Mathf.PI/27*Mathf.Cos(b*Mathf.PI*2),0,1).normalized;var right=new Vector3(tangent.z,0,-tangent.x);
            for(int c=0;c<cols;c++)
            {
                int k=r*cols+c;float a=c/(float)(cols-1), lateral=(a-.5f)*7.2f;
                float h=Mathf.Clamp01(.15f+b*.65f+Mathf.Sin(a*7+b*12)*.17f);
                vertices[k]=centre+right*lateral+Vector3.up*(h*1.5f);uv[k]=new Vector2(a,b);surface[k]=new Vector3(lateral,distance,h);
                colors[k]=h<.5f?Color.Lerp(new Color(.25f,.7f,.3f),new Color(.95f,.8f,.35f),h*2):Color.Lerp(new Color(.95f,.8f,.35f),new Color(.8f,.25f,.15f),(h-.5f)*2);
                if(r<rows-1 && c<cols-1){triangles[t++]=k;triangles[t++]=k+cols;triangles[t++]=k+1;triangles[t++]=k+1;triangles[t++]=k+cols;triangles[t++]=k+cols+1;}
            }
        }
        var mesh=new Mesh();mesh.vertices=vertices;mesh.uv=uv;mesh.SetUVs(1,surface);mesh.colors=colors;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);return mesh;
    }
}
