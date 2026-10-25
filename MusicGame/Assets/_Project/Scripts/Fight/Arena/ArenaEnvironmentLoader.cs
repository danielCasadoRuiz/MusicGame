using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

/// <summary>Owns exactly one arena and its handles. Scene qualities load additively and are
/// fully activated before fighters spawn. Unconverted arenas retain the existing prefab fallback.
/// Requests serialize unload/load; a stale or abandoned scene load always unloads its result.</summary>
public class ArenaEnvironmentLoader : MonoBehaviour
{
    private GameObject _placeholder, _instance;
    private Scene _ownerScene, _scene;
    private AsyncOperationHandle<GameObject> _prefabHandle;
    private AsyncOperationHandle<SceneInstance> _sceneHandle;
    private ArenaEnvironmentSettings.Snapshot? _restore;
    private Light _fightSun; private bool _fightSunEnabled;
    private readonly SemaphoreSlim _gate=new(1,1);
    private int _token; private bool _disposed, _ownsScene;
    private Task _shutdownTask;
    public bool IsReady { get; private set; }=true;
    public string CurrentKey { get; private set; }
    public string LoadedQuality { get; private set; }
    public GameObject Instance => _instance;
    public Scene LoadedScene => _scene;
    public Task LoadingTask { get; private set; }=Task.CompletedTask;
    public static int LiveSceneHandles { get; private set; }

    public void Initialize(GameObject placeholder)
    { _placeholder=placeholder; _ownerScene=gameObject.scene; }

    public void Request(AssetReferenceGameObject arena,string key)
    {
        if(_disposed || CurrentKey==key) return;
        CurrentKey=key; IsReady=false;
        LoadingTask=LoadAsync(arena,++_token);
    }
    private bool IsCurrent(int token) => !_disposed && this!=null && token==_token;
    private async Task LoadAsync(AssetReferenceGameObject arena,int token)
    {
        await _gate.WaitAsync();
        try
        {
            if(!IsCurrent(token)) return;
            await ReleaseCurrentAsync();
            if(!IsCurrent(token)) return;
            ShowPlaceholder(true);
            var sceneRef=ArenaSceneCatalog.Resolve(arena,out string quality);
            bool registered=arena!=null && ArenaSceneCatalog.Current!=null && ArenaSceneCatalog.Current.Contains(arena.AssetGUID);
            if(sceneRef!=null)
            {
                // Never preload an unactivated scene: it blocks Unity's asynchronous operation queue.
                var handle=Addressables.LoadSceneAsync(sceneRef.RuntimeKey,LoadSceneMode.Additive,true);
                bool handedOff=false;
                try
                {
                    await handle.Task;
                    if(handle.Status!=AsyncOperationStatus.Succeeded || !handle.Result.Scene.isLoaded)
                        throw new System.Exception("Arena scene failed: "+sceneRef.RuntimeKey);
                    if(!IsCurrent(token)) return;
                    _sceneHandle=handle; _scene=handle.Result.Scene; _ownsScene=true; LiveSceneHandles++; handedOff=true;
                    foreach(var root in _scene.GetRootGameObjects())
                    {
                        var marker=root.GetComponentInChildren<ArenaEnvironment>(true);
                        if(marker!=null) { _instance=marker.gameObject; break; }
                    }
                    if(_instance==null) throw new System.Exception("Arena scene has no ArenaEnvironment marker");
                    _fightSun=FindFightSun();
                    if(_fightSun!=null) { _fightSunEnabled=_fightSun.enabled; _fightSun.enabled=false; }
                    var lighting=_instance.GetComponentInChildren<ArenaSceneLighting>(true);
                    ApplyEnvironment(lighting!=null?lighting.mainLight:null);
                    SceneManager.SetActiveScene(_scene);
                    // Built-in probe groups need their interpolation cells rebuilt after additive loading.
                    if(LightmapSettings.lightProbes!=null && LightmapSettings.lightProbes.count>0) LightProbes.Tetrahedralize();
                    LoadedQuality=quality;
                    Debug.Log($"[Arena] Scene '{_scene.name}' ready, quality {quality}; fighters may now spawn.");
                }
                finally
                {
                    if(!handedOff) await ReleaseSceneAsync(handle);
                }
            }
            else if(registered)
                throw new System.Exception("Arena is registered for scenes but has no valid quality variant");
            else if(arena!=null && arena.RuntimeKeyIsValid())
            {
                var handle=Addressables.LoadAssetAsync<GameObject>(arena.RuntimeKey);
                bool handedOff=false;
                try
                {
                    await handle.Task;
                    if(handle.Status!=AsyncOperationStatus.Succeeded || handle.Result==null) throw new System.Exception("Arena prefab failed");
                    if(!IsCurrent(token)) return;
                    _prefabHandle=handle; handedOff=true;
                    _instance=Instantiate(handle.Result,Vector3.zero,Quaternion.identity);
                    _instance.name=handle.Result.name; SceneManager.MoveGameObjectToScene(_instance,_ownerScene);
                    _instance.SetActive(true); ApplyEnvironment(FindFightSun());
                }
                finally { if(!handedOff && handle.IsValid()) Addressables.Release(handle); }
            }
            if(IsCurrent(token)) { ShowPlaceholder(_instance==null); IsReady=true; }
        }
        catch(System.Exception e)
        {
            await ReleaseCurrentAsync();
            if(IsCurrent(token)) { Debug.LogError("[Arena] "+e+"; using placeholder floor."); ShowPlaceholder(true); IsReady=true; }
        }
        finally { _gate.Release(); }
    }
    private void ApplyEnvironment(Light sun)
    {
        foreach(var cam in _instance.GetComponentsInChildren<Camera>(true)) cam.enabled=false;
        foreach(var listener in _instance.GetComponentsInChildren<AudioListener>(true)) listener.enabled=false;
        var settings=_instance.GetComponentInChildren<ArenaEnvironmentSettings>(true);
        if(settings!=null) _restore=settings.Apply(sun,FightSceneBootstrap.Instance!=null?FightSceneBootstrap.Instance.ArenaCamera:null);
    }
    private Light FindFightSun()
    {
        if(!_ownerScene.IsValid() || !_ownerScene.isLoaded) return null;
        foreach(var root in _ownerScene.GetRootGameObjects())
            foreach(var light in root.GetComponentsInChildren<Light>(true))
                if(light.type==LightType.Directional && (_instance==null || !light.transform.IsChildOf(_instance.transform))) return light;
        return null;
    }
    private async Task ReleaseCurrentAsync()
    {
        bool ownsLighting=SceneManager.GetActiveScene()==_scene || SceneManager.GetActiveScene()==_ownerScene;
        if(SceneManager.GetActiveScene()==_scene && _ownerScene.IsValid() && _ownerScene.isLoaded) SceneManager.SetActiveScene(_ownerScene);
        if(_restore.HasValue && ownsLighting) ArenaEnvironmentSettings.Restore(_restore.Value);
        _restore=null;
        if(_fightSun!=null) _fightSun.enabled=_fightSunEnabled;
        _fightSun=null;
        if(_ownsScene)
        {
            var h=_sceneHandle; _ownsScene=false; _sceneHandle=default; _scene=default; _instance=null;
            LiveSceneHandles--; await ReleaseSceneAsync(h);
        }
        else if(_instance!=null) { Destroy(_instance); _instance=null; }
        if(_prefabHandle.IsValid()) Addressables.Release(_prefabHandle);
        _prefabHandle=default; LoadedQuality=null;
    }
    private static async Task ReleaseSceneAsync(AsyncOperationHandle<SceneInstance> handle)
    {
        if(!handle.IsValid()) return;
        if(handle.Status==AsyncOperationStatus.Succeeded && handle.Result.Scene.isLoaded)
        {
            var unload=Addressables.UnloadSceneAsync(handle,true);
            await unload.Task;
        }
        else Addressables.Release(handle);
    }
    public Task ShutdownAsync()
    {
        if(_shutdownTask!=null) return _shutdownTask;
        _disposed=true; ++_token; IsReady=false;
        return _shutdownTask=ShutdownCoreAsync();
    }
    private async Task ShutdownCoreAsync()
    {
        await _gate.WaitAsync();
        try { await ReleaseCurrentAsync(); }
        finally { _gate.Release(); }
    }
    private void ShowPlaceholder(bool show) { if(_placeholder!=null) _placeholder.SetActive(show); }
    private void OnDestroy() { _=ShutdownAsync(); }
}

public static class ArenaSelector
{
    private static readonly Dictionary<string, int> s_last = new();
    private static readonly System.Random s_rng = new();

    public static AssetReferenceGameObject Pick(OpponentDefinition composer, int version = 0)
    {
        if (composer == null || composer.arenas == null || composer.arenas.Count == 0) return null;
        var valid = new List<int>();
        for (int i = 0; i < composer.arenas.Count; i++)
            if (composer.arenas[i] != null && composer.arenas[i].RuntimeKeyIsValid()) valid.Add(i);
        if (valid.Count == 0) return null;
        string id = composer.id ?? composer.name;
        int chosen;
        if (version >= 1) chosen = valid[Mathf.Min(version - 1, valid.Count - 1)];
        else
        {
            if (valid.Count > 1 && s_last.TryGetValue(id, out int last)) valid.Remove(last);
            chosen = valid[s_rng.Next(valid.Count)];
        }
        s_last[id] = chosen;
        return composer.arenas[chosen];
    }

    /// <summary>Tests: the index Pick would avoid next.</summary>
    public static int LastIndex(string composerId) => s_last.TryGetValue(composerId, out int i) ? i : -1;
}
