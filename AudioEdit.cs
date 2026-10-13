using System;
using System.IO;
using System.Text;

namespace EasyEngine
{
    /// <summary>A decoded sound: one float array per channel (−1…1), all the same length.</summary>
    public sealed class AudioData
    {
        public int Rate { get; }
        public float[][] Channels { get; }
        public int Length => Channels.Length == 0 ? 0 : Channels[0].Length;
        public double Seconds => Rate > 0 ? Length / (double)Rate : 0;

        public AudioData(int rate, float[][] channels)
        {
            if (channels.Length == 0) throw new ArgumentException("No channels.");
            Rate = Math.Max(1, rate); Channels = channels;
        }
    }

    /// <summary>What the audio editor does to a sound, in the order it is applied: trim → reverse → speed → pitch →
    /// volume → echo → fades → a limiter that keeps the result inside −1…1.</summary>
    public sealed class AudioEffects
    {
        public double TrimStart;        // seconds from the start of the original
        public double TrimEnd = -1;     // seconds; ≤ 0 (or past the end) = the end
        public double Speed = 1;        // 0.5…2: like a tape (faster = higher and shorter)
        public double Pitch;            // semitones, −12…12: keeps the length
        public double Volume = 1;       // 0…2
        public double FadeIn, FadeOut;  // seconds
        public bool Reverse;
        public double Echo;             // 0…1

        public bool IsNeutral => TrimStart <= 0 && TrimEnd <= 0 && Speed == 1 && Pitch == 0 && Volume == 1
                                 && FadeIn <= 0 && FadeOut <= 0 && !Reverse && Echo <= 0;
    }

    /// <summary>The audio editor's sound processing: read WAV files, apply <see cref="AudioEffects"/>, write 16-bit WAV.
    /// No libraries; never throws on odd-but-valid files (extra chunks, odd sizes, a wrong data size).</summary>
    public static class AudioEdit
    {
        public const double EchoDelay = 0.25; // seconds between echoes

        // ---------------------------------------------------------------- reading
        public static AudioData ReadWav(string path) => ReadWav(File.ReadAllBytes(path));

        /// <summary>Reads a RIFF/WAVE file: PCM 8/16/24/32-bit or 32/64-bit float (also WAVE_FORMAT_EXTENSIBLE), any rate,
        /// any number of channels (more than two are kept as the first two). Throws <see cref="InvalidDataException"/>
        /// with a plain message when it isn't a WAV it can read.</summary>
        public static AudioData ReadWav(byte[] b)
        {
            if (b.Length < 12 || Ascii(b, 0) != "RIFF" || Ascii(b, 8) != "WAVE")
                throw new InvalidDataException(L.T("This is not a WAV file."));
            int pos = 12;
            int format = 0, channels = 0, rate = 0, bits = 0;
            int dataStart = -1, dataLen = 0;
            while (pos + 8 <= b.Length)
            {
                string id = Ascii(b, pos);
                long size = BitConverter.ToUInt32(b, pos + 4);
                int body = pos + 8;
                long avail = b.Length - body;
                if (id == "fmt " && size >= 16 && avail >= 16)
                {
                    format = BitConverter.ToUInt16(b, body);
                    channels = BitConverter.ToUInt16(b, body + 2);
                    rate = (int)BitConverter.ToUInt32(b, body + 4);
                    bits = BitConverter.ToUInt16(b, body + 14);
                    if (format == 0xFFFE && size >= 40 && avail >= 26) format = BitConverter.ToUInt16(b, body + 24); // extensible: the sub-format
                }
                else if (id == "data")
                {
                    dataStart = body;
                    dataLen = (int)Math.Min(size, avail); // a streamed file may say 0 or 0xFFFFFFFF
                    if (size == 0 || size == 0xFFFFFFFF) dataLen = (int)avail;
                    if (format != 0) break;
                }
                long next = body + size + (size & 1); // chunks are padded to an even size
                if (next <= pos || next > b.Length) break;
                pos = (int)next;
            }
            if (format == 0 || channels <= 0 || rate <= 0)
                throw new InvalidDataException(L.T("The WAV file has no sound format."));
            if (dataStart < 0) throw new InvalidDataException(L.T("The WAV file has no sound data."));
            bool isFloat = format == 3;
            if (!(format == 1 && (bits == 8 || bits == 16 || bits == 24 || bits == 32)) && !(isFloat && (bits == 32 || bits == 64)))
                throw new InvalidDataException(L.F("This kind of WAV can't be edited ({0}-bit, format {1}).", bits, format));

            int bps = bits / 8, frame = bps * channels;
            int frames = dataLen / frame;
            int keep = Math.Min(channels, 2);
            var ch = new float[keep][];
            for (int c = 0; c < keep; c++) ch[c] = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                int o = dataStart + i * frame;
                for (int c = 0; c < keep; c++)
                {
                    int p = o + c * bps;
                    ch[c][i] = isFloat
                        ? (bits == 32 ? BitConverter.ToSingle(b, p) : (float)BitConverter.ToDouble(b, p))
                        : bits switch
                        {
                            8 => (b[p] - 128) / 128f,
                            16 => BitConverter.ToInt16(b, p) / 32768f,
                            24 => ((b[p] | (b[p + 1] << 8) | (b[p + 2] << 16)) << 8 >> 8) / 8388608f,
                            _ => BitConverter.ToInt32(b, p) / 2147483648f,
                        };
                    if (float.IsNaN(ch[c][i])) ch[c][i] = 0;
                }
            }
            return new AudioData(rate, ch);
        }

        private static string Ascii(byte[] b, int at) => at + 4 <= b.Length ? Encoding.ASCII.GetString(b, at, 4) : "";

        // ---------------------------------------------------------------- writing
        /// <summary>16-bit PCM WAV bytes (values outside −1…1 are clipped).</summary>
        public static byte[] ToWav16(AudioData a)
        {
            int ch = a.Channels.Length, n = a.Length, dataLen = n * ch * 2;
            var ms = new MemoryStream(44 + dataLen);
            using (var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataLen); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)ch);
                w.Write(a.Rate); w.Write(a.Rate * ch * 2); w.Write((short)(ch * 2)); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataLen);
                for (int i = 0; i < n; i++)
                    for (int c = 0; c < ch; c++)
                    {
                        float v = Math.Clamp(a.Channels[c][i], -1f, 1f);
                        w.Write((short)Math.Round(v * 32767f));
                    }
            }
            return ms.ToArray();
        }

        // ---------------------------------------------------------------- effects
        /// <summary>The whole chain: a new sound, the source is never changed.</summary>
        public static AudioData Apply(AudioData src, AudioEffects fx)
        {
            var a = Trim(src, fx.TrimStart, fx.TrimEnd);
            if (fx.Reverse) a = Reverse(a);
            if (Math.Abs(fx.Speed - 1) > 1e-6) a = ChangeSpeed(a, fx.Speed);
            if (Math.Abs(fx.Pitch) > 1e-6) a = ShiftPitch(a, fx.Pitch);
            if (Math.Abs(fx.Volume - 1) > 1e-6) a = Gain(a, fx.Volume);
            if (fx.Echo > 0) a = AddEcho(a, fx.Echo);
            if (fx.FadeIn > 0 || fx.FadeOut > 0) a = Fade(a, fx.FadeIn, fx.FadeOut);
            return Limit(a);
        }

        private static AudioData Map(AudioData a, Func<float[], float[]> perChannel)
        {
            var ch = new float[a.Channels.Length][];
            for (int c = 0; c < ch.Length; c++) ch[c] = perChannel(a.Channels[c]);
            return new AudioData(a.Rate, ch);
        }

        public static AudioData Trim(AudioData a, double startSec, double endSec)
        {
            int n = a.Length;
            int s = Math.Clamp((int)Math.Round(Math.Max(0, startSec) * a.Rate), 0, n);
            int e = endSec <= 0 ? n : Math.Clamp((int)Math.Round(endSec * a.Rate), 0, n);
            if (e < s) (s, e) = (e, s);
            int len = e - s;
            return Map(a, src => { var d = new float[len]; Array.Copy(src, s, d, 0, len); return d; });
        }

        public static AudioData Reverse(AudioData a) =>
            Map(a, src => { var d = (float[])src.Clone(); Array.Reverse(d); return d; });

        /// <summary>Plays the sound faster or slower, like a tape: 2 = twice as fast (half as long, an octave higher).</summary>
        public static AudioData ChangeSpeed(AudioData a, double speed)
        {
            speed = Math.Clamp(speed, 0.1, 10);
            int len = (int)Math.Floor(a.Length / speed);
            return Map(a, src =>
            {
                var d = new float[len];
                for (int i = 0; i < len; i++) d[i] = Sample(src, i * speed);
                return d;
            });
        }

        /// <summary>Raises or lowers the pitch by <paramref name="semitones"/> and keeps the length: overlapping grains
        /// (Hann windows, 50 % overlap, which add up to 1) are each read faster or slower from where they sit.</summary>
        public static AudioData ShiftPitch(AudioData a, double semitones)
        {
            double ratio = Math.Pow(2, semitones / 12.0);
            int grain = Math.Max(64, (int)(a.Rate * 0.06)) & ~1; // ~60 ms
            int hop = grain / 2;
            var win = new float[grain];
            for (int k = 0; k < grain; k++) win[k] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * k / grain)); // periodic Hann
            int n = a.Length;
            return Map(a, src =>
            {
                var d = new float[n];
                for (int o = -hop; o < n; o += hop)
                {
                    // a grain that would read past the end starts earlier so it reads real sound, not silence
                    double readFrom = o;
                    double span = grain * ratio;
                    if (ratio > 1 && readFrom + span > n) readFrom = Math.Max(0, n - span);
                    for (int k = 0; k < grain; k++)
                    {
                        int i = o + k;
                        if (i < 0 || i >= n) continue;
                        d[i] += win[k] * Sample(src, readFrom + k * ratio);
                    }
                }
                return d;
            });
        }

        public static AudioData Gain(AudioData a, double gain)
        {
            float g = (float)Math.Max(0, gain);
            return Map(a, src => { var d = new float[src.Length]; for (int i = 0; i < d.Length; i++) d[i] = src[i] * g; return d; });
        }

        /// <summary>Repeats the sound every <see cref="EchoDelay"/> seconds, each repeat quieter; the sound gets longer so
        /// the last echoes aren't cut. 0 = no echo, 1 = strong.</summary>
        public static AudioData AddEcho(AudioData a, double amount)
        {
            double fb = 0.6 * Math.Clamp(amount, 0, 1);
            if (fb <= 0) return a;
            int delay = Math.Max(1, (int)(EchoDelay * a.Rate));
            int repeats = Math.Clamp((int)Math.Ceiling(Math.Log(0.02) / Math.Log(fb)), 1, 10);
            int len = a.Length + delay * repeats;
            return Map(a, src =>
            {
                var d = new float[len];
                Array.Copy(src, d, src.Length);
                for (int i = delay; i < len; i++) d[i] += (float)(fb * d[i - delay]);
                return d;
            });
        }

        public static AudioData Fade(AudioData a, double inSec, double outSec)
        {
            int n = a.Length;
            int fi = Math.Clamp((int)(Math.Max(0, inSec) * a.Rate), 0, n);
            int fo = Math.Clamp((int)(Math.Max(0, outSec) * a.Rate), 0, n);
            return Map(a, src =>
            {
                var d = (float[])src.Clone();
                for (int i = 0; i < fi; i++) d[i] *= i / (float)fi;
                for (int i = 0; i < fo; i++) d[n - 1 - i] *= i / (float)fo;
                return d;
            });
        }

        /// <summary>Keeps the sound inside −1…1: if anything is louder, the whole sound is turned down just enough.</summary>
        public static AudioData Limit(AudioData a)
        {
            float peak = Peak(a);
            if (peak <= 1f) return a;
            return Gain(a, 0.98 / peak);
        }

        public static float Peak(AudioData a)
        {
            float peak = 0;
            foreach (var ch in a.Channels) foreach (var v in ch) { float m = Math.Abs(v); if (m > peak) peak = m; }
            return peak;
        }

        private static float Sample(float[] src, double pos)
        {
            if (pos < 0 || pos > src.Length - 1) return 0;
            int i = (int)pos; double f = pos - i;
            if (i + 1 >= src.Length) return src[i];
            return (float)(src[i] + (src[i + 1] - src[i]) * f);
        }

        // ---------------------------------------------------------------- drawing
        /// <summary>The lowest and highest value (channels mixed) in each of <paramref name="columns"/> slices — what a
        /// waveform drawing needs.</summary>
        public static (float[] min, float[] max) Peaks(AudioData a, int columns)
        {
            columns = Math.Max(1, columns);
            var mn = new float[columns]; var mx = new float[columns];
            int n = a.Length; if (n == 0) return (mn, mx);
            int ch = a.Channels.Length;
            for (int col = 0; col < columns; col++)
            {
                int s = (int)((long)col * n / columns), e = Math.Max(s + 1, (int)((long)(col + 1) * n / columns));
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = s; i < e && i < n; i++)
                {
                    float v = 0;
                    for (int c = 0; c < ch; c++) v += a.Channels[c][i];
                    v /= ch;
                    if (v < lo) lo = v; if (v > hi) hi = v;
                }
                mn[col] = lo == float.MaxValue ? 0 : lo; mx[col] = hi == float.MinValue ? 0 : hi;
            }
            return (mn, mx);
        }
    }
}
