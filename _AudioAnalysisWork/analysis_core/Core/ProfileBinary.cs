using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MusicGame.Audio.Analysis
{
    /// <summary>Identity + compatibility block written in front of every binary profile. Small and
    /// readable without parsing the body (validation / staleness checks before loading anything).</summary>
    public sealed class ProfileHeader
    {
        public int    formatVersion = ProfileBinary.FormatVersion;
        public int    pipelineVersion;     // AnalysisVersions.Pipeline at analysis time
        public ulong  paramsHash;          // AnalysisParams.Hash at analysis time
        public int    tagModelVersion;     // 0 = no semantic tags
        public ulong  tagReuseKey;         // MusicnnPreprocessor.TagReuseKey(...) of the stored tags
        public string songId = "";         // stable catalog id (or import id)
        public ulong  audioIdentity;       // hash of the audio revision (source bytes + import settings, or PCM)
        public int    clipSamples;         // frames per channel of the analysed clip
        public int    clipChannels;
        public int    clipFrequency;
        public uint   flags;               // bit 0: tags present, bit 1: half-precision frame arrays
        public uint   bodyLength;
        public uint   bodyCrc32;

        // ── Format 2: binding of the ANALYSED audio (the lossless master) to the PLAYBACK representation
        //    the game actually plays (a published catalog profile). All zero/empty = not bound (a runtime
        //    analysis of the very clip that plays: imports, dev cache). ──
        public ulong  playbackRevision;    // hash of the playback representation: master revision + the import settings that shape its decoded samples
        public int    playbackSamples;     // frames per channel of the playback clip (cheap runtime check against the loaded clip)
        public int    playbackChannels;
        public int    playbackFrequency;
        public float  analysisGain = 1f;   // linear gain applied to the master PCM before analysis (normalisation policy); 1 = none
        public byte   timeConversion;      // ProfileTimeConversion: 0 identity (playback clip time == profile time), 1 constant offset
        public float  playbackOffsetSeconds; // with ConstantOffset: playbackClipTime = profileTime + playbackOffsetSeconds
        public float  alignmentMaxErrorMs; // measured max |master→playback lag| over the bake's start/middle/end checks (diagnostic)
        public string playbackPlatform = ""; // build target whose effective import settings were bound (informational)

        public bool HasTags => (flags & ProfileBinary.FlagTags) != 0;
        public bool HasPlaybackBinding => playbackRevision != 0UL;

        /// <summary>Profile clip time → playback clip time under the stored conversion (identity unless a
        /// measured constant offset was bound at bake time; sign: playback = profile + offset).</summary>
        public double ToPlaybackTime(double profileTime)
            => timeConversion == ProfileTimeConversion.ConstantOffset ? profileTime + playbackOffsetSeconds : profileTime;

        /// <summary>The profile was produced by this code with these params for this audio.</summary>
        public bool IsCompatible(AnalysisParams p, ulong audio, int clipSamples, int clipFrequency)
            => formatVersion == ProfileBinary.FormatVersion && pipelineVersion == AnalysisVersions.Pipeline
               && paramsHash == p.Hash && audioIdentity == audio && this.clipSamples == clipSamples && this.clipFrequency == clipFrequency;
    }

    /// <summary>How profile (analysed master) times map onto the playback clip's timeline.</summary>
    public static class ProfileTimeConversion
    {
        public const byte Identity = 0, ConstantOffset = 1;
    }

    /// <summary>
    /// Versioned little-endian binary profile ("MGSP"): header + CRC-checked body made of tagged
    /// sections (unknown sections are skipped → forward compatible within a format version).
    /// Lossless by default (float32 arrays copied with Buffer.BlockCopy); optional half precision
    /// for frame arrays exists ONLY for evaluation and is not the default.
    /// </summary>
    public static class ProfileBinary
    {
        /// <summary>2: header binds the analysed master to the playback representation (P3 published profiles).</summary>
        public const int  FormatVersion = 2;
        /// <summary>Oldest format whose header/body this code can still READ (compatibility checks still
        /// require <see cref="FormatVersion"/>, so an older file is stale, never silently accepted).</summary>
        public const int  MinReadableFormatVersion = 1;
        public const uint FlagTags = 1, FlagHalf = 2;
        private static readonly byte[] Magic = { (byte)'M', (byte)'G', (byte)'S', (byte)'P' };

        private const uint SecScalars = 0x4C414353; // "SCAL"
        private const uint SecBands   = 0x53444E42; // "BNDS"
        private const uint SecFrames  = 0x4D415246; // "FRAM"
        private const uint SecOnsets  = 0x54534E4F; // "ONST"
        private const uint SecBeats   = 0x54414542; // "BEAT"
        private const uint SecSegs    = 0x53474553; // "SEGS"
        private const uint SecTags    = 0x53474154; // "TAGS"

        private const byte ArrEnergy = 1, ArrCentroid = 2, ArrFlux = 3, ArrFlatness = 4, ArrVoice = 5, ArrChroma = 6, ArrBand0 = 16, ArrVisual0 = 64;

        // ── Writing ─────────────────────────────────────────────────────────────────────────────

        public static byte[] Write(ProfileHeader h, AnalysisResult r, TagScore[] tags, bool halfPrecision = false)
        {
            if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("big-endian host");
            var body = new Buf(Math.Max(1024, EstimateBodySize(r)));

            // Scalars
            int sc = body.BeginSection(SecScalars);
            body.F(r.duration); body.F(r.hopTime); body.I(r.frameCount); body.I(r.sampleCount); body.I(r.sampleRate);
            body.F(r.averageEnergy); body.F(r.maxEnergy); body.F(r.estimatedBpm); body.F(r.bpmConfidence); body.F(r.beatPhase);
            body.EndSection(sc);

            // Band layouts (so consumers know what each band index means)
            sc = body.BeginSection(SecBands);
            WriteSpecs(body, r.classificationBandSpecs); WriteSpecs(body, r.visualBandSpecs);
            body.EndSection(sc);

            // Frame arrays
            sc = body.BeginSection(SecFrames);
            var arrays = new List<(byte, float[])>
            {
                (ArrEnergy, r.energy), (ArrCentroid, r.centroid), (ArrFlux, r.flux), (ArrFlatness, r.flatness), (ArrVoice, r.voice), (ArrChroma, r.chroma),
            };
            for (int b = 0; b < (r.bands?.Length ?? 0); b++) arrays.Add(((byte)(ArrBand0 + b), r.bands[b]));
            for (int b = 0; b < (r.visualBands?.Length ?? 0); b++) arrays.Add(((byte)(ArrVisual0 + b), r.visualBands[b]));
            int present = 0; foreach (var a in arrays) if (a.Item2 != null) present++;
            body.I(present);
            foreach (var (id, arr) in arrays)
            {
                if (arr == null) continue;
                body.B(id); body.B((byte)(halfPrecision ? 1 : 0)); body.B(0); body.B(0);
                body.I(arr.Length);
                if (halfPrecision) body.Halves(arr); else body.Floats(arr);
            }
            body.EndSection(sc);

            // Onset events (28 bytes each)
            sc = body.BeginSection(SecOnsets);
            var on = r.onsets ?? Array.Empty<OnsetEvent>();
            body.I(on.Length);
            for (int i = 0; i < on.Length; i++)
            {
                var e = on[i];
                body.F(e.time); body.F(e.strength); body.B((byte)e.families); body.B((byte)e.origin); body.B(0); body.B(0);
                body.F(e.kickConfidence); body.F(e.snareConfidence); body.F(e.hihatConfidence); body.F(e.genericConfidence);
            }
            body.EndSection(sc);

            sc = body.BeginSection(SecBeats);
            var beats = r.beatTimes ?? Array.Empty<float>();
            body.I(beats.Length); body.Floats(beats);
            body.EndSection(sc);

            sc = body.BeginSection(SecSegs);
            var segs = r.segments ?? Array.Empty<AnalysisSegment>();
            body.I(segs.Length);
            foreach (var s in segs) { body.F(s.startTime); body.F(s.endTime); body.F(s.averageEnergy); body.F(s.maxEnergy); body.B((byte)s.level); body.B(0); body.B(0); body.B(0); }
            body.EndSection(sc);

            uint flags = halfPrecision ? FlagHalf : 0;
            if (tags != null)
            {
                flags |= FlagTags;
                sc = body.BeginSection(SecTags);
                body.I(tags.Length);
                foreach (var t in tags) { body.S(t.tag); body.F(t.score); }
                body.EndSection(sc);
            }

            h.flags = flags;
            h.formatVersion = FormatVersion;
            h.bodyLength = (uint)body.Length;
            h.bodyCrc32 = Crc32.Compute(body.Data, 0, body.Length);

            var head = new Buf(128);
            head.Raw(Magic, 4);
            head.U16((ushort)FormatVersion);
            int sizePos = head.Length; head.U16(0); // header size, patched below
            head.I(h.pipelineVersion); head.L(h.paramsHash); head.I(h.tagModelVersion); head.L(h.tagReuseKey);
            head.L(h.audioIdentity); head.I(h.clipSamples); head.I(h.clipChannels); head.I(h.clipFrequency);
            head.U32(h.flags); head.U32(h.bodyLength); head.U32(h.bodyCrc32);
            head.S(h.songId);
            head.Align4();
            head.L(h.playbackRevision); head.I(h.playbackSamples); head.I(h.playbackChannels); head.I(h.playbackFrequency);
            head.F(h.analysisGain); head.B(h.timeConversion); head.B(0); head.B(0); head.B(0);
            head.F(h.playbackOffsetSeconds); head.F(h.alignmentMaxErrorMs);
            head.S(h.playbackPlatform);
            head.Align4();
            head.PatchU16(sizePos, (ushort)head.Length);

            var outBytes = new byte[head.Length + body.Length];
            Buffer.BlockCopy(head.Data, 0, outBytes, 0, head.Length);
            Buffer.BlockCopy(body.Data, 0, outBytes, head.Length, body.Length);
            return outBytes;
        }

        private static int EstimateBodySize(AnalysisResult r)
        {
            int frames = r.frameCount;
            int arrays = 5 + (r.bands?.Length ?? 0) + (r.visualBands?.Length ?? 0) + 12;
            return 4096 + frames * arrays * 4 + (r.onsets?.Length ?? 0) * 28 + (r.beatTimes?.Length ?? 0) * 4 + (r.segments?.Length ?? 0) * 20;
        }

        private static void WriteSpecs(Buf b, BandSpec[] specs)
        {
            specs = specs ?? Array.Empty<BandSpec>();
            b.I(specs.Length);
            foreach (var s in specs) { b.S(s.label); b.F(s.minHz); b.F(s.maxHz); }
        }

        // ── Reading ─────────────────────────────────────────────────────────────────────────────

        /// <summary>Parses only the header (cheap). Throws InvalidDataException on a foreign/corrupt file.</summary>
        public static ProfileHeader ReadHeader(byte[] data, out int headerSize)
        {
            if (data == null || data.Length < 16) throw new InvalidDataException("profile too small");
            for (int i = 0; i < 4; i++) if (data[i] != Magic[i]) throw new InvalidDataException("not a MusicGame profile (bad magic)");
            var rd = new Rd(data, 4, data.Length);
            int fmt = rd.U16();
            if (fmt < MinReadableFormatVersion || fmt > FormatVersion) throw new InvalidDataException("unsupported profile format " + fmt);
            headerSize = rd.U16();
            var h = new ProfileHeader
            {
                formatVersion = fmt, pipelineVersion = rd.I(), paramsHash = rd.L(), tagModelVersion = rd.I(), tagReuseKey = rd.L(),
                audioIdentity = rd.L(), clipSamples = rd.I(), clipChannels = rd.I(), clipFrequency = rd.I(),
                flags = rd.U32(), bodyLength = rd.U32(), bodyCrc32 = rd.U32(),
            };
            h.songId = rd.S();
            if (fmt >= 2)
            {
                rd.Align4();
                h.playbackRevision = rd.L(); h.playbackSamples = rd.I(); h.playbackChannels = rd.I(); h.playbackFrequency = rd.I();
                h.analysisGain = rd.F(); h.timeConversion = rd.B(); rd.B(); rd.B(); rd.B();
                h.playbackOffsetSeconds = rd.F(); h.alignmentMaxErrorMs = rd.F();
                h.playbackPlatform = rd.S();
            }
            if (headerSize < rd.Pos || headerSize > data.Length) throw new InvalidDataException("bad header size");
            if ((long)headerSize + h.bodyLength != data.Length) throw new InvalidDataException("truncated or padded profile");
            return h;
        }

        /// <summary>Full read: header + CRC check + body. Every array is a fresh copy (callers own them).</summary>
        public static AnalysisResult Read(byte[] data, out ProfileHeader header, out TagScore[] tags, bool verifyCrc = true)
        {
            if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("big-endian host");
            header = ReadHeader(data, out int hs);
            if (verifyCrc && Crc32.Compute(data, hs, (int)header.bodyLength) != header.bodyCrc32) throw new InvalidDataException("profile body CRC mismatch (corrupt)");
            var r = new AnalysisResult();
            tags = null;
            var rd = new Rd(data, hs, data.Length);
            var band = new List<(int, float[])>(); var vis = new List<(int, float[])>();
            while (rd.Pos < data.Length)
            {
                uint id = rd.U32(); int len = rd.I();
                int end = rd.Pos + len;
                if (len < 0 || end > data.Length) throw new InvalidDataException("bad section length");
                switch (id)
                {
                    case SecScalars:
                        r.duration = rd.F(); r.hopTime = rd.F(); r.frameCount = rd.I(); r.sampleCount = rd.I(); r.sampleRate = rd.I();
                        r.averageEnergy = rd.F(); r.maxEnergy = rd.F(); r.estimatedBpm = rd.F(); r.bpmConfidence = rd.F(); r.beatPhase = rd.F();
                        break;
                    case SecBands:
                        r.classificationBandSpecs = ReadSpecs(ref rd); r.visualBandSpecs = ReadSpecs(ref rd);
                        break;
                    case SecFrames:
                    {
                        int count = rd.I();
                        for (int a = 0; a < count; a++)
                        {
                            byte aid = rd.B(); byte enc = rd.B(); rd.B(); rd.B();
                            int n = rd.I();
                            float[] arr = enc == 1 ? rd.Halves(n) : rd.Floats(n);
                            switch (aid)
                            {
                                case ArrEnergy: r.energy = arr; break;
                                case ArrCentroid: r.centroid = arr; break;
                                case ArrFlux: r.flux = arr; break;
                                case ArrFlatness: r.flatness = arr; break;
                                case ArrVoice: r.voice = arr; break;
                                case ArrChroma: r.chroma = arr; break;
                                default:
                                    if (aid >= ArrVisual0) vis.Add((aid - ArrVisual0, arr));
                                    else if (aid >= ArrBand0) band.Add((aid - ArrBand0, arr));
                                    break;
                            }
                        }
                        break;
                    }
                    case SecOnsets:
                    {
                        int n = rd.I();
                        var on = new OnsetEvent[n];
                        for (int i = 0; i < n; i++)
                        {
                            on[i].time = rd.F(); on[i].strength = rd.F(); on[i].families = (OnsetFamily)rd.B(); on[i].origin = (OnsetOrigin)rd.B(); rd.B(); rd.B();
                            on[i].kickConfidence = rd.F(); on[i].snareConfidence = rd.F(); on[i].hihatConfidence = rd.F(); on[i].genericConfidence = rd.F();
                        }
                        r.onsets = on;
                        break;
                    }
                    case SecBeats: { int n = rd.I(); r.beatTimes = rd.Floats(n); break; }
                    case SecSegs:
                    {
                        int n = rd.I();
                        var s = new AnalysisSegment[n];
                        for (int i = 0; i < n; i++) { s[i].startTime = rd.F(); s[i].endTime = rd.F(); s[i].averageEnergy = rd.F(); s[i].maxEnergy = rd.F(); s[i].level = (SegmentLevel)rd.B(); rd.B(); rd.B(); rd.B(); }
                        r.segments = s;
                        break;
                    }
                    case SecTags:
                    {
                        int n = rd.I();
                        tags = new TagScore[n];
                        for (int i = 0; i < n; i++) { tags[i].tag = rd.S(); tags[i].score = rd.F(); }
                        break;
                    }
                }
                rd.Pos = end; // skip unknown sections / padding
            }
            r.bands = Assemble(band); r.visualBands = Assemble(vis);
            r.onsets = r.onsets ?? Array.Empty<OnsetEvent>();
            r.beatTimes = r.beatTimes ?? Array.Empty<float>();
            r.segments = r.segments ?? Array.Empty<AnalysisSegment>();
            return r;
        }

        private static float[][] Assemble(List<(int, float[])> list)
        {
            int n = 0; foreach (var (i, _) in list) n = Math.Max(n, i + 1);
            var o = new float[n][];
            foreach (var (i, a) in list) o[i] = a;
            return o;
        }

        private static BandSpec[] ReadSpecs(ref Rd rd)
        {
            int n = rd.I(); var s = new BandSpec[n];
            for (int i = 0; i < n; i++) { s[i].label = rd.S(); s[i].minHz = rd.F(); s[i].maxHz = rd.F(); }
            return s;
        }

        // ── Low-level helpers ───────────────────────────────────────────────────────────────────

        private sealed class Buf
        {
            public byte[] Data; public int Length;
            public Buf(int cap) { Data = new byte[cap]; }
            private void Need(int n) { if (Length + n > Data.Length) Array.Resize(ref Data, Math.Max(Data.Length * 2, Length + n)); }
            public void B(byte v) { Need(1); Data[Length++] = v; }
            public void U16(ushort v) { Need(2); Data[Length++] = (byte)v; Data[Length++] = (byte)(v >> 8); }
            public void PatchU16(int pos, ushort v) { Data[pos] = (byte)v; Data[pos + 1] = (byte)(v >> 8); }
            public void U32(uint v) { Need(4); for (int i = 0; i < 4; i++) Data[Length++] = (byte)(v >> (8 * i)); }
            public void I(int v) => U32((uint)v);
            public void L(ulong v) { Need(8); for (int i = 0; i < 8; i++) Data[Length++] = (byte)(v >> (8 * i)); }
            public void F(float v) => I(BitConverter.SingleToInt32Bits(v));
            public void Raw(byte[] b, int n) { Need(n); Buffer.BlockCopy(b, 0, Data, Length, n); Length += n; }
            public void S(string s) { var b = Encoding.UTF8.GetBytes(s ?? ""); if (b.Length > ushort.MaxValue) throw new ArgumentException("string too long"); U16((ushort)b.Length); Raw(b, b.Length); }
            public void Align4() { while ((Length & 3) != 0) B(0); }
            public void Floats(float[] a) { Align4(); int n = a.Length * 4; Need(n); Buffer.BlockCopy(a, 0, Data, Length, n); Length += n; }
            public void Halves(float[] a) { Align4(); Need(a.Length * 2); for (int i = 0; i < a.Length; i++) { ushort hv = HalfConv.ToHalf(a[i]); Data[Length++] = (byte)hv; Data[Length++] = (byte)(hv >> 8); } Align4(); }
            public int BeginSection(uint id) { Align4(); U32(id); int p = Length; I(0); return p; }
            public void EndSection(int lenPos) { Align4(); int len = Length - lenPos - 4; Data[lenPos] = (byte)len; Data[lenPos + 1] = (byte)(len >> 8); Data[lenPos + 2] = (byte)(len >> 16); Data[lenPos + 3] = (byte)(len >> 24); }
        }

        private struct Rd
        {
            private readonly byte[] _d; private readonly int _end; public int Pos;
            public Rd(byte[] d, int pos, int end) { _d = d; Pos = pos; _end = end; }
            private void Need(int n) { if (Pos + n > _end) throw new InvalidDataException("unexpected end of profile"); }
            public byte B() { Need(1); return _d[Pos++]; }
            public int U16() { Need(2); int v = _d[Pos] | (_d[Pos + 1] << 8); Pos += 2; return v; }
            public uint U32() { Need(4); uint v = (uint)(_d[Pos] | (_d[Pos + 1] << 8) | (_d[Pos + 2] << 16) | (_d[Pos + 3] << 24)); Pos += 4; return v; }
            public int I() => (int)U32();
            public ulong L() { ulong lo = U32(), hi = U32(); return lo | (hi << 32); }
            public float F() => BitConverter.Int32BitsToSingle(I());
            public string S() { int n = U16(); Need(n); var s = Encoding.UTF8.GetString(_d, Pos, n); Pos += n; return s; }
            public void Align4() { while ((Pos & 3) != 0) Pos++; }
            public float[] Floats(int n)
            {
                if (n < 0) throw new InvalidDataException("negative array length");
                Align4(); Need(n * 4); var a = new float[n]; Buffer.BlockCopy(_d, Pos, a, 0, n * 4); Pos += n * 4; return a;
            }
            public float[] Halves(int n)
            {
                if (n < 0) throw new InvalidDataException("negative array length");
                Align4(); Need(n * 2); var a = new float[n];
                for (int i = 0; i < n; i++) a[i] = HalfConv.ToFloat((ushort)(_d[Pos + 2 * i] | (_d[Pos + 2 * i + 1] << 8)));
                Pos += n * 2; Align4(); return a;
            }
        }
    }

    /// <summary>IEEE 754 binary16 conversion (round to nearest even). Evaluation only.</summary>
    public static class HalfConv
    {
        public static ushort ToHalf(float f)
        {
            uint x = (uint)BitConverter.SingleToInt32Bits(f);
            uint sign = (x >> 16) & 0x8000;
            int exp = (int)((x >> 23) & 0xFF) - 127 + 15;
            uint mant = x & 0x7FFFFF;
            if (((x >> 23) & 0xFF) == 0xFF) return (ushort)(sign | 0x7C00 | (mant != 0 ? 0x200u : 0));
            if (exp >= 31) return (ushort)(sign | 0x7C00);
            if (exp <= 0)
            {
                if (exp < -10) return (ushort)sign;
                mant |= 0x800000;
                int shift = 14 - exp;
                uint hm = mant >> shift;
                uint rem = mant & ((1u << shift) - 1), halfway = 1u << (shift - 1);
                if (rem > halfway || (rem == halfway && (hm & 1) != 0)) hm++;
                return (ushort)(sign | hm);
            }
            uint h = sign | ((uint)exp << 10) | (mant >> 13);
            uint r = mant & 0x1FFF;
            if (r > 0x1000 || (r == 0x1000 && (h & 1) != 0)) h++;
            return (ushort)h;
        }

        public static float ToFloat(ushort h)
        {
            uint sign = (uint)(h & 0x8000) << 16;
            int exp = (h >> 10) & 0x1F;
            uint mant = (uint)(h & 0x3FF);
            uint bits;
            if (exp == 0)
            {
                if (mant == 0) bits = sign;
                else { int sh = 0; while ((mant & 0x400) == 0) { mant <<= 1; sh++; } mant &= 0x3FF; bits = sign | (uint)(127 - 14 - sh) << 23 | (mant << 13); }
            }
            else if (exp == 31) bits = sign | 0x7F800000 | (mant << 13);
            else bits = sign | (uint)(exp - 15 + 127) << 23 | (mant << 13);
            return BitConverter.Int32BitsToSingle((int)bits);
        }
    }

    /// <summary>CRC-32 (IEEE, reflected 0xEDB88320), slicing-by-8 tables.</summary>
    public static class Crc32
    {
        private static readonly uint[] T = Build();
        private static uint[] Build()
        {
            var t = new uint[8 * 256];
            for (uint i = 0; i < 256; i++) { uint c = i; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[i] = c; }
            for (int i = 0; i < 256; i++)
                for (int s = 1; s < 8; s++) t[s * 256 + i] = (t[(s - 1) * 256 + i] >> 8) ^ t[t[(s - 1) * 256 + i] & 0xFF];
            return t;
        }
        public static uint Compute(byte[] d, int off, int len)
        {
            uint c = 0xFFFFFFFFu;
            int i = off, end = off + len;
            for (; i + 8 <= end; i += 8)
            {
                uint lo = c ^ (uint)(d[i] | (d[i + 1] << 8) | (d[i + 2] << 16) | (d[i + 3] << 24));
                uint hi = (uint)(d[i + 4] | (d[i + 5] << 8) | (d[i + 6] << 16) | (d[i + 7] << 24));
                c = T[7 * 256 + (lo & 0xFF)] ^ T[6 * 256 + ((lo >> 8) & 0xFF)] ^ T[5 * 256 + ((lo >> 16) & 0xFF)] ^ T[4 * 256 + (lo >> 24)]
                  ^ T[3 * 256 + (hi & 0xFF)] ^ T[2 * 256 + ((hi >> 8) & 0xFF)] ^ T[1 * 256 + ((hi >> 16) & 0xFF)] ^ T[hi >> 24];
            }
            for (; i < end; i++) c = T[(c ^ d[i]) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }
    }
}
