using System;
using UnityEngine;

/// <summary>
/// Abstraction over "show the player a rewarded ad, then tell me if they earned the reward" — the
/// seam a real ad SDK (AdMob, IronSource, etc.) will implement later, once one actually exists.
/// MatchResultController's Rewarded Ad confirmation modal (see its own doc) depends only on this
/// interface, never on any concrete ad technology.
/// </summary>
public interface IRewardedAdService
{
    /// <summary>Invoked exactly once with true if the reward should be granted (ad watched fully),
    /// false otherwise (not implemented, failed, or skipped/closed early). Never throws.</summary>
    void ShowRewardedAd(Action<bool> onComplete);
}

/// <summary>
/// The only IRewardedAdService today — no ad SDK exists yet (task's own explicit "NO connectis cap
/// servei real" requirement). Always reports failure in a real build — never silently grants a
/// reward (task's own explicit "NO vull un Fake Ad automàtic que doni la vida silenciosament en
/// producció" requirement). In the EDITOR only, an explicit opt-in debug toggle (default OFF, flipped
/// via FightDebugHUD) lets a developer simulate a successful watch, to test the "+1 life -> Fight
/// Again" flow without a real ad ever existing.
/// </summary>
public class NotImplementedRewardedAdService : IRewardedAdService
{
#if UNITY_EDITOR
    /// <summary>EDITOR-ONLY — never compiled into a real build. Off by default so simply opening the
    /// Editor never grants a free life; a developer must explicitly flip this via FightDebugHUD.</summary>
    public static bool DebugSimulateSuccess = false;
#endif

    public void ShowRewardedAd(Action<bool> onComplete)
    {
#if UNITY_EDITOR
        if (DebugSimulateSuccess)
        {
            Debug.Log("[NotImplementedRewardedAdService] EDITOR DEBUG: simulating a successful Rewarded Ad watch.");
            onComplete?.Invoke(true);
            return;
        }
#endif
        Debug.Log("[NotImplementedRewardedAdService] Rewarded Ad not implemented — no reward granted.");
        onComplete?.Invoke(false);
    }
}
