using UnityEngine;

/// <summary>
/// Ambient background-color mood driven by the same SongProfile analysis already used for
/// gameplay — NOT a per-frame FFT visualizer, and NOT a naive "note = color" lookup. A full
/// mix is polyphonic: chromaFlat is an energy DISTRIBUTION over the 12 pitch classes at each
/// moment, not a single detected note, so this projects that distribution's energy-weighted
/// centre onto a hue circle (WeightedHue) instead of asserting "this note = this color".
///
/// The circle uses CIRCLE-OF-FIFTHS order, not raw chromatic (semitone) order: harmonically
/// close material (e.g. a I-V progression, 7 semitones apart) needs to land close together in
/// hue, and a chromatic circle gets that backwards — two notes a semitone apart (maximally
/// dissonant) would be neighbours, while a fifth apart (the closest real harmonic relationship
/// after the octave) would sit nearly opposite. Circle-of-fifths order fixes that topology so
/// hue DISTANCE tracks harmonic distance, which is what "smooth musical change → smooth visual
/// change" actually requires.
///
/// The energy-weighted vector sum can also be genuinely small/directionless — a drum break or
/// noisy passage with no clear tonal centre. Rather than let a near-zero vector produce an
/// arbitrary/jittery angle, the target hue only UPDATES when the (smoothed) chroma's harmonic
/// clarity (vector magnitude, normalized 0..1) clears hueConfidenceThreshold; otherwise it
/// holds its last value. Saturation/value keep updating regardless (loudness is not ambiguous
/// the same way tonal center can be).
///
/// Two smoothing stages keep the result slow and musical instead of flickery:
///   1. Feature-level: chroma/intensity are sampled every colorSampleInterval seconds (NOT
///      every frame) and folded into an exponential moving average (chromaEMAAlpha).
///   2. Display-level: the actual displayed color chases that already-smoothed target with
///      its own multi-second time constant (colorSmoothingTimeConstant).
///
/// Buildup (profile.GetBuildupAt) intensifies saturation/value toward the target; an
/// Impact/Drop MacroEvent snaps partway to the target ("the transition culminates here").
/// Reads MusicClock.Instance.SongTime — never writes to it, and never touches
/// PlayerController/CameraFollow.
///
/// Created dynamically by GameplayManager, same pattern as every other subsystem — call
/// Initialize() after AddComponent.
/// </summary>
public class MusicEnvironmentController : MonoBehaviour
{
    private GameplayConfig _config;
    private SongProfile    _profile;

    private readonly float[] _chromaEMA = new float[12];
    private float _intensityEMA;
    private float _sampleTimer;
    private bool  _featuresInitialized;
    private float _lastBuildup;

    private float _targetHue;     // held across ticks where harmonic clarity is too low to trust
    private float _lastClarity;

    private Color _currentColor = Color.black;
    private Color _targetColor  = Color.black;

    public void Initialize(GameplayConfig config) => _config = config;

    // Debug-only readouts (see GameplayDebugHUD).
    public Color CurrentColor     => _currentColor;
    public Color TargetColor      => _targetColor;
    public float LastBuildupValue => _lastBuildup;
    public float LastClarity      => _lastClarity;
    public bool  HasProfile       => _profile != null;

    private System.Action<SongProfileReadyEvent>  _onProfile;
    private System.Action<MacroEventOccurredEvent> _onMacro;

    private void OnEnable()
    {
        _onProfile = e => _profile = e.Profile;
        _onMacro   = e =>
        {
            if (_config == null) return;
            if (e.Type == MacroEventType.Impact || e.Type == MacroEventType.Drop)
                _currentColor = Color.Lerp(_currentColor, _targetColor, _config.macroSnapFraction);
        };
        EventBus.Subscribe(_onProfile);
        EventBus.Subscribe(_onMacro);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onProfile);
        EventBus.Unsubscribe(_onMacro);
    }

    private void Update()
    {
        if (_config == null || !_config.enableMusicEnvironment || _profile == null) return;

        var clock = MusicClock.Instance;
        if (clock == null || !clock.IsRunning) return;

        _sampleTimer -= Time.deltaTime;
        if (_sampleTimer <= 0f)
        {
            _sampleTimer = Mathf.Max(0.05f, _config.colorSampleInterval);
            SampleFeatures(clock.SongTime);
        }

        float rate = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, _config.colorSmoothingTimeConstant));
        _currentColor = Color.Lerp(_currentColor, _targetColor, rate);

        var cam = Camera.main;
        if (cam != null) cam.backgroundColor = _currentColor;
    }

    private void SampleFeatures(float songTime)
    {
        var chroma = _profile.GetChromaAt(songTime);
        float alpha = Mathf.Clamp01(_config.chromaEMAAlpha);

        if (chroma != null)
        {
            if (!_featuresInitialized)
            {
                for (int i = 0; i < 12; i++) _chromaEMA[i] = chroma[i];
                _intensityEMA        = _profile.GetIntensityAt(songTime);
                _featuresInitialized = true;
            }
            else
            {
                for (int i = 0; i < 12; i++) _chromaEMA[i] = Mathf.Lerp(_chromaEMA[i], chroma[i], alpha);
                _intensityEMA = Mathf.Lerp(_intensityEMA, _profile.GetIntensityAt(songTime), alpha);
            }
        }

        // Only trust/update the hue when the chroma vector has a clear-enough resultant
        // direction (harmonic clarity) — otherwise hold the last target hue rather than let a
        // near-zero vector (drum break, noise, silence) produce an arbitrary angle.
        var (candidateHue, clarity) = WeightedHue(_chromaEMA);
        _lastClarity = clarity;
        if (clarity >= _config.hueConfidenceThreshold)
            _targetHue = candidateHue;

        _lastBuildup = _profile.GetBuildupAt(songTime);
        float boost  = Mathf.Lerp(1f, _config.buildupIntensityBoost, _lastBuildup);

        float sat = Mathf.Clamp01(_config.baseSaturation * boost);
        float val = Mathf.Clamp01((_config.baseValue + _intensityEMA * 0.25f) * boost);

        _targetColor = Color.HSVToRGB(_targetHue, sat, val);
    }

    // Pitch class → circle-of-fifths position (NOT chromatic/semitone order — see class doc):
    // angle(p) = ((7*p) mod 12) * 30°. Equivalent to walking the sequence C,G,D,A,E,B,F#,C#,
    // G#,D#,A#,F and placing each pitch class at its index in that sequence.
    private static float FifthsAngle(int pitchClass) => ((7 * pitchClass) % 12) / 12f * Mathf.PI * 2f;

    // Projects the 12-bin chroma ENERGY DISTRIBUTION onto the fifths-ordered hue circle and
    // takes the energy-weighted vector sum. Deliberately not a "detected note" lookup: a full
    // mix is polyphonic, so this just reads which tonal region currently carries the most
    // energy — the same underlying data HarmonyAnalyzer uses for estimatedKey, but sampled
    // continuously per-frame instead of once for the whole song. Returns (hue, clarity) where
    // clarity is the normalized vector magnitude (0 = energy spread evenly/no clear centre —
    // e.g. a drum break — 1 = all energy at a single pitch class).
    private static (float hue, float clarity) WeightedHue(float[] chroma)
    {
        float x = 0f, y = 0f, sum = 0f;
        for (int i = 0; i < 12; i++)
        {
            float angle = FifthsAngle(i);
            x   += chroma[i] * Mathf.Cos(angle);
            y   += chroma[i] * Mathf.Sin(angle);
            sum += chroma[i];
        }
        if (sum < 0.0001f) return (0f, 0f);

        float magnitude = Mathf.Sqrt(x * x + y * y);
        float clarity   = Mathf.Clamp01(magnitude / sum);
        if (magnitude < 0.0001f) return (0f, 0f);

        float a = Mathf.Atan2(y, x);
        if (a < 0f) a += Mathf.PI * 2f;
        return (a / (Mathf.PI * 2f), clarity);
    }
}
