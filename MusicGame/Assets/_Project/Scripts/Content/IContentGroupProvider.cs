/// <summary>An asset that knows which logical download group (Addressables label, see ContentKeys)
/// its quality variants belong to — used by the Editor content setup to label/pack them.</summary>
public interface IContentGroupProvider
{
    string ContentGroupOrDefault { get; }
}
