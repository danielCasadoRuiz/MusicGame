using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioPreAnalyzer : MonoBehaviour
{
    public IEnumerator Analyze(AudioClip clip, AudioAnalysisConfig config, System.Action<SongProfile> onComplete)
    {
        // Wait one frame so all OnEnable() subscriptions are registered before publishing any event
        yield return null;

        if (SongCache.TryLoad(clip, out var cached))
        {
            EventBus.Publish(new SongProfileReadyEvent { Profile = cached });
            onComplete?.Invoke(cached);
            yield break;
        }

        EventBus.Publish(new PreAnalysisStartedEvent());

        int sampleRate   = clip.frequency;
        int channels     = clip.channels;
        int totalSamples = clip.samples * channels;

        float[] raw  = new float[totalSamples];
        clip.GetData(raw, 0);

        float[] mono = MixToMono(raw, channels);

        int   windowSize     = Mathf.NextPowerOfTwo(config.spectrumSize);
        int   hopSize        = windowSize / 2;
        int   numFrames      = Mathf.Max(0, (mono.Length - windowSize) / hopSize);
        float hopTime        = (float)hopSize / sampleRate;
        int   numBands       = config.bands.Length;
        int   framesPerYield = 150;

        float[]   energyEnv   = new float[numFrames];
        float[]   spectralCen = new float[numFrames];
        float[]   spectralFlx = new float[numFrames];
        float[][] bandEnv     = new float[numBands][];
        for (int b = 0; b < numBands; b++) bandEnv[b] = new float[numFrames];

        float[] prevSpectrum = null;

        for (int f = 0; f < numFrames; f++)
        {
            float[] chunk = new float[windowSize];
            System.Array.Copy(mono, f * hopSize, chunk, 0, windowSize);

            // RMS energy
            float rms = 0f;
            for (int i = 0; i < windowSize; i++) rms += chunk[i] * chunk[i];
            energyEnv[f] = Mathf.Sqrt(rms / windowSize);

            // FFT
            float[] spectrum = OfflineFFT.Compute(chunk, FFTWindowType.BlackmanHarris);

            // Spectral centroid (brightness)
            float wSum = 0f, magSum = 0f;
            for (int i = 0; i < spectrum.Length; i++)
            {
                float freq = (float)i * sampleRate / windowSize;
                wSum   += freq * spectrum[i];
                magSum += spectrum[i];
            }
            spectralCen[f] = magSum > 0f ? wSum / magSum : 0f;

            // Spectral flux — half-wave rectified (onset strength)
            if (prevSpectrum != null)
            {
                float flux = 0f;
                for (int i = 0; i < spectrum.Length; i++)
                {
                    float d = spectrum[i] - prevSpectrum[i];
                    if (d > 0f) flux += d;
                }
                spectralFlx[f] = flux;
            }
            prevSpectrum = spectrum;

            // Per-band energy
            for (int b = 0; b < numBands; b++)
                bandEnv[b][f] = ComputeBandEnergy(spectrum, config.bands[b], sampleRate, windowSize);

            if (f % framesPerYield == 0)
            {
                EventBus.Publish(new PreAnalysisProgressEvent { Progress = (float)f / numFrames });
                yield return null;
            }
        }

        float avgEnergy = 0f, maxEnergy = 0f;
        for (int f = 0; f < numFrames; f++)
        {
            avgEnergy += energyEnv[f];
            if (energyEnv[f] > maxEnergy) maxEnergy = energyEnv[f];
        }
        avgEnergy /= Mathf.Max(numFrames, 1);

        float[]       onsets   = DetectOnsets(spectralFlx, hopTime);
        float         bpm      = EstimateBPM(onsets);
        SongSegment[] segments = SegmentSong(energyEnv, hopTime, avgEnergy, maxEnergy);

        var profile = new SongProfile
        {
            duration         = (float)mono.Length / sampleRate,
            sampleRate       = sampleRate,
            estimatedBPM     = bpm,
            averageEnergy    = avgEnergy,
            maxEnergy        = maxEnergy,
            analysisHopTime  = hopTime,
            energyEnvelope   = energyEnv,
            spectralCentroid = spectralCen,
            spectralFlux     = spectralFlx,
            onsetTimes       = onsets,
            bandEnvelopes    = bandEnv,
            segments         = segments,
        };

        SongCache.Save(clip, profile);
        EventBus.Publish(new SongProfileReadyEvent { Profile = profile });
        onComplete?.Invoke(profile);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private static float[] MixToMono(float[] samples, int channels)
    {
        if (channels == 1) return samples;
        int     len  = samples.Length / channels;
        float[] mono = new float[len];
        for (int i = 0; i < len; i++)
        {
            float s = 0f;
            for (int c = 0; c < channels; c++) s += samples[i * channels + c];
            mono[i] = s / channels;
        }
        return mono;
    }

    private static float ComputeBandEnergy(float[] spectrum, FrequencyBandConfig band, int sampleRate, int windowSize)
    {
        int lo = Mathf.Clamp(Mathf.RoundToInt(band.minHz * windowSize / sampleRate), 0, spectrum.Length - 1);
        int hi = Mathf.Clamp(Mathf.RoundToInt(band.maxHz * windowSize / sampleRate), 0, spectrum.Length - 1);
        if (hi <= lo) return spectrum[lo];
        float sum = 0f;
        for (int i = lo; i <= hi; i++) sum += spectrum[i];
        return sum / (hi - lo + 1);
    }

    private static float[] DetectOnsets(float[] flux, float hopTime)
    {
        int  halfWin = 21; // ~1 s local window at typical hop sizes
        var  onsets  = new List<float>();

        for (int i = 1; i < flux.Length - 1; i++)
        {
            int lo = Mathf.Max(0, i - halfWin);
            int hi = Mathf.Min(flux.Length - 1, i + halfWin);

            float mean = 0f;
            for (int j = lo; j <= hi; j++) mean += flux[j];
            mean /= (hi - lo + 1);

            float variance = 0f;
            for (int j = lo; j <= hi; j++) variance += (flux[j] - mean) * (flux[j] - mean);
            float std = Mathf.Sqrt(variance / (hi - lo + 1));

            // Local maximum above adaptive threshold
            if (flux[i] > mean + 1.5f * std && flux[i] > flux[i - 1] && flux[i] >= flux[i + 1])
                onsets.Add(i * hopTime);
        }
        return onsets.ToArray();
    }

    private static float EstimateBPM(float[] onsets)
    {
        if (onsets.Length < 4) return 120f;

        float minPeriod = 60f / 200f;
        float maxPeriod = 60f / 60f;
        int   bins      = 80;
        float binSize   = (maxPeriod - minPeriod) / bins;
        int[] histogram = new int[bins];

        for (int i = 0; i < onsets.Length - 1; i++)
        {
            float ioi = onsets[i + 1] - onsets[i];
            // Test IOI and its common multiples/divisors (half-beat, beat, double)
            for (float mult = 0.5f; mult <= 2.01f; mult += 0.5f)
            {
                float period = ioi * mult;
                if (period < minPeriod || period > maxPeriod) continue;
                int b = Mathf.Clamp(Mathf.FloorToInt((period - minPeriod) / binSize), 0, bins - 1);
                histogram[b]++;
            }
        }

        int peak = 0;
        for (int i = 1; i < bins; i++)
            if (histogram[i] > histogram[peak]) peak = i;

        float dominantPeriod = minPeriod + (peak + 0.5f) * binSize;
        return Mathf.Round(60f / dominantPeriod);
    }

    private static SongSegment[] SegmentSong(float[] energy, float hopTime, float avg, float max)
    {
        int minFrames = Mathf.Max(1, Mathf.RoundToInt(4f / hopTime));
        var segs      = new List<SongSegment>();

        for (int start = 0; start < energy.Length; start += minFrames)
        {
            int   end    = Mathf.Min(start + minFrames, energy.Length);
            float segAvg = 0f, segMax = 0f;
            for (int f = start; f < end; f++)
            {
                segAvg += energy[f];
                if (energy[f] > segMax) segMax = energy[f];
            }
            segAvg /= (end - start);

            segs.Add(new SongSegment
            {
                startTime     = start * hopTime,
                endTime       = end   * hopTime,
                averageEnergy = segAvg,
                maxEnergy     = segMax,
                level         = ClassifyLevel(segAvg, max),
            });
        }
        return segs.ToArray();
    }

    private static EnergyLevel ClassifyLevel(float energy, float max)
    {
        if (max <= 0f) return EnergyLevel.Low;
        float r = energy / max;
        if (r < 0.15f) return EnergyLevel.Low;
        if (r < 0.40f) return EnergyLevel.Mid;
        if (r < 0.70f) return EnergyLevel.High;
        return EnergyLevel.Drop;
    }
}
