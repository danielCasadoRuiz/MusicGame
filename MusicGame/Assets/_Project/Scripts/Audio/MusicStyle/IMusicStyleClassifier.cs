/// <summary>
/// SongProfile → MusicStyleId. The rest of the game depends only on this interface, never on the
/// underlying tag vocabulary/model (SentisMusicTagger, MusicTagScore) — so the real classification
/// strategy can improve later (a dedicated genre model, a mixed-style/confidence result, etc.)
/// without touching any consumer (Theme resolution, UI, debug tools).
///
/// Deliberately synchronous: by the time this runs, SongProfile (and its MusicTagScore[], if
/// semantic tagging was enabled) is already fully computed — classification is a cheap lookup over
/// already-produced data, not a new analysis pass.
/// </summary>
public interface IMusicStyleClassifier
{
    MusicStyleId Classify(SongProfile profile);
}
