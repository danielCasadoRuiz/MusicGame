using UnityEngine;

/// <summary>
/// The Song system's own editable config — composed under AppConfigSO like FlowConfigSO/
/// ThemeSystemConfigSO. Local/always-available: the predefined song library is core, always-
/// needed content (see PredefinedSongLibrarySO's own doc on why it isn't Addressable content).
/// </summary>
[CreateAssetMenu(fileName = "SongSystemConfig", menuName = "MusicGame/App/Song System Config")]
public class SongSystemConfigSO : ScriptableObject
{
    public PredefinedSongLibrarySO predefinedSongs;
}
