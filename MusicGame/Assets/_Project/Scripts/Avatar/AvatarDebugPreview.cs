using UnityEngine;

/// <summary>
/// Drop this on any GameObject in a test scene, drag an AvatarRecipeSO into `recipe`, press Play, then
/// use Build/Rebuild/Release from this component's right-click context menu (the gear icon, or
/// right-click the component header, in the Inspector) — task's own explicit tooling ask: "un
/// AvatarDebugPreview on pugui arrossegar un AvatarRecipeSO i fer Build/Rebuild/Release", so assets
/// can be iterated on without wiring a real Fight/Runner flow every time.
///
/// Requires Play Mode — AvatarFactory.CreateAsync loads real Addressables, which only resolve during
/// Play (or via an Editor-only load path this class deliberately does NOT use, to stay a thin
/// consumer of the exact same runtime path Fight/Runner use, never a parallel preview-only pipeline).
/// </summary>
public class AvatarDebugPreview : MonoBehaviour
{
    public AvatarRecipeSO recipe;

    private AvatarInstance _instance;

    [ContextMenu("Build")]
    public void Build()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[AvatarDebugPreview] Build only works in Play Mode — AvatarFactory loads real Addressables.");
            return;
        }
        if (recipe == null)
        {
            Debug.LogWarning("[AvatarDebugPreview] No AvatarRecipeSO assigned.");
            return;
        }
        if (_instance != null)
        {
            Debug.LogWarning("[AvatarDebugPreview] Already built — use Rebuild to replace it.");
            return;
        }

        _ = BuildAsync();
    }

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        Release();
        Build();
    }

    [ContextMenu("Release")]
    public void Release()
    {
        _instance?.Dispose();
        _instance = null;
    }

    private async System.Threading.Tasks.Task BuildAsync()
    {
        var runtimeRecipe = recipe.ToRuntime();
        Debug.Log($"[AvatarDebugPreview] Building '{recipe.name}'...");
        _instance = await AvatarFactory.CreateAsync(runtimeRecipe, transform);
        Debug.Log(_instance?.Root != null
            ? $"[AvatarDebugPreview] Build complete — {_instance.EquippedItems.Count} item(s) equipped."
            : "[AvatarDebugPreview] Build FAILED — see errors above.");
    }

    private void OnDestroy() => Release();
}
