using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

/// <summary>
/// Runtime access to the generated composer voices (ComposerVoicePack bundles via Addressables).
///   Preload(id)            the rival is known (Opponent Selection): its pack for the ACTIVE language starts
///                          loading; every other composer's packs are released.
///   GetLineAsync(...)      the clip of a dialogue line in the language its TEXT is shown in, only if that pack
///                          exists, the line is in it and its text hash matches the text now shown — else null
///                          (the subtitle shows alone; nothing ever blocks). Bounded by the caller's cancellation.
///   GetEfforts(id)         the composer's vocalisations (shared recorded clips in the GameSfxLibrary).
///   ReleaseAll()           leaving Fight.
/// A pack that is not in the catalog (a language without generated audio yet) is simply absent — no error.
/// </summary>
public static class ComposerVoiceService
{
    private sealed class Held
    {
        public AsyncOperationHandle<ComposerVoicePack> Handle;
        public Task<ComposerVoicePack> Task;
    }

    private static readonly Dictionary<string, Held> s_Packs = new(); // address → handle

    /// <summary>Diagnostics / tests: the pack addresses currently held (requested and not released).</summary>
    public static IEnumerable<string> HeldAddresses => s_Packs.Keys;

    public static void Preload(string composerId)
    {
        if (string.IsNullOrEmpty(composerId)) return;
        ReleaseAllExcept(composerId);
        _ = PreloadLanguageAsync(composerId);
    }

    private static async Task PreloadLanguageAsync(string composerId)
    {
        var locale = await ComposerDialogueService.ActiveLocaleAsync();
        if (locale != null) await LoadAsync(ComposerVoicePack.Address(composerId, locale.Identifier.Code));
    }

    /// <summary>The composer's vocalisations: shared recorded clips in the GameSfxLibrary (not part of any voice pack).</summary>
    public static VoiceEffortSet GetEfforts(string composerId) =>
        GameAudio.Instance != null && GameAudio.Instance.Library != null ? GameAudio.Instance.Library.EffortsFor(composerId) : null;

    /// <summary>The voiced clip for exactly this text, or null. Never throws; cancellation → null.</summary>
    public static async Task<AudioClip> GetLineAsync(string composerId, string key, string text, string localeCode, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(composerId) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(localeCode)) return null;
        try
        {
            var pack = await LoadAsync(ComposerVoicePack.Address(composerId, localeCode));
            if (ct.IsCancellationRequested || pack == null || !pack.TryGet(key, out var line) || line.clip == null) return null;
            if (line.textHash != VoiceTextHash.Of(text))
            {
                Debug.Log($"[ComposerVoice] '{key}' ({localeCode}): the text changed since its audio was generated — subtitle only.");
                return null;
            }
            var clip = line.clip;
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            while (clip.loadState == AudioDataLoadState.Loading && !ct.IsCancellationRequested) await Task.Yield();
            return !ct.IsCancellationRequested && clip.loadState == AudioDataLoadState.Loaded ? clip : null;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[ComposerVoice] '{key}' ({localeCode}) unavailable: {e.Message}");
            return null;
        }
    }

    // One load per address, shared by every caller; null when the pack does not exist or fails.
    private static Task<ComposerVoicePack> LoadAsync(string address)
    {
        if (s_Packs.TryGetValue(address, out var held)) return held.Task;
        held = new Held();
        held.Task = LoadNewAsync(address, held);
        s_Packs[address] = held;
        return held.Task;
    }

    private static async Task<ComposerVoicePack> LoadNewAsync(string address, Held held)
    {
        try
        {
            var locs = Addressables.LoadResourceLocationsAsync(address, typeof(ComposerVoicePack));
            IList<IResourceLocation> found = await locs.Task;
            bool exists = locs.Status == AsyncOperationStatus.Succeeded && found != null && found.Count > 0;
            Addressables.Release(locs);
            if (!exists) return null;                                   // no audio for this language / composer yet
            held.Handle = Addressables.LoadAssetAsync<ComposerVoicePack>(address);
            var pack = await held.Handle.Task;
            if (held.Handle.Status != AsyncOperationStatus.Succeeded) return null;
            if (pack != null) foreach (var l in pack.lines) if (l.clip != null && l.clip.loadState == AudioDataLoadState.Unloaded) l.clip.LoadAudioData();
            if (pack != null && pack.efforts != null)
                foreach (var cue in pack.efforts.All()) if (cue.clips != null) foreach (var c in cue.clips) if (c != null) c.LoadAudioData();
            return pack;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[ComposerVoice] Could not load '{address}': {e.Message} — subtitles only.");
            return null;
        }
    }

    public static void ReleaseAllExcept(string composerId)
    {
        string keep = composerId != null ? $"VoicePack/{composerId}/" : null;
        foreach (var address in new List<string>(s_Packs.Keys))
            if (keep == null || !address.StartsWith(keep)) Release(address);
    }

    public static void ReleaseAll() => ReleaseAllExcept(null);

    private static void Release(string address)
    {
        if (!s_Packs.TryGetValue(address, out var held)) return;
        s_Packs.Remove(address);
        if (held.Task.IsCompleted) { if (held.Handle.IsValid()) Addressables.Release(held.Handle); }
        else _ = ReleaseWhenDone(held);                                  // never leak a handle still loading
    }

    private static async Task ReleaseWhenDone(Held held)
    {
        try { await held.Task; } catch { }
        if (held.Handle.IsValid()) Addressables.Release(held.Handle);
    }
}
