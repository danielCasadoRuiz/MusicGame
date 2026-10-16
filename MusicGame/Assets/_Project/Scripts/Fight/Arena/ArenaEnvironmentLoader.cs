using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// The fight's ARENA ENVIRONMENT, loaded on demand through Addressables (one bundle per arena) and owned
/// by FightSceneBootstrap — the same path for composer arenas and final-rival stage arenas.
///
/// OWNERSHIP: the arena prefab owns floor, decor, background/midground/foreground and any EXTRA lights /
/// global ambience (ArenaEnvironmentSettings, applied on load and restored on unload). The Fight scene owns
/// the camera, the main directional light and the fighters; FightArenaConfig owns spawns and movement
/// bounds. Cameras / AudioListeners found inside an arena are disabled. Exactly one arena instance exists:
///
///   Request(arena, key)   same key as the current one (Fight Again) → nothing happens;
///                         otherwise the previous arena is destroyed and its handle released, then the new
///                         one loads (a newer Request while loading wins: the stale load is released unused)
///   null / invalid / failed reference → the grey placeholder floor (always works)
///   OnDestroy (leaving Fight) → instance destroyed, handle released, ambience restored
/// </summary>
public class ArenaEnvironmentLoader : MonoBehaviour
{
    private GameObject _placeholder;
    private GameObject _instance;
    private AsyncOperationHandle<GameObject> _handle;
    private string _currentKey;
    private int _token;
    private ArenaEnvironmentSettings.Snapshot? _restore;

    /// <summary>False while an arena is downloading/loading (the Versus waits for it, with a cap).</summary>
    public bool IsReady { get; private set; } = true;
    public string CurrentKey => _currentKey;
    public GameObject Instance => _instance;

    public void Initialize(GameObject placeholderFloor) => _placeholder = placeholderFloor;

    /// <param name="key">Identity of the fight this arena belongs to (rival + version + boss stage): the same
    /// key keeps the arena (Fight Again).</param>
    public void Request(AssetReferenceGameObject arena, string key)
    {
        if (key == _currentKey) return;
        _currentKey = key;
        int token = ++_token;
        Unload();

        if (arena == null || !arena.RuntimeKeyIsValid())
        {
            ShowPlaceholder(true);
            IsReady = true;
            return;
        }
        IsReady = false;
        _ = LoadAsync(arena.RuntimeKey, token);
    }

    private async Task LoadAsync(object runtimeKey, int token)
    {
        AsyncOperationHandle<GameObject> handle = default;
        try
        {
            handle = Addressables.LoadAssetAsync<GameObject>(runtimeKey);
            await handle.Task;
        }
        catch (System.Exception e) { Debug.LogError($"[Arena] Loading '{runtimeKey}' threw: {e.Message}"); }

        if (this == null || token != _token)
        {
            if (handle.IsValid()) Addressables.Release(handle); // superseded or Fight left meanwhile
            return;
        }
        if (!handle.IsValid() || handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
        {
            Debug.LogError($"[Arena] Arena '{runtimeKey}' could not be loaded — placeholder floor used.");
            if (handle.IsValid()) Addressables.Release(handle);
            ShowPlaceholder(true);
            IsReady = true;
            return;
        }

        _handle = handle;
        _instance = Instantiate(handle.Result, Vector3.zero, Quaternion.identity);
        _instance.name = handle.Result.name;
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_instance, gameObject.scene);
        _instance.SetActive(true); // an arena saved with an inactive root must still show
        foreach (var cam in _instance.GetComponentsInChildren<Camera>(true)) cam.enabled = false;
        foreach (var listener in _instance.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
        var settings = _instance.GetComponentInChildren<ArenaEnvironmentSettings>(true);
        if (settings != null) _restore = settings.Apply(FindSceneSun(), FightSceneBootstrap.Instance != null && FightSceneBootstrap.Instance.CameraController != null
                                                                          ? FightSceneBootstrap.Instance.CameraController.Camera : null);
        ShowPlaceholder(false);
        IsReady = true;
        Debug.Log($"[Arena] Loaded '{_instance.name}' ({_currentKey}).");
    }

    private void Unload()
    {
        if (_restore.HasValue) { ArenaEnvironmentSettings.Restore(_restore.Value); _restore = null; }
        if (_instance != null) Destroy(_instance);
        _instance = null;
        if (_handle.IsValid()) Addressables.Release(_handle);
        _handle = default;
    }

    /// <summary>The Fight scene's own main directional light (RenderSettings.sun, else the first directional
    /// light of this scene that is not part of the arena) — the one an arena may re-tint for its daylight.</summary>
    private Light FindSceneSun()
    {
        if (RenderSettings.sun != null && (_instance == null || !RenderSettings.sun.transform.IsChildOf(_instance.transform))) return RenderSettings.sun;
        foreach (var root in gameObject.scene.GetRootGameObjects())
        {
            if (_instance != null && root == _instance) continue;
            foreach (var light in root.GetComponentsInChildren<Light>(true))
                if (light.type == LightType.Directional) return light;
        }
        return null;
    }

    private void ShowPlaceholder(bool show)
    {
        if (_placeholder != null) _placeholder.SetActive(show);
    }

    private void OnDestroy()
    {
        _token++;
        Unload();
    }
}

/// <summary>Arena choice for a composer, by the rival's VERSION (OpponentLevelConfig.level — the same per-level
/// logic as the rival's versions): version 1 → the 1st valid entry of OpponentDefinition.arenas, 2 → the 2nd…,
/// a version beyond the list uses the last one. Only without a version (0) is it a random valid entry, avoiding
/// the last one used for that composer when there is an alternative. Runtime state only (never written into
/// the content ScriptableObject).</summary>
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
