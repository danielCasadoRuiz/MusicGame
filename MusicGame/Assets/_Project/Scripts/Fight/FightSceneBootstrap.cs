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
    private FightFlowConfig _flowConfig;

    private FighterActor _playerActor;
    private FighterActor _opponentActor;

    /// <summary>Non-null only once the Opponent's real AvatarRecipeSO (if any — see
    /// OpponentLevelConfig.avatarRecipe's own doc) has finished building. Disposed on OnDestroy so a
    /// Fight scene unload never leaks the Avatar module's own Addressables handles/cloned meshes —
    /// this bootstrap owns that lifecycle exactly the same way it owns the camera/arena's.</summary>
    private AvatarInstance _opponentAvatarInstance;
    private AvatarInstance _playerAvatarInstance;

    // The opponent is NOT known when this scene starts in the real flow: Fight.unity loads while
    // FightFlowState is OpponentSelection and the roulette commits GameSession.SelectedOpponentLevelConfig
    // seconds later (and again for every new match inside this same scene). So the opponent's visual,
    // AI profile, stats and combat profile are applied when the selection is committed (VersusIntro)
    // — see ApplySelectedOpponent. Spawn-time application only happens when no flow is running
    // (direct scene play / tests that pre-set GameSession).
    private OpponentLevelConfig _appliedOpponentConfig;
    private int _opponentBuildToken;
    private AppConfigSO _appConfig;
    private System.Action<FightFlowStateChangedEvent> _onFightFlowChanged;

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
        _appConfig = appConfig;
        _arenaConfig         = appConfig != null ? appConfig.arena         : null;
        _cameraConfig        = appConfig != null ? appConfig.fightCamera   : null;
        _combatBalanceConfig = appConfig != null ? appConfig.combatBalance : null;
        _flowConfig          = appConfig != null ? appConfig.fightFlow     : null;
        if (_arenaConfig == null)
            Debug.LogWarning("[FightSceneBootstrap] No FightArenaConfig (AppConfig.arena) configured — using hardcoded fallback spawn/bounds/speed values.");

        _previousMainCamera = Camera.main;
        if (_previousMainCamera != null && _previousMainCamera != arenaCamera)
            _previousMainCamera.enabled = false;

        if (arenaCamera != null) arenaCamera.enabled = true;

        BuildArena();
        SpawnFighters();
        SetupCamera();

        _onFightFlowChanged = e =>
        {
            if (e.Current == FightFlowState.VersusIntro)
                ApplySelectedOpponent(GameSession.Instance != null ? GameSession.Instance.SelectedOpponentLevelConfig : null);
        };
        EventBus.Subscribe(_onFightFlowChanged);
    }

    private void OnDestroy()
    {
        if (_previousMainCamera != null)
            _previousMainCamera.enabled = true;

        if (_onFightFlowChanged != null) EventBus.Unsubscribe(_onFightFlowChanged);
        _opponentAvatarInstance?.Dispose();
        _opponentAvatarInstance = null;
        _playerAvatarInstance?.Dispose();
        _playerAvatarInstance = null;
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
        // While the roulette is still running, GameSession may hold the PREVIOUS match's pick — never
        // use it then; ApplySelectedOpponent runs when the new pick is committed (VersusIntro).
        bool selectionPending = FightFlowController.Instance != null && FightFlowController.Instance.CurrentState == FightFlowState.OpponentSelection;
        var opponentLevelConfig = !selectionPending && GameSession.Instance != null ? GameSession.Instance.SelectedOpponentLevelConfig : null;
        GameObject opponentPrefab = opponentLevelConfig != null ? opponentLevelConfig.fighterPrefab : null;
        AIDifficultyProfile aiProfile = opponentLevelConfig != null ? opponentLevelConfig.difficultyProfile : null;

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
        _playerActor.SetStats(playerStats);
        _opponentActor.SetStats(FighterStats.Default()); // real values: ApplySelectedOpponent

        WireFighter(_playerActor, _opponentActor, isPlayer: true, aiProfile: null);
        WireFighter(_opponentActor, _playerActor, isPlayer: false, aiProfile: aiProfile);

        ApplyPlayerCombatSetup();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        gameObject.AddComponent<FightDebugCombatInput>().Initialize(_playerActor, _opponentActor); // TEMPORARY debug keys 1-9
#endif

        // Player: the ONE shared player identity (AppConfig.playerAvatar — the same recipe the Runner
        // builds), legacy FightArenaConfig.playerAvatarRecipe as fallback. The placeholder capsule is
        // hidden while the avatar builds and only shown again if the build fails.
        var playerRecipe = PlayerAvatarConfigSO.ResolveRecipe(_appConfig);
        if (playerRecipe == null && _arenaConfig != null) playerRecipe = _arenaConfig.playerAvatarRecipe;
        if (playerRecipe != null)
        {
            _playerActor.SetPlaceholderVisible(false);
            _ = TryBuildAvatar(playerRecipe, _playerActor, "Player", i => { _playerAvatarInstance?.Dispose(); _playerAvatarInstance = i; return true; });
        }

        // Opponent: hidden until the committed selection gives it its avatar (no capsule flash
        // behind the roulette/versus screens).
        _opponentActor.SetPlaceholderVisible(false);
        if (opponentLevelConfig != null) ApplySelectedOpponent(opponentLevelConfig);
    }

    /// <summary>
    /// Applies the committed opponent pick (GameSession.SelectedOpponentLevelConfig — the resolved
    /// TIER config) to the already-spawned opponent fighter: stats, AI profile, combat profile/build/
    /// resources, and its visual built from the tier's AvatarRecipe through AvatarFactory. Generic for
    /// every composer; re-applied for every new match in this scene; re-committing the same config is
    /// a no-op. A production tier without a recipe is an ERROR (capsule shown only as a debug fallback).
    /// </summary>
    private void ApplySelectedOpponent(OpponentLevelConfig levelConfig)
    {
        if (_opponentActor == null) return;
        if (levelConfig == null)
        {
            Debug.LogError("[FightSceneBootstrap] Opponent selection committed with no SelectedOpponentLevelConfig — opponent keeps placeholder data.");
            _opponentActor.SetPlaceholderVisible(true);
            return;
        }
        if (levelConfig == _appliedOpponentConfig) return;
        _appliedOpponentConfig = levelConfig;

        var session = GameSession.Instance;
        string who = session != null && session.SelectedOpponent != null
            ? $"Opponent {session.SelectedOpponent.displayName} tier {session.SelectedOpponentTier}"
            : $"Opponent (level config {levelConfig.level})";

        var stats = levelConfig.combatStats != null ? levelConfig.combatStats.Build() : FighterStats.Default();
        _opponentActor.SetStats(stats);
        if (_opponentActor.MoveController != null) _opponentActor.MoveController.Stats = stats;

        if (levelConfig.difficultyProfile == null)
            Debug.LogWarning($"[FightSceneBootstrap] {who}: no AIDifficultyProfile configured — the Opponent will use flat 0.5-everywhere AI defaults this match.");
        _opponentActor.AI?.SetProfile(levelConfig.difficultyProfile);

        ApplyOpponentCombatSetup(levelConfig);

        int token = ++_opponentBuildToken;
        if (levelConfig.avatarRecipe == null)
        {
            Debug.LogError($"[FightSceneBootstrap] {who}: no AvatarRecipe on the resolved tier config — production opponents must have one. Showing the debug capsule.");
            _opponentActor.SetPlaceholderVisible(true);
            return;
        }
        _ = TryBuildAvatar(levelConfig.avatarRecipe, _opponentActor, who, i =>
        {
            if (token != _opponentBuildToken) return false; // a newer pick superseded this build
            _opponentAvatarInstance?.Dispose();
            _opponentAvatarInstance = i;
            return true;
        });
    }
    /// <summary>
    /// Combat side of each fighter (never the avatar): its FighterCombatProfileSO, its five build stats
    /// and its spendable resources.
    ///   Player   — FightFlowConfig.defaultPlayerCombatProfile (TEMPORARY), build = the Runner's
    ///              RunnerResults.CombatBuild as-is, resources = RunnerResults.Resources (a per-match
    ///              wallet; the Runner output itself is never modified).
    ///   Opponent — the selected TIER config's combatProfile (fallback defaultOpponentCombatProfile),
    ///              build = its FighterStatsProfileSO.buildStats, resources = its tier's own counts.
    /// </summary>
    private float NeutralBuildBudget => _combatBalanceConfig != null ? _combatBalanceConfig.neutralBuildBudget : 80f;

    private void ApplyPlayerCombatSetup()
    {
        var results = GameSession.Instance != null ? GameSession.Instance.RunnerResults : null;

        _playerActor.SetBuildStats(results?.CombatBuild != null ? FighterBuildStats.FromCombatBuild(results.CombatBuild) : FighterBuildStats.Even(NeutralBuildBudget));
        _playerActor.SetCombatResources(results != null ? FighterCombatResources.FromRunner(results.Resources) : new FighterCombatResources(0, 0, 0));
        _playerActor.SetCombatProfile(_flowConfig != null ? _flowConfig.defaultPlayerCombatProfile : null);
        // Until a pick is committed the opponent carries the default profile (Fighting only starts after VersusIntro).
        _opponentActor.SetCombatProfile(_flowConfig != null ? _flowConfig.defaultOpponentCombatProfile : null);
    }

    private void ApplyOpponentCombatSetup(OpponentLevelConfig opponentLevelConfig)
    {
        float neutral = NeutralBuildBudget;
        _opponentActor.SetBuildStats(opponentLevelConfig?.combatStats != null ? opponentLevelConfig.combatStats.buildStats : FighterBuildStats.Even(neutral));
        _opponentActor.SetCombatResources(opponentLevelConfig != null
            ? new FighterCombatResources(opponentLevelConfig.tripleCombos, opponentLevelConfig.quadCombos, opponentLevelConfig.specials)
            : new FighterCombatResources(0, 0, 0));
        var opponentProfile = opponentLevelConfig != null && opponentLevelConfig.combatProfile != null
            ? opponentLevelConfig.combatProfile
            : (_flowConfig != null ? _flowConfig.defaultOpponentCombatProfile : null);
        _opponentActor.SetCombatProfile(opponentProfile);

        Debug.Log($"[FightSceneBootstrap] Combat — Player profile '{(_playerActor.CombatProfile != null ? _playerActor.CombatProfile.name : "(none)")}' " +
                  $"build [{_playerActor.BuildStats}] resources [{_playerActor.CombatResources}] | Opponent profile " +
                  $"'{(opponentProfile != null ? opponentProfile.name : "(none)")}' build [{_opponentActor.BuildStats}]");
    }



    /// <summary>
    /// Integrates the Avatar module MINIMALLY (task's own explicit scope note): if this level has no
    /// avatarRecipe, this is never even called — the existing fighterPrefab/capsule fallback from
    /// SpawnActor stands completely unchanged. When it IS called, it builds the recipe via
    /// AvatarFactory directly under the Opponent's own FighterActor.VisualRoot, then swaps it in via
    /// FighterActor.ReplaceVisual — the ONLY two Avatar-module symbols this bootstrap ever touches.
    /// Never touches gameplay/hitboxes/AI (task's own explicit "no toquis" requirement) — this only
    /// ever replaces what's under VisualRoot, exactly like the capsule-vs-prefab choice already did.
    /// </summary>
    private async System.Threading.Tasks.Task TryBuildAvatar(AvatarRecipeSO recipeSO, FighterActor opponentActor, string who, System.Func<AvatarInstance, bool> keep)
    {
        var runtimeRecipe = recipeSO.ToRuntime();
        var instance = await AvatarFactory.CreateAsync(runtimeRecipe, opponentActor.VisualRoot);

        // The scene/actor may already be gone by the time an Addressables load resolves (Fight
        // exited mid-load — see the Fight-exit lifecycle hardening elsewhere in this project) —
        // dispose the freshly-built avatar instead of touching a destroyed actor.
        if (this == null || opponentActor == null)
        {
            instance.Dispose();
            return;
        }

        if (instance.Root == null)
        {
            Debug.LogError($"[FightSceneBootstrap] {who}: AvatarRecipe '{recipeSO.name}' failed to build — showing the debug capsule fallback (NOT a valid production visual).");
            instance.Dispose();
            opponentActor.SetPlaceholderVisible(true);
            return;
        }

        if (!keep(instance)) { instance.Dispose(); return; }
        opponentActor.ReplaceVisual(instance.Root);
        // The actor's gameplay origin sits at spawn height (the capsule's centre); the avatar's feet
        // must be on the arena floor (y = 0).
        instance.Root.localPosition = new Vector3(0f, -opponentActor.transform.position.y, 0f);
        instance.Root.localRotation = Quaternion.identity;
        bool bound = opponentActor.BindAnimator(instance.Animator, _flowConfig != null ? _flowConfig.combatAnimatorController : null);
        Debug.Log($"[FightSceneBootstrap] {who} avatar '{recipeSO.name}' built and applied — combat animator {(bound ? "bound" : "NOT bound")}.");
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
