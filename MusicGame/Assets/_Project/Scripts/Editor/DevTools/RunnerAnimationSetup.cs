using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Content setup for the data-driven Runner animation (idempotent):
///   1. Runner segments in the existing combat animation library (reviewed windows of existing takes,
///      chosen with RunnerClipAnalysis) → processed in-place clips via CombatAnimationImporter.
///   2. RunnerUpperBody.mask (arms/torso/head) for gesture flourishes over running legs.
///   3. ONE shared RunnerHumanoid.controller: base layer "<Role>_<i>" states, UpperBody layer
///      "Empty" + "Flourish_<i>", placeholder clips "RunnerHumanoid_<Role>_<i>" (slots per role =
///      the largest variant count any style uses).
///   4. RunnerStyle_Default / Salsa / Electronic / Classical + RunnerAnimationStyles library
///      (Latin → Salsa; Electronic/House/Techno → Electronic; Classical → Classical; rest → Default),
///      assigned to AppConfig.playerAvatar. Style assets are written only when first created
///      (manual edits win) unless -overwriteRunnerStyles is passed.
/// Menu: Tools > MusicGame > Avatar > Setup Runner Animation. Batch: RunnerAnimationSetup.SetupFromCommandLine.
/// </summary>
public static class RunnerAnimationSetup
{
    private const string Root           = "Assets/_Project/Configs/Player/RunnerAnimation";
    private const string PlaceholderDir = Root + "/Placeholders";
    private const string StylesDir      = Root + "/Styles";
    private const string ControllerPath = Root + "/RunnerHumanoid.controller";
    private const string MaskPath       = Root + "/RunnerUpperBody.mask";
    private const string LibraryPath    = Root + "/RunnerAnimationStyles.asset";
    private const string LegacyControllerPath = "Assets/_Project/Configs/Player/RunnerPlayer.controller";
    private const string CmuDir = "Assets/_Project/Avatar/AnimationTests/Clips/Derived/";

    private const string Jump1   = "EricJacobus_20210714_s010_runJump_overObstacle_tk01_ERJA_mvn232";
    private const string RifleRun = "EricJacobus_20210714_s007_runForward_assultRiffle_tk01_ERJA_mvn228";
    private const string RifleSprint = "EricJacobus_20210714_s008_sprintForward_assultRiffle_tk01_ERJA_mvn230";
    private const string Wave    = "EricJacobus_20210714_s003_fingerWave_atAudience_tk01_ERJA_mvn002";
    private const string Teach   = "EricJacobus_20210714_s001_teaching_Guestures_tk01_ERJA_mvn001";

    private static readonly (string source, string name, float start, float end, bool loop, bool keepFacing, string notes)[] Segments =
    {
        (Jump1,  "RunnerJump",           3.55f, 4.45f, false, true,  "Hurdle take-off → apex (4.1) → touchdown (4.5)."),
        (Jump1,  "RunnerLand",           4.40f, 4.95f, false, true,  "Touchdown and running recovery after the hurdle."),
        (RifleRun, "RunnerRifleRunLoop", 2.20f, 4.10f, true,  true,  "Straight stretch (yaw ±8°), 3 cycles of ~0.63 s; rigid rifle-carry arms."),
        (RifleSprint, "RunnerRifleSprintLoop", 2.60f, 4.10f, true, true, "Straight sprint stretch, 3 cycles of ~0.5 s; rifle-carry arms."),
        ("Fight_RoundHouseKick_mixamo", "RunnerSpin360", 6.70f, 7.62f, false, false, "Net ≈ -360° spin (ends facing its start) with a kick."),
        (Wave,   "RunnerWaveCrowd",      14.8f, 17.8f, false, true,  "Hands up waving at the audience."),
        (Wave,   "RunnerArmsUp",         54.6f, 57.6f, false, true,  "Both arms raised high."),
        (Teach,  "RunnerPresentGesture", 38.6f, 41.6f, false, true,  "Open presenting arm gesture."),
        ("MartialArts_thaichitake_12_DEFAULT_QUP", "RunnerTaiChiRaise", 4.5f, 9.5f, false, true, "Slow tai chi arm raise."),
        ("MartialArts_MartialArtsKata_MIXAMO_769", "RunnerKataStrikes", 11.3f, 13.3f, false, true, "Sharp kata strikes."),
    };

    // Clip keys: "cmu:<Name>" = Avatar/AnimationTests/Clips/Derived/<Name>.anim; otherwise a library
    // entry name (its in-place clip). P = provisional.
    private class E { public string key; public float speed = 1f; public bool upper = true; public bool prov = true; public string note; }
    private static E C(string key, string note, float speed = 1f, bool upper = true, bool prov = true) => new() { key = key, note = note, speed = speed, upper = upper, prov = prov };
    private static string Seg(string source, string name) => source + "__" + name;

    private class StyleDef
    {
        public string asset, display;
        public E[] idle, loco, fast, jump, land, autoReturn, flourish;
        public float chance = 0.4f, minCd = 7f, maxCd = 16f;
        public Vector2 locoRange = new(0.85f, 1.2f);
    }

    private static readonly E IdleCmu = C("cmu:Idle", "CMU idle (rig-validation clip).", prov: false);
    private static readonly E JumpSeg = C(Seg(Jump1, "RunnerJump"), "PROVISIONAL generic hurdle jump — replace with a style-specific jump.");
    private static readonly E LandSeg = C(Seg(Jump1, "RunnerLand"), "PROVISIONAL hurdle landing.");
    private static readonly E Spin    = C(Seg("Fight_RoundHouseKick_mixamo", "RunnerSpin360"), "PROVISIONAL spinning roundhouse as a 360 turn.");

    private static readonly StyleDef[] Styles =
    {
        new()
        {
            asset = "RunnerStyle_Default", display = "Default",
            idle = new[] { IdleCmu },
            loco = new[] { C("cmu:Run", "CMU run cycle — generic locomotion.", prov: false) },
            fast = new[] { C("cmu:Run", "PROVISIONAL: same run, faster cadence (no clean sprint clip).", 1.2f) },
            jump = new[] { JumpSeg }, land = new[] { LandSeg }, autoReturn = new[] { Spin },
            flourish = new[] { C(Seg(Wave, "RunnerWaveCrowd"), "PROVISIONAL wave at the crowd."), C(Seg(Wave, "RunnerArmsUp"), "PROVISIONAL arms up.") },
            chance = 0.3f, minCd = 9f, maxCd = 18f,
        },
        new()
        {
            asset = "RunnerStyle_Salsa", display = "Salsa / Latin",
            loco = new[] { C("cmu:Run", "PROVISIONAL: no salsa locomotion clip yet — generic run.", 0.95f) },
            fast = new[] { C("cmu:Run", "PROVISIONAL: generic run, faster.", 1.2f) },
            jump = new[] { C(Seg(Jump1, "RunnerJump"), "PROVISIONAL: wants a playful small-turn jump.") },
            land = new[] { LandSeg },
            autoReturn = new[] { C(Seg("Fight_RoundHouseKick_mixamo", "RunnerSpin360"), "PROVISIONAL dance-turn substitute (kick spin).") },
            flourish = new[]
            {
                C("Fight_ShadowBoxing_mixamo__DuckWeave", "PROVISIONAL boxing weave as hip/shoulder sway."),
                C(Seg(Wave, "RunnerWaveCrowd"), "PROVISIONAL playful wave."),
                C(Seg(Wave, "RunnerArmsUp"), "PROVISIONAL arms up."),
                C(Seg("Fight_RoundHouseKick_mixamo", "RunnerSpin360"), "PROVISIONAL full-body small spin.", 1f, upper: false),
            },
            chance = 0.55f, minCd = 5f, maxCd = 11f,
        },
        new()
        {
            asset = "RunnerStyle_Electronic", display = "Electronic",
            loco = new[] { C(Seg(RifleRun, "RunnerRifleRunLoop"), "PROVISIONAL rifle-carry run: rigid fixed arms read as mechanical.") },
            fast = new[] { C(Seg(RifleSprint, "RunnerRifleSprintLoop"), "PROVISIONAL rifle-carry sprint.") },
            jump = new[] { C(Seg(Jump1, "RunnerJump"), "PROVISIONAL: wants a rigid robot hop.", 1.1f) },
            land = new[] { LandSeg },
            autoReturn = new[] { C(Seg("Fight_RoundHouseKick_mixamo", "RunnerSpin360"), "PROVISIONAL snappier spin as a mechanical turn.", 1.25f) },
            flourish = new[] { C(Seg("MartialArts_MartialArtsKata_MIXAMO_769", "RunnerKataStrikes"), "PROVISIONAL sharp kata strikes as robotic arm moves.") },
            chance = 0.4f, minCd = 7f, maxCd = 15f, locoRange = new Vector2(0.9f, 1.15f),
        },
        new()
        {
            asset = "RunnerStyle_Classical", display = "Classical",
            loco = new[] { C("cmu:Run", "PROVISIONAL: no ballet locomotion — generic run at a lighter cadence.", 0.9f) },
            fast = new[] { C("cmu:Run", "PROVISIONAL: generic run, faster.", 1.1f) },
            jump = new[] { C(Seg(Jump1, "RunnerJump"), "PROVISIONAL: wants an elegant ballet jump.", 0.9f) },
            land = new[] { LandSeg },
            autoReturn = new[] { C(Seg("Fight_RoundHouseKick_mixamo", "RunnerSpin360"), "PROVISIONAL pirouette substitute (slower spin).", 0.85f) },
            flourish = new[]
            {
                C(Seg("MartialArts_thaichitake_12_DEFAULT_QUP", "RunnerTaiChiRaise"), "PROVISIONAL tai chi arm raise as port de bras.", 1.35f),
                C(Seg(Teach, "RunnerPresentGesture"), "PROVISIONAL theatrical presenting gesture."),
            },
            chance = 0.4f, minCd = 8f, maxCd = 16f,
        },
    };

    [MenuItem("Tools/MusicGame/Avatar/Setup Runner Animation")]
    public static void SetupMenu() => Debug.Log(Setup(overwriteStyles: false));

    public static void SetupFromCommandLine()
    {
        string report = Setup(System.Environment.GetCommandLineArgs().Contains("-overwriteRunnerStyles"));
        File.WriteAllText("Logs/RunnerAnimationSetup.txt", report);
        Debug.Log(report);
        EditorApplication.Exit(report.Contains("ERROR") ? 1 : 0);
    }

    public static string Setup(bool overwriteStyles)
    {
        var sb = new StringBuilder("[RunnerAnimationSetup]\n");
        foreach (var d in new[] { Root, PlaceholderDir, StylesDir }) EnsureFolder(d);

        // 1. Segments.
        var library = AssetDatabase.LoadAssetAtPath<CombatAnimationLibrarySO>(CombatAnimationImporter.LibraryPath);
        int added = 0;
        foreach (var seg in Segments)
        {
            if (library.segments.Any(s => s.sourceEntry == seg.source && s.name == seg.name)) continue;
            if (!library.entries.Any(e => e.name == seg.source)) { sb.AppendLine($"  ERROR  source take '{seg.source}' not in the library"); continue; }
            library.segments.Add(new CombatSegmentDefinition { sourceEntry = seg.source, name = seg.name, start = seg.start, end = seg.end,
                category = CombatAnimationCategory.Movement, loop = seg.loop, keepFacing = seg.keepFacing, review = CombatReviewStatus.Approved,
                notes = "Runner segment (RunnerClipAnalysis 2026-10-01). " + seg.notes });
            added++;
        }
        if (added > 0)
        {
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            CombatAnimationImporter.ImportAll(out _);
            library = AssetDatabase.LoadAssetAtPath<CombatAnimationLibrarySO>(CombatAnimationImporter.LibraryPath);
        }
        sb.AppendLine($"  segments: {added} added ({Segments.Length} defined)");

        // 2. Styles (data first: the controller's slot counts come from them).
        var styles = new Dictionary<string, RunnerAnimationStyleSO>();
        foreach (var def in Styles)
        {
            string path = $"{StylesDir}/{def.asset}.asset";
            var style = AssetDatabase.LoadAssetAtPath<RunnerAnimationStyleSO>(path);
            bool created = style == null;
            if (created) { style = ScriptableObject.CreateInstance<RunnerAnimationStyleSO>(); AssetDatabase.CreateAsset(style, path); }
            if (created || overwriteStyles)
            {
                style.displayName = def.display;
                style.idle = Entries(def.idle, library, sb, def.asset);
                style.locomotion = Entries(def.loco, library, sb, def.asset);
                style.fastLocomotion = Entries(def.fast, library, sb, def.asset);
                style.jump = Entries(def.jump, library, sb, def.asset);
                style.land = Entries(def.land, library, sb, def.asset);
                style.autoReturn = Entries(def.autoReturn, library, sb, def.asset);
                style.flourish = Entries(def.flourish, library, sb, def.asset);
                style.fall = System.Array.Empty<RunnerAnimationEntry>();
                style.flourishChance = def.chance;
                style.flourishMinCooldown = def.minCd;
                style.flourishMaxCooldown = def.maxCd;
                style.locomotionSpeedRange = def.locoRange;
                EditorUtility.SetDirty(style);
            }
            styles[def.asset] = style;
        }

        // 3. Mask + controller.
        var mask = BuildMask();
        var slots = new Dictionary<RunnerAnimationRole, int>();
        foreach (RunnerAnimationRole role in System.Enum.GetValues(typeof(RunnerAnimationRole)))
            slots[role] = Mathf.Max(1, styles.Values.Max(s => s.Usable(role).Length));
        slots[RunnerAnimationRole.Flourish] = Mathf.Max(slots[RunnerAnimationRole.Flourish], 4); // headroom for new variants
        var controller = BuildController(mask, slots, sb);

        // 4. Library + config.
        var lib = AssetDatabase.LoadAssetAtPath<RunnerAnimationStyleLibrarySO>(LibraryPath);
        if (lib == null) { lib = ScriptableObject.CreateInstance<RunnerAnimationStyleLibrarySO>(); AssetDatabase.CreateAsset(lib, LibraryPath); }
        lib.defaultStyle = styles["RunnerStyle_Default"];
        lib.mappings = new[]
        {
            new RunnerAnimationStyleLibrarySO.Mapping { musicStyles = new[] { MusicStyleId.Latin }, style = styles["RunnerStyle_Salsa"] },
            new RunnerAnimationStyleLibrarySO.Mapping { musicStyles = new[] { MusicStyleId.Electronic, MusicStyleId.House, MusicStyleId.Techno }, style = styles["RunnerStyle_Electronic"] },
            new RunnerAnimationStyleLibrarySO.Mapping { musicStyles = new[] { MusicStyleId.Classical }, style = styles["RunnerStyle_Classical"] },
        };
        EditorUtility.SetDirty(lib);

        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        if (appConfig?.playerAvatar == null) sb.AppendLine("  ERROR  AppConfig.playerAvatar missing (run PlayerAvatarSetup first)");
        else
        {
            appConfig.playerAvatar.runnerAnimatorController = controller;
            appConfig.playerAvatar.runnerAnimationStyles = lib;
            EditorUtility.SetDirty(appConfig.playerAvatar);
        }
        if (AssetDatabase.LoadAssetAtPath<Object>(LegacyControllerPath) != null)
        {
            AssetDatabase.DeleteAsset(LegacyControllerPath);
            sb.AppendLine("  removed the superseded RunnerPlayer.controller (Idle/Run only)");
        }
        AssetDatabase.SaveAssets();

        // Report.
        foreach (var def in Styles)
        {
            var s = styles[def.asset];
            sb.AppendLine($"  style {s.name} ({s.displayName}):");
            foreach (RunnerAnimationRole role in System.Enum.GetValues(typeof(RunnerAnimationRole)))
            {
                var entries = s.Usable(role);
                if (entries.Length == 0) { sb.AppendLine($"      {role,-15} — (falls back to Default{(role == RunnerAnimationRole.Fall ? " → Jump" : "")})"); continue; }
                sb.AppendLine($"      {role,-15} " + string.Join(", ", entries.Select(e => $"{e.clip.name} ×{e.playbackSpeed:0.##}{(role == RunnerAnimationRole.Flourish ? (e.upperBodyOnly ? " [upper]" : " [full]") : "")}{(e.provisional ? " (P)" : "")}")));
            }
        }
        sb.AppendLine($"  {(styles["RunnerStyle_Default"].Usable(RunnerAnimationRole.Locomotion).Length > 0 ? "PASS" : "ERROR")}  Default has locomotion (global fallback)");
        return sb.ToString();
    }

    private static RunnerAnimationEntry[] Entries(E[] defs, CombatAnimationLibrarySO library, StringBuilder sb, string style)
    {
        if (defs == null) return System.Array.Empty<RunnerAnimationEntry>();
        var list = new List<RunnerAnimationEntry>();
        foreach (var d in defs)
        {
            AnimationClip clip = d.key.StartsWith("cmu:")
                ? AssetDatabase.LoadAssetAtPath<AnimationClip>(CmuDir + d.key.Substring(4) + ".anim")
                : library.entries.FirstOrDefault(e => e.name == d.key)?.inPlaceClip;
            if (clip == null) { sb.AppendLine($"  ERROR  {style}: clip '{d.key}' not found"); continue; }
            list.Add(new RunnerAnimationEntry { clip = clip, playbackSpeed = d.speed, upperBodyOnly = d.upper, provisional = d.prov, note = d.note });
        }
        return list.ToArray();
    }

    private static AvatarMask BuildMask()
    {
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        if (mask == null) { mask = new AvatarMask(); AssetDatabase.CreateAsset(mask, MaskPath); }
        foreach (AvatarMaskBodyPart part in System.Enum.GetValues(typeof(AvatarMaskBodyPart)))
        {
            if (part == AvatarMaskBodyPart.LastBodyPart) continue;
            bool upper = part is AvatarMaskBodyPart.Body or AvatarMaskBodyPart.Head or AvatarMaskBodyPart.LeftArm or AvatarMaskBodyPart.RightArm
                              or AvatarMaskBodyPart.LeftFingers or AvatarMaskBodyPart.RightFingers or AvatarMaskBodyPart.LeftHandIK or AvatarMaskBodyPart.RightHandIK;
            mask.SetHumanoidBodyPartActive(part, upper);
        }
        EditorUtility.SetDirty(mask);
        return mask;
    }

    private static AnimatorController BuildController(AvatarMask mask, Dictionary<RunnerAnimationRole, int> slots, StringBuilder sb)
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(ControllerPath) != null) AssetDatabase.DeleteAsset(ControllerPath); // rebuilt from scratch
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter(RunnerAvatarAnimator.BaseSpeedParameter, AnimatorControllerParameterType.Float);
        controller.AddParameter(RunnerAvatarAnimator.UpperSpeedParameter, AnimatorControllerParameterType.Float);
        controller.parameters = controller.parameters.Select(p => { p.defaultFloat = 1f; return p; }).ToArray();

        var baseSm = controller.layers[0].stateMachine;
        var flourishPlaceholders = new List<AnimationClip>();
        int row = 0, total = 0;
        foreach (RunnerAnimationRole role in System.Enum.GetValues(typeof(RunnerAnimationRole)))
        {
            for (int i = 0; i < slots[role]; i++)
            {
                var placeholder = Placeholder($"{RunnerAvatarAnimator.PlaceholderPrefix}{role}_{i}");
                if (role == RunnerAnimationRole.Flourish) flourishPlaceholders.Add(placeholder);
                var state = baseSm.AddState($"{role}_{i}", new Vector3(260f + i * 220f, row * 60f, 0f));
                state.motion = placeholder;
                state.speedParameterActive = true;
                state.speedParameter = RunnerAvatarAnimator.BaseSpeedParameter;
                state.iKOnFeet = true;
                state.writeDefaultValues = true;
                if (role == RunnerAnimationRole.Idle && i == 0) baseSm.defaultState = state;
                total++;
            }
            row++;
        }

        controller.AddLayer(RunnerAvatarAnimator.UpperLayerName);
        var layers = controller.layers;
        layers[1].avatarMask = mask;
        layers[1].defaultWeight = 0f; // driven by RunnerAvatarAnimator
        layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
        controller.layers = layers;
        var upperSm = controller.layers[1].stateMachine;
        var empty = upperSm.AddState(RunnerAvatarAnimator.UpperEmptyState, new Vector3(260f, 0f, 0f));
        upperSm.defaultState = empty;
        for (int i = 0; i < flourishPlaceholders.Count; i++)
        {
            var state = upperSm.AddState($"Flourish_{i}", new Vector3(260f + i * 220f, 80f, 0f));
            state.motion = flourishPlaceholders[i];
            state.speedParameterActive = true;
            state.speedParameter = RunnerAvatarAnimator.UpperSpeedParameter;
        }
        EditorUtility.SetDirty(controller);
        sb.AppendLine($"  PASS  RunnerHumanoid.controller: {total} base states ({string.Join(", ", slots.Select(kv => $"{kv.Key}×{kv.Value}"))}), UpperBody layer Empty + {flourishPlaceholders.Count} Flourish (masked)");
        return controller;
    }

    private static AnimationClip Placeholder(string name)
    {
        string path = $"{PlaceholderDir}/{name}.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip { name = name }; AssetDatabase.CreateAsset(clip, path); }
        return clip;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
