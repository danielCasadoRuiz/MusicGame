using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioPreAnalyzer : MonoBehaviour
{
    // Purely cosmetic — a cache hit resolves in well under a frame, which would make the
    // Analyzing screen flash on/off invisibly fast (or never even appear, since
    // PreAnalysisStartedEvent used to not fire at all on a hit). Holding here for a beat gives
    // the flow the same "something is happening" feel as a real analysis. onTagsReady (and the
    // MusicStyleDetectedEvent/theme swap it triggers, via SongAnalysisController) already fires
    // BEFORE this wait even starts, so the theme is already changing while this plays out —
    // AnalyzingScreenController's own progress-bar animation during this window is a self-paced
    // simulation (there's no real per-frame signal for a cache hit), not hard-synced to this exact
    // duration.
    private const float CacheHitFakeDelaySeconds = 2f;

    // `onTagsReady` fires as soon as the semantic-tagging pass (genre/mood ML classification) knows
    // this song's tags — genuinely BEFORE the per-frame FFT loop below even starts (cache miss), or
    // essentially immediately (cache hit, tags already cached) — so SongAnalysisController can
    // classify the music style and swap the theme early, while the rest of the (level-generation-
    // critical) analysis keeps going in the background. Never fires if semantic tagging is
    // disabled (config.advancedEnabled/advancedSemanticTagging) — callers must tolerate that and
    // fall back to classifying from the final SongProfile instead (see SongAnalysisController).
    public IEnumerator Analyze(AudioClip clip, AudioAnalysisConfig config, System.Action<SongProfile> onComplete,
        System.Action<MusicTagScore[]> onTagsReady = null)
    {
        // Wait one frame so all OnEnable() subscriptions are registered before publishing any event
        yield return null;

        // A cache saved before visualBandEnvelopes existed won't have it — fall through to a
        // full re-analysis rather than silently shipping a flat, bandless ground mesh. Same
        // reasoning for chromaFlat: a cache saved with advancedHarmony off (or from before this
        // feature existed) has it null, and there's no cheap way to derive chroma after the fact
        // (needs the raw per-frame spectrum from the main FFT loop below, not just cached band
        // envelopes) — so if harmony is wanted now but the cache doesn't have it, this must also
        // fall through to a full re-analysis instead of silently running MusicEnvironmentController
        // forever with an empty chroma vector (frozen hue, only saturation/value ever moving).
        bool wantsChroma = config.advancedEnabled && config.advancedHarmony;
        if (SongCache.TryLoad(clip, out var cached) && cached.VisualBandCount > 0 &&
            (!wantsChroma || (cached.chromaFlat != null && cached.chromaFlat.Length > 0)))
        {
            // Derived features are always recomputed (fast, < 1 s) so config changes take effect
            RunOfflineAnalyzers(cached, config);

            // Semantic tags ARE cached (ML inference isn't "fast"), so on a normal cache hit we
            // do NOT re-run them — only if tagging just got enabled or the model/preprocessing
            // changed (ModelVersion bump) do we re-tag, without discarding anything else cached.
            bool needsRetag = config.advancedEnabled && config.advancedSemanticTagging &&
                              (!cached.HasMusicTags || cached.musicTagModelVersion != SentisMusicTagger.ModelVersion);
            if (needsRetag)
            {
                float[] monoForTag = MixToMono(GetClipData(clip), clip.channels);
                yield return RunSemanticTagging(monoForTag, clip.frequency, cached, config);
                SongCache.Save(clip, cached);
            }

            // IsCacheHit must be known to listeners (AnalyzingScreenController) BEFORE onTagsReady's
            // MusicStyleDetectedEvent below — that's what tells them whether to expect real
            // PreAnalysisProgressEvent ticks afterward (cache miss) or not (cache hit, none ever
            // come).
            EventBus.Publish(new PreAnalysisStartedEvent { IsCacheHit = true });
            onTagsReady?.Invoke(cached.musicTags);

            yield return new WaitForSeconds(CacheHitFakeDelaySeconds);

            EventBus.Publish(new SongProfileReadyEvent { Profile = cached });
            onComplete?.Invoke(cached);
            yield break;
        }

        EventBus.Publish(new PreAnalysisStartedEvent());

        int sampleRate   = clip.frequency;
        int channels     = clip.channels;

        float[] raw  = GetClipData(clip);

        float[] mono = MixToMono(raw, channels);

        // Semantic tagging (genre/mood ML pass) runs FIRST — it only needs this raw mono buffer,
        // nothing the per-frame FFT loop below produces — so style detection (and the theme swap
        // it triggers, via SongAnalysisController's onTagsReady) happens as early as possible,
        // with the detailed per-frame analysis (needed for level generation) continuing afterward
        // instead of only ever becoming known once EVERYTHING else has also finished.
        MusicTagScore[] earlyTags = null;
        int             earlyTagModelVersion = 0;
        if (config.advancedEnabled && config.advancedSemanticTagging)
        {
            var tagCarrier = new SongProfile();
            yield return RunSemanticTagging(mono, sampleRate, tagCarrier, config);
            earlyTags            = tagCarrier.musicTags;
            earlyTagModelVersion = tagCarrier.musicTagModelVersion;
        }
        onTagsReady?.Invoke(earlyTags);

        int   windowSize     = Mathf.NextPowerOfTwo(config.spectrumSize);
        int   hopSize        = windowSize / 2;
        int   numFrames      = Mathf.Max(0, (mono.Length - windowSize) / hopSize);
        float hopTime        = (float)hopSize / sampleRate;
        int   numBands       = config.bands.Length;
        int   framesPerYield = 150;

        // Visual spectrum: a SEPARATE, higher-resolution band set (log-spaced 20 Hz..Nyquist)
        // used by the ground mesh cross-section and the debug spectrum panel. Independent from
        // `config.bands` above so it can never disturb Kick/Snare/HiHat classification, which
        // relies on that exact 6-band layout by index.
        int    numVisualBands  = Mathf.Max(1, config.visualBandCount);
        var    visualBandDefs  = BuildLogBands(numVisualBands, 20f, sampleRate * 0.5f);

        // ── Core arrays ────────────────────────────────────────────────────────
        float[]   energyEnv   = new float[numFrames];
        float[]   spectralCen = new float[numFrames];
        float[]   spectralFlx = new float[numFrames];
        float[][] bandEnv     = new float[numBands][];
        for (int b = 0; b < numBands; b++) bandEnv[b] = new float[numFrames];
        float[][] visualBandEnv = new float[numVisualBands][];
        for (int b = 0; b < numVisualBands; b++) visualBandEnv[b] = new float[numFrames];

        // ── Advanced per-frame arrays (allocated only if the feature is enabled) ─
        bool needFlatness = config.advancedEnabled &&
                            (config.advancedTimbre || config.advancedVoice || config.advancedStructure);
        bool needChroma   = config.advancedEnabled && config.advancedHarmony;
        bool needVoice    = config.advancedEnabled && config.advancedVoice;

        float[] spectralFlatness = needFlatness ? new float[numFrames]      : null;
        float[] chromaFlat       = needChroma   ? new float[numFrames * 12] : null;
        float[] voiceProb        = needVoice    ? new float[numFrames]       : null;
        float[] chromaBuf        = needChroma   ? new float[12]              : null;

        float[] prevSpectrum = null;

        // ── Main FFT loop ──────────────────────────────────────────────────────
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

            // Visual spectrum — reuses the SAME per-frame spectrum, no extra FFT
            for (int b = 0; b < numVisualBands; b++)
                visualBandEnv[b][f] = ComputeBandEnergy(spectrum, visualBandDefs[b], sampleRate, windowSize);

            // ── Advanced per-frame (reuse spectrum, no extra FFT) ───────────────
            float flat = 0.5f;
            if (needFlatness)
            {
                flat = AdvancedFFTFeatures.SpectralFlatness(spectrum);
                spectralFlatness[f] = flat;
            }
            if (needChroma)
            {
                AdvancedFFTFeatures.ComputeChroma(spectrum, sampleRate, windowSize, chromaBuf);
                System.Array.Copy(chromaBuf, 0, chromaFlat, f * 12, 12);
            }
            if (needVoice)
                voiceProb[f] = AdvancedFFTFeatures.VoiceProbability(spectrum, sampleRate, windowSize, flat);

            if (f % framesPerYield == 0)
            {
                EventBus.Publish(new PreAnalysisProgressEvent { Progress = (float)f / numFrames });
                yield return null;
            }
        }

        // ── Post-loop aggregation ──────────────────────────────────────────────
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
            duration          = (float)mono.Length / sampleRate,
            sampleRate        = sampleRate,
            estimatedBPM      = bpm,
            averageEnergy     = avgEnergy,
            maxEnergy         = maxEnergy,
            analysisHopTime   = hopTime,
            energyEnvelope    = energyEnv,
            spectralCentroid  = spectralCen,
            spectralFlux      = spectralFlx,
            onsetTimes        = onsets,
            bandEnvelopes     = bandEnv,
            visualBandEnvelopes = visualBandEnv,
            segments          = segments,
            spectralFlatness  = spectralFlatness,
            chromaFlat        = chromaFlat,
            voiceProbability  = voiceProb,
            estimatedKey      = -1,
            musicTags            = earlyTags,
            musicTagModelVersion = earlyTagModelVersion,
        };

        // Semantic tagging already ran ABOVE, before the FFT loop (see earlyTags) — ML inference
        // isn't "fast to recompute" so it's still only ever run once, just earlier than before.

        // Save BEFORE running derived analyzers: the cache stores raw per-frame arrays.
        // Derived features (loudness, dynamics, zones, harmony, etc.) are always
        // recomputed from the cached data — they're fast and excluded from cache
        // so config changes take effect without invalidating the cache.
        SongCache.Save(clip, profile);

        RunOfflineAnalyzers(profile, config);
        EventBus.Publish(new SongProfileReadyEvent { Profile = profile });
        onComplete?.Invoke(profile);
    }

    // ── Offline analyzer pipeline ──────────────────────────────────────────────

    private static void RunOfflineAnalyzers(SongProfile profile, AudioAnalysisConfig config)
    {
        if (!config.advancedEnabled) return;

        var ctx = new OfflineAnalysisContext
        {
            Config           = config,
            SampleRate       = profile.sampleRate,
            HopTime          = profile.analysisHopTime,
            Duration         = profile.duration,
            NumFrames        = profile.energyEnvelope?.Length ?? 0,
            AvgEnergy        = profile.averageEnergy,
            MaxEnergy        = profile.maxEnergy,
            EstimatedBPM     = profile.estimatedBPM,
            EnergyEnvelope   = profile.energyEnvelope,
            SpectralCentroid = profile.spectralCentroid,
            SpectralFlux     = profile.spectralFlux,
            OnsetTimes       = profile.onsetTimes,
            BandEnvelopes    = profile.bandEnvelopes,
            SpectralFlatness = profile.spectralFlatness,
            ChromaFlat       = profile.chromaFlat,
            VoiceProb        = profile.voiceProbability,
        };

        // Order matters: TimbralAnalyzer must run before DynamicsAnalyzer (writes intensity)
        IOfflineAnalyzer[] analyzers =
        {
            new LoudnessAnalyzer(),
            new TimbralAnalyzer(),    // writes intensity → needed by DynamicsAnalyzer
            new DynamicsAnalyzer(),   // reads intensity
            new StructureAnalyzer(),  // reads intensity, timbralChange
            new HarmonyAnalyzer(),
            new VoiceAnalyzer(),
            new SimilarityAnalyzer(),
        };

        foreach (var a in analyzers)
            if (a.IsEnabled(config))
                a.Analyze(ctx, profile);
    }

    // ── Semantic tagging (musicnn / Sentis) ────────────────────────────────────

    private static float[] GetClipData(AudioClip clip)
    {
        float[] raw = new float[clip.samples * clip.channels];
        clip.GetData(raw, 0);
        return raw;
    }

    /// <summary>
    /// Runs local ML inference (see SentisMusicTagger) and writes the result straight onto
    /// `profile`. Owns the tagger's lifetime — created and Dispose()'d here — so no Sentis
    /// worker/tensor ever lives past this single coroutine, in line with "no ML during
    /// gameplay": tagging only ever runs here, during pre-analysis.
    /// </summary>
    private static IEnumerator RunSemanticTagging(float[] mono, int sampleRate, SongProfile profile, AudioAnalysisConfig config)
    {
        IMusicTagger tagger = new SentisMusicTagger(config);
        if (!tagger.IsAvailable)
        {
            tagger.Dispose();
            yield break;
        }

        MusicTagScore[] result = null;
        yield return tagger.Tag(mono, sampleRate, r => result = r);
        tagger.Dispose();

        if (result != null)
        {
            profile.musicTags            = result;
            profile.musicTagModelVersion = SentisMusicTagger.ModelVersion;
        }
    }

    // ─── Core helpers ─────────────────────────────────────────────────────────

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

    // Log-spaced band edges (finer resolution at low frequencies, matching how an equalizer
    // is normally laid out) — deterministic given (count, minHz, maxHz), so the same visual
    // band layout is reproduced identically every time a song is analyzed.
    private static FrequencyBandConfig[] BuildLogBands(int count, float minHz, float maxHz)
    {
        var bands = new FrequencyBandConfig[count];
        float ratio = maxHz / minHz;
        for (int i = 0; i < count; i++)
        {
            float lo = minHz * Mathf.Pow(ratio, (float)i / count);
            float hi = minHz * Mathf.Pow(ratio, (float)(i + 1) / count);
            bands[i] = new FrequencyBandConfig($"VB{i}", lo, hi);
        }
        return bands;
    }

    private static float ComputeBandEnergy(float[] spectrum, FrequencyBandConfig band,
                                           int sampleRate, int windowSize)
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
        int  halfWin = 21;
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
