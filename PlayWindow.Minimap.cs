using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EasyEngine
{
    // Minimap (BehaviorKind.Minimap): a small HUD map in a corner, one coloured dot per marked thing ("Slime:#9B59F0" —
    // a marker matches an object's name OR its group), a big centred map on a key, and in an endless world a window
    // around the player with the ground line drawn in.
    public partial class PlayWindow
    {
        private partial class Entity
        {
            public Border? MiniBox;                    // the frame (holds the title + MiniCanvas)
            public TextBlock? MiniTitle;               // "MAP — M to close" (big map only)
            public Polygon? MiniGround;                // endless worlds: the ground under the surface line
            public (Func<double, double?>? surface, double x0, double y0, double w, double h) MiniGroundAt; // what it was drawn for
            public List<Ellipse>? MiniDots;            // reused every frame (only the ones in use are visible)
            public bool MiniMax, MiniKeyWas;           // big map open + key edge memory
            public string? MiniMarkersSrc;             // the "markers" text the parsed list below came from
            public List<(string name, string color)>? MiniMarkers;
        }

        private const double EndlessMapWidth = 3000;   // world px shown across the minimap in an endless world
        private const double EndlessMapRatio = 0.6;    // its height / width
        private const string BigMapUi = "the map";     // _uiRects key while the big map is open

        /// <summary>"Slime:#9B59F0, Coin" → (Slime, #9B59F0), (Coin, "") — no colour = the default gray dot.</summary>
        public static List<(string name, string color)> ParseMapMarkers(string s)
        {
            var list = new List<(string, string)>();
            foreach (var part in (s ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int i = part.LastIndexOf(':');
                if (i > 0 && i + 1 < part.Length && part[i + 1] == '#') list.Add((part.Substring(0, i).Trim(), part.Substring(i + 1).Trim()));
                else list.Add((part, ""));
            }
            return list;
        }

        // Builds the minimap overlay (added to the HUD canvas, independent of the object's box).
        private void BuildMinimap(Entity en, BehaviorData bh)
        {
            double size = Math.Max(60, bh.Num("size", 180));
            var inner = new Canvas { Width = size, Height = size * MiniRatio(), ClipToBounds = true };
            en.MiniGround = new Polygon { Fill = FrozenBrush("#AA7D5A37", Colors.Sienna), Visibility = Visibility.Collapsed };
            inner.Children.Add(en.MiniGround);
            en.MiniTitle = new TextBlock { Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 6), Visibility = Visibility.Collapsed };
            var stack = new StackPanel();
            stack.Children.Add(en.MiniTitle); stack.Children.Add(inner);
            en.MiniBox = new Border
            {
                Background = FrozenBrush("#9614141C", Colors.Black),
                BorderBrush = FrozenBrush("#C878788C", Colors.Gray),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = stack
            };
            en.Visual.Opacity = 0; // hide the placeholder box; the overlay is the map
            Hud.Children.Add(en.MiniBox);
            en.MiniCanvas = inner;
            en.MiniDots = new List<Ellipse>();
        }

        // height / width of the small map: the scene's shape, or a fixed window in an endless world
        private double MiniRatio() => _endlessX ? EndlessMapRatio : _scene.Height / Math.Max(1, _scene.Width);

        // Every logic tick (also headless): the maximize key toggles the big map on its press edge.
        partial void MinimapLogic()
        {
            foreach (var en in _entities)
            {
                if (en.MiniCanvas == null || en.Dead) continue;
                var bh = en.Beh(BehaviorKind.Minimap);
                if (bh == null) continue;
                if (bh.Num("canMax", 1) < 0.5) { en.MiniMax = false; continue; }
                bool down = KeyHeld(bh, "maxKey");
                if (down && !en.MiniKeyWas)
                {
                    en.MiniMax = !en.MiniMax;
                    Info("Minimap", en.Data.Name, en.MiniMax ? "Big map opened" : "Big map closed");
                }
                en.MiniKeyWas = down;
            }
        }

        private void UpdateMinimap(Entity en, BehaviorData bh)
        {
            var inner = en.MiniCanvas; var box = en.MiniBox;
            if (inner == null || box == null || en.MiniDots == null) return;
            double vw = Root.ActualWidth > 0 ? Root.ActualWidth : 960, vh = Root.ActualHeight > 0 ? Root.ActualHeight : 540;
            double size = Math.Max(60, bh.Num("size", 180)), ratio = MiniRatio();
            bool big = en.MiniMax;
            double mw = size, mh = size * ratio, dotScale = 1;
            if (big) // ~70% of the screen, same shape (the title sits above the map)
            {
                double k = Math.Min(vw * 0.7 / mw, (vh * 0.7 - 30) / mh);
                mw *= k; mh *= k; dotScale = 2;
            }
            if (inner.Width != mw) inner.Width = mw;
            if (inner.Height != mh) inner.Height = mh;

            // frame + where it sits
            box.Opacity = big ? 1 : Math.Clamp(bh.Num("opacity", 0.8), 0.1, 1);
            box.Background = big ? FrozenBrush("#E00E0E14", Colors.Black) : FrozenBrush("#9614141C", Colors.Black);
            double pad = big ? 10 : 0;
            if (box.Padding.Left != pad) box.Padding = new Thickness(pad);
            en.MiniTitle!.Visibility = big ? Visibility.Visible : Visibility.Collapsed;
            if (big) en.MiniTitle.Text = $"MAP — {KeyLabel(bh.Text("maxKey", "M"))} to close";
            double bw = mw + (big ? 22 : 2), bhh = mh + (big ? 22 + 26 : 2), m = 10, left, top;
            if (big) { left = (vw - bw) / 2; top = (vh - bhh) / 2; }
            else
            {
                string corner = bh.Text("corner", "tr").ToLowerInvariant();
                // top-left starts below the play bar (Stop / Console), which sits over the HUD
                double topLeftY = TopBar.IsVisible && TopBar.ActualHeight > 0 ? TopBar.ActualHeight + 12 + 8 : 64;
                left = corner.Contains('l') ? m : vw - bw - m;
                top = corner.Contains('b') ? vh - bhh - m : corner.Contains('l') ? topLeftY : m;
            }
            Canvas.SetLeft(box, left); Canvas.SetTop(box, top);
            Panel.SetZIndex(box, big ? 1000 : 0);
            if (big) _uiRects[BigMapUi] = new Rect(left, top, bw, bhh); else _uiRects.Remove(BigMapUi);

            // which part of the world the map shows
            var player = FindNamed(bh.Text("player", "Player"));
            double x0 = 0, y0 = 0, ww = Math.Max(1, _scene.Width), wh = Math.Max(1, _scene.Height);
            if (_endlessX)
            {
                double cx = player?.CX ?? -_camX + vw / 2, cy = player?.CY ?? -_camY + vh / 2;
                ww = EndlessMapWidth; wh = EndlessMapWidth * ratio;
                x0 = cx - ww / 2; y0 = cy - wh / 2;
            }
            double sx = mw / ww, sy = mh / wh;

            // the ground (endless worlds): the surface line sampled across the window, filled down to the bottom — redrawn
            // only once the window has moved a map pixel (or the map changed size)
            var ground = en.MiniGround!;
            if (_endlessX && _terrainSurfaceAt != null)
            {
                var at = en.MiniGroundAt;
                if (at.surface != _terrainSurfaceAt || at.w != mw || at.h != mh || Math.Abs(at.x0 - x0) * sx >= 1 || Math.Abs(at.y0 - y0) * sy >= 1)
                {
                    const int n = 64;
                    var pts = new PointCollection(n + 3) { new Point(0, mh) };
                    for (int i = 0; i <= n; i++)
                    {
                        double wx = x0 + ww * i / n;
                        double y = _terrainSurfaceAt(wx) is double gy ? (gy - y0) * sy : mh;
                        pts.Add(new Point(mw * i / n, Math.Clamp(y, 0, mh)));
                    }
                    pts.Add(new Point(mw, mh));
                    pts.Freeze();
                    ground.Points = pts;
                    en.MiniGroundAt = (_terrainSurfaceAt, x0, y0, mw, mh);
                }
                ground.Visibility = Visibility.Visible;
            }
            else ground.Visibility = Visibility.Collapsed;

            // the dots: marked things first, the player last (on top)
            var markersSrc = bh.Text("markers", "");
            if (en.MiniMarkers == null || en.MiniMarkersSrc != markersSrc)
            {
                en.MiniMarkersSrc = markersSrc;
                en.MiniMarkers = ParseMapMarkers(markersSrc);
            }
            var markers = en.MiniMarkers;
            int used = 0;
            void Dot(Entity e, SolidColorBrush b, double r)
            {
                r *= dotScale;
                double px = (e.CX - x0) * sx, py = (e.CY - y0) * sy;
                if (_endlessX && (px < -r || px > mw + r || py < -r || py > mh + r)) return; // outside the window
                if (used == en.MiniDots.Count) { var el = new Ellipse(); en.MiniDots.Add(el); inner.Children.Add(el); }
                var d = en.MiniDots[used++];
                if (d.Width != r * 2) { d.Width = r * 2; d.Height = r * 2; }
                if (d.Fill != b) d.Fill = b;
                d.Visibility = Visibility.Visible;
                Canvas.SetLeft(d, Math.Clamp(px - r, 0, mw)); Canvas.SetTop(d, Math.Clamp(py - r, 0, mh));
            }
            if (markers.Count > 0)
                foreach (var e in _entities)
                {
                    if (e.Dead || e == en || e == player) continue;
                    foreach (var (name, color) in markers)
                        if (e.Data.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || string.Equals(e.Data.Group, name, StringComparison.OrdinalIgnoreCase))
                        {
                            Dot(e, FrozenBrush(color, Color.FromRgb(180, 180, 190)), 2);
                            break;
                        }
                }
            if (player != null) Dot(player, FrozenBrush(bh.Text("playerColor", "#00E5FF"), Color.FromRgb(0, 229, 255)), 3);
            for (int i = used; i < en.MiniDots.Count; i++) en.MiniDots[i].Visibility = Visibility.Collapsed;
        }

        // a destroyed minimap object takes its frame (and the big map's click area) with it
        partial void MinimapEntityRemoved(Entity en)
        {
            if (en.MiniBox == null) return;
            Hud.Children.Remove(en.MiniBox);
            if (en.MiniMax) _uiRects.Remove(BigMapUi);
        }

        // "D1" → "1", "LeftShift" → "Shift" (as the key pickers show them)
        private static string KeyLabel(string key)
        {
            foreach (var (v, l) in ParamSources.Keys) if (v.Equals(key.Trim(), StringComparison.OrdinalIgnoreCase)) return l;
            return key.Trim();
        }
    }
}
