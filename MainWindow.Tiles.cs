using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace EasyEngine
{
    // Tilemap tiles with pictures: the selected palette tile gets "🎨 Draw" (the sprite editor at the tile's size),
    // "Choose image…", "🎞 Animate" (the animator, focused on one tile's frames + speed) and ✕ (back to a plain colour).
    // The scene and the palette swatches show a tile's picture (an animated tile's first frame); its colour stays the
    // fallback for a missing picture. Every cell of a kind shares one frozen brush.
    public partial class MainWindow
    {
        private readonly Dictionary<string, ImageBrush?> _tileImgBrushes = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The brush a tile is drawn with: its picture (first frame), or its colour when it has none / it can't load.</summary>
        private Brush TileFill(TileKind tile)
        {
            var pic = tile.FirstPicture;
            if (pic != null && TileImageBrush(pic) is ImageBrush b) return b;
            return Theme.Hex(tile.Color, "#888");
        }

        /// <summary>One brush per palette entry (shared by every cell of that kind).</summary>
        private Brush[] TileFills(List<TileKind> pal)
        {
            var fills = new Brush[pal.Count];
            for (int i = 0; i < pal.Count; i++) fills[i] = TileFill(pal[i]);
            return fills;
        }

        private static Rectangle TileRect(Brush fill, double ts, double opacity)
        {
            var rect = new Rectangle { Width = ts, Height = ts, Fill = fill, IsHitTestVisible = false };
            if (opacity < 1) rect.Opacity = opacity;
            if (fill is ImageBrush) RenderOptions.SetBitmapScalingMode(rect, BitmapScalingMode.NearestNeighbor); // crisp pixel art
            return rect;
        }

        private ImageBrush? TileImageBrush(string relative)
        {
            if (_tileImgBrushes.TryGetValue(relative, out var cached)) return cached;
            ImageBrush? brush = null;
            var abs = ProjectIO.AbsoluteAsset(_project, relative);
            if (abs != null)
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bmp.UriSource = new Uri(abs); bmp.EndInit();
                    brush = new ImageBrush(bmp) { Stretch = Stretch.Fill };
                    if (brush.CanFreeze) brush.Freeze();
                }
                catch { brush = null; }
            _tileImgBrushes[relative] = brush;
            return brush;
        }

        // ---- the selected tile's picture: 🎨 Draw · Choose image… · 🎞 Animate · ✕
        private void TilePictureRow(GameObjectData obj, TileKind tile, Panel panel)
        {
            panel.Children.Add(new TextBlock { Text = L.T("Tile picture"), Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 12, Margin = new Thickness(0, 2, 0, 4) });
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
            Button Btn(string text, string tip, Action act)
            {
                var b = new Button { Content = text, ToolTip = tip, Height = 28, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 4), FontSize = 12, Style = (Style)FindResource("CardButton"), HorizontalContentAlignment = HorizontalAlignment.Center };
                b.Click += (s, e) => act();
                row.Children.Add(b);
                return b;
            }
            int px = TilePixels();
            Btn(L.T("🎨 Draw"), L.F("Draw this tile in the sprite editor ({0}×{0} pixels)", px), () => DrawTile(obj, tile));
            Btn(L.T("Choose image…"), L.T("Use a picture file for this tile"), () => ChooseTileImage(obj, tile));
            Btn(L.T("🎞 Animate"), L.T("Give this tile frames that play in the game (water, lava, torches…) — every copy of it moves together"), () => AnimateTile(obj, tile));
            if (tile.HasPicture)
            {
                var clear = Btn("✕", L.T("Back to a plain colour"), () => ChangeTile(obj, () => tile.ClearPicture()));
                clear.Foreground = (SolidColorBrush)FindResource("Danger");
            }
            panel.Children.Add(row);
            var desc = new TextBlock { Text = tile.Describe(), Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            if (tile.HasPicture && !tile.Animated) L.SetSkip(desc, true); // a file name
            panel.Children.Add(desc);
        }

        /// <summary>The tile's size in pixels for the sprite editor / animator canvas.</summary>
        private int TilePixels() => (int)Math.Clamp(Math.Round(_tmSize), 4, 256);

        private void ChangeTile(GameObjectData obj, Action change)
        {
            PushUndo();
            change();
            _tileImgBrushes.Clear(); // an edited picture may reuse a file name
            SerializeTilemap(); RenderScene(); BuildInspector(obj); Dirty();
        }

        private void DrawTile(GameObjectData obj, TileKind tile)
        {
            // edits the current picture (an animated tile's first frame); a new one is the tile's size
            var cur = tile.FirstPicture;
            var win = new SpriteEditorWindow(_project, TilePixels(), cur == null ? null : ProjectIO.AbsoluteAsset(_project, cur)) { Owner = this };
            if (win.ShowDialog() != true || win.SavedRelativePath is not string rel) return;
            ChangeTile(obj, () => { if (tile.Animated) tile.Frames[0] = rel; else tile.SetPicture(rel); });
        }

        private void ChooseTileImage(GameObjectData obj, TileKind tile)
        {
            var dlg = new OpenFileDialog { Title = L.T("Choose image"), Filter = L.T("Images") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
            if (dlg.ShowDialog() != true) return;
            try { var rel = ProjectIO.ImportAsset(_project, dlg.FileName, "sprites"); ChangeTile(obj, () => tile.SetPicture(rel)); }
            catch (Exception ex) { MessageBox.Show(L.F("Error: {0}", ex.Message)); }
        }

        private void AnimateTile(GameObjectData obj, TileKind tile)
        {
            var start = tile.Frames.Count > 0 ? tile.Frames : tile.FirstPicture is string one ? new List<string> { one } : new List<string>();
            var win = AnimationWindow.ForTile(_project, TilePixels(), start, tile.Fps);
            win.Owner = this;
            win.ShowDialog();
            var frames = win.TileFrames; double fps = win.TileFps;
            if (frames.SequenceEqual(start) && Math.Abs(fps - tile.Fps) < 1e-9) return; // nothing changed
            ChangeTile(obj, () => tile.SetFrames(frames, fps));
        }

        // ---- APPEARANCE for a tilemap object: only what changes how its tiles look (see BuildInspector)
        internal IEnumerable<string> TestAppearanceLabels()
        {
            // the row labels between APPEARANCE and PHYSICS (texts of the first column / buttons), for the tests
            bool inside = false;
            foreach (var child in InspectorRoot.Children)
            {
                if (child is TextBlock t && t.Text == L.T("APPEARANCE")) { inside = true; continue; }
                if (child is TextBlock t2 && t2.Text == L.T("PHYSICS")) yield break;
                if (!inside) continue;
                if (child is Grid g) foreach (var tb in g.Children.OfType<TextBlock>()) yield return tb.Text;
                if (child is Panel p) foreach (var b in p.Children.OfType<Button>()) if (b.Content is string s) yield return s;
                if (child is Grid g2) foreach (var sp in g2.Children.OfType<StackPanel>()) foreach (var b in sp.Children.OfType<Button>()) if (b.Content is string s) yield return s;
            }
        }

        // ---- test hooks
        internal List<TileKind> TestTilePalette => _tmPalette;
        internal int TestActiveTile { get => _tmActive; set => _tmActive = value; }
        internal Brush? TestTileSwatchBackground(int index) =>
            InspectorRoot.Children.OfType<FrameworkElement>().SelectMany(Descendants).OfType<WrapPanel>()
                .Select(w => w.Children.Count > index ? w.Children[index] as Border : null)
                .FirstOrDefault(b => b != null && b.Width == 34 && b.Height == 34)?.Background;
        internal Brush? TestSceneTileFill(int col, int row) => _tmCellRects.TryGetValue(row * _tmCols + col, out var r) ? r.Fill : null;
        internal void TestChangeTile(GameObjectData obj, Action<TileKind> change) => ChangeTile(obj, () => change(_tmPalette[_tmActive - 1]));
        internal void TestRedraw() => RenderScene(); // redraw without an undo step or unsaved changes
        internal void TestPaintCell(int col, int row, bool erase) { if (_tmObj == null) return; PaintAt(new Point(_tmObj.X + (col + 0.5) * _tmSize, _tmObj.Y + (row + 0.5) * _tmSize), erase); } // as a click (right = erase)
        internal int TestTilesDrawnAt(int col, int row) => _tmObj == null ? 0 : SceneCanvas.Children.OfType<Rectangle>().Count(r => Math.Abs(Canvas.GetLeft(r) - (_tmObj.X + col * _tmSize)) < 0.01 && Math.Abs(Canvas.GetTop(r) - (_tmObj.Y + row * _tmSize)) < 0.01);

        private static IEnumerable<object> Descendants(object node)
        {
            yield return node;
            if (node is DependencyObject d)
                foreach (var c in LogicalTreeHelper.GetChildren(d))
                    foreach (var x in Descendants(c)) yield return x;
        }
    }
}
