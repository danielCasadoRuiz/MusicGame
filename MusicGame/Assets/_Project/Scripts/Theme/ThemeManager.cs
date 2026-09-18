using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Owns CurrentTheme — the ONE piece of app-level state the Theme system exposes. Deliberately
/// thin: layer MERGING is ThemeResolver's job (stateless), Addressables load/release is
/// ThemeAssetLoader's job (reference-counted) — this class only decides WHEN to swap layers and
/// re-resolve, and publishes ThemeChanging/ThemeChangedEvent so the rest of the game never needs a
/// direct dependency on it to react to a Theme change.
///
/// The "middle" layer (see VisualOverrideLayer) is EITHER a random FrontendVisualPresetSO (no
/// song analyzed yet) OR the MusicStyleVisualSO for the currently-detected style — Section 15/16
/// of the app-flow/Theme refactor plan: these are mutually exclusive, never both active. There is
/// still no REAL Event activation system (nothing schedules "Christmas is live now") — `eventTheme`
/// only ever gets set via the Theme Debugger's DebugForceEvent/DebugClearEvent today, which is fine:
/// ThemeResolver already treats a null event override as "no event override" correctly.
///
/// TRANSITIONS: load the new content, apply it (Rebuild), THEN release the old content — never the
/// reverse (Section 17 of the plan). Rebuild() publishes ThemeChangingEvent with BOTH the old and
/// new resolved theme before committing CurrentTheme — every UI screen (ThemeReceiverBehaviour)
/// picks that up and interpolates its own colors over ThemeTransitionController's configurable
/// duration; this class itself stays completely unaware of that (it just publishes the two events,
/// same as always).
/// </summary>
public class ThemeManager : MonoBehaviour, IAppModule, IConfigurableModule<ThemeSystemConfigSO>
{
    public static ThemeManager Instance { get; private set; }

    public ResolvedTheme CurrentTheme { get; private set; }

    /// <summary>Exposed read-only so debug/preview tooling (see Tools > MusicGame > Theme Debugger)
    /// can enumerate the registries to build its pickers — nothing else should need this; regular
    /// gameplay code reacts to ThemeChangedEvent / reads CurrentTheme instead.</summary>
    public ThemeSystemConfigSO Config => _config;

    private ThemeSystemConfigSO _config;
    private AppContext          _context;

    private VisualOverrideLayer _currentStyleLayer;
    private EventThemeSO        _currentEventTheme;

    // Whichever of these is non-null tells us which kind of reference to release once a new
    // style layer replaces it — exactly one is ever non-null at a time.
    private AssetReferenceT<FrontendVisualPresetSO> _loadedFrontendRef;
    private AssetReferenceT<MusicStyleVisualSO>      _loadedStyleRef;

    // Independent of the style/frontend layer above (Base + Style + Event are three separate
    // layers merged by ThemeResolver) — never loaded by real gameplay yet (no Event
    // content/activation system exists), only exercised today via DebugForceEvent/DebugClearEvent.
    private AssetReferenceT<EventThemeSO> _loadedEventRef;

    private System.Action<MusicStyleDetectedEvent> _onMusicStyleDetected;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Configure(ThemeSystemConfigSO config) => _config = config;

    void IAppModule.Initialize(AppContext context)
    {
        _context = context;

        // CurrentTheme must never be null (Section 11 of the plan) — resolve pure BaseTheme
        // synchronously right now, before the random frontend preset's async Addressable load
        // even starts.
        Rebuild();

        PickRandomFrontendVisual();
    }

    void IAppModule.Shutdown()
    {
        if (_loadedFrontendRef != null) _context.ThemeAssets.ReleaseFrontendVisual(_loadedFrontendRef);
        if (_loadedStyleRef != null) _context.ThemeAssets.ReleaseMusicStyleVisual(_loadedStyleRef);
        if (_loadedEventRef != null) _context.ThemeAssets.ReleaseEventTheme(_loadedEventRef);
    }

    private void OnEnable()
    {
        _onMusicStyleDetected = e => OnMusicStyleDetected(e.Style);
        EventBus.Subscribe(_onMusicStyleDetected);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onMusicStyleDetected);
    }

    private void PickRandomFrontendVisual()
    {
        if (_config?.frontendVisualRegistry == null) return;
        if (!_config.frontendVisualRegistry.TryGetRandom(out var reference)) return;

        _context.ThemeAssets.LoadFrontendVisual(reference, preset =>
        {
            if (preset == null) return; // ThemeAssetLoader already warned
            SwapStyleLayer(preset, styleRef: null, frontendRef: reference);
        });
    }

    private void OnMusicStyleDetected(MusicStyleId style)
    {
        if (_config?.musicStyleRegistry == null) return;

        if (!_config.musicStyleRegistry.TryGetVisualReference(style, out var reference))
        {
            // No visual authored for this style yet — keep showing whatever's currently active
            // (most likely the random frontend preset) rather than snapping back to bare
            // BaseTheme just because this particular style has no content.
            return;
        }

        _context.ThemeAssets.LoadMusicStyleVisual(reference, visual =>
        {
            if (visual == null) return;
            SwapStyleLayer(visual, styleRef: reference, frontendRef: null);
        });
    }

    private void SwapStyleLayer(VisualOverrideLayer newLayer, AssetReferenceT<MusicStyleVisualSO> styleRef, AssetReferenceT<FrontendVisualPresetSO> frontendRef)
    {
        var oldFrontendRef = _loadedFrontendRef;
        var oldStyleRef     = _loadedStyleRef;

        _currentStyleLayer = newLayer;
        _loadedFrontendRef  = frontendRef;
        _loadedStyleRef     = styleRef;

        Rebuild();

        // Release the PREVIOUS layer only now that the new one is already applied above (load →
        // apply → release, never the reverse) — at most one of these two is ever non-null.
        if (oldFrontendRef != null) _context.ThemeAssets.ReleaseFrontendVisual(oldFrontendRef);
        if (oldStyleRef != null) _context.ThemeAssets.ReleaseMusicStyleVisual(oldStyleRef);
    }

    private void Rebuild()
    {
        var resolved = ThemeResolver.Resolve(_config != null ? _config.baseTheme : null, _currentStyleLayer, _currentEventTheme);
        if (resolved == null) return; // ThemeResolver already logged why (missing BaseTheme)

        // Published BEFORE committing CurrentTheme, with the OLD value still readable, so receivers
        // (ThemeReceiverBehaviour) know exactly what to interpolate FROM and TO (Section 4 of the
        // plan) — Old is null on the very first-ever resolve at boot, which receivers treat as
        // "nothing to transition from, just snap".
        EventBus.Publish(new ThemeChangingEvent { Old = CurrentTheme, New = resolved });

        CurrentTheme = resolved;
        EventBus.Publish(new ThemeChangedEvent { Theme = CurrentTheme });
    }

    private void SetEventTheme(EventThemeSO newEvent, AssetReferenceT<EventThemeSO> reference)
    {
        var oldRef = _loadedEventRef;

        _currentEventTheme = newEvent;
        _loadedEventRef     = reference;

        Rebuild();

        if (oldRef != null) _context.ThemeAssets.ReleaseEventTheme(oldRef);
    }

    // ── Debug/preview API (Tools > MusicGame > Theme Debugger) ──────────────────────────────────
    // Thin wrappers around the exact same paths real gameplay uses (OnMusicStyleDetected/
    // SwapStyleLayer/SetEventTheme/Rebuild) — no separate "debug" code path to drift out of sync
    // with the real one.

    /// <summary>Simulates a real Song Analysis style detection — same effect as MusicStyleDetectedEvent,
    /// without needing to actually analyze a song.</summary>
    public void DebugForceMusicStyle(MusicStyleId style) => OnMusicStyleDetected(style);

    /// <summary>Forces a specific Frontend Visual preset instead of the random pick Initialize() made.</summary>
    public void DebugForceFrontendVisual(AssetReferenceT<FrontendVisualPresetSO> reference)
    {
        if (_context == null || reference == null || !reference.RuntimeKeyIsValid()) return;

        _context.ThemeAssets.LoadFrontendVisual(reference, preset =>
        {
            if (preset == null) return; // ThemeAssetLoader already warned
            SwapStyleLayer(preset, styleRef: null, frontendRef: reference);
        });
    }

    /// <summary>Forces an Event override layer on top of whatever style/frontend layer is active —
    /// the first real exercise of this pathway (see class doc: no Event activation system exists
    /// yet).</summary>
    public void DebugForceEvent(AssetReferenceT<EventThemeSO> reference)
    {
        if (_context == null || reference == null || !reference.RuntimeKeyIsValid()) return;

        _context.ThemeAssets.LoadEventTheme(reference, evt =>
        {
            if (evt == null) return;
            SetEventTheme(evt, reference);
        });
    }

    /// <summary>Removes whatever Event override is currently forced, falling back to Base+Style only.</summary>
    public void DebugClearEvent()
    {
        if (_currentEventTheme == null && _loadedEventRef == null) return;
        SetEventTheme(null, null);
    }

    /// <summary>Re-resolves and republishes CurrentTheme from whatever layers are currently loaded —
    /// useful after editing BaseTheme/a style asset's fields in the Inspector during Play Mode.</summary>
    public void DebugReloadTheme() => Rebuild();
}
