using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace EasyEngine
{
    /// <summary>The editor's theme (App.xaml resources) for windows built in code, and "#RRGGBB" → brush with a fallback.</summary>
    internal static class Theme
    {
        /// <summary>The editor themes: "dark" (default) and "light". Each is the colour of every palette brush in App.xaml.</summary>
        public static readonly string[] Names = { "dark", "light" };

        // Keys of the App.xaml palette. Light: text ≥ 4.5:1 on every background it sits on (tested by --selftest);
        // the accent is darker there, so it reads as text on white, and text ON the accent turns white.
        private static readonly Dictionary<string, (string Dark, string Light)> Palette = new()
        {
            ["Bg0"] = ("#141418", "#E9ECF1"),
            ["Bg1"] = ("#1C1C22", "#F6F7F9"),
            ["Bg2"] = ("#24242B", "#FFFFFF"),
            ["Bg3"] = ("#2E2E37", "#E2E6EC"),
            ["Hover"] = ("#34343F", "#DCE1E8"),
            ["Border"] = ("#35353F", "#C6CCD5"),
            ["Text"] = ("#E6E6EA", "#1B1E24"),
            ["TextDim"] = ("#8C8C97", "#565D69"),
            ["Accent"] = ("#18B98A", "#087A59"),
            ["AccentHover"] = ("#1ED29B", "#0A9068"),
            ["Danger"] = ("#E5484B", "#B4232A"),
            ["AccentText"] = ("#062018", "#FFFFFF"),
            ["Viewport"] = ("#111115", "#D5D9E0"),      // around the scene canvas
            ["ViewportCard"] = ("#1A1A20", "#C9CED7"),  // the scene's frame inside it
        };

        public static string Current { get; private set; } = "dark";
        public static bool IsLight => Current == "light";

        /// <summary>The palette colours of <paramref name="name"/> (for tests and previews).</summary>
        public static Dictionary<string, Color> PaletteOf(string name) =>
            Palette.ToDictionary(kv => kv.Key, kv => ParseColor(name == "light" ? kv.Value.Light : kv.Value.Dark));

        private static bool _ready;

        /// <summary>Sets the theme for this run ("dark"/"light"; unknown → dark), then loads the styles (Styles.xaml).
        /// Called once, first thing at startup: WPF freezes application resources and the styles keep the very brushes
        /// they loaded with (CardButton even recognises the active button by its Accent brush), so the theme can't change
        /// under open windows — a new choice applies the next time EasyEngine starts.</summary>
        public static void Init(string? name)
        {
            if (_ready || Application.Current == null) return;
            _ready = true;
            Current = name == "light" ? "light" : "dark";
            var res = Application.Current.Resources;
            foreach (var (key, color) in PaletteOf(Current)) res[key] = new SolidColorBrush(color);
            res.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/EasyEngine;component/Styles.xaml") });
        }

        /// <summary>A colour for the current theme: <paramref name="dark"/> or <paramref name="light"/>.</summary>
        public static Brush Pick(string dark, string light) => Hex(IsLight ? light : dark);

        /// <summary>The theme brush <paramref name="key"/>, or <paramref name="fallbackHex"/> when there is none (e.g. no App).</summary>
        public static Brush Brush(string key, string fallbackHex) => Application.Current?.TryFindResource(key) as Brush ?? Hex(fallbackHex);

        public static Style? Style(string key) => Application.Current?.TryFindResource(key) as Style;

        /// <summary>Parses a colour ("#RRGGBB", "#AARRGGBB", "red"…); a bad <paramref name="hex"/> gives <paramref name="fallback"/>, then gray.</summary>
        public static Color ParseColor(string? hex, string? fallback = null)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch
            {
                try { return (Color)ColorConverter.ConvertFromString(fallback); }
                catch { return Colors.Gray; }
            }
        }

        /// <summary>A brush of <see cref="ParseColor"/>, optionally with its alpha replaced.</summary>
        public static SolidColorBrush Hex(string? hex, string? fallback = null, byte? alpha = null)
        {
            var c = ParseColor(hex, fallback);
            return new SolidColorBrush(alpha is byte a ? Color.FromArgb(a, c.R, c.G, c.B) : c);
        }
    }
}
