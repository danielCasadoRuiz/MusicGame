using UnityEngine;

/// <summary>
/// Owns the Horizon Camera's procedural sky material (ProceduralSky.shader) — a per-camera Skybox
/// override, so it never touches RenderSettings.skybox / the gameplay Main Camera at all.
///
/// OWNERSHIP: this is the ONLY system that writes to the sky material's shader properties.
///
/// STATIC vs DYNAMIC: noise scale/strength and sun position/size/glow-size are pure art-direction
/// — applied once in ApplyStaticConfig() (Initialize, or every frame only if
/// HorizonConfig.devLiveConfigSync is on for live-tuning). All SIX of the sky's own colors —
/// Zenith/Upper/Lower/Horizon gradient, Horizon Glow Color AND Sun Color — plus Horizon Glow
/// Intensity ARE genuinely dynamic — see MACRO below — so those are resent every frame, via cached
/// property IDs, no allocation.
///
/// MACRO reactivity: reads MusicEnvironmentController.Instance.SmoothedMacroIntensity (a single
/// shared, slow-smoothed 0..1 driver computed once from profile intensity/buildup — see that
/// class's own doc), reshapes it (HorizonConfig.horizonSkyMacroResponseCurve — the raw driver
/// rarely nears 1.0, so a plain linear response left the sky barely moving), and Color.Lerps each
/// of the six sky colors from its BASE toward its own explicit *Intense color by that amount —
/// exactly the same plain-Lerp pattern Haze/Mountain/Water already use. (An earlier version
/// instead rotated all six through HSV hue space by a raw degree amount — technically "coordinated"
/// but able to wander through whatever hues happened to lie along the way with no way to see or
/// bound the result in advance; a straight Lerp toward an explicit, hand-picked color can never
/// leave the segment between the two colors actually chosen.) Never derives its own competing
/// smoothing/subscription — MusicEnvironmentController is the sole owner of "how intense is the
/// music right now".
/// </summary>
public class ProceduralSkyController : MonoBehaviour
{
    private static readonly int ZenithColorID  = Shader.PropertyToID("_ZenithColor");
    private static readonly int UpperColorID   = Shader.PropertyToID("_UpperColor");
    private static readonly int LowerColorID   = Shader.PropertyToID("_LowerColor");
    private static readonly int HorizonColorID = Shader.PropertyToID("_HorizonColor");
    private static readonly int NoiseScaleID    = Shader.PropertyToID("_NoiseScale");
    private static readonly int NoiseStrengthID = Shader.PropertyToID("_NoiseStrength");
    private static readonly int GlowColorID     = Shader.PropertyToID("_GlowColor");
    private static readonly int GlowIntensityID = Shader.PropertyToID("_GlowIntensity");
    private static readonly int SunColorID      = Shader.PropertyToID("_SunColor");
    private static readonly int SunDirectionID  = Shader.PropertyToID("_SunDirection");
    private static readonly int SunSizeID       = Shader.PropertyToID("_SunSize");
    private static readonly int SunGlowSizeID      = Shader.PropertyToID("_SunGlowSize");
    private static readonly int SunGlowIntensityID = Shader.PropertyToID("_SunGlowIntensity");

    private HorizonConfig _config;
    private Material      _material;

    public void Initialize(HorizonConfig config, Camera horizonCamera)
    {
        _config = config;

        var shader = Shader.Find("MusicGame/ProceduralSky");
        if (shader == null)
        {
            Debug.LogWarning("[ProceduralSkyController] 'MusicGame/ProceduralSky' shader not found — " +
                              "keeping the Horizon Camera's default skybox instead.");
            return;
        }
        _material = new Material(shader) { name = "Horizon_Sky" };

        var skybox = horizonCamera.gameObject.AddComponent<Skybox>();
        skybox.material = _material;

        ApplyStaticConfig();
        ApplyDynamic(0f);
    }

    public void Tick()
    {
        if (_config == null || _material == null) return;

        if (_config.devLiveConfigSync) ApplyStaticConfig();

        float intensity = MusicEnvironmentController.Instance != null
            ? MusicEnvironmentController.Instance.SmoothedMacroIntensity
            : 0f;
        ApplyDynamic(intensity);
    }

    /// <summary>The genuinely-dynamic slice: all six sky colors (Zenith/Upper/Lower/Horizon/Glow/
    /// Sun), each a plain Color.Lerp from its BASE to its own explicit *Intense color, scaled by
    /// the reshaped macro-intensity response, plus horizon glow intensity. Cheap, no allocation,
    /// so this runs every frame unconditionally — it's the one part of this shader that's SUPPOSED
    /// to change continuously.</summary>
    private void ApplyDynamic(float intensity)
    {
        // Reshape first (see horizonSkyMacroResponseCurve doc) — the raw driver rarely sits near
        // 1.0 in practice, so a plain linear response left the sky barely moving; this front-loads
        // the curve while keeping the 0→0 / 1→1 endpoints exact.
        float response = Mathf.Pow(Mathf.Clamp01(intensity), Mathf.Max(0.01f, _config.horizonSkyMacroResponseCurve));

        _material.SetColor(ZenithColorID,  Color.Lerp(_config.horizonSkyZenithColor,  _config.horizonSkyZenithColorIntense,  response));
        _material.SetColor(UpperColorID,   Color.Lerp(_config.horizonSkyUpperColor,   _config.horizonSkyUpperColorIntense,   response));
        _material.SetColor(LowerColorID,   Color.Lerp(_config.horizonSkyLowerColor,   _config.horizonSkyLowerColorIntense,   response));
        _material.SetColor(HorizonColorID, Color.Lerp(_config.horizonSkyHorizonColor, _config.horizonSkyHorizonColorIntense, response));
        _material.SetColor(GlowColorID,    Color.Lerp(_config.horizonGlowColor,       _config.horizonGlowColorIntense,       response));
        _material.SetColor(SunColorID,     Color.Lerp(_config.horizonSunColor,        _config.horizonSunColorIntense,        response));

        float glowMul = Mathf.Lerp(1f, Mathf.Max(0f, _config.horizonGlowIntensityIntenseMultiplier), response);
        _material.SetFloat(GlowIntensityID, Mathf.Max(0f, _config.horizonGlowIntensity) * glowMul);
    }

    /// <summary>Pure art-direction — never changes on its own, so applied once here (Initialize)
    /// and only re-applied on demand (HorizonConfig.devLiveConfigSync, for live-tuning in Play).</summary>
    public void ApplyStaticConfig()
    {
        _material.SetFloat(NoiseScaleID,    _config.horizonSkyNoiseScale);
        _material.SetFloat(NoiseStrengthID, _config.horizonSkyNoiseStrength);

        float az = _config.horizonSunAzimuthDeg * Mathf.Deg2Rad;
        float el = _config.horizonSunElevationDeg * Mathf.Deg2Rad;
        Vector3 sunDir = new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
        _material.SetVector(SunDirectionID, sunDir);
        _material.SetFloat(SunSizeID, _config.horizonSunSize);
        _material.SetFloat(SunGlowSizeID, _config.horizonSunGlowSize);
        _material.SetFloat(SunGlowIntensityID, _config.horizonSunGlowIntensity);
    }
}
