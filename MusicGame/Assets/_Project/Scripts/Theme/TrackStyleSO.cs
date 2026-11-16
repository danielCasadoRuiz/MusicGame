using UnityEngine;

/// <summary>
/// Track presentation content resolved through Base / MusicStyle / Event themes.
/// Terrain variants are grouped by gameplay style because several gameplay styles can share
/// a legacy theme key (for example Chill and Dreamy both resolve to Ambient).
/// The match seed selects a flat alternative; the persisted assignment keeps retries stable and excludes the previous floor.
///
/// Side decoration is deliberately just a prefab + spacing for now — Section "Track Visual Theme"
/// explicitly asks for the extensible base, not a full procedural decoration framework. Whatever
/// places these must sample the real path/musicDistance representation, never `worldZ`.
/// </summary>
[CreateAssetMenu(fileName = "TrackStyle", menuName = "MusicGame/Theme/Track Style")]
public class TrackStyleSO : ScriptableObject
{
    public Material trackMaterial;
    [Tooltip("Single-texture classical destination reached at the finish arch. Uses the atlas cell selected by the material's first layer.")]
    public Material finishMaterial;

    [System.Serializable]
    public class TerrainSurfaceSet
    {
        public GameMusicStyle style;
        public Material[] variants = System.Array.Empty<Material>();
    }
    [Header("Runner terrain alternatives by musical style")]
    [Tooltip("Three or more alternatives per gameplay style. The saved match seed keeps the selection stable on retry and resume. Empty sets fall back to trackMaterial.")]
    public TerrainSurfaceSet[] terrainSurfaces = System.Array.Empty<TerrainSurfaceSet>();

    public Material ResolveTerrainMaterial(GameMusicStyle style, int matchSeed) => ResolveTerrainMaterial(style,matchSeed,null,null);

    // Flat choice among every non-null alternative. Saved ids survive array reordering.
    public Material ResolveTerrainMaterial(GameMusicStyle style,int matchSeed,string assignedId,string excludedId)
    {
        if(terrainSurfaces!=null)foreach(var set in terrainSurfaces)
        {
            if(set==null || set.style!=style || set.variants==null)continue;
            int count=0,eligible=0;
            foreach(var material in set.variants)
            {
                if(material==null)continue;
                if(!string.IsNullOrEmpty(assignedId) && material.name==assignedId)return material;
                count++;if(material.name!=excludedId)eligible++;
            }
            if(count==0)continue;
            bool exclude=eligible>0;int choices=exclude?eligible:count;
            uint hash=unchecked((uint)matchSeed ^ ((uint)style*0x9e3779b9u) ^ 0x51f15eadu);
            unchecked{hash^=hash>>16;hash*=0x7feb352du;hash^=hash>>15;hash*=0x846ca68bu;hash^=hash>>16;}
            int selected=(int)(hash%(uint)choices);
            foreach(var material in set.variants)
                if(material!=null && (!exclude || material.name!=excludedId) && selected--==0)return material;
        }
        return trackMaterial;
    }
    public Color    musicLineColor = Color.white;

    [Header("Side decoration (extension point — see class doc)")]
    public GameObject sideDecorationPrefab;
    public float       sideDecorationSpacing = 10f;
}
