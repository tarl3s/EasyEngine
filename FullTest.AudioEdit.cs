using System;
using System.IO;
using System.Linq;
using System.Text;

namespace EasyEngine;

// The sound editor ("✂ Edit…" next to a sound): reading WAVs of every common kind, each effect, and the window saving
// a NEW sound (the original is never touched). All headless — nothing is played or shown.
public partial class App
{
    partial void FeatureTestsAudioEdit(FullTestKit t)
    {
        const int Rate = 22050;
        static float[] Sine(double hz, double seconds, float amp = 0.5f, int rate = Rate)
            => Enumerable.Range(0, (int)(seconds * rate)).Select(i => (float)(amp * Math.Sin(2 * Math.PI * hz * i / rate))).ToArray();
        static AudioData Mono(float[] s) => new(Rate, new[] { s });
        static int Crossings(float[] s, int from, int to)
        {
            int n = 0;
            for (int i = Math.Max(1, from); i < to; i++) if ((s[i - 1] < 0) != (s[i] < 0)) n++;
            return n;
        }
        // a hand-made WAV: header + optional extra chunks + data, so the reader is tested on files it didn't write
        static byte[] Wav(int format, int bits, int channels, byte[] data, params (string id, byte[] body)[] extra)
        {
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(0); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)format); w.Write((short)channels);
            w.Write(Rate); w.Write(Rate * channels * bits / 8); w.Write((short)(channels * bits / 8)); w.Write((short)bits);
            foreach (var (id, body) in extra)
            {
                w.Write(Encoding.ASCII.GetBytes(id)); w.Write(body.Length); w.Write(body);
                if ((body.Length & 1) == 1) w.Write((byte)0); // chunks are padded to an even size
            }
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(data.Length); w.Write(data);
            var b = ms.ToArray(); BitConverter.GetBytes(b.Length - 8).CopyTo(b, 4);
            return b;
        }

        var tone = Sine(440, 1.0);

        t.Test("audio edit: a 16-bit WAV round-trips (same rate, channels, length; samples within 1/16000)", () =>
        {
            var back = AudioEdit.ReadWav(AudioEdit.ToWav16(Mono(tone)));
            return back.Rate == Rate && back.Channels.Length == 1 && back.Length == tone.Length
                   && Enumerable.Range(0, tone.Length).All(i => Math.Abs(back.Channels[0][i] - tone[i]) < 1 / 16000f);
        });
        t.Test("audio edit: a 32-bit float stereo WAV is read (both channels, exact values)", () =>
        {
            var data = new byte[tone.Length * 8];
            for (int i = 0; i < tone.Length; i++)
            {
                BitConverter.GetBytes(tone[i]).CopyTo(data, i * 8);
                BitConverter.GetBytes(-tone[i]).CopyTo(data, i * 8 + 4);
            }
            var a = AudioEdit.ReadWav(Wav(3, 32, 2, data));
            return a.Channels.Length == 2 && a.Length == tone.Length && a.Channels[0][100] == tone[100] && a.Channels[1][100] == -tone[100];
        });
        t.Test("audio edit: 8-bit and 24-bit PCM WAVs are read", () =>
        {
            var d8 = tone.Select(v => (byte)Math.Round(v * 127 + 128)).ToArray();
            var d24 = new byte[tone.Length * 3];
            for (int i = 0; i < tone.Length; i++)
            {
                int v = (int)Math.Round(tone[i] * 8388607);
                d24[i * 3] = (byte)v; d24[i * 3 + 1] = (byte)(v >> 8); d24[i * 3 + 2] = (byte)(v >> 16);
            }
            var a8 = AudioEdit.ReadWav(Wav(1, 8, 1, d8));
            var a24 = AudioEdit.ReadWav(Wav(1, 24, 1, d24));
            return a8.Length == tone.Length && Math.Abs(a8.Channels[0][50] - tone[50]) < 0.01
                   && a24.Length == tone.Length && Math.Abs(a24.Channels[0][50] - tone[50]) < 1e-5 && a24.Channels[0].Min() < -0.49f;
        });
        t.Test("audio edit: extra chunks (LIST, an odd-sized one) before the data are skipped", () =>
        {
            var pcm = AudioEdit.ToWav16(Mono(tone)).Skip(44).ToArray();
            var a = AudioEdit.ReadWav(Wav(1, 16, 1, pcm, ("LIST", Encoding.ASCII.GetBytes("INFOISFT\u0005\0\0\0Test\0")), ("odd ", new byte[] { 1, 2, 3 })));
            return a.Length == tone.Length && Math.Abs(a.Channels[0][200] - tone[200]) < 1 / 16000f;
        });
        t.Test("audio edit: something that isn't a WAV (an MP3, junk) is refused with a message", () =>
        {
            bool Refused(byte[] b) { try { AudioEdit.ReadWav(b); return false; } catch (InvalidDataException e) { return e.Message.Length > 0; } }
            return Refused(new byte[] { 0x49, 0x44, 0x33, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }) && Refused(Array.Empty<byte>())
                   && Refused(Wav(2, 4, 1, new byte[10])); // ADPCM: a WAV it can't decode
        });

        t.Test("audio edit: cutting 0.25 s → 0.75 s keeps half a second", () =>
            Math.Abs(AudioEdit.Apply(Mono(tone), new AudioEffects { TrimStart = 0.25, TrimEnd = 0.75 }).Length - Rate / 2) <= 1);
        t.Test("audio edit: speed 2× halves the length (and doubles the pitch); 0.5× doubles it", () =>
        {
            var fast = AudioEdit.Apply(Mono(tone), new AudioEffects { Speed = 2 }).Channels[0];
            var slow = AudioEdit.Apply(Mono(tone), new AudioEffects { Speed = 0.5 }).Length;
            int c0 = Crossings(tone, 0, tone.Length / 2), c1 = Crossings(fast, 0, fast.Length);
            return Math.Abs(fast.Length - tone.Length / 2) <= 1 && Math.Abs(slow - tone.Length * 2) <= 2 && Math.Abs(c1 - 2 * c0) <= 4;
        });
        t.Test("audio edit: pitch +5 semitones keeps the length and raises the tone (≈ ×1.33); −12 lowers it an octave", () =>
        {
            var up = AudioEdit.Apply(Mono(tone), new AudioEffects { Pitch = 5 }).Channels[0];
            var down = AudioEdit.Apply(Mono(tone), new AudioEffects { Pitch = -12 }).Channels[0];
            int mid0 = tone.Length / 4, mid1 = tone.Length * 3 / 4;
            double baseC = Crossings(tone, mid0, mid1), upR = Crossings(up, mid0, mid1) / baseC, downR = Crossings(down, mid0, mid1) / baseC;
            return Math.Abs(up.Length - tone.Length) <= tone.Length * 0.02 && Math.Abs(down.Length - tone.Length) <= tone.Length * 0.02
                   && Math.Abs(upR - Math.Pow(2, 5 / 12.0)) < 0.12 && Math.Abs(downR - 0.5) < 0.08;
        });
        t.Test("audio edit: reverse plays it backwards", () =>
        {
            var ramp = Enumerable.Range(0, 1000).Select(i => i / 1000f).ToArray();
            var r = AudioEdit.Apply(Mono(ramp), new AudioEffects { Reverse = true }).Channels[0];
            return r.Length == 1000 && r[0] == ramp[999] && r[999] == ramp[0];
        });
        t.Test("audio edit: fade in starts silent, fade out ends silent, the middle is untouched", () =>
        {
            var full = Enumerable.Repeat(0.5f, Rate).ToArray();
            var f = AudioEdit.Apply(Mono(full), new AudioEffects { FadeIn = 0.2, FadeOut = 0.2 }).Channels[0];
            return f[0] == 0 && Math.Abs(f[^1]) < 0.001 && f[Rate / 10] < 0.3f && f[Rate / 2] == 0.5f;
        });
        t.Test("audio edit: a strong echo makes it longer but never louder than −1…1; 200% volume is kept inside too", () =>
        {
            var loud = Sine(220, 1.0, amp: 1f);
            var echo = AudioEdit.Apply(Mono(loud), new AudioEffects { Echo = 1 });
            var gain = AudioEdit.Apply(Mono(loud), new AudioEffects { Volume = 2 });
            var half = AudioEdit.Apply(Mono(tone), new AudioEffects { Volume = 0.5 });
            return echo.Length > loud.Length && AudioEdit.Peak(echo) <= 1f && AudioEdit.Peak(gain) <= 1f
                   && Math.Abs(AudioEdit.Peak(half) - 0.25f) < 0.01f;
        });
        t.Test("audio edit: no effects = the same sound; every effect at once still gives a playable sound", () =>
        {
            var same = AudioEdit.Apply(Mono(tone), new AudioEffects());
            var all = AudioEdit.Apply(Mono(tone), new AudioEffects { TrimStart = 0.1, TrimEnd = 0.9, Speed = 1.5, Pitch = -3, Volume = 1.5, FadeIn = 0.1, FadeOut = 0.1, Reverse = true, Echo = 0.5 });
            return same.Channels[0].SequenceEqual(tone) && all.Length > 0 && AudioEdit.Peak(all) <= 1f
                   && all.Channels[0].All(v => !float.IsNaN(v)) && AudioEdit.ReadWav(AudioEdit.ToWav16(all)).Length == all.Length;
        });
        t.Test("audio edit: the waveform peaks cover the sound (one min/max pair per column)", () =>
        {
            var (mn, mx) = AudioEdit.Peaks(Mono(tone), 300);
            return mn.Length == 300 && mx.Length == 300 && mx.Max() > 0.49f && mn.Min() < -0.49f && mn.Zip(mx).All(p => p.First <= p.Second);
        });

        // the window, built but never shown: cut, speed up, save as a NEW file next to the untouched original
        t.Test("audio edit: the editor window cuts, applies effects and saves a new sound (the original stays)", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "EE_FullAudio_" + Guid.NewGuid().ToString("N").Substring(0, 6)); // cleaned with EE_Full*
            Directory.CreateDirectory(dir);
            var project = ProjectIO.CreateNew("Audio", dir);
            var original = ProjectIO.SaveSound(project, AudioEdit.ToWav16(Mono(tone)), "beep");
            var originalBytes = File.ReadAllBytes(ProjectIO.AbsoluteAsset(project, original)!);
            var w = new AudioEditorWindow(project, AudioEdit.ReadWav(ProjectIO.AbsoluteAsset(project, original)!), "beep");
            try
            {
                bool sliders = w.TestSliderCount == 6;
                w.TestEffects.Speed = 2;
                w.TestTrim(0, 0.5); // keep the first half, then twice as fast → a quarter second
                bool len = Math.Abs(w.TestResult.Length - Rate / 4) <= 2;
                var rel = w.TestSave("beep_edited");
                var again = w.TestSave("beep_edited");
                var saved = AudioEdit.ReadWav(ProjectIO.AbsoluteAsset(project, rel)!);
                return sliders && len && rel.EndsWith("beep_edited.wav") && again.EndsWith("beep_edited_1.wav")
                       && Math.Abs(saved.Length - Rate / 4) <= 2 && w.SavedRelativePath == again
                       && File.ReadAllBytes(ProjectIO.AbsoluteAsset(project, original)!).SequenceEqual(originalBytes);
            }
            finally { w.Close(); TestScenarios.TryDeleteDir(dir); }
        });
    }
}
