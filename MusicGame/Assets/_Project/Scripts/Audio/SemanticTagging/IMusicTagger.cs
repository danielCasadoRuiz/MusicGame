using System.Collections;

/// <summary>
/// Abstraction over "run semantic music tagging on this song's audio". The rest of the game
/// (AudioPreAnalyzer, SongProfile, UI) depends only on this interface and on MusicTagScore —
/// never directly on Sentis/ONNX/musicnn, so the model/runtime can change without touching
/// anything downstream. Mirrors IOfflineAnalyzer's role but is coroutine-based (not a plain
/// synchronous Analyze call) since ML preprocessing benefits from spreading work across frames.
/// </summary>
public interface IMusicTagger : System.IDisposable
{
    /// <summary>False if the model/runtime failed to load (missing asset, unsupported platform,
    /// etc.) — callers must check this before calling Tag() and simply skip tagging if false.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Runs tagging on a full song's mono audio (native sample rate — the tagger resamples
    /// internally to whatever rate its model needs). Invokes onComplete exactly once, with the
    /// tags sorted by descending score, before the coroutine finishes. Never yields mid-model-
    /// inference in a way that leaves partial/torn results visible to onComplete.
    /// </summary>
    IEnumerator Tag(float[] monoSamples, int sourceSampleRate, System.Action<MusicTagScore[]> onComplete);
}
