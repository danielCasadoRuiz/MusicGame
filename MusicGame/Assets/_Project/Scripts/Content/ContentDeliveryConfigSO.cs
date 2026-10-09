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
    [Min(0)] public int downloadRetries = 2;
    [Min(0f)] public float retryDelaySeconds = 1.5f;
}
