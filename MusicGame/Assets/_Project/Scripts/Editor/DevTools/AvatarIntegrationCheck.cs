using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One lightweight play-mode check of the real avatar pipeline in both contexts:
///   Fight  — fighters spawn BEFORE the opponent is picked (real flow), then the pick is committed
///            (VersusIntro) for Bach, then re-picked (Chopin): each must become a real Humanoid
///            AvatarInstance with no visible capsule;
///   Runner — a PlayerController builds the shared player recipe with the Runner controller and
///            goes Idle → Run when running.
/// One capture each: Logs/AvatarCheck_Fight.png, Logs/AvatarCheck_Runner.png.
/// Batch: -executeMethod AvatarIntegrationCheck.RunFromCommandLine (no -quit; exits itself).
/// </summary>
[InitializeOnLoad]
public static class AvatarIntegrationCheck
{
    private const string PendingKey = "AvatarIntegrationCheck.Pending";
    private static readonly StringBuilder Log = new();
    private static int _fails;

    static AvatarIntegrationCheck()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false)) return;
            SessionState.SetBool(PendingKey, false);
            _ = Run();
        };
    }

    public static void RunFromCommandLine()
    {
        EditorSceneManager.OpenScene(MakeHumanBodyBuilder.DebugScenePath, OpenSceneMode.Single);
        SessionState.SetBool(PendingKey, true);
        EditorApplication.isPlaying = true;
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        Log.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : " — " + detail)}");
        if (!ok) _fails++;
    }

    private static async Task<bool> WaitUntil(System.Func<bool> cond, float timeout)
    {
        float end = Time.realtimeSinceStartup + timeout;
        while (Time.realtimeSinceStartup < end) { if (cond()) return true; await Task.Yield(); }
        return cond();
    }

    private static async Task Run()
    {
        Log.Clear(); _fails = 0;
        try { await Body(); }
        catch (System.Exception e) { Check("Exception", false, e.ToString()); }
        string report = (_fails == 0 ? "[AvatarIntegrationCheck] ALL PASSED\n" : $"[AvatarIntegrationCheck] {_fails} FAILED\n") + Log;
        File.WriteAllText("Logs/AvatarIntegrationCheck.txt", report);
        Debug.Log(report);
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(_fails == 0 ? 0 : 1);
    }

    private static string VisualSummary(Transform root, out bool capsuleVisible, out Animator animator)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
        capsuleVisible = renderers.Any(r => r is MeshRenderer && r.GetComponent<MeshFilter>()?.sharedMesh?.name == "Capsule");
        animator = root.GetComponentInChildren<Animator>();
        return $"{root.childCount} child(ren), {renderers.Length} visible renderer(s), humanoid {(animator != null && animator.isHuman)}";
    }

    private static async Task Body()
    {
        foreach (var p in Object.FindObjectsByType<AvatarDebugPreview>(FindObjectsSortMode.None)) p.gameObject.SetActive(false);

        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        var session = new GameObject("TestGameSession").AddComponent<GameSession>();
        session.Configure(appConfig.fightStats);
        session.Configure(appConfig.progression);
        session.SelectedOpponentLevelConfig = null; // real flow: not picked yet when Fight spawns

        // ── Fight ──
        await SceneManager.LoadSceneAsync("Fight", LoadSceneMode.Additive);
        FighterActor player = null, opponent = null;
        await WaitUntil(() =>
        {
            var actors = Object.FindObjectsByType<FighterActor>(FindObjectsSortMode.None);
            player = actors.FirstOrDefault(a => a.Side == FighterSide.Player);
            opponent = actors.FirstOrDefault(a => a.Side == FighterSide.Opponent);
            return player != null && player.AnimationDriver is AnimatorFighterAnimationDriver;
        }, 30f);
        Check("Fight: player avatar built + combat animator bound", player != null && player.AnimationDriver is AnimatorFighterAnimationDriver);
        VisualSummary(opponent.VisualRoot, out bool pendingCapsule, out _);
        Check("Fight: opponent placeholder capsule hidden while no pick is committed", !pendingCapsule);

        foreach (var (name, tier) in new[] { ("Bach", 1), ("Chopin", 2) })
        {
            var def = appConfig.opponentRoster.opponents.First(o => o.displayName == name);
            var cfg = def.GetConfigForTier(tier, out int resolved);
            var before = opponent.VisualRoot.childCount > 0 ? opponent.VisualRoot.GetChild(0) : null;
            session.SelectedOpponent = def;
            session.SelectedOpponentLevelConfig = cfg;
            session.SelectedOpponentTier = resolved;
            EventBus.Publish(new FightFlowStateChangedEvent { Previous = FightFlowState.OpponentSelection, Current = FightFlowState.VersusIntro });

            await WaitUntil(() => opponent.VisualRoot.childCount == 1 && opponent.VisualRoot.GetChild(0) != before &&
                                  opponent.AnimationDriver is AnimatorFighterAnimationDriver d && d.Override != null &&
                                  opponent.GetComponentInChildren<Animator>()?.runtimeAnimatorController == d.Override, 30f);
            await Task.Yield();
            string summary = VisualSummary(opponent.VisualRoot, out bool capsule, out var anim);
            Check($"Fight: {name} tier {resolved} → real AvatarInstance '{cfg.avatarRecipe?.name}' bound, no visible capsule",
                  opponent.VisualRoot.childCount == 1 && !capsule && anim != null && anim.isHuman &&
                  anim.runtimeAnimatorController is AnimatorOverrideController, summary);
            Check($"Fight: {name} combat profile / AI profile from its tier",
                  opponent.CombatProfile == (cfg.combatProfile ?? appConfig.fightFlow.defaultOpponentCombatProfile) &&
                  opponent.AI != null && opponent.AI.Profile == cfg.difficultyProfile,
                  $"profile {opponent.CombatProfile?.name}, ai {opponent.AI?.Profile?.name}");
        }
        float oppFeet = FootY(opponent.GetComponentInChildren<Animator>());
        Check("Fight: opponent feet on the floor", Mathf.Abs(oppFeet) < 0.2f, $"lowest foot y {oppFeet:F2}");
        Capture("AvatarCheck_Fight", new Vector3(0f, 1.05f, -4.5f), new Vector3(0f, 0.95f, 0f));

        // ── Runner ──
        var runnerGO = new GameObject("RunnerPlayerCheck");
        runnerGO.transform.position = new Vector3(0f, 0f, 8f);
        var pc = runnerGO.AddComponent<PlayerController>();
        await WaitUntil(() => runnerGO.GetComponent<RunnerAvatarAnimator>() != null, 30f);
        var anchor = runnerGO.transform.Find("VisualAnchor");
        string rs = VisualSummary(anchor, out bool runnerCapsule, out var runnerAnim);
        Check("Runner: player avatar built from the shared recipe, no visible capsule",
              anchor.childCount == 1 && !runnerCapsule && runnerAnim != null && runnerAnim.isHuman, rs);
        Check("Runner: uses RunnerPlayer.controller (not the combat controller)",
              runnerAnim != null && runnerAnim.runtimeAnimatorController == appConfig.playerAvatar.runnerAnimatorController && !runnerAnim.applyRootMotion,
              runnerAnim?.runtimeAnimatorController?.name);
        Check("Runner: VisualRenderers exposes the avatar", pc.VisualRenderers.Length > 0 && pc.VisualRenderers.All(r => r != null && r.transform.IsChildOf(anchor)));
        await WaitUntil(() => runnerAnim.GetCurrentAnimatorStateInfo(0).IsName("Idle"), 2f);
        Check("Runner: Idle before running", runnerAnim.GetCurrentAnimatorStateInfo(0).IsName("Idle"));
        pc.StartRunning();
        bool running = await WaitUntil(() => runnerAnim.GetCurrentAnimatorStateInfo(0).IsName("Run") && !runnerAnim.IsInTransition(0), 3f);
        Check("Runner: Run state once running", running);
        await Task.Delay(300);
        float runFeet = FootY(runnerAnim);
        Check("Runner: feet near the root (ground)", runFeet > -0.15f && runFeet < 0.25f, $"lowest foot y {runFeet:F2}");
        Check("Runner: root transform not moved by the avatar", runnerGO.transform.position == new Vector3(0f, 0f, 8f));
        Capture("AvatarCheck_Runner", new Vector3(3.2f, 1.0f, 8f), new Vector3(0f, 0.9f, 8f));
    }

    private static float FootY(Animator a)
    {
        if (a == null || !a.isHuman) return 99f;
        return Mathf.Min(a.GetBoneTransform(HumanBodyBones.LeftFoot).position.y, a.GetBoneTransform(HumanBodyBones.RightFoot).position.y);
    }

    private static void Capture(string file, Vector3 pos, Vector3 target)
    {
        var camGO = new GameObject("CheckCam");
        var cam = camGO.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(target);
        cam.fieldOfView = 40f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.22f, 0.24f, 0.28f);
        cam.enabled = false;
        var rt = new RenderTexture(640, 480, 24);
        var tex = new Texture2D(640, 480, TextureFormat.RGB24, false);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, 640, 480), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes($"Logs/{file}.png", tex.EncodeToPNG());
        cam.targetTexture = null;
        Object.Destroy(rt); Object.Destroy(tex); Object.Destroy(camGO);
    }
}
