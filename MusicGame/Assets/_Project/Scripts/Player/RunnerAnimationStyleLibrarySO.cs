using UnityEngine;

/// <summary>
/// Music style → Runner animation style lookup (kept separate from body identity and from
/// MusicStyleAvatarProfileSO, which only dresses the avatar). Unmapped styles, Unknown, or a missing
/// asset resolve to defaultStyle. Referenced from PlayerAvatarConfigSO.runnerAnimationStyles.
/// </summary>
[CreateAssetMenu(fileName = "RunnerAnimationStyles", menuName = "MusicGame/Player/Runner Animation Style Library")]
public class RunnerAnimationStyleLibrarySO : ScriptableObject
{
    [System.Serializable]
    public class Mapping
    {
        public MusicStyleId[] musicStyles = System.Array.Empty<MusicStyleId>();
        public RunnerAnimationStyleSO style;
    }

    public RunnerAnimationStyleSO defaultStyle;
    public Mapping[] mappings = System.Array.Empty<Mapping>();

    public RunnerAnimationStyleSO Resolve(MusicStyleId musicStyle)
    {
        foreach (var m in mappings)
            if (m != null && m.style != null && System.Array.IndexOf(m.musicStyles, musicStyle) >= 0)
                return m.style;
        return defaultStyle;
    }

    /// <summary>Default first, then every mapped style once — the debug cycle order.</summary>
    public System.Collections.Generic.List<RunnerAnimationStyleSO> AllStyles()
    {
        var list = new System.Collections.Generic.List<RunnerAnimationStyleSO>();
        if (defaultStyle != null) list.Add(defaultStyle);
        foreach (var m in mappings)
            if (m?.style != null && !list.Contains(m.style)) list.Add(m.style);
        return list;
    }
}
