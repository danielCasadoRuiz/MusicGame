using UnityEngine;

/// <summary>
/// Owns the Horizon Camera's procedural sky material (ProceduralSky.shader) — a per-camera Skybox
/// override, so it never touches RenderSettings.skybox / the gameplay Main Camera at all.
///
/// OWNERSHIP: this is the ONLY system that writes to the sky material's shader properties.
/// When HorizonWorld passes the Runner sun, its rotation follows the same sun vector;
/// previews without a supplied light keep their lighting unchanged.
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
    private static readonly int BelowColorID = Shader.PropertyToID("_BelowColor");
    private static readonly int ZenithFarID  = Shader.PropertyToID("_ZenithFar");
    private static readonly int UpperFarID   = Shader.PropertyToID("_UpperFar");
    private static readonly int LowerFarID   = Shader.PropertyToID("_LowerFar");
    private static readonly int HorizonFarID = Shader.PropertyToID("_HorizonFar");
    private static readonly int SunKeepRadiusID = Shader.PropertyToID("_SunKeepRadius");
    private static readonly int FarNearSunID    = Shader.PropertyToID("_FarNearSun");
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
    private Light _sunLight;
    private Quaternion _originalSunRotation, _appliedSunRotation;
    private bool _ownsSunRotation;

    private void OnDisable()
    {
        if (_ownsSunRotation && _sunLight != null &&
            Quaternion.Angle(_sunLight.transform.rotation, _appliedSunRotation) < .01f)
            _sunLight.transform.rotation = _originalSunRotation;
        _ownsSunRotation = false;
    }

    private void OnEnable()
    {
        if (_config != null && _material != null) ApplyStaticConfig();
    }

    public void Initialize(HorizonConfig config, Camera horizonCamera, Light sunLight = null)
    {
        _config = config;
        _sunLight = sunLight;

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
        _material.SetColor(BelowColorID, _config.horizonWaterColor);
        _material.SetColor(GlowColorID,    Color.Lerp(_config.horizonGlowColor,       _config.horizonGlowColorIntense,       response));
        _material.SetColor(SunColorID,     Color.Lerp(_config.horizonSunColor,        _config.horizonSunColorIntense,        response));

        var palette = HorizonPaletteDriver.Instance;
        if (palette != null && palette.HasPalette)
        {
            // Near the sun: the style's fixed sunset (Anchor). Far from it: the music-varied Frame
            // (the shader blends them by distance from the sun). The sun itself never changes.
            var a = palette.Anchor;
            var f = palette.Frame;
            _material.SetColor(ZenithColorID, a.zenith);
            _material.SetColor(UpperColorID, a.upper);
            _material.SetColor(LowerColorID, a.lower);
            _material.SetColor(HorizonColorID, a.horizon);
            _material.SetColor(ZenithFarID, f.zenith);
            _material.SetColor(UpperFarID, f.upper);
            _material.SetColor(LowerFarID, f.lower);
            _material.SetColor(HorizonFarID, f.horizon);
            _material.SetColor(BelowColorID, f.water * 0.5f);
            _material.SetFloat(SunKeepRadiusID, _config.horizonPaletteSunKeepDegrees * Mathf.Deg2Rad);
            _material.SetFloat(FarNearSunID, _config.horizonPaletteNearSunVariation);
            _material.SetColor(GlowColorID, f.glow);
            _material.SetColor(SunColorID, f.sun);
        }
        else
        {
            _material.SetColor(ZenithFarID, _material.GetColor(ZenithColorID));
            _material.SetColor(UpperFarID, _material.GetColor(UpperColorID));
            _material.SetColor(LowerFarID, _material.GetColor(LowerColorID));
            _material.SetColor(HorizonFarID, _material.GetColor(HorizonColorID));
        }

        float glowMul = Mathf.Lerp(1f, Mathf.Max(0f, _config.horizonGlowIntensityIntenseMultiplier), response);
        _material.SetFloat(GlowIntensityID, Mathf.Max(0f, _config.horizonGlowIntensity) * glowMul);
    }

    /// <summary>Pure art-direction — never changes on its own, so applied once here (Initialize)
    /// and only re-applied on demand (HorizonConfig.devLiveConfigSync, for live-tuning in Play).</summary>
    public void ApplyStaticConfig()
    {
        _material.SetFloat("_GradientStart", _config.horizonSkyGradientStart);
        _material.SetFloat("_GradientEnd", Mathf.Max(_config.horizonSkyGradientStart + 0.01f, _config.horizonSkyGradientEnd));
        _material.SetFloat(NoiseScaleID,    _config.horizonSkyNoiseScale);
        _material.SetFloat(NoiseStrengthID, _config.horizonSkyNoiseStrength);

        float az = _config.horizonSunAzimuthDeg * Mathf.Deg2Rad;
        float el = _config.horizonSunElevationDeg * Mathf.Deg2Rad;
        Vector3 sunDir = new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
        _material.SetVector(SunDirectionID, sunDir);
        // The sky vector points TOWARDS the sun; a directional light's forward points
        // along the incoming rays, AWAY from it. Both use this same world-space vector.
        if (_sunLight != null)
        {
            if (!_ownsSunRotation) _originalSunRotation = _sunLight.transform.rotation;
            Vector3 up = Mathf.Abs(Vector3.Dot(sunDir, Vector3.up)) > .999f ? Vector3.forward : Vector3.up;
            _appliedSunRotation = Quaternion.LookRotation(-sunDir, up);
            _sunLight.transform.rotation = _appliedSunRotation;
            _ownsSunRotation = true;
        }
        _material.SetFloat(SunSizeID, _config.horizonSunSize);
        _material.SetFloat(SunGlowSizeID, _config.horizonSunGlowSize);
        _material.SetFloat(SunGlowIntensityID, _config.horizonSunGlowIntensity);
    }
}
