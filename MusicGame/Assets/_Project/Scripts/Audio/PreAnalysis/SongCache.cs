using System.IO;
using UnityEngine;

public static class SongCache
{
    private static string CachePath(AudioClip clip)
        => Path.Combine(Application.persistentDataPath, "SongCache", clip.name + "_" + clip.samples + ".json");

    public static bool TryLoad(AudioClip clip, out SongProfile profile)
    {
        profile = null;
        string path = CachePath(clip);
        if (!File.Exists(path)) return false;

        try
        {
            var data = JsonUtility.FromJson<SongProfileData>(File.ReadAllText(path));
            if (data == null || !data.Matches(clip)) return false;
            profile = data.ToProfile();
            Debug.Log($"[SongCache] Loaded from cache: {clip.name}");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SongCache] Cache load failed: {e.Message}");
            return false;
        }
    }

    public static void Save(AudioClip clip, SongProfile profile)
    {
        string path = CachePath(clip);
        Directory.CreateDirectory(Path.GetDirectoryName(path));

        try
        {
            File.WriteAllText(path, JsonUtility.ToJson(SongProfileData.From(clip, profile), true));
            Debug.Log($"[SongCache] Saved cache: {clip.name}");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SongCache] Cache save failed: {e.Message}");
        }
    }
}
