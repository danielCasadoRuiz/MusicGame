using UnityEngine;

/// <summary>
/// Lives in the real Fight Mode Scene (Fight.unity), loaded additively by SceneFlowController the
/// instant GameFlowState becomes Fight and unloaded the instant it leaves Fight (see
/// SceneFlowController's GameFlowStateChangedEvent reaction). Still an architectural SHELL, not a
/// real fighting game: creates a flat arena placeholder plus a player and an AI enemy placeholder,
/// using whatever data the Runner already prepared (GameSession's SelectedSong/Profile/
/// RunnerResults/DetectedMusicStyleId) and CurrentTheme — it never re-analyzes music or re-resolves
/// a style itself, only reads what's already there.
///
/// The Fight HUD (top bar, timer, health bars, pause menu) is NOT here — that's FightController,
/// living in the always-loaded UI Scene (see that class's own doc). This bootstrap only owns this
/// scene's own 3D placeholder content and camera.
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

    private static readonly Color EnemyColor = new(0.9f, 0.25f, 0.2f);

    private Camera _previousMainCamera;

    private void Start()
    {
        LogIncomingData();

        _previousMainCamera = Camera.main;
        if (_previousMainCamera != null && _previousMainCamera != arenaCamera)
            _previousMainCamera.enabled = false;

        if (arenaCamera != null) arenaCamera.enabled = true;

        BuildArena();
        SpawnPlaceholders();
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

    private void SpawnPlaceholders()
    {
        Color playerColor = ThemeManager.Instance != null && ThemeManager.Instance.CurrentTheme != null && ThemeManager.Instance.CurrentTheme.UI != null
            ? ThemeManager.Instance.CurrentTheme.UI.accentColor
            : new Color(0.2f, 0.6f, 1f);

        SpawnCapsule("FightPlayerPlaceholder", new Vector3(-1.5f, 1f, 0f), playerColor);
        SpawnCapsule("FightEnemyPlaceholder",  new Vector3(1.5f, 1f, 0f), EnemyColor);
    }

    private static void SpawnCapsule(string name, Vector3 position, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        go.transform.position = position;
        var rend = go.GetComponent<Renderer>();
        if (rend != null) rend.material.color = color;
    }
}
