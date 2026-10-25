using UnityEngine;

/// <summary>
/// The FIGHT difficulty of the current match (0..1) — what sizes the rival (FightCombatBalanceConfig.RivalTuningAt):
///   campaign match → the position's continuous difficulty (CampaignConfigSO.RunnerDifficultyAt: level slice + the
///                    match inside the level + lap step), so the rival grows level after level AND after every
///                    opponent beaten inside a level;
///   final-rival stage → 1 (the stage adds its own bonus);
///   no campaign position (free song, debug fight) → the rival version's place in the campaign range.
/// Never depends on the player's build or run: the player's power comes from the runner, the rival's from here.
/// </summary>
public static class FightDifficulty
{
    public static float Resolve(OpponentLevelConfig levelConfig, out int bossStage)
    {
        var session = GameSession.Instance;
        bossStage = session != null ? session.CurrentBossStage : -1;
        if (bossStage >= 0) return 1f;

        var config = CampaignService.Config;
        var run = PlayerProgressService.Instance != null ? PlayerProgressService.Instance.PendingRun : null;
        if (config != null && run != null && run.IsSet && !run.IsBoss && !string.IsNullOrEmpty(run.levelId))
        {
            float d = config.RunnerDifficultyAt(run.levelId, run.matchNumber, run.lap);
            if (d >= 0f) return d;
        }

        // Fallback: where this rival version sits in the campaign's range (version 1 → start, 4 → end).
        float start = config != null ? config.campaignDifficultyStart : 0.15f;
        float end = config != null ? config.campaignDifficultyEnd : 0.85f;
        int level = levelConfig != null ? Mathf.Max(1, levelConfig.level) : 1;
        return Mathf.Lerp(start, end, Mathf.Clamp01((level - 1) / 3f));
    }
}
