using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Manages the MusicPath for the current session.
/// Created dynamically by GameplayManager — no scene setup required.
/// Subscribe order: MusicWorldManager subscribes BEFORE GameplayManager so Path is ready
/// by the time GameplayManager's coroutine checks for it.
/// </summary>
public class MusicWorldManager : MonoBehaviour
{
    public static MusicWorldManager Instance { get; private set; }

    /// <summary>
    /// A single authoritative surface query result — mesh generation and collectible
    /// placement both derive from the same underlying function, so they can never disagree.
    /// </summary>
    public readonly struct SurfaceSample
    {
        public readonly Vector3 position; // world position ON the surface (no clearance added)
        public readonly Vector3 normal;   // surface normal at this exact point (path-local slope aware)
        public readonly float   height;   // world-space height above the path centerline

        public SurfaceSample(Vector3 position, Vector3 normal, float height)
        {
            this.position = position;
            this.normal   = normal;
            this.height   = height;
        }
    }

    private MusicRunnerGameplayConfig _config;
    private Material       _pathMaterial; // optional

    public MusicPath Path    { get; private set; }
    public bool      IsReady => Path != null;

    private SongProfile _profile;
    private float[]     _bandReference;
    private Material    _meshMaterial;
    private System.Action<SongProfileReadyEvent> _onProfile;

    // The played window's own end (song-time seconds, same convention as GameplayManager.SongPlayEnd
    // — see PlayRangeResolver) — 0 until BuildWorld resolves it. Used only by NormalizedBandValue's
    // fade-to-flat near the end; the countdown's synthetic hill (time &lt; 0) needs no such state.
    private float _playRangeEnd;

    // ── Debug/HUD-facing stats about the current window ─────────────────────────
    public int CrossSegments      { get; private set; }
    public int FrequencyBandsUsed { get; private set; }
    public int WindowRowCount     { get; private set; }
    public int WindowVertexCount  { get; private set; }
    public int WindowTriangleCount{ get; private set; }
    public string MaterialShaderName => _meshMaterial != null ? _meshMaterial.shader.name : "(none)";
    public bool   ReceiveShadows     { get; private set; }
    public ShadowCastingMode ShadowCasting { get; private set; }

    // ── Ground (windowed / streamed, one mesh = visual + collision) ─────────────
    //
    // The path itself (MusicPath) is a single smooth centreline (coarse — buildups, drops,
    // big turns). The ground you actually see AND stand on is a finer musical "equalizer /
    // waveform" surface layered on top of it: subdivided across the WIDTH into CrossSegments
    // columns. Frequency resolution comes from SongProfile.visualBandEnvelopes — a SEPARATE,
    // higher-resolution, log-spaced band set (AudioAnalysisConfig.visualBandCount, computed
    // offline alongside the classification bands, never touching Kick/Snare/HiHat detection)
    // — geometrically interpolated across CrossSegments columns (a configurable, INDEPENDENT
    // mesh resolution). Column 0 = lowest frequency, last column = highest — deterministic,
    // identical for every window/chunk. Each column's height follows how energetic that band
    // is right now.
    //
    // HEIGHT and terrain COLOR intentionally read the SAME processed (smoothed + slope-clamped)
    // grid value — VISUALLY, the terrain's color has to read as "the shape of this terrain", not
    // decorrelated from it (a raw pre-smoothing value was tried and looked wrong: physically-low
    // patches could paint red while a visible ridge painted green, since the raw spectrum value
    // and the smoothed/clamped height can diverge a lot at any single point). Color still gets
    // its own independent knob — terrainColorRedThreshold (TerrainVuColor) — a pure remap of
    // "how soon does the palette reach red", never touching the shared height/color value itself.
    //
    // The SCANLINE (FrequencyTexture, see UpdatePlayheadGlobals/UpdateFrequencyTexture) is the
    // one place that DOES still read the raw, unsmoothed, per-instant NormalizedBandValue — it's
    // a live "equalizer" reading of the CURRENT moment, not a description of the terrain sitting
    // under the player, so it deliberately doesn't share the terrain's smoothing/clamping/color
    // remap at all.
    //
    // Longitudinal row spacing is its own configurable resolution (longitudinalSegmentsPerMeter),
    // independent of MusicPath's own control-point/curve resolution (pathSampleSpacing) —
    // rows are sampled directly from MusicPath.GetSample() at a fixed GLOBAL distance grid
    // (rowIndex * rowSpacing), not from MusicPath's internal sample array. That global grid is
    // what makes two consecutive window rebuilds line up exactly: row N always sits at the
    // same world distance no matter where the window currently starts, and its processed
    // height comes from the one whole-path grid (see SEAMLESS WINDOWS below).
    //
    // Two smoothing/safety layers on HEIGHT (and, by sharing the same value, on terrain COLOR
    // too — see above):
    //   - smoothing passes both along travel and across width (organic, no hard bar edges)
    //   - a hard slope clamp in BOTH directions, expressed in WORLD units and converted back
    //     through maxFrequencyHeight — guarantees no local slope ever exceeds what the
    //     CharacterController can climb, regardless of how tall maxFrequencyHeight makes
    //     the surface look.
    //
    // Indexed by SONG DISTANCE, not world position: a long winding song can pass near itself
    // in world space, so a mesh built from the whole path at once could let collision resolve
    // against a completely unrelated moment of the song. Keeping only a slice near the
    // player's current distance makes that structurally impossible.

    //
    // SEAMLESS WINDOWS: the processed height field (raw bands → smoothing → slope clamps →
    // post-pass) is computed ONCE for the whole path at BuildWorld (_globalGrid, addressed by a
    // global row index = distance / rowSpacing). Every window is an exact SLICE of it, so the
    // terrain at a given distance is bit-identical in every window that contains it — a rebuild
    // can never move/reshape anything the player or camera can see. (Previously each window
    // smoothed and slope-clamped only its own rows; window-edge effects made the same distance
    // differ by up to ~0.5 m between consecutive windows, visibly popping the terrain ahead and
    // behind every 15 m.) One ground GameObject + one render mesh are reused in place; collision
    // uses two alternating meshes baked on a worker thread (Physics.BakeMesh) and swapped in when
    // ready — the overlap is identical, so the still-active previous collider is exact meanwhile.
    private GameObject   _groundGO;
    private MeshRenderer _groundMR;
    private MeshCollider _groundMC;
    private Mesh         _groundMesh;
    private readonly Mesh[] _colliderMeshes = new Mesh[2];
    private int          _colliderBuffer;
    private JobHandle    _bakeHandle;
    private Mesh         _pendingColliderMesh;
    private float        _groundWindowCenter = float.NaN;

    private float[,] _globalGrid;
    private float    _rowSpacing;
    private int      _globalRows;
    private int      _gridCols;

    // Reused mesh buffers (reallocated only when the window's row count changes — path start/end).
    private Vector3[] _verts, _normals;
    private Color32[] _colors;
    private Vector2[] _uvs;
    private int[]     _tris;
    private int       _bufferRows = -1;
    private int       _bufferRow0 = int.MinValue; // global row index held in buffer row 0

    private struct BakeColliderJob : IJob
    {
        public int MeshId;
        public MeshColliderCookingOptions Options;
        public void Execute() => Physics.BakeMesh(MeshId, false, Options);
    }

    private const float GroundWindowBehind = 20f;
    private const float GroundWindowAhead  = 60f;
    private const float GroundRebuildStep  = 15f;

    // ── Playhead scanline (see MusicRunnerLevelConfig's own doc for the design) ─────────
    // Per-chunk (set once per rebuild via MaterialPropertyBlock — no Material cloning):
    private static readonly int StartMusicDistanceID = Shader.PropertyToID("_StartMusicDistance");
    private static readonly int EndMusicDistanceID    = Shader.PropertyToID("_EndMusicDistance");
    private MaterialPropertyBlock _groundMPB;
    // Global (pushed once per frame — every chunk's shader instance picks these up automatically,
    // no per-chunk CPU work, no "which chunk is active" lookup):
    private static readonly int PlayheadDistanceID = Shader.PropertyToID("_PlayheadMusicDistance");
    private static readonly int PlayheadEnabledID  = Shader.PropertyToID("_PlayheadEnabled");
    private static readonly int PlayheadWidthID    = Shader.PropertyToID("_PlayheadLineWidth");
    private static readonly int PlayheadEmissionID = Shader.PropertyToID("_PlayheadEmission");
    private static readonly int PlayheadUseFreqID  = Shader.PropertyToID("_PlayheadUseFreqColors");
    private static readonly int PlayheadColorID    = Shader.PropertyToID("_PlayheadSingleColor");
    private static readonly int FreqTexID          = Shader.PropertyToID("_FreqTex");
    // Reused every frame — resized only if the band count itself changes (never mid-song).
    private Texture2D _freqTex;
    private Color32[] _freqPixels;


    // ── Factory ───────────────────────────────────────────────────────────────

    public static MusicWorldManager GetOrCreate(GameObject host)
    {
        if (Instance != null) return Instance;
        return host.AddComponent<MusicWorldManager>();
    }

    public void Initialize(MusicRunnerGameplayConfig config, Material pathMaterial = null)
    {
        _config       = config;
        _pathMaterial = pathMaterial;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        // Never leave _FreqTex truly unbound (harmless either way, but avoids relying on
        // undefined-texture-slot behavior before the first UpdateFrequencyTexture call, e.g. if
        // playheadUseFrequencyColors starts false and is toggled on later).
        Shader.SetGlobalTexture(FreqTexID, Texture2D.blackTexture);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        _bakeHandle.Complete();
        if (_groundGO != null) Destroy(_groundGO);
        if (_groundMesh != null) Destroy(_groundMesh);
        foreach (var m in _colliderMeshes) if (m != null) Destroy(m);
        if (_meshMaterial != null) Destroy(_meshMaterial);
        if (_freqTex != null) Destroy(_freqTex);
    }

    private void Update()
    {
        if (Path == null) return;
        var clock = MusicClock.Instance;
        if (clock == null) return;

        if (_pendingColliderMesh != null && _bakeHandle.IsCompleted) ApplyPendingCollider();

        float dist = clock.MusicDistance;
        if (float.IsNaN(_groundWindowCenter) || Mathf.Abs(dist - _groundWindowCenter) > GroundRebuildStep)
            RebuildGroundWindow(dist, syncCollider: false);

        UpdatePlayheadGlobals(dist);
    }

    // Everything here is GLOBAL shader state (Shader.SetGlobalX) — touches zero renderers/
    // materials, costs nothing per-chunk, and needs no "which chunk is the player in" lookup:
    // every chunk's own vertex UV.y + its MaterialPropertyBlock start/end already let its shader
    // resolve independently whether the playhead line falls inside it.
    private void UpdatePlayheadGlobals(float musicDistance)
    {
        if (_config == null) return;

        Shader.SetGlobalFloat(PlayheadEnabledID, _config.levelGeneration.playheadEnabled ? 1f : 0f);
        if (!_config.levelGeneration.playheadEnabled) return;

        Shader.SetGlobalFloat(PlayheadDistanceID, musicDistance + _config.levelGeneration.playheadOffset);
        Shader.SetGlobalFloat(PlayheadWidthID, Mathf.Max(0.001f, _config.levelGeneration.playheadLineWidth));
        Shader.SetGlobalFloat(PlayheadEmissionID, Mathf.Max(0f, _config.levelGeneration.playheadEmissionIntensity));
        Shader.SetGlobalFloat(PlayheadUseFreqID, _config.levelGeneration.playheadUseFrequencyColors ? 1f : 0f);
        Shader.SetGlobalColor(PlayheadColorID, _config.levelGeneration.playheadSingleColor);

        if (_config.levelGeneration.playheadUseFrequencyColors)
            UpdateFrequencyTexture(musicDistance);
    }

    // A small 1D (Nx1) texture, one texel per visual band, so the shader can sample the WHOLE
    // current spectrum with a single tex2D lookup at (uv.x, 0.5) instead of the shader touching
    // per-band data directly. Reuses the EXACT same source (NormalizedBandValue) and palette
    // (VuColor / low-mid-highEnergyColor) the ground mesh's own vertex colors already use — never
    // a second color system to keep in sync. _freqPixels is resized only when the band count
    // itself changes (effectively once, when the profile loads), so a normal frame just
    // overwrites it in place and re-uploads — no per-frame allocation.
    private void UpdateFrequencyTexture(float musicDistance)
    {
        int numBands = _profile?.VisualBandCount ?? 0;
        if (numBands <= 0) return;

        if (_freqTex == null || _freqTex.width != numBands)
        {
            if (_freqTex != null) Destroy(_freqTex);
            _freqTex = new Texture2D(numBands, 1, TextureFormat.RGBA32, false, false)
            {
                name       = "MusicPath_PlayheadFreq",
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _freqPixels = new Color32[numBands];
        }

        float time = DistanceToSongTime(musicDistance);
        for (int b = 0; b < numBands; b++)
            _freqPixels[b] = VuColor(NormalizedBandValue(b, time));

        _freqTex.SetPixels32(_freqPixels);
        _freqTex.Apply(false);
        Shader.SetGlobalTexture(FreqTexID, _freqTex);
    }

    private void OnEnable()
    {
        _onProfile = e => BuildWorld(e.Profile);
        EventBus.Subscribe(_onProfile);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onProfile);
    }

    // ── World generation ──────────────────────────────────────────────────────

    private void BuildWorld(SongProfile profile)
    {
        if (_config == null) { Debug.LogError("[MusicWorldManager] Not initialized — call Initialize() first."); return; }

        Vector3 startPos = new Vector3(0f, _config.levelGeneration.pathBaseHeight, 0f);
        Path     = new SnakeWayWorldGenerator().Generate(profile, _config, startPos);
        _profile = profile;
        _bandReference = ComputeBandReferences(profile, _config.levelGeneration.frequencyColorReferencePercentile);
        FrequencyBandsUsed = profile.VisualBandCount;
        _playRangeEnd = PlayRangeResolver.Resolve(_config.core, profile.duration).End;

        if (_meshMaterial != null) Destroy(_meshMaterial);
        _meshMaterial = VertexColorMaterial();

        _groundWindowCenter = float.NaN;
        ComputeGlobalGrid(); // the whole path's terrain, once — windows are slices of it
        _bufferRow0 = int.MinValue; // buffered rows belonged to the previous path
        RebuildGroundWindow(0f, syncCollider: true); // real ground under the player before the first Update() tick

        EventBus.Publish(new MusicPathReadyEvent { Path = Path });
    }

    // ── Ground window: banded height grid → one mesh (visual + collision) ──────

    /// <summary>
    /// THE single normalized 0..1 value for a band at a time — height, colour, the UI
    /// equalizer (AudioDebugVisualizer) and the debug HUD ALL read this exact function, so none
    /// of them can quietly drift into disagreement with the others. _bandReference[b] is that
    /// band's own Nth-percentile energy over the whole song (see ComputeBandReferences) —
    /// reaching it maps to 1.0 by construction, regardless of how skewed that band's raw energy
    /// distribution is. frequencyContrastGamma then reshapes the result (norm^gamma) — energy
    /// envelopes are right-skewed, so even after percentile referencing, most moments still
    /// cluster in the low-mid range; gamma &lt; 1 pushes that range up so the terrain actually
    /// uses the orange/red end of the gradient instead of reading as mostly yellow. Applied HERE
    /// (not separately for colour) so height and colour always represent the identical number.
    /// </summary>
    public float NormalizedBandValue(int b, float time)
    {
        // Countdown pre-roll (time < 0 — the song hasn't actually started playing yet, see
        // DistanceToSongTime's own doc): there's no real analysis to show here, so every band
        // returns the SAME synthetic value instead of a per-frequency shape — a hill spanning the
        // full width of the path, not a left-to-right spectrum silhouette.
        if (time < 0f) return SyntheticCountdownValue(time);

        if (_bandReference == null || b < 0 || b >= _bandReference.Length || _profile == null) return 0f;
        float norm = _bandReference[b] > 0.0001f ? _profile.GetVisualBandEnergyAtSmooth(b, time) / _bandReference[b] : 0f;
        norm = Mathf.Clamp01(norm);
        norm = Mathf.Pow(norm, Mathf.Max(0.01f, _config.levelGeneration.frequencyContrastGamma));
        return norm * EndingFadeMultiplier(time);
    }

    // One hill per second of countdown, peaking at exactly the half-second mark and back to 0 at
    // the second boundary — "as if" a steady, evenly-paced countdown beat had been analyzed.
    // Mathf.Floor-based fractional part is correct for negative `time` without special-casing
    // (e.g. time=-2.3 → floor=-3 → fraction=0.7, exactly 0.7s into that countdown second).
    // Peaks at 0.5 — HALF of NormalizedBandValue's own real max of 1.0 — never full height.
    private static float SyntheticCountdownValue(float time)
    {
        float fractionOfSecond = time - Mathf.Floor(time);
        return Mathf.Sin(fractionOfSecond * Mathf.PI) * 0.5f;
    }

    // Ramps 1 → 0 over the LAST fadeOutSeconds of the played window, finishing exactly at its end
    // point (_playRangeEnd) — and stays clamped at 0 for any time beyond that (the farewell
    // stretch, plus whatever of the underlying clip's own content happens to extend past it —
    // see GameplayManager's own ending sequence, which fades the AUDIO out over this identical
    // window so the terrain and the music go silent/flat together).
    private float EndingFadeMultiplier(float time)
    {
        if (_playRangeEnd <= 0f || _config.core.fadeOutSeconds <= 0f) return 1f;
        float fadeStart = _playRangeEnd - _config.core.fadeOutSeconds;
        if (time < fadeStart) return 1f;
        return Mathf.Clamp01(1f - (time - fadeStart) / _config.core.fadeOutSeconds);
    }

    // Raw analytic surface height (0..1 normalized), with NO smoothing/slope-clamp — used for
    // edge-of-window normal finite-differences (see BuildMesh) and for SampleSurface(), both of
    // which need a value at a distance that may fall outside the currently-built grid array.
    // Being a pure function of (distance, lateralFrac) — no window state involved — it's exactly
    // reproducible from either side of a chunk boundary, which is what keeps normals seam-free.
    private float HeightAt(float distance, float lateralFrac, int numBands)
    {
        float time = DistanceToSongTime(distance);
        return numBands > 0 ? BlendedNormalized(lateralFrac, time, numBands) : 0f;
    }

    // Distance → song-TIME, in the SAME convention CameraFollow already uses for its own
    // profile lookups (clock.SongTime - warmupTime) — MusicDistance = SongTime * UnitsPerSecond,
    // so distance/speed IS SongTime, and subtracting warmupTime here converts it to real,
    // audio-relative song time. Genuinely negative during the countdown (before the song has
    // actually started playing) — NormalizedBandValue treats that as its own case (a synthetic
    // hill), never a clamp to frame 0, which is what the un-corrected formula used to do (and
    // what left the terrain permanently warmupTime seconds ahead of the audible audio).
    private float DistanceToSongTime(float distance) =>
        _config.core.playerSpeed > 0f ? distance / _config.core.playerSpeed - _config.core.warmupTime : 0f;

    // Smoothly blends the two nearest bands so the terrain rolls continuously across the
    // width instead of stepping — each band's own "purity" peaks at its centre column. This is
    // the geometric interpolation that lets CrossSegments be much higher than the number of
    // real frequency bands without inventing new musical detail.
    private float BlendedNormalized(float lateralFrac, float time, int numBands)
    {
        BandBlend(lateralFrac, numBands, out int b0, out int b1, out float smoothT);
        return Mathf.Lerp(NormalizedBandValue(b0, time), NormalizedBandValue(b1, time), smoothT);
    }

    // Which two bands a lateral position blends, and how — shared by every sampling path.
    private static void BandBlend(float lateralFrac, int numBands, out int b0, out int b1, out float smoothT)
    {
        float bandPos  = Mathf.Clamp01(lateralFrac) * numBands;
        float centered = bandPos - 0.5f;
        b0 = Mathf.Clamp(Mathf.FloorToInt(centered), 0, numBands - 1);
        b1 = Mathf.Clamp(b0 + 1, 0, numBands - 1);
        float t = Mathf.Clamp01(centered - b0);
        smoothT = t * t * (3f - 2f * t); // smoothstep
    }

    /// <summary>
    /// Forces an immediate, synchronous ground rebuild centered at `distance` — used by
    /// FallRespawnSystem right after teleporting the player to a checkpoint, so solid ground
    /// already exists there the instant normal movement resumes, instead of waiting up to a
    /// frame for Update()'s own distance-drift check to notice and rebuild on its own.
    /// </summary>
    public void RebuildNow(float distance) => RebuildGroundWindow(distance, syncCollider: true);

    /// <summary>
    /// The processed normalized height field for the WHOLE path (rows on the global distance grid,
    /// cols across the width) — the exact pipeline windows used to run per window, now run once so
    /// every window is a slice of the same result. Pure function of (profile, path, config).
    /// </summary>
    private void ComputeGlobalGrid()
    {
        _rowSpacing = 1f / Mathf.Max(_config.levelGeneration.longitudinalSegmentsPerMeter, 0.01f);
        float total = Path.TotalLength;
        int rows = Mathf.Max(2, Mathf.CeilToInt(total / _rowSpacing) + 1);
        int cols = Mathf.Max(2, _config.levelGeneration.crossMeshSegments) + 1;
        int numBands = _profile?.VisualBandCount ?? 0;
        _globalRows = rows;
        _gridCols = cols;

        var rowDistance = new float[rows];
        var rowWidth    = new float[rows];
        for (int r = 0; r < rows; r++)
        {
            rowDistance[r] = Mathf.Min(r * _rowSpacing, total);
            rowWidth[r]    = Path.GetSample(rowDistance[r]).width;
        }

        // ── 1. Raw normalized height grid from the band spectrum ───────────────
        // (each band evaluated once per row, then blended per column — same result as
        // BlendedNormalized, far fewer energy lookups over a whole song)
        var grid = new float[rows, cols];
        var bandValues = new float[Mathf.Max(1, numBands)];
        for (int r = 0; r < rows; r++)
        {
            float time = DistanceToSongTime(rowDistance[r]);
            for (int b = 0; b < numBands; b++) bandValues[b] = NormalizedBandValue(b, time);
            for (int c = 0; c < cols; c++)
            {
                if (numBands <= 0) { grid[r, c] = 0f; continue; }
                BandBlend(cols > 1 ? (float)c / (cols - 1) : 0.5f, numBands, out int b0, out int b1, out float smoothT);
                grid[r, c] = Mathf.Lerp(bandValues[b0], bandValues[b1], smoothT);
            }
        }

        // ── 2/3. Smoothing — LONGITUDINAL (pathSmoothness, along travel) and LATERAL
        // (crossSmoothness, across the width) are independent knobs. Neither touches
        // crossMeshSegments/vertex count: just box-blur passes over the SAME grid, once per song.
        float smoothness         = Mathf.Clamp01(_config.levelGeneration.pathSmoothness);
        int   longitudinalPasses = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(1f, 3f, smoothness)));
        float smoothingRadius    = Mathf.Lerp(1f, 3f, smoothness);
        int   radiusZ            = Mathf.Max(1, Mathf.RoundToInt(smoothingRadius / Mathf.Max(_rowSpacing, 0.01f)));

        float crossSmoothness = Mathf.Clamp01(_config.levelGeneration.crossSmoothness);
        int   lateralPasses   = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(1f, 3f, crossSmoothness)));
        int   radiusX         = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(1f, 4f, crossSmoothness)));

        BoxBlurLongitudinal(grid, rows, cols, radiusZ, longitudinalPasses);
        BoxBlurLateral(grid, rows, cols, radiusX, lateralPasses);

        // ── 4. Hard slope clamps (in NORMALIZED space, converted from world-space limits) —
        // guarantees CharacterController never treats this as a wall, REGARDLESS of
        // maxFrequencyHeight, and — because clamping happens before colour is derived — colour
        // always matches the geometry. Limits are relative to CharacterController.slopeLimit (45°)
        // with headroom for the discretized mesh. Run over the whole path from its start, so the
        // sequential longitudinal clamp has one stable origin (no per-window re-anchoring).
        float scale = Mathf.Max(0.001f, _config.levelGeneration.maxFrequencyHeight);
        for (int c = 0; c < cols; c++)
        {
            for (int r = 1; r < rows; r++)
            {
                float dz = rowDistance[r] - rowDistance[r - 1];
                float maxDelta = (_config.levelGeneration.longitudinalSlopeLimit * Mathf.Max(dz, 0.001f)) / scale;
                grid[r, c] = Mathf.Clamp(grid[r, c], grid[r - 1, c] - maxDelta, grid[r - 1, c] + maxDelta);
            }
        }
        for (int r = 0; r < rows; r++)
        {
            float colSpacing = cols > 1 ? rowWidth[r] / (cols - 1) : 1f;
            float maxDeltaX  = (_config.levelGeneration.crossSlopeLimit * Mathf.Max(colSpacing, 0.001f)) / scale;
            for (int c = 1; c < cols; c++)
                grid[r, c] = Mathf.Clamp(grid[r, c], grid[r, c - 1] - maxDeltaX, grid[r, c - 1] + maxDeltaX);
        }

        // ── 5. Light post-clamp pass — rounds the kinks the hard min/max clamps leave exactly where
        // they engage/release, on BOTH axes (a small fixed corrective pass, not a stylistic knob).
        BoxBlurLongitudinal(grid, rows, cols, radius: 1, passes: 1);
        BoxBlurLateral(grid, rows, cols, radius: 1, passes: 1);

        _globalGrid = grid;
    }

    private void RebuildGroundWindow(float centerDistance, bool syncCollider)
    {
        _groundWindowCenter = centerDistance;
        if (_globalGrid == null) return;

        // Constant window length (slid inside [0, total] near the path's ends) → constant row count,
        // so every rebuild reuses the buffers and only computes the newly exposed rows.
        float total = Path.TotalLength;
        float span = GroundWindowBehind + GroundWindowAhead;
        float lo = Mathf.Clamp(centerDistance - GroundWindowBehind, 0f, Mathf.Max(0f, total - span));
        float hi = Mathf.Min(total, lo + span);
        if (hi - lo < _rowSpacing) return;

        int rowIndex0 = Mathf.FloorToInt(lo / _rowSpacing);
        int rowIndex1 = Mathf.Min(_globalRows - 1, Mathf.CeilToInt(hi / _rowSpacing));
        int rows      = rowIndex1 - rowIndex0 + 1;
        if (rows < 2) return;

        BuildMesh(rowIndex0, rows, syncCollider);

        CrossSegments       = _gridCols - 1;
        WindowRowCount      = rows;
        WindowVertexCount   = rows * _gridCols;
        WindowTriangleCount = (rows - 1) * (_gridCols - 1) * 2;
    }

    // Box-blur along the travel direction (rows) — multi-pass approximates a near-Gaussian.
    private static void BoxBlurLongitudinal(float[,] grid, int rows, int cols, int radius, int passes)
    {
        // Prefix sums (double) per column: same clamped-window average, O(1) per cell — this runs
        // over the whole path once per song (ComputeGlobalGrid), not just one window.
        var prefix = new double[rows + 1];
        for (int pass = 0; pass < passes; pass++)
        {
            for (int c = 0; c < cols; c++)
            {
                prefix[0] = 0d;
                for (int r = 0; r < rows; r++) prefix[r + 1] = prefix[r] + grid[r, c];
                for (int r = 0; r < rows; r++)
                {
                    int lo = Mathf.Max(0, r - radius);
                    int hi = Mathf.Min(rows - 1, r + radius);
                    grid[r, c] = (float)((prefix[hi + 1] - prefix[lo]) / (hi - lo + 1));
                }
            }
        }
    }

    // Box-blur across the width (columns), ON TOP of the band-blend interpolation — both radius
    // and pass count now configurable (crossSmoothness), independent of the longitudinal knob.
    private static void BoxBlurLateral(float[,] grid, int rows, int cols, int radius, int passes)
    {
        for (int pass = 0; pass < passes; pass++)
        {
            var src = (float[,])grid.Clone();
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int lo = Mathf.Max(0, c - radius);
                    int hi = Mathf.Min(cols - 1, c + radius);
                    float sum = 0f;
                    for (int j = lo; j <= hi; j++) sum += src[r, j];
                    grid[r, c] = sum / (hi - lo + 1);
                }
            }
        }
    }

    // ── Mesh + per-vertex colour (terrain color itself stays texture-free — see class doc for
    // why; UVs exist only so the playhead scanline shader can locate itself, see MusicRunnerLevelConfig's
    // Playhead Scanline doc) ─────────────────────────────────────────────────────

    private void EnsureGround()
    {
        if (_groundGO != null) return;
        _groundGO = new GameObject("MusicPath_Ground") { layer = 0 };
        var mf = _groundGO.AddComponent<MeshFilter>();
        _groundMR = _groundGO.AddComponent<MeshRenderer>();
        _groundMC = _groundGO.AddComponent<MeshCollider>();
        _groundMesh = new Mesh { indexFormat = IndexFormat.UInt32, name = "MusicPath_Ground" };
        _groundMesh.MarkDynamic();
        mf.sharedMesh = _groundMesh;
        _groundMR.material = _meshMaterial;
        // Receives player/collectible shadows (depth perception) AND casts its own relief's
        // shadows — see VertexColorLit shader, which keeps the musical vertex-colour gradient
        // readable (config.terrainAmbientFloor) instead of going to black under shadow.
        _groundMR.shadowCastingMode = ShadowCastingMode.On;
        _groundMR.receiveShadows    = true;
        ReceiveShadows = _groundMR.receiveShadows;
        ShadowCasting  = _groundMR.shadowCastingMode;
    }

    private void BuildMesh(int rowIndex0, int rows, bool syncCollider)
    {
        EnsureGround();
        if (_groundMR.sharedMaterial != _meshMaterial) _groundMR.sharedMaterial = _meshMaterial; // new song → new material

        int cols = _gridCols;
        float scale = Mathf.Max(0.001f, _config.levelGeneration.maxFrequencyHeight);
        float total = Path.TotalLength;
        bool resized = rows != _bufferRows;
        if (resized)
        {
            int n = rows * cols;
            _verts = new Vector3[n]; _normals = new Vector3[n]; _colors = new Color32[n]; _uvs = new Vector2[n];
            _tris = new int[(rows - 1) * (cols - 1) * 6];
            int ti = 0;
            for (int r = 0; r < rows - 1; r++)
                for (int c = 0; c < cols - 1; c++)
                {
                    int v0 = r * cols + c, v1 = v0 + 1, v2 = v0 + cols, v3 = v2 + 1;
                    _tris[ti++] = v0; _tris[ti++] = v2; _tris[ti++] = v1;
                    _tris[ti++] = v1; _tris[ti++] = v2; _tris[ti++] = v3;
                }
            _bufferRows = rows;
        }

        // Rows shared with the previous window are a pure function of their global index (one global
        // grid) — shift them in the buffers and compute only the rows that are new to this window.
        int shift = _bufferRow0 == int.MinValue ? -1 : rowIndex0 - _bufferRow0;
        int keep = !resized && shift >= 0 && shift < rows ? rows - shift : 0;
        if (keep > 0 && shift > 0)
        {
            System.Array.Copy(_verts,   shift * cols, _verts,   0, keep * cols);
            System.Array.Copy(_normals, shift * cols, _normals, 0, keep * cols);
            System.Array.Copy(_colors,  shift * cols, _colors,  0, keep * cols);
        }
        FillRows(rowIndex0, keep, rows, cols, scale, total);
        _bufferRow0 = rowIndex0;

        // UV.y is window-relative (0 = window start .. 1 = window end, see the MPB below).
        for (int r = 0; r < rows; r++)
        {
            float v = rows > 1 ? (float)r / (rows - 1) : 0f;
            for (int c = 0; c < cols; c++)
                _uvs[r * cols + c] = new Vector2(cols > 1 ? (float)c / (cols - 1) : 0.5f, v);
        }

        float startDistance = Mathf.Min(rowIndex0 * _rowSpacing, total);
        float endDistance   = Mathf.Min((rowIndex0 + rows - 1) * _rowSpacing, total);

        // Render mesh: updated in place (no GameObject/Mesh churn).
        if (resized) _groundMesh.Clear();
        _groundMesh.SetVertices(_verts);
        _groundMesh.SetNormals(_normals);
        _groundMesh.SetColors(_colors);
        _groundMesh.SetUVs(0, _uvs);
        if (resized) _groundMesh.SetTriangles(_tris, 0);
        _groundMesh.RecalculateBounds();

        // Collision: fill the buffer NOT currently used by the collider, bake it off the main thread
        // and swap it in when ready (sync for the first build / respawn, which need ground now).
        if (_pendingColliderMesh != null) ApplyPendingCollider();
        var colliderMesh = _colliderMeshes[_colliderBuffer] ??= new Mesh { indexFormat = IndexFormat.UInt32, name = "MusicPath_GroundCollider" };
        _colliderBuffer ^= 1;
        colliderMesh.Clear();
        colliderMesh.SetVertices(_verts);
        colliderMesh.SetTriangles(_tris, 0);
        if (syncCollider)
        {
            _groundMC.sharedMesh = colliderMesh;
        }
        else
        {
            _pendingColliderMesh = colliderMesh;
            _bakeHandle = new BakeColliderJob { MeshId = colliderMesh.GetInstanceID(), Options = _groundMC.cookingOptions }.Schedule();
        }

        // This window's own [start, end] music-distance range, via MaterialPropertyBlock — NOT a
        // cloned Material. Exactly the distances UV.y was built from, so the shader's
        // playhead01 = (playheadDistance - start) / (end - start) lines up with UV.y perfectly.
        _groundMPB ??= new MaterialPropertyBlock();
        _groundMPB.Clear();
        _groundMPB.SetFloat(StartMusicDistanceID, startDistance);
        _groundMPB.SetFloat(EndMusicDistanceID, endDistance);
        _groundMR.SetPropertyBlock(_groundMPB);
    }

    // Vertex position / colour / normal for buffer rows [fromRow, toRow) of the window starting at
    // global row rowIndex0 — everything comes from the global grid, so a row's data never depends
    // on which window it is in.
    private void FillRows(int rowIndex0, int fromRow, int toRow, int cols, float scale, float total)
    {
        float LateralFrac(int c) => cols > 1 ? (float)c / (cols - 1) : 0.5f;
        float HeightWorld(int globalRow, int c) =>
            _globalGrid[Mathf.Clamp(globalRow, 0, _globalRows - 1), Mathf.Clamp(c, 0, cols - 1)] * scale;

        for (int r = fromRow; r < toRow; r++)
        {
            int g = rowIndex0 + r;
            var s  = Path.GetSample(Mathf.Min(g * _rowSpacing, total));
            float hw = s.width * 0.5f;
            float colSpacing = cols > 1 ? s.width / (cols - 1) : 1f;

            for (int c = 0; c < cols; c++)
            {
                int v = r * cols + c;
                float lateralFrac = LateralFrac(c);
                float xOff = -hw + hw * 2f * lateralFrac;
                float norm = Mathf.Clamp01(_globalGrid[g, c]);

                _verts[v]  = s.position + s.right * xOff + s.up * (norm * scale);
                // COLOR reads the SAME processed value as HEIGHT (norm = smoothed + slope-clamped);
                // terrainColorRedThreshold is a pure COLOR remap (see TerrainVuColor).
                _colors[v] = TerrainVuColor(norm);

                // Analytic central-difference normal (not RecalculateNormals, which would differ at
                // window edges) — neighbours come from the global grid, so seams light identically.
                Vector3 tangentVec = (s.tangent * (2f * _rowSpacing) + s.up * (HeightWorld(g + 1, c) - HeightWorld(g - 1, c))).normalized;
                Vector3 rightVec   = (s.right   * (2f * colSpacing) + s.up * (HeightWorld(g, c + 1) - HeightWorld(g, c - 1))).normalized;
                Vector3 normal = Vector3.Cross(tangentVec, rightVec).normalized;
                if (Vector3.Dot(normal, s.up) < 0f) normal = -normal;
                _normals[v] = normal;
            }
        }
    }

    private void ApplyPendingCollider()
    {
        _bakeHandle.Complete();
        if (_groundMC != null && _pendingColliderMesh != null) _groundMC.sharedMesh = _pendingColliderMesh; // already baked → cheap
        _pendingColliderMesh = null;
    }

    // Custom URP shader: main-light diffuse + shadows (cast AND receive) multiplied by the
    // per-vertex musical colour, with a configurable ambient floor so shadow never reads as
    // pure black and kills the green/yellow/red gradient. Falls back to Sprites/Default
    // (built-in, always available, unlit but still vertex-colour-aware — no shadows) if the
    // custom shader isn't present, so the terrain is never invisible/white as a worst case.
    private Material VertexColorMaterial()
    {
        var shader = Shader.Find("MusicGame/VertexColorLit") ?? Shader.Find("Sprites/Default")
                     ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader) { color = Color.white };
        if (mat.HasProperty("_AmbientFloor"))
            mat.SetFloat("_AmbientFloor", Mathf.Clamp01(_config.levelGeneration.terrainAmbientFloor));
        return mat;
    }

    // Per-band reference over the whole song, so a column's height/colour reflects how
    // energetic that band is RIGHT NOW relative to its own norm — not raw magnitude (which
    // would always favour bass, since it naturally carries more energy in most music). This
    // stays true with a percentile reference exactly as it did with a mean — both are still
    // computed independently PER band, so a globally-quiet band is judged against its own
    // history, never against another band's.
    //
    // Uses the band's own Nth-percentile energy (default P90), NOT the mean. Energy envelopes
    // are heavily right-skewed (many quiet/typical frames, a few very loud transient peaks), so
    // the MEAN sits well above the "typical" frame — reaching 1.3x the mean (the old
    // FillSensitivity-adjusted threshold) turned out to be a rare event only the loudest
    // overall moments crossed, which is why the gradient was dominated by yellow (a mid
    // fraction of the mean) and red almost never appeared. A percentile is a real, song-wide
    // statistical fact about that specific band (not an arbitrary instant threshold): by
    // construction, roughly (1-percentile) of that band's own frames legitimately reach the
    // top of the gradient, regardless of how skewed its distribution is.
    private static float[] ComputeBandReferences(SongProfile profile, float percentile)
    {
        int n = profile.visualBandEnvelopes?.Length ?? 0;
        var refs = new float[n];
        percentile = Mathf.Clamp01(percentile);
        for (int b = 0; b < n; b++)
        {
            var band = profile.visualBandEnvelopes[b];
            if (band == null || band.Length == 0) continue;
            var sorted = (float[])band.Clone();
            System.Array.Sort(sorted);
            int idx = Mathf.Clamp(Mathf.FloorToInt(percentile * (sorted.Length - 1)), 0, sorted.Length - 1);
            refs[b] = sorted[idx];
        }
        return refs;
    }

    // Classic VU-meter gradient: low → mid → high, configurable in MusicRunnerLevelConfig.
    private Color VuColor(float t) => t < 0.5f
        ? Color.Lerp(_config.levelGeneration.lowEnergyColor, _config.levelGeneration.midEnergyColor, t * 2f)
        : Color.Lerp(_config.levelGeneration.midEnergyColor, _config.levelGeneration.highEnergyColor, (t - 0.5f) * 2f);

    // Terrain-only color remap — exaggerates how soon the palette reaches red, WITHOUT touching
    // the value used for height/geometry (that stays plain `norm`). Never used by the scanline's
    // FrequencyTexture (UpdateFrequencyTexture calls VuColor directly) — that one is meant to
    // stay a faithful, un-exaggerated equalizer reading.
    private Color TerrainVuColor(float processedValue) =>
        VuColor(Mathf.Clamp01(processedValue / Mathf.Max(0.05f, _config.levelGeneration.terrainColorRedThreshold)));

    // ── Debug HUD support ────────────────────────────────────────────────────────

    /// <summary>Raw band-ratio and normalized (0..1) frequency value at a distance/lateral
    /// fraction — for the debug HUD to display what's directly under the player.</summary>
    public float SampleFrequencyValue(float distance, float lateralFrac, out float normalized)
    {
        int numBands = _profile?.VisualBandCount ?? 0;
        if (_profile == null || _bandReference == null || numBands == 0) { normalized = 0f; return 0f; }

        float time = DistanceToSongTime(distance);
        int   b0   = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(lateralFrac) * numBands - 0.5f), 0, numBands - 1);
        float raw  = _bandReference[b0] > 0.0001f ? _profile.GetVisualBandEnergyAt(b0, time) / _bandReference[b0] : 0f;

        normalized = BlendedNormalized(lateralFrac, time, numBands);
        return raw;
    }

    /// <summary>
    /// THE single authoritative surface query, shared by mesh generation and collectible
    /// placement, so they can never disagree about where the ground actually is at a given
    /// (distance, lateralOffset). Deliberately skips the longitudinal/lateral smoothing passes
    /// and the slope-clamp (both are windowed-grid operations that don't apply to a one-off
    /// query) — a very close analytic approximation of the rendered mesh, since the slope
    /// clamp rarely engages given crossSlopeLimit/longitudinalSlopeLimit's headroom under
    /// CharacterController.slopeLimit. Callers needing a hard guarantee (e.g. "never below the
    /// mesh") should still add their own safety clearance on top.
    /// </summary>
    public float SampleSurfaceHeight(float distance, float lateralOffset)
    {
        int numBands = _profile?.VisualBandCount ?? 0;
        if (_profile == null || _bandReference == null || numBands == 0 || Path == null) return 0f;

        var   sample     = Path.GetSample(distance);
        float lateralFrac = LateralFracFromOffset(sample, lateralOffset);
        return ConservativeHeightAt(distance, lateralFrac, numBands) * Mathf.Max(0f, _config.levelGeneration.maxFrequencyHeight);
    }

    // The actual rendered mesh applies longitudinal smoothing (box blur) that this single-point
    // analytic query deliberately skips (see class doc) — meaning the smoothed mesh at a given
    // point can end up SLIGHTLY taller than the raw analytic value if neighbouring distances are
    // taller. Sampling a few neighbours within the smoothing radius and taking the max closes
    // that gap in the SAFE direction (never underestimates the real surface — floating a touch
    // high is harmless, embedding is not).
    private float ConservativeHeightAt(float distance, float lateralFrac, int numBands)
    {
        // Same radius formula RebuildGroundWindow derives from pathSmoothness — kept in sync so
        // this single-point query's safety margin always matches what the rendered mesh actually
        // does, regardless of where pathSmoothness is set.
        float radius = Mathf.Lerp(1f, 3f, Mathf.Clamp01(_config.levelGeneration.pathSmoothness));
        float best   = HeightAt(distance, lateralFrac, numBands);
        if (radius > 0.01f)
        {
            best = Mathf.Max(best, HeightAt(distance - radius * 0.5f, lateralFrac, numBands));
            best = Mathf.Max(best, HeightAt(distance + radius * 0.5f, lateralFrac, numBands));
            best = Mathf.Max(best, HeightAt(distance - radius, lateralFrac, numBands));
            best = Mathf.Max(best, HeightAt(distance + radius, lateralFrac, numBands));
        }
        return best;
    }

    private static float LateralFracFromOffset(MusicPath.Sample sample, float lateralOffset)
    {
        float halfWidth = sample.width * 0.5f;
        return halfWidth > 0.0001f
            ? Mathf.Clamp01((lateralOffset + halfWidth) / (halfWidth * 2f))
            : 0.5f;
    }

    /// <summary>
    /// THE single authoritative surface query used for collectible placement — position AND
    /// normal, so a tilted section of terrain (e.g. a strong bass bump) tilts the placement
    /// consistently with what's actually rendered there. Normal comes from the same analytic
    /// central-difference approach BuildMesh uses (see HeightAt's doc) — a very close match to
    /// the real rendered mesh, since the slope clamp rarely engages with the current headroom.
    /// </summary>
    public SurfaceSample SampleSurface(float distance, float lateralOffset)
    {
        int numBands = _profile?.VisualBandCount ?? 0;
        if (_profile == null || _bandReference == null || numBands == 0 || Path == null)
            return new SurfaceSample(Vector3.zero, Vector3.up, 0f);

        var   sample = Path.GetSample(distance);
        float lateralFrac = LateralFracFromOffset(sample, lateralOffset);
        float scale  = Mathf.Max(0f, _config.levelGeneration.maxFrequencyHeight);
        float height = ConservativeHeightAt(distance, lateralFrac, numBands) * scale;

        const float d = 0.5f; // finite-difference step, world units
        float hF = HeightAt(distance + d, lateralFrac, numBands) * scale;
        float hB = HeightAt(distance - d, lateralFrac, numBands) * scale;
        float halfWidth = sample.width * 0.5f;
        float dFrac = halfWidth > 0.0001f ? d / (halfWidth * 2f) : 0f;
        float hR = HeightAt(distance, Mathf.Clamp01(lateralFrac + dFrac), numBands) * scale;
        float hL = HeightAt(distance, Mathf.Clamp01(lateralFrac - dFrac), numBands) * scale;

        Vector3 tangentVec = (sample.tangent * (2f * d) + sample.up * (hF - hB)).normalized;
        Vector3 rightVec   = (sample.right   * (2f * d) + sample.up * (hR - hL)).normalized;
        Vector3 normal = Vector3.Cross(tangentVec, rightVec).normalized;
        if (Vector3.Dot(normal, sample.up) < 0f) normal = -normal;

        Vector3 position = sample.position + sample.right * lateralOffset + sample.up * height;
        return new SurfaceSample(position, normal, height);
    }
}
