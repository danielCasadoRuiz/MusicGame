using UnityEngine;

/// <summary>Editor dialog in Unity; native document pickers on Windows, Android and iOS builds.</summary>
public static class LocalSongPickerFactory
{
    public static ILocalSongPicker Create()
    {
#if UNITY_EDITOR
        return new EditorLocalSongPicker();
#else
        return new NativeLocalSongPicker();
#endif
    }
}

#if UNITY_EDITOR
/// <summary>
/// DEV CONVENIENCE ONLY — uses UnityEditor.EditorUtility.OpenFilePanel, which only exists in the
/// Editor. The #if UNITY_EDITOR guard around this whole class means it never exists in a real
/// build at all (not even compiled), so this is never an editor-only dependency a shipped build
/// could accidentally pull in.
/// </summary>
internal class EditorLocalSongPicker : ILocalSongPicker
{
    public string PickFile()
    {
        string path = UnityEditor.EditorUtility.OpenFilePanel("Select Audio (WAV/MP3/OGG/AIFF)", "", "wav,mp3,ogg,aif,aiff");
        return string.IsNullOrEmpty(path) ? null : path;
    }
}
#endif
