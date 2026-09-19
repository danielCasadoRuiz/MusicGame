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
        _arenaConfig  = appConfig != null ? appConfig.arena       : null;
        _cameraConfig = appConfig != null ? appConfig.fightCamera : null;
        if (_arenaConfig == null)
            Debug.LogWarning("[FightSceneBootstrap] No FightArenaConfig (AppConfig.arena) configured — using hardcoded fallback spawn/bounds/speed values.");

        _previousMainCamera = Camera.main;
        if (_previousMainCamera != null && _previousMainCamera != arenaCamera)
            _previousMainCamera.enabled = false;

        if (arenaCamera != null) arenaCamera.enabled = true;

        BuildArena();
        SpawnFighters();
        WirePlayerControl();
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
        var opponentLevelConfig = GameSession.Instance != null ? GameSession.Instance.SelectedOpponentLevelConfig : null;
        GameObject opponentPrefab = opponentLevelConfig != null ? opponentLevelConfig.fighterPrefab : null;

        _playerActor   = SpawnActor("FighterPlayer",   FighterSide.Player,   playerSpawn,   playerPrefab,   playerColor);
        _opponentActor = SpawnActor("FighterOpponent", FighterSide.Opponent, opponentSpawn, opponentPrefab, opponentColor);

        _playerActor.SetOpponent(_opponentActor);
        _opponentActor.SetOpponent(_playerActor);

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

    // Only the Player gets real locomotion/facing-into-input wiring this phase — the Opponent has
    // no AIFightInputSource yet (see FighterActor/FighterMovement's own doc), so it simply stays
    // put, still fully participating in facing/DistanceToOpponent/camera/lunge-target as a plain
    // FighterActor with no Movement/MoveController attached.
    private void WirePlayerControl()
    {
        var inputController = FindFirstObjectByType<FighterInputController>();
        var moveController  = FindFirstObjectByType<FighterMoveController>();

        var playerFacing   = new RealFightFacingProvider(_playerActor.transform, _opponentActor.transform);
        var opponentFacing = new RealFightFacingProvider(_opponentActor.transform, _playerActor.transform);
        _playerActor.SetFacingProvider(playerFacing);
        _opponentActor.SetFacingProvider(opponentFacing);

        if (inputController != null)
        {
            inputController.SetFacingProvider(playerFacing);
        }
        else
        {
            Debug.LogWarning("[FightSceneBootstrap] No FighterInputController found (UIFlowController didn't add one?) — " +
                              "Player facing/movement won't respond to real input this session.");
        }

        var movement = _playerActor.gameObject.AddComponent<FighterMovement>();
        movement.Initialize(_playerActor, _opponentActor, _arenaConfig, inputController);
        _playerActor.SetMovement(movement);

        if (moveController != null)
        {
            moveController.SetMovementDriver(new RealFighterMovementDriver(movement));
            _playerActor.SetMoveController(moveController);
        }
        else
        {
            Debug.LogWarning("[FightSceneBootstrap] No FighterMoveController found (UIFlowController didn't add one?) — " +
                              "Player moves won't execute this session.");
        }
    }

    private void SetupCamera()
    {
        if (arenaCamera == null) return;
        var camController = arenaCamera.gameObject.AddComponent<FightCameraController>();
        camController.Initialize(_cameraConfig, _playerActor.transform, _opponentActor.transform);
    }
}
