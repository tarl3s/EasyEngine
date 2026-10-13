using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EasyEngine
{
    // "Blocks" lighting (Day/Night → Lighting style): the light is worked out block by block over the
    // visible area. Each light spreads from its block along the cheapest path (so it flows around walls; with 'Solid
    // blocks stop the light' a solid block costs SolidCost blocks of reach). The result is one small bitmap — one pixel
    // per block, drawn with square pixels — laid over the world in place of the smooth darkness + glow.
    public partial class PlayWindow
    {
        private bool _gridOn;
        private int _gridCell = 16;
        private Canvas? _gridHost;
        private Image? _gridImage;
        private WriteableBitmap? _gridBmp;
        private double _gridOffX;                  // world x of cell column 0 (aligned to the terrain's blocks)
        private int _gx0, _gy0, _gCols, _gRows;    // the grid's first cell and size
        private float[] _gR = Array.Empty<float>(), _gG = Array.Empty<float>(), _gB = Array.Empty<float>(), _gDist = Array.Empty<float>();
        private float[] _gTint = Array.Empty<float>(); // how much of each cell's light comes from lights that colour what they touch
        private bool[] _gSolid = Array.Empty<bool>(), _gSkyLit = Array.Empty<bool>();
        private byte[] _gPixels = Array.Empty<byte>();
        private readonly PriorityQueue<int, float> _gQueue = new();
        private readonly List<int> _gTouched = new();
        private const float SolidCost = 4f;        // a solid block eats this many blocks of a light's reach
        private const float FullUntil = 0.55f;     // a light is at full strength up to this part of its radius, then fades

        private void ConfigureLightGrid(BehaviorData bh)
        {
            _gridOn = bh.Num("lightStyle", 0) >= 0.5;
            _gridCell = bh.Num("lightCell", 16) >= 24 ? 32 : 16;
        }

        private void ResetLightGrid() => _gridOn = false; // a new scene starts Smooth until its Day/Night says otherwise

        // Called from UpdateLighting (every rendered frame, and by the tests).
        private void UpdateLightGrid()
        {
            var smooth = _gridOn ? Visibility.Hidden : Visibility.Visible;
            if (Darkness.Visibility != smooth) { Darkness.Visibility = smooth; Lights.Visibility = smooth; }
            if (!_gridOn) { if (_gridHost != null) _gridHost.Visibility = Visibility.Collapsed; return; }

            if (_gridHost == null)
            {
                _gridImage = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(_gridImage, BitmapScalingMode.NearestNeighbor); // square blocks of light
                _gridHost = new Canvas { IsHitTestVisible = false, RenderTransform = _lightTransform, Children = { _gridImage } };
                Root.Children.Insert(Root.Children.IndexOf(Lights), _gridHost); // over the world (and the darkness slot), under the HUD
            }
            _gridHost.Visibility = Visibility.Visible;
            ComputeLightGrid();
            PaintLightGrid();
        }

        private void ComputeLightGrid()
        {
            int cs = _gridCell;
            double vw = Root.ActualWidth > 10 ? Root.ActualWidth : 960, vh = Root.ActualHeight > 10 ? Root.ActualHeight : 540;

            double maxR = 0;
            foreach (var en in _entities)
                if (IsGridLight(en)) maxR = Math.Max(maxR, LightParams(en).radius + (en.Look?.Shape is LightShapes.Area or LightShapes.Bar ? Math.Max(en.W, en.H) / 2 : 0));
            int margin = (int)Math.Ceiling(maxR / cs) + 1; // lights just off-screen still reach in

            _gridOffX = _terrain != null ? _terrain.X0 - Math.Floor(_terrain.X0 / cs) * cs : 0;
            _gx0 = (int)Math.Floor((-_camX - _gridOffX) / cs) - margin;
            _gy0 = (int)Math.Floor(-_camY / cs) - margin;
            _gCols = (int)Math.Ceiling(vw / cs) + 1 + 2 * margin;
            _gRows = (int)Math.Ceiling(vh / cs) + 1 + 2 * margin;
            int n = _gCols * _gRows;
            if (_gR.Length < n)
            {
                _gR = new float[n]; _gG = new float[n]; _gB = new float[n]; _gDist = new float[n]; _gTint = new float[n];
                _gSolid = new bool[n]; _gSkyLit = new bool[n]; _gPixels = new byte[n * 4];
                Array.Fill(_gDist, float.PositiveInfinity);
            }
            Array.Clear(_gR, 0, n); Array.Clear(_gG, 0, n); Array.Clear(_gB, 0, n); Array.Clear(_gTint, 0, n); Array.Clear(_gSkyLit, 0, n);

            MarkGridSolids(n);

            foreach (var en in _entities)
                if (IsGridLight(en)) SpreadLight(en);

            // the sun and moon stay bright (the sky behind them is already shaded by ShadeSky)
            foreach (var e in _skyBodies)
            {
                if (e.Dead || e.Visual.Visibility != Visibility.Visible) continue;
                double r = Math.Max(e.W, e.H) * 0.5;
                MarkGridArea(e.CX - _camX - r, e.CY - _camY - r, 2 * r, 2 * r, _gSkyLit);
            }
        }

        private bool IsGridLight(Entity en) =>
            !en.Dead && !_skyBodies.Contains(en) && en.LightVisual != null && en.LightVisual.Visibility == Visibility.Visible;

        private void MarkGridSolids(int n)
        {
            Array.Clear(_gSolid, 0, n);
            int cs = _gridCell;
            double wx0 = _gridOffX + _gx0 * cs, wy0 = _gy0 * (double)cs;
            foreach (var s in _solids)
                if (!s.Dead && !s.SolidDisabled && !s.OneWay) MarkGridArea(s.X, s.Y, s.W, s.H, _gSolid);
            var t = _terrain;
            if (t == null) return;
            for (int j = 0; j < _gRows; j++)
            {
                double cy = wy0 + (j + 0.5) * cs;
                if (cy < 0) continue;
                int r = (int)(cy / t.Ts);
                for (int i = 0; i < _gCols; i++)
                    if (t.Cell(t.Column(wx0 + (i + 0.5) * cs), r) is { Broken: false, Solid: true, OneWay: false }) _gSolid[j * _gCols + i] = true;
            }
        }

        // every grid cell whose centre lies in the world box
        private void MarkGridArea(double x, double y, double w, double h, bool[] into)
        {
            int cs = _gridCell;
            int i0 = Math.Max(0, (int)Math.Ceiling((x - _gridOffX) / cs - 0.5) - _gx0), i1 = Math.Min(_gCols - 1, (int)Math.Floor((x + w - _gridOffX) / cs - 0.5) - _gx0);
            int j0 = Math.Max(0, (int)Math.Ceiling(y / cs - 0.5) - _gy0), j1 = Math.Min(_gRows - 1, (int)Math.Floor((y + h) / cs - 0.5) - _gy0);
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++) into[j * _gCols + i] = true;
        }

        private static readonly (int dx, int dy, float cost)[] GridSteps =
            { (1, 0, 1f), (-1, 0, 1f), (0, 1, 1f), (0, -1, 1f), (1, 1, 1.4142f), (1, -1, 1.4142f), (-1, 1, 1.4142f), (-1, -1, 1.4142f) };

        private void LightSourceCell(int idx)
        {
            if (_gDist[idx] == 0) return;
            if (float.IsPositiveInfinity(_gDist[idx])) _gTouched.Add(idx);
            _gDist[idx] = 0; _gQueue.Enqueue(idx, 0);
        }

        private void SpreadLight(Entity en)
        {
            int cs = _gridCell;
            var (radius, intensity) = LightParams(en); // with its effect (flicker, pulse, blink)
            double wx = en.ScreenSpace ? en.CX - _camX : en.CX, wy = en.ScreenSpace ? en.CY - _camY : en.CY;
            var look = en.Look;
            string shape = look?.Shape ?? LightShapes.Circle;
            bool cone = shape is LightShapes.Beam or LightShapes.Half;

            // where the light starts: its centre block, or every block of its rectangle (area) / along its length (bar)
            _gTouched.Clear(); _gQueue.Clear();
            int si = (int)Math.Floor((wx - _gridOffX) / cs) - _gx0, sj = (int)Math.Floor(wy / cs) - _gy0;
            if (shape is LightShapes.Area or LightShapes.Bar)
            {
                double hw = en.W / 2, hh = en.H / 2;
                if (shape == LightShapes.Bar) { if (hw >= hh) hh = 0; else hw = 0; }
                int i0 = (int)Math.Ceiling((wx - hw - _gridOffX) / cs - 0.5) - _gx0, i1 = (int)Math.Floor((wx + hw - _gridOffX) / cs - 0.5) - _gx0;
                int j0 = (int)Math.Ceiling((wy - hh) / cs - 0.5) - _gy0, j1 = (int)Math.Floor((wy + hh) / cs - 0.5) - _gy0;
                if (i0 > i1) i0 = i1 = si;
                if (j0 > j1) j0 = j1 = sj;
                for (int j = Math.Max(0, j0); j <= Math.Min(_gRows - 1, j1); j++)
                    for (int i = Math.Max(0, i0); i <= Math.Min(_gCols - 1, i1); i++) LightSourceCell(j * _gCols + i);
            }
            else if (si >= 0 && sj >= 0 && si < _gCols && sj < _gRows) LightSourceCell(sj * _gCols + si);
            if (_gTouched.Count == 0) return;

            // each Light's own options: solid blocks stop it, it colours what it touches, it adds up with other lights
            var lb = en.Beh(BehaviorKind.LightSource);
            bool blocked = lb?.Num("stopByBlocks", 1) >= 0.5, tint = lb?.Num("tint", 1) >= 0.5, add = lb?.Num("addUp", 1) >= 0.5;

            float R = (float)(radius / cs);
            float cr = 1, cg = 1, cb = 1;
            if (tint)
            {
                var c = ParseColor(lb?.Text("color", "#FFA040") ?? "#FFA040", Colors.White);
                float m = Math.Max(1f, Math.Max(c.R, Math.Max(c.G, c.B)));
                cr = c.R / m; cg = c.G / m; cb = c.B / m; // the light's hue at full strength
            }

            while (_gQueue.TryDequeue(out int idx, out float d))
            {
                if (d > _gDist[idx]) continue; // a stale entry
                int i = idx % _gCols, j = idx / _gCols;
                foreach (var (dx, dy, step) in GridSteps)
                {
                    int ni = i + dx, nj = j + dy;
                    if (ni < 0 || nj < 0 || ni >= _gCols || nj >= _gRows) continue;
                    int nIdx = nj * _gCols + ni;
                    float nd = d + (blocked && _gSolid[nIdx] ? step * SolidCost : step);
                    if (nd >= R || nd >= _gDist[nIdx]) continue;
                    if (float.IsPositiveInfinity(_gDist[nIdx])) _gTouched.Add(nIdx);
                    _gDist[nIdx] = nd;
                    _gQueue.Enqueue(nIdx, nd);
                }
            }

            // a beam / half circle only lights the blocks inside its cone (plus, for a beam, a little around its holder)
            double ca = 0, sa = 0, holder = 0;
            if (cone)
            {
                double a = -look!.Angle * Math.PI / 180; ca = Math.Cos(a); sa = Math.Sin(a);
                if (shape == LightShapes.Beam) holder = LightShapes.HolderRadius(radius);
            }
            foreach (int idx in _gTouched)
            {
                float b = (float)intensity * Math.Clamp((R - _gDist[idx]) / (R * (1 - FullUntil)), 0, 1);
                _gDist[idx] = float.PositiveInfinity; // ready for the next light
                if (cone && b > 0)
                {
                    double dx = _gridOffX + (_gx0 + idx % _gCols + 0.5) * cs - wx, dy = (_gy0 + idx / _gCols + 0.5) * cs - wy;
                    double f = LightShapes.Cone(shape, dx * ca - dy * sa, dx * sa + dy * ca, look!.Spread, look.Soft);
                    if (holder > 0) f = Math.Max(f, 0.85 * Math.Clamp((holder - Math.Sqrt(dx * dx + dy * dy)) / (holder * 0.5), 0, 1));
                    b *= (float)f;
                }
                if (tint) _gTint[idx] = add ? _gTint[idx] + b : Math.Max(_gTint[idx], b);
                if (add) { _gR[idx] += b * cr; _gG[idx] += b * cg; _gB[idx] += b * cb; }
                else { _gR[idx] = Math.Max(_gR[idx], b * cr); _gG[idx] = Math.Max(_gG[idx], b * cg); _gB[idx] = Math.Max(_gB[idx], b * cb); }
            }
        }

        // One premultiplied pixel per cell: the darkness (what the ambient + lights don't cover), and under it a warm tint
        // of the lights' colour where they shine (normal blending can't multiply, so the tint is a deeper shade).
        private void PaintLightGrid()
        {
            float amb = (float)Math.Clamp(1 - Darkness.Opacity, 0, 1);
            var dark = (Darkness.Fill as SolidColorBrush)?.Color ?? Colors.Black;
            int n = _gCols * _gRows;
            for (int k = 0; k < n; k++)
            {
                float ar = _gR[k], ag = _gG[k], ab = _gB[k], acc = Math.Max(ar, Math.Max(ag, ab));
                float lit = Math.Min(1, amb + acc);
                float ad = _gSkyLit[k] ? 0 : 1 - lit; // darkness left
                float s = 0, kr = 0, kg = 0, kb = 0;
                if (_gTint[k] > 0.001f && !_gSkyLit[k])
                {
                    // only as much as the colouring lights (not daylight, not white lights) light this block
                    s = 0.45f * Math.Clamp(Math.Min(_gTint[k], lit - amb) / Math.Max(lit, 0.001f), 0, 1);
                    kr = 0.62f * 255 * ar / acc; kg = 0.62f * 255 * ag / acc; kb = 0.62f * 255 * ab / acc;
                }
                float keep = 1 - ad, alpha = 1 - (1 - s) * keep; // tint, then the darkness over it
                int p = k * 4;
                _gPixels[p] = (byte)Math.Clamp(kb * s * keep + dark.B * ad, 0, 255);
                _gPixels[p + 1] = (byte)Math.Clamp(kg * s * keep + dark.G * ad, 0, 255);
                _gPixels[p + 2] = (byte)Math.Clamp(kr * s * keep + dark.R * ad, 0, 255);
                _gPixels[p + 3] = (byte)Math.Clamp(alpha * 255, 0, 255);
            }

            if (_gridBmp == null || _gridBmp.PixelWidth != _gCols || _gridBmp.PixelHeight != _gRows)
            {
                _gridBmp = new WriteableBitmap(_gCols, _gRows, 96, 96, PixelFormats.Pbgra32, null);
                _gridImage!.Source = _gridBmp;
            }
            _gridBmp.WritePixels(new Int32Rect(0, 0, _gCols, _gRows), _gPixels, _gCols * 4, 0);
            int cs = _gridCell;
            _gridImage!.Width = _gCols * cs; _gridImage.Height = _gRows * cs;
            Canvas.SetLeft(_gridImage, _gridOffX + _gx0 * cs);
            Canvas.SetTop(_gridImage, _gy0 * (double)cs);
        }

        // the grid's premultiplied pixel over a world point (null when Blocks is off or the point is outside the grid)
        private (byte r, byte g, byte b, byte a)? LightGridAt(double wx, double wy)
        {
            if (!_gridOn || _gCols == 0) return null;
            int i = (int)Math.Floor((wx - _gridOffX) / _gridCell) - _gx0, j = (int)Math.Floor(wy / _gridCell) - _gy0;
            if (i < 0 || j < 0 || i >= _gCols || j >= _gRows) return null;
            int p = (j * _gCols + i) * 4;
            return (_gPixels[p + 2], _gPixels[p + 1], _gPixels[p], _gPixels[p + 3]);
        }
    }
}
