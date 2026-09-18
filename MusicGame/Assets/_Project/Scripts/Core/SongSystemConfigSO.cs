using UnityEngine;

/// <summary>
/// The Song system's own editable config — composed under AppConfigSO like FlowConfigSO/
/// ThemeSystemConfigSO. The catalog asset itself is local/always-available (see SongCatalogSO's own
/// doc) — only each song's actual audio content is Addressable.
/// </summary>
[CreateAssetMenu(fileName = "SongSystemConfig", menuName = "MusicGame/App/Song System Config")]
public class SongSystemConfigSO : ScriptableObject
{
    public SongCatalogSO catalog;
}
