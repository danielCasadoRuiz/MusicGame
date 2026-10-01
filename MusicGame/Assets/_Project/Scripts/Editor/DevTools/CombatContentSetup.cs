using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Tools > MusicGame > Combat — builds the combat architecture content from what already exists
/// (safe to re-run; it reuses / updates assets in place):
///   - the ONE shared logical controller FighterCombat.controller (one state per CombatRole with a
///     placeholder clip "FighterCombat_&lt;Role&gt;", speed parameter RoleSpeed, Foot IK);
///   - roles/scaling/costs on the existing FightMoveDefinitions + the missing provisional moves
///     (Heavy, Block, Dodge, Special_Test, Triple/Quad technique tests, Taunt) in FightMoveSet.asset;
///   - Classical_Default_AnimationSet (roles → processed Rokoko in-place clips, referenced);
///   - profiles: Classical_Default, Mozart, Player_Temp (debug); Mozart tiers → Mozart profile, the
///     other composers → Classical_Default; FightFlowConfig/FightArenaConfig references;
///   - a lightweight validation.
/// </summary>
public static class CombatContentSetup
{
    private const string Root         = "Assets/_Project/Configs/Fight/Combat";
    private const string AnimatorDir  = Root + "/Animator";
    private const string PlaceholdDir = AnimatorDir + "/Placeholders";
    private const string MovesDir     = Root + "/Moves";
    private const string SetsDir      = Root + "/AnimationSets";
    private const string ProfilesDir  = Root + "/Profiles";
    public  const string ControllerPath = AnimatorDir + "/FighterCombat.controller";
    public  const string AnimationSetPath = SetsDir + "/Classical_Default_AnimationSet.asset";
    public  const string DefaultProfilePath = ProfilesDir + "/CombatProfile_Classical_Default.asset";
    public  const string MozartProfilePath  = ProfilesDir + "/CombatProfile_Mozart.asset";
    public  const string PlayerProfilePath  = "Assets/_Project/Configs/Fight/Debug/CombatProfile_Player_Temp.asset";
    private const string MoveSetPath  = "Assets/_Project/Configs/Fight/FightMoveSet.asset";
    private const string FlowPath     = "Assets/_Project/Configs/Fight/FightFlowConfig.asset";
    private const string ArenaPath    = "Assets/_Project/Configs/Fight/FightArenaConfig.asset";
    private const string PlayerRecipe = "Assets/_Project/Avatar/MakeHuman/Content/Recipes/Avatar_MakeHuman_TestFemale.asset";

    private const string Shadow = "Fight_ShadowBoxing_mixamo", Loser = "Fight_KnockOut_Loser_mixamo";

    /// <summary>Tighter windows (reviewed takes) so a clip fits a move's gameplay duration without
    /// heavy speed-up. Added to the combat library once; imported by CombatAnimationImporter.</summary>
    private static readonly (string source, string name, float start, float end, CombatAnimationCategory category, bool loop, string notes)[] CombatSegments =
    {
        (Shadow, "Jab",     8.15f,  8.75f, CombatAnimationCategory.AttackPunch, false, "Single left jab (extended ~0.2 s in) — LightAttack clip."),
        (Shadow, "Hook",    19.45f, 20.5f, CombatAnimationCategory.AttackPunch, false, "Rotating right hook (landing ~0.45 s in) — HeavyAttack clip."),
        (Loser,  "HitSnap", 9.0f,   9.8f,  CombatAnimationCategory.HitReaction, false, "Short head-snap reaction — HitReaction clip."),
        (Loser,  "Downed",  13.0f,  16.0f, CombatAnimationCategory.Knockdown,   true,  "Lying on the back (between the fall and the get-up) — Downed loop."),
    };

    [MenuItem("Tools/MusicGame/Combat/Setup Combat Content")]
    public static void SetupMenu() => Debug.Log(Setup());

    public static void SetupFromCommandLine()
    {
        File.WriteAllText("Logs/CombatContentSetup.txt", Setup() + "\n" + Validate(out bool ok));
        EditorApplication.Exit(ok ? 0 : 1);
    }

    public static string Setup()
    {
        var report = new StringBuilder("[CombatContentSetup]\n");
        foreach (var d in new[] { PlaceholdDir, MovesDir, SetsDir, ProfilesDir }) EnsureFolder(d);

        // 1. Library segments (import only if something new was defined).
        var library = AssetDatabase.LoadAssetAtPath<CombatAnimationLibrarySO>(CombatAnimationImporter.LibraryPath);
        bool added = false;
        foreach (var seg in CombatSegments)
            if (!library.segments.Any(s => s.sourceEntry == seg.source && s.name == seg.name))
            {
                library.segments.Add(new CombatSegmentDefinition { sourceEntry = seg.source, name = seg.name, start = seg.start, end = seg.end,
                    category = seg.category, loop = seg.loop, keepFacing = true, review = CombatReviewStatus.Approved,
                    notes = "Reviewed 2026-10-01 (window renders). " + seg.notes });
                added = true;
            }
        if (added)
        {
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            report.AppendLine("  combat library: new segments -> reimport\n" + CombatAnimationImporter.ImportAll(out _).Split('\n').Count(l => l.Contains("__")) + " segment entries");
            library = AssetDatabase.LoadAssetAtPath<CombatAnimationLibrarySO>(CombatAnimationImporter.LibraryPath);
        }

        // 2. Shared controller.
        var controller = BuildController(report);

        // 3. Moves.
        var moveSet = AssetDatabase.LoadAssetAtPath<FightMoveSetSO>(MoveSetPath);
        ConfigureExistingMoves(moveSet);
        var created = new List<FightMoveDefinition>
        {
            Move("Move_HeavyPunch", "heavy_punch", "Heavy Punch", CombatRole.HeavyAttack, FighterBuildStat.PunchPower, 0.25f, 0.12f, 0.45f, 16f, 0.45f, 1.5f),
            Move("Move_Block", "block", "Block", CombatRole.Block, FighterBuildStat.None, 0.02f, 0.7f, 0.12f, 0f, 0f, 0f, hits: false),
            Move("Move_Dodge", "dodge", "Dodge (provisional anim)", CombatRole.Dodge, FighterBuildStat.Agility, 0.05f, 0.4f, 0.25f, 0f, 0f, 0f, hits: false,
                 invulnStart: 0.05f, invulnDuration: 0.4f),
            Move("Move_Special_Test", "special_test", "Special (TEST)", CombatRole.Special, FighterBuildStat.ImpactPower, 0.3f, 0.15f, 0.6f, 25f, 0.6f, 2.2f,
                 cost: CombatResourceType.Special, knockdown: true),
            Move("Move_TripleTechnique_Test", "triple_technique_test", "Triple Technique (TEST)", CombatRole.HeavyAttack, FighterBuildStat.PunchPower, 0.2f, 0.12f, 0.4f, 20f, 0.5f, 1.8f,
                 cost: CombatResourceType.TripleCombo),
            Move("Move_QuadTechnique_Test", "quad_technique_test", "Quad Technique (TEST)", CombatRole.Kick, FighterBuildStat.KickPower, 0.22f, 0.14f, 0.45f, 22f, 0.5f, 2f,
                 cost: CombatResourceType.QuadCombo, knockdown: true),
            Move("Move_Taunt", "taunt", "Taunt", CombatRole.Taunt, FighterBuildStat.None, 0f, 0.1f, 1.8f, 0f, 0f, 0f, hits: false),
        };
        var list = new List<FightMoveDefinition>(created);
        foreach (var m in moveSet.moves) if (m != null && !list.Contains(m)) list.Add(m);
        moveSet.moves = list.ToArray(); // role moves first, so FighterCombatProfileSO.GetMove(role) finds them
        EditorUtility.SetDirty(moveSet);

        // 4. Animation set (references the processed in-place clips).
        var set = LoadOrCreate<FighterAnimationSetSO>(AnimationSetPath);
        AnimationClip Clip(string entry)
        {
            var e = library.Find(entry);
            if (e == null || e.inPlaceClip == null) report.AppendLine($"  WARNING: library entry '{entry}' missing");
            return e?.inPlaceClip;
        }
        set.clips = new[]
        {
            Role(CombatRole.CombatIdle,  Clip("Fight_Boxing_mixamo__GuardIdle"), false, "Boxing guard, looped"),
            Role(CombatRole.LightAttack, Clip(Shadow + "__Jab"), false, "ShadowBoxing single jab"),
            Role(CombatRole.HeavyAttack, Clip(Shadow + "__Hook"), false, "ShadowBoxing rotating hook"),
            Role(CombatRole.Kick,        Clip("Fight_BigFrontKick_mixamo__FrontKick"), false, "BigFrontKick front kick"),
            Role(CombatRole.Block,       Clip(Shadow + "__CoverBlock"), false, "ShadowBoxing high cover"),
            Role(CombatRole.Dodge,       Clip(Shadow + "__DuckWeave"), true, "PROVISIONAL — covered bob/weave, no clean dodge exists in the packs"),
            Role(CombatRole.HitReaction, Clip(Loser + "__HitSnap"), false, "KnockOut_Loser head snap"),
            Role(CombatRole.Knockdown,   Clip(Loser + "__KnockedDown"), false, "KnockOut_Loser fall (source ground glitch at impact)"),
            Role(CombatRole.Downed,      Clip(Loser + "__Downed"), false, "KnockOut_Loser lying on the back"),
            Role(CombatRole.GetUp,       Clip(Loser + "__GetUp"), false, "KnockOut_Loser get up"),
            Role(CombatRole.Special,     Clip("Fight_RoundHouseKick_mixamo__SpinningRoundhouse"), true, "PROVISIONAL — spinning roundhouse stands in for a real special"),
            Role(CombatRole.Taunt,       Clip("MartialArts_BattleTaunts_WithSword_MIXAMO_769"), true, "PROVISIONAL — full taunt take (start only plays)"),
            Role(CombatRole.Victory,     Clip("MartialArts_BattleTaunts_WithSword_MIXAMO_769__FistRaised"), false, "Fist raised"),
            Role(CombatRole.Defeat,      Clip(Loser + "__KnockedDown"), true, "PROVISIONAL — reuses the knockdown fall"),
        };
        EditorUtility.SetDirty(set);

        // 5. Profiles.
        FighterCombatProfileSO Profile(string path)
        {
            var p = LoadOrCreate<FighterCombatProfileSO>(path);
            p.moveSet = moveSet;
            p.animationSet = set;
            EditorUtility.SetDirty(p);
            return p;
        }
        var classical = Profile(DefaultProfilePath);
        var mozart    = Profile(MozartProfilePath);
        var player    = Profile(PlayerProfilePath);

        // 6. Tier references.
        var roster = AssetDatabase.LoadAssetAtPath<OpponentRosterSO>(OpponentRosterSetup.RosterPath);
        foreach (var o in roster.opponents)
        {
            if (o == null) continue;
            var profile = o.id == "mozart" ? mozart : classical;
            foreach (var tier in o.levels) if (tier != null) tier.combatProfile = profile;
            if (o.defaultConfig != null) o.defaultConfig.combatProfile = profile;
            EditorUtility.SetDirty(o);
        }
        var flow = AssetDatabase.LoadAssetAtPath<FightFlowConfig>(FlowPath);
        flow.combatAnimatorController      = controller;
        flow.defaultPlayerCombatProfile    = player;
        flow.defaultOpponentCombatProfile  = classical;
        EditorUtility.SetDirty(flow);
        var arena = AssetDatabase.LoadAssetAtPath<FightArenaConfig>(ArenaPath);
        arena.playerAvatarRecipe = AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>(PlayerRecipe);
        EditorUtility.SetDirty(arena);

        AssetDatabase.SaveAssets();
        report.AppendLine($"  controller {ControllerPath}; {created.Count} provisional moves; animation set {set.clips.Length} roles; profiles Classical_Default / Mozart / Player_Temp");
        report.AppendLine("  Mozart tiers -> CombatProfile_Mozart, other composers -> CombatProfile_Classical_Default");
        return report.ToString();
    }

    private static FighterAnimationSetSO.RoleClip Role(CombatRole role, AnimationClip clip, bool provisional, string note) =>
        new() { role = role, clip = clip, provisional = provisional, note = note };

    private static void ConfigureExistingMoves(FightMoveSetSO set)
    {
        void Set(FightMoveDefinition m, CombatRole role, FighterBuildStat stat, float? s = null, float? a = null, float? r = null)
        {
            if (m == null) return;
            m.role = role;
            m.scalingStat = stat;
            if (s.HasValue) { m.startupDuration = s.Value; m.activeDuration = a.Value; m.recoveryDuration = r.Value; }
            EditorUtility.SetDirty(m);
        }
        // Normals: gameplay timing set to fit the animation (placeholder balance).
        Set(set.normalPunch, CombatRole.LightAttack, FighterBuildStat.PunchPower, 0.12f, 0.1f, 0.33f);
        Set(set.normalKick,  CombatRole.Kick,        FighterBuildStat.KickPower,  0.2f,  0.12f, 0.4f);
        Set(set.airNormalPunch, CombatRole.LightAttack, FighterBuildStat.PunchPower);
        Set(set.airNormalKick,  CombatRole.Kick,        FighterBuildStat.KickPower);
        Set(set.runNormalPunch, CombatRole.HeavyAttack, FighterBuildStat.PunchPower);
        foreach (var m in set.moves)
        {
            if (m == null || m.name.EndsWith("_Test") || m.role != CombatRole.CombatIdle) continue;
            if (m.name.Contains("Dash") || m.name.Contains("Backdash")) continue; // movement only
            bool kick = m.name.EndsWith("B") || m.name.Contains("DownB");
            Set(m, m.name.Contains("Special") ? CombatRole.HeavyAttack : (kick ? CombatRole.Kick : CombatRole.HeavyAttack),
                kick ? FighterBuildStat.KickPower : FighterBuildStat.PunchPower);
        }
    }

    private static FightMoveDefinition Move(string file, string id, string debugName, CombatRole role, FighterBuildStat stat,
        float startup, float active, float recovery, float damage, float hitStun, float knockback, bool hits = true,
        CombatResourceType cost = CombatResourceType.None, bool knockdown = false, float invulnStart = 0f, float invulnDuration = 0f)
    {
        var m = LoadOrCreate<FightMoveDefinition>($"{MovesDir}/{file}.asset");
        m.id = id; m.debugName = debugName; m.animationState = role.ToString();
        m.moveType = role == CombatRole.Special ? FightMoveType.Special : FightMoveType.Normal;
        m.role = role; m.scalingStat = stat; m.resourceCost = cost; m.resourceAmount = cost == CombatResourceType.None ? 0 : 1;
        m.knockdownOnHit = knockdown; m.invulnerableStart = invulnStart; m.invulnerableDuration = invulnDuration;
        m.startupDuration = startup; m.activeDuration = active; m.recoveryDuration = recovery;
        m.movementLocked = true; m.lockFacingDuringMove = true;
        m.hits = hits ? new[] { new FightHitDefinition { localOffset = new Vector3(0.8f, 1f, 0f), size = new Vector3(0.7f, 0.6f, 0.6f),
                                                         baseDamage = damage, baseHitStun = hitStun, baseKnockback = knockback } }
                      : System.Array.Empty<FightHitDefinition>();
        EditorUtility.SetDirty(m);
        return m;
    }

    private static RuntimeAnimatorController BuildController(StringBuilder report)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                         ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        if (!controller.parameters.Any(p => p.name == AnimatorFighterAnimationDriver.SpeedParameter))
            controller.AddParameter(new AnimatorControllerParameter { name = AnimatorFighterAnimationDriver.SpeedParameter, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

        var machine = controller.layers[0].stateMachine;
        foreach (var st in machine.states.ToArray()) machine.RemoveState(st.state);
        AnimatorState idle = null;
        int i = 0;
        foreach (CombatRole role in System.Enum.GetValues(typeof(CombatRole)))
        {
            string clipPath = $"{PlaceholdDir}/{AnimatorFighterAnimationDriver.PlaceholderPrefix}{role}.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null) { clip = new AnimationClip { name = AnimatorFighterAnimationDriver.PlaceholderPrefix + role }; AssetDatabase.CreateAsset(clip, clipPath); }
            var state = machine.AddState(role.ToString(), new Vector3(300f + (i % 4) * 220f, (i / 4) * 70f, 0f));
            state.motion = clip;
            state.writeDefaultValues = true;
            state.iKOnFeet = true;
            state.speedParameterActive = true;
            state.speedParameter = AnimatorFighterAnimationDriver.SpeedParameter;
            if (role == CombatRole.CombatIdle) idle = state;
            i++;
        }
        machine.defaultState = idle;
        EditorUtility.SetDirty(controller);
        report.AppendLine($"  shared controller: {i} logical states (transitions are code-driven crossfades)");
        return controller;
    }

    // ── Validation (lightweight) ──────────────────────────────────────────────

    [MenuItem("Tools/MusicGame/Combat/Validate Combat Content")]
    public static void ValidateMenu() => Debug.Log(Validate(out _));

    public static string Validate(out bool ok)
    {
        var sb = new StringBuilder("[CombatContentSetup] validation\n");
        int fails = 0;
        void Check(string name, bool pass, string detail = "") { sb.AppendLine($"  {(pass ? "PASS" : "FAIL")}  {name}{(pass ? "" : " — " + detail)}"); if (!pass) fails++; }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        Check("shared FighterCombat controller exists with a state per CombatRole", controller != null &&
              System.Enum.GetValues(typeof(CombatRole)).Cast<CombatRole>().All(r => controller.layers[0].stateMachine.states.Any(s => s.state.name == r.ToString())));

        var profiles = AssetDatabase.FindAssets("t:FighterCombatProfileSO").Select(g => AssetDatabase.LoadAssetAtPath<FighterCombatProfileSO>(AssetDatabase.GUIDToAssetPath(g))).ToList();
        foreach (var p in profiles)
        {
            var moves = new List<FightMoveDefinition>();
            if (p.moveSet != null) { moves.Add(p.moveSet.normalPunch); moves.Add(p.moveSet.normalKick); moves.AddRange(p.moveSet.moves); }
            var real = moves.Where(m => m != null).ToList();
            Check($"{p.name}: has moveSet + animationSet", p.moveSet != null && p.animationSet != null);
            Check($"{p.name}: no null move references", p.moveSet == null || p.moveSet.moves.All(m => m != null));
            var dup = real.GroupBy(m => m.id).Where(g => g.Select(x => x).Distinct().Count() > 1).Select(g => g.Key).ToList();
            Check($"{p.name}: unique move ids", dup.Count == 0, string.Join(", ", dup));
            if (p.animationSet != null)
            {
                var missing = real.Select(m => m.role).Distinct().Where(r => r != CombatRole.CombatIdle && p.animationSet.Get(r) == null).ToList();
                foreach (var r in new[] { CombatRole.CombatIdle, CombatRole.HitReaction, CombatRole.Knockdown, CombatRole.Downed, CombatRole.GetUp, CombatRole.Victory, CombatRole.Defeat })
                    if (p.animationSet.Get(r) == null) missing.Add(r);
                Check($"{p.name}: every used/required role has a clip", missing.Count == 0, string.Join(", ", missing.Distinct()));
                var costly = real.Where(m => m.resourceCost != CombatResourceType.None && p.animationSet.Get(m.role) == null).Select(m => m.id).ToList();
                Check($"{p.name}: resource-cost moves have animation", costly.Count == 0, string.Join(", ", costly));
            }
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MakeHumanBodyBuilder.PrefabPath);
        var animator = prefab != null ? prefab.GetComponent<Animator>() : null;
        Check("fighter avatar has a Humanoid Animator", animator != null && animator.avatar != null && animator.avatar.isHuman);

        var roster = AssetDatabase.LoadAssetAtPath<OpponentRosterSO>(OpponentRosterSetup.RosterPath);
        var noProfile = roster.opponents.Where(o => o != null).SelectMany(o => o.levels.Where(l => l.combatProfile == null).Select(l => $"{o.id} T{l.level}")).ToList();
        Check("every composer tier references a combat profile", noProfile.Count == 0, string.Join(", ", noProfile));
        ok = fails == 0;
        sb.Insert(0, ok ? "ALL PASSED\n" : $"{fails} FAILED\n");
        return sb.ToString();
    }

    private static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
