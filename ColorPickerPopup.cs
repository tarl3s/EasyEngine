using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Rectangle = System.Windows.Shapes.Rectangle;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace EasyEngine
{
    /// <summary>Visual color picker (hue bar + saturation/brightness square) shared by the inspector and the sprite editor.</summary>
    public static class ColorPickerPopup
    {
        public static Color HsvToColor(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360;
            double c = v * s, x = c * (1 - Math.Abs((h / 60.0) % 2 - 1)), m = v - c;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }

        public static void ColorToHsv(Color col, out double h, out double s, out double v)
        {
            double r = col.R / 255.0, g = col.G / 255.0, b = col.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            v = max;
            s = max <= 0 ? 0 : d / max;
            if (d <= 0) h = 0;
            else if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * (((b - r) / d) + 2);
            else h = 60 * (((r - g) / d) + 4);
            if (h < 0) h += 360;
        }

        public static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        private static object Res(string key) => Application.Current.TryFindResource(key) ?? Brushes.Gray;

        /// <param name="onOpen">Called once when the picker opens (e.g. push an undo snapshot).</param>
        public static void Open(FrameworkElement anchor, string initial, Action<string> onPick, Action? onOpen = null)
        {
            const double Sz = 180;
            Color start; try { start = (Color)ColorConverter.ConvertFromString(initial); } catch { start = Colors.Gray; }
            ColorToHsv(start, out double H, out double S, out double V);

            var hueBg = new SolidColorBrush(HsvToColor(H, 1, 1));
            var sv = new Canvas { Width = Sz, Height = Sz, Background = hueBg, ClipToBounds = true };
            var satRect = new Rectangle { Width = Sz, Height = Sz, IsHitTestVisible = false, Fill = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), new Point(0, 0.5), new Point(1, 0.5)) };
            var valRect = new Rectangle { Width = Sz, Height = Sz, IsHitTestVisible = false, Fill = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, new Point(0.5, 0), new Point(0.5, 1)) };
            var svDot = new Ellipse { Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
            sv.Children.Add(satRect); sv.Children.Add(valRect); sv.Children.Add(svDot);

            var hg = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
            for (int i = 0; i <= 6; i++) hg.GradientStops.Add(new GradientStop(HsvToColor(i * 60, 1, 1), i / 6.0));
            var hue = new Canvas { Width = 22, Height = Sz, Background = hg, Margin = new Thickness(10, 0, 0, 0) };
            var hueMarker = new Rectangle { Width = 26, Height = 3, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 0.5, IsHitTestVisible = false };
            Canvas.SetLeft(hueMarker, -2); hue.Children.Add(hueMarker);

            var top = new StackPanel { Orientation = Orientation.Horizontal };
            top.Children.Add(sv); top.Children.Add(hue);

            var hexBox = new TextBox { Width = 120, Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
            var ok = new Button { Content = "Select", Height = 28, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(8, 0, 0, 0), Style = Res("AccentButton") as Style };
            var bottom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            bottom.Children.Add(hexBox); bottom.Children.Add(ok);

            var panel = new StackPanel();
            panel.Children.Add(top); panel.Children.Add(bottom);
            var rootBorder = new Border { Background = Res("Bg1") as Brush, BorderBrush = Res("Border") as Brush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10), Child = panel };
            var pop = new System.Windows.Controls.Primitives.Popup { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, Child = rootBorder };

            bool syncing = false;
            void Positions()
            {
                Canvas.SetLeft(svDot, S * Sz - 6); Canvas.SetTop(svDot, (1 - V) * Sz - 6);
                Canvas.SetTop(hueMarker, (H / 360.0) * Sz - 1.5);
            }
            void Emit(bool writeHex)
            {
                hueBg.Color = HsvToColor(H, 1, 1);
                Positions();
                var hex = ColorToHex(HsvToColor(H, S, V));
                if (writeHex) { syncing = true; hexBox.Text = hex; syncing = false; }
                onPick(hex);
            }

            void SvSet(Point p) { S = Math.Clamp(p.X / Sz, 0, 1); V = Math.Clamp(1 - p.Y / Sz, 0, 1); Emit(true); }
            sv.MouseLeftButtonDown += (s, e) => { sv.CaptureMouse(); SvSet(e.GetPosition(sv)); };
            sv.MouseMove += (s, e) => { if (sv.IsMouseCaptured) SvSet(e.GetPosition(sv)); };
            sv.MouseLeftButtonUp += (s, e) => sv.ReleaseMouseCapture();

            void HueSet(Point p) { H = Math.Clamp(p.Y / Sz, 0, 1) * 360; Emit(true); }
            hue.MouseLeftButtonDown += (s, e) => { hue.CaptureMouse(); HueSet(e.GetPosition(hue)); };
            hue.MouseMove += (s, e) => { if (hue.IsMouseCaptured) HueSet(e.GetPosition(hue)); };
            hue.MouseLeftButtonUp += (s, e) => hue.ReleaseMouseCapture();

            hexBox.TextChanged += (s, e) =>
            {
                if (syncing) return;
                try { ColorToHsv((Color)ColorConverter.ConvertFromString(hexBox.Text.Trim()), out H, out S, out V); Emit(false); } catch { }
            };
            ok.Click += (s, e) => pop.IsOpen = false;

            onOpen?.Invoke();
            syncing = true; hexBox.Text = ColorToHex(HsvToColor(H, S, V)); syncing = false;
            Positions();
            pop.IsOpen = true;
        }
    }
}
