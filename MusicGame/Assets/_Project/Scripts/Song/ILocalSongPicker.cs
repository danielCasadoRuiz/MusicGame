/// <summary>
/// Abstraction over "let the user pick a local audio file" — LocalFileSongSource depends only on
/// this, never on a specific file-dialog API. There is no cross-platform, in-build file picker in
/// stock Unity, so a production implementation needs a native file-dialog plugin per target
/// platform; see LocalSongPickerFactory for what's actually wired up today (an Editor-only dev
/// convenience, and a clearly-labeled "unsupported" stand-in for real builds).
/// </summary>
public interface ILocalSongPicker
{
    /// <summary>Returns an absolute file path, or null if the user cancelled.</summary>
    string PickFile();
}
