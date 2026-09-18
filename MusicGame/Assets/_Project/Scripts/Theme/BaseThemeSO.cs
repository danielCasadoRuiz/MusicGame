using UnityEngine;

/// <summary>
/// The complete, always-valid fallback visual configuration — every field here is REQUIRED (see
/// ThemeResolver, which never needs a null-check against BaseTheme itself, only against the
/// override layers). The game must be able to run correctly on BaseTheme alone: no internet, no
/// remote Addressables, no MusicStyle/Event override resolved — this is what's on screen then.
///
/// Must ship locally with the build (never Addressable) — see the Addressables section of the
/// app-flow/Theme refactor plan for why (BaseTheme is the guaranteed-available fallback content,
/// not swappable/updatable content).
/// </summary>
[CreateAssetMenu(fileName = "BaseTheme", menuName = "MusicGame/Theme/Base Theme")]
public class BaseThemeSO : ScriptableObject
{
    public UIStyleSO          ui;
    public WorldStyleSO       world;
    public TrackStyleSO       track;
    public PlayerStyleSO      player;
    public CollectibleStyleSO collectibles;
    public VFXStyleSO         vfx;
}
