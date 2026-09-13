using UnityEngine;

/// <summary>
/// Owns the Horizon Camera's procedural sky material (ProceduralSky.shader) — a per-camera Skybox
/// override, so it never touches RenderSettings.skybox / the gameplay Main Camera at all.
///
/// OWNERSHIP: this is the ONLY system that writes to the sky material's shader properties.
///
/// STATIC vs DYNAMIC: noise scale/strength, sun position/size/glow-size and the sun COLOR are
/// pure art-direction — applied once in ApplyStaticConfig() (Initialize, or every frame only if
/// HorizonConfig.devLiveConfigSync is on for live-tuning). The sky's four gradient colors + glow
/// intensity ARE genuinely dynamic — see MACRO below — so those alone are resent every frame,
/// via cached property IDs, no allocation.
///
/// MACRO reactivity: reads MusicEnvironmentController.Instance.SmoothedMacroIntensity (a single
/// shared, slow-smoothed 0..1 driver computed once from profile intensity/buildup — see that
/// class's own doc) and blends each of its own BASE colors toward HorizonConfig's matching
/// *Intense color by that amount — FinalColor = Lerp(base, intense, smoothedIntensity). Never
/// derives its own competing smoothing/subscription — MusicEnvironmentController is the sole
/// owner of "how intense is the music right now".
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

    /// <summary>The genuinely-dynamic slice: the 4 gradient colors + glow intensity, blended
    /// BASE→Intense by the shared macro-intensity driver. Cheap (a handful of Color.Lerp calls),
    /// so this runs every frame unconditionally — it's the one part of this shader that's
    /// SUPPOSED to change continuously.</summary>
    private void ApplyDynamic(float intensity)
    {
        _material.SetColor(ZenithColorID,  Color.Lerp(_config.horizonSkyZenithColor,  _config.horizonSkyZenithColorIntense,  intensity));
        _material.SetColor(UpperColorID,   Color.Lerp(_config.horizonSkyUpperColor,   _config.horizonSkyUpperColorIntense,   intensity));
        _material.SetColor(LowerColorID,   Color.Lerp(_config.horizonSkyLowerColor,   _config.horizonSkyLowerColorIntense,   intensity));
        _material.SetColor(HorizonColorID, Color.Lerp(_config.horizonSkyHorizonColor, _config.horizonSkyHorizonColorIntense, intensity));

        float glowMul = Mathf.Lerp(1f, Mathf.Max(0f, _config.horizonGlowIntensityIntenseMultiplier), intensity);
        _material.SetFloat(GlowIntensityID, Mathf.Max(0f, _config.horizonGlowIntensity) * glowMul);
    }

    /// <summary>Pure art-direction — never changes on its own, so applied once here (Initialize)
    /// and only re-applied on demand (HorizonConfig.devLiveConfigSync, for live-tuning in Play).</summary>
    public void ApplyStaticConfig()
    {
        _material.SetFloat(NoiseScaleID,    _config.horizonSkyNoiseScale);
        _material.SetFloat(NoiseStrengthID, _config.horizonSkyNoiseStrength);
        _material.SetColor(GlowColorID, _config.horizonGlowColor);
        _material.SetColor(SunColorID,  _config.horizonSunColor);

        float az = _config.horizonSunAzimuthDeg * Mathf.Deg2Rad;
        float el = _config.horizonSunElevationDeg * Mathf.Deg2Rad;
        Vector3 sunDir = new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
        _material.SetVector(SunDirectionID, sunDir);
        _material.SetFloat(SunSizeID, _config.horizonSunSize);
        _material.SetFloat(SunGlowSizeID, _config.horizonSunGlowSize);
        _material.SetFloat(SunGlowIntensityID, _config.horizonSunGlowIntensity);
    }
}
