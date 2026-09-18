using UnityEngine;

/// <summary>
/// Shared shape for a PARTIAL visual override — every field optional (null = fall through to
/// whatever the resolver's next-lower layer provides). Extracted once a third concrete case
/// (FrontendVisualPresetSO) actually needed the exact same shape as MusicStyleVisualSO/EventThemeSO
/// — not built speculatively ahead of that (see those two classes' own history/doc).
///
/// Abstract: never used directly, only through a concrete layer (MusicStyleVisualSO, EventThemeSO,
/// FrontendVisualPresetSO), each of which adds its own identity (style id / event id / nothing).
/// </summary>
public abstract class VisualOverrideLayer : ScriptableObject
{
    public UIStyleSO          ui;
    public WorldStyleSO       world;
    public TrackStyleSO       track;
    public PlayerStyleSO      player;
    public CollectibleStyleSO collectibles;
    public VFXStyleSO         vfx;
}
