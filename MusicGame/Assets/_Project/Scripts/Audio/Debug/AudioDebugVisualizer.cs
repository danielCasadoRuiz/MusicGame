using System;
using UnityEngine;

public class AudioDebugVisualizer : MonoBehaviour
{
    private AudioAnalysisConfig _config;

    // Per-bar state
    private float[] _barRaw;
    private float[] _barSmoothed;
    private float[] _peakValues;
    private float[] _peakTimers;

    // Event flash
    private string _lastEvent     = "";
    private float  _lastEventTime = -99f;

    // Single white pixel — all colored rects drawn with GUI.color tint
    private Texture2D _whiteTex;

    // Cached delegates
    private Action<AudioDataReadyEvent>      _onData;
    private Action<PeakDetectedEvent>        _onPeak;
    private Action<OnsetDetectedEvent>       _onOnset;
    private Action<BeatDetectedEvent>        _onBeat;
    private Action<KickDetectedEvent>        _onKick;
    private Action<SnareDetectedEvent>       _onSnare;
    private Action<HiHatDetectedEvent>       _onHiHat;
    private Action<SongProfileReadyEvent>    _onProfile;
    private Action<PreAnalysisProgressEvent> _onProgress;

    // ─── Init ─────────────────────────────────────────────────────────────

    public void Initialize(AudioAnalysisConfig config)
    {
        _config   = config;
        _whiteTex = new Texture2D(1, 1);
        _whiteTex.SetPixel(0, 0, Color.white);
        _whiteTex.Apply();
        RebuildBars();
    }

    private void RebuildBars()
    {
        int n      = Mathf.Max(1, _config.visualizerBarCount);
        _barRaw    = new float[n];
        _barSmoothed = new float[n];
        _peakValues  = new float[n];
        _peakTimers  = new float[n];
    }

    // ─── EventBus subscriptions ───────────────────────────────────────────

    private void OnEnable()
    {
        _onData     = e => OnAudioData(e.Data.spectrum);
        _onPeak     = e => Flash($"PEAK  {e.Intensity:F4}");
        _onOnset    = e => Flash($"ONSET {e.Strength:F4}");
        _onBeat     = e => Flash($"BEAT  {e.Confidence:F2}");
        _onKick     = e => Flash($"KICK  {e.Intensity:F4}");
        _onSnare    = e => Flash($"SNARE {e.Intensity:F4}");
        _onHiHat    = e => Flash($"HIHAT {e.Intensity:F4}");
        _onProfile  = e => Flash($"BPM ~ {e.Profile.estimatedBPM:F0}");
        _onProgress = e => Flash($"SCAN  {e.Progress * 100f:F0}%");

        EventBus.Subscribe(_onData);
        EventBus.Subscribe(_onPeak);
        EventBus.Subscribe(_onOnset);
        EventBus.Subscribe(_onBeat);
        EventBus.Subscribe(_onKick);
        EventBus.Subscribe(_onSnare);
        EventBus.Subscribe(_onHiHat);
        EventBus.Subscribe(_onProfile);
        EventBus.Subscribe(_onProgress);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onData);
        EventBus.Unsubscribe(_onPeak);
        EventBus.Unsubscribe(_onOnset);
        EventBus.Unsubscribe(_onBeat);
        EventBus.Unsubscribe(_onKick);
        EventBus.Unsubscribe(_onSnare);
        EventBus.Unsubscribe(_onHiHat);
        EventBus.Unsubscribe(_onProfile);
        EventBus.Unsubscribe(_onProgress);
    }

    private void OnDestroy()
    {
        if (_whiteTex != null) Destroy(_whiteTex);
    }

    // ─── Data ─────────────────────────────────────────────────────────────

    private void OnAudioData(float[] spectrum)
    {
        if (_barRaw == null || _barRaw.Length != _config.visualizerBarCount)
            RebuildBars();

        int   n        = _barRaw.Length;
        int   sr       = AudioSettings.outputSampleRate;
        float minFreq  = 20f;
        float maxFreq  = Mathf.Min(20000f, sr * 0.5f);

        for (int i = 0; i < n; i++)
        {
            int lo, hi;
            if (_config.visualizerLogScale)
            {
                float fLo = minFreq * Mathf.Pow(maxFreq / minFreq, (float)i       / n);
                float fHi = minFreq * Mathf.Pow(maxFreq / minFreq, (float)(i + 1) / n);
                lo = Mathf.Clamp(FreqToBin(fLo, sr, spectrum.Length), 0, spectrum.Length - 1);
                hi = Mathf.Clamp(FreqToBin(fHi, sr, spectrum.Length), 0, spectrum.Length - 1);
            }
            else
            {
                lo = i       * spectrum.Length / n;
                hi = (i + 1) * spectrum.Length / n - 1;
            }
            hi = Mathf.Max(hi, lo);

            float sum = 0f;
            for (int b = lo; b <= hi; b++) sum += spectrum[b];
            _barRaw[i] = (sum / (hi - lo + 1)) * _config.visualizerSensitivity;
        }
    }

    private void Update()
    {
        if (_barSmoothed == null) return;

        float alpha = 1f - _config.visualizerSmoothing;

        for (int i = 0; i < _barSmoothed.Length; i++)
        {
            float target    = Mathf.Clamp01(_barRaw[i]);
            _barSmoothed[i] = Mathf.Lerp(_barSmoothed[i], target, alpha);

            if (_barSmoothed[i] >= _peakValues[i])
            {
                _peakValues[i] = _barSmoothed[i];
                _peakTimers[i] = _config.visualizerPeakHold;
            }
            else
            {
                _peakTimers[i] -= Time.deltaTime;
                if (_peakTimers[i] < 0f)
                {
                    float speed    = _config.visualizerPeakFall > 0f ? 1f / _config.visualizerPeakFall : 2f;
                    _peakValues[i] = Mathf.Max(_peakValues[i] - Time.deltaTime * speed, 0f);
                }
            }
        }
    }

    // ─── Render ───────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (_config == null || !_config.enableVisualizer) return;
        if (_barSmoothed == null || _whiteTex == null) return;

        float sw      = Screen.width;
        float sh      = Screen.height;
        float panelH  = _config.visualizerHeight;
        float panelY  = sh - panelH;
        float pad     = 8f;
        float gap     = 2f;
        int   n       = _barSmoothed.Length;
        float totalW  = sw - pad * 2f;
        float barW    = Mathf.Max(1f, (totalW - gap * (n - 1)) / n);
        float labelH  = 16f;
        float barAreaH = panelH - labelH - 6f;
        float barAreaY = panelY + 4f;

        // Background panel
        DrawRect(0, panelY, sw, panelH, new Color(0.03f, 0.03f, 0.03f, 0.94f));
        // Top border line
        DrawRect(0, panelY, sw, 1f, new Color(0.18f, 0.18f, 0.18f));

        // Bars
        for (int i = 0; i < n; i++)
        {
            float x = pad + i * (barW + gap);
            DrawBar(x, barAreaY, barW, barAreaH, _barSmoothed[i], _peakValues[i]);
        }

        // LED grid overlay (horizontal scan lines across all bars)
        DrawLEDGrid(pad, barAreaY, totalW, barAreaH);

        // Frequency labels
        DrawFreqLabels(pad, barAreaY + barAreaH + 2f, barW, gap, n);

        // Event flash (top-left of panel)
        float age  = Time.time - _lastEventTime;
        float fade = Mathf.Clamp01(1f - age / 0.7f);
        if (fade > 0f)
        {
            GUI.color = new Color(1f, 0.88f, 0.08f, fade);
            GUI.Label(new Rect(pad, panelY + 2f, 200f, 18f), _lastEvent);
            GUI.color = Color.white;
        }
    }

    private void DrawBar(float x, float areaY, float w, float h, float value, float peak)
    {
        float baseY   = areaY + h;
        float filledH = value * h;

        // Three colour zones drawn from bottom up, clipped to filledH
        float gZone = h * 0.60f;  // green:  0–60 %
        float yZone = h * 0.20f;  // yellow: 60–80 %
        float rZone = h * 0.20f;  // red:    80–100 %

        float rem = filledH;

        float gH = Mathf.Min(rem, gZone);
        if (gH > 0.5f) DrawRect(x, baseY - gH, w, gH, new Color(0.06f, 0.84f, 0.14f));
        rem -= gH;

        float yH = Mathf.Clamp(rem, 0f, yZone);
        if (yH > 0.5f) DrawRect(x, baseY - gZone - yH, w, yH, new Color(0.96f, 0.84f, 0.04f));
        rem -= yH;

        float rH = Mathf.Clamp(rem, 0f, rZone);
        if (rH > 0.5f) DrawRect(x, baseY - gZone - yZone - rH, w, rH, new Color(1f, 0.10f, 0.04f));

        // Peak hold line
        if (peak > 0.01f)
        {
            float peakY = baseY - peak * h - 1f;
            DrawRect(x, peakY, w, 2f, new Color(1f, 1f, 1f, 0.85f));
        }
    }

    private void DrawLEDGrid(float x, float y, float w, float h)
    {
        int   leds = Mathf.Max(2, _config.visualizerLEDCount);
        float segH = h / leds;
        var   col  = new Color(0.03f, 0.03f, 0.03f, 0.65f);
        for (int row = 0; row <= leds; row++)
            DrawRect(x, y + row * segH, w, 1f, col);
    }

    private void DrawFreqLabels(float areaX, float labelY, float barW, float gap, int n)
    {
        int   sr      = AudioSettings.outputSampleRate;
        float minFreq = 20f;
        float maxFreq = Mathf.Min(20000f, sr * 0.5f);

        // Show a label roughly every 1/6 of the bar range
        int step = Mathf.Max(1, n / 6);
        for (int i = 0; i < n; i += step)
        {
            float fc    = minFreq * Mathf.Pow(maxFreq / minFreq, (i + 0.5f) / n);
            string lbl  = fc >= 1000f ? $"{fc / 1000f:F0}k" : $"{fc:F0}";
            float  barX = areaX + i * (barW + gap);
            GUI.color   = new Color(0.38f, 0.38f, 0.38f);
            GUI.Label(new Rect(barX - 4f, labelY, barW + 20f, 16f), lbl);
        }
        GUI.color = Color.white;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private void DrawRect(float x, float y, float w, float h, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(new Rect(x, y, w, h), _whiteTex);
        GUI.color = Color.white;
    }

    private void Flash(string msg)
    {
        _lastEvent     = msg;
        _lastEventTime = Time.time;
        if (_config != null && _config.enableDebugLog)
            Debug.Log($"[Audio] {msg}");
    }

    private static int FreqToBin(float freq, int sampleRate, int spectrumLength)
        => Mathf.RoundToInt(freq * spectrumLength * 2f / sampleRate);
}
