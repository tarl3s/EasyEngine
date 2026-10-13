using System;

namespace EasyEngine
{
    /// <summary>
    /// The Light behavior's shapes and effects: the words a person (or the AI) may use for them, and the math of how
    /// much a light lights a point. Shared by the smooth lighting (one picture per light), the block lighting
    /// (light per block), the AI clean-up and the tests, so all of them agree on what a "beam" is.
    /// A light with no shape (every project made before shapes existed) is a circle, exactly as before.
    /// </summary>
    public static class LightShapes
    {
        public const string Circle = "circle", Beam = "beam", Half = "half", Area = "area", Bar = "bar";
        public const string None = "none", Flicker = "flicker", Pulse = "pulse", Blink = "blink";

        /// <summary>The shape a word means ("flashlight" → beam, "window" → area, "neon tube" → bar); unknown or empty → circle.</summary>
        public static string Shape(string? word) => Lookup(word, ShapeWords, Circle);

        /// <summary>Where a beam / half light points: facing (follows the object), right, left, up, down or mouse.</summary>
        public static string Direction(string? word) => Lookup(word, DirectionWords, "facing");

        /// <summary>none, flicker (candle/torch), pulse (slowly grows and shrinks) or blink (on/off).</summary>
        public static string Effect(string? word) => Lookup(word, EffectWords, None);

        private static readonly (string value, string[] words)[] ShapeWords =
        {
            (Beam, new[] { "beam", "cone", "flashlight", "spotlight", "spot", "headlight", "headlights", "lanterna", "feixe", "searchlight", "lighthouse", "farol" }),
            (Half, new[] { "half", "halfcircle", "semicircle", "semi", "dome", "walllamp", "meiocirculo", "meialua" }),
            (Area, new[] { "area", "rect", "rectangle", "box", "square", "window", "janela", "room", "shaft", "zone", "fill" }),
            (Bar, new[] { "bar", "line", "tube", "neon", "strip", "stripe", "fluorescent", "barra", "linha", "tubo" }),
            (Circle, new[] { "circle", "round", "point", "radial", "all", "circulo" }),
        };
        private static readonly (string value, string[] words)[] DirectionWords =
        {
            ("facing", new[] { "facing", "face", "faces", "forward", "front", "ahead", "player", "frente" }),
            ("mouse", new[] { "mouse", "cursor", "pointer", "aim", "mira", "rato" }),
            ("right", new[] { "right", "direita", "east", "0" }),
            ("left", new[] { "left", "esquerda", "west", "180" }),
            ("up", new[] { "up", "upward", "upwards", "top", "cima", "north", "above", "270", "-90", "sky", "ceiling" }),
            ("down", new[] { "down", "downward", "downwards", "bottom", "baixo", "south", "below", "floor", "ground", "90" }),
        };
        private static readonly (string value, string[] words)[] EffectWords =
        {
            (None, new[] { "none", "steady", "off", "no", "nothing", "constant" }),
            (Flicker, new[] { "flicker", "flickers", "flickering", "candle", "torch", "fire", "flame", "vela", "tocha", "fogo", "tremula", "tremer" }),
            (Pulse, new[] { "pulse", "pulses", "pulsing", "breathe", "breathing", "throb", "pulsar", "pulsa" }),
            (Blink, new[] { "blink", "blinks", "blinking", "flash", "flashing", "strobe", "alarm", "siren", "piscar", "pisca", "alarme" }),
        };

        // the exact word first; else the first table word (4+ letters) inside it ("neon tube" → neon, "toward the mouse" → mouse)
        private static string Lookup(string? word, (string value, string[] words)[] table, string fallback)
        {
            var w = Squash(word);
            if (w.Length == 0) return fallback;
            foreach (var (value, words) in table) if (Array.IndexOf(words, w) >= 0) return value;
            foreach (var (value, words) in table)
                foreach (var k in words) if (k.Length >= 4 && w.Contains(k, StringComparison.Ordinal)) return value;
            return fallback;
        }

        // lower case, letters and digits only (a leading minus kept: "-90")
        private static string Squash(string? w)
        {
            if (string.IsNullOrWhiteSpace(w)) return "";
            Span<char> buf = stackalloc char[Math.Min(w.Length, 40)];
            int n = 0;
            foreach (char c in w.Trim())
            {
                if (n == buf.Length) break;
                if (char.IsLetterOrDigit(c) || (c == '-' && n == 0)) buf[n++] = char.ToLowerInvariant(c);
            }
            return new string(buf[..n]);
        }

        /// <summary>The direction's angle in degrees (screen: 0 = right, 90 = down), or NaN for facing/mouse.</summary>
        public static double FixedAngle(string direction) => direction switch
        {
            "right" => 0, "down" => 90, "left" => 180, "up" => -90, _ => double.NaN
        };

        /// <summary>Darkness left at normalized distance d (0 = the source, 1 = the light's reach): the original circle's
        /// profile — a clear core, a short soft edge. softness 0.5 (the default) = exactly the circle every light had before.</summary>
        public static double Dark(double d, double intensity, double softness)
        {
            double soft = Math.Clamp(softness, 0, 1);
            double core = 1 - 0.64 * soft, mid = 1 - 0.3 * soft;      // 0.5 → 0.68 and 0.85
            double dc = Math.Max(0, 1 - intensity * 1.1), dm = 1 - intensity * 0.5;
            if (d >= 1) return 1;
            if (d <= core) return dc;
            if (d <= mid) return mid - core < 1e-9 ? dm : dc + (dm - dc) * (d - core) / (mid - core);
            return dm + (1 - dm) * (d - mid) / (1 - mid);
        }

        /// <summary>The coloured glow's strength (0-1, before intensity) at normalized distance d — the original circle's tint.</summary>
        public static double Glow(double d)
        {
            if (d >= 1) return 0;
            if (d <= 0) return 0.45;
            if (d <= 0.6) return 0.45 - 0.11 * d / 0.6;
            if (d <= 0.88) return 0.34 - 0.14 * (d - 0.6) / 0.28;
            return 0.2 * (1 - d) / 0.12;
        }

        /// <summary>How far a point (lx, ly) in the light's own frame is from the light's source, as a part of its reach:
        /// a point (circle, beam, half), the segment along the object's length (bar) or the object's rectangle (area).</summary>
        public static double Distance(string shape, double lx, double ly, double radius, double halfW, double halfH)
        {
            double r = Math.Max(1, radius);
            switch (shape)
            {
                case Area:
                {
                    double dx = Math.Max(0, Math.Abs(lx) - halfW), dy = Math.Max(0, Math.Abs(ly) - halfH);
                    return Math.Sqrt(dx * dx + dy * dy) / r;
                }
                case Bar:
                {
                    // along its longer side: a wide object glows along its width, a tall one along its height
                    double dx = halfW >= halfH ? Math.Max(0, Math.Abs(lx) - halfW) : lx, dy = halfW >= halfH ? ly : Math.Max(0, Math.Abs(ly) - halfH);
                    return Math.Sqrt(dx * dx + dy * dy) / r;
                }
                default: return Math.Sqrt(lx * lx + ly * ly) / r;
            }
        }

        /// <summary>For beam and half lights (pointing along +x): 1 inside the cone, 0 outside, a soft band at its sides.
        /// Other shapes: 1.</summary>
        public static double Cone(string shape, double lx, double ly, double spreadDeg, double softness)
        {
            if (shape != Beam && shape != Half) return 1;
            if (lx * lx + ly * ly < 4) return 1;
            double half = shape == Half ? 90 : Math.Clamp(spreadDeg, 5, 170) / 2;
            double band = Math.Max(2, (shape == Half ? 60 : 2 * half) * Math.Clamp(softness, 0, 1) * 0.8);
            double a = Math.Abs(Math.Atan2(ly, lx)) * 180 / Math.PI;
            double lo = half - band / 2, hi = half + band / 2;
            if (a <= lo) return 1;
            if (a >= hi) return 0;
            double u = (hi - a) / (hi - lo);
            return u * u * (3 - 2 * u); // smoothstep
        }

        /// <summary>A beam also lights a little around whoever holds it (so the holder isn't in the dark): its radius.</summary>
        public static double HolderRadius(double radius) => Math.Clamp(radius * 0.15, 14, 48);

        /// <summary>How much a light lights the point (lx, ly) in its own frame (0-1, 1 = no darkness left) and how strong
        /// its coloured glow is there (0-1).</summary>
        public static (double light, double glow) At(string shape, double lx, double ly, double radius, double intensity, double softness,
                                                     double spreadDeg, double halfW, double halfH)
        {
            double d = Distance(shape, lx, ly, radius, halfW, halfH);
            double cone = Cone(shape, lx, ly, spreadDeg, softness);
            double light = (1 - Dark(d, intensity, softness)) * cone, glow = intensity * Glow(d) * cone;
            if (shape == Beam)
            {
                double dh = Math.Sqrt(lx * lx + ly * ly) / HolderRadius(radius);
                light = Math.Max(light, (1 - Dark(dh, intensity, softness)) * 0.85);
                glow = Math.Max(glow, intensity * Glow(dh) * 0.6);
            }
            return (light, glow);
        }

        /// <summary>An effect at game time t: how much bigger the light is (scale) and how bright (level, 0-1).
        /// flicker = small smooth random changes (each light its own, from seed), pulse = slowly grows and shrinks,
        /// blink = on half of the time. speed 1 = normal.</summary>
        public static (double scale, double level) EffectAt(string effect, double t, double speed, double seed)
        {
            double s = Math.Clamp(speed, 0.05, 20);
            switch (effect)
            {
                case Flicker:
                {
                    double f = 0.5 * Math.Sin(t * s * 9.1 + seed) + 0.3 * Math.Sin(t * s * 15.7 + 2.3 * seed) + 0.2 * Math.Sin(t * s * 27.3 + 4.1 * seed);
                    return (1 + 0.05 * f, 0.88 + 0.12 * f);
                }
                case Pulse:
                {
                    double p = Math.Sin(2 * Math.PI * t * s * 0.5);
                    return (1 + 0.12 * p, 0.92 + 0.08 * p);
                }
                case Blink:
                {
                    double ph = t * s;
                    return (1, ph - Math.Floor(ph) < 0.5 ? 1 : 0);
                }
                default: return (1, 1);
            }
        }
    }
}
