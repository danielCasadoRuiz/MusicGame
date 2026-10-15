using UnityEngine;

/// <summary>
/// Content delivery settings (AppConfigSO.contentDelivery). Optional: without an asset the defaults
/// below apply (no remote device profiles, 2 download retries).
/// </summary>
[CreateAssetMenu(fileName = "ContentDeliveryConfig", menuName = "MusicGame/Content/Content Delivery Config")]
public class ContentDeliveryConfigSO : ScriptableObject
{
    [Header("Device profiles (DeviceQualityResolver)")]
    [Tooltip("Optional HTTPS URL of a remote DeviceProfiles.json (same format as Resources/Content/DeviceProfiles.json). " +
             "A valid copy with an equal or higher version overrides the bundled rules and is cached for offline use.")]
    public string remoteDeviceProfilesUrl = "";
    [Min(1f)] public float remoteProfilesTimeoutSeconds = 4f;

    [Header("Addressables (ContentDownloadManager)")]
    [Tooltip("Check the remote catalog for content updates at startup (only does anything when a remote catalog is built).")]
    public bool checkCatalogUpdatesOnStart = true;
    [Tooltip("Re-check the remote catalog when returning to the Main Menu if at least this many minutes passed " +
             "since the last check (never during a fight / dialogue). 0 = only at startup.")]
    [Min(0f)] public float catalogRecheckMinutes = 30f;
    [Min(0)] public int downloadRetries = 2;
    [Min(0f)] public float retryDelaySeconds = 1.5f;
}
