using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>Native document pickers copy into the app cache first; ImportedAudioLibrary keeps the durable copy.</summary>
public class NativeLocalSongPicker : IAsyncLocalSongPicker
{
    public string PickFile() => null;
    public IEnumerator Pick(Action<string> onPicked)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using var bridge = new AndroidJavaClass("com.tempofury.music.AudioPicker");
        using var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        using var activity = unity.GetStatic<AndroidJavaObject>("currentActivity");
        bridge.CallStatic("open", activity);
        string result;
        while ((result = bridge.CallStatic<string>("result")) == "__PENDING__") yield return null;
        onPicked?.Invoke(string.IsNullOrEmpty(result) ? null : result);
#elif UNITY_IOS && !UNITY_EDITOR
        TFMusicPick();
        string result;
        while ((result = Marshal.PtrToStringAnsi(TFMusicPickResult())) == "__PENDING__") yield return null;
        onPicked?.Invoke(string.IsNullOrEmpty(result) ? null : result);
#elif UNITY_STANDALONE_WIN && !UNITY_EDITOR
        var dialog = new OpenFileName();
        dialog.size = Marshal.SizeOf(dialog);
        dialog.filter = "Audio (WAV, MP3, OGG, AIFF)\0*.wav;*.mp3;*.ogg;*.aif;*.aiff\0\0";
        dialog.file = new System.Text.StringBuilder(32768); dialog.maxFile = dialog.file.Capacity;
        dialog.title = "Import audio"; dialog.flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008;
        onPicked?.Invoke(GetOpenFileName(dialog) ? dialog.file.ToString() : null);
        yield break;
#else
        Debug.LogWarning("[CustomAudio] No native picker available on this platform.");
        onPicked?.Invoke(null);
        yield break;
#endif
    }
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void TFMusicPick();
    [DllImport("__Internal")] private static extern IntPtr TFMusicPickResult();
#endif
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private class OpenFileName
    {
        public int size; public IntPtr owner, instance;
        public string filter, customFilter; public int maxCustomFilter, filterIndex;
        public System.Text.StringBuilder file; public int maxFile;
        public string fileTitle; public int maxFileTitle;
        public string initialDir, title; public int flags; public short fileOffset, fileExtension;
        public string defaultExtension; public IntPtr customData, hook; public string template;
        public IntPtr reserved; public int reserved2, flagsEx;
    }
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileName([In, Out] OpenFileName dialog);
#endif
}
