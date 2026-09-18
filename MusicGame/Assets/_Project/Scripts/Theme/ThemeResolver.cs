using UnityEngine;

/// <summary>
/// Combines the three Theme layers (Base + optional "middle" override + optional Event override)
/// into one ResolvedTheme — per-category priority is always Event ?? Middle ?? Base (see
/// EventThemeSO's own doc: EventThemeMode is authoring intent, not a different algorithm here).
/// `styleLayer` is typed as the shared VisualOverrideLayer base because it can be EITHER a
/// MusicStyleVisualSO (a style has been detected) OR a FrontendVisualPresetSO (no song analyzed
/// yet — see ThemeManager) — never both at once; whichever is "currently active" is ThemeManager's
/// concern, not this method's. A missing/null override layer is completely normal — only a
/// missing/invalid BaseTheme (or one of ITS required categories) is an actual error, since
/// BaseTheme is supposed to be the always-complete fallback.
///
/// Stateless — a plain static method, not a module/service. Nothing here decides WHEN to
/// re-resolve or WHICH layers are currently active; that's ThemeManager's job.
/// </summary>
public static class ThemeResolver
{
    public static ResolvedTheme Resolve(BaseThemeSO baseTheme, VisualOverrideLayer styleLayer, EventThemeSO eventOverride)
    {
        if (baseTheme == null)
        {
            Debug.LogError("[ThemeResolver] No BaseTheme assigned — cannot resolve a valid CurrentTheme.");
            return null;
        }

        var resolved = new ResolvedTheme
        {
            UI           = eventOverride?.ui           ?? styleLayer?.ui           ?? baseTheme.ui,
            World        = eventOverride?.world        ?? styleLayer?.world        ?? baseTheme.world,
            Track        = eventOverride?.track        ?? styleLayer?.track        ?? baseTheme.track,
            Player       = eventOverride?.player       ?? styleLayer?.player       ?? baseTheme.player,
            Collectibles = eventOverride?.collectibles ?? styleLayer?.collectibles ?? baseTheme.collectibles,
            VFX          = eventOverride?.vfx          ?? styleLayer?.vfx          ?? baseTheme.vfx,
        };

        WarnIfMissing(nameof(resolved.UI), resolved.UI);
        WarnIfMissing(nameof(resolved.World), resolved.World);
        WarnIfMissing(nameof(resolved.Track), resolved.Track);
        WarnIfMissing(nameof(resolved.Player), resolved.Player);
        WarnIfMissing(nameof(resolved.Collectibles), resolved.Collectibles);
        WarnIfMissing(nameof(resolved.VFX), resolved.VFX);

        return resolved;
    }

    private static void WarnIfMissing(string category, Object value)
    {
        if (value == null)
            Debug.LogWarning($"[ThemeResolver] BaseTheme is missing its '{category}' category — " +
                              "BaseTheme is supposed to have every category filled in. Fix the " +
                              "BaseTheme asset; this is not something an override layer caused.");
    }
}
