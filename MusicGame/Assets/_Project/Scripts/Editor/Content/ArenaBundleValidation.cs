#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.AnalyzeRules;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;

// Shared files are grouped by their source folder (one prop and one quality).
// The configuration holds GUID strings through Addressables, never a runtime prefab catalog.
public static class ArenaBundleValidation
{
    const string Root="Assets/_Project/Art/Arenas";
    const string FloraRoot="Assets/_Project/Art/Shared/ReferenceFlora";
    const string GroupName="Remote_ArenaDependencies";
    const string SharedGroupName="Remote_ArenaShared";
    static readonly string[] Extensions={".fbx",".mat",".png",".jpg",".jpeg",".tga",".exr",".asset"};
    static void CopyTree(string source,string destination)
    {
        if(!Directory.Exists(source))return;
        foreach(string file in Directory.GetFiles(source,"*",SearchOption.AllDirectories))
        {
            string relative=file.Substring(source.TrimEnd(Path.DirectorySeparatorChar).Length+1);
            string target=Path.Combine(destination,relative);Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(file,target,true);
        }
    }
    [MenuItem("Tools/MusicGame/Content/Arenas/Register Shared Bundle Dependencies")]
    public static void Register()
    {
        var settings=AddressableAssetSettingsDefaultObject.Settings;
        ArenaDistanceQuality.Backup("_Base");
        var group=settings.FindGroup(GroupName);
        if(group==null)group=settings.CreateGroup(GroupName,false,false,true,null,typeof(BundledAssetGroupSchema),typeof(ContentUpdateGroupSchema));
        var schema=group.GetSchema<BundledAssetGroupSchema>();
        schema.BundleMode=BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel;
        schema.BuildPath.SetVariableByName(settings,AddressableAssetSettings.kRemoteBuildPath);
        schema.LoadPath.SetVariableByName(settings,AddressableAssetSettings.kRemoteLoadPath);
        var paths=new HashSet<string>();var sharedPaths=new HashSet<string>();
        foreach(string scene in AssetDatabase.FindAssets("t:Scene",new[]{Root}).Select(AssetDatabase.GUIDToAssetPath).Where(p=>p.Contains("/Scenes/")))
        foreach(string dependency in AssetDatabase.GetDependencies(scene,true))
        {
            string extension=Path.GetExtension(dependency).ToLowerInvariant();
            if((dependency.StartsWith(Root+"/",StringComparison.Ordinal)||dependency.StartsWith(FloraRoot+"/",StringComparison.Ordinal))&&Extensions.Contains(extension))paths.Add(dependency);
            else if(extension==".shader"||(extension==".mat"&&dependency.StartsWith("Packages/com.unity.render-pipelines",StringComparison.Ordinal)))sharedPaths.Add(dependency);
        }
        foreach(var old in group.entries.Where(e=>!paths.Contains(e.AssetPath)).ToArray())settings.RemoveAssetEntry(old.guid);
        foreach(string path in paths.OrderBy(p=>p))
        {
            string guid=AssetDatabase.AssetPathToGUID(path);
            var entry=settings.FindAssetEntry(guid);
            if(entry!=null&&entry.parentGroup!=group) {Debug.Log("Keep existing shared address: "+path);continue;}
            entry=settings.CreateOrMoveEntry(guid,group);
            // All maps, meshes and materials in a prop/quality folder share exactly this label.
            string label="ArenaDependency_"+AssetDatabase.AssetPathToGUID(Path.GetDirectoryName(path).Replace('\\','/'));
            if(path.StartsWith(FloraRoot+"/Models/",StringComparison.Ordinal))
            {
                string file=Path.GetFileNameWithoutExtension(path);
                string quality=new[]{"Low","Mid","HighMid","High","Ultra"}.Single(q=>file.EndsWith("_"+q,StringComparison.Ordinal));
                label="ArenaDependency_Flora_"+file.Split('_')[0]+"_"+quality;
            }
            else if(path.StartsWith(FloraRoot+"/",StringComparison.Ordinal))label="ArenaDependency_FloraPalette";
            foreach(string prior in entry.labels.Where(l=>l.StartsWith("ArenaDependency_",StringComparison.Ordinal)).ToArray())entry.SetLabel(prior,false);
            settings.AddLabel(label);entry.SetLabel(label,true);
            entry.address=path.StartsWith(FloraRoot+"/",StringComparison.Ordinal)?"ArenaDependency/SharedFlora/"+path.Substring(FloraRoot.Length+1):"ArenaDependency/"+path.Substring(Root.Length+1);
        }
        EditorUtility.SetDirty(schema);EditorUtility.SetDirty(group);EditorUtility.SetDirty(settings);AssetDatabase.SaveAssets();
        var shared=settings.FindGroup(SharedGroupName);
        if(shared==null)shared=settings.CreateGroup(SharedGroupName,false,false,true,null,typeof(BundledAssetGroupSchema),typeof(ContentUpdateGroupSchema));
        var sharedSchema=shared.GetSchema<BundledAssetGroupSchema>();sharedSchema.BundleMode=BundledAssetGroupSchema.BundlePackingMode.PackTogether;
        sharedSchema.BuildPath.SetVariableByName(settings,AddressableAssetSettings.kRemoteBuildPath);sharedSchema.LoadPath.SetVariableByName(settings,AddressableAssetSettings.kRemoteLoadPath);
        foreach(var old in shared.entries.Where(e=>!sharedPaths.Contains(e.AssetPath)).ToArray())settings.RemoveAssetEntry(old.guid);
        foreach(string path in sharedPaths)
        {
            string guid=AssetDatabase.AssetPathToGUID(path);if(string.IsNullOrEmpty(guid))continue;
            var prior=settings.FindAssetEntry(guid);if(prior!=null&&prior.parentGroup!=shared){Debug.Log("Keep existing shared shader address: "+path);continue;}
            settings.CreateOrMoveEntry(guid,shared).address="ArenaShared/"+path;
        }
        EditorUtility.SetDirty(sharedSchema);EditorUtility.SetDirty(shared);EditorUtility.SetDirty(settings);AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Logs");File.WriteAllLines("Logs/ArenaSharedDependencies.txt",paths.OrderBy(p=>p));
    }
    public static void RunFromCommandLine()
    {
        int code=0;
        try {Register();ArenaDistanceQuality.ApplyAllTexturePlatformCaps();ArenaDistanceQuality.Audit();ArenaDistanceQuality.RefreshGeneratedSceneMetrics();BuildAndAnalyze();}
        catch(Exception e) {Directory.CreateDirectory("Logs");File.WriteAllText("Logs/ArenaBundleValidation.error.txt",e.ToString());Debug.LogException(e);code=1;}
        EditorApplication.Exit(code);
    }
    public static void RunBuildOnlyFromCommandLine()
    {
        int code=0;try{Register();BuildAndAnalyze();}
        catch(Exception e){Directory.CreateDirectory("Logs");File.WriteAllText("Logs/ArenaBundleValidation.error.txt",e.ToString());Debug.LogException(e);code=1;}
        EditorApplication.Exit(code);
    }
    [Serializable] class LayoutRef {public long rid;}
    [Serializable] class LayoutType {public string @class;}
    [Serializable] class LayoutNode
    {
        public string AssetPath,Name;public long FileSize;public LayoutRef Bundle;
        public LayoutRef[] Files,Assets,OtherAssets,Dependencies,ExternallyReferencedAssets,InternalReferencedExplicitAssets,InternalReferencedOtherAssets;
    }
    [Serializable] class LayoutEntry {public long rid;public LayoutType type;public LayoutNode data;}
    [Serializable] class LayoutReferences {public LayoutEntry[] RefIds;}
    [Serializable] class LayoutSnapshot {public string BuildError;public LayoutReferences references;}
    static bool IsPropModel(string path)=>!string.IsNullOrEmpty(path)&&path.IndexOf("/Props/",StringComparison.OrdinalIgnoreCase)>=0&&path.EndsWith(".fbx",StringComparison.OrdinalIgnoreCase);
    static void VerifyBuiltDependencies(string output,string cache)
    {
        var layout=JsonUtility.FromJson<LayoutSnapshot>(File.ReadAllText("Library/com.unity.addressables/buildlayout.json"));
        if(!string.IsNullOrEmpty(layout.BuildError))throw new Exception(layout.BuildError);
        var entries=layout.references.RefIds.ToDictionary(e=>e.rid);
        var bundles=entries.Values.Where(e=>e.type.@class=="BuildLayout/Bundle").ToDictionary(e=>e.rid,e=>e.data);
        var models=new Dictionary<long,HashSet<string>>();
        foreach(var pair in bundles)
        {
            var found=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var file in pair.Value.Files??Array.Empty<LayoutRef>())
            {
                var node=entries[file.rid].data;
                foreach(var asset in (node.Assets??Array.Empty<LayoutRef>()).Concat(node.OtherAssets??Array.Empty<LayoutRef>()))
                {string path=entries[asset.rid].data.AssetPath;if(IsPropModel(path))found.Add(path);}
            }
            models[pair.Key]=found;
        }
        var report=new List<string>{"scene,expectedModels,bundledModels,dependencyBundles,compressedDependencyBytes,status"};int count=0;
        foreach(var entry in entries.Values.Where(e=>e.type.@class=="BuildLayout/ExplicitAsset"))
        {
            string scene=entry.data.AssetPath;
            if(!System.Text.RegularExpressions.Regex.IsMatch(scene??"",@"_Arena_0[123]_(Low|Mid|HighMid|High|Ultra)\.unity$"))continue;
            var reached=new HashSet<long>();var pending=new Stack<long>();pending.Push(entry.data.Bundle.rid);
            while(pending.Count>0){long rid=pending.Pop();if(!reached.Add(rid))continue;foreach(var dependency in bundles[rid].Dependencies??Array.Empty<LayoutRef>())if(bundles.ContainsKey(dependency.rid))pending.Push(dependency.rid);}
            var actual=new HashSet<string>(reached.SelectMany(rid=>models[rid]),StringComparer.OrdinalIgnoreCase);
            var direct=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var visited=new HashSet<long>();pending.Push(entry.rid);
            while(pending.Count>0)
            {
                long rid=pending.Pop();if(!visited.Add(rid)||!entries.ContainsKey(rid))continue;
                var node=entries[rid].data;if(IsPropModel(node.AssetPath))direct.Add(node.AssetPath);
                foreach(var dependency in (node.ExternallyReferencedAssets??Array.Empty<LayoutRef>()).Concat(node.InternalReferencedExplicitAssets??Array.Empty<LayoutRef>()).Concat(node.InternalReferencedOtherAssets??Array.Empty<LayoutRef>()))pending.Push(dependency.rid);
            }
            if(!actual.SetEquals(direct))throw new Exception("Built model variants differ from scene references: "+scene);
            report.Add($"{scene},{direct.Count},{actual.Count},{reached.Count},{reached.Sum(rid=>bundles[rid].FileSize)},PASS");count++;
        }
        if(count!=180)throw new Exception("Expected 180 built scenes, found "+count);
        File.WriteAllLines("Logs/ArenaBuiltDependencyVerification.csv",report);
    }
    public static void VerifyExistingLayoutFromCommandLine()
    {
        try{VerifyBuiltDependencies("","");File.WriteAllText("Logs/ArenaBundleValidation.txt","PASS local StandaloneWindows64 build and exact model dependency verification for all 180 scene addresses. Profiles, other content groups and the previous local runtime cache restored. Nothing published.");EditorApplication.Exit(0);}
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    [MenuItem("Tools/MusicGame/Content/Arenas/Validate Arena Bundles Locally")]
    public static void BuildAndAnalyze()
    {
        var settings=AddressableAssetSettingsDefaultObject.Settings;
        var schemas=settings.groups.Where(g=>g!=null).Select(g=>new {group=g,schema=g.GetSchema<BundledAssetGroupSchema>()}).Where(x=>x.schema!=null).ToArray();
        var included=schemas.ToDictionary(x=>x.schema,x=>x.schema.IncludeInBuild);
        string profile=settings.activeProfileId;
        string oldBuild=settings.profileSettings.GetValueByName(profile,AddressableAssetSettings.kRemoteBuildPath);
        string oldLoad=settings.profileSettings.GetValueByName(profile,AddressableAssetSettings.kRemoteLoadPath);
        int builder=settings.ActivePlayerDataBuilderIndex;
        bool oldLayout=ProjectConfigData.GenerateBuildLayout;
        string output=Path.GetFullPath("Logs/ArenaBundleValidation/"+EditorUserBuildSettings.activeBuildTarget).Replace('\\','/');
        Directory.CreateDirectory(output);
        string cache=Path.GetFullPath(UnityEngine.AddressableAssets.Addressables.BuildPath).TrimEnd(Path.DirectorySeparatorChar);
        if(!cache.StartsWith(Path.GetFullPath("Library")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Unexpected local build cache: "+cache);
        string snapshot=Path.Combine(output,"PreviousLocalRuntime-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
        string localArchive=Path.Combine(output,"ArenaLocalRuntime-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
        var originalFiles=new HashSet<string>(Directory.Exists(cache)?Directory.GetFiles(cache,"*",SearchOption.AllDirectories).Select(f=>f.Substring(cache.Length+1)):Array.Empty<string>());
        CopyTree(cache,snapshot);
        ScriptableObject packedBuilder=null;string packedBuilderState=null;
        try
        {
            foreach(var x in schemas)x.schema.IncludeInBuild=x.group.Name==ArenaContentSetup.GroupName||x.group.Name==GroupName||x.group.Name==SharedGroupName;
            settings.profileSettings.SetValue(profile,AddressableAssetSettings.kRemoteBuildPath,output);
            settings.profileSettings.SetValue(profile,AddressableAssetSettings.kRemoteLoadPath,output);
            int packed=settings.DataBuilders.FindIndex(b=>b!=null&&b.GetType().Name=="BuildScriptPackedMode");
            if(packed<0)throw new Exception("Packed content builder is unavailable");
            packedBuilder=settings.DataBuilders[packed];packedBuilderState=EditorJsonUtility.ToJson(packedBuilder);
            settings.ActivePlayerDataBuilderIndex=packed;ProjectConfigData.GenerateBuildLayout=true;
            var analysis=new CheckBundleDupeDependencies().RefreshAnalysis(settings);
            File.WriteAllLines("Logs/ArenaBundleDuplicates.txt",analysis.Select(r=>r.severity+": "+r.resultName));
            if(analysis.Any(r=>r.resultName.Contains("/Props/")))throw new Exception("Prop dependencies are still duplicated; see ArenaBundleDuplicates.txt");
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
            if(!string.IsNullOrEmpty(result.Error))throw new Exception(result.Error);
            VerifyBuiltDependencies(output,cache);
            File.WriteAllText("Logs/ArenaBundleValidation.txt","Local arena-only content build completed for "+EditorUserBuildSettings.activeBuildTarget+".\nOutput: "+output+"\nBuild layout: Library/com.unity.addressables/buildlayout\nOther content groups and original profile paths restored after validation. Nothing published.\n");
        }
        finally
        {
            Exception cacheFailure=null;
            try
            {
            CopyTree(cache,localArchive);
            if(Directory.Exists(cache))foreach(string file in Directory.GetFiles(cache,"*",SearchOption.AllDirectories))
            {
                string absolute=Path.GetFullPath(file);
                if(!absolute.StartsWith(cache+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Unexpected generated cache path");
                string relative=absolute.Substring(cache.Length+1);
                if(originalFiles.Contains(relative))continue;
                string target=Path.Combine(localArchive,"GeneratedCacheFiles",relative);Directory.CreateDirectory(Path.GetDirectoryName(target));File.Move(absolute,target);
            }
            CopyTree(snapshot,cache);
            }
            catch(Exception error){cacheFailure=error;}
            if(packedBuilder!=null&&packedBuilderState!=null){EditorJsonUtility.FromJsonOverwrite(packedBuilderState,packedBuilder);EditorUtility.SetDirty(packedBuilder);}
            foreach(var pair in included) {pair.Key.IncludeInBuild=pair.Value;EditorUtility.SetDirty(pair.Key);}
            settings.profileSettings.SetValue(profile,AddressableAssetSettings.kRemoteBuildPath,oldBuild);
            settings.profileSettings.SetValue(profile,AddressableAssetSettings.kRemoteLoadPath,oldLoad);
            settings.ActivePlayerDataBuilderIndex=builder;ProjectConfigData.GenerateBuildLayout=oldLayout;
            EditorUtility.SetDirty(settings);AssetDatabase.SaveAssets();
            if(cacheFailure!=null)throw new Exception("Could not restore the previous local runtime cache; recoverable snapshots: "+snapshot,cacheFailure);
        }
    }
}
#endif
