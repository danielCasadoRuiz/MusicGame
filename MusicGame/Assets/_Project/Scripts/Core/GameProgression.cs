using UnityEngine;

/// <summary>
/// Persistent progression state — ONLY the raw fact (completed songs); the tier is always derived
/// from it through ProgressionConfigSO, never stored separately. [Serializable] so a future save
/// game can persist it as-is.
/// </summary>
[System.Serializable]
public class GameProgressionState
{
    public int completedSongs;
}

/// <summary>Read-only progression view for UI/debug — computed, never recalculated by consumers.</summary>
public readonly struct ProgressionSnapshot
{
    public readonly int CompletedSongs;
    public readonly int CurrentTier;                 // 1-based
    public readonly int CompletedSongsInCurrentTier; // e.g. 3 (of 5)
    public readonly int SongsRequiredForNextTier;    // e.g. 5
    public int SongsUntilNextTier => Mathf.Max(0, SongsRequiredForNextTier - CompletedSongsInCurrentTier);
    public float Progress01 => SongsRequiredForNextTier > 0 ? Mathf.Clamp01((float)CompletedSongsInCurrentTier / SongsRequiredForNextTier) : 0f;

    public ProgressionSnapshot(int completedSongs, int currentTier, int inTier, int required)
    {
        CompletedSongs              = completedSongs;
        CurrentTier                 = currentTier;
        CompletedSongsInCurrentTier = inTier;
        SongsRequiredForNextTier    = required;
    }
}

/// <summary>Progression logic over GameProgressionState + ProgressionConfigSO (pure C#).</summary>
public class GameProgression
{
    private readonly GameProgressionState _state;
    private ProgressionConfigSO _config;

    public GameProgression(GameProgressionState state, ProgressionConfigSO config)
    {
        _state  = state ?? new GameProgressionState();
        _config = config;
    }

    public GameProgressionState State => _state;
    public void SetConfig(ProgressionConfigSO config) => _config = config;

    private int SongsPerTier => _config != null ? Mathf.Max(1, _config.songsPerTier) : 5;

    public int CompletedSongs => _state.completedSongs;
    public int CurrentTier => _config != null ? _config.TierForCompletedSongs(_state.completedSongs) : _state.completedSongs / SongsPerTier + 1;

    public ProgressionSnapshot Snapshot
    {
        get
        {
            int required = _config != null ? _config.SongsRequiredForTier(CurrentTier) : SongsPerTier;
            return new ProgressionSnapshot(_state.completedSongs, CurrentTier, _state.completedSongs % SongsPerTier, required);
        }
    }

    /// <summary>One properly completed game cycle. Returns the (possibly new) current tier.</summary>
    public int RegisterCompletedSong()
    {
        _state.completedSongs++;
        return CurrentTier;
    }
}
