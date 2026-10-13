using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace EasyEngine
{
    /// <summary>A small built-in pixel-art editor (pencil / eraser / fill / eyedropper) that saves PNG sprites.</summary>
    public class SpriteEditorWindow : Window
    {
        private readonly Project _project;
        private int _w, _h;
        private double _scale;
        private byte[] _data = Array.Empty<byte>();
        private WriteableBitmap _bmp = null!;

        private enum Tool { Pencil, Eraser, Line, Shape, Polygon, Fill, Blur, Jumble, Select, Eyedropper }
        private Tool _tool = Tool.Pencil;
        private byte _cr = 0, _cg = 0, _cb = 0, _ca = 255; // current color (black, opaque)
        private int _brush = 1; // pencil / eraser / line / shape / blur / jumble size in pixels
        private string _shape = "Rectangle";
        private bool _fillShapes;
        private readonly Random _rng = new();
        private readonly System.Collections.Generic.List<(int x, int y)> _poly = new();
        private byte[]? _polySnap;

        private byte[]? _snapshot;      // base image for live shape preview (line/rect)
        private int _dragX0, _dragY0;   // drag start pixel

        // selection / move
        private (int x, int y, int w, int h)? _sel;
        private byte[]? _selBuf;         // lifted selection pixels (null = not floating)
        private byte[]? _selBase;        // image with the selection removed (the "hole")
        private bool _movingSel;
        private int _grabDX, _grabDY;
        // rotation: always redrawn from the pixels as first lifted (so several small turns don't smear them)
        private byte[]? _selOrig;
        private int _selOrigW, _selOrigH;
        private double _selAngle;        // degrees, clockwise
        private int _rotateStep = 90;    // what one ⟲ / ⟳ click turns
        private Rectangle _marquee = null!;
        private Canvas _overlay = null!;

        private Image _view = null!;
        private Border _canvasHost = null!;
        private Rectangle _swatch = null!;
        private CheckBox _fillChk = null!;
        private TextBlock _hint = null!;      // what the chosen tool does, above the drawing
        private Rectangle _polyStart = null!; // marks the polygon's first point (click it again to close)
        private TextBlock _sizeLabel = null!;
        private readonly System.Collections.Generic.Dictionary<Tool, Button> _toolBtns = new();
        private int _lastPx = -1, _lastPy = -1;

        /// <summary>Set when the user saves; the project-relative path of the written PNG.</summary>
        public string? SavedRelativePath { get; private set; }
        internal string TestShape => _shape; // tests: the Shape tool's shape (picked by its translated label)

        public SpriteEditorWindow(Project project, int size = 32, string? loadAbsolutePath = null)
        {
            _project = project;
            Title = L.T("Sprite editor");
            Width = 760; Height = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Theme.Brush("Bg0", "#15151A");

            _w = _h = Math.Clamp(size, 4, 256);
            BuildUi();
            NewCanvas(_w, _h);
            if (loadAbsolutePath != null) TryLoad(loadAbsolutePath);
        }

        private void BuildUi()
        {
            var root = new DockPanel { Margin = new Thickness(10) };
            Brush dim = Theme.Brush("TextDim", "#AAA"), text = Theme.Brush("Text", "#EEE");

            // --- bottom: the canvas (size, new, clear) on the left, Cancel / Save on the right ---
            var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
            DockPanel.SetDock(bottom, Dock.Bottom);
            Button Ghost(string label, string? tip = null) => new() { Content = label, Height = 30, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(8, 0, 8, 0), Style = Theme.Style("GhostButton"), ToolTip = tip };
            var canvasBar = new StackPanel { Orientation = Orientation.Horizontal };
            var undo = Ghost("↶", L.T("Undo (Ctrl+Z)")); undo.FontSize = 15; undo.Click += (s, e) => Undo();
            var redo = Ghost("↷", L.T("Redo (Ctrl+Y)")); redo.FontSize = 15; redo.Margin = new Thickness(0, 0, 12, 0); redo.Click += (s, e) => Redo();
            canvasBar.Children.Add(undo); canvasBar.Children.Add(redo);
            _sizeLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = dim, FontSize = 12, Margin = new Thickness(0, 0, 8, 0) };
            canvasBar.Children.Add(_sizeLabel);
            foreach (var sz in new[] { 16, 32, 64 })
            {
                var b = Ghost($"{sz}²", L.F("New {0}×{1} canvas", sz, sz));
                int s2 = sz; b.Click += (s, e) => { if (Confirm(L.T("Start a new blank canvas? Current drawing will be lost."))) { Checkpoint(); NewCanvas(s2, s2); } };
                canvasBar.Children.Add(b);
            }
            var custom = Ghost(L.T("Custom…"), L.T("New canvas of any size, e.g. 32×128"));
            custom.Click += (s, e) => CustomCanvas();
            canvasBar.Children.Add(custom);
            var clear = Ghost(L.T("Clear"));
            clear.Foreground = Theme.Brush("Danger", "#E5484B");
            clear.Click += (s, e) => { if (Confirm(L.T("Clear the canvas?"))) { Checkpoint(); DropSelection(); Array.Clear(_data, 0, _data.Length); Flush(); } };
            canvasBar.Children.Add(clear);
            DockPanel.SetDock(canvasBar, Dock.Left);
            bottom.Children.Add(canvasBar);
            var save = new Button { Content = L.T("Save sprite"), Height = 32, Padding = new Thickness(16, 0, 16, 0), Style = Theme.Style("AccentButton") };
            save.Click += (s, e) => SaveAndClose();
            var cancel = new Button { Content = L.T("Cancel"), Height = 32, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 0, 8, 0), Style = Theme.Style("GhostButton") };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            DockPanel.SetDock(save, Dock.Right); DockPanel.SetDock(cancel, Dock.Right);
            bottom.Children.Add(save); bottom.Children.Add(cancel);
            root.Children.Add(bottom);

            // --- left: every tool on its own row, in groups; Shape has its shape picker right beside it ---
            var tools = new StackPanel { Width = 184, Margin = new Thickness(0, 0, 10, 0) };
            DockPanel.SetDock(tools, Dock.Left);
            TextBlock Group(string label) => new() { Text = label, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = dim, Margin = new Thickness(2, 6, 0, 4) };
            Button ToolBtn(string label, Tool t)
            {
                var b = new Button { Content = label, Height = 30, Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(10, 0, 8, 0), HorizontalContentAlignment = HorizontalAlignment.Left, Style = Theme.Style("CardButton") };
                b.Click += (s, e) => SelectTool(t);
                _toolBtns[t] = b;
                return b;
            }
            Button SmallBtn(string label, string tip, Action act)
            {
                var b = new Button { Content = label, Height = 30, MinWidth = 34, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(6, 0, 6, 0), Style = Theme.Style("CardButton"), ToolTip = tip };
                b.Click += (s, e) => act();
                return b;
            }
            tools.Children.Add(Group(L.T("DRAW")));
            tools.Children.Add(ToolBtn(L.T("✏ Pencil"), Tool.Pencil));
            tools.Children.Add(ToolBtn(L.T("⌫ Eraser"), Tool.Eraser));
            tools.Children.Add(ToolBtn(L.T("／ Line"), Tool.Line));
            var shapeRow = new Grid();
            shapeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            shapeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            var shapeBtn = ToolBtn(L.T("◆ Shape"), Tool.Shape);
            var shapeCombo = new ComboBox { Height = 30, Margin = new Thickness(4, 0, 0, 4), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = L.T("Which shape the Shape tool draws") };
            foreach (var (sh, label) in Shapes()) shapeCombo.Items.Add(new ComboBoxItem { Content = label, Tag = sh }); // the shape is read back by its Tag
            shapeCombo.SelectedIndex = 0;
            shapeCombo.SelectionChanged += (s, e) => { _shape = (shapeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Rectangle"; SelectTool(Tool.Shape); };
            Grid.SetColumn(shapeCombo, 1);
            shapeRow.Children.Add(shapeBtn); shapeRow.Children.Add(shapeCombo);
            tools.Children.Add(shapeRow);
            tools.Children.Add(ToolBtn(L.T("⬡ Polygon"), Tool.Polygon));
            tools.Children.Add(ToolBtn(L.T("🪣 Fill"), Tool.Fill));
            tools.Children.Add(ToolBtn(L.T("💉 Pick"), Tool.Eyedropper));
            tools.Children.Add(Group(L.T("EFFECTS")));
            tools.Children.Add(ToolBtn(L.T("🌫 Blur"), Tool.Blur));
            tools.Children.Add(ToolBtn(L.T("🌀 Jumble"), Tool.Jumble));
            tools.Children.Add(Group(L.T("SELECTION")));
            tools.Children.Add(ToolBtn(L.T("⬚ Select"), Tool.Select));
            // turn / mirror the selection (or the whole drawing when nothing is selected)
            var turn = new StackPanel { Orientation = Orientation.Horizontal };   // ⟲ ⟳ and how much one turn rotates
            turn.Children.Add(SmallBtn("⟲", L.T("Turn the selection left (R with Shift). Nothing selected = the whole drawing"), () => RotateSelection(-_rotateStep)));
            turn.Children.Add(SmallBtn("⟳", L.T("Turn the selection right (R). Nothing selected = the whole drawing"), () => RotateSelection(_rotateStep)));
            var stepCombo = new ComboBox { Height = 30, Width = 64, Margin = new Thickness(0, 0, 4, 4), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = L.T("How much one turn rotates") };
            foreach (var st in new[] { 90, 45, 15 }) stepCombo.Items.Add($"{st}°");
            stepCombo.SelectedIndex = 0;
            stepCombo.SelectionChanged += (s, e) => _rotateStep = new[] { 90, 45, 15 }[Math.Max(0, stepCombo.SelectedIndex)];
            turn.Children.Add(stepCombo);
            tools.Children.Add(turn);
            var mirror = new StackPanel { Orientation = Orientation.Horizontal }; // ⇋ ⇵
            mirror.Children.Add(SmallBtn("⇋", L.T("Mirror the selection left ↔ right"), () => FlipSelection(true)));
            mirror.Children.Add(SmallBtn("⇵", L.T("Mirror the selection up ↕ down"), () => FlipSelection(false)));
            tools.Children.Add(mirror);
            // copy / cut / paste (Ctrl+C / X / V): between sprites, and pictures from other programs
            var clipRow = new UniformGrid { Columns = 3 };
            clipRow.Children.Add(SmallBtn(L.T("Copy"), L.T("Copy the selection, or the whole drawing (Ctrl+C)"), CopySelection));
            clipRow.Children.Add(SmallBtn(L.T("Cut"), L.T("Cut the selection (Ctrl+X)"), CutSelection));
            clipRow.Children.Add(SmallBtn(L.T("Paste"), L.T("Paste what you copied, here or from another sprite or program, then drag it into place (Ctrl+V)"), PasteClipboard));
            tools.Children.Add(clipRow);
            root.Children.Add(tools);

            // --- top of the drawing area: colour, brush, Fill (shapes) and what the chosen tool does ---
            var right = new DockPanel();
            var options = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(options, Dock.Top);
            var optRow = new WrapPanel();
            _swatch = new Rectangle { Width = 30, Height = 30, Margin = new Thickness(0, 0, 6, 0), RadiusX = 4, RadiusY = 4, Stroke = Theme.Brush("Border", "#555"), StrokeThickness = 1, Cursor = Cursors.Hand, ToolTip = L.T("Pick a color"), Fill = new SolidColorBrush(Color.FromArgb(_ca, _cr, _cg, _cb)) };
            _swatch.MouseLeftButtonUp += (s, e) =>
                ColorPickerPopup.Open(_swatch, ColorPickerPopup.ColorToHex(Color.FromRgb(_cr, _cg, _cb)), hex =>
                {
                    try
                    {
                        var c = (Color)ColorConverter.ConvertFromString(hex); _cr = c.R; _cg = c.G; _cb = c.B; _ca = 255; _swatch.Fill = new SolidColorBrush(Color.FromArgb(255, _cr, _cg, _cb));
                        if (_tool is Tool.Eraser or Tool.Eyedropper or Tool.Select) SelectTool(Tool.Pencil); // a new colour is for painting
                    }
                    catch { }
                });
            optRow.Children.Add(_swatch);
            optRow.Children.Add(new TextBlock { Text = L.T("Color"), VerticalAlignment = VerticalAlignment.Center, Foreground = dim, FontSize = 12, Margin = new Thickness(0, 0, 16, 0) });
            optRow.Children.Add(new TextBlock { Text = L.T("Brush"), VerticalAlignment = VerticalAlignment.Center, Foreground = dim, FontSize = 12, Margin = new Thickness(0, 0, 6, 0) });
            var brushVal = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = text, FontSize = 12, MinWidth = 34 };
            // click to jump AND drag (SliderBar), in whole pixels
            var brushSlider = SliderBar.Make(1, 10, _brush, v => _brush = Math.Max(1, (int)Math.Round(v)), v => brushVal.Text = $" {(int)Math.Round(v)} px", step: 1);
            brushSlider.Width = 110; brushSlider.VerticalAlignment = VerticalAlignment.Center;
            optRow.Children.Add(brushSlider);
            optRow.Children.Add(brushVal);
            _fillChk = new CheckBox { Content = L.T("Fill the shape"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), Foreground = text, ToolTip = L.T("Fill shapes / polygon") };
            _fillChk.Checked += (s, e) => _fillShapes = true;
            _fillChk.Unchecked += (s, e) => _fillShapes = false;
            optRow.Children.Add(_fillChk);
            options.Children.Add(optRow);
            _hint = new TextBlock { Foreground = dim, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            options.Children.Add(_hint);
            right.Children.Add(options);

            // --- the drawing ---
            _view = new Image { Stretch = Stretch.Fill, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(_view, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(_view, EdgeMode.Aliased);
            _view.MouseLeftButtonDown += View_Down;
            _view.MouseMove += View_Move;
            _view.MouseLeftButtonUp += View_Up;

            _overlay = new Canvas { IsHitTestVisible = false };
            _marquee = new Rectangle { Stroke = Theme.Pick("#FFFFFF", "#1B1E24"), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 2 }, Fill = new SolidColorBrush(Color.FromArgb(48, 90, 160, 255)), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            _overlay.Children.Add(_marquee);
            // the polygon's first point: clicking it again closes the polygon
            _polyStart = new Rectangle { Stroke = Theme.Brush("Accent", "#18B98A"), StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(60, 24, 185, 138)), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            _overlay.Children.Add(_polyStart);
            var stage = new Grid();
            stage.Children.Add(_view);
            stage.Children.Add(_overlay);

            _canvasHost = new Border { Background = Checker(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, BorderBrush = Theme.Brush("Border", "#555"), BorderThickness = new Thickness(1), Child = stage };
            var center = new Grid();
            center.Children.Add(_canvasHost);
            right.Children.Add(center);
            root.Children.Add(right);

            Content = root;
            SelectTool(Tool.Pencil);
        }

        private bool Confirm(string msg) => MessageBox.Show(this, msg, L.T("Sprite editor"), MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

        // the Shape tool's shapes: (id the drawing code uses, what the dropdown shows)
        private static (string id, string label)[] Shapes() => new[]
        {
            ("Rectangle", L.T("Rectangle")), ("Ellipse", L.T("Ellipse")), ("Triangle", L.T("Triangle")), ("Diamond", L.T("Diamond")),
            ("Pentagon", L.T("Pentagon")), ("Hexagon", L.T("Hexagon")), ("Star", L.T("Star")), ("Heart", L.T("Heart")),
        };

        private static DrawingBrush Checker()
        {
            var g = new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x40)), null, new RectangleGeometry(new Rect(0, 0, 2, 2)));
            var lightA = new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x4a, 0x4a, 0x52)), null, new RectangleGeometry(new Rect(0, 0, 1, 1)));
            var lightB = new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x4a, 0x4a, 0x52)), null, new RectangleGeometry(new Rect(1, 1, 1, 1)));
            var grp = new DrawingGroup();
            grp.Children.Add(g); grp.Children.Add(lightA); grp.Children.Add(lightB);
            return new DrawingBrush(grp) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill }; // the 2×2 pattern fills each 16 px tile: an 8 px checkerboard
        }

        private void SelectTool(Tool t)
        {
            if (_tool == Tool.Select && t != Tool.Select) DropSelection();
            if (_tool == Tool.Polygon && t != Tool.Polygon) CancelPolygon();
            _tool = t;
            foreach (var kv in _toolBtns)
            {
                kv.Value.Background = kv.Key == t ? Theme.Brush("Accent", "#2bb673") : Theme.Brush("Bg2", "#2a2a30");
                kv.Value.Foreground = kv.Key == t ? Theme.Brush("AccentText", "#062018") : Theme.Brush("Text", "#EEE");
            }
            _fillChk.Visibility = t is Tool.Shape or Tool.Polygon ? Visibility.Visible : Visibility.Collapsed;
            _hint.Text = t switch
            {
                Tool.Pencil => L.T("Click or drag to draw."),
                Tool.Eraser => L.T("Click or drag to erase."),
                Tool.Line => L.T("Drag from one end of the line to the other."),
                Tool.Shape => L.T("Drag to draw the shape you picked beside the tool."),
                Tool.Polygon => L.T("Click each corner, then click the first point again to close it (or double-click / Enter). Esc cancels."),
                Tool.Fill => L.T("Click an area to fill it with the colour."),
                Tool.Blur => L.T("Drag to soften the pixels."),
                Tool.Jumble => L.T("Drag to scatter the pixels."),
                Tool.Select => L.T("Drag to select, then drag the selection to move it. R turns it, Ctrl+C / Ctrl+V copy and paste, Esc lets it go."),
                Tool.Eyedropper => L.T("Click a pixel to take its colour."),
                _ => "",
            };
        }

        // ---- undo / redo (Ctrl+Z / Ctrl+Y): a picture of the drawing before each change, up to 60 steps ----
        private const int UndoSteps = 60;
        private readonly LinkedList<(byte[] px, int w, int h)> _undo = new();
        private readonly Stack<(byte[] px, int w, int h)> _redo = new();

        /// <summary>Remembers the drawing as it is now, before a change (a new change forgets what was undone).</summary>
        private void Checkpoint()
        {
            if (_data == null) return;
            _undo.AddLast(((byte[])_data.Clone(), _w, _h));
            if (_undo.Count > UndoSteps) _undo.RemoveFirst();
            _redo.Clear();
        }

        /// <summary>Ctrl+Z: back one step (a polygon being drawn is cancelled first).</summary>
        public void Undo()
        {
            if (_poly.Count > 0) { CancelPolygon(); return; }
            if (_undo.Count == 0) return;
            _redo.Push(((byte[])_data.Clone(), _w, _h));
            var s = _undo.Last!.Value; _undo.RemoveLast();
            RestoreDrawing(s);
        }

        /// <summary>Ctrl+Y (or Ctrl+Shift+Z): forward again.</summary>
        public void Redo()
        {
            if (_redo.Count == 0) return;
            _undo.AddLast(((byte[])_data.Clone(), _w, _h));
            RestoreDrawing(_redo.Pop());
        }

        private void RestoreDrawing((byte[] px, int w, int h) s)
        {
            DropSelection();
            if (s.w != _w || s.h != _h)
            {
                _w = s.w; _h = s.h;
                _bmp = new WriteableBitmap(_w, _h, 96, 96, PixelFormats.Bgra32, null);
                RecomputeScale();
                _sizeLabel.Text = $"{_w}×{_h}";
            }
            _data = (byte[])s.px.Clone();
            Flush();
        }

        internal int TestUndoSteps => _undo.Count;

        private void NewCanvas(int w, int h)
        {
            DropSelection();
            _poly.Clear(); _polySnap = null; ShowPolyStart();
            _w = Math.Clamp(w, 4, 256); _h = Math.Clamp(h, 4, 256);
            _data = new byte[_w * _h * 4];
            _bmp = new WriteableBitmap(_w, _h, 96, 96, PixelFormats.Bgra32, null);
            RecomputeScale();
            Flush();
            _sizeLabel.Text = $"{_w}×{_h}";
        }

        private void RecomputeScale()
        {
            double target = 448;
            _scale = Math.Max(1, Math.Floor(target / Math.Max(_w, _h)));
            _view.Source = _bmp;
            _view.Width = _w * _scale;
            _view.Height = _h * _scale;
        }

        private void TryLoad(string absPath)
        {
            try
            {
                var src = new BitmapImage();
                src.BeginInit(); src.CacheOption = BitmapCacheOption.OnLoad; src.UriSource = new Uri(absPath); src.EndInit();
                var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight;
                if (w < 1 || h < 1 || w > 256 || h > 256) return;
                NewCanvas(w, h);
                conv.CopyPixels(_data, _w * 4, 0);
                Flush();
            }
            catch { }
        }

        private void Flush() => _bmp.WritePixels(new Int32Rect(0, 0, _w, _h), _data, _w * 4, 0);

        private (int x, int y)? PixelAt(Point p)
        {
            int x = (int)Math.Floor(p.X / _scale), y = (int)Math.Floor(p.Y / _scale);
            if (x < 0 || y < 0 || x >= _w || y >= _h) return null;
            return (x, y);
        }

        private (int x, int y) ClampPixel(Point p) =>
            (Math.Clamp((int)Math.Floor(p.X / _scale), 0, _w - 1), Math.Clamp((int)Math.Floor(p.Y / _scale), 0, _h - 1));

        private void View_Down(object sender, MouseButtonEventArgs e)
        {
            var (x, y) = ClampPixel(e.GetPosition(_view));
            bool inside = PixelAt(e.GetPosition(_view)) != null;

            if (_tool == Tool.Polygon) { PolygonDown(x, y, e.ClickCount >= 2); return; }

            _view.CaptureMouse();
            _dragX0 = x; _dragY0 = y; _lastPx = x; _lastPy = y;
            if (_tool is Tool.Pencil or Tool.Eraser or Tool.Fill or Tool.Blur or Tool.Jumble or Tool.Line or Tool.Shape) Checkpoint();
            switch (_tool)
            {
                case Tool.Eyedropper: if (inside) Pick(x, y); break;
                case Tool.Fill: if (inside) FloodFill(x, y); break;
                case Tool.Pencil: case Tool.Eraser: if (inside) Paint(x, y); break;
                case Tool.Blur: if (inside) BlurStamp(x, y); break;
                case Tool.Jumble: if (inside) JumbleStamp(x, y); break;
                case Tool.Line: _snapshot = (byte[])_data.Clone(); DrawLineData(x, y, x, y); Flush(); break;
                case Tool.Shape: _snapshot = (byte[])_data.Clone(); DrawShapeData(x, y, x, y); Flush(); break;
                case Tool.Select: SelectDown(x, y); break;
            }
        }

        private void View_Move(object sender, MouseEventArgs e)
        {
            var (x, y) = ClampPixel(e.GetPosition(_view));
            if (_tool == Tool.Polygon) { PolygonPreview(x, y); return; } // follows the cursor even without a button
            if (e.LeftButton != MouseButtonState.Pressed || !_view.IsMouseCaptured) return;
            switch (_tool)
            {
                case Tool.Pencil: case Tool.Eraser:
                    if (_lastPx >= 0) PaintLine(_lastPx, _lastPy, x, y); else Paint(x, y);
                    _lastPx = x; _lastPy = y; break;
                case Tool.Blur:
                    if (_lastPx >= 0) BlurLine(_lastPx, _lastPy, x, y); else BlurStamp(x, y);
                    _lastPx = x; _lastPy = y; break;
                case Tool.Jumble:
                    JumbleStamp(x, y); break;
                case Tool.Line:
                    if (_snapshot != null) { Array.Copy(_snapshot, _data, _data.Length); DrawLineData(_dragX0, _dragY0, x, y); Flush(); } break;
                case Tool.Shape:
                    if (_snapshot != null) { Array.Copy(_snapshot, _data, _data.Length); DrawShapeData(_dragX0, _dragY0, x, y); Flush(); } break;
                case Tool.Select:
                    SelectMove(x, y); break;
            }
        }

        private void View_Up(object sender, MouseButtonEventArgs e)
        {
            _view.ReleaseMouseCapture();
            if (_tool == Tool.Select) _movingSel = false;
            _lastPx = _lastPy = -1;
            _snapshot = null;
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (ListSearch.WantsKey(e)) return; // keys that search an open list (R, Esc…) aren't shortcuts
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z) { Redo(); e.Handled = true; return; }
            if (Keyboard.Modifiers == ModifierKeys.Control && !(Keyboard.FocusedElement is TextBox))
            {
                switch (e.Key)
                {
                    case Key.Z: Undo(); e.Handled = true; return;
                    case Key.Y: Redo(); e.Handled = true; return;
                    case Key.C: CopySelection(); e.Handled = true; return;
                    case Key.X: CutSelection(); e.Handled = true; return;
                    case Key.V: PasteClipboard(); e.Handled = true; return;
                    case Key.A: SelectAll(); e.Handled = true; return;
                }
            }
            if (_tool == Tool.Polygon)
            {
                if (e.Key == Key.Enter) { FinishPolygon(); e.Handled = true; }
                else if (e.Key == Key.Escape) { CancelPolygon(); e.Handled = true; }
            }
            else if (_tool == Tool.Select && e.Key == Key.Escape) { DropSelection(); e.Handled = true; }
            if (_tool == Tool.Select && e.Key == Key.R)
            {
                RotateSelection(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -_rotateStep : _rotateStep);
                e.Handled = true;
            }
        }

        // ---- shape / blur helpers (write _data only; caller Flushes) ----
        // Bounds of the square brush centred on (cx,cy), clamped to the canvas. Returns false if empty.
        private bool BrushBox(int cx, int cy, out int x0, out int y0, out int x1, out int y1)
        {
            int half = _brush / 2;
            x0 = Math.Max(0, cx - half); y0 = Math.Max(0, cy - half);
            x1 = Math.Min(_w - 1, cx - half + _brush - 1); y1 = Math.Min(_h - 1, cy - half + _brush - 1);
            return x1 >= x0 && y1 >= y0;
        }

        // Bresenham line; calls plot(x,y) at each step.
        private static void WalkLine(int x0, int y0, int x1, int y1, Action<int, int> plot)
        {
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx - dy;
            while (true)
            {
                plot(x0, y0);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }

        private void StampData(int cx, int cy, bool erase)
        {
            if (!BrushBox(cx, cy, out int x0, out int y0, out int x1, out int y1)) return;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = (y * _w + x) * 4;
                    if (erase) { _data[i] = _data[i + 1] = _data[i + 2] = _data[i + 3] = 0; }
                    else { _data[i] = _cb; _data[i + 1] = _cg; _data[i + 2] = _cr; _data[i + 3] = 255; }
                }
        }

        private void DrawLineData(int x0, int y0, int x1, int y1) => WalkLine(x0, y0, x1, y1, (x, y) => StampData(x, y, false));

        private void DrawRectData(int ax, int ay, int bx, int by)
        {
            int x0 = Math.Min(ax, bx), x1 = Math.Max(ax, bx), y0 = Math.Min(ay, by), y1 = Math.Max(ay, by);
            DrawLineData(x0, y0, x1, y0); DrawLineData(x0, y1, x1, y1);
            DrawLineData(x0, y0, x0, y1); DrawLineData(x1, y0, x1, y1);
        }

        private void BlurStamp(int cx, int cy)
        {
            if (!BrushBox(cx, cy, out int x0, out int y0, out int x1, out int y1)) return;
            var outBuf = new (int i, byte b, byte g, byte r, byte a)[(x1 - x0 + 1) * (y1 - y0 + 1)];
            int k = 0;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int sb = 0, sg = 0, sr = 0, sa = 0, n = 0;
                    for (int ny = Math.Max(0, y - 1); ny <= Math.Min(_h - 1, y + 1); ny++)
                        for (int nx = Math.Max(0, x - 1); nx <= Math.Min(_w - 1, x + 1); nx++)
                        {
                            int j = (ny * _w + nx) * 4;
                            sb += _data[j]; sg += _data[j + 1]; sr += _data[j + 2]; sa += _data[j + 3]; n++;
                        }
                    outBuf[k++] = ((y * _w + x) * 4, (byte)(sb / n), (byte)(sg / n), (byte)(sr / n), (byte)(sa / n));
                }
            foreach (var (i, b, g, r, a) in outBuf) { _data[i] = b; _data[i + 1] = g; _data[i + 2] = r; _data[i + 3] = a; }
            int off = (y0 * _w + x0) * 4;
            _bmp.WritePixels(new Int32Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1), _data, _w * 4, off);
        }

        private void BlurLine(int x0, int y0, int x1, int y1) => WalkLine(x0, y0, x1, y1, BlurStamp);

        // ---- shapes / polygon / jumble ----
        private void SetPixelData(int x, int y)
        {
            if (x < 0 || y < 0 || x >= _w || y >= _h) return;
            int i = (y * _w + x) * 4;
            _data[i] = _cb; _data[i + 1] = _cg; _data[i + 2] = _cr; _data[i + 3] = 255;
        }

        private void DrawShapeData(int ax, int ay, int bx, int by)
        {
            if (_shape == "Rectangle")
            {
                if (_fillShapes)
                {
                    int x0 = Math.Min(ax, bx), x1 = Math.Max(ax, bx), y0 = Math.Min(ay, by), y1 = Math.Max(ay, by);
                    for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) SetPixelData(x, y);
                }
                else DrawRectData(ax, ay, bx, by);
                return;
            }
            DrawPolygonData(ShapePoints(_shape, ax, ay, bx, by), _fillShapes, true);
        }

        private List<(double x, double y)> ShapePoints(string shape, double ax, double ay, double bx, double by)
        {
            double left = Math.Min(ax, bx), right = Math.Max(ax, bx), top = Math.Min(ay, by), bottom = Math.Max(ay, by);
            double cx = (left + right) / 2, cy = (top + bottom) / 2, rx = Math.Max(0.5, (right - left) / 2), ry = Math.Max(0.5, (bottom - top) / 2);
            var pts = new List<(double, double)>();
            void Reg(int n) { for (int i = 0; i < n; i++) { double a = -Math.PI / 2 + i * 2 * Math.PI / n; pts.Add((cx + rx * Math.Cos(a), cy + ry * Math.Sin(a))); } }
            switch (shape)
            {
                case "Ellipse": { int n = Math.Max(16, (int)(Math.Max(rx, ry) * 3)); for (int i = 0; i < n; i++) { double a = i * 2 * Math.PI / n; pts.Add((cx + rx * Math.Cos(a), cy + ry * Math.Sin(a))); } break; }
                case "Triangle": pts.Add((cx, top)); pts.Add((right, bottom)); pts.Add((left, bottom)); break;
                case "Diamond": pts.Add((cx, top)); pts.Add((right, cy)); pts.Add((cx, bottom)); pts.Add((left, cy)); break;
                case "Pentagon": Reg(5); break;
                case "Hexagon": Reg(6); break;
                case "Star": for (int i = 0; i < 10; i++) { double a = -Math.PI / 2 + i * Math.PI / 5; double r = (i % 2 == 0) ? 1.0 : 0.42; pts.Add((cx + rx * r * Math.Cos(a), cy + ry * r * Math.Sin(a))); } break;
                case "Heart":
                    {
                        var raw = new List<(double, double)>();
                        int N = 48;
                        for (int i = 0; i < N; i++) { double t = i * 2 * Math.PI / N; double hx = 16 * Math.Pow(Math.Sin(t), 3); double hy = 13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t); raw.Add((hx, -hy)); }
                        double minx = raw.Min(p => p.Item1), maxx = raw.Max(p => p.Item1), miny = raw.Min(p => p.Item2), maxy = raw.Max(p => p.Item2);
                        foreach (var (hx, hy) in raw) { double nx = left + (hx - minx) / (maxx - minx) * (right - left); double ny = top + (hy - miny) / (maxy - miny) * (bottom - top); pts.Add((nx, ny)); }
                        break;
                    }
                default: pts.Add((left, top)); pts.Add((right, top)); pts.Add((right, bottom)); pts.Add((left, bottom)); break;
            }
            return pts;
        }

        private void DrawPolygonData(List<(double x, double y)> pts, bool fill, bool close)
        {
            if (pts.Count == 0) return;
            if (fill && pts.Count >= 3) ScanlineFill(pts);
            for (int i = 0; i < pts.Count - 1; i++) DrawLineData((int)Math.Round(pts[i].x), (int)Math.Round(pts[i].y), (int)Math.Round(pts[i + 1].x), (int)Math.Round(pts[i + 1].y));
            if (close && pts.Count >= 2) { var a = pts[^1]; var b = pts[0]; DrawLineData((int)Math.Round(a.x), (int)Math.Round(a.y), (int)Math.Round(b.x), (int)Math.Round(b.y)); }
        }

        private void ScanlineFill(List<(double x, double y)> pts)
        {
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (var p in pts) { if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y; }
            int y0 = Math.Max(0, (int)Math.Floor(minY)), y1 = Math.Min(_h - 1, (int)Math.Ceiling(maxY));
            var xs = new List<double>();
            for (int y = y0; y <= y1; y++)
            {
                double yc = y + 0.5; xs.Clear();
                for (int i = 0; i < pts.Count; i++)
                {
                    var a = pts[i]; var b = pts[(i + 1) % pts.Count];
                    if ((a.y <= yc && b.y > yc) || (b.y <= yc && a.y > yc)) { double t = (yc - a.y) / (b.y - a.y); xs.Add(a.x + t * (b.x - a.x)); }
                }
                xs.Sort();
                for (int i = 0; i + 1 < xs.Count; i += 2)
                {
                    int xa = Math.Max(0, (int)Math.Round(xs[i])), xb = Math.Min(_w - 1, (int)Math.Round(xs[i + 1]));
                    for (int x = xa; x <= xb; x++) SetPixelData(x, y);
                }
            }
        }

        private void JumbleStamp(int cx, int cy)
        {
            if (!BrushBox(cx, cy, out int x0, out int y0, out int x1, out int y1)) return;
            int count = (x1 - x0 + 1) * (y1 - y0 + 1);
            for (int s = 0; s < count; s++)
            {
                int ax = _rng.Next(x0, x1 + 1), ay = _rng.Next(y0, y1 + 1);
                int bx = Math.Clamp(ax + _rng.Next(-1, 2), x0, x1), by = Math.Clamp(ay + _rng.Next(-1, 2), y0, y1);
                int ia = (ay * _w + ax) * 4, ib = (by * _w + bx) * 4;
                for (int k = 0; k < 4; k++) { var tmp = _data[ia + k]; _data[ia + k] = _data[ib + k]; _data[ib + k] = tmp; }
            }
            int off = (y0 * _w + x0) * 4;
            _bmp.WritePixels(new Int32Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1), _data, _w * 4, off);
        }

        internal void PolygonDown(int x, int y, bool doubleClick)
        {
            if (doubleClick) { FinishPolygon(); return; }
            if (NearPolyStart(x, y)) { FinishPolygon(); return; } // back on the first point: close it
            if (_poly.Count == 0) { Checkpoint(); _polySnap = (byte[])_data.Clone(); }
            _poly.Add((x, y));
            ShowPolyStart();
            PolygonPreview(x, y);
        }

        // the cursor is on the polygon's first point (a few screen pixels of slack when zoomed out), once it has 3 corners
        private bool NearPolyStart(int x, int y)
        {
            if (_poly.Count < 3) return false;
            int slack = Math.Max(0, (int)Math.Ceiling(5 / Math.Max(1, _scale)));
            return Math.Abs(x - _poly[0].x) <= slack && Math.Abs(y - _poly[0].y) <= slack;
        }

        private void PolygonPreview(int cursorX, int cursorY)
        {
            if (_poly.Count == 0 || _polySnap == null) return;
            if (NearPolyStart(cursorX, cursorY)) (cursorX, cursorY) = _poly[0]; // snaps shut: the closing line shows
            Array.Copy(_polySnap, _data, _data.Length);
            for (int i = 0; i < _poly.Count - 1; i++) DrawLineData(_poly[i].x, _poly[i].y, _poly[i + 1].x, _poly[i + 1].y);
            DrawLineData(_poly[^1].x, _poly[^1].y, cursorX, cursorY);
            Flush();
        }

        private void ShowPolyStart()
        {
            if (_poly.Count == 0) { _polyStart.Visibility = Visibility.Collapsed; return; }
            double size = Math.Max(10, _scale);
            _polyStart.Width = _polyStart.Height = size;
            Canvas.SetLeft(_polyStart, (_poly[0].x + 0.5) * _scale - size / 2);
            Canvas.SetTop(_polyStart, (_poly[0].y + 0.5) * _scale - size / 2);
            _polyStart.Visibility = Visibility.Visible;
        }

        internal void FinishPolygon()
        {
            if (_poly.Count < 2 || _polySnap == null) { CancelPolygon(); return; }
            Array.Copy(_polySnap, _data, _data.Length);
            DrawPolygonData(_poly.Select(p => ((double)p.x, (double)p.y)).ToList(), _fillShapes, true);
            Flush();
            _poly.Clear(); _polySnap = null; ShowPolyStart();
        }

        private void CancelPolygon()
        {
            if (_poly.Count > 0 && _undo.Count > 0) _undo.RemoveLast(); // nothing was drawn: forget its undo step
            if (_polySnap != null) { Array.Copy(_polySnap, _data, _data.Length); Flush(); }
            _poly.Clear(); _polySnap = null; ShowPolyStart();
        }

        internal int TestPolygonPoints => _poly.Count;   // tests: corners of the polygon being drawn (0 = none / closed)
        internal void TestSelectTool(string tool) => SelectTool(Enum.Parse<Tool>(tool));
        internal int TestPixelAlpha(int x, int y) => _data[(y * _w + x) * 4 + 3];

        // ---- copy / cut / paste ----
        // Through the Windows clipboard, as a picture AND as a PNG (the PNG keeps transparency): it works between sprites,
        // and pictures copied in other programs can be pasted. What was copied here is also kept, in case the clipboard
        // can't be read.
        private static (byte[] px, int w, int h)? _clip;
        internal static bool UseSystemClipboard = true; // tests turn it off: they must never touch the person's clipboard

        /// <summary>The selection's pixels (the whole drawing when nothing is selected).</summary>
        private (byte[] px, int w, int h) SelectionPixels()
        {
            if (_sel is not { } s) return ((byte[])_data.Clone(), _w, _h);
            if (_selBuf != null) return ((byte[])_selBuf.Clone(), s.w, s.h);
            int x0 = Math.Max(0, s.x), y0 = Math.Max(0, s.y), x1 = Math.Min(_w, s.x + s.w), y1 = Math.Min(_h, s.y + s.h);
            int w = Math.Max(1, x1 - x0), h = Math.Max(1, y1 - y0);
            var px = new byte[w * h * 4];
            for (int j = 0; j < h && y0 + j < _h; j++) Array.Copy(_data, ((y0 + j) * _w + x0) * 4, px, j * w * 4, Math.Min(w, _w - x0) * 4);
            return (px, w, h);
        }

        /// <summary>Copies the selection (or the whole drawing). Ctrl+C.</summary>
        public void CopySelection()
        {
            var c = SelectionPixels();
            _clip = c;
            if (!UseSystemClipboard) return;
            try
            {
                var bmp = BitmapSource.Create(c.w, c.h, 96, 96, PixelFormats.Bgra32, null, c.px, c.w * 4);
                var png = new System.IO.MemoryStream();
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp)); enc.Save(png);
                var data = new DataObject();
                data.SetImage(bmp);
                data.SetData("PNG", png);
                Clipboard.SetDataObject(data, true);
            }
            catch { } // the clipboard is busy: pasting here still works from _clip
        }

        /// <summary>Copies, then removes the selection (or the whole drawing). Ctrl+X.</summary>
        public void CutSelection()
        {
            Checkpoint();
            CopySelection();
            if (_sel is not { } s) Array.Clear(_data, 0, _data.Length);
            else if (_selBuf != null && _selBase != null) Array.Copy(_selBase, _data, _data.Length);
            else
                for (int y = Math.Max(0, s.y); y < Math.Min(_h, s.y + s.h); y++)
                    Array.Clear(_data, (y * _w + Math.Max(0, s.x)) * 4, (Math.Min(_w, s.x + s.w) - Math.Max(0, s.x)) * 4);
            DropSelection();
            Flush();
        }

        /// <summary>Pastes what was copied (here, in another sprite, or a picture from another program) as a selection
        /// you can drag into place. Ctrl+V.</summary>
        public void PasteClipboard()
        {
            var c = (UseSystemClipboard ? ReadClipboard() : null) ?? _clip;
            if (c == null) return;
            Checkpoint();
            if (_tool != Tool.Select) SelectTool(Tool.Select); else DropSelection();
            _selBase = (byte[])_data.Clone();
            _selBuf = c.Value.px; _sel = (0, 0, c.Value.w, c.Value.h);
            _selOrig = (byte[])_selBuf.Clone(); _selOrigW = c.Value.w; _selOrigH = c.Value.h; _selAngle = 0;
            PasteSel(0, 0);
            Flush(); UpdateMarquee();
        }

        /// <summary>Selects the whole drawing. Ctrl+A.</summary>
        public void SelectAll()
        {
            if (_tool != Tool.Select) SelectTool(Tool.Select); else DropSelection();
            _sel = (0, 0, _w, _h);
            UpdateMarquee();
        }

        private static (byte[] px, int w, int h)? ReadClipboard()
        {
            try
            {
                BitmapSource? src = null;
                if (Clipboard.ContainsData("PNG") && Clipboard.GetData("PNG") is System.IO.Stream png)
                    src = BitmapDecoder.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                else if (Clipboard.ContainsImage()) src = Clipboard.GetImage();
                if (src == null) return null;
                var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
                int w = Math.Min(conv.PixelWidth, 256), h = Math.Min(conv.PixelHeight, 256); // a big picture is cut to the canvas limit
                var px = new byte[w * h * 4];
                conv.CopyPixels(new Int32Rect(0, 0, w, h), px, w * 4, 0);
                return (px, w, h);
            }
            catch { return null; }
        }

        internal (int w, int h)? TestSelectionSize => _sel is { } s ? (s.w, s.h) : null;
        internal void TestSelect(int x, int y, int w, int h) { SelectAll(); DropSelection(); _sel = (x, y, w, h); UpdateMarquee(); }

        // ---- selection / move ----
        private static bool PointIn((int x, int y, int w, int h) s, int x, int y) => x >= s.x && x < s.x + s.w && y >= s.y && y < s.y + s.h;

        private void SelectDown(int x, int y)
        {
            if (_sel is { } s && PointIn(s, x, y))
            {
                if (_selBuf == null) { Checkpoint(); LiftSelection(); }
                _movingSel = true;
                _grabDX = x - _sel!.Value.x; _grabDY = y - _sel!.Value.y;
            }
            else
            {
                DropSelection();
                _movingSel = false;
                _sel = (x, y, 1, 1);
                UpdateMarquee();
            }
        }

        private void LiftSelection()
        {
            var s = _sel!.Value;
            _selBuf = new byte[s.w * s.h * 4];
            for (int j = 0; j < s.h; j++)
                for (int i = 0; i < s.w; i++)
                {
                    int src = ((s.y + j) * _w + (s.x + i)) * 4;
                    int dst = (j * s.w + i) * 4;
                    _selBuf[dst] = _data[src]; _selBuf[dst + 1] = _data[src + 1]; _selBuf[dst + 2] = _data[src + 2]; _selBuf[dst + 3] = _data[src + 3];
                    _data[src] = _data[src + 1] = _data[src + 2] = _data[src + 3] = 0; // clear original
                }
            _selBase = (byte[])_data.Clone();
            _selOrig = (byte[])_selBuf.Clone(); _selOrigW = s.w; _selOrigH = s.h; _selAngle = 0;
            Flush();
        }

        // ---- turn / mirror the selection ----
        // Without a selection it works on the whole drawing (selected first, so it can still be moved afterwards).
        private bool EnsureFloatingSelection()
        {
            if (_tool != Tool.Select) SelectTool(Tool.Select);
            if (_sel == null) { _sel = (0, 0, _w, _h); UpdateMarquee(); }
            if (_selBuf == null) LiftSelection();
            return _selBuf != null && _selBase != null;
        }

        /// <summary>Turns the selection by <paramref name="degrees"/> (clockwise; negative = left) around its centre.</summary>
        public void RotateSelection(double degrees)
        {
            Checkpoint();
            if (!EnsureFloatingSelection()) return;
            _selAngle = ((_selAngle + degrees) % 360 + 360) % 360;
            var (buf, w, h) = Rotate(_selOrig!, _selOrigW, _selOrigH, _selAngle);
            var s = _sel!.Value;
            double cx = s.x + s.w / 2.0, cy = s.y + s.h / 2.0;
            int nx = (int)Math.Round(cx - w / 2.0), ny = (int)Math.Round(cy - h / 2.0);
            _selBuf = buf; _sel = (nx, ny, w, h);
            Array.Copy(_selBase!, _data, _data.Length);
            PasteSel(nx, ny);
            Flush(); UpdateMarquee();
        }

        /// <summary>Mirrors the selection left↔right (<paramref name="horizontal"/>) or up↕down.</summary>
        public void FlipSelection(bool horizontal)
        {
            Checkpoint();
            if (!EnsureFloatingSelection()) return;
            var s = _sel!.Value;
            var flipped = new byte[_selBuf!.Length];
            for (int j = 0; j < s.h; j++)
                for (int i = 0; i < s.w; i++)
                    Array.Copy(_selBuf, (j * s.w + i) * 4, flipped, (horizontal ? j * s.w + (s.w - 1 - i) : (s.h - 1 - j) * s.w + i) * 4, 4);
            _selBuf = flipped; _selOrig = (byte[])flipped.Clone(); _selOrigW = s.w; _selOrigH = s.h; _selAngle = 0;
            Array.Copy(_selBase!, _data, _data.Length);
            PasteSel(s.x, s.y);
            Flush();
        }

        /// <summary>The pixels turned by <paramref name="degrees"/> clockwise: exact for quarter turns, nearest pixel
        /// otherwise (pixel art stays crisp). The result is the turned picture's bounding box.</summary>
        public static (byte[] buf, int w, int h) Rotate(byte[] src, int w, int h, double degrees)
        {
            double d = ((degrees % 360) + 360) % 360;
            if (Math.Abs(d % 90) < 1e-9 || Math.Abs(d % 90 - 90) < 1e-9)
            {
                int k = (int)Math.Round(d / 90) % 4;
                int nw = k % 2 == 1 ? h : w, nh = k % 2 == 1 ? w : h;
                var q = new byte[nw * nh * 4];
                for (int y = 0; y < nh; y++)
                    for (int x = 0; x < nw; x++)
                    {
                        var (sx, sy) = k switch { 1 => (y, h - 1 - x), 2 => (w - 1 - x, h - 1 - y), 3 => (w - 1 - y, x), _ => (x, y) };
                        Array.Copy(src, (sy * w + sx) * 4, q, (y * nw + x) * 4, 4);
                    }
                return (q, nw, nh);
            }
            double rad = d * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
            int ow = (int)Math.Ceiling(Math.Abs(w * cos) + Math.Abs(h * sin) - 1e-6), oh = (int)Math.Ceiling(Math.Abs(w * sin) + Math.Abs(h * cos) - 1e-6);
            var o = new byte[ow * oh * 4];
            for (int y = 0; y < oh; y++)
                for (int x = 0; x < ow; x++)
                {
                    double dx = x + 0.5 - ow / 2.0, dy = y + 0.5 - oh / 2.0;
                    int sx = (int)Math.Floor(dx * cos + dy * sin + w / 2.0), sy = (int)Math.Floor(-dx * sin + dy * cos + h / 2.0); // turn back into the source
                    if (sx < 0 || sy < 0 || sx >= w || sy >= h) continue;
                    Array.Copy(src, (sy * w + sx) * 4, o, (y * ow + x) * 4, 4);
                }
            return (o, ow, oh);
        }

        // ---- custom canvas size ----
        private void CustomCanvas()
        {
            var text = InputDialog.Prompt(this, L.T("Canvas size"), L.T("Width × height in pixels (4 to 256 each), for example 32x128:"), $"{_w}x{_h}");
            if (text == null) return;
            if (ParseSize(text) is not { } size) { MessageBox.Show(this, L.T("Write the size as width x height, for example 32x128 (each from 4 to 256)."), L.T("Sprite editor")); return; }
            if (Confirm(L.F("Start a new blank {0}×{1} canvas? Current drawing will be lost.", size.w, size.h))) { Checkpoint(); NewCanvas(size.w, size.h); }
        }

        /// <summary>"32x128", "32 × 128", "32*128", "32,128" → (32, 128); null if it isn't two sizes from 4 to 256.</summary>
        public static (int w, int h)? ParseSize(string text)
        {
            var m = System.Text.RegularExpressions.Regex.Match(text ?? "", @"^\s*(\d{1,4})\s*[xX×*,;/ ]\s*(\d{1,4})\s*$");
            if (!m.Success) return null;
            int w = int.Parse(m.Groups[1].Value), h = int.Parse(m.Groups[2].Value);
            return w is >= 4 and <= 256 && h is >= 4 and <= 256 ? (w, h) : null;
        }

        private void PasteSel(int ox, int oy)
        {
            var s = _sel!.Value;
            for (int j = 0; j < s.h; j++)
                for (int i = 0; i < s.w; i++)
                {
                    int px = ox + i, py = oy + j;
                    if (px < 0 || py < 0 || px >= _w || py >= _h) continue;
                    int sb = (j * s.w + i) * 4;
                    if (_selBuf![sb + 3] == 0) continue; // keep transparent parts floating
                    int dst = (py * _w + px) * 4;
                    _data[dst] = _selBuf[sb]; _data[dst + 1] = _selBuf[sb + 1]; _data[dst + 2] = _selBuf[sb + 2]; _data[dst + 3] = _selBuf[sb + 3];
                }
        }

        private void SelectMove(int x, int y)
        {
            if (_movingSel && _selBuf != null && _selBase != null)
            {
                int ox = x - _grabDX, oy = y - _grabDY;
                Array.Copy(_selBase, _data, _data.Length);
                _sel = (ox, oy, _sel!.Value.w, _sel!.Value.h);
                PasteSel(ox, oy);
                Flush();
                UpdateMarquee();
            }
            else if (!_movingSel && _sel != null)
            {
                int x0 = Math.Min(_dragX0, x), y0 = Math.Min(_dragY0, y);
                int w = Math.Abs(x - _dragX0) + 1, h = Math.Abs(y - _dragY0) + 1;
                w = Math.Min(w, _w - x0); h = Math.Min(h, _h - y0);
                _sel = (x0, y0, Math.Max(1, w), Math.Max(1, h));
                UpdateMarquee();
            }
        }

        private void DropSelection()
        {
            _selBuf = null; _selBase = null; _sel = null; _movingSel = false; _selOrig = null; _selAngle = 0;
            if (_marquee != null) _marquee.Visibility = Visibility.Collapsed;
        }

        private void UpdateMarquee()
        {
            if (_sel is { } s)
            {
                _marquee.Visibility = Visibility.Visible;
                Canvas.SetLeft(_marquee, s.x * _scale); Canvas.SetTop(_marquee, s.y * _scale);
                _marquee.Width = s.w * _scale; _marquee.Height = s.h * _scale;
            }
            else _marquee.Visibility = Visibility.Collapsed;
        }

        private void Paint(int cx, int cy)
        {
            if (!BrushBox(cx, cy, out int x0, out int y0, out int x1, out int y1)) return;
            StampData(cx, cy, _tool == Tool.Eraser);
            int off = (y0 * _w + x0) * 4;
            _bmp.WritePixels(new Int32Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1), _data, _w * 4, off);
        }

        private void PaintLine(int x0, int y0, int x1, int y1) => WalkLine(x0, y0, x1, y1, Paint);

        private void Pick(int x, int y)
        {
            int i = (y * _w + x) * 4;
            if (_data[i + 3] == 0) return; // transparent → nothing to pick
            _cb = _data[i]; _cg = _data[i + 1]; _cr = _data[i + 2]; _ca = 255;
            _swatch.Fill = new SolidColorBrush(Color.FromArgb(255, _cr, _cg, _cb));
            SelectTool(Tool.Pencil);
        }

        private void FloodFill(int x, int y)
        {
            int start = (y * _w + x) * 4;
            byte tb = _data[start], tg = _data[start + 1], tr = _data[start + 2], ta = _data[start + 3];
            byte nb = _cb, ng = _cg, nr = _cr, na = 255; // fill with the current colour
            if (tb == nb && tg == ng && tr == nr && ta == na) return; // already that colour

            var stack = new System.Collections.Generic.Stack<(int, int)>();
            stack.Push((x, y));
            while (stack.Count > 0)
            {
                var (cx, cy) = stack.Pop();
                if (cx < 0 || cy < 0 || cx >= _w || cy >= _h) continue;
                int i = (cy * _w + cx) * 4;
                if (_data[i] != tb || _data[i + 1] != tg || _data[i + 2] != tr || _data[i + 3] != ta) continue;
                _data[i] = nb; _data[i + 1] = ng; _data[i + 2] = nr; _data[i + 3] = na;
                stack.Push((cx + 1, cy)); stack.Push((cx - 1, cy)); stack.Push((cx, cy + 1)); stack.Push((cx, cy - 1));
            }
            Flush();
        }

        private void SaveAndClose()
        {
            bool anyPixel = false;
            for (int i = 3; i < _data.Length; i += 4) if (_data[i] != 0) { anyPixel = true; break; }
            if (!anyPixel) { MessageBox.Show(this, L.T("The canvas is empty — draw something first."), L.T("Sprite editor")); return; }
            if (string.IsNullOrEmpty(_project.FolderPath)) { MessageBox.Show(this, L.T("Save the project first so sprites have a folder.")); return; }

            var name = InputDialog.Prompt(this, L.T("Save sprite"), L.T("Sprite name:"), "sprite");
            if (name == null) return;
            try
            {
                SavedRelativePath = ProjectIO.SaveSprite(_project, _bmp, name);
                DialogResult = true;
                Close();
            }
            catch (Exception ex) { MessageBox.Show(this, L.T("Error saving:") + " " + ex.Message); }
        }
    }
}
