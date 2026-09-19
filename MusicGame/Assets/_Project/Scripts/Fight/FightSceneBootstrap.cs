using UnityEngine;

/// <summary>
/// Lives in the real Fight Mode Scene (Fight.unity), loaded additively by SceneFlowController the
/// instant GameFlowState becomes Fight and unloaded the instant it leaves Fight (see
/// SceneFlowController's GameFlowStateChangedEvent reaction). Spawns the real Player/Opponent
/// FighterActors (capsule visuals — no humanoids exist yet, see FighterActor's own doc), a flat
/// arena floor, and this scene's own Fight camera, using whatever data the Runner/Opponent
/// Selection already prepared (GameSession's SelectedSong/Profile/RunnerResults/
/// DetectedMusicStyleId/SelectedOpponentLevelConfig) and CurrentTheme — it never re-analyzes music,
/// re-resolves a style, or re-resolves an opponent-per-level itself, only reads what's already
/// there and cached.
///
/// BOTH fighters now get the EXACT SAME wiring (see WireFighter) — FighterInputController,
/// FighterMovement, FighterMoveController, FighterAttack, FighterHitReaction, FighterGuard — the
/// ONLY difference is which IFightInputSource drives each one's own FighterInputController
/// (Player: HumanFightInputSource; Opponent: AIFightInputSource, driven by a FighterAI "brain" —
/// see that class's own doc) and that only the Opponent gets a FighterAI at all.
///
/// The Fight HUD (top bar, timer, health bars, pause menu) is NOT here — that's FightController,
/// living in the always-loaded UI Scene (see that class's own doc). This bootstrap only owns this
/// scene's own 3D content (arena, fighters, camera).
///
/// Disables whatever camera was active right before Fight finished loading (Runner's own, almost
/// always — LoadMode loads Fight BEFORE unloading Runner, so Runner's camera is still briefly alive
/// at this exact moment) so this scene's camera is what actually renders during that brief overlap,
/// and "restores" it on OnDestroy — in practice a no-op today, since Runner and its camera are
/// fully unloaded by SceneFlowController shortly after (Fight REPLACES Runner, not overlays it — see
/// SceneFlowController's own doc), so the reference is already a destroyed Unity Object by then and
/// the restore harmlessly skips. Kept anyway: it's still correct, and cheap insurance against a
/// future change that makes Fight coexist with something else again.
/// </summary>
public class FightSceneBootstrap : MonoBehaviour
{
    [SerializeField] private Camera arenaCamera;

    private Camera _previousMainCamera;
    private FightArenaConfig _arenaConfig;
    private FightCameraConfig _cameraConfig;
    private FightCombatBalanceConfig _combatBalanceConfig;

    private FighterActor _playerActor;
    private FighterActor _opponentActor;

    // Same "Mode Scene opened directly in the Editor" allowance RunnerSceneBootstrap already has
    // — lets a developer open Fight.unity and press Play directly (with FlowConfigSO.initialState
    // temporarily set to Fight) without SceneFlowController trying to also load it, which is
    // exactly the debug entry point the Fight-flow tooling relies on (see
    // OpponentSelectionController/FightFlowController's own debug logging).
    private void OnEnable()
    {
        AppBootstrap.Context?.SceneFlow.NotifyCurrentModeAlreadyLoaded(GameMode.Fight);
    }

    private void Start()
    {
        LogIncomingData();

        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        _arenaConfig         = appConfig != null ? appConfig.arena         : null;
        _cameraConfig        = appConfig != null ? appConfig.fightCamera   : null;
        _combatBalanceConfig = appConfig != null ? appConfig.combatBalance : null;
        if (_arenaConfig == null)
            Debug.LogWarning("[FightSceneBootstrap] No FightArenaConfig (AppConfig.arena) configured — using hardcoded fallback spawn/bounds/speed values.");

        _previousMainCamera = Camera.main;
        if (_previousMainCamera != null && _previousMainCamera != arenaCamera)
            _previousMainCamera.enabled = false;

        if (arenaCamera != null) arenaCamera.enabled = true;

        BuildArena();
        SpawnFighters();
        SetupCamera();
    }

    private void OnDestroy()
    {
        if (_previousMainCamera != null)
            _previousMainCamera.enabled = true;
    }

    private void LogIncomingData()
    {
        var session      = GameSession.Instance;
        bool hasProfile  = session?.Profile != null;
        bool hasResults  = session?.RunnerResults != null;
        bool hasFightStats = session?.FighterStats != null;
        var style        = session != null ? session.DetectedMusicStyleId : MusicStyleId.Unknown;
        bool hasTheme    = ThemeManager.Instance != null && ThemeManager.Instance.CurrentTheme != null;

        Debug.Log($"[FightSceneBootstrap] Entering Fight — Profile:{hasProfile} RunnerResults:{hasResults} " +
                  $"FighterStats:{hasFightStats} Style:{style} CurrentTheme:{hasTheme}");

        if (hasFightStats)
        {
            var sb = new System.Text.StringBuilder("[FightSceneBootstrap] FighterStats: ");
            foreach (var kv in session.FighterStats.Values)
                sb.Append($"{kv.Key}={kv.Value:F1} ");
            Debug.Log(sb.ToString());
        }

        if (!hasProfile || !hasResults)
            Debug.LogWarning("[FightSceneBootstrap] Missing Profile/RunnerResults — Fight was reached " +
                              "without a completed Runner run (fine for direct testing, not for the real flow).");
    }

    private void BuildArena()
    {
        var arena = GameObject.CreatePrimitive(PrimitiveType.Plane);
        arena.name = "ArenaFloorPlaceholder";
        arena.transform.position   = Vector3.zero;
        arena.transform.localScale = new Vector3(2f, 1f, 2f);
    }

    // ── Fighters ──────────────────────────────────────────────────────────────

    private void SpawnFighters()
    {
        Vector3 playerSpawn   = _arenaConfig != null ? _arenaConfig.playerSpawnPosition   : new Vector3(-1.5f, 1f, 0f);
        Vector3 opponentSpawn = _arenaConfig != null ? _arenaConfig.opponentSpawnPosition : new Vector3(1.5f, 1f, 0f);

        // Theme accent (when available) wins over the config's own debug color for the Player,
        // same preference the old placeholder capsule already had — purely cosmetic, never read by
        // anything gameplay-relevant.
        Color playerColor = ThemeManager.Instance != null && ThemeManager.Instance.CurrentTheme != null && ThemeManager.Instance.CurrentTheme.UI != null
            ? ThemeManager.Instance.CurrentTheme.UI.accentColor
            : (_arenaConfig != null ? _arenaConfig.playerDebugColor : new Color(0.2f, 0.6f, 1f));
        Color opponentColor = _arenaConfig != null ? _arenaConfig.opponentDebugColor : new Color(0.9f, 0.25f, 0.2f);

        GameObject playerPrefab = _arenaConfig != null ? _arenaConfig.playerFighterPrefab : null;

        // The EXACT version the player already saw during Opponent Selection/Versus — never
        // re-resolved here (see class doc and GameSession.SelectedOpponentLevelConfig's own doc).
        // This is ALSO where the Opponent's AIDifficultyProfile comes from (task's own explicit
        // "no tornis a resoldre el rival per level" requirement) — never re-derived elsewhere.
        var opponentLevelConfig = GameSession.Instance != null ? GameSession.Instance.SelectedOpponentLevelConfig : null;
        GameObject opponentPrefab = opponentLevelConfig != null ? opponentLevelConfig.fighterPrefab : null;
        AIDifficultyProfile aiProfile = opponentLevelConfig != null ? opponentLevelConfig.difficultyProfile : null;
        if (aiProfile == null)
            Debug.LogWarning("[FightSceneBootstrap] No AIDifficultyProfile (SelectedOpponentLevelConfig.difficultyProfile) configured — the Opponent will use flat 0.5-everywhere AI defaults this match.");

        _playerActor   = SpawnActor("FighterPlayer",   FighterSide.Player,   playerSpawn,   playerPrefab,   playerColor);
        _opponentActor = SpawnActor("FighterOpponent", FighterSide.Opponent, opponentSpawn, opponentPrefab, opponentColor);

        _playerActor.SetOpponent(_opponentActor);
        _opponentActor.SetOpponent(_playerActor);

        // Player: whatever the Runner actually produced (see GameSession.FighterStats' own doc);
        // Opponent: this level's hand-authored profile, or a flat 100-everywhere fallback (see
        // OpponentLevelConfig.combatStats/FighterStats.Default's own doc). DELIBERATELY separate
        // from aiProfile above — combat capability (FighterStats) and AI quality (AIDifficultyProfile)
        // are independent axes (task's own explicit requirement).
        var playerStats = GameSession.Instance != null && GameSession.Instance.FighterStats != null
            ? GameSession.Instance.FighterStats
            : FighterStats.Default();
        var opponentStats = opponentLevelConfig != null && opponentLevelConfig.combatStats != null
            ? opponentLevelConfig.combatStats.Build()
            : FighterStats.Default();
        _playerActor.SetStats(playerStats);
        _opponentActor.SetStats(opponentStats);

        WireFighter(_playerActor, _opponentActor, isPlayer: true, aiProfile: null);
        WireFighter(_opponentActor, _playerActor, isPlayer: false, aiProfile: aiProfile);

        Debug.Log($"[FightSceneBootstrap] Spawned fighters — Player visual:{(_playerActor.UsedFallbackCapsule ? "capsule fallback" : "prefab")} " +
                  $"Opponent visual:{(_opponentActor.UsedFallbackCapsule ? "capsule fallback" : "prefab")}");
    }

    private static FighterActor SpawnActor(string name, FighterSide side, Vector3 position, GameObject visualPrefab, Color debugColor)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        var actor = go.AddComponent<FighterActor>();
        actor.Initialize(side, visualPrefab, debugColor);
        return actor;
    }

    /// <summary>
    /// Gives `self` the FULL Fighter pipeline — FighterInputController (Human or AI-driven),
    /// FighterMovement, FighterMoveController, FighterAttack, FighterHitReaction, FighterGuard —
    /// identically for Player and Opponent (see class doc). The ONLY branch is which
    /// IFightInputSource drives the input controller, and whether a FighterAI "brain" is attached
    /// to actually decide what that AI input source should do.
    /// </summary>
    private void WireFighter(FighterActor self, FighterActor other, bool isPlayer, AIDifficultyProfile aiProfile)
    {
        var facingProvider = new RealFightFacingProvider(self.transform, other.transform);
        self.SetFacingProvider(facingProvider);

        AIFightInputSource aiInputSource = isPlayer ? null : new AIFightInputSource();
        IFightInputSource inputSource = isPlayer ? new HumanFightInputSource() : aiInputSource;

        var inputController = self.gameObject.AddComponent<FighterInputController>();
        inputController.SetInputSource(inputSource);
        inputController.SetFacingProvider(facingProvider);
        self.SetInputController(inputController);

        var movement = self.gameObject.AddComponent<FighterMovement>();
        movement.Initialize(self, other, _arenaConfig, inputController);
        self.SetMovement(movement);

        var moveController = self.gameObject.AddComponent<FighterMoveController>();
        moveController.SetInputController(inputController);
        moveController.SetMovementDriver(new RealFighterMovementDriver(movement));
        moveController.Stats = self.Stats;
        moveController.SetActor(self);
        self.SetMoveController(moveController);

        var attack = self.gameObject.AddComponent<FighterAttack>();
        attack.Initialize(self, other, moveController, _combatBalanceConfig);
        self.SetAttack(attack);

        // Universal on both sides — see FighterActor.HitReaction/Guard's own doc. Needs the real
        // per-side FighterInputController above, so it can't happen any earlier.
        self.AttachHitReaction(_arenaConfig, inputController);

        if (!isPlayer)
        {
            var ai = self.gameObject.AddComponent<FighterAI>();
            ai.Initialize(self, other, aiInputSource, aiProfile, _arenaConfig);
            self.SetAI(ai);
        }
    }

    private void SetupCamera()
    {
        if (arenaCamera == null) return;
        var camController = arenaCamera.gameObject.AddComponent<FightCameraController>();
        camController.Initialize(_cameraConfig, _playerActor.transform, _opponentActor.transform);
    }
}
