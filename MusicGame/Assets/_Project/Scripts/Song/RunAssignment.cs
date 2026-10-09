using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "This is the song I'm trying to DEFEND, and this is the rival who challenges it."
/// The ONE pipeline that assigns a run, used by every entry point (CONTINUE, SELECT SONG confirm,
/// post-victory next song):
///   song → exact final rival + version decided NOW (GameSession shuffle bag, current tier)
///        → PlayerProgress.pendingRun saved immediately → GameSession.Assigned* → analysis / Runner.
/// The Opponent Selection roulette later only REVEALS GameSession.AssignedOpponent (never re-rolls).
/// A pending assignment survives quitting (the song restarts from the beginning — no mid-song
/// state is stored), a lost fight and Fight Again; only the victory clears it
/// (GameSession.RegisterCompletedSong), or a CONFIRMED manual choice replaces it.
/// </summary>
public static class RunAssignment
{
    private static OpponentRosterSO Roster
    {
        get { var app = Resources.Load<AppConfigSO>("AppConfig"); return app != null ? app.opponentRoster : null; }
    }

    /// <summary>Assigns `songId` (a playable catalog song): picks + persists its rival. Replaces any
    /// previous pending assignment — callers only do that deliberately (a new route song when none is
    /// pending, or a confirmed manual SELECT SONG).</summary>
    public static PendingRunData CreatePendingRun(string songId)
    {
        var progress = PlayerProgressService.Instance;
        var session  = GameSession.Instance;
        if (string.IsNullOrEmpty(songId) || session == null) return null;

        var (opponent, level, tier) = PickOpponent(session);
        var run = new PendingRunData
        {
            songId        = songId,
            opponentId    = opponent != null ? opponent.id : "",
            opponentLevel = level != null ? level.level : 0,
            opponentTier  = tier,
        };
        if (progress != null && progress.PendingRun.IsSet && progress.PendingRun.songId != songId)
            Debug.Log($"[RunAssignment] Unfinished assignment '{progress.PendingRun.songId}' abandoned (NOT completed) for '{songId}'.");
        progress?.SetPendingRun(run); // saved BEFORE gameplay starts
        session.SetAssignedOpponent(opponent, level, tier);
        // The rival is known before the Runner: fetch its content group now (active quality only), so
        // the fight never waits on a download. No-op when nothing is labelled for it / already cached.
        if (opponent != null) ContentDownloadManager.Instance?.PreloadContentAsync(ContentKeys.Enemy(opponent.id));
        return run;
    }

    /// <summary>A local-file run: a rival is still decided up front for the roulette, but nothing is
    /// persisted (no stable song id) and the catalog's pendingRun is left untouched.</summary>
    public static void AssignSessionOnly()
    {
        var session = GameSession.Instance;
        if (session == null) return;
        var (opponent, level, tier) = PickOpponent(session);
        session.SetAssignedOpponent(opponent, level, tier);
    }

    /// <summary>CONTINUE / next song: a VALID pending assignment wins (same song + same rival, the
    /// route is not consulted); otherwise the first uncompleted route song becomes a new assignment.</summary>
    public static NextSongStatus ResolveContinue(IReadOnlyList<string> catalogIds, out string songId)
    {
        if (TryRestorePending(catalogIds, out songId)) return NextSongStatus.Found;
        var status = SongProgression.ResolveNext(catalogIds, out songId);
        if (status == NextSongStatus.Found) CreatePendingRun(songId);
        return status;
    }

    /// <summary>Validates the saved assignment against the current content; if valid it is applied to
    /// GameSession (exact rival + version) and true is returned. Invalid (song gone from the playable
    /// catalog or already completed, rival or version gone) → cleared, false — never mismatched content.</summary>
    public static bool TryRestorePending(IReadOnlyList<string> catalogIds, out string songId)
    {
        songId = null;
        var progress = PlayerProgressService.Instance;
        var session  = GameSession.Instance;
        if (progress == null || session == null || !progress.PendingRun.IsSet) return false;

        var run = progress.PendingRun;
        string problem = null;
        if (catalogIds == null || !Contains(catalogIds, run.songId)) problem = "song not in the playable catalog";
        else if (progress.IsSongCompleted(run.songId)) problem = "song already completed";
        else if (!TryResolveOpponent(run, out var opponent, out var level)) problem = $"rival '{run.opponentId}' v{run.opponentLevel} not available";
        else
        {
            session.SetAssignedOpponent(opponent, level, run.opponentTier);
            ContentDownloadManager.Instance?.PreloadContentAsync(ContentKeys.Enemy(opponent.id));
            songId = run.songId;
            Debug.Log($"[RunAssignment] Resuming pending run: '{run.songId}' vs {opponent.displayName} v{run.opponentLevel} (same song, same rival).");
            return true;
        }

        Debug.LogWarning($"[RunAssignment] Pending run '{run.songId}' discarded ({problem}) — a fresh assignment will be made.");
        progress.ClearPendingRun();
        return false;
    }

    /// <summary>Exact rival + version from stable ids (OpponentDefinition.id + OpponentLevelConfig.level).</summary>
    public static bool TryResolveOpponent(PendingRunData run, out OpponentDefinition opponent, out OpponentLevelConfig level)
    {
        opponent = null; level = null;
        var roster = Roster;
        if (run == null || string.IsNullOrEmpty(run.opponentId) || roster == null || roster.opponents == null) return false;
        foreach (var o in roster.opponents) if (o != null && o.id == run.opponentId) { opponent = o; break; }
        if (opponent == null) return false;
        if (opponent.levels != null)
            foreach (var l in opponent.levels) if (l != null && l.level == run.opponentLevel) { level = l; return true; }
        // Level 0 = the safety-net defaultConfig of a rival without any levels[] (see GetConfigForTier).
        if (run.opponentLevel == 0 && (opponent.levels == null || opponent.levels.Length == 0)) { level = opponent.defaultConfig; return level != null; }
        return false;
    }

    private static (OpponentDefinition, OpponentLevelConfig, int) PickOpponent(GameSession session)
    {
        int tier = session.EffectiveOpponentTier;
        var opponent = session.PickNextOpponent(Roster);
        var level = opponent != null ? opponent.GetConfigForTier(tier) : null;
        return (opponent, level, tier);
    }

    private static bool Contains(IReadOnlyList<string> ids, string id)
    {
        foreach (var x in ids) if (x == id) return true;
        return false;
    }
}
