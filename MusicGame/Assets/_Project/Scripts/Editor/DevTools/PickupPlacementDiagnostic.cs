using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Light edit-mode check of difficult-pickup placement on real cached songs: builds the real path +
/// GameplayTimeline and verifies Life/Special sit outside the racing line, within physical reach
/// (beyond-edge air reach, from-centre reaction time), spaced from other difficult pickups and never
/// on opposite sides back to back; off-track score bonuses keep their original edge range.
/// Batch: -executeMethod PickupPlacementDiagnostic.RunFromCommandLine (Logs/PickupPlacementDiagnostic.txt).
/// </summary>
public static class PickupPlacementDiagnostic
{
    private const string ConfigPath = "Assets/_Project/Configs/Gameplay/MusicRunner/_MusicRunnerGameplayConfig.asset";
    private static int _fails;

    public static void RunFromCommandLine()
    {
        var sb = new StringBuilder("[PickupPlacementDiagnostic]\n");
        _fails = 0;
        try { Run(sb); } catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); _fails++; }
        sb.Insert(0, _fails == 0 ? "ALL PASSED\n" : $"{_fails} FAILED\n");
        File.WriteAllText("Logs/PickupPlacementDiagnostic.txt", sb.ToString());
        EditorApplication.Exit(0);
    }

    private static void Check(StringBuilder sb, string name, bool ok, string detail = "")
    {
        sb.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : " — " + detail)}");
        if (!ok) _fails++;
    }

    private static void Run(StringBuilder sb)
    {
        var config = AssetDatabase.LoadAssetAtPath<MusicRunnerGameplayConfig>(ConfigPath);
        var core = config.core; var c = config.collectibles;
        float airTime = 2f * core.jumpForce / -core.gravity;
        float maxJump = core.jumpForce * core.jumpForce / (2f * -core.gravity);
        float maxBeyond = core.allowAirControl ? core.EffectiveStrafeSpeed * airTime * 0.5f * c.difficultPickupAirReach : 0f;
        float maxFromCenter = core.EffectiveStrafeSpeed * (c.spawnLookAhead - c.minReactionTime);
        sb.AppendLine($"limits: air time {airTime:0.00}s, max jump {maxJump:0.00} m, beyond-edge reach {maxBeyond:0.00} m, from-centre reach {maxFromCenter:0.00} m");

        string cacheDir = Path.Combine(Application.persistentDataPath, "SongCache");
        foreach (var pattern in new[] { "BillieJean*.json", "Jamiroquai*.json", "Daft Punk*.json" })
        {
            string file = Directory.GetFiles(cacheDir, pattern).FirstOrDefault();
            if (file == null) continue;
            var profile = JsonUtility.FromJson<SongProfileData>(File.ReadAllText(file)).ToProfile();
            var path = new SnakeWayWorldGenerator().Generate(profile, config, new Vector3(0f, config.levelGeneration.pathBaseHeight, 0f));
            var tl = GameplayTimeline.Generate(profile, config, path);
            sb.AppendLine($"== {Path.GetFileNameWithoutExtension(file)}: {tl.Events.Length} events");

            var difficult = tl.Events.Where(e => e.isOffTrack || RingTypes.IsResource(e.ringType)).OrderBy(e => e.eventTime).ToArray();
            foreach (var e in tl.Events.Where(e => RingTypes.IsResource(e.ringType)))
            {
                float half = path.GetWidth(e.eventDistance) * 0.5f;
                float edge = Mathf.Abs(e.lateralOffset) - half;
                var rares = tl.Events.Where(d => RingTypes.IsResource(d.ringType) && d.eventTime != e.eventTime).ToArray();
                float gap = rares.Length > 0 ? rares.Min(d => Mathf.Abs(d.eventTime - e.eventTime)) : 99f;
                var near = difficult.Where(d => d.isOffTrack && Mathf.Abs(d.eventTime - e.eventTime) < c.difficultPickupMinSpacing).ToArray();
                bool sameSide = near.All(d => Mathf.Sign(d.lateralOffset) == Mathf.Sign(e.lateralOffset));
                float reachTime = Mathf.Abs(e.lateralOffset) / core.EffectiveStrafeSpeed + c.minReactionTime;
                sb.AppendLine($"    {e.ringType,-7} t {e.eventTime,6:0.0}s  lateral {e.lateralOffset,6:+0.00;-0.00} (half-width {half:0.00}, beyond edge {edge:+0.00;-0.00} m)  " +
                              $"height {e.verticalOffset:0.00}/{maxJump:0.00} m  nearest Life/Special {gap:0.0}s, {near.Length} off-track bonus(es) within {c.difficultPickupMinSpacing:0}s all same side {sameSide}  reach {reachTime:0.00}s/{c.spawnLookAhead:0.00}s");
                Check(sb, $"{e.ringType} @{e.eventTime:0.0}s outside the racing line, reachable, spaced, consistent side",
                      edge > 0f && edge <= maxBeyond + 1e-3f && reachTime <= c.spawnLookAhead + 1e-3f &&
                      gap >= c.difficultPickupMinSpacing - 1e-3f && sameSide && e.verticalOffset <= maxJump + 1e-3f);
            }

            var offTrack = tl.Events.Where(e => e.isOffTrack).ToArray();
            if (offTrack.Length > 0)
            {
                var edges = offTrack.Select(e => Mathf.Abs(e.lateralOffset) - path.GetWidth(e.eventDistance) * 0.5f).ToArray();
                Check(sb, $"{offTrack.Length} off-track score bonuses keep their original edge range [{c.collectibleRadius:0.00}, {c.collectibleRadius + c.offTrackBonusMaxOffset:0.00}] m",
                      edges.All(x => x >= c.collectibleRadius - 1e-3f && x <= c.collectibleRadius + c.offTrackBonusMaxOffset + 1e-3f),
                      $"observed {edges.Min():0.00}..{edges.Max():0.00}");
            }
        }
    }
}
