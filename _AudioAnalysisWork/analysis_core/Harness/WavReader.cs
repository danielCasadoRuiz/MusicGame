using System;
using System.IO;

namespace Harness
{
    /// <summary>Minimal RIFF/WAVE reader: PCM 16/24/32-bit int and 32-bit float, any channel count.</summary>
    public static class WavReader
    {
        public sealed class Wav { public float[] interleaved; public int channels, sampleRate; public int Frames => interleaved.Length / channels; }

        public static Wav Read(string path)
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            if (new string(br.ReadChars(4)) != "RIFF") throw new InvalidDataException("not RIFF");
            br.ReadInt32();
            if (new string(br.ReadChars(4)) != "WAVE") throw new InvalidDataException("not WAVE");
            int fmt = 0, ch = 0, sr = 0, bits = 0;
            while (fs.Position + 8 <= fs.Length)
            {
                string id = new string(br.ReadChars(4));
                int size = br.ReadInt32();
                long next = fs.Position + size + (size & 1);
                if (id == "fmt ")
                {
                    fmt = br.ReadInt16(); ch = br.ReadInt16(); sr = br.ReadInt32(); br.ReadInt32(); br.ReadInt16(); bits = br.ReadInt16();
                    if (fmt == unchecked((short)0xFFFE) || fmt == -2) { br.ReadInt16(); br.ReadInt16(); br.ReadInt32(); fmt = br.ReadInt16(); }
                }
                else if (id == "data")
                {
                    if (ch == 0) throw new InvalidDataException("data before fmt");
                    int bps = bits / 8;
                    int count = size / bps;
                    var data = br.ReadBytes(count * bps);
                    var o = new float[count];
                    if (fmt == 3 && bits == 32) Buffer.BlockCopy(data, 0, o, 0, count * 4);
                    else if (bits == 16) for (int i = 0; i < count; i++) o[i] = (short)(data[2 * i] | (data[2 * i + 1] << 8)) / 32768f;
                    else if (bits == 24) for (int i = 0; i < count; i++) { int v = data[3 * i] | (data[3 * i + 1] << 8) | ((sbyte)data[3 * i + 2] << 16); o[i] = v / 8388608f; }
                    else if (bits == 32) for (int i = 0; i < count; i++) o[i] = BitConverter.ToInt32(data, 4 * i) / 2147483648f;
                    else throw new InvalidDataException("unsupported bits " + bits);
                    return new Wav { interleaved = o, channels = ch, sampleRate = sr };
                }
                fs.Position = next;
            }
            throw new InvalidDataException("no data chunk");
        }

        /// <summary>Approximation of Unity's importer "Normalize": scale so the peak is 1.0.</summary>
        public static void PeakNormalize(float[] x)
        {
            float m = 0f;
            for (int i = 0; i < x.Length; i++) { float a = Math.Abs(x[i]); if (a > m) m = a; }
            if (m <= 0f) return;
            float g = 1f / m;
            for (int i = 0; i < x.Length; i++) x[i] *= g;
        }
    }
}
