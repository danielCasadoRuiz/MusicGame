/// <summary>
/// One raw tag/score pair from the semantic music tagger, e.g. {"funk", 0.86}.
/// Kept as the model's own vocabulary and un-clamped sigmoid score — no genre/mood
/// simplification here (that only happens in the UI layer, see MusicTagUI).
/// </summary>
[System.Serializable]
public struct MusicTagScore
{
    public string tag;
    public float  score;

    public MusicTagScore(string tag, float score)
    {
        this.tag   = tag;
        this.score = score;
    }
}
