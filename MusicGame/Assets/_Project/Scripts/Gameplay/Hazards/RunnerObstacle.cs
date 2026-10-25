using System.Collections.Generic;
using UnityEngine;

public enum RunnerObstacleKind
{
    LowBlock,   // jump over it or dodge it sideways
    HighBlock,  // dodge only (taller than any normal jump)
    Portal,     // full-width frame: pass crouched under the beam
    WindowWall, // full-width wall with a central ELEVATED window: jump + crouch through it
    CrawlWall,  // full-width wall with an opening at ground level: pass crouched
    UShape,     // two tall side blocks with an open walkable corridor between them
}

/// <summary>
/// One obstacle, built from plain boxes: every solid part is its own child with a real (non-trigger)
/// BoxCollider + a cube mesh, so an opening is REALLY empty — no invisible collider ever covers it.
/// The dimensions are fields of the prefab (adjust them there); Build() lays the parts out for the local
/// track width at runtime. Local frame: x across the track (track centre = 0, +x = path right), y up from
/// the base (the HIGHEST ground point under the obstacle, so every opening keeps at least its height
/// everywhere), z along the path (centred). Solid parts reach groundSink below the base so uneven ground
/// never leaves a gap under them.
///
/// Default sizes come from the player (PlayerController.CapsuleHeight 1.7 m, radius 0.45 m,
/// MusicRunnerCoreConfig.crouchHeight 1.0 m, jump apex ≈ jumpForce²/2|g| = 1.84 m of the feet, air tuck lifts
/// the feet ≈ 0.49 m): see each field's tooltip; HazardLayoutTests checks them against the live config.
/// </summary>
public class RunnerObstacle : MonoBehaviour
{
    public RunnerObstacleKind kind;
    [SerializeField] private Material material;

    [Header("Common")]
    [Tooltip("Thickness along the path (m).")]
    [Min(0.2f)] public float depth = 0.8f;
    [Tooltip("How far solid parts reach below the base, so uneven ground never leaves a gap (m).")]
    [Min(0f)] public float groundSink = 1.0f;
    [Tooltip("Full-width walls/frames extend this far beyond each track edge (m).")]
    [Min(0f)] public float edgeOverhang = 0.6f;
    [Tooltip("Free width that must stay walkable next to a block (m): ≥ 2 × radius 0.45 + margin.")]
    [Min(1f)] public float minPassage = 2.2f;

    [Header("Blocks (LowBlock / HighBlock)")]
    [Tooltip("Block width as a fraction of the track width (clamped so minPassage stays free on one side).")]
    [Range(0.1f, 0.9f)] public float blockWidthFraction = 0.45f;
    [Tooltip("LowBlock top (m): above the 0.3 m step offset (can't be walked over), far below the 1.84 m jump apex.")]
    [Min(0.35f)] public float lowHeight = 0.7f;
    [Tooltip("HighBlock / U-shape / wall height (m): higher than the jump apex (1.84) + the capsule (1.7).")]
    [Min(2f)] public float highHeight = 3.8f;

    [Header("Portal")]
    [Tooltip("Free height under the beam (m): crouched 1.0 + tolerance fits; standing 1.7 does not.")]
    [Min(1f)] public float portalClearance = 1.35f;
    [Min(0.2f)] public float beamThickness = 0.6f;
    [Min(0.2f)] public float pillarWidth = 0.5f;

    [Header("Window wall")]
    [Tooltip("Window bottom (m): below the 1.84 m jump apex of the feet, above anything reachable from the ground.")]
    [Min(0.5f)] public float windowBottom = 1.66f;
    [Tooltip("Window height (m): < 1.7 so a standing body never fits; a tucked body (1.0 m, feet lifted) fits around the apex.")]
    [Min(1f)] public float windowHeight = 1.69f;
    [Min(1f)] public float windowWidth = 2.2f;

    [Header("Crawl wall")]
    [Tooltip("Ground-level opening height (m): crouched 1.0 + tolerance.")]
    [Min(1f)] public float crawlHeight = 1.35f;
    [Min(1f)] public float crawlWidth = 2.0f;

    [Header("U shape")]
    [Tooltip("Walkable corridor width between the two side blocks (m).")]
    [Min(1f)] public float corridorWidth = 2.4f;
    [Min(0.5f)] public float corridorLength = 2.5f;

    /// <summary>Length along the path this obstacle occupies.</summary>
    public float Length => kind == RunnerObstacleKind.UShape ? corridorLength : depth;

    /// <summary>The track width the opening (or the free side next to a block) needs.</summary>
    public float RequiredTrackWidth => kind switch
    {
        RunnerObstacleKind.LowBlock or RunnerObstacleKind.HighBlock => 1.2f + minPassage,
        RunnerObstacleKind.Portal     => 2f * pillarWidth + minPassage,
        RunnerObstacleKind.WindowWall => windowWidth + 1f,
        RunnerObstacleKind.CrawlWall  => crawlWidth + 1f,
        RunnerObstacleKind.UShape     => corridorWidth + 1f,
        _ => 4f,
    };

    /// <summary>Half the width of the opening / block centred on `lateral` — the layout keeps it on the track.</summary>
    public float HalfFeatureWidth(float trackWidth) => kind switch
    {
        RunnerObstacleKind.LowBlock or RunnerObstacleKind.HighBlock => BlockWidth(trackWidth) * 0.5f,
        RunnerObstacleKind.WindowWall => windowWidth * 0.5f,
        RunnerObstacleKind.CrawlWall  => crawlWidth * 0.5f,
        RunnerObstacleKind.UShape     => corridorWidth * 0.5f,
        _ => 0f,
    };

    public float BlockWidth(float trackWidth) =>
        Mathf.Clamp(blockWidthFraction * trackWidth, 1.2f, Mathf.Max(1.2f, trackWidth - minPassage));

    /// <summary>Where the opening is, in local space (for tests/debug): x centre, y bottom, width, height.</summary>
    public Rect OpeningRect { get; private set; }
    public float TrackWidth { get; private set; }
    public float Lateral { get; private set; }

    private readonly List<GameObject> _parts = new();
    private static Mesh s_cube;

    /// <summary>Lays out the solid parts for a track `trackWidth` wide, the block / opening centred on
    /// `lateral` (path-local, m). Reuses its children (pooled obstacles rebuild without allocation churn).</summary>
    public void Build(float trackWidth, float lateral)
    {
        TrackWidth = trackWidth;
        Lateral = lateral;
        int used = 0;
        float half = trackWidth * 0.5f, oh = edgeOverhang, s = -groundSink;
        float L = -half - oh, R = half + oh, z = Length;
        switch (kind)
        {
            case RunnerObstacleKind.LowBlock:
            case RunnerObstacleKind.HighBlock:
            {
                float w = BlockWidth(trackWidth), top = kind == RunnerObstacleKind.LowBlock ? lowHeight : highHeight;
                Part(ref used, lateral - w * 0.5f, lateral + w * 0.5f, s, top, z);
                OpeningRect = new Rect(lateral - w * 0.5f, 0f, w, top); // the block itself
                break;
            }
            case RunnerObstacleKind.Portal:
            {
                float beamTop = portalClearance + beamThickness;
                Part(ref used, L, -half + pillarWidth, s, beamTop, z);
                Part(ref used, half - pillarWidth, R, s, beamTop, z);
                Part(ref used, -half + pillarWidth, half - pillarWidth, portalClearance, beamTop, z);
                OpeningRect = new Rect(-half + pillarWidth, 0f, trackWidth - 2f * pillarWidth, portalClearance);
                break;
            }
            case RunnerObstacleKind.WindowWall:
            {
                float l = lateral - windowWidth * 0.5f, r = lateral + windowWidth * 0.5f, wb = windowBottom, wt = windowBottom + windowHeight;
                Part(ref used, L, l, s, highHeight, z);
                Part(ref used, r, R, s, highHeight, z);
                Part(ref used, l, r, s, wb, z);
                Part(ref used, l, r, wt, highHeight, z);
                OpeningRect = new Rect(l, wb, windowWidth, windowHeight);
                break;
            }
            case RunnerObstacleKind.CrawlWall:
            {
                float l = lateral - crawlWidth * 0.5f, r = lateral + crawlWidth * 0.5f;
                Part(ref used, L, l, s, highHeight, z);
                Part(ref used, r, R, s, highHeight, z);
                Part(ref used, l, r, crawlHeight, highHeight, z);
                OpeningRect = new Rect(l, 0f, crawlWidth, crawlHeight);
                break;
            }
            case RunnerObstacleKind.UShape:
            {
                float l = lateral - corridorWidth * 0.5f, r = lateral + corridorWidth * 0.5f;
                Part(ref used, L, l, s, highHeight, z);
                Part(ref used, r, R, s, highHeight, z);
                OpeningRect = new Rect(l, 0f, corridorWidth, highHeight);
                break;
            }
        }
        for (int i = used; i < _parts.Count; i++) _parts[i].SetActive(false);
    }

    // One solid box from x0..x1 (across), y0..y1 (up), centred along the path with length z.
    private void Part(ref int used, float x0, float x1, float y0, float y1, float z)
    {
        if (x1 - x0 < 0.01f || y1 - y0 < 0.01f) return;
        if (used >= _parts.Count)
        {
            var go = new GameObject("Part");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = CubeMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            go.AddComponent<BoxCollider>(); // unit cube collider, scaled with the part
            _parts.Add(go);
        }
        var p = _parts[used++];
        p.SetActive(true);
        p.name = $"Part{used}";
        p.transform.localPosition = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, 0f);
        p.transform.localRotation = Quaternion.identity;
        p.transform.localScale = new Vector3(x1 - x0, y1 - y0, z);
    }

    private static Mesh CubeMesh()
    {
        if (s_cube != null) return s_cube;
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        s_cube = tmp.GetComponent<MeshFilter>().sharedMesh;
        if (Application.isPlaying) Destroy(tmp); else DestroyImmediate(tmp);
        return s_cube;
    }

    /// <summary>Editor builder: drop children that were baked into the prefab for preview (they are rebuilt).</summary>
    public void AdoptExistingParts()
    {
        _parts.Clear();
        foreach (Transform c in transform) _parts.Add(c.gameObject);
    }

    private void Awake() => AdoptExistingParts();
}
