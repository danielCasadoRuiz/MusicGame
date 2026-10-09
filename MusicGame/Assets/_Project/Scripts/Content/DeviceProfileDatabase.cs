using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Device → recommended Unity quality level rules (JSON, versioned). Sources, best version wins:
///   1. local, bundled:  Resources/Content/DeviceProfiles.json (always available, offline fallback)
///   2. remote, cached:  persistentDataPath/Content/DeviceProfiles.remote.json — the last valid remote
///                       copy (ContentDeliveryConfigSO.remoteDeviceProfilesUrl), so updated rules apply
///                       even offline and without a new build.
/// Rule order (first hit wins): exact device model → GPU family → hardware rules → platform default.
/// The JSON picks the QUALITY LEVEL (which drives the URP asset + the 3D asset variants); it never
/// duplicates the Unity graphics configuration. On top of that, a small optional set of runtime
/// overrides may be given per rule (targetFPS, renderScale, shadowDistance) and per-quality defaults
/// live under "qualityDefaults" (e.g. the mobile target FPS of each level). Unknown fields are ignored.
/// </summary>
public class DeviceProfileDatabase
{
    public const string LocalResourcePath = "Content/DeviceProfiles";

    public int version;
    /// <summary>Platform key ("Android", "iOS", "Windows", "macOS", "Linux", "Default") → quality name.</summary>
    public Dictionary<string, string> platformDefaults = new();
    public List<DeviceRule> devices = new();
    public List<GpuRule> gpuFamilies = new();
    public List<HardwareRule> hardwareRules = new();
    /// <summary>Per quality level (name → defaults), e.g. { "Low": { "targetFPS": 30 } }.</summary>
    public Dictionary<string, GraphicsOverrides> qualityDefaults = new();

    /// <summary>Optional runtime overrides (null = not set). Applied by DeviceGraphicsApplier.</summary>
    public class GraphicsOverrides
    {
        /// <summary>Mobile: Application.targetFrameRate (capped to the display refresh rate). Desktop: an
        /// explicit cap (VSync off); unset on desktop = the quality level's VSync.</summary>
        public int? targetFPS;
        public float? renderScale;
        public float? shadowDistance;
        public int? maxVisibleCharacters; // prepared for gameplay systems (not applied yet)
    }

    public class DeviceRule : GraphicsOverrides { public string model; public string platform; public string quality; }
    public class GpuRule : GraphicsOverrides { public string pattern; public string platform; public string quality; }
    public class HardwareRule : GraphicsOverrides
    {
        public string platform;
        public int minSystemMemoryMB;
        public int minGraphicsMemoryMB;
        public int minProcessorCount;
        public string quality;
    }

    [JsonIgnore] public string Source { get; private set; } = "none";

    public static DeviceProfileDatabase Parse(string json, string source)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var db = JsonConvert.DeserializeObject<DeviceProfileDatabase>(json);
            if (db == null) return null;
            db.platformDefaults ??= new(); db.devices ??= new(); db.gpuFamilies ??= new(); db.hardwareRules ??= new(); db.qualityDefaults ??= new();
            db.Source = source;
            return db;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[DeviceProfileDatabase] Invalid device profiles JSON ({source}): {e.Message}");
            return null;
        }
    }

    public static string RemoteCachePath => Path.Combine(Application.persistentDataPath, "Content", "DeviceProfiles.remote.json");

    public static DeviceProfileDatabase LoadLocal()
    {
        var asset = Resources.Load<TextAsset>(LocalResourcePath);
        return asset != null ? Parse(asset.text, "local") : null;
    }

    public static DeviceProfileDatabase LoadCachedRemote()
    {
        try { return File.Exists(RemoteCachePath) ? Parse(File.ReadAllText(RemoteCachePath), "remote-cache") : null; }
        catch (System.Exception e) { Debug.LogWarning($"[DeviceProfileDatabase] Remote cache unreadable: {e.Message}"); return null; }
    }

    public static void SaveRemoteCache(string json)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RemoteCachePath));
            File.WriteAllText(RemoteCachePath, json);
        }
        catch (System.Exception e) { Debug.LogWarning($"[DeviceProfileDatabase] Could not cache remote profiles: {e.Message}"); }
    }

    /// <summary>The newest valid rules available right now (cached remote overrides local when its
    /// version is equal or higher).</summary>
    public static DeviceProfileDatabase LoadBest()
    {
        var local = LoadLocal();
        var remote = LoadCachedRemote();
        if (remote != null && (local == null || remote.version >= local.version)) return remote;
        return local;
    }
}
