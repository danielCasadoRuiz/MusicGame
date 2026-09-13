using UnityEngine;

/// <summary>
/// Owns the Horizon Camera's procedural sky material (ProceduralSky.shader) — a per-camera Skybox
/// override, so it never touches RenderSettings.skybox / the gameplay Main Camera at all.
///
/// MACRO reactivity (separate from the bars' fast MICRO response): profile.GetBuildupAt eases the
/// horizon glow intensity up over horizonMacroSmoothingTime seconds, and an Impact/Drop
/// MacroEventOccurredEvent snaps it partway toward the target (same "partway there" idea
/// MusicEnvironmentController.macroSnapFraction already uses for the background color) — always
/// slow/smoothed, never a per-beat flicker. Deliberately independent of MusicEnvironmentController
/// (which still drives Camera.backgroundColor as a fallback when Horizon World is disabled) rather
/// than reading its output directly, so this keeps working even if that system is ever disabled.
/// </summary>
public class ProceduralSkyController : MonoBehaviour
{
    private GameplayConfig _config;
    private SongProfile    _profile;
    private Material       _material;

    private float _glowBias;      // 0..~1 extra multiplier on horizonGlowIntensity, macro-smoothed
    private float _targetGlowBias;

    private System.Action<SongProfileReadyEvent>  _onProfile;
    private System.Action<MacroEventOccurredEvent> _onMacro;

    public void Initialize(GameplayConfig config, Camera horizonCamera)
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

        ApplyStaticValues();
    }

    private void OnEnable()
    {
        _onProfile = e => _profile = e.Profile;
        _onMacro   = e =>
        {
            if (_config == null || !_config.horizonMacroReactivity) return;
            if (e.Type == MacroEventType.Impact || e.Type == MacroEventType.Drop)
                _glowBias = Mathf.Lerp(_glowBias, _targetGlowBias, Mathf.Clamp01(_config.horizonMacroSnapFraction));
        };
        EventBus.Subscribe(_onProfile);
        EventBus.Subscribe(_onMacro);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onProfile);
        EventBus.Unsubscribe(_onMacro);
    }

    public void Tick()
    {
        if (_config == null || _material == null) return;

        if (_config.horizonMacroReactivity && _profile != null)
        {
            var clock = MusicClock.Instance;
            if (clock != null && clock.IsRunning)
            {
                _targetGlowBias = Mathf.Clamp01(_profile.GetBuildupAt(clock.SongTime));
                float rate = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, _config.horizonMacroSmoothingTime));
                _glowBias = Mathf.Lerp(_glowBias, _targetGlowBias, rate);
            }
        }
        else
        {
            _glowBias = 0f;
        }

        _material.SetFloat("_GlowIntensity", Mathf.Max(0f, _config.horizonGlowIntensity) * (1f + _glowBias));
        ApplyStaticValues();
    }

    private void ApplyStaticValues()
    {
        _material.SetColor("_ZenithColor",  _config.horizonSkyZenithColor);
        _material.SetColor("_UpperColor",   _config.horizonSkyUpperColor);
        _material.SetColor("_LowerColor",   _config.horizonSkyLowerColor);
        _material.SetColor("_HorizonColor", _config.horizonSkyHorizonColor);
        _material.SetFloat("_NoiseScale",    _config.horizonSkyNoiseScale);
        _material.SetFloat("_NoiseStrength", _config.horizonSkyNoiseStrength);
        _material.SetColor("_GlowColor", _config.horizonGlowColor);
        _material.SetColor("_SunColor",  _config.horizonSunColor);

        float az = _config.horizonSunAzimuthDeg * Mathf.Deg2Rad;
        float el = _config.horizonSunElevationDeg * Mathf.Deg2Rad;
        Vector3 sunDir = new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
        _material.SetVector("_SunDirection", sunDir);
        _material.SetFloat("_SunSize", _config.horizonSunSize);
        _material.SetFloat("_SunGlowSize", _config.horizonSunGlowSize);
        _material.SetFloat("_SunGlowIntensity", _config.horizonSunGlowIntensity);
    }
}
