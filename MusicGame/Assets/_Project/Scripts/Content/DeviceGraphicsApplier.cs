using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Applies the runtime graphics parameters that sit ON TOP of the chosen Unity quality level:
///   • FPS — mobile (Android/iOS): Application.targetFrameRate = rule override ?? the level's
///     "qualityDefaults" targetFPS ?? 60, capped to the display refresh rate (VSync is ignored on
///     mobile; vSyncCount is set to 0 so the two never conflict).
///     Desktop (Windows/macOS/Linux): the quality level's VSync decides (targetFrameRate = -1); only an
///     explicit rule targetFPS turns VSync off and caps the frame rate instead.
///   • renderScale / shadowDistance — optional overrides written onto the ACTIVE URP asset. Player
///     builds only: in the Editor that would modify the URP asset file, so it is skipped (logged).
/// The quality level itself is never changed here — Unity QualitySettings stays the single source.
/// </summary>
public static class DeviceGraphicsApplier
{
    public struct Decision
    {
        public int targetFrameRate;   // -1 = platform default (VSync-driven)
        public int vSyncCount;        // -1 = keep the quality level's value
        public float? renderScale;
        public float? shadowDistance;
        public string summary;
    }

    public static bool IsMobile(string platform) => platform == "Android" || platform == "iOS";

    /// <summary>Pure decision (testable).</summary>
    public static Decision Decide(DeviceProfileDatabase db, string qualityName, DeviceProfileDatabase.GraphicsOverrides overrides,
                                  string platform, double refreshRateHz)
    {
        DeviceProfileDatabase.GraphicsOverrides defaults = null;
        if (db?.qualityDefaults != null && qualityName != null)
            foreach (var kv in db.qualityDefaults)
                if (string.Equals(kv.Key, qualityName, System.StringComparison.OrdinalIgnoreCase)) { defaults = kv.Value; break; }

        var d = new Decision
        {
            vSyncCount = -1,
            targetFrameRate = -1,
            renderScale = overrides?.renderScale ?? defaults?.renderScale,
            shadowDistance = overrides?.shadowDistance ?? defaults?.shadowDistance,
        };

        if (IsMobile(platform))
        {
            int fps = overrides?.targetFPS ?? defaults?.targetFPS ?? 60;
            int refresh = refreshRateHz > 1 ? Mathf.RoundToInt((float)refreshRateHz) : 0;
            if (refresh > 0) fps = Mathf.Min(fps, refresh);
            d.targetFrameRate = Mathf.Max(15, fps);
            d.vSyncCount = 0;
            d.summary = $"mobile target {d.targetFrameRate} FPS{(refresh > 0 ? $" (display {refresh} Hz)" : "")}";
        }
        else if (overrides?.targetFPS is int cap && cap > 0)
        {
            d.targetFrameRate = cap;
            d.vSyncCount = 0;
            d.summary = $"desktop capped at {cap} FPS (VSync off by device rule)";
        }
        else d.summary = "desktop: quality level VSync";

        return d;
    }

    public static void Apply(DeviceProfileDatabase db, string qualityName, DeviceProfileDatabase.GraphicsOverrides overrides, DeviceInfo device)
    {
        double refresh = Screen.currentResolution.refreshRateRatio.value;
        var d = Decide(db, qualityName, overrides, device.platform, refresh);
        if (d.vSyncCount >= 0) QualitySettings.vSyncCount = d.vSyncCount;
        Application.targetFrameRate = d.targetFrameRate;

        string extra = "";
        if (d.renderScale.HasValue || d.shadowDistance.HasValue)
        {
#if UNITY_EDITOR
            extra = " — renderScale/shadowDistance overrides skipped in the Editor (would modify the URP asset)";
#else
            if (QualitySettings.renderPipeline is UniversalRenderPipelineAsset urp)
            {
                if (d.renderScale.HasValue) urp.renderScale = Mathf.Clamp(d.renderScale.Value, 0.1f, 2f);
                if (d.shadowDistance.HasValue) urp.shadowDistance = Mathf.Max(0f, d.shadowDistance.Value);
                extra = $" — URP overrides: renderScale {urp.renderScale:0.##}, shadowDistance {urp.shadowDistance:0.#}";
            }
#endif
        }
        Debug.Log($"[DeviceGraphicsApplier] {qualityName}: {d.summary}{extra}");
    }
}
