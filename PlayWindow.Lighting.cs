using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyEngine
{
    // Lighting: the scene's sky colour, the ambient darkness overlay, and lights that CUT HOLES in that
    // darkness (instead of being painted on top of everything). Each light is one radial opacity mask;
    // masks are nested, so they multiply: darkness × (1 − light₁) × (1 − light₂)… In full daylight there is
    // no darkness, so lights show nothing; at night they reveal the world around them with a soft warm tint.
    public partial class PlayWindow
    {
        private readonly List<(Entity en, RadialGradientBrush mask)> _lightMasks = new();
        private int _darkIndex = -1;           // where the darkness sits in Root.Children
        private string _darkColor = "#000000";

        // the scene's background colour is the sky (it used to be ignored → black sky)
        private void ApplySky()
        {
            Root.Background = Brush(string.IsNullOrWhiteSpace(_scene.BackgroundColor) ? "#1A1A20" : _scene.BackgroundColor);
            SetDarknessColor("#000000");
            Lights.Opacity = Math.Clamp(Darkness.Opacity * 0.55, 0, 1); // no glow in daylight, from the very first frame
        }

        private void SetDarknessColor(string hex)
        {
            if (hex == _darkColor && Darkness.Fill != null) return;
            _darkColor = hex;
            Darkness.Fill = Brush(hex);
        }

        // Called every rendered frame (and by the tests): keep one hole per visible light, at its screen position.
        private void UpdateLighting()
        {
            var lit = _entities.Where(e => !e.Dead && e.LightVisual != null && e.LightVisual.Visibility == Visibility.Visible).ToList();
            if (lit.Count != _lightMasks.Count || lit.Where((e, i) => _lightMasks[i].en != e).Any()) RebuildDarkStack(lit);

            foreach (var (en, mask) in _lightMasks)
            {
                var lb = en.Beh(BehaviorKind.LightSource);
                double r = lb?.Num("radius", 200) ?? 200;
                double sx = en.CX + (en.ScreenSpace ? 0 : _camX), sy = en.CY + (en.ScreenSpace ? 0 : _camY);
                mask.Center = mask.GradientOrigin = new Point(sx, sy);
                mask.RadiusX = mask.RadiusY = r;
            }
            // the coloured glow is a tint on top: only visible as much as it is dark (none in daylight)
            Lights.Opacity = Math.Clamp(Darkness.Opacity * 0.55, 0, 1);
        }

        private void RebuildDarkStack(List<Entity> lit)
        {
            // unwrap the darkness from any previous chain of masks
            if (_darkIndex < 0) _darkIndex = Root.Children.IndexOf(Darkness);
            UIElement outer = Darkness;
            while (VisualTreeHelper.GetParent(outer) is Border b) outer = b;
            if (Root.Children.Contains(outer)) Root.Children.Remove(outer);
            if (VisualTreeHelper.GetParent(Darkness) is Border inner) inner.Child = null;
            _lightMasks.Clear();

            UIElement chain = Darkness;
            foreach (var en in lit)
            {
                var lb = en.Beh(BehaviorKind.LightSource);
                double intensity = Math.Clamp(lb?.Num("intensity", 0.9) ?? 0.9, 0, 1);
                byte A(double darkness) => (byte)Math.Round(255 * Math.Clamp(darkness, 0, 1));
                var mask = new RadialGradientBrush
                {
                    MappingMode = BrushMappingMode.Absolute,
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(A(1 - intensity), 0, 0, 0), 0),
                        new GradientStop(Color.FromArgb(A(1 - intensity * 0.75), 0, 0, 0), 0.45),
                        new GradientStop(Color.FromArgb(A(1 - intensity * 0.3), 0, 0, 0), 0.75),
                        new GradientStop(Color.FromArgb(255, 0, 0, 0), 1)
                    }
                };
                chain = new Border { IsHitTestVisible = false, Child = chain, OpacityMask = mask };
                _lightMasks.Add((en, mask));
            }
            Root.Children.Insert(Math.Min(_darkIndex, Root.Children.Count), chain);
        }

        // ---------------- "Set time of day" (custom action + block) ----------------
        private void SetTimeOfDay(string when, string source)
        {
            var dnEn = _entities.FirstOrDefault(e => !e.Dead && e.Has(BehaviorKind.DayNight));
            if (dnEn == null) { Warn("DayNight", source, "Set time: there is no Day/Night cycle object in this scene"); return; }
            var bh = dnEn.Beh(BehaviorKind.DayNight)!;
            double dayLen = Math.Max(0.5, bh.Num("dayLength", 60)), nightLen = Math.Max(0.5, bh.Num("nightLength", 60));
            double trans = Math.Clamp(bh.Num("transition", 4), 0, Math.Min(dayLen, nightLen) / 2);
            double t = (when ?? "").Trim().ToLowerInvariant() switch
            {
                "day" or "morning" => trans,
                "noon" => dayLen / 2,
                "dusk" or "evening" => Math.Max(trans, dayLen - trans),
                "night" => dayLen,
                "midnight" => dayLen + nightLen / 2,
                var s when double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var f) => Math.Clamp(f, 0, 0.9999) * (dayLen + nightLen),
                _ => -1
            };
            if (t < 0) { Warn("DayNight", source, $"Set time: unknown time '{when}' (use day, noon, dusk, night, midnight or 0-1)"); return; }
            dnEn.DNStarted = true; dnEn.DNTimer = t;
            Info("DayNight", source, $"Time set to {when}", $"cycle time {t:0.0}s of {dayLen + nightLen:0}s");
        }
    }
}
