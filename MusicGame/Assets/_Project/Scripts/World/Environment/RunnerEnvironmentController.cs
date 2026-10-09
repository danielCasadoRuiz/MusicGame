using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Owns the Runner's visual environment MODULES — the single owner of their creation/destruction.
/// Resolves, for every RunnerEnvironmentSlot, base environment + style:
///   Disabled → nothing;  Override → the style's prefab (missing prefab → warning, base prefab);
///   Inherit (or no style) → the base prefab.
/// Instantiates each resolved prefab ONCE under its own slot root, hands it a
/// RunnerEnvironmentContext (InitializeModule) and ticks it from HorizonWorld's deterministic
/// LateUpdate order (Tick). Rebuilding (a new run / another style) destroys the previous modules
/// first, so nothing accumulates.
///
///   [Runner Environment]          (under the Horizon World root — same anchor the modules always used)
///     HorizonRoot       → PF_HorizonMountains_*
///     MusicReactiveRoot → PF_SpectrumBars_*
///     (future: SkyRoot, FarSceneryRoot, MidSceneryRoot, NearSceneryRoot … — one per new slot)
/// Created by HorizonWorld (dynamic, like the rest of the Horizon World — no scene object).
/// </summary>
public class RunnerEnvironmentController : MonoBehaviour
{
    public static RunnerEnvironmentController Instance { get; private set; }

    private static readonly RunnerEnvironmentSlot[] Slots = (RunnerEnvironmentSlot[])System.Enum.GetValues(typeof(RunnerEnvironmentSlot));

    private readonly RunnerEnvironmentContext _context = new();
    private readonly List<IRunnerEnvironmentModule>[] _modules = new List<IRunnerEnvironmentModule>[Slots.Length];
    private readonly string[] _resolvedInfo = new string[Slots.Length];
    private Transform _root;
    // Quality-variant modules are Addressables: their handles live until the next Build/Clear.
    private readonly List<AsyncOperationHandle<GameObject>> _handles = new();
    private int _buildToken;

    public RunnerEnvironmentBaseSO BaseEnvironment { get; private set; }
    public RunnerEnvironmentStyleSO Style { get; private set; }
    /// <summary>Debug: "Horizon: PF_x (Inherit) | MusicReactive: none (Disabled)".</summary>
    public string Summary { get; private set; } = "";

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        Clear();
        if (Instance == this) Instance = null;
    }

    /// <summary>Resolves and instantiates every slot (destroying any previous build first).</summary>
    public void Build(RunnerEnvironmentBaseSO baseEnvironment, RunnerEnvironmentStyleSO style, Transform parent,
                      HorizonConfig config, HorizonWater water)
    {
        Clear();
        BaseEnvironment = baseEnvironment;
        Style = style;
        if (baseEnvironment == null)
            Debug.LogWarning("[RunnerEnvironment] No RunnerEnvironmentBaseSO (BaseTheme.runnerEnvironment) — only Override modules can load.");

        _root = new GameObject("[Runner Environment]").transform;
        _root.SetParent(parent, false);
        _context.Config = config;
        _context.Water = water;

        for (int i = 0; i < Slots.Length; i++)
        {
            var slot = Slots[i];
            var slotRoot = new GameObject($"{slot}Root").transform;
            slotRoot.SetParent(_root, false);
            _modules[i] = new List<IRunnerEnvironmentModule>();

            var source = ResolveSource(baseEnvironment, style, slot, out string how);
            if (source.IsEmpty) { _resolvedInfo[i] = $"{slot}: none ({how})"; continue; }

            if (source.prefab != null) { Spawn(i, slot, source.prefab, slotRoot, how); continue; }

            // Quality variant (Addressable, active Unity quality): instantiated once loaded.
            _resolvedInfo[i] = $"{slot}: loading {source.resolvedQuality} variant ({how})";
            int token = _buildToken, index = i;
            var handle = Addressables.LoadAssetAsync<GameObject>(source.reference);
            _handles.Add(handle);
            handle.Completed += h =>
            {
                if (token != _buildToken || slotRoot == null) return; // rebuilt / destroyed meanwhile
                if (h.Status != AsyncOperationStatus.Succeeded || h.Result == null)
                {
                    Debug.LogError($"[RunnerEnvironment] {slot}: failed to load the {source.resolvedQuality} variant ({how}).");
                    _resolvedInfo[index] = $"{slot}: load failed";
                    return;
                }
                Spawn(index, slot, h.Result, slotRoot, $"{how}, {source.resolvedQuality}");
                Summary = $"style '{(style != null ? style.name : "(none — base)")}' → {string.Join(" | ", _resolvedInfo)}";
            };
        }

        Summary = $"style '{(style != null ? style.name : "(none — base)")}' → {string.Join(" | ", _resolvedInfo)}";
        Debug.Log($"[RunnerEnvironment] {Summary}");
    }

    private void Spawn(int i, RunnerEnvironmentSlot slot, GameObject prefab, Transform slotRoot, string how)
    {
        var instance = Instantiate(prefab, slotRoot, false);
        instance.name = prefab.name;
        instance.GetComponentsInChildren(true, _modules[i]);
        if (_modules[i].Count == 0)
            Debug.LogWarning($"[RunnerEnvironment] '{prefab.name}' ({slot}) has no IRunnerEnvironmentModule — it is shown as static content only.");
        _context.ModuleRoot = instance.transform;
        foreach (var module in _modules[i]) module.InitializeModule(_context);
        _resolvedInfo[i] = $"{slot}: {prefab.name} ({how})";
    }

    /// <summary>The resolution rule + QUALITY: the slot's decided module (Disabled / style Override /
    /// base) becomes its active-quality Addressable variant when that module has quality variants
    /// (QualityAssetResolver, with fallback), else its direct prefab.</summary>
    public static EnvironmentModuleSource ResolveSource(RunnerEnvironmentBaseSO baseEnvironment, RunnerEnvironmentStyleSO style,
                                                        RunnerEnvironmentSlot slot, out string how)
    {
        var decision = style != null ? style.Get(slot) : null;
        var prefab = Resolve(baseEnvironment, style, slot, out how);
        if (how == "Disabled") return default;
        QualityAssetCollection quality = how == "Override"
            ? decision?.qualityPrefabs
            : baseEnvironment != null ? baseEnvironment.GetQualityPrefabs(slot) : null;
        if (quality != null && quality.HasAny)
        {
            var reference = QualityAssetResolver.Resolve(quality, out string q);
            if (reference != null) return new EnvironmentModuleSource { reference = reference, resolvedQuality = q };
        }
        return new EnvironmentModuleSource { prefab = prefab };
    }

    /// <summary>The resolution rule (see class doc). `how` explains the outcome for logs/debug.</summary>
    public static GameObject Resolve(RunnerEnvironmentBaseSO baseEnvironment, RunnerEnvironmentStyleSO style,
                                     RunnerEnvironmentSlot slot, out string how)
    {
        var decision = style != null ? style.Get(slot) : null;
        var basePrefab = baseEnvironment != null ? baseEnvironment.GetPrefab(slot) : null;
        var mode = decision != null ? decision.mode : EnvironmentOverrideMode.Inherit;
        switch (mode)
        {
            case EnvironmentOverrideMode.Disabled:
                how = "Disabled";
                return null;
            case EnvironmentOverrideMode.Override when decision.prefab != null || (decision.qualityPrefabs != null && decision.qualityPrefabs.HasAny):
                how = "Override";
                return decision.prefab;
            case EnvironmentOverrideMode.Override:
                Debug.LogWarning($"[RunnerEnvironment] Style '{style.name}' overrides {slot} but assigns no prefab — falling back to the base module.");
                how = "Override without prefab → base";
                return basePrefab;
            default:
                how = style != null ? "Inherit" : "Base";
                return basePrefab;
        }
    }

    /// <summary>Ticks one slot's modules (called by HorizonWorld in its fixed per-frame order).</summary>
    public void Tick(RunnerEnvironmentSlot slot, MusicWorldManager world, float songTime)
    {
        var modules = _modules[(int)slot];
        if (modules == null || modules.Count == 0) return;
        _context.World = world;
        _context.SongTime = songTime;
        for (int i = 0; i < modules.Count; i++) modules[i].TickModule(_context);
    }

    public bool HasModule(RunnerEnvironmentSlot slot) => _modules[(int)slot] != null && _modules[(int)slot].Count > 0;

    private void Clear()
    {
        _buildToken++;
        for (int i = 0; i < _modules.Length; i++) _modules[i]?.Clear();
        foreach (var h in _handles) if (h.IsValid()) Addressables.Release(h);
        _handles.Clear();
        if (_root != null) Destroy(_root.gameObject);
        _root = null;
        Summary = "";
    }
}
