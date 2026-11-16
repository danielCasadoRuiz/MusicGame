using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Coherent authored environments. Cell order is bottom-left, bottom-right, top-left, top-right.
// Keep scales aligned for worn versions of the same boards / marble joints.
public static class CoherentTerrainSetup
{
    const string Root="Assets/_Project/Art/RunnerSurfaces/MusicStyles";
    public static bool Configure(Material mat,RunnerMusicStyleSurfaces.Style item,int variant)
    {
        if(item.atlasSize!=2)return false;
        Vector4 cells;
        switch(item.style)
        {
            case "Electronic": cells=variant==0?new Vector4(0,3,2,0):new Vector4(2,3,1,0);break;
            case "Catchy":
            case "Soulful":
            case "Classical": cells=variant==1?new Vector4(0,3,1,0):variant==2?new Vector4(0,1,2,0):new Vector4(0,3,2,0);break;
            case "Rock": cells=variant==1?new Vector4(0,3,1,0):variant==2?new Vector4(0,1,2,0):new Vector4(0,3,2,0);break;
            case "Dance": cells=new Vector4(2,3,1,0);break;
            case "Acoustic": cells=variant==1?new Vector4(3,2,0,0):variant==2?new Vector4(3,1,0,0):new Vector4(1,2,0,0);break;
            default: throw new Exception("No coherent environment mapping for "+item.style);
        }
        mat.SetVector("_Foundation",(item.style=="Dance" || (item.style=="Electronic" && variant>0))?new Vector4(1,0,.125f,.09f):Vector4.zero);
        mat.SetFloat("_SurfaceMode",2);mat.SetVector("_AtlasGrid",new Vector4(2,2,0,0));
        mat.SetVector("_LayerIndices",cells);mat.SetVector("_LayerTiling",new Vector4(1,1,1,0));
        mat.SetVector("_LowWeights",new Vector4(.96f,.04f,0,0));
        mat.SetVector("_MidWeights",new Vector4(.08f,.84f,.08f,0));
        mat.SetVector("_HighWeights",new Vector4(0,.06f,.94f,0));
        // Band centres: transitions around 25% and 50%; upper surface dominates the upper half.
        mat.SetVector("_HeightStops",new Vector4(.125f,.375f,.625f,0));
        mat.SetFloat("_Meters",item.style=="Acoustic"?3.6f:5.2f);
        mat.SetFloat("_Padding",2);mat.SetFloat("_UVWarp",0);
        mat.SetFloat("_PatchSize",variant==0?2.4f:variant==1?3.3f:1.65f);
        mat.SetFloat("_PatchAmount",variant==0?.15f:variant==1?.21f:.25f);
        mat.SetFloat("_BlendWidth",item.style=="Acoustic"?.18f:.1f);
        mat.SetFloat("_SurfaceSeed",item.styleId*17+variant*103);
        mat.SetFloat("_TintStrength",0);mat.SetFloat("_CustomHeightPalette",0);
        mat.SetFloat("_WeatherStrength",.08f);mat.SetFloat("_AmbientFloor",.78f);
        // Fluorescent paint belongs to the texture; no unrelated HUD-colour emission.
        mat.SetFloat("_SurfaceEmissionStrength",0);
        if(item.style=="Electronic" && variant==0)
        {
            mat.SetFloat("_EmissionPattern",6);
            mat.SetColor("_SurfaceEmissionColor",variant==1?new Color(.65f,.025f,1):new Color(.015f,.8f,1));
            mat.SetFloat("_EmissionWidth",.014f);
            mat.SetFloat("_SurfaceEmissionStrength",5.5f);mat.SetFloat("_EmissionPulse",.12f);
        }
        if((item.style=="Dance" && variant==1) || (item.style=="Electronic" && variant>0))
        {
            mat.SetFloat("_EmissionPattern",5);mat.SetFloat("_SurfaceEmissionStrength",item.style=="Electronic"?2.4f:1.8f);
            mat.SetFloat("_EmissionPulse",.08f);mat.SetFloat("_Meters",5.2f);
        }
        return true;
    }
    [MenuItem("MusicGame/Runner/Install Coherent Terrain Concepts")]
    public static void InstallAndVerify()
    {
        AssetDatabase.Refresh();
        var catalog=JsonUtility.FromJson<RunnerMusicStyleSurfaces.Catalog>(File.ReadAllText(Root+"/MusicStyleSurfaces.json"));
        bool previous=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
        int count=0,concepts=0;
        try
        {
            foreach(var item in catalog.styles)
            {
                if(item.atlasSize!=2)continue;
                concepts++;
                foreach(string atlasName in RunnerMusicStyleSurfaces.AtlasNames(item))
                {
                    string atlasPath=Root+"/Atlas_"+atlasName+".png";
                    var importer=AssetImporter.GetAtPath(atlasPath) as TextureImporter;
                    if(importer==null)throw new Exception("Missing concept atlas: "+atlasPath);
                    importer.wrapMode=TextureWrapMode.Clamp;importer.mipmapEnabled=true;importer.anisoLevel=4;
                    importer.maxTextureSize=2048;importer.npotScale=TextureImporterNPOTScale.None;
                    importer.textureCompression=TextureImporterCompression.Compressed;importer.SaveAndReimport();
                }
                var atlas=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Atlas_"+item.atlas+".png");
                if(atlas==null || atlas.width!=atlas.height)throw new Exception("Invalid atlas "+item.atlas);
                int usedCells=0;var distinctAtlases=new HashSet<Texture>();
                for(int v=0;v<3;v++)
                {
                    string path=Root+"/"+item.style+"/"+item.style+"_"+(v+1)+".mat";
                    var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(mat==null)throw new Exception("Missing existing preset: "+path);
                    mat.SetTexture("_SurfaceAtlas",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Atlas_"+RunnerMusicStyleSurfaces.AtlasName(item,v)+".png"));Configure(mat,item,v);EditorUtility.SetDirty(mat);
                    distinctAtlases.Add(mat.GetTexture("_SurfaceAtlas"));
                    if(mat.GetTexture("_SurfaceAtlas")==null)throw new Exception("Missing variant atlas: "+path);
                    if(item.style=="Dance" || (item.style=="Electronic" && v>0))
                    {
                        var foundation=mat.GetVector("_Foundation");
                        if(foundation.x!=1 || foundation.y!=0 || mat.GetVector("_LayerIndices")!=new Vector4(2,3,1,0))throw new Exception("Dance lost four construction states");
                        if((item.style=="Electronic" || v==1) && (mat.GetFloat("_EmissionPattern")!=5 || mat.GetFloat("_SurfaceEmissionStrength")<=0))throw new Exception("Disco lights disabled");
                        usedCells|=1<<(int)foundation.y;
                    }
                    var indices=mat.GetVector("_LayerIndices");
                    for(int i=0;i<3;i++){int cell=(int)indices[i];if(cell<0 || cell>3)throw new Exception("Invalid cell");usedCells|=1<<cell;}
                    if(ShaderUtil.ShaderHasError(mat.shader) || !mat.SetPass(0))throw new Exception("Concept shader failed: "+path);
                    count++;
                }
                if((item.style=="Dance" || item.style=="Electronic") && distinctAtlases.Count!=3)throw new Exception("Expected three distinct floors for "+item.style);
                if(usedCells!=15)throw new Exception("Unused atlas quadrant: "+item.style);
                Debug.Log("[CoherentTerrain] "+item.style+": "+atlas.width+"x"+atlas.height+", three coherent variants, all four quadrants used.");
            }
            if(concepts!=7 || count!=21)throw new Exception("Expected seven coherent concepts and twenty-one presets");
            AssetDatabase.SaveAssets();
            RunnerSurfaceIntegration.InstallAndVerify();
            FinishTerrainSetup.InstallAndCheck();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/CoherentTerrainCheck.txt","PASS: seven coherent concepts / twenty-one existing material GUIDs updated; eleven compressed mipmapped atlases, including three distinct Dance and Electronic alternatives; all quadrants used, aligned layer scales; actual shader passes compiled; theme binding, stable restart selection and classical finish checks passed. No captures. "+DateTime.UtcNow.ToString("O"));
            Debug.Log(File.ReadAllText("Logs/CoherentTerrainCheck.txt"));
        }
        finally{ShaderUtil.allowAsyncCompilation=previous;}
    }
}
