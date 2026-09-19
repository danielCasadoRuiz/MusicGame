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
/// DRAW / TIE-BREAK POLICY (TimeOut only — a KO always has a real winner): compares CurrentHealth/
/// MaxHealth PERCENTAGE (never absolute values — the two sides could have different MaxHealth
/// someday). Different percentages -> Decisive, straightforward winner, exactly like before.
/// EXACTLY equal percentages is where this gets interesting — see EndRound's own doc for the full
/// tie-break: it is resolved using MatchPointDifferential (accumulated from PREVIOUS rounds only)
/// rather than simply repeating the round, specifically so a Best of 3 essentially never needs a
/// 4th round. Only the truly exceptional case — tied health AND zero accumulated differential —
/// still repeats the same round number (see RoundResolution.TrueDraw's own doc); this is a fallback,
/// not sudden death.
/// </summary>
public class FightMatchController : MonoBehaviour
{
    public static FightMatchController Instance { get; private set; }

    public int CurrentRound { get; private set; }
    public int PlayerRoundsWon { get; private set; }
    public int OpponentRoundsWon { get; private set; }
    public bool RoundActive { get; private set; }
    public float RoundTimeRemaining { get; private set; }

    /// <summary>Running total of playerHealthPercent - opponentHealthPercent across every DECISIVE
    /// or DrawResolvedByPoints round completed so far this match (see EndRound's own doc) — reset to
    /// 0 at the start of a new match (BeginMatch). Positive favors the Player, negative the Opponent.
    /// This is the ONLY thing an exactly-tied TimeOut round consults to pick a winner instead of
    /// repeating — see RoundResolution's own doc.</summary>
    public float MatchPointDifferential { get; private set; }

    /// <summary>The most recently completed round's OWN playerHealthPercent - opponentHealthPercent
    /// — debug/UI convenience, not itself used for any decision (MatchPointDifferential already
    /// folds it in once the round is decisive).</summary>
    public float LastRoundDifferential { get; private set; }

    public RoundResolution LastRoundResolution { get; private set; }

    /// <summary>How the most recently completed round ended — KO or TimeOut. Cached alongside
    /// LastRoundDifferential/LastRoundResolution purely for MatchEndedEvent's own small match-summary
    /// payload (see its own doc) — never used for any decision.</summary>
    public RoundEndReason LastRoundReason { get; private set; }
    public float LastRoundPlayerHealthPercent { get; private set; }
    public float LastRoundOpponentHealthPercent { get; private set; }

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

    private void BeginMatch()
    {
        PlayerRoundsWon = 0;
        OpponentRoundsWon = 0;
        MatchPointDifferential = 0f;
        LastRoundDifferential = 0f;
        CurrentRound = 1;
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
    /// a KO; pass null for TimeOut to let this method resolve it (including the tie-break below).
    ///
    /// TIE-BREAK (TimeOut, exactly equal health percentages only): consults MatchPointDifferential —
    /// the accumulated differential from PREVIOUS rounds ONLY, never this round's own (which is
    /// always exactly 0 in this branch, so including it would be a no-op anyway — see class doc on
    /// why this is called out explicitly rather than left as an implicit coincidence). A nonzero
    /// accumulator picks a winner (DrawResolvedByPoints); an exact zero is the one case that still
    /// repeats the round (TrueDraw).
    /// </summary>
    private void EndRound(RoundEndReason reason, FighterSide? explicitWinner)
    {
        if (_roundEndProcessed || !RoundActive) return;
        _roundEndProcessed = true;
        RoundActive = false;

        float playerPct   = HealthPercent(_player);
        float opponentPct = HealthPercent(_opponent);
        float roundDifferential = playerPct - opponentPct;

        FighterSide? winner = explicitWinner;
        var resolution = RoundResolution.Decisive;

        if (winner == null)
        {
            if (!Mathf.Approximately(playerPct, opponentPct))
            {
                winner = playerPct > opponentPct ? FighterSide.Player : FighterSide.Opponent;
            }
            else if (Mathf.Approximately(MatchPointDifferential, 0f))
            {
                resolution = RoundResolution.TrueDraw;
            }
            else
            {
                winner = MatchPointDifferential > 0f ? FighterSide.Player : FighterSide.Opponent;
                resolution = RoundResolution.DrawResolvedByPoints;
            }
        }

        if (winner == FighterSide.Player) PlayerRoundsWon++;
        else if (winner == FighterSide.Opponent) OpponentRoundsWon++;
        // TrueDraw (winner == null) awards nobody a round — see class doc.

        LastRoundDifferential = roundDifferential;
        LastRoundReason = reason;
        LastRoundPlayerHealthPercent = playerPct;
        LastRoundOpponentHealthPercent = opponentPct;
        if (resolution != RoundResolution.TrueDraw)
            MatchPointDifferential += roundDifferential; // always 0 for DrawResolvedByPoints too — see doc
        LastRoundResolution = resolution;

        Debug.Log($"[FightMatchController] Round {CurrentRound} ended — reason:{reason} resolution:{resolution} " +
                  $"winner:{(winner.HasValue ? winner.Value.ToString() : "TrueDraw")} differential:{roundDifferential:+0.00;-0.00} " +
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

    /// <summary>Called by RoundEndController once its own KO/TIME-UP (+ DRAW / WINS ON POINTS, when
    /// applicable) display has finished — the ONE real branch point in the whole flow (see class
    /// doc). Never called from anywhere else.</summary>
    public void NotifyRoundEndDisplayComplete()
    {
        if (LastRoundResolution == RoundResolution.TrueDraw)
        {
            RepeatRound();
            return;
        }

        int roundsToWin = _config != null ? Mathf.Max(1, _config.roundsToWin) : 2;

        if (PlayerRoundsWon >= roundsToWin)
        {
            // Level Up is decided HERE, deterministically, the instant the match is officially won —
            // never deferred to Continue (task's own explicit "no esperis a Continue per decidir si
            // ha pujat" requirement) — so MatchEndedEvent already carries the old/new level for
            // MatchResultController to display.
            int oldLevel = GameSession.Instance != null ? GameSession.Instance.PlayerLevel : 1;
            int newLevel = GameSession.Instance != null ? GameSession.Instance.LevelUp() : oldLevel;

            EventBus.Publish(BuildMatchEndedEvent(FighterSide.Player, oldLevel, newLevel));
            FightFlowController.Instance?.RequestState(FightFlowState.MatchWon);
        }
        else if (OpponentRoundsWon >= roundsToWin)
        {
            // A loss never touches PlayerLevel — Old/New are the same, unchanged value (task's own
            // explicit "si perds, no baixa, no puja" requirement).
            int level = GameSession.Instance != null ? GameSession.Instance.PlayerLevel : 1;

            EventBus.Publish(BuildMatchEndedEvent(FighterSide.Opponent, level, level));
            FightFlowController.Instance?.RequestState(FightFlowState.MatchLost);
        }
        else
        {
            StartNextRound();
        }
    }

    private MatchEndedEvent BuildMatchEndedEvent(FighterSide winner, int oldLevel, int newLevel) => new MatchEndedEvent
    {
        Winner = winner,
        PlayerRoundsWon = PlayerRoundsWon,
        OpponentRoundsWon = OpponentRoundsWon,
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

    /// <summary>TrueDraw only — same round number, nobody scored, everything else resets exactly
    /// like a normal next round (task's own explicit "currentRound no avança" requirement).</summary>
    private void RepeatRound()
    {
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
    /// as a health-tie (Decisive-or-not is then decided by MatchPointDifferential, same as a real
    /// one — see EndRound's own doc) — debug only. Goes through the real FighterHealth.ApplyDamage
    /// pipeline, never sets CurrentHealth directly. Combine with DebugSetAccumulatedDifferential to
    /// deliberately test all three tie-break outcomes (see FightDebugHUD's own doc).</summary>
    public void DebugForceDraw()
    {
        FindActors();
        if (_player?.Health == null || _opponent?.Health == null) return;

        float target = Mathf.Min(_player.Health.CurrentHealth, _opponent.Health.CurrentHealth) * 0.5f;
        _player.Health.ApplyDamage(_player.Health.CurrentHealth - target);
        _opponent.Health.ApplyDamage(_opponent.Health.CurrentHealth - target);
        DebugSetRoundTimeRemaining(0.05f);
    }

    /// <summary>Temporarily overrides MatchPointDifferential — debug only, so the three tie-break
    /// outcomes (points to Player, points to Opponent, TrueDraw) can each be tested on demand
    /// instead of having to play out real rounds to reach a specific accumulated value.</summary>
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
