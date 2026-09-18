/// <summary>
/// The final, merged visual configuration the rest of the game actually consumes — "CurrentTheme"
/// in the app-flow/Theme refactor plan's own terminology. Every field is guaranteed non-null (see
/// ThemeResolver — it can only ever be built by falling all the way back to BaseTheme's required
/// fields), so consumers never null-check this, and never need to know whether a given category
/// came from Base, a MusicStyle, or an Event override.
///
/// Plain C# class, not a ScriptableObject/MonoBehaviour: this is a RUNTIME VALUE (the resolver's
/// output), never an asset on disk and never something with its own Unity lifecycle.
/// </summary>
public class ResolvedTheme
{
    public UIStyleSO          UI;
    public WorldStyleSO       World;
    public TrackStyleSO       Track;
    public PlayerStyleSO      Player;
    public CollectibleStyleSO Collectibles;
    public VFXStyleSO         VFX;
}
