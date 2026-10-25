using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owner of the PERSISTENT player progression (PlayerProgressData) — the campaign (THE progression:
/// levels / matches / laps / records, see CampaignService), extra lives, opponent-tier progress and the
/// rival collection. There is no XP or player level any more (retired: it duplicated the campaign; the
/// old save fields stay as ignored legacy data). Separate from GameSession (current-run state, see
/// RunSession) and from the per-match combat state (FighterCombatResources): those reset, this never
/// does except through a future explicit "Reset Progress".
///
/// Every meaningful change is saved IMMEDIATELY (PlayerProgressStore, atomic) — LIFE collected or
/// consumed, a newly defeated rival version, a completed song — so closing the game right
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

    /// <summary>True when a save file existed at launch or one has been written since — the main
    /// menu shows Continue instead of Play.</summary>
    public bool HasSave { get; private set; }

    /// <summary>Extra lives: the Fight Again inventory (LIFE pickups add, Fight Again consumes).</summary>
    public int ExtraLives => _data.extraLives;
    /// <summary>Total fights won — a plain statistic (no rank / level is derived from it).</summary>
    public int FightsWon => _data.fightsWon;
    public GameProgressionState TierProgress => _data.tierProgress;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _data = PlayerProgressStore.Load();
        HasSave = _data != null;
        _data ??= new PlayerProgressData();
        Debug.Log(HasSave
            ? $"[PlayerProgress] Loaded save: {_data.extraLives} extra lives, " +
              $"{_data.defeatedOpponents.Count} rivals with defeated versions, {_data.tierProgress.completedSongs} completed songs ({PlayerProgressStore.FilePath})"
            : "[PlayerProgress] No save yet — fresh progression (first launch).");
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Called once at startup (the progression config only drives the opponent tier, read by
    /// GameSession) — repairs old data and sets up / re-anchors the campaign.</summary>
    public void Configure(ProgressionConfigSO config)
    {
        // Older saves: every completed song IS a won fight (GameSession.RegisterCompletedSong runs only
        // on a win), so the victory count can be repaired safely. Rival versions cannot be — none invented.
        if (_data.fightsWon < _data.tierProgress.completedSongs) _data.fightsWon = _data.tierProgress.completedSongs;
        InitializeCampaign();
    }

    // ── Campaign (v5) ────────────────────────────────────────────────────────

    public CampaignProgressData Campaign => _data.campaign;
    public SongCycleData SongCycle => _data.songCycle;

    /// <summary>First run of v5 code on this profile: places the pre-campaign history deterministically
    /// (CampaignModel.MigrateLegacy — one won match per position, in order, completed without stars);
    /// afterwards only re-anchors the position on the current config.</summary>
    private void InitializeCampaign()
    {
        var app = Resources.Load<AppConfigSO>("AppConfig");
        var campaign = app != null ? app.campaign : null;
        if (campaign == null || campaign.LevelCount == 0) { Debug.LogWarning("[PlayerProgress] No CampaignConfigSO (AppConfig.campaign) — campaign disabled."); return; }
        if (!_data.campaign.initialized)
        {
            int legacyWins = HasSave ? Mathf.Max(_data.tierProgress.completedSongs, _data.completedSongIds.Count) : 0;
            CampaignModel.MigrateLegacy(_data.campaign, campaign, legacyWins);
            _data.campaign.initialized = true;
            if (HasSave)
            {
                Debug.Log($"[PlayerProgress] Campaign set up from the old save: {legacyWins} won match(es) → {_data.campaign.migratedLegacyPositions} " +
                          $"position(s) completed without stars; resuming at lap {_data.campaign.lap}, " +
                          $"{(CampaignModel.IsLapFinished(_data.campaign, campaign) ? "lap finished" : CampaignModel.CurrentPositionId(_data.campaign, campaign))}.");
                // An old pending assignment keeps its song + rival and simply belongs to the current position.
                if (_data.pendingRun.IsSet && string.IsNullOrEmpty(_data.pendingRun.levelId) && !CampaignModel.IsLapFinished(_data.campaign, campaign))
                {
                    _data.pendingRun.lap = _data.campaign.lap;
                    _data.pendingRun.levelId = _data.campaign.levelId;
                    _data.pendingRun.matchNumber = _data.campaign.matchNumber;
                }
                Save();
            }
        }
        else CampaignModel.Normalize(_data.campaign, campaign);

        // v6: song use is per lap. An old cycle is rebuilt once from this lap's own records (never the history).
        string rebuilt = SongCycleModel.BindToCurrentLap(_data.songCycle, _data.campaign, campaign);
        if (rebuilt != null)
        {
            Debug.Log("[PlayerProgress] Song use bound to the current lap — " + rebuilt);
            if (HasSave) Save();
        }
    }

    /// <summary>Owned cards among `required` (non-linear collection: each version on its own).</summary>
    public int CountOwned(IEnumerable<(string id, int level)> required)
    {
        int n = 0;
        foreach (var (id, level) in required) if (IsDefeated(id, level)) n++;
        return n;
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

    /// <summary>A LIFE pickup of the current run (`collectedThisRun` = LIFE pickups this run has collected,
    /// this one included). For a run of the pending campaign assignment (`songId` matches it) the life is
    /// granted only beyond what earlier attempts of the SAME match already granted (pendingRun.livesGranted,
    /// saved in the same write) — replaying, restarting or resuming a match never farms lives. Runs outside
    /// the assignment (local file) are capped by the caller (GameSession). True when +1 life was granted.</summary>
    public bool GrantRunLife(string songId, int collectedThisRun, string source)
    {
        var run = _data.pendingRun;
        bool ofAssignment = run.IsSet && !run.IsBoss && !string.IsNullOrEmpty(songId) && run.songId == songId;
        if (ofAssignment)
        {
            if (!RunRewardRules.GrantsLife(collectedThisRun, run.livesGranted))
            {
                Debug.Log($"[PlayerProgress] LIFE pickup #{collectedThisRun} of '{songId}' not granted — an earlier attempt of this match already earned {run.livesGranted}.");
                return false;
            }
            run.livesGranted = collectedThisRun; // persisted by AddLife's save below (one write)
        }
        AddLife(1, source);
        return true;
    }

    /// <summary>True when `songId` is the song of the pending campaign assignment (lives capped by it).</summary>
    public bool IsPendingSong(string songId) =>
        _data.pendingRun.IsSet && !_data.pendingRun.IsBoss && !string.IsNullOrEmpty(songId) && _data.pendingRun.songId == songId;

    /// <summary>The tier progression (GameProgression) changed — persist it.</summary>
    public void NotifyTierProgressChanged() => Changed("tier");

    // ── Rival collection (non-linear: each version independent) ─────────────

    public struct VictoryResult { public bool NewVersion; }

    /// <summary>A fight WON against the EXACT rival version that was fought (the caller passes the
    /// cached OpponentDefinition + OpponentLevelConfig.level — never something re-derived from the
    /// progression state). Safe order: 1 record the exact version, 2 +1 fight won (statistic),
    /// 3 ONE save, 4 announce (unlock event, then the change event).</summary>
    public VictoryResult RecordVictory(OpponentDefinition opponent, int level)
    {
        var result = new VictoryResult();

        // 1. collection — exactly this composer + this version; never lower/higher ones
        if (opponent != null && !string.IsNullOrEmpty(opponent.id) && level > 0)
        {
            var record = _data.defeatedOpponents.Find(r => r.opponentId == opponent.id);
            if (record == null) { record = new DefeatedOpponentRecord { opponentId = opponent.id }; _data.defeatedOpponents.Add(record); }
            result.NewVersion = !record.levels.Contains(level);
            if (result.NewVersion) { record.levels.Add(level); record.levels.Sort(); }
        }
        else Debug.LogWarning($"[PlayerProgress] Victory without a valid rival version (opponent {(opponent != null ? opponent.id : "null")}, level {level}) — nothing added to the collection.");

        // 2. victories (statistic)
        _data.fightsWon++;

        // 3. persist once
        Save();
        Debug.Log($"[PlayerProgress] Victory vs {(opponent != null ? opponent.displayName : "?")} level {level}: " +
                  $"{(result.NewVersion ? "NEW version unlocked, " : "")}{_data.fightsWon} wins (saved)");

        // 4. announce
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

    public IReadOnlyList<string> PlayedSongIds => _data.playedSongIds;
    /// <summary>Played at least once, in any lap (Song Selection's check) — never decides availability.</summary>
    public bool IsSongPlayed(string songId) => !string.IsNullOrEmpty(songId) && _data.playedSongIds.Contains(songId);

    /// <summary>Adds a playable song to the played history (the Runner finished). `save` false lets the caller batch it.</summary>
    public bool MarkSongPlayed(string songId, bool save = true)
    {
        if (string.IsNullOrEmpty(songId) || _data.playedSongIds.Contains(songId)) return false;
        _data.playedSongIds.Add(songId);
        if (save) Changed("song played");
        return true;
    }

    // ── Player profile (v8) ─────────────────────────────────────────────────────────────────

    public PlayerProfileData Profile => _data.profile;

    /// <summary>Averages one run's CONVERTED build (what the fight receives) into the profile — once per run.</summary>
    public void RecordRunBuild(float punch, float kick, float resistance, float agility)
    {
        var p = _data.profile;
        int n = p.runs + 1;
        p.avgPunchPower += (punch - p.avgPunchPower) / n;
        p.avgKickPower += (kick - p.avgKickPower) / n;
        p.avgResistance += (resistance - p.avgResistance) / n;
        p.avgAgility += (agility - p.avgAgility) / n;
        p.runs = n;
        p.lastPunchPower = punch; p.lastKickPower = kick; p.lastResistance = resistance; p.lastAgility = agility;
        p.bestTotal = Mathf.Max(p.bestTotal, punch + kick + resistance + agility);
        p.lastUtcTicks = System.DateTime.UtcNow.Ticks;
        Changed("profile build");
    }

    /// <summary>One MANUAL song choice of `style` (Song Selection; automatic choices are never counted).</summary>
    public void RecordStylePick(GameMusicStyle style)
    {
        if (style == GameMusicStyle.Unknown) return;
        var picks = _data.profile.stylePicks;
        var entry = picks.Find(x => x.style == (int)style);
        if (entry == null) picks.Add(entry = new StylePickData { style = (int)style });
        entry.count++;
        Changed("profile style");
    }

    /// <summary>The style the player picks most (Unknown until they chose one).</summary>
    public GameMusicStyle PreferredStyle
    {
        get
        {
            StylePickData best = null;
            foreach (var x in _data.profile.stylePicks) if (best == null || x.count > best.count) best = x;
            return best != null ? (GameMusicStyle)best.style : GameMusicStyle.Unknown;
        }
    }

    /// <summary>New preference seed (a manual catalog choice) — the cached route is invalidated.</summary>
    public void SetSongSeed(string songId)
    {
        _data.seedSongId = songId ?? "";
        _data.songRoute = new SongRouteData();
        Changed("seed");
    }

    /// <summary>The persisted song + rival assignment (never null; IsSet false when none).</summary>
    public PendingRunData PendingRun => _data.pendingRun;

    /// <summary>Replaces the pending assignment and saves at once (before analysis/Runner starts).</summary>
    public void SetPendingRun(PendingRunData run)
    {
        _data.pendingRun = run ?? new PendingRunData();
        Debug.Log($"[PlayerProgress] Pending run: song '{_data.pendingRun.songId}' vs '{_data.pendingRun.opponentId}' " +
                  $"v{_data.pendingRun.opponentLevel} (tier {_data.pendingRun.opponentTier}) (saved)");
        Changed("pending");
    }

    /// <summary>Clears the pending assignment. `save` false = part of a batched change (victory).</summary>
    public void ClearPendingRun(bool save = true)
    {
        if (!_data.pendingRun.IsSet) return;
        _data.pendingRun = new PendingRunData();
        if (save) Changed("pending-clear");
    }

    public void SetSongRoute(List<string> songIds, string signature)
    {
        _data.songRoute = new SongRouteData { songIds = songIds ?? new List<string>(), signature = signature ?? "" };
        Changed("route");
    }
}

/// <summary>Any persistent progression value changed (already saved). Reason: life+, life-, tier, victory, song, pending….</summary>
public struct PlayerProgressChangedEvent { public string Reason; }

/// <summary>A rival VERSION was defeated for the first time (already saved).</summary>
public struct RivalVersionUnlockedEvent { public OpponentDefinition Opponent; public int Level; }
