using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Content setup for the real avatar pipeline in BOTH presentation contexts (idempotent):
///   - RunnerPlayer.controller: Humanoid Idle (default) ⇄ Run on bool "Running", reusing the
///     MakeHuman rig-validation clips (Avatar/AnimationTests/Clips/Derived/Idle|Run.anim);
///   - PlayerAvatarConfig.asset: the ONE provisional player recipe (the one Fight already used,
///     FightArenaConfig.playerAvatarRecipe) + the Runner controller, assigned to AppConfig.playerAvatar;
///   - validates that every production composer tier (and defaultConfig) resolves an AvatarRecipe,
///     assigning the shared provisional opponent recipe only where one is missing.
/// Menu: Tools > MusicGame > Avatar > Setup Player Avatar (Runner + Fight).
/// Batch: -executeMethod PlayerAvatarSetup.SetupFromCommandLine (writes Logs/PlayerAvatarSetup.txt).
/// </summary>
public static class PlayerAvatarSetup
{
    private const string Folder         = "Assets/_Project/Configs/Player";
    private const string ControllerPath = Folder + "/RunnerPlayer.controller";
    private const string ConfigPath     = Folder + "/PlayerAvatarConfig.asset";
    private const string IdleClipPath   = "Assets/_Project/Avatar/AnimationTests/Clips/Derived/Idle.anim";
    private const string RunClipPath    = "Assets/_Project/Avatar/AnimationTests/Clips/Derived/Run.anim";
    private const string OpponentRecipePath = "Assets/_Project/Avatar/MakeHuman/Content/Recipes/Avatar_MakeHuman_TestMale.asset";

    [MenuItem("Tools/MusicGame/Avatar/Setup Player Avatar (Runner + Fight)")]
    public static void SetupMenu() => Debug.Log(Setup());

    public static void SetupFromCommandLine()
    {
        string report = Setup();
        File.WriteAllText("Logs/PlayerAvatarSetup.txt", report);
        Debug.Log(report);
        EditorApplication.Exit(report.Contains("ERROR") ? 1 : 0);
    }

    public static string Setup()
    {
        var sb = new StringBuilder("[PlayerAvatarSetup]\n");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Project/Configs", "Player");

        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        if (appConfig == null) return sb.AppendLine("ERROR: Resources/AppConfig not found").ToString();

        var controller = BuildRunnerController(sb);

        var config = AssetDatabase.LoadAssetAtPath<PlayerAvatarConfigSO>(ConfigPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<PlayerAvatarConfigSO>();
            AssetDatabase.CreateAsset(config, ConfigPath);
        }
        if (config.avatarRecipe == null && appConfig.arena != null) config.avatarRecipe = appConfig.arena.playerAvatarRecipe;
        config.runnerAnimatorController = controller;
        EditorUtility.SetDirty(config);
        appConfig.playerAvatar = config;
        EditorUtility.SetDirty(appConfig);
        sb.AppendLine($"  {(config.avatarRecipe != null ? "PASS" : "ERROR")}  player recipe: {(config.avatarRecipe != null ? config.avatarRecipe.name : "(none)")} (shared Runner + Fight)");
        sb.AppendLine($"  {(controller != null ? "PASS" : "ERROR")}  runner controller: {ControllerPath}");

        ValidateOpponents(appConfig, sb);

        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    private static AnimatorController BuildRunnerController(StringBuilder sb)
    {
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);
        var run  = AssetDatabase.LoadAssetAtPath<AnimationClip>(RunClipPath);
        if (idle == null || run == null)
        {
            sb.AppendLine($"  ERROR  missing clip(s): Idle {idle != null}, Run {run != null}");
            return null;
        }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                         ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        // Rebuild the graph from scratch every time (idempotent, no stale states).
        foreach (var p in controller.parameters) controller.RemoveParameter(p);
        controller.AddParameter(RunnerAvatarAnimator.RunningParameter, AnimatorControllerParameterType.Bool);

        var sm = controller.layers[0].stateMachine;
        foreach (var st in sm.states) sm.RemoveState(st.state);

        var idleState = sm.AddState("Idle");
        idleState.motion = idle;
        idleState.iKOnFeet = true;
        var runState = sm.AddState("Run");
        runState.motion = run;
        runState.iKOnFeet = true;
        sm.defaultState = idleState;

        var toRun = idleState.AddTransition(runState);
        toRun.hasExitTime = false;
        toRun.duration = 0.15f;
        toRun.AddCondition(AnimatorConditionMode.If, 0f, RunnerAvatarAnimator.RunningParameter);
        var toIdle = runState.AddTransition(idleState);
        toIdle.hasExitTime = false;
        toIdle.duration = 0.2f;
        toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, RunnerAvatarAnimator.RunningParameter);

        EditorUtility.SetDirty(controller);
        sb.AppendLine($"  PASS  RunnerPlayer.controller: Idle ({idle.name}, loop {idle.isLooping}) ⇄ Run ({run.name}, loop {run.isLooping})");
        return controller;
    }

    private static void ValidateOpponents(AppConfigSO appConfig, StringBuilder sb)
    {
        var roster = appConfig.opponentRoster;
        if (roster == null) { sb.AppendLine("  ERROR  AppConfig.opponentRoster missing"); return; }
        var shared = AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>(OpponentRecipePath);

        int tiers = 0, assigned = 0;
        foreach (var opponent in roster.opponents)
        {
            if (opponent == null) { sb.AppendLine("  ERROR  null opponent in roster"); continue; }
            bool changed = false;
            var recipes = new System.Collections.Generic.HashSet<string>();
            foreach (var level in opponent.levels)
            {
                if (level == null) continue;
                tiers++;
                if (level.avatarRecipe == null && shared != null) { level.avatarRecipe = shared; assigned++; changed = true; }
                if (level.avatarRecipe == null) sb.AppendLine($"  ERROR  {opponent.displayName} tier {level.level}: no AvatarRecipe");
                else recipes.Add(level.avatarRecipe.name);
            }
            if (opponent.defaultConfig != null && opponent.defaultConfig.avatarRecipe == null && shared != null)
            { opponent.defaultConfig.avatarRecipe = shared; assigned++; changed = true; }
            if (changed) EditorUtility.SetDirty(opponent);
            sb.AppendLine($"  {(recipes.Count > 0 ? "PASS" : "ERROR")}  {opponent.displayName}: {opponent.levels.Length} tier(s) → {string.Join(", ", recipes)}");
        }
        sb.AppendLine($"  {roster.opponents.Length} opponents, {tiers} tier configs, {assigned} missing recipe(s) assigned the shared provisional '{(shared != null ? shared.name : "(none)")}'");
    }
}
