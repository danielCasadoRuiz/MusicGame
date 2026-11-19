using System;
using UnityEngine;

// Keep the original stable identifiers; the other products were never released.
public enum MusicProduct { Jukebox = 0, CustomAudio = 3 }

/// <summary>Store integration seam. No production entitlement is granted by the placeholder UI.
/// A future billing adapter supplies verified ownership and implements purchasing/restoration.</summary>
public interface IMusicStore
{
    bool Owns(MusicProduct product);
    void Purchase(MusicProduct product);
    void Restore();
}

public static class MusicAccess
{
    private static IMusicStore s_store;
    public static IMusicStore Store
    {
        get => s_store;
        set { s_store = value; NotifyChanged(); }
    }
    public static event Action Changed;
    public static void NotifyChanged() => Changed?.Invoke();
    public static event Action<MusicProduct?> StoreRequested;
    public static bool Jukebox => Owns(MusicProduct.Jukebox);
    public static bool Background => Jukebox;
    public static bool CustomAudio => Owns(MusicProduct.CustomAudio);
    private static bool Owns(MusicProduct p) => Store?.Owns(p) == true;
    public static void Request(MusicProduct product)
    {
        StoreRequested?.Invoke(product);
    }
    public static void ConfirmPurchase(MusicProduct product)
    {
        Store?.Purchase(product);
        NotifyChanged();
    }
    public static void Restore()
    {
        if (Store != null) Store.Restore();
        else StoreRequested?.Invoke(null);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { s_store = null; StoreRequested = null; Changed = null; }
}
