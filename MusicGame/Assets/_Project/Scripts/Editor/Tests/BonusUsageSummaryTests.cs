#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using MusicGame.Audio.Analysis;
using Object=UnityEngine.Object;

public class BonusUsageSummaryTests
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static BonusLayoutRecord Record()=>new BonusLayoutRecord{
        key=BonusLayoutKey.Create(default,"test",.5f,1,1),seed=37,generatedAtUnixSeconds=12345,
        events=new[]{new TimelineEvent{ringType=RingType.Snare},new TimelineEvent{ringType=RingType.Beat},new TimelineEvent{ringType=RingType.Snare},new TimelineEvent{ringType=RingType.Kick}}
    };
    [Test] public void SummaryPersistsDistinctTypesAndCountsAndHandlesEmptySongs()
    {
        var record=FileBonusLayoutStore.Read(FileBonusLayoutStore.Write(Record()));
        CollectionAssert.AreEqual(new[]{RingType.Beat,RingType.Kick,RingType.Snare},record.bonusTypes.Select(u=>u.type));
        CollectionAssert.AreEqual(new[]{1,1,2},record.bonusTypes.Select(u=>u.count));
        record.events=Array.Empty<TimelineEvent>();record.bonusTypes=null;
        Assert.IsEmpty(FileBonusLayoutStore.Read(FileBonusLayoutStore.Write(record)).bonusTypes);
    }
    [Test] public void CachedSummaryIsReadWithoutDerivingTypesFromEvents()
    {
        var record=Record();
        record.bonusTypes=new[]{new BonusTypeUsage{type=RingType.Life,count=4}};
        // Deliberately different but structurally valid manifest: a reader that rescans events loses it.
        var loaded=FileBonusLayoutStore.Read(FileBonusLayoutStore.Write(record));
        Assert.AreEqual(RingType.Life,loaded.bonusTypes.Single().type);
    }
    public static byte[] LegacyBytes(BonusLayoutRecord record)
    {
        byte[] bytes=FileBonusLayoutStore.Write(record);
        int summaryLength=4+BonusTypeUsage.Summarize(record.events).Length*8;
        Array.Resize(ref bytes,bytes.Length-summaryLength);
        using(var w=new BinaryWriter(new MemoryStream(bytes)))
        {
            w.BaseStream.Position=4;w.Write(1);w.Write(bytes.Length-16);w.Write(Crc32.Compute(bytes,16,bytes.Length-16));
        }
        return bytes;
    }
    [Test] public void LegacyFileRemainsReadableWithEventsSeedAndDateIntact()
    {
        var original=Record();var loaded=FileBonusLayoutStore.Read(LegacyBytes(original));
        Assert.IsNull(loaded.bonusTypes);Assert.AreEqual(original.seed,loaded.seed);Assert.AreEqual(original.generatedAtUnixSeconds,loaded.generatedAtUnixSeconds);
        CollectionAssert.AreEqual(original.events.Select(e=>e.ringType),loaded.events.Select(e=>e.ringType));
    }
    [Test] public void PoolsUseOnlyCachedTypesAndReuseObjects()
    {
        var go=new GameObject("PoolManifestTest");go.SetActive(false);
        var parent=new GameObject("PoolManifestObjects");
        var manager=go.AddComponent<GameplayManager>();
        try
        {
            var config=AssetDatabase.LoadAssetAtPath<MusicRunnerGameplayConfig>("Assets/_Project/Configs/Gameplay/MusicRunner/_MusicRunnerGameplayConfig.asset");
            typeof(GameplayManager).GetField("config",Private).SetValue(manager,config);
            typeof(GameplayManager).GetField("_poolParent",Private).SetValue(manager,parent.transform);
            typeof(GameplayManager).GetField("_usedBonusTypes",Private).SetValue(manager,new[]{new BonusTypeUsage{type=RingType.Beat,count=2},new BonusTypeUsage{type=RingType.Snare,count=1}});
            typeof(GameplayManager).GetMethod("InitializePools",Private).Invoke(manager,null);
            CollectionAssert.AreEquivalent(new[]{RingType.Beat,RingType.Snare},manager.RingPools.Keys);
            var pool=manager.RingPools[RingType.Snare];Assert.AreEqual(1,pool.TotalCount);
            var instance=pool.Get();instance.SetActive(true);pool.Return(instance);
            Assert.AreSame(instance,pool.Get());Assert.AreEqual(1,pool.TotalCount);
        }
        finally{Clean(manager,parent);Object.DestroyImmediate(go);}
    }
    [Test] public void PoolKeepsBonusTexturesAndStillColoursPlainPlaceholders()
    {
        var go=new GameObject("BonusMaterialTest");go.SetActive(false);
        var parent=new GameObject("BonusMaterialObjects");var manager=go.AddComponent<GameplayManager>();
        try
        {
            var config=AssetDatabase.LoadAssetAtPath<MusicRunnerGameplayConfig>("Assets/_Project/Configs/Gameplay/MusicRunner/_MusicRunnerGameplayConfig.asset");
            typeof(GameplayManager).GetField("config",Private).SetValue(manager,config);
            typeof(GameplayManager).GetField("_poolParent",Private).SetValue(manager,parent.transform);
            var build=typeof(GameplayManager).GetMethod("BuildPoolObject",Private);
            foreach(var type in new[]{RingType.Beat,RingType.Kick,RingType.Snare,RingType.HiHat,RingType.Onset,RingType.Impact})
            {
                var prefab=config.collectibles.ResolvePrefab(type);
                var instance=(GameObject)build.Invoke(manager,new object[]{type,prefab});
                Assert.AreSame(prefab.GetComponentInChildren<MeshRenderer>().sharedMaterial,instance.GetComponentInChildren<MeshRenderer>().sharedMaterial);
                Assert.IsNotNull(instance.GetComponentInChildren<MeshRenderer>().sharedMaterial.GetTexture("_BaseMap"));
            }
            var plain=(GameObject)build.Invoke(manager,new object[]{RingType.Life,config.collectibles.ResolvePrefab(RingType.Life)});
            Assert.AreEqual(config.collectibles.RingColor(RingType.Life),plain.GetComponentInChildren<MeshRenderer>().sharedMaterial.color);
        }
        finally{Clean(manager,parent);Object.DestroyImmediate(go);}
    }
    static void Clean(GameplayManager manager,GameObject parent)
    {
        var materials=(Dictionary<RingType,Material>)typeof(GameplayManager).GetField("_materials",Private).GetValue(manager);
        foreach(var material in materials.Values)Object.DestroyImmediate(material);materials.Clear();
        typeof(GameplayManager).GetField("_poolParent",Private).SetValue(manager,null);Object.DestroyImmediate(parent);
    }
}
#endif
