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

        // The Sky canvas (screen-fixed, behind every layer) holds the scene's Sky layer and the Day/Night sun & moon
        // (even if they were put on another layer). Sun & moon are drawn under the night darkness, so each one also
        // cuts a hole the size of its disc — the moon stays bright at night. Other sky objects (clouds…) just darken.
        private readonly HashSet<Entity> _skyBodies = new();

        private void PutInSky(Entity e)
        {
            if (VisualTreeHelper.GetParent(e.Visual) == Sky) return;
            if (VisualTreeHelper.GetParent(e.Visual) is Panel parent) parent.Children.Remove(e.Visual);
            Sky.Children.Add(e.Visual);
            e.ScreenSpace = true; // positioned in screen pixels, no camera
        }

        private void MoveToSky(Entity e)
        {
            if (_skyBodies.Add(e)) PutInSky(e);
        }

        private (double radius, double intensity) LightParams(Entity en)
        {
            if (_skyBodies.Contains(en)) return (Math.Max(en.W, en.H) * 0.62, 1.0);
            var lb = en.Beh(BehaviorKind.LightSource);
            return (lb?.Num("radius", 200) ?? 200, Math.Clamp(lb?.Num("intensity", 0.9) ?? 0.9, 0, 1));
        }

        // Called every rendered frame (and by the tests): keep one hole per visible light, at its screen position.
        private void UpdateLighting()
        {
            var lit = _entities.Where(e => !e.Dead && !_skyBodies.Contains(e) && e.LightVisual != null && e.LightVisual.Visibility == Visibility.Visible)
                      .Concat(_skyBodies.Where(e => !e.Dead && e.Visual.Visibility == Visibility.Visible)).ToList();
            if (lit.Count != _lightMasks.Count || lit.Where((e, i) => _lightMasks[i].en != e).Any()) RebuildDarkStack(lit);

            foreach (var (en, mask) in _lightMasks)
            {
                double r = LightParams(en).radius;
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
                double intensity = LightParams(en).intensity;
                byte A(double darkness) => (byte)Math.Round(255 * Math.Clamp(darkness, 0, 1));
                var mask = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute };
                if (_skyBodies.Contains(en)) // a crisp disc: the body itself, with a thin soft rim
                {
                    mask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
                    mask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.8));
                    mask.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 1));
                }
                else
                {
                    mask.GradientStops.Add(new GradientStop(Color.FromArgb(A(1 - intensity), 0, 0, 0), 0));
                    mask.GradientStops.Add(new GradientStop(Color.FromArgb(A(1 - intensity * 0.75), 0, 0, 0), 0.45));
                    mask.GradientStops.Add(new GradientStop(Color.FromArgb(A(1 - intensity * 0.3), 0, 0, 0), 0.75));
                    mask.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 1));
                }
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
