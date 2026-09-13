using UnityEngine;

/// <summary>
/// Real distance fog for the GAMEPLAY world only (ground/circuit + rings/bonuses) — a different
/// concept from Horizon Haze (see GameplayConfig's own doc). Implemented via Unity's built-in
/// RenderSettings.fog (Linear mode): ring/bonus materials use the stock URP Lit shader, which
/// already blends RenderSettings.fog automatically (see LitForwardPass.hlsl), and
/// VertexColorLit.shader (the ground) now does the same blend explicitly.
///
/// Deliberately never affects the Horizon World: none of its shaders (HorizonBar, CheapWater,
/// ProceduralSky, HorizonMountainLayer) sample fog at all, so RenderSettings.fog being globally
/// "on" for the whole render has zero visual effect there regardless of this class's settings —
/// no per-object opt-out hack needed.
/// </summary>
public class GameplayFogController : MonoBehaviour
{
    private GameplayConfig _config;

    public void Initialize(GameplayConfig config) => _config = config;

    private void Update()
    {
        if (_config == null) return;

        RenderSettings.fog = _config.gameplayFogEnabled;
        if (!_config.gameplayFogEnabled) return;

        RenderSettings.fogMode  = FogMode.Linear;
        RenderSettings.fogColor = _config.gameplayFogColor;

        // "Strength" steepens/relaxes the falloff without re-tuning both distances by hand:
        // effectiveEnd is pulled closer to Start as Strength rises above 1 (steeper ramp), pushed
        // further out as it drops below 1 (gentler ramp) — 1 = exactly as authored.
        float strength = Mathf.Max(0.1f, _config.gameplayFogStrength);
        float start = _config.gameplayFogStartDistance;
        float end   = start + (_config.gameplayFogEndDistance - start) / strength;

        RenderSettings.fogStartDistance = start;
        RenderSettings.fogEndDistance   = Mathf.Max(start + 0.01f, end);
    }
}
