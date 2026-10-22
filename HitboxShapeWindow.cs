using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace EasyEngine
{
    /// <summary>The ⬠ Hitbox shape editor: the object's picture with its hitbox as a polygon on top. Click to add a corner
    /// (on a side, it goes into that side), drag a corner to move it, right-click a corner to remove it; or start from the
    /// box's corners or trace the drawing. The corners are kept as fractions of the box (PhysicsData.Shape).</summary>
    public class HitboxShapeWindow : Window
    {
        private const double MaxW = 560, MaxH = 420, Pad = 16, Grab = 9;
        private readonly List<PolyPoint> _pts;
        private readonly BitmapSource? _picture;
        private readonly double _w, _h;                 // the picture as shown
        private readonly Canvas _canvas;
        private readonly Polygon _poly;
        private readonly TextBlock _info;
        private int _drag = -1;

        /// <summary>What was chosen: the shape, or null for "the box" (no shape).</summary>
        public List<PolyPoint>? Result { get; private set; }

        /// <summary>Opens the editor for an object; true when the person chose a shape (or removed it). Result holds it.</summary>
        public static bool Edit(Window owner, Project project, GameObjectData obj, out List<PolyPoint>? shape)
        {
            var w = new HitboxShapeWindow(project, obj) { Owner = owner };
            bool ok = w.ShowDialog() == true;
            shape = w.Result;
            return ok;
        }

        public HitboxShapeWindow(Project project, GameObjectData obj)
        {
            _pts = obj.Physics.Shape?.Select(p => new PolyPoint(p.X, p.Y)).ToList() ?? new List<PolyPoint>();
            _picture = PictureOf(project, obj);
            double s = Math.Min(MaxW / Math.Max(1, obj.Width), MaxH / Math.Max(1, obj.Height));
            s = Math.Min(s, 8); // a tiny object isn't blown up past 8×
            _w = Math.Max(40, obj.Width * s); _h = Math.Max(40, obj.Height * s);

            Title = L.T("Hitbox shape");
            SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Theme.Brush("Bg0", "#15151A"); FontFamily = new FontFamily("Segoe UI");
            Brush dim = Theme.Brush("TextDim", "#8C8C97"), accent = Theme.Brush("Accent", "#2BB673");

            var root = new StackPanel { Margin = new Thickness(14) };
            var head = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) };
            head.Inlines.Add(L.Keep(new System.Windows.Documents.Run(obj.Name)));
            root.Children.Add(head);
            root.Children.Add(new TextBlock
            {
                Text = L.T("Click to add a corner (on a side, it goes into that side) · drag a corner to move it · right-click a corner to remove it."),
                Foreground = dim, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = Math.Max(_w + 2 * Pad, 420), Margin = new Thickness(0, 0, 0, 8)
            });

            // the picture on a checkerboard, the box's outline, and the shape on top
            _canvas = new Canvas { Width = _w + 2 * Pad, Height = _h + 2 * Pad, Background = Theme.Brush("Bg1", "#1E1E26"), ClipToBounds = true, Cursor = Cursors.Cross };
            var board = new Rectangle { Width = _w, Height = _h, Fill = Checker() };
            Canvas.SetLeft(board, Pad); Canvas.SetTop(board, Pad); _canvas.Children.Add(board);
            FrameworkElement pic = _picture != null
                ? new Image { Source = _picture, Width = _w, Height = _h, Stretch = Stretch.Fill }
                : new Rectangle { Width = _w, Height = _h, Fill = ColorBrush(obj.Color) };
            if (_picture != null) RenderOptions.SetBitmapScalingMode(pic, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(pic, Pad); Canvas.SetTop(pic, Pad); _canvas.Children.Add(pic);
            var box = new Rectangle { Width = _w, Height = _h, Stroke = dim, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 }, IsHitTestVisible = false };
            Canvas.SetLeft(box, Pad); Canvas.SetTop(box, Pad); _canvas.Children.Add(box);
            var fill = ((SolidColorBrush)accent).Color;
            _poly = new Polygon { Stroke = accent, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(70, fill.R, fill.G, fill.B)), IsHitTestVisible = false };
            _canvas.Children.Add(_poly);
            _canvas.MouseLeftButtonDown += (s, e) => { var p = e.GetPosition(_canvas); Press(p); _canvas.CaptureMouse(); };
            _canvas.MouseMove += (s, e) => { if (_drag >= 0) MoveTo(e.GetPosition(_canvas)); };
            _canvas.MouseLeftButtonUp += (s, e) => { _drag = -1; _canvas.ReleaseMouseCapture(); };
            _canvas.MouseRightButtonDown += (s, e) => { int i = HitCorner(e.GetPosition(_canvas)); if (i >= 0) { _pts.RemoveAt(i); Redraw(); } };
            root.Children.Add(new Border { Child = _canvas, HorizontalAlignment = HorizontalAlignment.Center, BorderBrush = Theme.Brush("Border", "#333"), BorderThickness = new Thickness(1) });

            _info = new TextBlock { Foreground = dim, FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
            root.Children.Add(_info);

            // tools
            var tools = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            Button Tool(string text, string tip, Action a)
            {
                var b = new Button { Content = text, ToolTip = tip, Height = 28, FontSize = 11.5, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 6, 6), Style = Theme.Style("CardButton") };
                b.Click += (s, e) => { a(); Redraw(); };
                tools.Children.Add(b);
                return b;
            }
            Tool(L.T("▭ Start from the box"), L.T("Put a corner on each corner of the box, then move them"), () => { _pts.Clear(); _pts.AddRange(new[] { new PolyPoint(0, 0), new PolyPoint(1, 0), new PolyPoint(1, 1), new PolyPoint(0, 1) }); });
            if (_picture != null && !obj.TileImage)
                Tool(L.T("✂ Trace the drawing"), L.T("Draw the shape around the picture's visible pixels (you can fix the corners after)"), () => { var t = Trace(_picture); if (t.Count >= 3) { _pts.Clear(); _pts.AddRange(t); } });
            Tool(L.T("🗑 Clear"), L.T("Remove every corner: the whole box collides again"), () => _pts.Clear());
            root.Children.Add(tools);

            // bottom
            var bottom = new DockPanel { Margin = new Thickness(0, 8, 0, 0), LastChildFill = false };
            var ok = new Button { Content = L.T("✓ Use this shape"), Height = 32, Padding = new Thickness(16, 0, 16, 0), Style = Theme.Style("AccentButton"), IsDefault = true };
            ok.Click += (s, e) => Confirm();
            var cancel = new Button { Content = L.T("Cancel"), Height = 32, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 0, 8, 0), Style = Theme.Style("GhostButton"), IsCancel = true };
            DockPanel.SetDock(ok, Dock.Right); DockPanel.SetDock(cancel, Dock.Right);
            bottom.Children.Add(ok); bottom.Children.Add(cancel);
            root.Children.Add(bottom);

            Content = root;
            Redraw();
        }

        // ---- the corners

        private Point ToCanvas(PolyPoint p) => new(Pad + p.X * _w, Pad + p.Y * _h);
        private PolyPoint ToShape(Point p) => new(Math.Round(Math.Clamp((p.X - Pad) / _w, 0, 1), 4), Math.Round(Math.Clamp((p.Y - Pad) / _h, 0, 1), 4));

        private int HitCorner(Point p)
        {
            for (int i = _pts.Count - 1; i >= 0; i--) if ((ToCanvas(_pts[i]) - p).Length <= Grab) return i;
            return -1;
        }

        private void Press(Point p)
        {
            _drag = HitCorner(p);
            if (_drag >= 0) return;
            int at = _pts.Count;
            if (_pts.Count >= 3) // on a side: the new corner goes into that side
            {
                double best = Grab;
                for (int i = 0; i < _pts.Count; i++)
                {
                    double d = DistToSegment(p, ToCanvas(_pts[i]), ToCanvas(_pts[(i + 1) % _pts.Count]));
                    if (d < best) { best = d; at = i + 1; }
                }
            }
            _pts.Insert(at, ToShape(p));
            _drag = at;
            Redraw();
        }

        private void MoveTo(Point p) { if (_drag < _pts.Count) { _pts[_drag] = ToShape(p); Redraw(); } }

        private static double DistToSegment(Point p, Point a, Point b)
        {
            var ab = b - a; double len = ab.LengthSquared;
            double t = len < 1e-9 ? 0 : Math.Clamp(Vector.Multiply(p - a, ab) / len, 0, 1);
            return (p - (a + t * ab)).Length;
        }

        private void Redraw()
        {
            _poly.Points = new PointCollection(_pts.Select(ToCanvas));
            for (int i = _canvas.Children.Count - 1; i >= 0; i--) if ((_canvas.Children[i] as FrameworkElement)?.Tag as string == "corner") _canvas.Children.RemoveAt(i);
            for (int i = 0; i < _pts.Count; i++)
            {
                var c = ToCanvas(_pts[i]);
                var dot = new Ellipse { Width = 11, Height = 11, Fill = i == 0 ? Theme.Brush("AccentText", "#FFFFFF") : Theme.Brush("Accent", "#2BB673"), Stroke = Brushes.Black, StrokeThickness = 1, Tag = "corner", IsHitTestVisible = false };
                Canvas.SetLeft(dot, c.X - 5.5); Canvas.SetTop(dot, c.Y - 5.5);
                _canvas.Children.Add(dot);
            }
            _info.Text = _pts.Count == 0 ? L.T("No shape: the whole box collides.")
                       : _pts.Count < 3 ? L.F("{0} corner(s) — a shape needs at least 3.", _pts.Count)
                       : L.F("{0} corners.", _pts.Count);
        }

        private void Confirm()
        {
            if (_pts.Count is 1 or 2) { MessageBox.Show(this, L.T("A shape needs at least 3 corners (or none, for the whole box)."), Title); return; }
            Result = _pts.Count == 0 ? null : _pts.Select(p => new PolyPoint(p.X, p.Y)).ToList();
            DialogResult = true;
        }

        // ---- the picture

        private static BitmapSource? PictureOf(Project project, GameObjectData obj)
        {
            var rel = obj.SpritePath ?? obj.Animations.SelectMany(a => a.Frames).FirstOrDefault();
            var abs = rel == null ? null : ProjectIO.AbsoluteAsset(project, rel);
            return abs == null ? null : HitboxFit.LoadBitmap(abs);
        }

        /// <summary>A shape around the picture's visible pixels: down its left side and back up its right side (row by row),
        /// simplified to a few corners.</summary>
        internal static List<PolyPoint> Trace(BitmapSource bmp, int maxCorners = 20)
        {
            BitmapSource src = bmp.Format == PixelFormats.Bgra32 || bmp.Format == PixelFormats.Pbgra32 ? bmp : new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            int w = src.PixelWidth, h = src.PixelHeight;
            if (w <= 0 || h <= 0) return new();
            var px = new byte[w * h * 4];
            src.CopyPixels(px, w * 4, 0);
            var left = new List<Point>(); var right = new List<Point>();
            for (int y = 0; y < h; y++)
            {
                int row = y * w * 4, a = -1, b = -1;
                for (int x = 0; x < w; x++) if (px[row + x * 4 + 3] > 32) { if (a < 0) a = x; b = x; }
                if (a < 0) continue;
                left.Add(new Point(a, y)); right.Add(new Point(b + 1, y));
                if (y + 1 >= h || RowEmpty(px, w, y + 1)) { left.Add(new Point(a, y + 1)); right.Add(new Point(b + 1, y + 1)); } // the row's bottom edge
            }
            if (left.Count == 0) return new();
            var ring = left.Concat(Enumerable.Reverse(right)).ToList();
            double tol = Math.Max(w, h) * 0.01;
            List<Point> simple;
            do { simple = Simplify(ring, tol); tol *= 1.5; } while (simple.Count > maxCorners);
            return simple.Select(p => new PolyPoint(Math.Round(p.X / w, 4), Math.Round(p.Y / h, 4))).ToList();
        }

        private static bool RowEmpty(byte[] px, int w, int y) { int row = y * w * 4; for (int x = 0; x < w; x++) if (px[row + x * 4 + 3] > 32) return false; return true; }

        // Douglas–Peucker on an open line (the ring's ends are its two halves' tops)
        private static List<Point> Simplify(List<Point> pts, double tol)
        {
            if (pts.Count < 3) return pts.ToList();
            var keep = new bool[pts.Count]; keep[0] = keep[^1] = true;
            var stack = new Stack<(int, int)>(); stack.Push((0, pts.Count - 1));
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                double best = 0; int at = -1;
                for (int i = a + 1; i < b; i++) { double d = DistToSegment(pts[i], pts[a], pts[b]); if (d > best) { best = d; at = i; } }
                if (at >= 0 && best > tol) { keep[at] = true; stack.Push((a, at)); stack.Push((at, b)); }
            }
            var outp = new List<Point>();
            for (int i = 0; i < pts.Count; i++) if (keep[i] && (outp.Count == 0 || (outp[^1] - pts[i]).Length > 0.01)) outp.Add(pts[i]);
            return outp;
        }

        private static Brush ColorBrush(string? hex)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(hex) ? "#5B8DEF" : hex)); }
            catch { return new SolidColorBrush(Color.FromRgb(0x5B, 0x8D, 0xEF)); }
        }

        private static Brush Checker()
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x44)), null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x34)), null, new GeometryGroup { Children = { new RectangleGeometry(new Rect(0, 0, 8, 8)), new RectangleGeometry(new Rect(8, 8, 8, 8)) } }));
            return new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute };
        }

        // ---- test hooks (FullTest.PolyHitbox.cs)
        internal void TestClick(double fx, double fy) { var p = new Point(Pad + fx * _w, Pad + fy * _h); Press(p); _drag = -1; }
        internal void TestDrag(int i, double fx, double fy) { _drag = i; MoveTo(new Point(Pad + fx * _w, Pad + fy * _h)); _drag = -1; }
        internal void TestRemove(int i) { _pts.RemoveAt(i); Redraw(); }
        internal List<PolyPoint> TestPoints => _pts;
        internal string TestInfo => _info.Text;
        internal List<PolyPoint>? TestConfirm() { Result = _pts.Count >= 3 ? _pts.ToList() : null; return Result; }
    }
}
