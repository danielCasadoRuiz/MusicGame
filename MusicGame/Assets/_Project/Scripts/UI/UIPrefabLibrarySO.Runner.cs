using UnityEngine;

// Runner group: the LiveHud / EndScreen / PauseMenu screens are scene instances in Runner.unity (wired through
// its UIRegistry), so only the items they spawn per data entry live here.
public partial class UIPrefabLibrarySO
{
    [Header("Runner items")]
    [Tooltip("Prefabs/UI/Items/RunnerPerformanceRow.prefab — one row per ring type on the Runner end screen.")]
    public RunnerPerformanceRowView runnerPerformanceRow;
}
