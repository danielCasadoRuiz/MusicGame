using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class RunnerSurfaceExamples
{
    const string Root="Assets/_Project/Art/RunnerSurfaces";
    [MenuItem("MusicGame/Runner/Build Surface Examples")]
    public static void Build()
    {
        var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        try
        {
            AssetDatabase.Refresh();
            foreach(var file in new[]{"SurfaceAtlas.png","PieceAtlas.png","SpaceAtlas.png"})
            {
                var imp=AssetImporter.GetAtPath(Root+"/"+file) as TextureImporter;
                if(imp==null) continue;
                imp.wrapMode=TextureWrapMode.Clamp; imp.mipmapEnabled=true;
                imp.maxTextureSize=4096; imp.textureCompression=TextureImporterCompression.Uncompressed;
                imp.anisoLevel=4; imp.SaveAndReimport();
            }
            var shader=Shader.Find("MusicGame/VertexColorLit");
            if(shader==null || UnityEditor.ShaderUtil.ShaderHasError(shader)) throw new Exception("Terrain shader compilation failed");
            var atlas=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/SurfaceAtlas.png");
            var pieces=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/PieceAtlas.png");
            var space=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/SpaceAtlas.png");
            string[] names={"01_VertexColor","02_Sand","03_Paving","04_Grass","05_Earth","06_TintedPaving","07_GrassEarthSand","08_TintedHeightMix","09_BuriedPaving","10_PieceVariants","11_OverlappingPieces","12_RuinedCourtyard","13_OldWarmMosaic","14_StormPaving","15_OvergrownRuins","16_DesertRemains","17_MossOchreChalk","18_TwilightSlate","19_AutumnTrail","20_ThreeColourBands","21_IndustrialDeck","22_CyanCircuitDeck","23_AlienTemple","24_WhiteStation","25_ReactorGrid","26_HyperspaceRails"};
            var light=new GameObject("Sun").AddComponent<Light>(); light.type=LightType.Directional; light.transform.rotation=Quaternion.Euler(45,-25,0);
            var camera=new GameObject("Preview Camera").AddComponent<Camera>(); camera.backgroundColor=new Color(.075f,.09f,.11f);camera.clearFlags=CameraClearFlags.SolidColor;
            camera.cullingMask=1<<31;
            camera.orthographic=true;camera.orthographicSize=6.7f;camera.nearClipPlane=.1f;camera.farClipPlane=100;
            camera.allowHDR=true;
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root+"/PreviewBloom.asset");
            if(profile==null) {profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,Root+"/PreviewBloom.asset");}
            Bloom bloom;if(!profile.TryGet<Bloom>(out bloom)) {bloom=profile.Add<Bloom>(true);AssetDatabase.AddObjectToAsset(bloom,profile);}
            bloom.threshold.Override(1);bloom.intensity.Override(.65f);bloom.scatter.Override(.65f);
            EditorUtility.SetDirty(profile);
            var volume=new GameObject("Preview Bloom").AddComponent<Volume>();volume.isGlobal=true;volume.sharedProfile=profile;volume.priority=100;
            Directory.CreateDirectory(Root+"/Previews");
            for(int i=0;i<names.Length;i++)
            {
                var mat=new Material(shader); mat.SetTexture("_SurfaceAtlas",atlas);mat.SetVector("_AtlasGrid",new Vector4(2,2,0,0));
                mat.SetFloat("_SurfaceMode",1);mat.SetFloat("_TintStrength",0);mat.SetFloat("_AmbientFloor",.65f);mat.SetFloat("_Meters",3);mat.SetFloat("_Padding",4);
                int[] indices={0,3,2,0,1,2};
                mat.SetVector("_LayerIndices",new Vector4(indices[Math.Min(i,5)],1,3,0));
                if(i==0) mat.SetFloat("_SurfaceMode",0);
                if(i==5) mat.SetFloat("_TintStrength",.55f);
                if(i>=6 && i<=8)
                {
                    mat.SetFloat("_SurfaceMode",2);mat.SetFloat("_PatchSize",2);mat.SetFloat("_PatchAmount",.15f);mat.SetFloat("_BlendWidth",.13f);
                    mat.SetVector("_LayerIndices",new Vector4(0,1,3,0));
                    if(i==7) mat.SetFloat("_TintStrength",.5f);
                    if(i==8)
                    {
                        mat.SetVector("_LayerIndices",new Vector4(2,3,0,0));
                        mat.SetVector("_LowWeights",new Vector4(.1f,.9f,0,0));mat.SetVector("_MidWeights",new Vector4(.45f,.45f,.1f,0));mat.SetVector("_HighWeights",new Vector4(.7f,.05f,.25f,0));
                    }
                }
                if(i==9 || i==10)
                {
                    if(pieces==null) throw new Exception("Piece atlas missing");
                    mat.SetTexture("_SurfaceAtlas",pieces);mat.SetVector("_AtlasGrid",new Vector4(8,8,0,0));mat.SetFloat("_SurfaceMode",3);mat.SetFloat("_Meters",.9f);
                    mat.SetVector("_PieceScale", i==9 ? new Vector4(.98f,.98f,0,0) : new Vector4(.7f,1.4f,0,0));
                    mat.SetFloat("_EdgeRoughness",i==9?0:.025f);
                }
                if(i>=11 && i<=13)
                {
                    mat.SetTexture("_SurfaceAtlas",pieces);mat.SetVector("_AtlasGrid",new Vector4(8,8,0,0));
                    mat.SetFloat("_SurfaceMode",3);mat.SetFloat("_Meters",i==12?.65f:.95f);
                    mat.SetVector("_PieceScale",new Vector4(.86f,1.12f,0,0));
                    mat.SetFloat("_PieceJitter",.08f);mat.SetFloat("_MissingPieces",i==11?.16f:.04f);
                    mat.SetFloat("_EdgeRoughness",.04f);mat.SetFloat("_ColorVariation",.2f);mat.SetFloat("_WeatherStrength",.8f);
                    mat.SetColor("_BaseColor",new Color(.12f,.10f,.065f));
                    if(i==12) Palette(mat,new Color(.35f,.21f,.15f),new Color(.85f,.57f,.32f),new Color(1,.88f,.65f),.6f,.12f);
                    if(i==13) Palette(mat,new Color(.13f,.22f,.3f),new Color(.45f,.53f,.55f),new Color(.85f,.9f,.88f),.7f,.15f);
                }
                if(i>=14 && i<=19)
                {
                    mat.SetFloat("_SurfaceMode",2);mat.SetVector("_LayerIndices",new Vector4(2,0,1,0));
                    mat.SetFloat("_PatchSize",1.2f);mat.SetFloat("_PatchAmount",.25f);mat.SetFloat("_BlendWidth",.065f);
                    mat.SetFloat("_WeatherStrength",.65f);mat.SetFloat("_UVWarp",.07f);
                    mat.SetVector("_LowWeights",new Vector4(.1f,.7f,.2f,0));
                    mat.SetVector("_MidWeights",new Vector4(.5f,.3f,.2f,0));
                    mat.SetVector("_HighWeights",new Vector4(.8f,.1f,.1f,0));
                    if(i==15)
                    {
                        mat.SetVector("_LayerIndices",new Vector4(2,3,1,0));
                        mat.SetVector("_LowWeights",new Vector4(.1f,.85f,.05f,0));
                        mat.SetVector("_MidWeights",new Vector4(.45f,.5f,.05f,0));
                        mat.SetVector("_HighWeights",new Vector4(.75f,.2f,.05f,0));
                        mat.SetFloat("_PatchSize",1.7f);
                    }
                    if(i==16) Palette(mat,new Color(.18f,.34f,.19f),new Color(.8f,.48f,.18f),new Color(.95f,.92f,.8f),.8f,.2f);
                    if(i==17) Palette(mat,new Color(.15f,.2f,.34f),new Color(.46f,.32f,.48f),new Color(.92f,.66f,.46f),.85f,.18f);
                    if(i==18) Palette(mat,new Color(.28f,.32f,.12f),new Color(.72f,.28f,.10f),new Color(.92f,.72f,.35f),.85f,.2f);
                    if(i==19)
                    {
                        mat.SetFloat("_SurfaceMode",0);
                        Palette(mat,new Color(.12f,.3f,.4f),new Color(.72f,.3f,.3f),new Color(.98f,.79f,.4f),1,0);
                        mat.SetVector("_TintTransitions",new Vector4(.3f,.12f,.68f,.16f));
                    }
                }
                if(i>=20)
                {
                    if(space==null) throw new Exception("Space atlas missing");
                    mat.SetTexture("_SurfaceAtlas",space);mat.SetFloat("_SurfaceMode",1);mat.SetFloat("_Meters",3.3f);
                    int[] cells={2,3,0,1,2,3};
                    mat.SetVector("_LayerIndices",new Vector4(cells[i-20],0,0,0));
                    mat.SetFloat("_AmbientFloor",.35f);mat.SetFloat("_WeatherStrength",i==20?.3f:0);
                    mat.SetFloat("_SurfaceEmissionStrength",i==20?2:5);
                    mat.SetFloat("_EmissionScale",i==22?2.2f:1.7f);
                    mat.SetFloat("_EmissionWidth",.012f);
                    mat.SetFloat("_EmissionPattern",i==20?1:i==21?2:i==22?3:i==23?1:i==24?0:1);
                    Color[] glow={new Color(1,.35f,.04f),new Color(.02f,.8f,1),new Color(.7f,.04f,1),new Color(1,.3f,.05f),new Color(.1f,1,.45f),new Color(.1f,.4f,1)};
                    mat.SetColor("_SurfaceEmissionColor",glow[i-20]);
                    if(i==22 || i==25) mat.SetFloat("_EmissionPulse",.35f);
                    if(i==24) Palette(mat,new Color(.05f,.25f,.2f),new Color(.18f,.4f,.32f),new Color(.6f,.8f,.65f),.5f,.1f);
                }
                camera.GetUniversalAdditionalCameraData().renderPostProcessing=i>=20;
                string path=Root+"/"+names[i]+".mat";
                var existing=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(existing!=null) {EditorUtility.CopySerialized(mat,existing); UnityEngine.Object.DestroyImmediate(mat);mat=existing;} else AssetDatabase.CreateAsset(mat,path);
                var go=new GameObject(names[i]);go.layer=31;go.transform.position=new Vector3(i*16,0,0);
                go.AddComponent<MeshFilter>().sharedMesh=MakeMesh();go.AddComponent<MeshRenderer>().sharedMaterial=mat;
                camera.transform.position=go.transform.position+new Vector3(9,12,-13);camera.transform.LookAt(go.transform.position+Vector3.up*.4f);
                var rt=RenderTexture.GetTemporary(1000,760,24,RenderTextureFormat.DefaultHDR);camera.targetTexture=rt;camera.Render();
                var before=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(1000,760,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1000,760),0,0);tex.Apply();
                File.WriteAllBytes(Root+"/Previews/"+names[i]+".png",tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);RenderTexture.active=before;camera.targetTexture=null;RenderTexture.ReleaseTemporary(rt);go.layer=0;
            }
            camera.cullingMask=-1;
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene,Root+"/RunnerSurfaceExamples.unity");
            File.WriteAllText(Root+"/Validation.txt","Shader compiled. Created and rendered "+names.Length+" examples. "+DateTime.UtcNow.ToString("O"));
        }
        finally { if(!Application.isBatchMode) {EditorSceneManager.CloseScene(scene,true);UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);} }
    }
    static void Palette(Material mat,Color low,Color mid,Color high,float strength,float noise)
    {
        mat.SetFloat("_CustomHeightPalette",1);mat.SetFloat("_TintStrength",strength);
        mat.SetColor("_LowHeightColor",low);mat.SetColor("_MidHeightColor",mid);mat.SetColor("_HighHeightColor",high);
        mat.SetFloat("_TintNoise",noise);mat.SetFloat("_TintSmoothness",1);
        mat.SetVector("_TintTransitions",new Vector4(.28f,.4f,.7f,.4f));
    }
    static Mesh MakeMesh()
    {
        const int n=65; var v=new Vector3[n*n];var uv=new Vector2[n*n];var surface=new Vector3[n*n];var colors=new Color[n*n];var tris=new int[(n-1)*(n-1)*6];int t=0;
        for(int z=0;z<n;z++) for(int x=0;x<n;x++)
        {
            int k=z*n+x;float a=x/(float)(n-1),b=z/(float)(n-1);float h=Mathf.Clamp01(b*.8f+Mathf.Sin(a*8+b*4)*.15f+.1f);
            v[k]=new Vector3((a-.5f)*10,h*1.5f,(b-.5f)*12);uv[k]=new Vector2(a,b);surface[k]=new Vector3(a*10,b*12,h);
            colors[k]=h<.5f?Color.Lerp(new Color(.25f,.7f,.3f),new Color(.95f,.8f,.35f),h*2):Color.Lerp(new Color(.95f,.8f,.35f),new Color(.8f,.25f,.15f),(h-.5f)*2);
            if(x<n-1 && z<n-1){tris[t++]=k;tris[t++]=k+n;tris[t++]=k+1;tris[t++]=k+1;tris[t++]=k+n;tris[t++]=k+n+1;}
        }
        var m=new Mesh();m.vertices=v;m.uv=uv;m.SetUVs(1,surface);m.colors=colors;m.triangles=tris;m.RecalculateNormals();m.RecalculateBounds();
        string path=Root+"/ExampleGround.asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old!=null){UnityEngine.Object.DestroyImmediate(m);return old;}AssetDatabase.CreateAsset(m,path);return m;
    }
}
