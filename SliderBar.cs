using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyEngine
{
    /// <summary>
    /// The editor's slider: click anywhere to jump there AND drag to slide (the native WPF Slider's thumb couldn't be
    /// dragged in this app). Every slider in the editor is made here.
    /// </summary>
    public static class SliderBar
    {
        /// <param name="step">0 = any value; otherwise values snap to multiples of it (1 = whole numbers).</param>
        /// <param name="onStart">Runs once when a click or drag begins (e.g. an undo point).</param>
        /// <param name="show">Shows the value (a label next to it); runs at the start and on every change.</param>
        public static FrameworkElement Make(double min, double max, double value, Action<double> onChange, Action<double>? show = null,
                                            double step = 0, Action? onStart = null)
        {
            const double Pad = 9, TrackH = 5, ThumbSz = 16, HostH = 24;
            var accent = Theme.Brush("Accent", "#18B98A");
            var host = new Canvas { Height = HostH, Background = Brushes.Transparent, ClipToBounds = false, Cursor = Cursors.Hand, MinWidth = 60 };
            var track = new Border { Height = TrackH, CornerRadius = new CornerRadius(TrackH / 2), Background = Theme.Brush("Border", "#35353F"), IsHitTestVisible = false };
            var fill = new Border { Height = TrackH, CornerRadius = new CornerRadius(TrackH / 2), Background = accent, IsHitTestVisible = false };
            var thumb = new Border { Width = ThumbSz, Height = ThumbSz, CornerRadius = new CornerRadius(ThumbSz / 2), Background = accent, BorderBrush = Brushes.White, BorderThickness = new Thickness(2), IsHitTestVisible = false };
            host.Children.Add(track); host.Children.Add(fill); host.Children.Add(thumb);

            double Snap(double v)
            {
                v = Math.Clamp(v, min, max);
                return step > 0 ? Math.Clamp(min + Math.Round((v - min) / step) * step, min, max) : v;
            }
            double cur = Snap(value);
            void Layout()
            {
                double w = host.ActualWidth; if (w <= 0) return;
                double usable = Math.Max(1, w - Pad * 2);
                double frac = max > min ? (cur - min) / (max - min) : 0;
                double cx = Pad + frac * usable;
                Canvas.SetTop(track, (HostH - TrackH) / 2); Canvas.SetLeft(track, 0); track.Width = w;
                Canvas.SetTop(fill, (HostH - TrackH) / 2); Canvas.SetLeft(fill, 0); fill.Width = cx;
                Canvas.SetTop(thumb, (HostH - ThumbSz) / 2); Canvas.SetLeft(thumb, cx - ThumbSz / 2);
            }
            void SetFromX(double x)
            {
                double usable = Math.Max(1, host.ActualWidth - Pad * 2);
                double v = Snap(min + Math.Clamp((x - Pad) / usable, 0, 1) * (max - min));
                if (v == cur) { Layout(); return; }
                cur = v;
                Layout(); show?.Invoke(cur); onChange(cur);
            }
            show?.Invoke(cur);
            host.SizeChanged += (s, e) => Layout();
            host.Loaded += (s, e) => Layout();
            bool dragging = false;
            host.MouseLeftButtonDown += (s, e) => { onStart?.Invoke(); dragging = true; host.CaptureMouse(); SetFromX(e.GetPosition(host).X); e.Handled = true; };
            host.MouseMove += (s, e) => { if (dragging) SetFromX(e.GetPosition(host).X); };
            host.MouseLeftButtonUp += (s, e) => { dragging = false; host.ReleaseMouseCapture(); };
            host.LostMouseCapture += (s, e) => dragging = false;
            return host;
        }
    }
}
