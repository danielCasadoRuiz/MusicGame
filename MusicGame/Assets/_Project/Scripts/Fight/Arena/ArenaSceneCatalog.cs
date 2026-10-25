using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>Maps the existing arena identity to independently loadable scene qualities.
/// GUID-only references keep the authoring prefab and the other qualities out of runtime dependencies.</summary>
[CreateAssetMenu(menuName="MusicGame/Arenas/Scene Catalog")]
public sealed class ArenaSceneCatalog : ScriptableObject
{
    [Serializable] public class Variant { public string qualityName; public AssetReference scene; }
    [Serializable] public class Entry { public string sourceArenaGuid; public List<Variant> variants = new(); }
    public List<Entry> entries = new();
    public static ArenaSceneCatalog Current => Resources.Load<ArenaSceneCatalog>("ArenaSceneCatalog");
    public Entry Find(string guid) => entries?.Find(e=>e!=null && e.sourceArenaGuid==guid);
    public bool Contains(string guid) => Find(guid)!=null;

    public static AssetReference Resolve(AssetReferenceGameObject source, out string quality)
    {
        quality=null;
        if(source==null || Current==null) return null;
        QualityAssetResolver.MarkQualityInUse();
        return Current.Resolve(source.AssetGUID,QualitySettings.names,QualitySettings.GetQualityLevel(),out quality);
    }
    public AssetReference Resolve(string guid,IReadOnlyList<string> names,int requested,out string quality)
    {
        quality=null; var entry=Find(guid);
        if(entry?.variants==null || names==null) return null;
        var available=new Dictionary<int,Variant>();
        foreach(var v in entry.variants)
        {
            if(v?.scene==null || !v.scene.RuntimeKeyIsValid()) continue;
            int index=QualityAssetResolver.IndexOf(names,v.qualityName);
            if(index>=0 && !available.ContainsKey(index)) available[index]=v;
        }
        int chosen=QualityAssetResolver.ChooseLevel(available.Keys,requested);
        if(chosen<0) return null;
        quality=names[chosen]; return available[chosen].scene;
    }
}
