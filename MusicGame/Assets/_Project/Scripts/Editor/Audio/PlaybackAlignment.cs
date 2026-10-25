#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// MEASURED correspondence between the analysed master and the PLAYBACK representation Unity built from it
/// (the imported, compressed clip as the game decodes it). Same duration does not prove alignment, so:
///   - length: decoded frames vs master frames (padding / trimming), sample rate (resampling);
///   - lag at the START (first second with signal), MIDDLE and END (last second with signal): normalised
///     cross-correlation, coarse on a ×8 box-decimated envelope over ±4096 samples, then refined at full rate
///     ±16 samples — a constant decoder delay (e.g. an MP3 priming of ~1105 samples) or a drift (resampling)
///     shows up as non-zero / non-constant lags;
///   - gain ratio (rms decoded / rms analysed master) — checks the normalisation policy;
///   - SNR of the decoded signal against the master at the best lag (lossy-codec quality).
/// Sign convention: lag &gt; 0 means the playback is LATE by lag samples (playback time = profile time + lag/rate).
/// </summary>
public static class PlaybackAlignment
{
    public const int WindowFrames = 48000;   // 1 s at 48 kHz
    public const int CoarseRange  = 4096;    // ±85 ms
    public const int Decimation   = 8;
    public const int FineRange    = 16;
    /// <summary>Largest accepted trailing padding of the playback clip (1.3 ms at 48 kHz), only with zero lag.</summary>
    public const int MaxEndPaddingFrames = 64;

    public struct Point { public string Name; public double AtSeconds; public int LagSamples; public double Correlation; }

    public sealed class Result
    {
        public int MasterFrames, PlaybackFrames, MasterRate, PlaybackRate;
        public Point Start, Middle, End;
        public double GainRatioDb, SnrDb;
        public int EndPaddingFrames => Math.Max(0, PlaybackFrames - MasterFrames);
        public float EndPaddingPeak;
        public string Error; // null = measured consistently

        public int MaxAbsLag => Math.Max(Math.Abs(Start.LagSamples), Math.Max(Math.Abs(Middle.LagSamples), Math.Abs(End.LagSamples)));
        public bool ConstantLag => Start.LagSamples == Middle.LagSamples && Middle.LagSamples == End.LagSamples;
        public double MaxAbsLagMs => MaxAbsLag * 1000.0 / Math.Max(1, PlaybackRate);

        public override string ToString() =>
            $"frames master {MasterFrames} / playback {PlaybackFrames} @ {MasterRate}/{PlaybackRate} Hz | lag start {Start.LagSamples} (r {Start.Correlation:F4} @ {Start.AtSeconds:F1}s), " +
            $"middle {Middle.LagSamples} (r {Middle.Correlation:F4} @ {Middle.AtSeconds:F1}s), end {End.LagSamples} (r {End.Correlation:F4} @ {End.AtSeconds:F1}s) samples | " +
            $"gain {GainRatioDb:+0.00;-0.00} dB | SNR {SnrDb:F1} dB" + (EndPaddingFrames > 0 ? $" | end padding {EndPaddingFrames} frame(s), peak {EndPaddingPeak:F4}" : "") + (Error != null ? " | ERROR " + Error : "");
    }

    /// <summary>Compares two mono signals (same rate expected).</summary>
    public static Result Measure(float[] master, int masterRate, float[] playback, int playbackRate)
    {
        var r = new Result { MasterFrames = master.Length, PlaybackFrames = playback.Length, MasterRate = masterRate, PlaybackRate = playbackRate };
        if (masterRate != playbackRate) { r.Error = $"sample rate changed {masterRate} → {playbackRate} Hz (resampling: needs an explicit conversion)"; return r; }
        int n = Math.Min(master.Length, playback.Length);
        int startAt = FirstSignal(master, WindowFrames);
        int endAt = LastSignal(master, WindowFrames);
        int midAt = Math.Max(CoarseRange, n / 2 - WindowFrames / 2);
        r.Start  = Lag("start", master, playback, startAt, masterRate);
        r.Middle = Lag("middle", master, playback, midAt, masterRate);
        r.End    = Lag("end", master, playback, endAt, masterRate);

        // Gain + SNR over the whole overlap at the middle lag.
        int lag = r.Middle.LagSamples;
        double em = 0, ep = 0, err = 0;
        for (int i = Math.Max(0, -lag); i < n && i + lag < playback.Length; i++)
        {
            double a = master[i], b = playback[i + lag];
            em += a * a; ep += b * b; err += (a - b) * (a - b);
        }
        r.GainRatioDb = em > 0 && ep > 0 ? 10 * Math.Log10(ep / em) : 0;
        r.SnrDb = err > 0 ? 10 * Math.Log10(em / err) : 200;

        // Unity's import pads a few trailing frames for some sources (measured on the catalog: +1 frame for
        // song_008 even as PCM, +2 for song_010 as Vorbis) with a zero lag at start, middle AND end: padding
        // AFTER the last master sample, so no musical time moves and no conversion is needed (the played
        // clip's exact length is bound in the header). Trimming, larger padding or any lag is an error.
        int dLen = playback.Length - master.Length;
        for (int i = master.Length; i < playback.Length; i++) r.EndPaddingPeak = Math.Max(r.EndPaddingPeak, Math.Abs(playback[i]));
        if (dLen < 0 || dLen > MaxEndPaddingFrames) r.Error = $"length differs by {dLen} frames (padding/trimming)";
        else if (!r.ConstantLag) r.Error = "lag differs between start/middle/end (drift)";
        else if (Math.Min(r.Start.Correlation, Math.Min(r.Middle.Correlation, r.End.Correlation)) < 0.9) r.Error = "weak correlation (not the same audio?)";
        return r;
    }

    private static Point Lag(string name, float[] m, float[] p, int at, int rate)
    {
        at = Math.Max(CoarseRange + FineRange, Math.Min(at, m.Length - WindowFrames - CoarseRange - FineRange - 1));
        // Coarse: box-decimated signals.
        int dn = WindowFrames / Decimation, dr = CoarseRange / Decimation;
        var md = new float[dn];
        for (int i = 0; i < dn; i++) { float s = 0; for (int k = 0; k < Decimation; k++) s += m[at + i * Decimation + k]; md[i] = s; }
        int bestCoarse = 0; double bestC = double.NegativeInfinity;
        var pd = new float[dn];
        for (int l = -dr; l <= dr; l++)
        {
            int o = at + l * Decimation;
            for (int i = 0; i < dn; i++) { float s = 0; for (int k = 0; k < Decimation; k++) s += p[o + i * Decimation + k]; pd[i] = s; }
            double c = Ncc(md, 0, pd, 0, dn);
            if (c > bestC) { bestC = c; bestCoarse = l * Decimation; }
        }
        // Fine: full rate around the coarse optimum.
        int best = bestCoarse; double bestF = double.NegativeInfinity;
        for (int l = bestCoarse - Decimation - FineRange; l <= bestCoarse + Decimation + FineRange; l++)
        {
            double c = Ncc(m, at, p, at + l, WindowFrames);
            if (c > bestF) { bestF = c; best = l; }
        }
        return new Point { Name = name, AtSeconds = at / (double)rate, LagSamples = best, Correlation = bestF };
    }

    private static double Ncc(float[] a, int ao, float[] b, int bo, int n)
    {
        double ab = 0, aa = 0, bb = 0;
        for (int i = 0; i < n; i++) { double x = a[ao + i], y = b[bo + i]; ab += x * y; aa += x * x; bb += y * y; }
        return aa > 0 && bb > 0 ? ab / Math.Sqrt(aa * bb) : 0;
    }

    // First / last window position whose content is above −40 dBFS RMS (skips silent intro/outro).
    private static int FirstSignal(float[] x, int window)
    {
        for (int at = 0; at + window < x.Length; at += window / 4)
            if (Rms(x, at, window) > 0.01) return at;
        return 0;
    }

    private static int LastSignal(float[] x, int window)
    {
        for (int at = x.Length - window - 1; at > 0; at -= window / 4)
            if (Rms(x, at, window) > 0.01) return at;
        return Math.Max(0, x.Length - window - 1);
    }

    private static double Rms(float[] x, int at, int n)
    {
        double s = 0; for (int i = 0; i < n; i++) s += x[at + i] * x[at + i];
        return Math.Sqrt(s / n);
    }

    // ── Decoding the playback representation in the editor ─────────────────────────

    /// <summary>Mono decode of the imported clip exactly as the game's decoder produces it. GetData needs
    /// DecompressOnLoad: when the published clip uses another load type, a TEMPORARY copy of the master with the
    /// identical sample settings but DecompressOnLoad is imported (the compressed data does not depend on the load
    /// type), decoded and deleted. The master and its importer are never modified.</summary>
    public static float[] DecodePlaybackMono(string assetPath, out int rate, out int channels)
    {
        var importer = (AudioImporter)AssetImporter.GetAtPath(assetPath);
        var settings = importer.defaultSampleSettings;
        string decodePath = assetPath;
        string temp = null;
        if (settings.loadType != AudioClipLoadType.DecompressOnLoad)
        {
            const string tempFolder = "Assets/__P3DecodeTemp";
            if (!AssetDatabase.IsValidFolder(tempFolder)) AssetDatabase.CreateFolder("Assets", "__P3DecodeTemp");
            temp = $"{tempFolder}/decode{Path.GetExtension(assetPath)}";
            AssetDatabase.DeleteAsset(temp);
            File.Copy(assetPath, temp, true);
            AssetDatabase.ImportAsset(temp, ImportAssetOptions.ForceSynchronousImport);
            var ti = (AudioImporter)AssetImporter.GetAtPath(temp);
            ti.forceToMono = importer.forceToMono;
            var so = new SerializedObject(ti); so.FindProperty("m_Normalize").boolValue = MasterAudio.Normalizes(importer); so.ApplyModifiedPropertiesWithoutUndo();
            var s = settings; s.loadType = AudioClipLoadType.DecompressOnLoad; ti.defaultSampleSettings = s;
            foreach (var platform in new[] { "Standalone", "Android", "iPhone" })
                if (importer.ContainsSampleSettingsOverride(platform))
                {
                    var o = importer.GetOverrideSampleSettings(platform); o.loadType = AudioClipLoadType.DecompressOnLoad;
                    ti.SetOverrideSampleSettings(platform, o);
                }
            ti.SaveAndReimport();
            decodePath = temp;
        }
        try
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(decodePath);
            if (clip == null) throw new InvalidOperationException($"no AudioClip at {decodePath}");
            if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
            var t0 = DateTime.UtcNow;
            while (clip.loadState == AudioDataLoadState.Loading && (DateTime.UtcNow - t0).TotalSeconds < 60) System.Threading.Thread.Sleep(5);
            rate = clip.frequency; channels = clip.channels;
            var inter = new float[clip.samples * clip.channels];
            if (!clip.GetData(inter, 0)) throw new InvalidOperationException($"GetData failed for {decodePath}");
            var mono = new float[clip.samples];
            for (int i = 0; i < mono.Length; i++)
            {
                float s = 0; for (int c = 0; c < channels; c++) s += inter[i * channels + c];
                mono[i] = s / channels;
            }
            return mono;
        }
        finally
        {
            if (temp != null) { AssetDatabase.DeleteAsset(temp); AssetDatabase.DeleteAsset("Assets/__P3DecodeTemp"); }
        }
    }
}
#endif
