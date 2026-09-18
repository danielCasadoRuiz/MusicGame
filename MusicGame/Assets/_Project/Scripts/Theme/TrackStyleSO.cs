using UnityEngine;

/// <summary>
/// Track PRESENTATION content for one Theme layer — never track GENERATION (geometry/curves/
/// widths/gaps stay music-generated, see MusicWorldManager/SnakeWayWorldGenerator; a Theme only
/// dresses the result). `trackMaterial` maps directly onto MusicWorldManager.Initialize's existing
/// optional `pathMaterial` parameter (falls back to "MusicGame/VertexColorLit" today) — not wired
/// yet (later phase), just the matching data field.
///
/// Side decoration is deliberately just a prefab + spacing for now — Section "Track Visual Theme"
/// explicitly asks for the extensible base, not a full procedural decoration framework. Whatever
/// places these must sample the real path/musicDistance representation, never `worldZ`.
/// </summary>
[CreateAssetMenu(fileName = "TrackStyle", menuName = "MusicGame/Theme/Track Style")]
public class TrackStyleSO : ScriptableObject
{
    public Material trackMaterial;
    public Color    musicLineColor = Color.white;

    [Header("Side decoration (extension point — see class doc)")]
    public GameObject sideDecorationPrefab;
    public float       sideDecorationSpacing = 10f;
}
