using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owner of the PERSISTENT player progression (PlayerProgressData) — level, XP, extra lives,
/// opponent-tier progress and the rival collection. Separate from GameSession (current-run state, see
/// RunSession) and from the per-match combat state (FighterCombatResources): those reset, this never
/// does except through a future explicit "Reset Progress".
///
/// Every meaningful change is saved IMMEDIATELY (PlayerProgressStore, atomic) — LIFE collected or
/// consumed, XP / level, a newly defeated rival version, a completed song — so closing the game right
/// after a reward never loses it. Changes are announced with PlayerProgressChangedEvent; a first-time
/// rival version defeat also publishes RivalVersionUnlockedEvent.
///
/// Lives on [App Bootstrap] (DontDestroyOnLoad), created by AppBootstrap BEFORE GameSession, which
/// borrows `TierProgress` so the existing tier rules keep working unchanged — now persisted.
/// </summary>
public class PlayerProgressService : MonoBehaviour
{
    public static PlayerProgressService Instance { get; private set; }

    private PlayerProgressData _data;
    private ProgressionConfigSO _config;

    /// <summary>True when a save file existed at launch or one has been written since — the main
    /// menu shows Continue instead of Play.</summary>
    public bool HasSave { get; private set; }

    public int Xp => _data.xp;
    /// <summary>XP-derived level — a secondary statistic now; the player's identity is MasteryRank.</summary>
    public int Level => _data.playerLevel;
    /// <summary>Musical Mastery: 0-based rank from total fights won (ProgressionConfigSO.masteryRanks).</summary>
    public int MasteryRank => _config != null ? _config.MasteryRankForWins(_data.fightsWon) : 0;
    /// <summary>Localized Musical Mastery rank name (e.g. "VIRTUOSO").</summary>
    public string MasteryRankName => Loc.Get("Mastery." + (_config != null ? _config.MasteryRankId(MasteryRank) : "ToneDeaf"));
    public int ExtraLives => _data.extraLives;
    public int FightsWon => _data.fightsWon;
    public GameProgressionState TierProgress => _data.tierProgress;

    /// <summary>XP gathered inside the current level, and the XP that level needs in total.</summary>
    public (int inLevel, int needed) LevelProgress
    {
        get
        {
            if (_config == null) return (0, 1);
            int start = _config.TotalXpForLevel(Level), next = _config.TotalXpForLevel(Level + 1);
            return (_data.xp - start, Mathf.Max(1, next - start));
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _data = PlayerProgressStore.Load();
        HasSave = _data != null;
        _data ??= new PlayerProgressData();
        Debug.Log(HasSave
            ? $"[PlayerProgress] Loaded save: level {_data.playerLevel}, {_data.xp} XP, {_data.extraLives} extra lives, " +
              $"{_data.defeatedOpponents.Count} rivals with defeated versions, {_data.tierProgress.completedSongs} completed songs ({PlayerProgressStore.FilePath})"
            : "[PlayerProgress] No save yet — fresh progression (first launch).");
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Configure(ProgressionConfigSO config)
    {
        _config = config;
        // Older saves: every completed song IS a won fight (GameSession.RegisterCompletedSong runs only
        // on a win), so the victory count can be repaired safely. Rival versions cannot be — none invented.
        if (_data.fightsWon < _data.tierProgress.completedSongs) _data.fightsWon = _data.tierProgress.completedSongs;
        if (_config != null) _data.playerLevel = _config.LevelForXp(_data.xp); // follow the current thresholds
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    /// <summary>Writes the save now (also used by the main menu's Play to start a profile).</summary>
    public void Save()
    {
        PlayerProgressStore.Save(_data);
        HasSave = true;
    }

    private void Changed(string what)
    {
        Save();
        EventBus.Publish(new PlayerProgressChangedEvent { Reason = what });
    }

    // ── Lives (persistent inventory) ─────────────────────────────────────────

    public void AddLife(int amount = 1, string source = "pickup")
    {
        if (amount <= 0) return;
        _data.extraLives += amount;
        Debug.Log($"[PlayerProgress] +{amount} LIFE ({source}) → {_data.extraLives} extra lives (saved)");
        Changed("life+");
    }

    /// <summary>Consumes one extra life (Fight Again); false when none are left.</summary>
    public bool TryConsumeLife()
    {
        if (_data.extraLives <= 0) return false;
        _data.extraLives--;
        Debug.Log($"[PlayerProgress] LIFE consumed → {_data.extraLives} extra lives (saved)");
        Changed("life-");
        return true;
    }

    // ── XP / level ───────────────────────────────────────────────────────────

    /// <summary>Adds XP and recomputes the level. Returns the level change (0 = none).</summary>
    public int AddXp(int amount, string source)
    {
        if (amount <= 0) return 0;
        int old = _data.playerLevel;
        _data.xp += amount;
        _data.playerLevel = _config != null ? _config.LevelForXp(_data.xp) : _data.playerLevel;
        Debug.Log($"[PlayerProgress] +{amount} XP ({source}) → {_data.xp} XP, level {_data.playerLevel}" +
                  (_data.playerLevel != old ? $" (LEVEL UP from {old})" : ""));
        Changed("xp");
        return _data.playerLevel - old;
    }

    /// <summary>The tier progression (GameProgression) changed — persist it.</summary>
    public void NotifyTierProgressChanged() => Changed("tier");

    // ── Rival collection (non-linear: each version independent) ─────────────

    public struct VictoryResult { public bool NewVersion; public int OldRank, NewRank, Xp; }

    /// <summary>A fight WON against the EXACT rival version that was fought (the caller passes the
    /// cached OpponentDefinition + OpponentLevelConfig.level — never something re-derived from the
    /// progression state). Safe order: 1 record the exact version, 2 +1 fight won, 3 recompute the
    /// Mastery rank, 4 XP, 5 ONE save, 6 announce (unlock event, then the change event).</summary>
    public VictoryResult RecordVictory(OpponentDefinition opponent, int level)
    {
        var result = new VictoryResult { OldRank = MasteryRank };

        // 1. collection — exactly this composer + this version; never lower/higher ones
        if (opponent != null && !string.IsNullOrEmpty(opponent.id) && level > 0)
        {
            var record = _data.defeatedOpponents.Find(r => r.opponentId == opponent.id);
            if (record == null) { record = new DefeatedOpponentRecord { opponentId = opponent.id }; _data.defeatedOpponents.Add(record); }
            result.NewVersion = !record.levels.Contains(level);
            if (result.NewVersion) { record.levels.Add(level); record.levels.Sort(); }
        }
        else Debug.LogWarning($"[PlayerProgress] Victory without a valid rival version (opponent {(opponent != null ? opponent.id : "null")}, level {level}) — nothing added to the collection.");

        // 2–3. victories → Musical Mastery
        _data.fightsWon++;
        result.NewRank = MasteryRank;

        // 4. XP (statistic)
        if (_config != null)
        {
            result.Xp = _config.xpPerFightWin + (result.NewVersion ? _config.xpFirstRivalVersion : 0);
            _data.xp += result.Xp;
            _data.playerLevel = _config.LevelForXp(_data.xp);
        }

        // 5. persist once
        Save();
        Debug.Log($"[PlayerProgress] Victory vs {(opponent != null ? opponent.displayName : "?")} level {level}: " +
                  $"{(result.NewVersion ? "NEW version unlocked, " : "")}{_data.fightsWon} wins, mastery {result.OldRank}→{result.NewRank}, +{result.Xp} XP (saved)");

        // 6. announce
        if (result.NewVersion) EventBus.Publish(new RivalVersionUnlockedEvent { Opponent = opponent, Level = level });
        EventBus.Publish(new PlayerProgressChangedEvent { Reason = "victory" });
        return result;
    }

    public bool IsDefeated(string opponentId, int level)
    {
        var record = _data.defeatedOpponents.Find(r => r.opponentId == opponentId);
        return record != null && record.levels.Contains(level);
    }

    /// <summary>Defeated levels of one rival (empty when none) — includes stored levels the current
    /// content may no longer define; callers intersect with OpponentDefinition.levels.</summary>
    public IReadOnlyList<int> DefeatedLevels(string opponentId)
    {
        var record = _data.defeatedOpponents.Find(r => r.opponentId == opponentId);
        return record != null ? record.levels : (IReadOnlyList<int>)System.Array.Empty<int>();
    }

    // ── Song history (ids only — vectors/analysis live in SongSimilarityCache) ───────────────

    public IReadOnlyList<string> CompletedSongIds => _data.completedSongIds;
    public bool IsSongCompleted(string songId) => !string.IsNullOrEmpty(songId) && _data.completedSongIds.Contains(songId);
    public string SeedSongId => _data.seedSongId;
    public SongRouteData SongRoute => _data.songRoute;

    /// <summary>Adds a playable song to the completed history. `save` false lets the caller batch it
    /// with another change into one save (GameSession.RegisterCompletedSong).</summary>
    public bool MarkSongCompleted(string songId, bool save = true)
    {
        if (string.IsNullOrEmpty(songId) || _data.completedSongIds.Contains(songId)) return false;
        _data.completedSongIds.Add(songId);
        Debug.Log($"[PlayerProgress] Song completed: '{songId}' ({_data.completedSongIds.Count} total)");
        if (save) Changed("song");
        return true;
    }

    /// <summary>New preference seed (a manual catalog choice) — the cached route is invalidated.</summary>
    public void SetSongSeed(string songId)
    {
        _data.seedSongId = songId ?? "";
        _data.songRoute = new SongRouteData();
        Changed("seed");
    }

    public void SetSongRoute(List<string> songIds, string signature)
    {
        _data.songRoute = new SongRouteData { songIds = songIds ?? new List<string>(), signature = signature ?? "" };
        Changed("route");
    }
}

/// <summary>Any persistent progression value changed (already saved). Reason: life+, life-, xp, tier, win, rival-unlock.</summary>
public struct PlayerProgressChangedEvent { public string Reason; }

/// <summary>A rival VERSION was defeated for the first time (already saved).</summary>
public struct RivalVersionUnlockedEvent { public OpponentDefinition Opponent; public int Level; }
