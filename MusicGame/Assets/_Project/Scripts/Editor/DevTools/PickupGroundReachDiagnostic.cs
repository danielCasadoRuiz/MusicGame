using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Proves Runner pickup heights against the REAL ground and the REAL volumes, on real cached songs
/// (persistentDataPath/SongCache — the smallest few), for Normal and Hard pickup difficulty:
///   A. Ground reference: MusicWorldManager.SampleSurface == a raycast onto the actual ground
///      MeshCollider at the pickup's spot (the old analytic query sat ~1 m above it).
///   B. Every pickup (musical, off-track, LIFE/SPECIAL) sits above the ground under its whole footprint.
///   C. Running pickups: the player's real standing capsule (CharacterController: height
///      PlayerController.CapsuleHeight, radius 0.45, skin 0.04) resting on the collider at the pickup's
///      spot overlaps the pickup's real trigger (prefab instance, RingController.Setup/Activate,
///      Physics.OverlapCapsule) — and still does with the airborne gap a running player gets over crests
///      (kinematic run-up with the real gravity / grounded push). Reported per zone: flat, sloped
///      (≥ 6°), raised relief (ground ≥ 0.75 m above the path line) and bent path (turning ≥ 8° or
///      climbing/descending over ±5 m).
///   D. Jump pickups (elevated band) are NOT collectable standing, but are with a full jump; off-track
///      pickups lie beyond the track edge; the share needing a jump stays a minority; multipliers
///      x1 running / ≥ elevatedMinScoreMultiplier jump / offTrack x2.
///   E. Seed: same seed → identical distribution, different seed → different; a pending run keeps its
///      stored seed (RunnerPickupLayout.EnsureSeed / Apply / JSON round-trip, old save without seed gets
///      one exactly once). Does NOT touch the real save.
/// Batch: -executeMethod PickupGroundReachDiagnostic.RunFromCommandLine (Logs/PickupGroundReachDiagnostic.txt,
/// exit code 0 = all passed, 1 = failures). Menu: Tools > MusicGame > Runner > Pickup Ground Reach Diagnostic.
/// </summary>
public static class PickupGroundReachDiagnostic
{
    private const string ConfigPath = "Assets/_Project/Configs/Gameplay/MusicRunner/_MusicRunnerGameplayConfig.asset";
    private const string LogPath = "Logs/PickupGroundReachDiagnostic.txt";
    private const int MaxSongs = 4;
    private const float CapsuleRadius = 0.45f; // PlayerController.BuildController
    private const float SkinWidth = 0.04f;     // PlayerController.BuildController
    private static int _fails;

    [MenuItem("Tools/MusicGame/Runner/Pickup Ground Reach Diagnostic")]
    public static void RunFromMenu()
    {
        int fails = RunAll();
        Debug.Log($"[PickupGroundReachDiagnostic] {(fails == 0 ? "ALL PASSED" : fails + " FAILED")} — see {LogPath}");
    }

    public static void RunFromCommandLine()
    {
        int fails = RunAll();
        EditorApplication.Exit(fails == 0 ? 0 : 1);
    }

    private static int RunAll()
    {
        var sb = new StringBuilder("[PickupGroundReachDiagnostic]\n");
        _fails = 0;
        var savedDifficulty = RunnerDifficultyOverride.Current;
        try { Run(sb); } catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); _fails++; }
        finally
        {
            RunnerDifficultyOverride.Current = savedDifficulty;
            RunnerPickupLayout.Apply(null);
        }
        sb.Insert(0, _fails == 0 ? "ALL PASSED\n" : $"{_fails} FAILED\n");
        Directory.CreateDirectory("Logs");
        File.WriteAllText(LogPath, sb.ToString());
        return _fails;
    }

    private static void Check(StringBuilder sb, string name, bool ok, string detail = "")
    {
        sb.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : " — " + detail)}");
        if (!ok) _fails++;
    }

    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    private static void Run(StringBuilder sb)
    {
        var config = AssetDatabase.LoadAssetAtPath<MusicRunnerGameplayConfig>(ConfigPath);
        if (config == null) { Check(sb, "gameplay config loads", false, ConfigPath); return; }
        var core = config.core;
        float maxJump = core.jumpForce * core.jumpForce / (2f * -core.gravity);
        sb.AppendLine($"player capsule {PlayerController.CapsuleHeight:0.00} m × r {CapsuleRadius:0.00}, max jump {maxJump:0.00} m");
        foreach (RingType t in System.Enum.GetValues(typeof(RingType)))
            sb.AppendLine($"  {t,-7} trigger half height {GameplayTimeline.CollectibleTriggerHalfHeight(t, config):0.00} m, visual (pulse) half {GameplayTimeline.CollectibleMaxHalfHeight(t, config):0.00} m, standing reach {GameplayTimeline.StandingReach(t, config):0.00} m");

        string cacheDir = Path.Combine(Application.persistentDataPath, "SongCache");
        var files = Directory.Exists(cacheDir)
            ? Directory.GetFiles(cacheDir, "*.json").OrderBy(f => new FileInfo(f).Length).Take(MaxSongs).ToArray()
            : new string[0];
        Check(sb, "cached song profiles found", files.Length > 0, $"{files.Length} in {cacheDir}");

        bool seedTestsDone = false;
        foreach (var file in files)
        {
            var profile = JsonUtility.FromJson<SongProfileData>(File.ReadAllText(file)).ToProfile();
            sb.AppendLine($"\n== {Path.GetFileNameWithoutExtension(file)} ({profile.duration:0}s)");
            var go = new GameObject("PickupDiagWorld");
            var world = go.AddComponent<MusicWorldManager>();
            var instanceProp = typeof(MusicWorldManager).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var previousInstance = MusicWorldManager.Instance;
            var pickupInstances = new Dictionary<RingType, GameObject>();
            try
            {
                instanceProp.SetValue(null, world);
                world.Initialize(config);
                typeof(MusicWorldManager).GetMethod("BuildWorld", Priv).Invoke(world, new object[] { profile });
                foreach (var diff in new[] { RunnerDifficulty.Normal, RunnerDifficulty.Hard })
                {
                    RunnerDifficultyOverride.Current = diff;
                    CheckSong(sb, config, world, profile, diff, pickupInstances, maxJump);
                }
                if (!seedTestsDone) { SeedTests(sb, config, world.Path, profile); seedTestsDone = true; }
            }
            finally
            {
                foreach (var p in pickupInstances.Values) if (p != null) Object.DestroyImmediate(p);
                var ground = (GameObject)typeof(MusicWorldManager).GetField("_groundGO", Priv).GetValue(world);
                if (ground != null) Object.DestroyImmediate(ground);
                Object.DestroyImmediate(go);
                instanceProp.SetValue(null, previousInstance);
            }
        }
    }

    private sealed class Zone { public int n, standing, running; }

    private static void CheckSong(StringBuilder sb, MusicRunnerGameplayConfig config, MusicWorldManager world, SongProfile profile,
                                  RunnerDifficulty diff, Dictionary<RingType, GameObject> instances, float maxJump)
    {
        var core = config.core; var c = config.collectibles;
        var path = world.Path;
        var tl = GameplayTimeline.Generate(profile, config, path, 12345);
        var groundGO = (GameObject)typeof(MusicWorldManager).GetField("_groundGO", Priv).GetValue(world);
        var rebuild = typeof(MusicWorldManager).GetMethod("RebuildGroundWindow", Priv);
        float windowCenter = float.NaN;
        var range = PlayRangeResolver.Resolve(core, profile.duration);
        float playEnd = core.warmupTime + range.End;

        var zones = new Dictionary<string, Zone> { ["flat"] = new(), ["sloped"] = new(), ["raised relief"] = new(), ["bent path"] = new() };
        int running = 0, runStanding = 0, runRunning = 0, elevated = 0, elevStanding = 0, elevJumpable = 0, offTrack = 0, offBeyond = 0;
        int resources = 0, resStanding = 0, buried = 0, total = 0, multBad = 0;
        float worstRef = 0f, worstGap = 0f, weightAll = 0f, weightJump = 0f;
        string firstBuried = null, firstMissed = null;

        foreach (var e in tl.Events)
        {
            if (e.eventType != EventType.Ring || e.eventTime >= playEnd) continue;
            total++;
            float d = e.eventDistance;
            if (float.IsNaN(windowCenter) || Mathf.Abs(d - windowCenter) > 15f)
            {
                rebuild.Invoke(world, new object[] { d, true });
                windowCenter = d;
                Physics.SyncTransforms();
            }
            var groundCol = groundGO.GetComponent<MeshCollider>();
            var sample = path.GetSample(d);
            float half = sample.width * 0.5f;
            var surface = world.SampleSurface(d, e.lateralOffset);
            Vector3 pos = GameplayManager.PickupWorldPosition(e, config);

            // A. ground reference == the actual collider (on the track).
            if (Mathf.Abs(e.lateralOffset) < half - 0.1f && GroundY(groundCol, pos, out float hitY))
                worstRef = Mathf.Max(worstRef, Mathf.Abs(hitY - surface.position.y));

            // B. above the ground under the footprint.
            float vHalf = GameplayTimeline.CollectibleMaxHalfHeight(e.ringType, config);
            float foot = vHalf * 1.42f;
            float highest = float.NegativeInfinity;
            Vector3 fwd = Vector3.ProjectOnPlane(sample.tangent, Vector3.up).normalized, right = sample.right;
            foreach (var off in new[] { Vector3.zero, fwd * foot, -fwd * foot, right * foot, -right * foot })
                if (GroundY(groundCol, pos + off, out float gy)) highest = Mathf.Max(highest, gy);
            if (!float.IsNegativeInfinity(highest) && pos.y - vHalf < highest - 0.005f)
            {
                buried++;
                firstBuried ??= $"{e.ringType} t {e.eventTime:0.0}s bottom {pos.y - vHalf:0.00} < ground {highest:0.00}";
            }

            // Real trigger volume + real standing capsule at this spot.
            var pickup = Instance(instances, e, config);
            Vector3 flat = fwd.sqrMagnitude > 1e-6f ? fwd : Vector3.forward;
            pickup.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(flat, Vector3.up));
            Physics.SyncTransforms();
            var trigger = pickup.GetComponent<Collider>();
            float feet = RestingFeetY(groundCol, pos);
            bool standing = !float.IsNaN(feet) && Overlaps(trigger, pos, feet);
            float gap = RunUpGap(world, core, d, e.lateralOffset);
            worstGap = Mathf.Max(worstGap, gap);
            bool runningOk = !float.IsNaN(feet) && Overlaps(trigger, pos, feet + gap);
            bool jumpOk = !float.IsNaN(feet) && Overlaps(trigger, pos, feet + maxJump);

            float mult = e.scoreMultiplier <= 0f ? 1f : e.scoreMultiplier;
            var (bandLo, _) = GameplayTimeline.ElevatedBand(e.ringType, config);
            if (RingTypes.IsResource(e.ringType))
            {
                resources++; if (standing) resStanding++;
                continue;
            }
            weightAll += mult;
            if (e.isOffTrack)
            {
                offTrack++; weightJump += mult;
                if (Mathf.Abs(e.lateralOffset) > half) offBeyond++;
                if (Mathf.Abs(mult - Mathf.Max(1f, c.offTrackBonusScoreMultiplier)) > 1e-3f) multBad++;
            }
            else if (e.verticalOffset >= bandLo)
            {
                elevated++; weightJump += mult;
                if (standing) elevStanding++;
                if (jumpOk) elevJumpable++;
                if (mult < c.elevatedMinScoreMultiplier - 1e-3f) multBad++;
            }
            else
            {
                running++;
                if (standing) runStanding++;
                if (runningOk) runRunning++;
                else firstMissed ??= $"{e.ringType} t {e.eventTime:0.0}s centre {pos.y - surface.position.y:0.00} m above ground, run-up gap {gap:0.00} m";
                if (Mathf.Abs(mult - 1f) > 1e-3f) multBad++;
                foreach (var z in ZonesOf(world, path, d, e.lateralOffset, surface))
                {
                    var zone = zones[z]; zone.n++;
                    if (standing) zone.standing++;
                    if (runningOk) zone.running++;
                }
            }
        }

        string P(int a, int b) => b > 0 ? $"{100f * a / b:0.0}% ({a}/{b})" : "n/a";
        sb.AppendLine($"  [{diff}] {total} pickups: running {running}, jump {elevated}, off-track {offTrack}, LIFE/SPECIAL {resources}");
        Check(sb, $"[{diff}] ground reference = actual ground collider", worstRef <= 0.02f, $"worst |SampleSurface − raycast| {worstRef:0.000} m");
        Check(sb, $"[{diff}] every pickup above the ground under its footprint", buried == 0, buried == 0 ? "" : $"{buried} buried, first: {firstBuried}");
        Check(sb, $"[{diff}] running pickups overlap the STANDING capsule (no jump)", running > 0 && runStanding >= running * 0.99f, P(runStanding, running));
        Check(sb, $"[{diff}] running pickups collected RUNNING (crest air-gap incl.)", running > 0 && runRunning >= running * 0.95f,
              $"{P(runRunning, running)}, worst run-up gap {worstGap:0.00} m{(firstMissed != null ? ", first miss: " + firstMissed : "")}");
        foreach (var kv in zones)
            if (kv.Value.n >= 5)
                Check(sb, $"[{diff}]   zone {kv.Key}: collectable without jumping", kv.Value.running >= kv.Value.n * 0.95f,
                      $"standing {P(kv.Value.standing, kv.Value.n)}, running {P(kv.Value.running, kv.Value.n)}");
            else sb.AppendLine($"  INFO  [{diff}]   zone {kv.Key}: {kv.Value.n} running pickups (too few to judge)");
        Check(sb, $"[{diff}] jump pickups need a jump and are reachable with one", elevStanding == 0 && elevJumpable == elevated,
              $"{elevated} jump pickups, {elevStanding} collectable standing, {elevJumpable} reachable with a full jump");
        Check(sb, $"[{diff}] off-track pickups lie beyond the track edge", offBeyond == offTrack, P(offBeyond, offTrack));
        float jumpShare = (elevated + offTrack) / (float)Mathf.Max(1, running + elevated + offTrack);
        float cap = diff == RunnerDifficulty.Hard ? 0.25f : 0.15f;
        Check(sb, $"[{diff}] jump/lateral pickups are a deliberate minority (≤ {cap:P0})", jumpShare <= cap,
              $"{jumpShare:P1} of musical pickups; {weightJump / Mathf.Max(1f, weightAll):P1} of the multiplier-weighted points");
        Check(sb, $"[{diff}] multipliers: running x1, jump ≥ x{c.elevatedMinScoreMultiplier:0.00}, off-track x{c.offTrackBonusScoreMultiplier:0.0}", multBad == 0, $"{multBad} mismatches");
        sb.AppendLine($"  INFO  [{diff}] LIFE/SPECIAL: {resources}, collectable standing {resStanding} (designed as off-track jump pickups)");
    }

    private static IEnumerable<string> ZonesOf(MusicWorldManager world, MusicPath path, float d, float lateral, MusicWorldManager.SurfaceSample s)
    {
        float slope = Vector3.Angle(s.normal, Vector3.up);
        yield return slope < 6f ? "flat" : "sloped";
        if (s.height >= 0.75f) yield return "raised relief";
        var a = path.GetSample(d - 5f); var b = path.GetSample(d + 5f);
        float turn = Vector3.Angle(Vector3.ProjectOnPlane(a.tangent, Vector3.up), Vector3.ProjectOnPlane(b.tangent, Vector3.up));
        if (turn >= 8f || Mathf.Abs(a.tangent.y) > 0.1f || Mathf.Abs(b.tangent.y) > 0.1f) yield return "bent path";
    }

    private static GameObject Instance(Dictionary<RingType, GameObject> instances, in TimelineEvent e, MusicRunnerGameplayConfig config)
    {
        if (!instances.TryGetValue(e.ringType, out var go) || go == null)
        {
            go = Object.Instantiate(config.collectibles.ResolvePrefab(e.ringType));
            go.name = "Diag_" + e.ringType;
            instances[e.ringType] = go;
            var rc = go.GetComponent<RingController>();
            if (rc != null) rc.Setup(e.ringType);
        }
        var ring = go.GetComponent<RingController>();
        if (ring != null) ring.Activate(e, null); // real trigger size (ghost-scale compensated)
        return go;
    }

    // Ground height of the actual MeshCollider straight below `p`.
    private static bool GroundY(MeshCollider ground, Vector3 p, out float y)
    {
        y = 0f;
        if (ground == null) return false;
        if (!ground.Raycast(new Ray(new Vector3(p.x, p.y + 30f, p.z), Vector3.down), out var hit, 80f)) return false;
        y = hit.point.y;
        return true;
    }

    // Where a standing CharacterController capsule centred on `p`'s XZ comes to rest on the ground collider.
    private static float RestingFeetY(MeshCollider ground, Vector3 p)
    {
        float h = PlayerController.CapsuleHeight, r = CapsuleRadius;
        float startFeet = p.y + 20f;
        var p1 = new Vector3(p.x, startFeet + r, p.z);
        var p2 = new Vector3(p.x, startFeet + h - r, p.z);
        float best = float.PositiveInfinity;
        foreach (var hit in Physics.CapsuleCastAll(p1, p2, r, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore))
            if (hit.collider == ground && hit.distance > 0f) best = Mathf.Min(best, hit.distance);
        return float.IsPositiveInfinity(best) ? float.NaN : startFeet - best + SkinWidth;
    }

    private static bool Overlaps(Collider trigger, Vector3 at, float feetY)
    {
        float h = PlayerController.CapsuleHeight, r = CapsuleRadius;
        var p1 = new Vector3(at.x, feetY + r, at.z);
        var p2 = new Vector3(at.x, feetY + h - r, at.z);
        foreach (var col in Physics.OverlapCapsule(p1, p2, r, ~0, QueryTriggerInteraction.Collide))
            if (col == trigger) return true;
        return false;
    }

    // Kinematic run-up along the pickup's lateral line (PlayerController: gravity, −2 m/s grounded push,
    // playerSpeed): how far above the ground the feet are when the player reaches the pickup.
    private static float RunUpGap(MusicWorldManager world, MusicRunnerCoreConfig core, float d, float lateral)
    {
        const float dt = 1f / 60f;
        float speed = Mathf.Max(0.1f, core.playerSpeed);
        float x = d - 20f;
        float feet = world.SampleSurface(x, lateral).position.y, vy = -2f;
        while (x < d)
        {
            float step = Mathf.Min(speed * dt, d - x);
            x += step;
            float h = step / speed;
            vy += core.gravity * h;
            feet += vy * h;
            float g = world.SampleSurface(x, lateral).position.y;
            if (feet <= g) { feet = g; vy = -2f; }
        }
        return Mathf.Max(0f, feet - world.SampleSurface(d, lateral).position.y);
    }

    // ── E. Seed / persistence ───────────────────────────────────────────────────────

    private static void SeedTests(StringBuilder sb, MusicRunnerGameplayConfig config, MusicPath path, SongProfile profile)
    {
        sb.AppendLine("\n== pickup distribution seed");
        RunnerDifficultyOverride.Current = RunnerDifficulty.Normal;
        var a1 = GameplayTimeline.Generate(profile, config, path, 1111).Events;
        var a2 = GameplayTimeline.Generate(profile, config, path, 1111).Events;
        var b  = GameplayTimeline.Generate(profile, config, path, 2222).Events;
        Check(sb, "same seed → identical distribution", Same(a1, a2), $"{a1.Length} events");
        int diffCount = Differences(a1, b);
        Check(sb, "different seed → different distribution", diffCount > 0, $"{a1.Length} vs {b.Length} events, {diffCount} differ");

        // New pending run: gets a seed + version once, keeps it afterwards; the runner uses it.
        var run = new PendingRunData { songId = "song_diag" };
        bool first = RunnerPickupLayout.EnsureSeed(run);
        int seed = run.pickupSeed;
        bool second = RunnerPickupLayout.EnsureSeed(run);
        Check(sb, "new pending run gets a non-zero seed + generator version exactly once",
              first && !second && seed != 0 && run.pickupSeed == seed && run.pickupLayoutVersion == RunnerPickupLayout.GeneratorVersion);
        var other = new PendingRunData { songId = "song_diag" };
        RunnerPickupLayout.EnsureSeed(other);
        Check(sb, "a NEW pending run (new match) draws a different seed", other.pickupSeed != seed, $"{seed} vs {other.pickupSeed}");

        // Persistence: the seed survives the save JSON; an old save without it gets one once.
        var restored = JsonUtility.FromJson<PendingRunData>(JsonUtility.ToJson(run));
        Check(sb, "seed survives the save JSON round-trip", restored.pickupSeed == seed && restored.pickupLayoutVersion == run.pickupLayoutVersion);
        var old = JsonUtility.FromJson<PendingRunData>("{\"songId\":\"song_004\",\"opponentId\":\"brahms\",\"opponentLevel\":4,\"bossStage\":-1}");
        bool migrated = RunnerPickupLayout.EnsureSeed(old);
        int oldSeed = old.pickupSeed;
        bool again = RunnerPickupLayout.EnsureSeed(JsonUtility.FromJson<PendingRunData>(JsonUtility.ToJson(old)));
        Check(sb, "old save pending run (no seed) gets one once, then keeps it", migrated && oldSeed != 0 && !again);

        // Resume path: Apply(stored run) → the Runner generates with the STORED seed.
        RunnerPickupLayout.Apply(restored);
        int used = RunnerPickupLayout.SeedFor(config.collectibles);
        var resumed = GameplayTimeline.Generate(profile, config, path, used).Events;
        var original = GameplayTimeline.Generate(profile, config, path, seed).Events;
        Check(sb, "resumed match uses the stored seed → same distribution", used == seed && Same(resumed, original), $"seed {used}");
        RunnerPickupLayout.Apply(null);
        Check(sb, "no assignment → config collectibleSeed", RunnerPickupLayout.SeedFor(config.collectibles) == config.collectibles.collectibleSeed);
    }

    private static bool Same(TimelineEvent[] a, TimelineEvent[] b) => a.Length == b.Length && Differences(a, b) == 0;

    private static int Differences(TimelineEvent[] a, TimelineEvent[] b)
    {
        int n = Mathf.Max(a.Length, b.Length), diff = 0;
        for (int i = 0; i < n; i++)
        {
            if (i >= a.Length || i >= b.Length) { diff++; continue; }
            var x = a[i]; var y = b[i];
            if (x.ringType != y.ringType || x.eventTime != y.eventTime || x.lateralOffset != y.lateralOffset ||
                x.verticalOffset != y.verticalOffset || x.isOffTrack != y.isOffTrack || x.scoreMultiplier != y.scoreMultiplier) diff++;
        }
        return diff;
    }
}
