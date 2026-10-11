using System;
using System.IO;

namespace EasyEngine
{
    /// <summary>
    /// Tiny procedural sound-effect generator: returns 16-bit mono PCM WAV bytes (22.05 kHz).
    /// Fixed seeds, so the same call always produces the same file. Every clip is ≥150 ms with 5 ms
    /// fades at both ends (MediaPlayer can clip or click very short sounds).
    /// </summary>
    public static class SoundSynth
    {
        public const int Rate = 22050;

        /// <summary>A tool swinging through the air: band-limited noise that rises and falls.</summary>
        public static byte[] Whoosh()
        {
            var s = Buffer(0.26);
            var rng = new Random(11);
            double lo = 0, lo2 = 0;
            for (int i = 0; i < s.Length; i++)
            {
                double t = (double)i / s.Length;
                double cutoff = 0.04 + 0.30 * Math.Sin(Math.PI * t);          // brightest in the middle of the swing
                double n = rng.NextDouble() * 2 - 1;
                lo += cutoff * (n - lo);                                       // low-pass (air)
                lo2 += 0.02 * (lo - lo2);                                      // very slow low-pass, subtracted = high-pass
                double env = Math.Pow(Math.Sin(Math.PI * t), 1.6);
                s[i] = (lo - lo2) * env;
            }
            return Finish(s, 0.75);
        }

        /// <summary>An axe biting into wood: a click plus a short, woody, quickly-damped "toc".</summary>
        public static byte[] Chop()
        {
            var s = Buffer(0.22);
            var rng = new Random(23);
            double noiseLo = 0;
            for (int i = 0; i < s.Length; i++)
            {
                double t = (double)i / Rate;
                double click = t < 0.004 ? (rng.NextDouble() * 2 - 1) * (1 - t / 0.004) : 0;
                double body = 0.9 * Math.Sin(2 * Math.PI * (190 - 40 * t) * t) * Math.Exp(-t / 0.045)
                            + 0.5 * Math.Sin(2 * Math.PI * 430 * t) * Math.Exp(-t / 0.025);
                noiseLo += 0.25 * ((rng.NextDouble() * 2 - 1) - noiseLo);
                double grain = noiseLo * Math.Exp(-t / 0.03) * 0.7;
                s[i] = click * 0.8 + body + grain;
            }
            return Finish(s, 0.85);
        }

        /// <summary>A tree giving way: a crackle of breaking fibres, then a low thud as it lands.</summary>
        public static byte[] TreeFall()
        {
            var s = Buffer(0.80);
            var rng = new Random(37);
            double rumble = 0;
            for (int i = 0; i < s.Length; i++)
            {
                double t = (double)i / Rate;
                // crackle: sparse random snaps, denser at the start
                double crackle = 0;
                if (t < 0.32 && rng.NextDouble() < 0.06 * (1 - t / 0.32)) crackle = (rng.NextDouble() * 2 - 1) * 0.9;
                // thud: a falling low tone + rumble once it hits the ground
                double thud = 0;
                if (t > 0.30)
                {
                    double u = t - 0.30;
                    thud = Math.Sin(2 * Math.PI * (75 - 25 * Math.Min(u, 0.4)) * u) * Math.Exp(-u / 0.16);
                    rumble += 0.04 * ((rng.NextDouble() * 2 - 1) - rumble);
                    thud += rumble * 3.0 * Math.Exp(-u / 0.2);
                }
                s[i] = crackle + thud;
            }
            return Finish(s, 0.9);
        }

        /// <summary>A soft low "bonk-bonk" for something that can't be done (wrong tool / nothing in hand).</summary>
        public static byte[] Denied()
        {
            var s = Buffer(0.20);
            double lo = 0;
            for (int i = 0; i < s.Length; i++)
            {
                double t = (double)i / Rate;
                double f = t < 0.09 ? 220 : 165;
                double local = t < 0.09 ? t : t - 0.10;
                double sq = Math.Sign(Math.Sin(2 * Math.PI * f * t));
                double gate = (t < 0.09 || t > 0.10) ? Math.Exp(-local / 0.05) : 0;
                lo += 0.12 * (sq * gate - lo);                                  // soften the square
                s[i] = lo;
            }
            return Finish(s, 0.55);
        }

        // ---------------- helpers ----------------
        private static double[] Buffer(double seconds) => new double[Math.Max((int)(Rate * Math.Max(0.15, seconds)), 1)];

        // Normalize to the peak, add 5 ms fades, and wrap as a WAV file.
        private static byte[] Finish(double[] s, double peak)
        {
            double max = 1e-9;
            foreach (var v in s) max = Math.Max(max, Math.Abs(v));
            int fade = Rate * 5 / 1000;
            var pcm = new short[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                double g = 1;
                if (i < fade) g = (double)i / fade;
                else if (i > s.Length - fade) g = (double)(s.Length - i) / fade;
                pcm[i] = (short)Math.Clamp(s[i] / max * peak * g * short.MaxValue, short.MinValue, short.MaxValue);
            }
            return Wav(pcm);
        }

        public static byte[] Wav(short[] pcm)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            int dataBytes = pcm.Length * 2;
            w.Write("RIFF"u8); w.Write(36 + dataBytes); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); // PCM, mono
            w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(dataBytes);
            foreach (var v in pcm) w.Write(v);
            w.Flush();
            return ms.ToArray();
        }
    }
}
