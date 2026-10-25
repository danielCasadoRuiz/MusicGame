using System;
using System.Text;

namespace MusicGame.Audio.Analysis
{
    /// <summary>
    /// Deterministic 64-bit FNV-1a hasher. Unlike string.GetHashCode / HashCode it is stable
    /// across processes, platforms and runtime versions, so it can be persisted in profile headers
    /// (params hash, audio identity, tag reuse key). Not cryptographic — it detects accidental
    /// mismatches (stale profile, other audio revision), not tampering.
    /// </summary>
    public struct StableHash
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime  = 1099511628211UL;

        private ulong _h;
        private bool  _started;

        public ulong Value => _started ? _h : Offset;

        public static StableHash Create() { var h = new StableHash(); h._h = Offset; h._started = true; return h; }

        public void Add(byte b)
        {
            if (!_started) { _h = Offset; _started = true; }
            _h ^= b;
            _h *= Prime;
        }

        public void Add(int v)    { for (int i = 0; i < 4; i++) Add((byte)(v >> (8 * i))); }
        public void Add(long v)   { for (int i = 0; i < 8; i++) Add((byte)(v >> (8 * i))); }
        public void Add(ulong v)  { Add((long)v); }
        public void Add(bool v)   { Add((byte)(v ? 1 : 0)); }
        /// <summary>Hashes the exact IEEE bit pattern (no culture/format dependence).</summary>
        public void Add(float v)  { Add(BitConverter.SingleToInt32Bits(v)); }
        public void Add(double v) { Add(BitConverter.DoubleToInt64Bits(v)); }

        public void Add(string s)
        {
            if (s == null) { Add(-1); return; }
            var bytes = Encoding.UTF8.GetBytes(s);
            Add(bytes.Length);
            for (int i = 0; i < bytes.Length; i++) Add(bytes[i]);
        }

        public void Add(byte[] data, int offset, int count)
        {
            if (!_started) { _h = Offset; _started = true; }
            ulong h = _h;
            int end = offset + count;
            for (int i = offset; i < end; i++) { h ^= data[i]; h *= Prime; }
            _h = h;
        }

        /// <summary>Hash of float PCM by bit pattern. Use for in-memory identity of decoded audio
        /// (NOTE: decoded compressed audio can differ per platform/decoder — prefer hashing the
        /// source asset bytes + import settings for published content).</summary>
        public static ulong HashPcm(float[] samples, int channels, int frequency)
        {
            var h = Create();
            h.Add(channels);
            h.Add(frequency);
            h.Add(samples?.Length ?? 0);
            if (samples != null)
            {
                ulong v = h._h;
                for (int i = 0; i < samples.Length; i++)
                {
                    int bits = BitConverter.SingleToInt32Bits(samples[i]);
                    v ^= (byte)bits;         v *= Prime;
                    v ^= (byte)(bits >> 8);  v *= Prime;
                    v ^= (byte)(bits >> 16); v *= Prime;
                    v ^= (byte)(bits >> 24); v *= Prime;
                }
                h._h = v;
            }
            return h.Value;
        }
    }
}
