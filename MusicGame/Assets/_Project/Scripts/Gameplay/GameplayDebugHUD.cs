using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// F1 toggle overlay. Shows MusicClock, path, player, camera, pools, events, fall/respawn.
/// Attach to any persistent GO. Auto-finds systems via singletons.
/// Gizmos (path, checkpoints, events) drawn in OnDrawGizmos.
/// </summary>
public class GameplayDebugHUD : MonoBehaviour
{
    [SerializeField] private bool showOnStart  = false;
    [SerializeField] private bool showGizmos   = true;

    private bool            _visible;
    private GameplayManager _manager;
    private PlayerController _player;
    private CameraFollow    _camera;
    private MusicEnvironmentController _environment;
    private AudioSystemBootstrapper _audioBootstrapper;
    private SongProfile             _profile;
    private GUIStyle        _boxStyle;
    private GUIStyle        _labelStyle;
    private GUIStyle        _headerStyle;

    // Rolling log of recently-collected Micro pickups — lets you watch, live, which type/
    // confidence was actually chosen and how its timing landed (F1 → PUM → line).
    private const int MaxLogLines = 8;
    private readonly List<string> _impactLog = new();
    private readonly List<string> _macroLog  = new();
    private System.Action<RingCollectedEvent>      _onRingDebug;
    private System.Action<MacroEventOccurredEvent> _onMacroDebug;
    private System.Action<SongProfileReadyEvent>   _onProfileDebug;

    private void Start()
    {
        _visible     = showOnStart;
        _manager     = FindFirstObjectByType<GameplayManager>();
        _player      = FindFirstObjectByType<PlayerController>();
        _camera      = FindFirstObjectByType<CameraFollow>();
        _environment = FindFirstObjectByType<MusicEnvironmentController>();
        _audioBootstrapper = FindFirstObjectByType<AudioSystemBootstrapper>();
    }

    private void OnEnable()
    {
        _onProfileDebug = e => _profile = e.Profile;
        EventBus.Subscribe(_onProfileDebug);
        _onRingDebug = e =>
        {
            string line = $"{e.Type,-7} str={e.Strength:F2} conf={e.Confidence:F2} [{e.Contributors}]  " +
                          $"exp={e.ExpectedTime:F2}s act={e.ActualTime:F2}s err={e.TimingError:F3}s";
            _impactLog.Insert(0, line);
            if (_impactLog.Count > MaxLogLines) _impactLog.RemoveAt(_impactLog.Count - 1);
        };
        _onMacroDebug = e =>
        {
            var clk = MusicClock.Instance;
            string line = $"{e.Type,-12} str={e.Strength:F2}  climax={e.IsClimax}  " +
                          $"t={(clk != null ? clk.SongTime : 0f):F2}s";
            _macroLog.Insert(0, line);
            if (_macroLog.Count > MaxLogLines) _macroLog.RemoveAt(_macroLog.Count - 1);
        };
        EventBus.Subscribe(_onRingDebug);
        EventBus.Subscribe(_onMacroDebug);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onRingDebug);
        EventBus.Unsubscribe(_onMacroDebug);
        EventBus.Unsubscribe(_onProfileDebug);
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            _visible = !_visible;
    }

    private void OnGUI()
    {
        if (!_visible) return;
        EnsureStyles();

        const float X = 8f;
        const float W = 430f; // wide enough for the "MUSICAL IMPACTS" contributor lines
        float y = 8f;

        y = DrawSection(X, y, W, "MUSIC CLOCK",  DrawClock);
        y = DrawSection(X, y, W, "PATH",          DrawPath);
        y = DrawSection(X, y, W, "PLAYER",        DrawPlayer);
        y = DrawSection(X, y, W, "CAMERA",        DrawCamera);
        y = DrawSection(X, y, W, "NEXT EVENT",    DrawNextEvent);
        y = DrawSection(X, y, W, "MICRO PICKUPS (recent)", DrawImpactLog);
        y = DrawSection(X, y, W, "MACRO EVENTS (recent)",  DrawMacroLog);
        y = DrawSection(X, y, W, "MUSIC ENVIRONMENT",      DrawEnvironment);
        y = DrawSection(X, y, W, "GAMEPLAY EVENTS", DrawEventStats);
        y = DrawSection(X, y, W, "PERFORMANCE BY TYPE", DrawPerformance);
        y = DrawSection(X, y, W, "SYNC AUDIT",     DrawSyncAudit);
        y = DrawSection(X, y, W, "POOLS",         DrawPools);
        y = DrawSection(X, y, W, "CHECKPOINT",    DrawCheckpoint);
        y = DrawSection(X, y, W, "FALL / RESPAWN", DrawFall);
        y = DrawSection(X, y, W, "SEMANTIC TAGS", DrawSemanticTags);
        DrawSection(X, y, W, "WORLD MESH", DrawWorldMesh);
    }

    // ── Section renderer ───────────────────────────────────────────────────────

    private delegate void ContentDrawer(float x, ref float y, float w);

    private float DrawSection(float x, float y, float w, string title, ContentDrawer draw)
    {
        float cy = -9999f;
        draw(x, ref cy, w);
        float contentH = cy - (-9999f);

        float h = 18f + contentH + 4f;
        GUI.Box(new Rect(x, y, w, h), "", _boxStyle);
        GUI.Label(new Rect(x + 6f, y + 2f, w - 12f, 16f), title, _headerStyle);

        float iy = y + 18f;
        draw(x, ref iy, w);
        return y + h + 4f;
    }

    // ── Sections ──────────────────────────────────────────────────────────────

    private void DrawClock(float x, ref float y, float w)
    {
        var c = MusicClock.Instance;
        if (c == null) { Row(x, ref y, w, "No MusicClock"); return; }
        Row(x, ref y, w, $"SongTime:      {c.SongTime:F3} s");
        Row(x, ref y, w, $"MusicDistance: {c.MusicDistance:F2} u");
        Row(x, ref y, w, $"Speed:         {c.UnitsPerSecond:F1} u/s   Running:{c.IsRunning}");
    }

    private void DrawPath(float x, ref float y, float w)
    {
        var path = MusicWorldManager.Instance?.Path;
        if (path == null) { Row(x, ref y, w, "No path"); return; }

        // PlayerController is the single authority for "where the player actually is" — read
        // ActualDistance from there rather than re-deriving MusicDistance + ForwardOffset here.
        float dist   = _player?.ActualDistance ?? 0f;
        var   sample = path.GetSample(dist);

        Row(x, ref y, w, $"TotalLength: {path.TotalLength:F1} u  Samples:{path.SampleCount}");
        Row(x, ref y, w, $"localPathWidth: {sample.width:F2} u  @ playerDistance");
        Row(x, ref y, w, $"PathPos:  {sample.position:F1}");
    }

    private void DrawPlayer(float x, ref float y, float w)
    {
        if (_player == null) { Row(x, ref y, w, "No player"); return; }

        var clock = MusicClock.Instance;

        float minimumDistance = _player.CanonicalDistance;
        float maximumDistance = minimumDistance + _player.MaxForwardDistance;
        float playerDistance  = _player.ActualDistance;

        Row(x, ref y, w, $"songTime:       {clock?.SongTime ?? 0f:F3} s");
        Row(x, ref y, w, $"musicDistance:  {minimumDistance:F2} u  (window {minimumDistance:F1} .. {maximumDistance:F1})");
        Row(x, ref y, w, $"playerDistance: {playerDistance:F2} u");
        Row(x, ref y, w, $"forwardOffset:  {_player.ForwardOffset:F2} / {_player.MaxForwardDistance:F2} u");
        Row(x, ref y, w, $"lateralOffset:  {_player.LateralOffset:+0.00;-0.00;0.00} u  (±{_player.LateralLimit:F2})");
        Row(x, ref y, w, $"verticalVel:    {_player.VerticalVelocity:F2} u/s   isGrounded:{_player.IsGrounded}");
        Row(x, ref y, w, _player.IsAtLateralLimit
            ? "<color=#ff8844>AT LATERAL LIMIT</color>"
            : "<color=#88ff88>ON PATH</color>");
    }

    private void DrawCamera(float x, ref float y, float w)
    {
        if (_camera == null) { Row(x, ref y, w, "No camera"); return; }
        Row(x, ref y, w, $"CameraPos: {_camera.transform.position:F1}");
    }

    private void DrawNextEvent(float x, ref float y, float w)
    {
        if (_manager?.Timeline == null) { Row(x, ref y, w, "No timeline"); return; }
        var events = _manager.Timeline.Events;
        int idx    = _manager.NextEventIndex;
        if (idx >= events.Length) { Row(x, ref y, w, "Song ended"); return; }

        var  evt = events[idx];
        var  clk = MusicClock.Instance;
        float dt = clk != null ? evt.eventTime - clk.SongTime : float.NaN;

        Row(x, ref y, w, $"#{idx} {evt.ringType} [{evt.sourceFeature}]  conf={evt.confidence:F2}");
        Row(x, ref y, w, $"  contributors=[{evt.contributors}]");
        Row(x, ref y, w, $"  t={evt.eventTime:F3}s  ({dt:+0.00;-0.00}s)  d={evt.eventDistance:F1}u");
        Row(x, ref y, w, $"  lateral={evt.lateralOffset:+0.00;-0.00} heightOffset={evt.verticalOffset:F2}");
        Row(x, ref y, w, $"  str={evt.strength:F2}   Active:{_manager.ActiveEventCount}");

        // Ground-clearance verification — reproduces GameplayManager.ActivateEvent's EXACT
        // placement math (floorClearance along the true surface normal, the remainder along
        // path-up) so this is a genuine check of the real final position, not an approximation.
        var world = MusicWorldManager.Instance;
        if (world != null && _manager.Config != null && world.Path != null)
        {
            var surface = world.SampleSurface(evt.eventDistance, evt.lateralOffset);
            var sample  = world.Path.GetSample(evt.eventDistance);

            const float baseClearance = 0.05f;
            float floorPortion = Mathf.Min(evt.verticalOffset, evt.floorClearance);
            float extraPortion = Mathf.Max(0f, evt.verticalOffset - evt.floorClearance);
            Vector3 finalPos = surface.position + surface.normal * (baseClearance + floorPortion) + sample.up * extraPortion;

            float halfHeight = GameplayTimeline.CollectibleMaxHalfHeight(evt.ringType, _manager.Config);
            float bottomY    = finalPos.y - halfHeight;
            float clearance  = bottomY - surface.position.y;
            string warn = clearance < 0f ? "  <color=#ff4444>EMBEDDED!</color>" : "";

            Row(x, ref y, w, $"  surfaceY={surface.position.y:F2}  finalY={finalPos.y:F2}");
            Row(x, ref y, w, $"  collectibleBottomY={bottomY:F2}  clearance={clearance:F2}{warn}");
            Row(x, ref y, w, $"  maxReachable≈{_manager.MaxReachableJumpHeight:F2}");
        }
    }

    private void DrawImpactLog(float x, ref float y, float w)
    {
        if (_impactLog.Count == 0) { Row(x, ref y, w, "(none collected yet)"); return; }
        foreach (var line in _impactLog) Row(x, ref y, w, line);
    }

    private void DrawMacroLog(float x, ref float y, float w)
    {
        if (_macroLog.Count == 0) { Row(x, ref y, w, "(none fired yet)"); return; }
        foreach (var line in _macroLog) Row(x, ref y, w, line);
    }

    private void DrawEnvironment(float x, ref float y, float w)
    {
        if (_environment == null) _environment = FindFirstObjectByType<MusicEnvironmentController>();
        if (_environment == null) { Row(x, ref y, w, "No MusicEnvironmentController"); return; }
        if (!_environment.HasProfile) { Row(x, ref y, w, "Waiting for SongProfile..."); return; }

        Color.RGBToHSV(_environment.CurrentColor, out float h, out float s, out float v);
        float swatchRowY = y;
        Row(x, ref y, w, $"current: hue={h:F2} sat={s:F2} val={v:F2}");
        Row(x, ref y, w, $"buildup: {_environment.LastBuildupValue:F2}   clarity: {_environment.LastClarity:F2}");

        float sw = 40f, sh = 13f;
        var rect = new Rect(x + w - sw - 8f, swatchRowY, sw, sh);
        var prevColor = GUI.color;
        GUI.color = _environment.CurrentColor;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = prevColor;
    }

    private void DrawSemanticTags(float x, ref float y, float w)
    {
        if (_audioBootstrapper == null) _audioBootstrapper = FindFirstObjectByType<AudioSystemBootstrapper>();
        var audioConfig = _audioBootstrapper?.Config;

        if (_profile == null || !_profile.HasMusicTags)
        {
            Row(x, ref y, w, "No tags (disabled, song too short, or not analyzed yet)");
            return;
        }

        if (audioConfig != null && !audioConfig.semanticTagDebugUI)
        {
            Row(x, ref y, w, $"{_profile.musicTags.Length} tags — enable AudioAnalysisConfig.semanticTagDebugUI for raw scores");
            return;
        }

        foreach (var t in _profile.musicTags)
            Row(x, ref y, w, $"{t.tag,-18} {t.score:F3}  [{MusicTagClassifier.Classify(t.tag)}]");
    }

    private void DrawEventStats(float x, ref float y, float w)
    {
        if (_manager?.Timeline == null) { Row(x, ref y, w, "No timeline"); return; }
        var events = _manager.Timeline.Events;
        if (events.Length == 0) { Row(x, ref y, w, "0 events"); return; }

        var counts = new Dictionary<RingType, int>();
        foreach (RingType rt in System.Enum.GetValues(typeof(RingType))) counts[rt] = 0;
        foreach (var e in events) counts[e.ringType]++;

        float span      = Mathf.Max(0.01f, events[events.Length - 1].eventTime - events[0].eventTime);
        float perMinute = events.Length / (span / 60f);

        Row(x, ref y, w, $"Total: {events.Length}   ({perMinute:F1} / min)");
        Row(x, ref y, w, $"Score: {_manager.Stats?.Score ?? 0} / max {_manager.MaxPossibleScore}  " +
                         $"(normalized {_manager.NormalizedScore * 100f:F0}%)");
        foreach (var rt in counts.Keys)
        {
            bool   spawnEnabled = _manager.Config == null || _manager.Config.collectibles.IsSpawnEnabled(rt);
            string warn = counts[rt] != 0 ? ""
                        : !spawnEnabled   ? "  <color=#888888>(off)</color>"
                        :                   "  <color=#ff6666>(0!)</color>";
            Row(x, ref y, w, $"  {rt,-7} {counts[rt]}{warn}");
        }
    }

    private void DrawPerformance(float x, ref float y, float w)
    {
        if (_manager == null) { Row(x, ref y, w, "No manager"); return; }
        var perf = _manager.BuildGamePerformance();

        Row(x, ref y, w, $"Overall: {perf.OverallEarnedScore} / {perf.OverallMaxPossibleScore}  " +
                         $"({perf.OverallNormalizedScore * 100f:F0}%)");

        foreach (RingType rt in System.Enum.GetValues(typeof(RingType)))
        {
            if (!perf.ByType.TryGetValue(rt, out var tp)) continue; // no data for this type this song
            Row(x, ref y, w,
                $"{rt,-7} {tp.Collected,3}/{tp.Available,-3} rate={tp.CollectionRate:F2}  " +
                $"score={tp.EarnedScore}/{tp.MaxPossibleScore} norm={tp.NormalizedScore:F2}  " +
                $"timing={tp.TimingAccuracy:F2} (err={tp.AverageTimingError * 1000f:F0}ms)");
        }
    }

    private void DrawSyncAudit(float x, ref float y, float w)
    {
        if (_manager?.Timeline == null) { Row(x, ref y, w, "No timeline"); return; }
        var s = _manager.Timeline.SyncDebug;

        Row(x, ref y, w, $"First analyzed onset:        {Fmt(s.firstAnalyzedOnset)}");
        Row(x, ref y, w, $"First classified percussion: {Fmt(s.firstClassified)}");
        Row(x, ref y, w, $"First micro survives filters: {Fmt(s.firstMicroKept)}");
        Row(x, ref y, w, $"First macro event:            {Fmt(s.firstMacro)}");
        Row(x, ref y, w, $"First final TimelineEvent:    {Fmt(s.firstFinalEvent)}");
        Row(x, ref y, w, "(all in SongTime domain — directly comparable to MUSIC CLOCK above)");

        static string Fmt(float v) => v >= 0f ? $"{v:F3}s" : "(none)";
    }

    private void DrawPools(float x, ref float y, float w)
    {
        var pools = _manager?.RingPools;
        if (pools == null || pools.Count == 0) { Row(x, ref y, w, "No pools"); return; }
        foreach (var kv in pools)
        {
            var p   = kv.Value;
            int pct = p.TotalCount > 0 ? Mathf.RoundToInt(100f * p.FreeCount / p.TotalCount) : 0;
            Row(x, ref y, w, $"{p.Label,-6} [{Bar(p.FreeCount, p.TotalCount, 10)}] {p.FreeCount}/{p.TotalCount} ({pct}%)");
        }
    }

    private void DrawCheckpoint(float x, ref float y, float w)
    {
        var cs = CheckpointSystem.Instance;
        if (cs == null || !cs.HasCheckpoints) { Row(x, ref y, w, "No checkpoints"); return; }

        var cp  = cs.CurrentCheckpoint;
        var clk = MusicClock.Instance;
        float dt = clk != null ? cp.songTime - clk.SongTime : 0f;

        Row(x, ref y, w, $"Current CP: #{cp.index}  total:{cs.All?.Length}");
        Row(x, ref y, w, $"  songTime={cp.songTime:F1}s  dist={cp.musicDistance:F1}u");
        Row(x, ref y, w, $"  next in {-dt:F1}s  firstEvt=#{cp.firstEventIndex}");
    }

    private void DrawFall(float x, ref float y, float w)
    {
        if (_player == null) { Row(x, ref y, w, "No player"); return; }

        string mode = "?";
        if (FallRespawnSystem.Instance != null)
        {
            // Read from config via manager or just show static info
            mode = _player.IsFalling ? "<color=#ff4444>FALLING</color>" : "OK";
        }

        Row(x, ref y, w, $"State: {mode}");
        Row(x, ref y, w, $"IsFalling: {_player.IsFalling}");
        Row(x, ref y, w, $"LateralLimit: {_player.LateralLimit:F2}  LateralAbs: {Mathf.Abs(_player.LateralOffset):F2}");
    }

    private void DrawWorldMesh(float x, ref float y, float w)
    {
        var world = MusicWorldManager.Instance;
        if (world == null) { Row(x, ref y, w, "No world"); return; }

        Row(x, ref y, w, $"active windows: {(world.IsReady ? 1 : 0)}   vertices: {world.WindowVertexCount}   tris: {world.WindowTriangleCount}");
        Row(x, ref y, w, $"crossSegments: {world.CrossSegments}   longitudinalRows: {world.WindowRowCount}");
        Row(x, ref y, w, $"frequencyBands: {world.FrequencyBandsUsed}");
        Row(x, ref y, w, $"shader: {world.MaterialShaderName}");
        Row(x, ref y, w, $"receiveShadows: {world.ReceiveShadows}   shadowCasting: {world.ShadowCasting}");

        var clock = MusicClock.Instance;
        if (clock != null)
        {
            float raw  = world.SampleFrequencyValue(clock.MusicDistance, 0.5f, out float norm);
            Row(x, ref y, w, $"underPlayer: frequencyValue={raw:F2}  normalized={norm:F2}");
        }
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (!showGizmos || !Application.isPlaying) return;

        var path = MusicWorldManager.Instance?.Path;
        if (path == null) return;

        // Path centreline
        var samples = path.AllSamples;
        for (int i = 1; i < samples.Length; i += 4)
        {
            Gizmos.color = Color.Lerp(Color.cyan, Color.magenta, (float)i / samples.Length);
            Gizmos.DrawLine(samples[i - 1 < 0 ? 0 : i - 1].position, samples[i].position);
        }

        // Path edges
        var clock = MusicClock.Instance;
        if (clock != null)
        {
            float start = Mathf.Max(0f, clock.MusicDistance - 30f);
            float end   = Mathf.Min(path.TotalLength, clock.MusicDistance + 80f);
            for (float d = start; d < end; d += 1f)
            {
                var s = path.GetSample(d);
                Vector3 left  = s.position - s.right * s.width * 0.5f;
                Vector3 right = s.position + s.right * s.width * 0.5f;
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
                Gizmos.DrawLine(left, right);
                Gizmos.color = new Color(0f, 1f, 0.5f, 0.5f);
                Gizmos.DrawLine(left, path.GetSample(d + 1f).position - path.GetSample(d + 1f).right * path.GetSample(d + 1f).width * 0.5f);
                Gizmos.DrawLine(right, path.GetSample(d + 1f).position + path.GetSample(d + 1f).right * path.GetSample(d + 1f).width * 0.5f);
            }
        }

        // Longitudinal playable window: [canonicalDistance, canonicalDistance + maxForwardDistance]
        if (_player != null)
        {
            float minDist = _player.CanonicalDistance;
            float maxDist = minDist + _player.MaxForwardDistance;

            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.9f); // minimumDistance — player can never fall behind this
            Gizmos.DrawWireSphere(path.GetSample(minDist).position, 1f);
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.9f); // maximumDistance — forward surge ceiling
            Gizmos.DrawWireSphere(path.GetSample(maxDist).position, 1f);
        }

        // Checkpoints
        var cs = CheckpointSystem.Instance;
        if (cs?.All != null)
        {
            foreach (var cp in cs.All)
            {
                bool isCurrent = cp.index == cs.CurrentIndex;
                Gizmos.color = isCurrent ? Color.yellow : new Color(1f, 1f, 0f, 0.4f);
                Gizmos.DrawWireSphere(cp.position, isCurrent ? 1.5f : 0.8f);
            }
        }

        // Active events (rings)
        if (_manager != null)
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.6f);
            for (int i = 0; i < _manager.ActiveEventCount; i++)
                if (i < 30)  // limit draw count
                    Gizmos.DrawWireCube(Vector3.zero, Vector3.one * 0.5f);  // placeholder
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void Row(float x, ref float y, float w, string text)
    {
        GUI.Label(new Rect(x + 6f, y, w - 12f, 16f), text, _labelStyle);
        y += 15f;
    }

    private static string Bar(int free, int total, int barW)
    {
        if (total == 0) return new string('-', barW);
        int filled = Mathf.RoundToInt(barW * (float)free / total);
        return new string('|', filled) + new string('.', barW - filled);
    }

    private void EnsureStyles()
    {
        if (_boxStyle != null) return;

        var bgTex = new Texture2D(1, 1);
        bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.78f));
        bgTex.Apply();

        _boxStyle = new GUIStyle(GUI.skin.box)
            { border = new RectOffset(4, 4, 4, 4) };
        _boxStyle.normal.background = bgTex;

        _labelStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 11, richText = true };
        _labelStyle.normal.textColor = new Color(0.9f, 0.95f, 1f);

        _headerStyle = new GUIStyle(_labelStyle)
            { fontStyle = FontStyle.Bold, fontSize = 11 };
        _headerStyle.normal.textColor = new Color(0.7f, 1f, 0.8f);
    }
}
