using UnityEngine;

/// <summary>
/// The single decision-maker for the whole match — currentRound/playerRoundsWon/
/// opponentRoundsWon/roundTimer/roundActive/round result/match result all live HERE, not scattered
/// across FightHud/FighterHealth/screen controllers. Every screen controller (RoundEndController,
/// MatchResultController) only ever NOTIFIES this class "my animation finished" and reacts to the
/// events this class publishes — none of them decide who won a round or the match (see this
/// phase's own explicit architecture note on why that branch was moved OUT of RoundIntroController's
/// self-driven-sequence style).
///
/// FLOW OWNERSHIP:
///   - VersusIntro -> RoundIntro (round 1) and RoundIntro -> Countdown -> Fighting (every round) stay
///     self-driven by VersusScreenController/RoundIntroController — pure on-rails sequences with no
///     game-logic branch, exactly like before.
///   - Fighting -> RoundEnd is decided HERE (KO or timeout) and is the one thing FighterHealth/the
///     timer ever triggers indirectly (via FighterKOEvent / this class's own Update).
///   - RoundEnd -> (RoundIntro | MatchWon | MatchLost) is ALSO decided here — the one real branch
///     point in the whole flow — via NotifyRoundEndDisplayComplete(), called by RoundEndController
///     only after its own KO/TIME-UP + result display has finished (it hands the decision back
///     instead of making it).
///
/// TIMER: the single source of truth (RoundTimeRemaining) — FightHud only ever reads it (see
/// FightController's own doc: presents data, never decides).
///
/// RESET: ResetFightersForRound is the ONLY place that touches FighterActor.ResetForRound — every
/// individual component's own reset lives on that component itself (FighterHealth/FighterMoveController/
/// etc.), never manipulated here as private fields (see FighterActor.ResetForRound's own doc).
///
/// BEST OF 3 — HARD RULE: at most FightFlowConfig.maxRounds (3) rounds are EVER played, no matter
/// what. There is no repeat-a-round mechanism anywhere in this class (deleted along with
/// RoundResolution.TrueDraw/DrawResolvedByPoints — see NotifyRoundEndDisplayComplete's own doc).
///
/// ROUND-LEVEL DRAW POLICY (TimeOut only — a KO always has a real winner): compares CurrentHealth/
/// MaxHealth PERCENTAGE (never absolute values — the two sides could have different MaxHealth
/// someday). Different percentages -> Decisive, straightforward winner. EXACTLY equal percentages ->
/// RoundResolution.Draw, which awards NOBODY a round win — see EndRound's own doc. A round-level Draw
/// is NEVER "resolved by points"; that would silently convert a legitimate draw into a fake round win,
/// which this format explicitly forbids. MatchPointDifferential still accumulates that round's (often
/// ~0) differential regardless — see MatchPointDifferential's own doc — but that accumulator is only
/// ever CONSULTED once, after every round has been played (see NotifyRoundEndDisplayComplete).
///
/// MATCH-LEVEL RESOLUTION (see NotifyRoundEndDisplayComplete's own doc for the full decision tree):
/// the match ends the instant a side reaches roundsToWin round wins (can happen before round 3), OR
/// once round `maxRounds` has been played, whichever comes first — and at that second point, if
/// nobody has strictly more round wins, MatchPointDifferential (summed across ALL rounds played,
/// Draws included) breaks the tie, with a final deterministic fallback (ResolveTiedMatch) for the
/// vanishingly rare case that's ALSO tied. Never a 4th round, ever.
/// </summary>
public class FightMatchController : MonoBehaviour
{
    public static FightMatchController Instance { get; private set; }

    public int CurrentRound { get; private set; }
    public int PlayerRoundsWon { get; private set; }
    public int OpponentRoundsWon { get; private set; }
    public bool RoundActive { get; private set; }
    public float RoundTimeRemaining { get; private set; }

    /// <summary>Running total of playerHealthPercent - opponentHealthPercent across EVERY round
    /// completed so far this match — Decisive AND Draw rounds alike (see EndRound's own doc; a Draw's
    /// own differential is typically ~0 but is still summed in, per this format's own explicit rule).
    /// Reset to 0 at the start of a new match (BeginMatch). Positive favors the Player, negative the
    /// Opponent. ONLY ever consulted once, by ResolveTiedMatch, after every round in maxRounds has
    /// been played AND round wins are tied — never mid-match, never to resolve an individual round.</summary>
    public float MatchPointDifferential { get; private set; }

    /// <summary>The most recently completed round's OWN playerHealthPercent - opponentHealthPercent
    /// — debug/UI convenience, not itself used for any decision (MatchPointDifferential already
    /// folds it in).</summary>
    public float LastRoundDifferential { get; private set; }

    public RoundResolution LastRoundResolution { get; private set; }

    /// <summary>How the most recently completed round ended — KO or TimeOut. Cached alongside
    /// LastRoundDifferential/LastRoundResolution purely for MatchEndedEvent's own small match-summary
    /// payload (see its own doc) — never used for any decision.</summary>
    public RoundEndReason LastRoundReason { get; private set; }
    public float LastRoundPlayerHealthPercent { get; private set; }
    public float LastRoundOpponentHealthPercent { get; private set; }

    /// <summary>How the MATCH was decided, once it actually is — null while a match is still in
    /// progress (or before any match has ever ended this session). See MatchResolution's own doc and
    /// NotifyRoundEndDisplayComplete for exactly when this gets set — debug/UI convenience
    /// (FightDebugHUD), also carried on MatchEndedEvent for MatchResultController's own summary.</summary>
    public MatchResolution? LastMatchResolution { get; private set; }

    /// <summary>FightFlowConfig.maxRounds, resolved with the same fallback every other reader of that
    /// config field uses — debug/UI convenience so FightDebugHUD can show "Round 2/3" without its own
    /// separate config lookup.</summary>
    public int MaxRounds => _config != null ? Mathf.Max(1, _config.maxRounds) : 3;

    private FightFlowConfig _config;
    private FightArenaConfig _arenaConfig;

    private FighterActor _player;
    private FighterActor _opponent;

    private bool _roundEndProcessed;

    private System.Action<FightFlowStateChangedEvent> _onFlowChanged;
    private System.Action<FighterKOEvent> _onKO;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        _config      = appConfig != null ? appConfig.fightFlow : null;
        _arenaConfig = appConfig != null ? appConfig.arena     : null;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        _onFlowChanged = e =>
        {
            if (e.Current == FightFlowState.VersusIntro) BeginMatch();
            else if (e.Current == FightFlowState.Fighting) BeginRound();
        };
        _onKO = e =>
        {
            if (RoundActive) EndRound(RoundEndReason.KO, WinnerFromKO(e.Fighter));
        };
        EventBus.Subscribe(_onFlowChanged);
        EventBus.Subscribe(_onKO);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFlowChanged);
        EventBus.Unsubscribe(_onKO);
    }

    private void Update()
    {
        if (!RoundActive) return;

        RoundTimeRemaining = Mathf.Max(0f, RoundTimeRemaining - Time.deltaTime);
        if (RoundTimeRemaining <= 0f)
            EndRound(RoundEndReason.TimeOut, null); // null = "no explicit winner yet" — resolved inside EndRound, tie-break included
    }

    // ── Match / round lifecycle ───────────────────────────────────────────────

    /// <summary>
    /// Resets EVERY piece of round/match state to a neutral start — deliberately exhaustive (not just
    /// the fields the old bug happened to touch) so a fresh match can NEVER inherit stale state from a
    /// previous one. Runs for both a genuinely fresh Fight entry AND for MatchResultController's own
    /// Fight Again (which requests FightFlowState.VersusIntro directly, same as a first entry, without
    /// re-running Opponent Selection — see BeginFightAgain's own doc) — this is precisely the path a
    /// "1-1 ends the match" symptom could otherwise be explained by carry-over rather than a logic
    /// bug, so every field NotifyRoundEndDisplayComplete/EndRound ever reads or writes is reset here.
    /// </summary>
    private void BeginMatch()
    {
        PlayerRoundsWon = 0;
        OpponentRoundsWon = 0;
        MatchPointDifferential = 0f;
        LastRoundDifferential = 0f;
        LastRoundResolution = default;
        LastRoundReason = default;
        LastRoundPlayerHealthPercent = 0f;
        LastRoundOpponentHealthPercent = 0f;
        LastMatchResolution = null;
        CurrentRound = 1;
        _roundEndProcessed = false;
        // Fight.unity (and every FighterActor in it) is freshly (re)loaded for a new Fight entry —
        // old references, if any, are already-destroyed Unity Objects by now; re-find them.
        _player = null;
        _opponent = null;

        // Set here — well before FightFlowState.RoundIntro is even requested (by
        // VersusScreenController, after its own hold) — so FightController's HUD (visible from
        // RoundIntro onward, see its own doc) shows the correct starting time throughout "ROUND
        // n"/3-2-1/FIGHT! instead of a stale/zero value. RoundActive stays false the whole time, so
        // Update() below never decrements it early; BeginRound() re-sets the exact same value, right
        // as the timer actually starts counting down for real.
        RoundTimeRemaining = _config != null ? Mathf.Max(1f, _config.roundDuration) : 60f;

        RoundIntroController.Instance?.SetRound(CurrentRound);
        ResetFightersForRound();
    }

    private void BeginRound()
    {
        FindActors();
        RoundActive = true;
        _roundEndProcessed = false;
        RoundTimeRemaining = _config != null ? Mathf.Max(1f, _config.roundDuration) : 60f;
        EventBus.Publish(new RoundStartedEvent { RoundNumber = CurrentRound });
    }

    /// <summary>
    /// Only ever called once per round — guarded by _roundEndProcessed (task's own explicit "només
    /// ho processa UNA vegada" requirement). `explicitWinner` is the immediate, decisive winner for
    /// a KO; pass null for TimeOut to let this method resolve it.
    ///
    /// A TimeOut with exactly equal health percentages is ALWAYS a Draw — winner stays null, nobody
    /// gets a round win, full stop. There is no round-level points tie-break (see class doc on why:
    /// that would silently convert a legitimate draw into a fake round win). MatchPointDifferential
    /// still accumulates this round's own differential either way (see its own doc) for
    /// ResolveTiedMatch to consult later, once every round has actually been played.
    /// </summary>
    private void EndRound(RoundEndReason reason, FighterSide? explicitWinner)
    {
        if (_roundEndProcessed || !RoundActive) return;
        _roundEndProcessed = true;
        RoundActive = false;

        float playerPct   = HealthPercent(_player);
        float opponentPct = HealthPercent(_opponent);
        float roundDifferential = playerPct - opponentPct;

        FighterSide? winner;
        RoundResolution resolution;

        if (explicitWinner.HasValue)
        {
            winner = explicitWinner;
            resolution = RoundResolution.Decisive;
        }
        else if (!Mathf.Approximately(playerPct, opponentPct))
        {
            winner = playerPct > opponentPct ? FighterSide.Player : FighterSide.Opponent;
            resolution = RoundResolution.Decisive;
        }
        else
        {
            winner = null;
            resolution = RoundResolution.Draw;
        }

        if (winner == FighterSide.Player) PlayerRoundsWon++;
        else if (winner == FighterSide.Opponent) OpponentRoundsWon++;
        // Draw (winner == null) awards nobody a round — see class doc.

        LastRoundDifferential = roundDifferential;
        LastRoundReason = reason;
        LastRoundPlayerHealthPercent = playerPct;
        LastRoundOpponentHealthPercent = opponentPct;
        // EVERY round contributes, Draws included — see MatchPointDifferential's own doc.
        MatchPointDifferential += roundDifferential;
        LastRoundResolution = resolution;

        Debug.Log($"[FightMatchController] Round {CurrentRound} ended — reason:{reason} resolution:{resolution} " +
                  $"winner:{(winner.HasValue ? winner.Value.ToString() : "Draw")} differential:{roundDifferential:+0.00;-0.00} " +
                  $"accumulated:{MatchPointDifferential:+0.00;-0.00} (Player {PlayerRoundsWon} - {OpponentRoundsWon} Opponent)");

        EventBus.Publish(new RoundEndedEvent
        {
            RoundNumber = CurrentRound,
            Reason = reason,
            Resolution = resolution,
            Winner = winner,
            PlayerHealthPercent = playerPct,
            OpponentHealthPercent = opponentPct,
            RoundDifferential = roundDifferential,
            AccumulatedPointDifferential = MatchPointDifferential,
        });

        FightFlowController.Instance?.RequestState(FightFlowState.RoundEnd);
    }

    /// <summary>
    /// Called by RoundEndController once its own KO/TIME-UP + result display has finished — the ONE
    /// real branch point in the whole flow (see class doc). Never called from anywhere else.
    ///
    /// DECISION TREE (see class doc's own "MATCH-LEVEL RESOLUTION" summary):
    ///   1. If either side has already reached roundsToWin, the match is over right now — this can
    ///      fire before maxRounds is reached (e.g. 2-0 after Round 2).
    ///   2. Otherwise, if maxRounds has just been played and NOBODY reached roundsToWin, the match is
    ///      STILL over right now (best-of-3 has a hard 3-round ceiling) — whoever has strictly more
    ///      round wins takes the match outright (e.g. 1-0 after 3 rounds, the third a Draw — see
    ///      class doc); if round wins are tied too, ResolveTiedMatch breaks the tie. Either way this
    ///      is the ONLY place a match can end without reaching roundsToWin, and it is also the LAST
    ///      possible round — there is no path from here back into another round, ever.
    ///   3. Otherwise (nobody has won yet, rounds remain) — and ONLY otherwise — play the next round.
    ///      A Draw round takes this same branch exactly like a Decisive one; a Draw never short-
    ///      circuits into a tie-break of its own (see EndRound's own doc).
    /// </summary>
    public void NotifyRoundEndDisplayComplete()
    {
        int roundsToWin = _config != null ? Mathf.Max(1, _config.roundsToWin) : 2;
        bool reachedWinThreshold = PlayerRoundsWon >= roundsToWin || OpponentRoundsWon >= roundsToWin;
        bool allRoundsPlayed     = CurrentRound >= MaxRounds;

        if (!reachedWinThreshold && !allRoundsPlayed)
        {
            StartNextRound();
            return;
        }

        FighterSide winner;
        MatchResolution resolution;

        if (PlayerRoundsWon > OpponentRoundsWon)
        {
            winner = FighterSide.Player;
            resolution = MatchResolution.DecisiveRounds;
        }
        else if (OpponentRoundsWon > PlayerRoundsWon)
        {
            winner = FighterSide.Opponent;
            resolution = MatchResolution.DecisiveRounds;
        }
        else
        {
            // Round wins tied with every round played (the only way to reach this branch — reaching
            // roundsToWin with equal counts is impossible) — see ResolveTiedMatch's own doc.
            (winner, resolution) = ResolveTiedMatch();
        }

        LastMatchResolution = resolution;

        if (winner == FighterSide.Player)
        {
            // Level Up is decided HERE, deterministically, the instant the match is officially won —
            // never deferred to Continue (task's own explicit "no esperis a Continue per decidir si
            // ha pujat" requirement) — so MatchEndedEvent already carries the old/new level for
            // MatchResultController to display.
            int oldLevel = GameSession.Instance != null ? GameSession.Instance.PlayerLevel : 1;
            int newLevel = GameSession.Instance != null ? GameSession.Instance.LevelUp() : oldLevel;

            EventBus.Publish(BuildMatchEndedEvent(FighterSide.Player, resolution, oldLevel, newLevel));
            FightFlowController.Instance?.RequestState(FightFlowState.MatchWon);
        }
        else
        {
            // A loss never touches PlayerLevel — Old/New are the same, unchanged value (task's own
            // explicit "si perds, no baixa, no puja" requirement).
            int level = GameSession.Instance != null ? GameSession.Instance.PlayerLevel : 1;

            EventBus.Publish(BuildMatchEndedEvent(FighterSide.Opponent, resolution, level, level));
            FightFlowController.Instance?.RequestState(FightFlowState.MatchLost);
        }
    }

    /// <summary>
    /// Called ONLY when round wins are tied after every round in maxRounds has been played. Breaks
    /// the tie using MatchPointDifferential (summed across ALL rounds played, Draws included) against
    /// a configurable epsilon — never a hardcoded magic number (see FightFlowConfig.matchPointsTiebreakEpsilon).
    ///
    /// EXACT PERFECT TIE fallback (round wins tied AND points tied within epsilon — vanishingly rare,
    /// needs three rounds' worth of health percentages to sum to an exact wash): falls back to the
    /// FINAL round's own health percentages (LastRoundPlayerHealthPercent/OpponentHealthPercent —
    /// already-existing data, same "overall performance" philosophy the task asked for, no new stats
    /// system invented). If even THAT is exactly tied, an explicit, deterministic, documented default
    /// decides it (Player) — this is intentionally arbitrary at that point (every measurable signal is
    /// genuinely identical) but NEVER unresolved and NEVER a 4th round.
    /// </summary>
    private (FighterSide winner, MatchResolution resolution) ResolveTiedMatch()
    {
        float epsilon = _config != null ? Mathf.Max(0.0001f, _config.matchPointsTiebreakEpsilon) : 0.01f;

        if (MatchPointDifferential > epsilon)
            return (FighterSide.Player, MatchResolution.PointsTiebreak);
        if (MatchPointDifferential < -epsilon)
            return (FighterSide.Opponent, MatchResolution.PointsTiebreak);

        if (LastRoundPlayerHealthPercent > LastRoundOpponentHealthPercent)
            return (FighterSide.Player, MatchResolution.ExactTieFallback);
        if (LastRoundOpponentHealthPercent > LastRoundPlayerHealthPercent)
            return (FighterSide.Opponent, MatchResolution.ExactTieFallback);

        Debug.LogWarning("[FightMatchController] Match tied on rounds, MatchPointDifferential AND " +
                          "final-round health% — every available metric is genuinely identical. " +
                          "Falling back to the documented default (Player) — see ResolveTiedMatch's own doc.");
        return (FighterSide.Player, MatchResolution.ExactTieFallback);
    }

    private MatchEndedEvent BuildMatchEndedEvent(FighterSide winner, MatchResolution resolution, int oldLevel, int newLevel) => new MatchEndedEvent
    {
        Winner = winner,
        PlayerRoundsWon = PlayerRoundsWon,
        OpponentRoundsWon = OpponentRoundsWon,
        Resolution = resolution,
        LastRoundReason = LastRoundReason,
        PlayerHealthPercent = LastRoundPlayerHealthPercent,
        OpponentHealthPercent = LastRoundOpponentHealthPercent,
        MatchPointDifferential = MatchPointDifferential,
        OldPlayerLevel = oldLevel,
        NewPlayerLevel = newLevel,
    };

    private void StartNextRound()
    {
        CurrentRound++;
        RoundTimeRemaining = _config != null ? Mathf.Max(1f, _config.roundDuration) : 60f; // see BeginMatch's own doc
        RoundIntroController.Instance?.SetRound(CurrentRound);
        ResetFightersForRound();
        FightFlowController.Instance?.RequestState(FightFlowState.RoundIntro);
    }

    // ── Reset (see FighterActor.ResetForRound's own doc — every component resets itself) ────────

    private void ResetFightersForRound()
    {
        FindActors();

        Vector3 playerSpawn   = _arenaConfig != null ? _arenaConfig.playerSpawnPosition   : new Vector3(-1.5f, 1f, 0f);
        Vector3 opponentSpawn = _arenaConfig != null ? _arenaConfig.opponentSpawnPosition : new Vector3(1.5f, 1f, 0f);

        // Each fighter now owns its own FighterInputController/FighterAI (see FighterAI's own doc)
        // — FighterActor.ResetForRound already resets both, no separate global lookup needed here.
        _player?.ResetForRound(playerSpawn);
        _opponent?.ResetForRound(opponentSpawn);
    }

    private void FindActors()
    {
        if (_player != null && _opponent != null) return;
        foreach (var actor in FindObjectsByType<FighterActor>(FindObjectsSortMode.None))
        {
            if (actor.Side == FighterSide.Player) _player = actor;
            else _opponent = actor;
        }
    }

    // ── Winner determination ──────────────────────────────────────────────────

    private static FighterSide? WinnerFromKO(FighterActor koFighter) =>
        koFighter == null ? null : (koFighter.Side == FighterSide.Player ? FighterSide.Opponent : FighterSide.Player);

    private static float HealthPercent(FighterActor actor) =>
        actor != null && actor.Health != null && actor.Health.MaxHealth > 0f
            ? actor.Health.CurrentHealth / actor.Health.MaxHealth
            : 0f;

    // ── Debug (see FightDebugHUD's own doc — keybinds call these, never touch internals) ────────

    /// <summary>Forces the round timer to a specific value — debug only, real gameplay never calls this.</summary>
    public void DebugSetRoundTimeRemaining(float seconds) => RoundTimeRemaining = Mathf.Max(0f, seconds);

    /// <summary>Forces both fighters to equal health percentages so the NEXT natural timeout resolves
    /// as a round-level Draw (see EndRound's own doc — this NEVER awards a round win to either side
    /// regardless of MatchPointDifferential) — debug only. Goes through the real FighterHealth.
    /// ApplyDamage pipeline, never sets CurrentHealth directly. Only actually matters as a MATCH
    /// tie-break trigger when used on the FINAL round (CurrentRound == MaxRounds) — combine with
    /// DebugSetAccumulatedDifferential to deliberately test the after-Round-3 tie-break outcomes (see
    /// FightDebugHUD's own doc / this format's own test cases D-H).</summary>
    public void DebugForceDraw()
    {
        FindActors();
        if (_player?.Health == null || _opponent?.Health == null) return;

        float target = Mathf.Min(_player.Health.CurrentHealth, _opponent.Health.CurrentHealth) * 0.5f;
        _player.Health.ApplyDamage(_player.Health.CurrentHealth - target);
        _opponent.Health.ApplyDamage(_opponent.Health.CurrentHealth - target);
        DebugSetRoundTimeRemaining(0.05f);
    }

    /// <summary>Temporarily overrides MatchPointDifferential — debug only, so the after-Round-3
    /// tie-break outcomes (points to Player, points to Opponent, exact tie) can each be tested on
    /// demand instead of having to play out real rounds to reach a specific accumulated value. Only
    /// actually decides anything once round wins are tied after the final round — see
    /// ResolveTiedMatch's own doc.</summary>
    public void DebugSetAccumulatedDifferential(float value)
    {
        MatchPointDifferential = value;
        Debug.Log($"[FightMatchController] Debug: MatchPointDifferential -> {value:+0.00;-0.00}");
    }

    /// <summary>Debug only — jumps straight to a match conclusion (Win or Lose) without playing out
    /// rounds, for quickly testing the post-Fight result flow (Level Up/Continue/Fight Again/Replay
    /// Song — see FightDebugHUD's own doc). Publishes the exact same MatchEndedEvent and requests the
    /// exact same FightFlowState.MatchWon/MatchLost a real match end would — MatchResultController
    /// can't tell the difference.</summary>
    public void DebugForceMatchResult(bool playerWins)
    {
        int roundsToWin = _config != null ? Mathf.Max(1, _config.roundsToWin) : 2;
        RoundActive = false;
        _roundEndProcessed = true;

        if (playerWins) { PlayerRoundsWon = Mathf.Max(PlayerRoundsWon, roundsToWin); OpponentRoundsWon = Mathf.Min(OpponentRoundsWon, roundsToWin - 1); }
        else            { OpponentRoundsWon = Mathf.Max(OpponentRoundsWon, roundsToWin); PlayerRoundsWon = Mathf.Min(PlayerRoundsWon, roundsToWin - 1); }

        LastRoundReason = RoundEndReason.KO;
        LastRoundResolution = RoundResolution.Decisive;
        LastRoundPlayerHealthPercent = playerWins ? 1f : 0f;
        LastRoundOpponentHealthPercent = playerWins ? 0f : 1f;

        Debug.Log($"[FightMatchController] Debug: forcing match result -> {(playerWins ? "Player" : "Opponent")} wins.");
        NotifyRoundEndDisplayComplete();
    }
}
