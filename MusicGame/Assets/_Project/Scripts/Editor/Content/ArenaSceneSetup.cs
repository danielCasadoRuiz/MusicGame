#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

/// <summary>Compiles the editable five-quality arena prefab to five independent Addressable scenes.
/// Re-run after changing the authoring composition. No bake is performed.</summary>
public static class ArenaSceneSetup
{
    public const string Master="Assets/_Project/Art/Arenas/Monteverdi/Monteverdi_Arena_01.prefab";
    public const string Folder="Assets/_Project/Art/Arenas/Monteverdi/Scenes";
    const string CatalogPath="Assets/_Project/Resources/ArenaSceneCatalog.asset";
    public static readonly string[] Qualities={"Low","Mid","HighMid","High","Ultra"};
    public static void RunFromCommandLine()
    {
        try { Build(); EditorApplication.Exit(0); }
        catch(Exception e) { File.WriteAllText("Logs/ArenaSceneSetup.txt",e.ToString()); Debug.LogException(e); EditorApplication.Exit(1); }
    }
    [MenuItem("Tools/MusicGame/Content/Arenas/Generate Monteverdi Quality Scenes")]
    public static void Build()
    {
        if(!Application.isBatchMode && UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
            throw new Exception("Save the active scene before generating arena scenes.");
        Directory.CreateDirectory(Folder); Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath)); AssetDatabase.Refresh();
        var settings=AddressableAssetSettingsDefaultObject.Settings;
        var group=settings.FindGroup(ArenaContentSetup.GroupName);
        if(group==null) throw new Exception("Run arena content setup first");
        var catalog=AssetDatabase.LoadAssetAtPath<ArenaSceneCatalog>(CatalogPath);
        if(catalog==null) { catalog=ScriptableObject.CreateInstance<ArenaSceneCatalog>(); AssetDatabase.CreateAsset(catalog,CatalogPath); }
        // NewScene(Single) unloads unused native assets even while managed locals still reference them.
        catalog.hideFlags|=HideFlags.DontUnloadUnusedAsset;
        string sourceGuid=AssetDatabase.AssetPathToGUID(Master);
        var entry=catalog.Find(sourceGuid);
        if(entry==null) {entry=new ArenaSceneCatalog.Entry{sourceArenaGuid=sourceGuid};catalog.entries.Add(entry);}
        entry.variants.Clear();
        var report=new List<string>(); string signature=null;
        foreach(string quality in Qualities)
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Master),scene);
            PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.OutermostRoot,InteractionMode.AutomatedAction);
            var selectors=root.GetComponentsInChildren<MonteverdiPropQuality>(true);
            var expectedModels=new HashSet<string>();
            int index=Array.IndexOf(Qualities,quality);
            foreach(var selector in selectors)
            {
                var selected=selector.variants[index];
                string path=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(selected);
                if(!path.EndsWith("_"+quality+".prefab")) throw new Exception("Unexpected quality source: "+path);
                foreach(string model in AssetDatabase.GetDependencies(path,true).Where(p=>p.EndsWith(".fbx") && p.Contains("/Props/"))) expectedModels.Add(model);
                var replacement=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),scene);
                replacement.name=selector.gameObject.name;
                replacement.transform.SetParent(selector.transform.parent,false);
                replacement.transform.localPosition=selector.transform.localPosition;
                replacement.transform.localRotation=selector.transform.localRotation;
                replacement.transform.localScale=selector.transform.localScale;
                replacement.SetActive(true);
                Object.DestroyImmediate(selector.gameObject);
            }
            if(root.GetComponentsInChildren<MonteverdiPropQuality>(true).Length!=0) throw new Exception("Unstripped multi-quality prop");
            string shape=string.Join("\n",root.transform.Find("MonteverdiScenery").GetComponentsInChildren<Transform>(true)
                .Where(t=>t.parent!=null && new[]{"North","South","East","West"}.Contains(t.parent.name))
                .Select(t=>$"{t.parent.name}/{t.name}|{t.localPosition:F5}|{t.localRotation:F5}|{t.localScale:F5}").OrderBy(s=>s));
            if(signature==null) signature=shape; else if(shape!=signature) throw new Exception("Composition drift: "+quality);
            var env=root.GetComponentInChildren<ArenaEnvironmentSettings>(true);
            var sunObject=new GameObject("ArenaSun"); sunObject.transform.SetParent(root.transform.Find("Lighting"),false);
            var sun=sunObject.AddComponent<Light>(); sun.type=LightType.Directional;
            sun.color=env.mainLightColor; sun.intensity=env.mainLightIntensity;
            sun.transform.rotation=Quaternion.Euler(env.mainLightEuler); sun.shadows=env.mainLightShadows; sun.shadowStrength=env.mainLightShadowStrength;
            root.AddComponent<ArenaSceneLighting>().mainLight=sun;
            RenderSettings.sun=sun; RenderSettings.skybox=env.skybox;
            RenderSettings.ambientMode=env.ambientMode; RenderSettings.ambientSkyColor=env.ambientColor;
            RenderSettings.ambientEquatorColor=env.ambientEquatorColor; RenderSettings.ambientGroundColor=env.ambientGroundColor;
            RenderSettings.ambientIntensity=env.ambientIntensity; RenderSettings.fog=env.fog;
            RenderSettings.fogColor=env.fogColor; RenderSettings.fogDensity=env.fogDensity;
            string scenePath=$"{Folder}/Monteverdi_Arena_01_{quality}.unity";
            EditorSceneManager.SaveScene(scene,scenePath);
            string guid=AssetDatabase.AssetPathToGUID(scenePath);
            var address=settings.CreateOrMoveEntry(guid,group);
            address.address=$"Arena/monteverdi/01/{quality}";
            foreach(string label in new[]{ArenaContentSetup.CategoryLabel,"Arena_monteverdi",QualityAssetResolver.LabelFor(quality)})
            { settings.AddLabel(label); address.SetLabel(label,true); }
            entry.variants.Add(new ArenaSceneCatalog.Variant{qualityName=quality,scene=new AssetReference(guid)});
            var deps=AssetDatabase.GetDependencies(scenePath,true);
            if(deps.Contains(Master) || deps.Any(p=>p.EndsWith("MonteverdiPropQuality.cs"))) throw new Exception("Authoring/master dependency leaked into scene");
            foreach(string other in Qualities.Where(q=>q!=quality))
                if(deps.Any(p=>p.Contains("/Props/") && p.Contains("/"+other+"/"))) throw new Exception("Wrong-quality dependency: "+other+" in "+quality);
            int fbx=deps.Count(p=>p.EndsWith(".fbx") && p.Contains("/Props/"));
            if(selectors.Length==0 || fbx!=expectedModels.Count) throw new Exception($"Missing selected models: expected {expectedModels.Count}, found {fbx}");
            report.Add($"PASS {quality}: identical composition, {selectors.Length} placements, {fbx} selected FBX, no other-quality/master dependencies. {address.address}");
        }
        // The prefab remains editable, but is no longer a downloadable runtime arena bundle.
        settings.RemoveAssetEntry(sourceGuid);
        catalog.hideFlags=HideFlags.None;
        EditorUtility.SetDirty(catalog); EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
        // Protect scene-backed arenas when the old generic setup is re-run.
        string arenaReport=ArenaContentSetup.Setup(out bool ok);
        if(!ok) throw new Exception(arenaReport);
        foreach(string quality in Qualities)
        {
            var r=catalog.Resolve(sourceGuid,Qualities,Array.IndexOf(Qualities,quality),out string chosen);
            if(r==null || chosen!=quality) throw new Exception("Quality resolution failed: "+quality);
        }
        report.Add("PASS quality resolution, existing composer/version/boss identities retained; legacy authoring entry excluded from Addressables.");
        File.WriteAllText("Logs/ArenaSceneSetup.txt",string.Join("\n",report)); Debug.Log(string.Join("\n",report));
    }
}
#endif
