using System;
using UnityEngine;

/// <summary>Independent of musical style. Automatic follows the device; manual choice persists.</summary>
public static class AppearancePreference
{
    public const string PrefKey = "MusicGame.Appearance";
    public const string Auto = "auto", Light = "light", Dark = "dark";
    public const float FadeSeconds = .65f;
    public static event Action Updated;
    public static event Action ModeChanged;
    private static bool initialized;
    private static float weight, target;
    public static string Mode => Normalize(PlayerPrefs.GetString(PrefKey, Auto));
    public static float LightWeight { get { Initialize(); return weight; } }
    public static string Normalize(string mode) => mode == Light || mode == Dark ? mode : Auto;
    public static bool ResolveLight(string mode, bool deviceLight) => Normalize(mode) == Light || (Normalize(mode) == Auto && deviceLight);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { initialized = false; Updated = null; ModeChanged = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Start()
    {
        Initialize();
        var driver = new GameObject("Appearance preference", typeof(AppearancePreferenceDriver));
        UnityEngine.Object.DontDestroyOnLoad(driver);
    }

    private static void Initialize()
    {
        if (initialized) return;
        initialized = true;
        weight = target = ResolveLight(Mode, DeviceIsLight()) ? 1f : 0f;
    }

    public static void SetMode(string mode)
    {
        Initialize();
        PlayerPrefs.SetString(PrefKey, Normalize(mode));
        PlayerPrefs.Save();
        RefreshDevice();
        ModeChanged?.Invoke();
    }

    public static void RefreshDevice()
    {
        Initialize();
        target = ResolveLight(Mode, DeviceIsLight()) ? 1f : 0f;
    }

    public static void Tick(float unscaledDelta)
    {
        Initialize();
        float next = Mathf.MoveTowards(weight, target, Mathf.Max(0, unscaledDelta) / FadeSeconds);
        if (Mathf.Approximately(next, weight)) return;
        weight = next;
        Updated?.Invoke();
    }

    private static bool DeviceIsLight()
    {
        try
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var resources = activity.Call<AndroidJavaObject>("getResources");
            using var config = resources.Call<AndroidJavaObject>("getConfiguration");
            return (config.Get<int>("uiMode") & 0x30) != 0x20;
#elif UNITY_IOS && !UNITY_EDITOR
            return MusicGameSystemIsLight() != 0;
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            uint size=4;
            return RegGetValue(new IntPtr(unchecked((int)0x80000001)), @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0x10, out _, out uint value, ref size)==0 && value!=0;
#else
            return false; // Unsupported desktop platforms keep the original night appearance.
#endif
        }
        catch { return false; }
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    [System.Runtime.InteropServices.DllImport("advapi32.dll", EntryPoint="RegGetValueW", CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int RegGetValue(IntPtr key,string subkey,string value,uint flags,out uint type,out uint data,ref uint size);
#endif
#if UNITY_IOS && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern int MusicGameSystemIsLight();
#endif
}
