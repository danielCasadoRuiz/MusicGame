using UnityEngine;

/// <summary>One flat terrain choice per assignment. No reroll on restart/resume, no immediate repeat.</summary>
public static class RunnerTerrainSelection
{
    public static Material ResolvePending(TrackStyleSO track,GameMusicStyle style,int seed,PendingRunData run,TerrainSelectionHistory previous,out bool changed)
    {
        changed=false;if(track==null)return null;
        string assigned=run.terrainStyle==(int)style?run.terrainMaterialId:null;
        string excluded=previous.style==(int)style?previous.materialId:null;
        var material=track.ResolveTerrainMaterial(style,seed,assigned,excluded);
        if(material==null)return null;
        if(run.terrainStyle==(int)style && run.terrainMaterialId==material.name)return material;
        run.terrainStyle=(int)style;run.terrainMaterialId=material.name;
        previous.style=(int)style;previous.materialId=material.name;changed=true;
        return material;
    }

    static int sessionSeed,sessionStyle;
    static string sessionMaterial;
    static TerrainSelectionHistory sessionHistory=new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset(){sessionSeed=0;sessionStyle=0;sessionMaterial=null;sessionHistory=new();}
    public static Material ResolveSession(TrackStyleSO track,GameMusicStyle style,int seed)
    {
        if(track==null)return null;
        bool same=sessionMaterial!=null && sessionSeed==seed && sessionStyle==(int)style;
        var run=new PendingRunData{terrainStyle=same?sessionStyle:0,terrainMaterialId=same?sessionMaterial:""};
        var material=ResolvePending(track,style,seed,run,sessionHistory,out _);
        sessionSeed=seed;sessionStyle=(int)style;sessionMaterial=material!=null?material.name:null;
        return material;
    }
}
