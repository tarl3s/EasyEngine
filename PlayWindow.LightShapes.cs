using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EasyEngine
{
    // Light shapes (circle, beam, half circle, area, bar) and effects (flicker, pulse, blink) — the math is in LightShapes.cs.
    // A plain circle with no effect (every light made before shapes existed) keeps the original radial hole and glow exactly.
    // Any other light gets two small pictures, made once per set of settings and shared by lights that look alike: its
    // darkness mask (alpha = darkness left) and its coloured glow. Every frame only their transforms move (position,
    // direction, effect size) and one opacity (effect brightness) — nothing is allocated per frame.
    public partial class PlayWindow
    {
        private sealed class LightLook
        {
            // the settings as read (raw: a change is noticed with cheap compares, every frame)
            public string RawShape = "", RawDir = "", RawEffect = "", Color = "";
            public double Radius, Intensity, Soft, Spread, Speed, W, H;
            // what they mean
            public string Shape = LightShapes.Circle, Dir = "facing", Effect = LightShapes.None;
            public bool Plain;               // circle, no effect: the original light
            public double Seed;              // each flickering light flickers its own way
            // live, set every frame by UpdateLightShapes
            public double Angle, Scale = 1, Level = 1;
            // smooth lighting: the mask (one link of the darkness chain) and the glow's transforms
            public LightPics? Pics;
            public DrawingBrush? Mask;
            public DrawingGroup? Dim;        // a black cover over the mask's picture: its opacity = 1 − brightness
            public readonly ScaleTransform Sc = new(), GlowSc = new();
            public readonly RotateTransform Rot = new(), GlowRot = new();
            public readonly TranslateTransform Move = new();
        }

        // one light's two pictures, in its own frame: centred on the light, beam/half pointing right
        private sealed class LightPics
        {
            public WriteableBitmap MaskBmp = null!, GlowBmp = null!;
            public byte[] MaskA = Array.Empty<byte>(); // darkness per pixel (for the tests)
            public int BW, BH; public double Cell, Ex, Ey; // size in pixels, one pixel = Cell px, half extents in px
        }

        private partial class Entity
        {
            public LightLook? Look;
            public double LightAim; // a top-down character's last walking direction (degrees), for a light that follows its facing
        }

        private readonly Dictionary<string, LightPics> _lightPics = new();
        private bool _lightsDirty;     // a light changed its look: rebuild the darkness chain
        private double _lightSeedNext;
        private Rect _maskScreen;

        private LightLook ReadLook(Entity en, BehaviorData lb)
        {
            var l = new LightLook { Seed = _lightSeedNext };
            _lightSeedNext += 2.399;
            l.RawShape = lb.Text("shape", ""); l.RawDir = lb.Text("direction", ""); l.RawEffect = lb.Text("effect", "");
            l.Color = lb.Text("color", "#FFA040");
            l.Radius = lb.Num("radius", 200); l.Intensity = lb.Num("intensity", 0.9); l.Soft = lb.Num("softness", 0.5);
            l.Spread = lb.Num("spread", 40); l.Speed = lb.Num("speed", 1);
            l.Shape = LightShapes.Shape(l.RawShape); l.Dir = LightShapes.Direction(l.RawDir); l.Effect = LightShapes.Effect(l.RawEffect);
            l.Plain = l.Shape == LightShapes.Circle && l.Effect == LightShapes.None && Math.Abs(l.Soft - 0.5) < 1e-6;
            if (l.Shape is LightShapes.Area or LightShapes.Bar) { l.W = Math.Round(en.W); l.H = Math.Round(en.H); }
            return l;
        }

        // the settings changed since the look was made (a rule changed them, or an area light's object grew)
        private static bool LookChanged(LightLook l, BehaviorData lb, Entity en) =>
            !string.Equals(l.RawShape, lb.Text("shape", ""), StringComparison.Ordinal) || !string.Equals(l.RawDir, lb.Text("direction", ""), StringComparison.Ordinal)
            || !string.Equals(l.RawEffect, lb.Text("effect", ""), StringComparison.Ordinal) || !string.Equals(l.Color, lb.Text("color", "#FFA040"), StringComparison.Ordinal)
            || l.Radius != lb.Num("radius", 200) || l.Intensity != lb.Num("intensity", 0.9) || l.Soft != lb.Num("softness", 0.5)
            || l.Spread != lb.Num("spread", 40) || l.Speed != lb.Num("speed", 1)
            || (l.Shape is LightShapes.Area or LightShapes.Bar && (l.W != Math.Round(en.W) || l.H != Math.Round(en.H)));

        // The light's coloured glow (in the Lights canvas): the original ellipse for a plain light, else its glow picture.
        private FrameworkElement MakeLight(Entity en, BehaviorData light)
        {
            var look = en.Look = ReadLook(en, light);
            if (look.Plain) return MakeLight(light);
            var pics = PicsFor(look);
            look.Mask = null; look.Dim = null; // the mask is made when the darkness chain is rebuilt
            return new Image
            {
                Source = pics.GlowBmp, Width = 2 * pics.Ex, Height = 2 * pics.Ey, Stretch = Stretch.Fill, IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new TransformGroup { Children = { look.GlowSc, look.GlowRot } }
            };
        }

        private LightPics PicsFor(LightLook l)
        {
            double r = Math.Max(1, l.Radius), I = Math.Clamp(l.Intensity, 0, 1), hw = l.W / 2, hh = l.H / 2;
            string key = string.Join("|", l.Shape, r.ToString("0.#", CultureInfo.InvariantCulture), I.ToString("0.###", CultureInfo.InvariantCulture),
                                     l.Soft.ToString("0.##", CultureInfo.InvariantCulture), l.Spread.ToString("0.#", CultureInfo.InvariantCulture),
                                     hw.ToString("0", CultureInfo.InvariantCulture), hh.ToString("0", CultureInfo.InvariantCulture), l.Color);
            if (_lightPics.TryGetValue(key, out var have)) return l.Pics = have;
            if (_lightPics.Count > 48) _lightPics.Clear(); // e.g. an area light whose object keeps growing

            double ex0 = hw + r, ey0 = hh + r;
            if (l.Shape is LightShapes.Circle or LightShapes.Beam or LightShapes.Half) ex0 = ey0 = r; // turns around its centre
            double cell = Math.Max(2, Math.Ceiling(2 * Math.Max(ex0, ey0) / 256));
            int bw = (int)Math.Ceiling(2 * (ex0 + 2 * cell) / cell), bh = (int)Math.Ceiling(2 * (ey0 + 2 * cell) / cell);
            var p = new LightPics { BW = bw, BH = bh, Cell = cell, Ex = bw * cell / 2, Ey = bh * cell / 2, MaskA = new byte[bw * bh] };

            var color = ParseColor(l.Color, Colors.White);
            double dr = color.R * 0.62, dg = color.G * 0.62, db = color.B * 0.62; // the same deeper shade as the round glow
            var mask = new byte[bw * bh * 4]; var glow = new byte[bw * bh * 4];
            for (int j = 0; j < bh; j++)
                for (int i = 0; i < bw; i++)
                {
                    double lx = -p.Ex + (i + 0.5) * cell, ly = -p.Ey + (j + 0.5) * cell;
                    // the outermost ring stays fully dark, so the picture joins the darkness around it without a seam
                    var (light, g) = i < 2 || j < 2 || i >= bw - 2 || j >= bh - 2 ? (0, 0) : LightShapes.At(l.Shape, lx, ly, r, I, l.Soft, l.Spread, hw, hh);
                    int k = j * bw + i;
                    byte a = (byte)Math.Round(255 * Math.Clamp(1 - light, 0, 1));
                    p.MaskA[k] = a;
                    mask[k * 4 + 3] = a;
                    double ga = Math.Clamp(g, 0, 1);
                    glow[k * 4] = (byte)Math.Round(db * ga); glow[k * 4 + 1] = (byte)Math.Round(dg * ga); glow[k * 4 + 2] = (byte)Math.Round(dr * ga);
                    glow[k * 4 + 3] = (byte)Math.Round(255 * ga);
                }
            p.MaskBmp = new WriteableBitmap(bw, bh, 96, 96, PixelFormats.Pbgra32, null);
            p.MaskBmp.WritePixels(new Int32Rect(0, 0, bw, bh), mask, bw * 4, 0); p.MaskBmp.Freeze();
            p.GlowBmp = new WriteableBitmap(bw, bh, 96, 96, PixelFormats.Pbgra32, null);
            p.GlowBmp.WritePixels(new Int32Rect(0, 0, bw, bh), glow, bw * 4, 0); p.GlowBmp.Freeze();
            _lightPics[key] = p;
            return l.Pics = p;
        }

        // One link of the darkness chain for a shaped light: black everywhere, except its mask picture (which moves, turns
        // and scales with the light), with a black cover over the picture whose opacity dims the light (effects).
        private DrawingBrush ShapedMask(LightLook l)
        {
            var p = l.Pics!;
            var box = new Rect(-p.Ex, -p.Ey, 2 * p.Ex, 2 * p.Ey);
            var inner = new Rect(-p.Ex + p.Cell, -p.Ey + p.Cell, 2 * p.Ex - 2 * p.Cell, 2 * p.Ey - 2 * p.Cell);
            var around = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(-20000, -20000, 40000, 40000)), new RectangleGeometry(inner));
            around.Freeze();
            var cover = new RectangleGeometry(box); cover.Freeze();
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(Brushes.Black, null, around));
            group.Children.Add(new ImageDrawing(p.MaskBmp, box));
            l.Dim = new DrawingGroup { Opacity = 1 - l.Level };
            l.Dim.Children.Add(new GeometryDrawing(Brushes.Black, null, cover));
            group.Children.Add(l.Dim);
            group.Transform = new TransformGroup { Children = { l.Sc, l.Rot, l.Move } };
            l.Mask = new DrawingBrush(group)
            {
                Stretch = Stretch.Fill, TileMode = TileMode.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
                ViewboxUnits = BrushMappingMode.Absolute, ViewportUnits = BrushMappingMode.Absolute, Viewbox = _maskScreen, Viewport = _maskScreen
            };
            return l.Mask;
        }

        private Rect MaskScreen()
        {
            double w = !double.IsNaN(Root.Width) && Root.Width > 10 ? Root.Width : Root.ActualWidth > 10 ? Root.ActualWidth : 960;
            double h = !double.IsNaN(Root.Height) && Root.Height > 10 ? Root.Height : Root.ActualHeight > 10 ? Root.ActualHeight : 540;
            return new Rect(0, 0, w, h);
        }

        // Every frame, before the darkness is drawn: notice changed settings, then aim and animate every shaped light.
        private void UpdateLightShapes()
        {
            var scr = MaskScreen();
            if (scr != _maskScreen)
            {
                _maskScreen = scr;
                foreach (var en in _entities) if (en.Look?.Mask is DrawingBrush b) { b.Viewbox = scr; b.Viewport = scr; }
            }
            for (int i = 0; i < _entities.Count; i++)
            {
                var en = _entities[i];
                if (en.Dead || en.Look == null || en.LightVisual == null) continue;
                var lb = en.Beh(BehaviorKind.LightSource);
                if (lb == null) continue;
                if (LookChanged(en.Look, lb, en)) RemakeLight(en, lb);
                var l = en.Look;
                if (l.Plain) continue;
                l.Angle = l.Shape is LightShapes.Beam or LightShapes.Half ? AimOf(en, l) : en.Rot;
                (l.Scale, l.Level) = LightShapes.EffectAt(l.Effect, _gameTime, l.Speed, l.Seed);
                if (l.GlowSc.ScaleX != l.Scale) { l.GlowSc.ScaleX = l.Scale; l.GlowSc.ScaleY = l.Scale; }
                if (l.GlowRot.Angle != l.Angle) l.GlowRot.Angle = l.Angle;
                if (en.LightVisual.Opacity != l.Level) en.LightVisual.Opacity = l.Level;
            }
        }

        private void RemakeLight(Entity en, BehaviorData lb)
        {
            var old = en.LightVisual!;
            int at = Lights.Children.IndexOf(old);
            var vis = old.Visibility;
            var fresh = MakeLight(en, lb);
            fresh.Visibility = vis;
            if (at >= 0) { Lights.Children.RemoveAt(at); Lights.Children.Insert(at, fresh); } else Lights.Children.Add(fresh);
            en.LightVisual = fresh;
            Canvas.SetLeft(fresh, en.CX - fresh.Width / 2); Canvas.SetTop(fresh, en.CY - fresh.Height / 2);
            _lightsDirty = true;
        }

        // where a beam / half light points (degrees, 0 = right, 90 = down)
        private double AimOf(Entity en, LightLook l)
        {
            double fixedAngle = LightShapes.FixedAngle(l.Dir);
            if (!double.IsNaN(fixedAngle)) return fixedAngle;
            if (l.Dir == "mouse")
            {
                var m = en.ScreenSpace ? _mouseScreen : _mouseWorld;
                double dx = m.X - en.CX, dy = m.Y - en.CY;
                return dx * dx + dy * dy < 1 ? l.Angle : Math.Atan2(dy, dx) * 180 / Math.PI;
            }
            if (en.Has(BehaviorKind.TopDown)) return en.LightAim;            // the way it last walked (8 directions)
            return (en.Facing < 0 ? 180 : 0) + en.Rot;                       // the way it faces (and turns: a Spin lighthouse)
        }

        // logic tick: a top-down character's light follows the way it walks (kept when it stops)
        partial void LightsLogic()
        {
            foreach (var en in _entities)
            {
                if (en.Dead || en.Look == null || en.Look.Plain) continue;
                if (en.VX * en.VX + en.VY * en.VY > 1 && en.Has(BehaviorKind.TopDown)) en.LightAim = Math.Atan2(en.VY, en.VX) * 180 / Math.PI;
            }
        }

        // a shaped light's link in the darkness chain follows it on screen
        private void PlaceShapedMask(LightLook l, double sx, double sy)
        {
            if (l.Sc.ScaleX != l.Scale) { l.Sc.ScaleX = l.Scale; l.Sc.ScaleY = l.Scale; }
            if (l.Rot.Angle != l.Angle) l.Rot.Angle = l.Angle;
            if (l.Move.X != sx) l.Move.X = sx;
            if (l.Move.Y != sy) l.Move.Y = sy;
            if (l.Dim != null && l.Dim.Opacity != 1 - l.Level) l.Dim.Opacity = 1 - l.Level;
        }

        private static bool IsShaped(Entity en) => en.Look is { Plain: false };

        // ---------------- for the tests ----------------

        // How much the shaped lights light a world point (0 = dark, 1 = no darkness left), read from their mask pictures
        // through the same transforms the screen uses.
        private double ShapedLightAt(double wx, double wy)
        {
            double dark = 1;
            foreach (var (en, radial) in _lightMasks)
            {
                if (radial != null || en.Look?.Pics == null) continue;
                var l = en.Look; var p = l.Pics;
                double px = wx + (en.ScreenSpace ? 0 : _camX) - l.Move.X, py = wy + (en.ScreenSpace ? 0 : _camY) - l.Move.Y;
                double a = -l.Angle * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
                double lx = (px * c - py * s) / l.Scale, ly = (px * s + py * c) / l.Scale;
                int i = (int)Math.Floor((lx + p.Ex) / p.Cell), j = (int)Math.Floor((ly + p.Ey) / p.Cell);
                double light = i < 0 || j < 0 || i >= p.BW || j >= p.BH ? 0 : (1 - p.MaskA[j * p.BW + i] / 255.0) * l.Level;
                dark *= 1 - light;
            }
            return 1 - dark;
        }

        // The darkness really drawn (0-1 alpha) at a screen point: the darkness with the chain's own light-hole brushes,
        // rendered off-screen by WPF (a copy of the chain: the test window is never shown, so its own tree is never laid out).
        private double RenderedDarknessAt(double sx, double sy)
        {
            UIElement copy = new System.Windows.Shapes.Rectangle { Fill = Darkness.Fill, Opacity = Darkness.Opacity };
            for (var parent = VisualTreeHelper.GetParent(Darkness); parent is Border b; parent = VisualTreeHelper.GetParent(b))
                copy = new Border { Child = copy, OpacityMask = b.OpacityMask };
            var size = new Size(_maskScreen.Width, _maskScreen.Height);
            var host = new Grid { Width = size.Width, Height = size.Height, Children = { copy } };
            host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
            var rtb = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(host);
            var px = new byte[4];
            rtb.CopyPixels(new Int32Rect((int)Math.Clamp(sx, 0, size.Width - 1), (int)Math.Clamp(sy, 0, size.Height - 1), 1, 1), px, 4, 0);
            return px[3] / 255.0;
        }
    }
}
