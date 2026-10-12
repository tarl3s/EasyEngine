using System;
using System.Windows;
using System.Windows.Media;

namespace EasyEngine
{
    /// <summary>WCAG contrast between two colours (1 = identical … 21 = black on white). Readable text needs ≥ 4.5.</summary>
    public static class Contrast
    {
        public static double Ratio(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        public static double Ratio(Brush? fg, Brush? bg) =>
            fg is SolidColorBrush f && bg is SolidColorBrush b ? Ratio(f.Color, b.Color) : 0;

        private static double Luminance(Color c)
        {
            static double Ch(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        }

        /// <summary>The Background/Foreground a style sets for a property (null if it doesn't set it).</summary>
        public static object? StyleValue(Style? style, DependencyProperty dp)
        {
            for (var s = style; s != null; s = s.BasedOn)
                foreach (var setterBase in s.Setters)
                    if (setterBase is Setter setter && setter.Property == dp) return setter.Value;
            return null;
        }
    }
}
