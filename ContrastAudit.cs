using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EasyEngine
{
    /// <summary>Measures every visible text of a window against the colour really drawn behind it (used by --selftest,
    /// in both themes): the backgrounds of its ancestors and of the siblings painted under it, blended through their
    /// opacity the way WPF composes them.</summary>
    public static class ContrastAudit
    {
        /// <summary>A text that is too faint for its background.</summary>
        public sealed record Finding(string Text, double Ratio, double Need, Color Fore, Color Back, string Where)
        {
            public override string ToString() =>
                $"\"{Text}\" {Ratio:0.0}:1 (needs {Need:0.#}) text {Hex(Fore)} on {Hex(Back)} — {Where}";
        }

        /// <summary>Texts under <paramref name="root"/> below 4.5:1 (3:1 for large text: ≥ 24px, or bold ≥ 18.66px).
        /// Hidden and disabled things are left out (WCAG exempts disabled controls), and so is any subtree
        /// <paramref name="skip"/> picks (e.g. the scene, which shows the game's own colours). <paramref name="measured"/>
        /// counts the texts checked; texts over an image (unknown background) aren't counted.</summary>
        public static List<Finding> Audit(Visual root, Func<DependencyObject, bool>? skip, out int measured)
        {
            var found = new List<Finding>();
            var seen = new HashSet<string>();
            int count = 0;
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (n is UIElement u && (!u.IsVisible || u.Opacity <= 0.01)) continue;
                if (skip != null && skip(n)) continue;
                if (n is TextBlock tb && tb.IsEnabled && tb.ActualWidth >= 1)
                    foreach (var (text, fg, size, weight) in Runs(tb)) Check(tb, tb, text, fg, size, weight);
                else if (n is TextBox box && box.IsEnabled && box.Text.Trim().Length > 0)
                    Check(box, box.Template?.FindName("PART_ContentHost", box) as FrameworkElement ?? box, box.Text, box.Foreground, box.FontSize, box.FontWeight);
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(n); i++) stack.Push(VisualTreeHelper.GetChild(n, i));
            }
            measured = count;
            return found;

            void Check(FrameworkElement owner, FrameworkElement start, string text, Brush? fg, double size, FontWeight weight)
            {
                text = text.Trim();
                if (text.Length == 0 || ColorOf(fg) is not Color f) return;
                if (Measure(root, start, f) is not (Color fore, Color back)) return;
                count++;
                double need = size >= 24 || (size >= 18.66 && weight.ToOpenTypeWeight() >= 700) ? 3 : 4.5;
                double ratio = Contrast.Ratio(fore, back);
                if (ratio >= need) return;
                if (text.Length > 48) text = text.Substring(0, 47) + "…";
                if (seen.Add($"{text}|{fore}|{back}")) found.Add(new Finding(text, ratio, need, fore, back, Where(owner)));
            }
        }

        // the pieces of a TextBlock with their own colour and size (a Run can have its own)
        private static IEnumerable<(string text, Brush? fg, double size, FontWeight weight)> Runs(TextBlock tb)
        {
            if (tb.Inlines.Count == 0) { yield return (tb.Text, tb.Foreground, tb.FontSize, tb.FontWeight); yield break; }
            var stack = new Stack<Inline>();
            foreach (var i in tb.Inlines) stack.Push(i);
            while (stack.Count > 0)
            {
                var inline = stack.Pop();
                if (inline is Run r) yield return (r.Text, r.Foreground, r.FontSize, r.FontWeight);
                else if (inline is Span s) foreach (var i in s.Inlines) stack.Push(i);
            }
        }

        /// <summary>The colour of a pixel of the text and of a pixel just beside it (null when an image or an unknown
        /// backdrop is behind). Each pixel is kept as "k × backdrop + c": a layer under what we have so far, or the opacity
        /// of a group (an element and everything inside it), updates both; the walk goes up to the window.</summary>
        private static (Color, Color)? Measure(Visual root, FrameworkElement start, Color fg)
        {
            Point center;
            try { center = start.TransformToAncestor(root).Transform(new Point(start.ActualWidth / 2, start.ActualHeight / 2)); }
            catch { return null; }

            double fa = fg.A / 255.0;
            var text = new Pixel(1 - fa, fg, fa);
            var beside = new Pixel(1, Colors.Black, 0);
            bool unknown = false;

            void Under(UIElement el, Brush? brush, double opacity)
            {
                if (brush == null) return;
                if (ColorOf(brush) is not Color c) { if (beside.K > 0.001) unknown = true; return; }
                double a = c.A / 255.0 * opacity;
                if (a <= 0.001) return;
                text.Under(c, a); beside.Under(c, a);
            }

            UIElement cur = start;
            Under(cur, OwnBackground(cur), 1);
            text.Group(cur.Opacity); beside.Group(cur.Opacity);
            while (VisualTreeHelper.GetParent(cur) is UIElement parent)
            {
                if (parent is Panel)
                {
                    // the siblings painted before this one (lower in z-order) that cover the text
                    int idx = -1, count = VisualTreeHelper.GetChildrenCount(parent);
                    for (int i = 0; i < count; i++) if (VisualTreeHelper.GetChild(parent, i) == cur) { idx = i; break; }
                    for (int i = idx - 1; i >= 0; i--)
                        if (VisualTreeHelper.GetChild(parent, i) is UIElement sib && sib.IsVisible && OwnBackground(sib) is Brush sb && Covers(root, sib, center))
                            Under(sib, sb, sib.Opacity);
                }
                Under(parent, OwnBackground(parent), 1);
                text.Group(parent.Opacity); beside.Group(parent.Opacity);
                cur = parent;
                if (ReferenceEquals(cur, root)) break;
            }
            if (unknown || beside.K > 0.01) return null;
            return (text.Color, beside.Color);
        }

        private static Brush? OwnBackground(UIElement el) => el switch
        {
            Panel p => p.Background,
            Border b => b.Background,
            TextBlock t => t.Background,
            Shape s => s.Fill,
            _ => null,
        };

        private static bool Covers(Visual root, UIElement sib, Point p)
        {
            try { return sib.TransformToAncestor(root).TransformBounds(new Rect(sib.RenderSize)).Contains(p); }
            catch { return false; }
        }

        /// <summary>A brush's colour (a gradient gives the average of its stops); null for images and other drawings.</summary>
        private static Color? ColorOf(Brush? brush)
        {
            switch (brush)
            {
                case SolidColorBrush s:
                    return Color.FromArgb((byte)Math.Round(s.Color.A * Math.Clamp(s.Opacity, 0, 1)), s.Color.R, s.Color.G, s.Color.B);
                case GradientBrush g when g.GradientStops.Count > 0:
                    double a = 0, r = 0, gr = 0, b = 0;
                    foreach (var st in g.GradientStops) { a += st.Color.A; r += st.Color.R; gr += st.Color.G; b += st.Color.B; }
                    int n = g.GradientStops.Count;
                    return Color.FromArgb((byte)(a / n * Math.Clamp(g.Opacity, 0, 1)), (byte)(r / n), (byte)(gr / n), (byte)(b / n));
                default:
                    return null;
            }
        }

        // a pixel as "K × (what is behind) + premultiplied colour"
        private sealed class Pixel
        {
            public double K, R, G, B;
            public Pixel(double k, Color c, double a) { K = k; R = c.R * a; G = c.G * a; B = c.B * a; }
            public void Under(Color c, double a) { R += K * a * c.R; G += K * a * c.G; B += K * a * c.B; K *= 1 - a; }
            public void Group(double o)
            {
                if (o >= 0.999) return;
                o = Math.Clamp(o, 0, 1);
                K = o * K + 1 - o; R *= o; G *= o; B *= o;
            }
            public Color Color => Color.FromRgb(Byte(R), Byte(G), Byte(B));
            private static byte Byte(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
        }

        private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        // where a text is: the nearest named element and the types around it
        private static string Where(FrameworkElement el)
        {
            var parts = new List<string>();
            DependencyObject? d = el;
            string? named = null;
            for (int i = 0; d != null && i < 40; i++, d = VisualTreeHelper.GetParent(d))
            {
                if (i < 4) parts.Add(d.GetType().Name);
                if (named == null && d is FrameworkElement fe && fe.Name.Length > 0) named = fe.Name;
            }
            parts.Reverse();
            return string.Join(">", parts) + (named != null ? $" in {named}" : "");
        }
    }
}
