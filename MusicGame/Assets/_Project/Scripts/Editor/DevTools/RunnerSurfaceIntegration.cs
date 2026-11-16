using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine.AddressableAssets;

public static class RunnerSurfaceIntegration
{
    const string Art="Assets/_Project/Art/RunnerSurfaces/MusicStyles";
    [MenuItem("MusicGame/Runner/Install Musical Surfaces Into Themes")]
    public static void InstallAndVerify()
    {
        var app=Resources.Load<AppConfigSO>("AppConfig");
        if(app==null || app.theme==null || app.theme.musicStyleRegistry==null)throw new Exception("App theme configuration missing");
        var catalog=JsonUtility.FromJson<RunnerMusicStyleSurfaces.Catalog>(File.ReadAllText(Art+"/MusicStyleSurfaces.json"));
        var rules=app.musicStyleRules!=null?app.musicStyleRules.rules:MusicStyleRulesSO.DefaultRules();
        var registry=app.theme.musicStyleRegistry;var entries=new List<MusicStyleRegistrySO.Entry>(registry.entries);
        var settings=AddressableAssetSettingsDefaultObject.GetSettings(true);
        var visualGroup=SurfaceGroup(settings,"Remote_Styles_Visuals");
        var atlasGroup=SurfaceGroup(settings,"Remote_Styles_SurfaceAtlases");
        foreach(var atlas in catalog.styles.SelectMany(RunnerMusicStyleSurfaces.AtlasNames).Distinct())
        {
            string path=Art+"/Atlas_"+atlas+".png";
            var atlasEntry=settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path),atlasGroup);
            atlasEntry.address="RunnerSurfaceAtlas/"+atlas;
            atlasEntry.SetLabel(ContentKeys.Category(ContentCategory.Styles),true,true);
        }
        int styleCount=0;
        foreach(var group in catalog.styles.GroupBy(s=>rules.First(r=>r.style==(GameMusicStyle)s.styleId).themeStyle))
        {
            string directory="Assets/_Project/Configs/Theme/Styles/"+group.Key;
            Directory.CreateDirectory(directory);AssetDatabase.Refresh();
            var entry=entries.Find(e=>e.style==group.Key);
            var visual=entry.visual!=null?AssetDatabase.LoadAssetAtPath<MusicStyleVisualSO>(AssetDatabase.GUIDToAssetPath(entry.visual.AssetGUID)):null;
            if(visual==null)
            {
                string path=directory+"/"+group.Key+"Visual.asset";
                visual=AssetDatabase.LoadAssetAtPath<MusicStyleVisualSO>(path);
                if(visual==null){visual=ScriptableObject.CreateInstance<MusicStyleVisualSO>();visual.style=group.Key;AssetDatabase.CreateAsset(visual,path);}
                string guid=AssetDatabase.AssetPathToGUID(path);
                var address=settings.CreateOrMoveEntry(guid,settings.DefaultGroup);address.address=group.Key+"Visual";
                entries.RemoveAll(e=>e.style==group.Key);
                entries.Add(new MusicStyleRegistrySO.Entry{style=group.Key,displayName=group.Key.ToString(),visual=new AssetReferenceT<MusicStyleVisualSO>(guid)});
            }
            var visualEntry=settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(visual)),visualGroup);
            visualEntry.SetLabel(ContentKeys.Style(group.Key.ToString()),true,true);
            visualEntry.SetLabel(ContentKeys.Category(ContentCategory.Styles),true,true);
            var track=visual.track;
            if(track==null)
            {
                string path=directory+"/TrackStyle_"+group.Key+".asset";
                track=AssetDatabase.LoadAssetAtPath<TrackStyleSO>(path);
                if(track==null){track=ScriptableObject.CreateInstance<TrackStyleSO>();AssetDatabase.CreateAsset(track,path);}
                visual.track=track;
            }
            var sets=new List<TrackStyleSO.TerrainSurfaceSet>(track.terrainSurfaces??Array.Empty<TrackStyleSO.TerrainSurfaceSet>());
            foreach(var item in group)
            {
                var style=(GameMusicStyle)item.styleId;
                var materials=Enumerable.Range(1,3).Select(v=>AssetDatabase.LoadAssetAtPath<Material>(Art+"/"+item.style+"/"+item.style+"_"+v+".mat")).ToArray();
                if(materials.Any(m=>m==null))throw new Exception("Missing surfaces for "+style);
                for(int v=0;v<materials.Length;v++){materials[v].name=item.style+"_"+(v+1);EditorUtility.SetDirty(materials[v]);}
                sets.RemoveAll(s=>s!=null && s.style==style);
                sets.Add(new TrackStyleSO.TerrainSurfaceSet{style=style,variants=materials});styleCount++;
            }
            track.terrainSurfaces=sets.ToArray();EditorUtility.SetDirty(track);EditorUtility.SetDirty(visual);
        }
        registry.entries=entries.ToArray();EditorUtility.SetDirty(registry);EditorUtility.SetDirty(settings);AssetDatabase.SaveAssets();
        Debug.Log("[RunnerSurface] Editor play-mode content source: "+settings.DataBuilders[settings.ActivePlayModeDataBuilderIndex].name);
        Verify(app,catalog,rules);
        File.WriteAllText(Art+"/IntegrationValidation.txt","PASS: "+styleCount+" gameplay styles / 51 materials connected through existing MusicStyleRegistry → MusicStyleVisual → TrackStyle. Flat selection, 256 successive assignments per style without repeats, restart/history stability, saved-run restore, null fallback, style isolation, selection variety, random-state isolation, theme resolution and actual world material cloning verified. "+DateTime.UtcNow.ToString("O"));
        Debug.Log(File.ReadAllText(Art+"/IntegrationValidation.txt"));
    }
    static AddressableAssetGroup SurfaceGroup(AddressableAssetSettings settings,string name)
    {
        var group=settings.FindGroup(name) ?? settings.CreateGroup(name,false,false,true,null,typeof(BundledAssetGroupSchema),typeof(ContentUpdateGroupSchema));
        var schema=group.GetSchema<BundledAssetGroupSchema>();
        schema.BuildPath.SetVariableByName(settings,AddressableAssetSettings.kRemoteBuildPath);
        schema.LoadPath.SetVariableByName(settings,AddressableAssetSettings.kRemoteLoadPath);
        schema.BundleMode=BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
        schema.UseAssetBundleCache=true;group.GetSchema<ContentUpdateGroupSchema>().StaticContent=false;
        EditorUtility.SetDirty(schema);EditorUtility.SetDirty(group);return group;
    }
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Verify(AppConfigSO app,RunnerMusicStyleSurfaces.Catalog catalog,MusicStyleRulesSO.Rule[] rules)
    {
        var globalRandom=JsonUtility.ToJson(UnityEngine.Random.state);
        foreach(var item in catalog.styles)
        {
            var style=(GameMusicStyle)item.styleId;var themeId=rules.First(r=>r.style==style).themeStyle;
            Require(app.theme.musicStyleRegistry.TryGetVisualReference(themeId,out var reference),"Missing registry "+style);
            var visual=AssetDatabase.LoadAssetAtPath<MusicStyleVisualSO>(AssetDatabase.GUIDToAssetPath(reference.AssetGUID));
            var bundled=settingsForValidation(reference.AssetGUID);
            Require(bundled.parentGroup.Name=="Remote_Styles_Visuals" && bundled.labels.Contains(ContentKeys.Style(themeId.ToString())),"Visual not in its style content group");
            var resolved=ThemeResolver.Resolve(app.theme.baseTheme,visual,null);
            Require(resolved.Track==visual.track,"Wrong track resolution "+style);
            var track=resolved.Track;var seen=new HashSet<Material>();
            for(int seed=1;seed<=128;seed++)
            {
                var first=track.ResolveTerrainMaterial(style,seed);Require(first!=null && first.name.StartsWith(item.style+"_",StringComparison.Ordinal),"Wrong style surface "+style);
                Require(first==track.ResolveTerrainMaterial(style,seed),"Retry changed surface "+style);
                var pending=new PendingRunData{songId="test-only",pickupSeed=seed};
                var restored=JsonUtility.FromJson<PendingRunData>(JsonUtility.ToJson(pending));
                Require(first==track.ResolveTerrainMaterial(style,restored.pickupSeed),"Resume changed surface "+style);seen.Add(first);
            }
            Require(seen.Count==3,"Not all three alternatives selectable "+style);
            VerifyTerrainAssignment(track,style);
            VerifyWorldMaterial(track.ResolveTerrainMaterial(style,42));
        }
        Require(globalRandom==JsonUtility.ToJson(UnityEngine.Random.state),"Selection mutated global random state");
        var fallback=ScriptableObject.CreateInstance<TrackStyleSO>();var material=new Material(Shader.Find("MusicGame/VertexColorLit"));
        fallback.trackMaterial=material;fallback.terrainSurfaces=new[]{new TrackStyleSO.TerrainSurfaceSet{style=GameMusicStyle.Rock,variants=new Material[]{null}}};
        Require(fallback.ResolveTerrainMaterial(GameMusicStyle.Rock,42)==material,"Empty variants fallback failed");
        Require(fallback.ResolveTerrainMaterial(GameMusicStyle.Unknown,42)==material,"Unknown style fallback failed");
        UnityEngine.Object.DestroyImmediate(fallback);UnityEngine.Object.DestroyImmediate(material);
    }
    static void VerifyTerrainAssignment(TrackStyleSO track,GameMusicStyle style)
    {
        var data=new PlayerProgressData();var seen=new HashSet<string>();string previous=null;
        for(int seed=1;seed<=256;seed++)
        {
            data.pendingRun=new PendingRunData{songId="test-only",pickupSeed=seed};
            var material=RunnerTerrainSelection.ResolvePending(track,style,seed,data.pendingRun,data.terrainSelection,out bool changed);
            Require(changed && material!=null,"New terrain assignment not recorded");
            Require(previous==null || previous!=material.name,"Consecutive new matches repeated a terrain");
            seen.Add(material.name);previous=material.name;
            var historyBefore=JsonUtility.ToJson(data.terrainSelection);
            var retry=RunnerTerrainSelection.ResolvePending(track,style,seed,data.pendingRun,data.terrainSelection,out changed);
            Require(retry==material && !changed && historyBefore==JsonUtility.ToJson(data.terrainSelection),"Restart rerolled or advanced terrain history");
            data=JsonUtility.FromJson<PlayerProgressData>(JsonUtility.ToJson(data));
            var resumed=RunnerTerrainSelection.ResolvePending(track,style,seed,data.pendingRun,data.terrainSelection,out changed);
            Require(resumed==material && !changed,"Saved terrain assignment changed after resume");
        }
        Require(seen.Count==3,"Flat non-repeat choice cannot reach all alternatives");
        // Saved ids are authoritative even after content ordering changes; one option must remain playable.
        var single=ScriptableObject.CreateInstance<TrackStyleSO>();
        var only=track.ResolveTerrainMaterial(style,1);
        single.terrainSurfaces=new[]{new TrackStyleSO.TerrainSurfaceSet{style=style,variants=new Material[]{null,only,null}}};
        Require(single.ResolveTerrainMaterial(style,3,null,only.name)==only,"Single remaining terrain was excluded");
        Require(track.ResolveTerrainMaterial(style,999,only.name,"unused")==only,"Saved terrain id was ignored");
        UnityEngine.Object.DestroyImmediate(single);
        var old=JsonUtility.FromJson<PlayerProgressData>("{\"version\":8,\"pendingRun\":{\"songId\":\"test-only\"}}");
        PlayerProgressStore.Migrate(old);
        Require(old.terrainSelection!=null && old.pendingRun.terrainMaterialId!=null,"Legacy save terrain defaults missing");
    }
    static AddressableAssetEntry settingsForValidation(string guid) => AddressableAssetSettingsDefaultObject.Settings.FindAssetEntry(guid);
    static void VerifyWorldMaterial(Material preset)
    {
        // Exercise the real consumer: previously the pathMaterial argument was stored but ignored.
        var host=new GameObject("TerrainMaterialIntegrationTest");host.SetActive(false);
        var config=ScriptableObject.CreateInstance<MusicRunnerGameplayConfig>();var level=ScriptableObject.CreateInstance<MusicRunnerLevelConfig>();config.levelGeneration=level;
        var world=host.AddComponent<MusicWorldManager>();world.Initialize(config,preset);
        var clone=(Material)typeof(MusicWorldManager).GetMethod("VertexColorMaterial",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(world,null);
        Require(clone!=preset && clone.shader==preset.shader && clone.GetTexture("_SurfaceAtlas")==preset.GetTexture("_SurfaceAtlas") && clone.GetVector("_LayerIndices")==preset.GetVector("_LayerIndices"),"World lost selected surface settings");
        UnityEngine.Object.DestroyImmediate(clone);UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(config);UnityEngine.Object.DestroyImmediate(level);
    }
}
