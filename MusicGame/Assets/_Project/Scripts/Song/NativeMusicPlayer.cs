using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public static class NativeMusicPlayer
{
    [Serializable] public class Item { public string path, title, artist, key; }
    [Serializable] private class Queue { public List<Item> items; }
    private static AsyncOperationHandle<TextAsset> s_pendingDownload;
    public static void CancelPreparation()
    {
        if (s_pendingDownload.IsValid()) Addressables.Release(s_pendingDownload);
        s_pendingDownload = default;
    }
    public static string CachedImportPath(string id) => Path.Combine(Application.persistentDataPath, "JukeboxAudio", id + ".wav");
    public static bool Supported => Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer;
    public static IEnumerator Prepare(List<JukeboxTrack> tracks, Action<List<Item>> completed)
    {
        var catalog = Resources.Load<JukeboxNativeCatalogSO>("JukeboxNativeCatalog");
        string folder = Path.Combine(Application.persistentDataPath, "JukeboxAudio");
        if (!TryCreateFolder(folder)) { completed(null); yield break; }
        var queue = new List<Item>();
        foreach (var track in tracks)
        {
            string path = track.path;
            if (!track.Imported)
            {
                var entry = catalog != null ? catalog.Find(track.id) : null;
                if (SongCatalogService.TryGetEntry(track.id, out var published) && !string.IsNullOrEmpty(published.nativeAudioAddress))
                    entry = new JukeboxNativeCatalogSO.Entry { songId = track.id, address = published.nativeAudioAddress, revision = published.nativeAudioRevision };
                if (entry == null) { completed(null); yield break; }
                path = Path.Combine(folder, track.id + "_" + entry.revision + ".tps");
                if (!File.Exists(path))
                {
                    var handle = Addressables.LoadAssetAsync<TextAsset>(entry.address);
                    s_pendingDownload = handle;
                    try
                    {
                        yield return handle;
                        if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null) { completed(null); yield break; }
                        if (!TryWrite(path, handle.Result.bytes)) { completed(null); yield break; }
                    }
                    finally
                    {
                        if (handle.IsValid()) Addressables.Release(handle);
                        s_pendingDownload = default;
                    }
                }
            }
            else
            {
                // Decode imports once to a native-friendly PCM file (including OGG on iOS).
                string native = CachedImportPath(track.id);
                if (!File.Exists(native))
                {
                    AudioClip clip = null;
                    yield return LocalAudioClipLoader.Load(path, loaded => clip = loaded);
                    if (clip == null) { completed(null); yield break; }
                    bool success = false;
                    try { yield return WriteWave(clip, native, ok => success = ok); }
                    finally { UnityEngine.Object.Destroy(clip); }
                    if (!success) { completed(null); yield break; }
                }
                path = native;
            }
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { completed(null); yield break; }
            string key = null;
            if (!track.Imported)
            {
                var validation = ReadProtectedKeyAsync(path);
                while (!validation.IsCompleted) yield return null;
                if (validation.Status != TaskStatus.RanToCompletion)
                {
                    Debug.LogWarning("[TPS] Protected native audio unavailable: " + validation.Exception?.GetBaseException().Message);
                    try { if (File.Exists(path)) File.Delete(path); } catch (IOException e) { Debug.LogWarning(e.Message); }
                    completed(null); yield break;
                }
                key = validation.Result;
                // Only derived legacy copies in this app-owned folder are removed, never sources.
                foreach (string legacy in Directory.GetFiles(folder, track.id + "_*.m4a")) File.Delete(legacy);
            }
            queue.Add(new Item { path = path, title = track.title, artist = track.artist ?? "", key = key });
        }
        completed(queue);
    }
    private static Task<string> ReadProtectedKeyAsync(string path)
    {
        try
        {
            byte[] envelope = File.ReadAllBytes(path);
            // Resolve Resources on the Unity thread; authenticate/decrypt on a worker so preparing
            // a cached playlist does not decrypt the entire collection in a single frame.
            byte[] keys = TpsKeyring.Resolve(TpsCrypto.KeyId(envelope));
            return Task.Run(() =>
            {
                try
                {
                    byte[] clear = TpsCrypto.Decrypt(envelope, keys, TpsCrypto.Aac);
                    Array.Clear(clear, 0, clear.Length);
                    return Convert.ToBase64String(keys);
                }
                finally { Array.Clear(keys, 0, keys.Length); }
            });
        }
        catch (Exception e) { return Task.FromException<string>(e); }
    }
    private static bool TryCreateFolder(string folder)
    {
        try { Directory.CreateDirectory(folder); return true; }
        catch (Exception e) { Debug.LogWarning("[Jukebox] " + e.Message); return false; }
    }
    private static bool TryWrite(string path, byte[] bytes)
    {
        try { File.WriteAllBytes(path + ".tmp", bytes); File.Move(path + ".tmp", path); return true; }
        catch (Exception e) { Debug.LogWarning("[Jukebox] " + e.Message); return false; }
    }
    private static IEnumerator WriteWave(AudioClip clip, string path, Action<bool> completed)
    {
        BinaryWriter writer = null;
        try { writer = new BinaryWriter(File.Create(path + ".tmp")); }
        catch (Exception e) { Debug.LogWarning(e.Message); }
        if (writer == null) { completed(false); yield break; }
        bool good = true;
        using (writer)
        {
            try
            {
                int bytes = checked(clip.samples * clip.channels * 2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(bytes + 36);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)clip.channels); writer.Write(clip.frequency);
                writer.Write(clip.frequency * clip.channels * 2); writer.Write((short)(clip.channels * 2)); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
            }
            catch (Exception e) { Debug.LogWarning(e.Message); good = false; }
            const int frames = 8192;
            var samples = new float[frames * clip.channels];
            for (int frame = 0, chunk = 0; good && frame < clip.samples; frame += frames, chunk++)
            {
                int count = Math.Min(frames, clip.samples - frame) * clip.channels;
                try
                {
                    if (!clip.GetData(samples, frame)) { good = false; break; }
                    for (int i = 0; i < count; i++) writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(samples[i], -1, 1) * 32767));
                }
                catch (Exception e) { Debug.LogWarning(e.Message); good = false; }
                if (chunk % 32 == 31) yield return null;
            }
        }
        if (good)
        {
            try { File.Move(path + ".tmp", path); }
            catch (Exception e) { Debug.LogWarning(e.Message); good = false; }
        }
        completed(good);
    }
    public static void Start(List<Item> items, int index)
    {
        string wrapped = JsonUtility.ToJson(new Queue { items = items });
        string json = wrapped.Substring(wrapped.IndexOf('['));
        json = json.Substring(0, json.LastIndexOf(']') + 1);
#if UNITY_ANDROID && !UNITY_EDITOR
        using var bridge = new AndroidJavaClass("com.tempofury.music.MusicService");
        using var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        using var activity = unity.GetStatic<AndroidJavaObject>("currentActivity");
        bridge.CallStatic("start", activity, json, index);
#elif UNITY_IOS && !UNITY_EDITOR
        TFMusicStart(json, index);
#endif
    }
    public static void Command(string command)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using var bridge = new AndroidJavaClass("com.tempofury.music.MusicService"); bridge.CallStatic("command", command);
#elif UNITY_IOS && !UNITY_EDITOR
        TFMusicCommand(command);
#endif
    }
    public static void SetVolume(float volume)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using var bridge = new AndroidJavaClass("com.tempofury.music.MusicService"); bridge.CallStatic("volume", Mathf.Clamp01(volume));
#elif UNITY_IOS && !UNITY_EDITOR
        TFMusicVolume(Mathf.Clamp01(volume));
#endif
    }
    public static int Index
    {
        get
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var bridge = new AndroidJavaClass("com.tempofury.music.MusicService"); return bridge.CallStatic<int>("index");
#elif UNITY_IOS && !UNITY_EDITOR
            return TFMusicIndex();
#else
            return -1;
#endif
        }
    }
    public static bool Playing
    {
        get
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var bridge = new AndroidJavaClass("com.tempofury.music.MusicService"); return bridge.CallStatic<bool>("isPlaying");
#elif UNITY_IOS && !UNITY_EDITOR
            return TFMusicPlaying();
#else
            return false;
#endif
        }
    }
    public static float PositionSeconds
    {
        get
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var bridge = new AndroidJavaClass("com.tempofury.music.MusicService"); return bridge.CallStatic<float>("positionSeconds");
#elif UNITY_IOS && !UNITY_EDITOR
            return TFMusicPosition();
#else
            return 0f;
#endif
        }
    }
    public static string Error
    {
        get
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var bridge = new AndroidJavaClass("com.tempofury.music.MusicService"); return bridge.CallStatic<string>("error");
#elif UNITY_IOS && !UNITY_EDITOR
            return Marshal.PtrToStringAnsi(TFMusicError());
#else
            return "";
#endif
        }
    }
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void TFMusicStart(string json, int index);
    [DllImport("__Internal")] private static extern void TFMusicCommand(string command);
    [DllImport("__Internal")] private static extern int TFMusicIndex();
    [DllImport("__Internal")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool TFMusicPlaying();
    [DllImport("__Internal")] private static extern IntPtr TFMusicError();
    [DllImport("__Internal")] private static extern void TFMusicVolume(float volume);
    [DllImport("__Internal")] private static extern float TFMusicPosition();
#endif
}
