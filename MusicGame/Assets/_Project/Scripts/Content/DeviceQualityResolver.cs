using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Hardware facts used for classification (from SystemInfo; a struct so rules are testable).</summary>
public struct DeviceInfo
{
    public string platform;          // "Android" / "iOS" / "Windows" / "macOS" / "Linux"
    public string deviceModel;
    public string graphicsDeviceName;
    public int graphicsMemoryMB;
    public int systemMemoryMB;
    public int processorCount;
    public string processorType;
    public string operatingSystem;

    public static DeviceInfo Current() => new()
    {
        platform = PlatformKey(Application.platform),
        deviceModel = SystemInfo.deviceModel,
        graphicsDeviceName = SystemInfo.graphicsDeviceName,
        graphicsMemoryMB = SystemInfo.graphicsMemorySize,
        systemMemoryMB = SystemInfo.systemMemorySize,
        processorCount = SystemInfo.processorCount,
        processorType = SystemInfo.processorType,
        operatingSystem = SystemInfo.operatingSystem,
    };

    public static string PlatformKey(RuntimePlatform p) => p switch
    {
        RuntimePlatform.Android => "Android",
        RuntimePlatform.IPhonePlayer => "iOS",
        RuntimePlatform.WindowsPlayer or RuntimePlatform.WindowsEditor => "Windows",
        RuntimePlatform.OSXPlayer or RuntimePlatform.OSXEditor => "macOS",
        RuntimePlatform.LinuxPlayer or RuntimePlatform.LinuxEditor => "Linux",
        _ => p.ToString(),
    };

    public override string ToString() =>
        $"{platform} '{deviceModel}', GPU '{graphicsDeviceName}' {graphicsMemoryMB} MB, RAM {systemMemoryMB} MB, {processorCount} cores";
}

/// <summary>
/// Picks the Unity quality level for this device at startup and applies it with
/// QualitySettings.SetQualityLevel — the user never chooses graphics quality.
///   AppBootstrap (BeforeSceneLoad) → ApplyAtStartup(): best AVAILABLE rules (cached remote ≥ local),
///   synchronously, before any quality-dependent asset can load.
///   Then RefreshRemoteRules() fetches the remote JSON (timeout); a newer valid version is cached and,
///   if no quality-dependent asset was resolved yet this session, applied immediately — otherwise it
///   takes effect on the next launch (the active quality stays stable during a session).
/// Classification is pure (Resolve) so a future performance calibration can simply feed it new rules.
/// </summary>
public static class DeviceQualityResolver
{
    private const string PrefsKey = "MusicGame.Quality.Resolved"; // "<quality>|<rules source>|v<version>" (diagnostics)

    public static string AppliedQuality { get; private set; }
    public static string AppliedReason { get; private set; }

    /// <summary>Pure classification: model → GPU family → hardware rules → platform default → middle level.
    /// Quality names that don't exist in `levelNames` are skipped (safe fallback). Returns the level index.</summary>
    public static int Resolve(DeviceProfileDatabase db, DeviceInfo device, IReadOnlyList<string> levelNames, out string reason) =>
        Resolve(db, device, levelNames, out reason, out _);

    /// <summary>Same, also returning the matched rule's optional overrides (null for platform defaults / fallback).</summary>
    public static int Resolve(DeviceProfileDatabase db, DeviceInfo device, IReadOnlyList<string> levelNames, out string reason,
                              out DeviceProfileDatabase.GraphicsOverrides overrides)
    {
        reason = FallbackReason;
        overrides = null;
        if (levelNames == null || levelNames.Count == 0) return 0;
        if (db != null)
        {
            foreach (var d in db.devices)
                if (d != null && PlatformMatches(d.platform, device.platform) && Matches(device.deviceModel, d.model, exact: true) && Valid(d.quality, levelNames, out int i))
                { reason = $"device model '{d.model}'"; overrides = d; return i; }
            foreach (var g in db.gpuFamilies)
                if (g != null && PlatformMatches(g.platform, device.platform) && Matches(device.graphicsDeviceName, g.pattern, exact: false) && Valid(g.quality, levelNames, out int i))
                { reason = $"GPU family '{g.pattern}'"; overrides = g; return i; }
            foreach (var h in db.hardwareRules)
                if (h != null && PlatformMatches(h.platform, device.platform) &&
                    device.systemMemoryMB >= h.minSystemMemoryMB && device.graphicsMemoryMB >= h.minGraphicsMemoryMB &&
                    device.processorCount >= h.minProcessorCount && Valid(h.quality, levelNames, out int i))
                { overrides = h; reason = $"hardware rule ({h.platform ?? "any"} ≥{h.minSystemMemoryMB} MB RAM, ≥{h.minGraphicsMemoryMB} MB VRAM, ≥{h.minProcessorCount} cores)"; return i; }
            if (device.platform != null && db.platformDefaults.TryGetValue(device.platform, out var p) && Valid(p, levelNames, out int pi))
            { reason = $"platform default ({device.platform})"; return pi; }
            if (db.platformDefaults.TryGetValue("Default", out var any) && Valid(any, levelNames, out int ai))
            { reason = "default"; return ai; }
        }
        // Built-in last resort: a conservative "Mid" (by name), else the middle level.
        int mid = QualityAssetResolver.IndexOf(levelNames, "Mid");
        return mid >= 0 ? mid : levelNames.Count / 2;
    }

    public const string FallbackReason = "built-in fallback (no valid rule)";

    /// <summary>Startup: classify with the best rules already on the device and apply them.</summary>
    public static void ApplyAtStartup()
    {
        var db = DeviceProfileDatabase.LoadBest();
        ApplyRules(db, "startup");
    }

    private static void ApplyRules(DeviceProfileDatabase db, string when)
    {
        var names = QualitySettings.names;
        var device = DeviceInfo.Current();
        int level = Resolve(db, device, names, out string reason, out var overrides);
        // Rules that match nothing valid (e.g. a remote file with wrong quality names) never win over
        // the bundled rules: retry with the local file before the built-in fallback.
        if (reason == FallbackReason && db != null && db.Source != "local")
        {
            var local = DeviceProfileDatabase.LoadLocal();
            if (local != null) { level = Resolve(local, device, names, out reason, out overrides); db = local; }
        }
        if (QualitySettings.GetQualityLevel() != level) QualitySettings.SetQualityLevel(level, true);
        // FPS + optional device overrides on top of the level (Unity QualitySettings stays the source of the level).
        DeviceGraphicsApplier.Apply(db, names[level], overrides, device);
        AppliedQuality = names.Length > level ? names[level] : level.ToString();
        AppliedReason = reason;
        PlayerPrefs.SetString(PrefsKey, $"{AppliedQuality}|{db?.Source ?? "none"}|v{db?.version ?? 0}");
        Debug.Log($"[DeviceQualityResolver] Quality '{AppliedQuality}' ({when}; {reason}; rules {db?.Source ?? "none"} v{db?.version ?? 0}) — {device}");
    }

    /// <summary>Fetches the remote rules (timeout, never blocks the game). A valid newer version is
    /// cached; it is applied now only if no quality-dependent asset has been resolved yet.</summary>
    public static IEnumerator RefreshRemoteRules(string url, float timeoutSeconds)
    {
        if (string.IsNullOrWhiteSpace(url)) yield break;
        using var request = UnityWebRequest.Get(url);
        request.timeout = Mathf.Max(1, Mathf.CeilToInt(timeoutSeconds));
        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.Log($"[DeviceQualityResolver] Remote device profiles unavailable ({request.error}) — keeping local rules.");
            yield break;
        }

        string json = request.downloadHandler.text;
        var remote = DeviceProfileDatabase.Parse(json, "remote");
        if (remote == null) yield break; // invalid → ignored, local/cached rules stay
        var current = DeviceProfileDatabase.LoadBest();
        if (current != null && remote.version < current.version) yield break;

        DeviceProfileDatabase.SaveRemoteCache(json);
        if (QualityAssetResolver.QualityInUse)
            Debug.Log($"[DeviceQualityResolver] Remote rules v{remote.version} cached — applied on next launch (quality already in use this session).");
        else
            ApplyRules(remote, "remote update");
    }

    private static bool Valid(string quality, IReadOnlyList<string> names, out int index)
    {
        index = QualityAssetResolver.IndexOf(names, quality);
        return index >= 0;
    }

    private static bool PlatformMatches(string rulePlatform, string platform) =>
        string.IsNullOrEmpty(rulePlatform) || string.Equals(rulePlatform, platform, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Case-insensitive; '*' wildcards; models match exactly (or by wildcard), GPU patterns as substrings.</summary>
    public static bool Matches(string value, string pattern, bool exact)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(pattern)) return false;
        if (pattern.Contains("*"))
        {
            var rx = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(value, rx, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        return exact ? string.Equals(value, pattern, System.StringComparison.OrdinalIgnoreCase)
                     : value.IndexOf(pattern, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
