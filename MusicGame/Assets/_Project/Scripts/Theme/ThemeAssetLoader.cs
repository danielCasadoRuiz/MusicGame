using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Owns every Addressables load/release for Theme CONTENT (MusicStyleVisualSO, EventThemeSO —
/// heavy, swappable/updatable assets; see the app-flow/Theme refactor's Addressables section for
/// why those two are Addressable while BaseTheme/registries/system configs stay local). Nothing
/// else in the project should call Addressables.LoadAssetAsync/Release for Theme content directly
/// — this is the one place that tracks handles, so a load can never be duplicated and a release
/// can never happen while something else still holds the same content.
///
/// Reference-counted: two callers requesting the SAME AssetReference share one underlying
/// Addressables load; the content is only actually released once every caller has released it.
/// This is deliberately conservative — ThemeManager (a later phase) decides WHEN to request/
/// release a style/event, this class only guarantees that's safe to do from multiple call sites
/// without double-loading or premature release.
///
/// Persistent (IAppModule, constructed by AppBootstrap) because loaded Theme content must survive
/// scene transitions (Frontend → Gameplay → Fight) without needing to reload on every scene.
/// </summary>
public class ThemeAssetLoader : MonoBehaviour, IAppModule
{
    private readonly Dictionary<string, AsyncOperationHandle> _handles   = new();
    private readonly Dictionary<string, int>                  _refCounts = new();

    void IAppModule.Initialize(AppContext context) { /* no cross-module wiring needed yet */ }

    void IAppModule.Shutdown()
    {
        // App is closing — release everything outstanding rather than leaving handles dangling.
        foreach (var handle in _handles.Values)
            if (handle.IsValid()) Addressables.Release(handle);
        _handles.Clear();
        _refCounts.Clear();
    }

    public void LoadMusicStyleVisual(AssetReferenceT<MusicStyleVisualSO> reference, Action<MusicStyleVisualSO> onLoaded) =>
        Load(reference, onLoaded);

    public void ReleaseMusicStyleVisual(AssetReferenceT<MusicStyleVisualSO> reference) => Release(reference);

    public void LoadEventTheme(AssetReferenceT<EventThemeSO> reference, Action<EventThemeSO> onLoaded) =>
        Load(reference, onLoaded);

    public void ReleaseEventTheme(AssetReferenceT<EventThemeSO> reference) => Release(reference);

    public void LoadFrontendVisual(AssetReferenceT<FrontendVisualPresetSO> reference, Action<FrontendVisualPresetSO> onLoaded) =>
        Load(reference, onLoaded);

    public void ReleaseFrontendVisual(AssetReferenceT<FrontendVisualPresetSO> reference) => Release(reference);

    private void Load<T>(AssetReferenceT<T> reference, Action<T> onLoaded) where T : UnityEngine.Object
    {
        if (reference == null || !reference.RuntimeKeyIsValid())
        {
            onLoaded?.Invoke(null);
            return;
        }

        string key = reference.AssetGUID;

        if (_handles.TryGetValue(key, out var existing))
        {
            _refCounts[key]++;
            if (existing.IsDone)
                onLoaded?.Invoke(existing.Result as T);
            else
                existing.Completed += h => onLoaded?.Invoke(h.Result as T);
            return;
        }

        var handle = Addressables.LoadAssetAsync<T>(reference);
        _handles[key]   = handle;
        _refCounts[key] = 1;
        handle.Completed += h =>
        {
            if (h.Status != AsyncOperationStatus.Succeeded)
                Debug.LogWarning($"[ThemeAssetLoader] Failed to load Addressable '{key}' ({typeof(T).Name}) — " +
                                  "is it actually marked Addressable and included in a build? " +
                                  "Callers get null and should fall back to BaseTheme.");
            onLoaded?.Invoke(h.Status == AsyncOperationStatus.Succeeded ? h.Result : null);
        };
    }

    private void Release<T>(AssetReferenceT<T> reference) where T : UnityEngine.Object
    {
        if (reference == null) return;
        string key = reference.AssetGUID;
        if (!_refCounts.TryGetValue(key, out int count)) return;

        count--;
        if (count > 0) { _refCounts[key] = count; return; }

        if (_handles.TryGetValue(key, out var handle) && handle.IsValid())
            Addressables.Release(handle);
        _handles.Remove(key);
        _refCounts.Remove(key);
    }
}
