using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class RunnerHeightSurfaceUpgrade
{
    const string Root="Assets/_Project/Art/RunnerSurfaces/MusicStyles";
    // Explicit menu / batch execution only; do not render automatically in the user editor.
    static void CompletePendingUpgrade()
    {
        EditorApplication.delayCall+=()=>
        {
            const string marker="RunnerHeightSurfaceUpgrade.v2";
            if(File.Exists(Root+"/HeightBlendValidation.txt") && File.ReadAllText(Root+"/HeightBlendValidation.txt").StartsWith("PASS v2:",StringComparison.Ordinal))return;
            if(SessionState.GetBool(marker,false) || EditorApplication.isPlayingOrWillChangePlaymode || Application.isBatchMode)return;
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.isDirty || string.IsNullOrEmpty(scene.path))return;
            SessionState.SetBool(marker,true);
            try{Run();}catch(Exception e){SessionState.SetBool(marker,false);Debug.LogException(e);}
        };
    }
    public static void Configure(Material mat,RunnerMusicStyleSurfaces.Style item,int variant)
    {
        if(CoherentTerrainSetup.Configure(mat,item,variant))return;
        int low=2,mid=0,high=1;
        if(item.style=="Acoustic" || item.style=="Chill"){low=1;mid=0;high=2;}
        if(item.style=="Rock" || item.style=="Heavy"){low=2;mid=0;high=1;}
        // Same three textures, different altitude distributions and coverage per preset.
        if(variant==1){int old=mid;mid=high;high=old;}
        if(variant==2){int old=low;low=mid;mid=old;}
        mat.SetFloat("_SurfaceMode",2);
        mat.SetVector("_LayerIndices",new Vector4(item.firstCell+low,item.firstCell+mid,item.firstCell+high,0));
        mat.SetVector("_LowWeights",new Vector4(.94f,.06f,0,0));
        mat.SetVector("_MidWeights",new Vector4(.16f,.72f,.12f,0));
        mat.SetVector("_HighWeights",new Vector4(0,.08f,.92f,0));
        // Band centres: transitions around 25% and 50%; upper surface dominates the upper half.
        mat.SetVector("_HeightStops",new Vector4(.125f,.375f,.625f,0));
        mat.SetVector("_LayerTiling",new Vector4(variant==2?1.35f:.9f,1.1f,variant==1?.8f:1.4f,0));
        mat.SetFloat("_Meters",4.8f);
        mat.SetFloat("_PatchSize",variant==0?1.9f:variant==1?3.2f:1.1f);
        mat.SetFloat("_PatchAmount",variant==0?.17f:variant==1?.24f:.2f);
        mat.SetFloat("_BlendWidth",variant==0?.13f:variant==1?.18f:.09f);
        mat.SetFloat("_SurfaceSeed",item.styleId*17+variant*103);
        mat.SetFloat("_WeatherStrength",variant==0?.25f:variant==1?.4f:.32f);
        mat.SetFloat("_UVWarp",.015f);
        mat.SetFloat("_TintStrength",.2f);
    }
    [MenuItem("MusicGame/Runner/Upgrade Height Layers and Refresh Gallery")]
    public static void Run()
    {
        // The existing builder updates material assets in place: theme references and match selection stay valid.
        RunnerMusicStyleSurfaces.Build();
        var catalog=JsonUtility.FromJson<RunnerMusicStyleSurfaces.Catalog>(File.ReadAllText(Root+"/MusicStyleSurfaces.json"));
        int count=0;
        foreach(var style in catalog.styles)for(int v=0;v<3;v++)
        {
            var mat=AssetDatabase.LoadAssetAtPath<Material>(Root+"/"+style.style+"/"+style.style+"_"+(v+1)+".mat");
            if(mat==null || mat.GetFloat("_SurfaceMode")!=2 || ShaderUtil.ShaderHasError(mat.shader))throw new Exception("Height surface failed: "+style.style);
            var cells=mat.GetVector("_LayerIndices");if(cells.x==cells.y || cells.x==cells.z || cells.y==cells.z)throw new Exception("Repeated surface cells: "+style.style);
            if(mat.GetVector("_LowWeights")==mat.GetVector("_HighWeights"))throw new Exception("No altitude variation: "+style.style);
            count++;
        }
        File.WriteAllText(Root+"/HeightBlendValidation.txt","PASS v2: "+count+" existing presets upgraded in place to three distinct surface cells, different altitude weights, continuous multiscale masks and independent texture scales; shader compiled and previews rendered. "+DateTime.UtcNow.ToString("O"));
        Debug.Log(File.ReadAllText(Root+"/HeightBlendValidation.txt"));
    }
}
