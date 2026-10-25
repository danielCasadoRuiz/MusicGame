#if UNITY_EDITOR
using System;
using System.IO;
using MusicGame.Audio.Analysis;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The LOSSLESS master of a catalog song (the WAV under Audio/Music — the same file Unity imports and
/// compresses for playback; it is never modified or published as an extra copy). Editor only.
///   Read      → interleaved float PCM exactly as stored (16/24/32-bit PCM or 32-bit float WAV).
///   Analysis  → the normalisation POLICY: the analysis sees the master at the SAME level the game plays it.
///               Unity's importer only normalises when "Force To Mono" is on (normalize applies to the mono
///               mix-down — measured: with forceToMono off the decoded clip is within 0.07 dB of the master
///               although normalize = 1), so the gain is 1 / peak(mono mix) only when forceToMono AND
///               normalize, else 1 — the scale the previous pipeline analysed (the imported clip). The gain
///               is recorded in the header (analysisGain) and VERIFIED against the decoded playback clip by
///               <see cref="PlaybackAlignment"/> (bake fails beyond 0.5 dB), never assumed.
/// </summary>
public static class MasterAudio
{
    public sealed class Pcm
    {
        public float[] Interleaved;
        public int Channels, SampleRate, Frames;
        public float Peak;
    }

    /// <summary>Reads a RIFF/WAVE file. Throws on an unsupported/corrupt file.</summary>
    public static Pcm ReadWav(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 44 || data[0] != 'R' || data[1] != 'I' || data[2] != 'F' || data[3] != 'F' || data[8] != 'W' || data[9] != 'A')
            throw new InvalidDataException($"{path}: not a RIFF/WAVE file");
        int pos = 12, fmt = 0, channels = 0, rate = 0, bits = 0, dataPos = -1, dataLen = 0;
        while (pos + 8 <= data.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(data, pos, 4);
            int len = BitConverter.ToInt32(data, pos + 4);
            if (id == "fmt ")
            {
                fmt = BitConverter.ToUInt16(data, pos + 8); channels = BitConverter.ToUInt16(data, pos + 10);
                rate = BitConverter.ToInt32(data, pos + 12); bits = BitConverter.ToUInt16(data, pos + 22);
                if (fmt == 0xFFFE && len >= 40) fmt = BitConverter.ToUInt16(data, pos + 32); // WAVE_FORMAT_EXTENSIBLE sub-format
            }
            else if (id == "data") { dataPos = pos + 8; dataLen = Math.Min(len, data.Length - dataPos); break; }
            pos += 8 + len + (len & 1);
        }
        if (dataPos < 0 || channels <= 0 || rate <= 0) throw new InvalidDataException($"{path}: no fmt/data chunk");
        int bytesPer = bits / 8;
        int count = dataLen / bytesPer;
        var pcm = new float[count - count % channels];
        float peak = 0f;
        for (int i = 0; i < pcm.Length; i++)
        {
            int o = dataPos + i * bytesPer;
            float v = (fmt, bits) switch
            {
                (1, 16) => BitConverter.ToInt16(data, o) / 32768f,
                (1, 24) => ((data[o] | (data[o + 1] << 8) | (data[o + 2] << 16)) << 8 >> 8) / 8388608f,
                (1, 32) => BitConverter.ToInt32(data, o) / 2147483648f,
                (3, 32) => BitConverter.ToSingle(data, o),
                _ => throw new InvalidDataException($"{path}: unsupported WAV encoding (format {fmt}, {bits} bit)"),
            };
            pcm[i] = v;
            float a = Math.Abs(v); if (a > peak) peak = a;
        }
        return new Pcm { Interleaved = pcm, Channels = channels, SampleRate = rate, Frames = pcm.Length / channels, Peak = peak };
    }

    /// <summary>The normalisation policy (see class doc): linear gain applied to the master before analysis.
    /// `levelNormalized` = <see cref="ImportNormalizesLevel"/> of the playback importer.</summary>
    public static float AnalysisGain(Pcm master, bool levelNormalized)
    {
        if (!levelNormalized) return 1f;
        float peak = 0f;
        foreach (var v in AnalysisMono(master, 1f)) { float a = Math.Abs(v); if (a > peak) peak = a; }
        return peak > 0f ? 1f / peak : 1f;
    }

    /// <summary>Does the import change the level of the played clip? Only with Force To Mono + Normalize.</summary>
    public static bool ImportNormalizesLevel(AudioImporter importer) => importer.forceToMono && Normalizes(importer);

    /// <summary>Mono analysis input: gain applied, channels averaged (same mix-down as the core / clip capture).</summary>
    public static float[] AnalysisMono(Pcm master, float gain)
    {
        int ch = master.Channels, n = master.Frames;
        var mono = new float[n];
        var x = master.Interleaved;
        for (int i = 0; i < n; i++)
        {
            float s = 0f;
            int o = i * ch;
            for (int c = 0; c < ch; c++) s += x[o + c];
            mono[i] = s / ch * gain;
        }
        return mono;
    }

    /// <summary>Identity of the analysed master revision: its exact bytes + the normalisation policy bit.
    /// Hashed on the calling thread (memoised per file length/time by SongAudioIdentitySource).</summary>
    public static SongAudioIdentity Identity(string songId, string assetPath, bool levelNormalized)
        => new SongAudioIdentitySource
        {
            Kind = SongAudioIdentityKind.Master, SongKey = songId, FilePath = Path.GetFullPath(assetPath),
            SettingsHash = levelNormalized ? 0x4E4F524DUL /* "NORM" */ : 0x52415721UL /* "RAW!" */,
        }.Resolve();

    // ── Playback representation ───────────────────────────────────────────────────

    /// <summary>The import settings Unity applies for `target` (platform override, else defaults).</summary>
    public static AudioImporterSampleSettings EffectiveSettings(AudioImporter importer, BuildTarget target)
    {
        string platform = BuildPipeline.GetBuildTargetGroup(target).ToString();
        return importer.ContainsSampleSettingsOverride(platform) ? importer.GetOverrideSampleSettings(platform) : importer.defaultSampleSettings;
    }

    public static bool Normalizes(AudioImporter importer)
    {
        var p = new SerializedObject(importer).FindProperty("m_Normalize");
        return p != null && p.boolValue;
    }

    /// <summary>Revision of the PLAYBACK representation: the master revision + every import setting that shapes
    /// the decoded samples (format, quality, sample rate, mono, normalise) + the editor version (its encoder).
    /// The load type only changes how the data is held, so it is NOT part of it (recorded separately).
    /// Hashes the EFFECTIVE settings, not the platform name: platforms without overrides share a revision.</summary>
    public static ulong PlaybackRevision(ulong masterIdentity, AudioImporter importer, BuildTarget target)
    {
        var s = EffectiveSettings(importer, target);
        var h = StableHash.Create();
        h.Add("MusicGame.PlaybackRevision.v1");
        h.Add(masterIdentity);
        h.Add((int)s.compressionFormat);
        h.Add(s.quality);
        h.Add((int)s.sampleRateSetting);
        h.Add(s.sampleRateSetting == AudioSampleRateSetting.OverrideSampleRate ? (int)s.sampleRateOverride : 0);
        h.Add(importer.forceToMono);
        h.Add(Normalizes(importer));
        h.Add(UnityMajorMinor());
        ulong v = h.Value;
        return v == 0 ? 1UL : v;
    }

    private static string UnityMajorMinor()
    {
        var parts = Application.unityVersion.Split('.');
        return parts.Length >= 2 ? parts[0] + "." + parts[1] : Application.unityVersion;
    }
}
#endif
