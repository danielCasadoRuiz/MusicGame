using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Prepares the coming fight in the BACKGROUND while the player is still on the runner's results panel: the
/// rival is already decided (GameSession.AssignedOpponent — the roulette only reveals it), so its avatar
/// parts (base body, hair, clothes), the player's avatar parts and the chosen arena are LOADED (Addressables
/// LoadAssetAsync — into memory, nothing instantiated, no scene work) on a low background-loading priority so
/// the results UI keeps its frame rate. When the Fight scene then builds the fighters and the arena, those
/// assets are already resident and the Versus loading card is short.
///
///   Prepare   on GameEndedEvent (runner song finished) when a rival is assigned; a new Prepare replaces the
///             previous one. The arena is CHOSEN here (ArenaSelector) and handed to the fight
///             (TakeArena) so what was preloaded is what loads.
///   Release   once the fight's stage is ready (VersusScreenController) or when leaving the fight — the
///             fight holds its own handles by then; ours only kept the assets alive in between.
/// Free runs with no assigned rival are not preloaded (the roulette picks at random).
/// </summary>
public static class FightPreloader
{
    private static readonly List<AsyncOperationHandle> s_handles = new();
    private static string s_arenaKey;
    private static AssetReferenceGameObject s_arena;
    private static int s_pending;
    private static ThreadPriority s_previousPriority = ThreadPriority.Normal;
    private static bool s_priorityLowered;
    private static System.Action<GameEndedEvent> s_onGameEnded;

    /// <summary>Number of preload operations still running (tests / diagnostics).</summary>
    public static int PendingLoads => s_pending;
    /// <summary>Assets currently kept resident for the coming fight (tests / diagnostics).</summary>
    public static int HeldAssets => s_handles.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        if (s_onGameEnded != null) return;
        s_onGameEnded = _ => PrepareAssignedFight();
        EventBus.Subscribe(s_onGameEnded);
    }

    /// <summary>The fight key FightSceneBootstrap.ApplyArenaEnvironment uses for the same rival/version/stage.</summary>
    public static string ArenaKey(OpponentDefinition opponent, int version, int stage) =>
        $"{(opponent != null ? opponent.id : "none")}|v{version}|s{stage}";

    public static void PrepareAssignedFight()
    {
        var session = GameSession.Instance;
        if (session == null || session.AssignedOpponent == null || session.AssignedOpponentLevelConfig == null) return;
        var opponent = session.AssignedOpponent;
        var level = session.AssignedOpponentLevelConfig;
        int stage = session.CurrentBossStage;

        Release();
        // Arena: chosen now, consumed by the fight (TakeArena) — the preloaded one is the one shown.
        var arena = stage >= 0 ? session.BossArena : ArenaSelector.Pick(opponent);
        if (arena != null && arena.RuntimeKeyIsValid())
        {
            s_arena = arena;
            s_arenaKey = ArenaKey(opponent, level.level, stage);
            Load(arena.RuntimeKey);
        }
        if (level.avatarRecipe != null)
            foreach (var r in AvatarFactory.CollectPrefabReferences(level.avatarRecipe.ToRuntime())) Load(r.RuntimeKey);
        var playerRecipe = PlayerAvatarConfigSO.ResolveRecipe(Resources.Load<AppConfigSO>("AppConfig"));
        if (playerRecipe != null)
            foreach (var r in AvatarFactory.CollectPrefabReferences(playerRecipe.ToRuntime())) Load(r.RuntimeKey);
        Debug.Log($"[FightPreloader] Preloading the fight vs {opponent.displayName} v{level.level}: {s_handles.Count} asset(s), arena {(s_arena != null ? s_arenaKey : "default")}.");
    }

    /// <summary>The arena chosen (and preloaded) for `key`, once; null if none was prepared for it.</summary>
    public static AssetReferenceGameObject TakeArena(string key)
    {
        if (s_arena == null || key != s_arenaKey) return null;
        var arena = s_arena;
        s_arena = null; s_arenaKey = null;
        return arena;
    }

    public static void Release()
    {
        foreach (var h in s_handles) if (h.IsValid()) Addressables.Release(h);
        s_handles.Clear();
        s_pending = 0;
        RestorePriority();
    }

    private static void Load(object key)
    {
        if (!s_priorityLowered)
        {
            s_previousPriority = Application.backgroundLoadingPriority;
            Application.backgroundLoadingPriority = ThreadPriority.Low; // results UI stays smooth
            s_priorityLowered = true;
        }
        var handle = Addressables.LoadAssetAsync<GameObject>(key);
        s_handles.Add(handle);
        s_pending++;
        handle.Completed += h =>
        {
            s_pending = Mathf.Max(0, s_pending - 1);
            if (h.Status != AsyncOperationStatus.Succeeded) Debug.LogWarning($"[FightPreloader] Could not preload '{key}' — the fight will load it itself.");
            if (s_pending == 0) RestorePriority();
        };
    }

    private static void RestorePriority()
    {
        if (!s_priorityLowered) return;
        Application.backgroundLoadingPriority = s_previousPriority;
        s_priorityLowered = false;
    }
}
