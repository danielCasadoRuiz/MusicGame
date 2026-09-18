using UnityEngine;

/// <summary>
/// Chooses which ILocalSongPicker implementation to use — the ONE place that decides this, so
/// LocalFileSongSource/SongSelectionService never care which concrete picker they got.
///
/// Today: an Editor-only dev convenience (EditorLocalSongPicker, entirely compiled out of real
/// builds by the #if UNITY_EDITOR guard below) and a clearly-labeled "unsupported" stand-in
/// everywhere else. There is no cross-platform, in-build file picker in stock Unity — a real
/// implementation for a shipped build needs a native file-dialog plugin (per platform) providing
/// its own ILocalSongPicker; wire it in here once one exists, and nothing else in the Song
/// Selection code needs to change.
/// </summary>
public static class LocalSongPickerFactory
{
    public static ILocalSongPicker Create()
    {
#if UNITY_EDITOR
        return new EditorLocalSongPicker();
#else
        return new UnsupportedLocalSongPicker();
#endif
    }
}

/// <summary>
/// Stand-in for any build with no real file picker wired up yet — always reports "the user
/// cancelled" (with a clear warning) instead of silently pretending to work. Replace
/// LocalSongPickerFactory.Create()'s non-Editor branch with a real native-dialog implementation
/// once a target platform actually needs "Play Your Song" outside the Editor.
/// </summary>
public class UnsupportedLocalSongPicker : ILocalSongPicker
{
    public string PickFile()
    {
        Debug.LogWarning("[UnsupportedLocalSongPicker] No local file picker is implemented for " +
                          "this build yet (needs a native file-dialog plugin — see " +
                          "ILocalSongPicker's own doc). Treating this as 'the user cancelled'.");
        return null;
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
        string path = UnityEditor.EditorUtility.OpenFilePanel("Select a Song (WAV/MP3)", "", "wav,mp3");
        return string.IsNullOrEmpty(path) ? null : path;
    }
}
#endif
