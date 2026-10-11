using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EasyEngine
{
    /// <summary>
    /// Small vector drawings of tools, so the player can SEE which tool is selected — in the hand and on
    /// the hotbar. Names containing "pick" draw a pickaxe, "axe" an axe, "sword"/"blade" a sword; anything
    /// else is a coloured tile with its initial. Drawn in a 24×24 design box, scaled to <c>size</c>.
    /// </summary>
    public static class ToolArt
    {
        private static readonly Brush Handle = Frozen("#7A5230"), HandleEdge = Frozen("#4A311C"), Edge = Frozen("#222228");

        /// <param name="grip">where the hand holds it, in the returned canvas' coordinates (size × size).</param>
        public static Canvas Make(string name, string colorHex, double size, out Point grip)
        {
            var c = new Canvas { Width = size, Height = size, IsHitTestVisible = false };
            var art = new Canvas { Width = 24, Height = 24, RenderTransform = new ScaleTransform(size / 24.0, size / 24.0) };
            c.Children.Add(art);
            Brush head = Brushes.Silver;
            try { head = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)); } catch { }
            string n = (name ?? "").ToLowerInvariant();
            grip = new Point(12 * size / 24.0, 20.5 * size / 24.0);

            if (n.Contains("pick"))
            {
                Bar(art, 11, 5, 2.4, 18.5, Handle, HandleEdge);
                Poly(art, head, Edge, (2, 7), (7, 3.2), (12, 2.2), (17, 3.2), (22, 7), (20.5, 8.4), (16, 5.6), (12, 4.9), (8, 5.6), (3.5, 8.4));
            }
            else if (n.Contains("axe"))
            {
                Bar(art, 12, 3, 2.4, 20.5, Handle, HandleEdge);
                Poly(art, head, Edge, (12.2, 4), (6, 2.5), (3.5, 6.5), (4, 10.5), (6.5, 12.5), (12.2, 9.5));
                Poly(art, Frozen("#F2F2F2"), null, (5.2, 4.2), (4.4, 6.6), (4.8, 9.8), (5.8, 10.6), (5.4, 7));  // bright cutting edge
            }
            else if (n.Contains("sword") || n.Contains("blade"))
            {
                Poly(art, head, Edge, (11, 15), (11, 4), (12, 1.5), (13, 4), (13, 15));
                Bar(art, 7.5, 15, 9, 2, Frozen("#C9A646"), Edge);
                Bar(art, 11, 17, 2, 5, Handle, HandleEdge);
                var pommel = new Ellipse { Width = 3.2, Height = 3.2, Fill = Frozen("#C9A646"), Stroke = Edge, StrokeThickness = 0.5 };
                Canvas.SetLeft(pommel, 10.4); Canvas.SetTop(pommel, 21.2); art.Children.Add(pommel);
                grip = new Point(12 * size / 24.0, 19.5 * size / 24.0);
            }
            else
            {
                var tile = new Rectangle { Width = 18, Height = 18, RadiusX = 3, RadiusY = 3, Fill = head, Stroke = Edge, StrokeThickness = 0.8 };
                Canvas.SetLeft(tile, 3); Canvas.SetTop(tile, 3); art.Children.Add(tile);
                var letter = new TextBlock { Text = n.Length > 0 ? char.ToUpperInvariant(n[0]).ToString() : "?", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Width = 18, TextAlignment = TextAlignment.Center };
                Canvas.SetLeft(letter, 3); Canvas.SetTop(letter, 4.5); art.Children.Add(letter);
                grip = new Point(12 * size / 24.0, 12 * size / 24.0);
            }
            return c;
        }

        /// <summary>"pickaxe" → "Pickaxe" (for labels and console lines).</summary>
        public static string Title(string name) => string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);

        private static void Bar(Canvas c, double x, double y, double w, double h, Brush fill, Brush? edge)
        {
            var r = new Rectangle { Width = w, Height = h, RadiusX = 0.8, RadiusY = 0.8, Fill = fill, Stroke = edge, StrokeThickness = 0.5 };
            Canvas.SetLeft(r, x); Canvas.SetTop(r, y); c.Children.Add(r);
        }

        private static void Poly(Canvas c, Brush fill, Brush? edge, params (double x, double y)[] pts)
        {
            var p = new Polygon { Fill = fill, Stroke = edge, StrokeThickness = 0.6 };
            foreach (var (x, y) in pts) p.Points.Add(new Point(x, y));
            c.Children.Add(p);
        }

        private static Brush Frozen(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    }
}
