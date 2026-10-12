using System.Windows;
using System.Windows.Media;

namespace EasyEngine
{
    /// <summary>The editor's theme (App.xaml resources) for windows built in code, and "#RRGGBB" → brush with a fallback.</summary>
    internal static class Theme
    {
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
