using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EasyEngine
{
    // Tilemap tiles with pictures. Every cell of a tile kind shares ONE brush: a plain colour or a still picture is a
    // frozen brush; an animated kind has one ImageBrush whose picture the game clock swaps — so a map of thousands of
    // water cells changes one property per frame change, every copy moves in sync, and nothing is allocated per frame.
    public partial class PlayWindow
    {
        private sealed class TileAnim
        {
            public ImageBrush Brush = null!;
            public BitmapImage[] Frames = Array.Empty<BitmapImage>();
            public double Fps;
            public int Cur;
        }
        private readonly List<TileAnim> _tileAnims = new();

        /// <summary>One brush per palette entry. A picture that can't load falls back to the tile's colour.</summary>
        private Brush[] TileFills(List<TileKind> pal)
        {
            var fills = new Brush[pal.Count];
            for (int i = 0; i < pal.Count; i++)
            {
                var k = pal[i];
                Brush? fill = null;
                if (k.Animated)
                {
                    var frames = new List<BitmapImage>(k.Frames.Count);
                    foreach (var f in k.Frames) if (Load(f) is BitmapImage b) frames.Add(b);
                    if (frames.Count > 1)
                    {
                        var anim = new TileAnim { Brush = new ImageBrush(frames[0]) { Stretch = Stretch.Fill }, Frames = frames.ToArray(), Fps = Math.Clamp(k.Fps, 1, 60) };
                        _tileAnims.Add(anim);
                        fill = anim.Brush;
                        anim.Cur = -1; SetTileFrame(anim); // start on the shared clock's frame
                    }
                    else if (frames.Count == 1) fill = StillTileBrush(frames[0]);
                }
                else if (k.FirstPicture is string pic && Load(pic) is BitmapImage still) fill = StillTileBrush(still);
                if (fill == null) { var b = Brush(k.Color); b.Freeze(); fill = b; }
                fills[i] = fill;
            }
            return fills;
        }

        private static ImageBrush StillTileBrush(BitmapSource bmp)
        {
            var b = new ImageBrush(bmp) { Stretch = Stretch.Fill };
            if (b.CanFreeze) b.Freeze();
            return b;
        }

        partial void TilesSceneReset() => _tileAnims.Clear();

        partial void TilesLogic()
        {
            for (int i = 0; i < _tileAnims.Count; i++) SetTileFrame(_tileAnims[i]);
        }

        // The frame comes from the game clock (not a timer per cell or per map): all copies of a kind agree.
        private void SetTileFrame(TileAnim a)
        {
            int f = (int)(_gameTime * a.Fps) % a.Frames.Length;
            if (f < 0) f = 0;
            if (f == a.Cur) return;
            a.Cur = f;
            a.Brush.ImageSource = a.Frames[f];
        }

        // ---- test hooks
        /// <summary>The frame each animated tile kind of this scene shows now (in palette order of the maps).</summary>
        public int[] TTileAnimFrames() { var r = new int[_tileAnims.Count]; for (int i = 0; i < r.Length; i++) r[i] = _tileAnims[i].Cur; return r; }
        /// <summary>The picture a breakable tilemap's cell shows now (null = no cell / not a picture).</summary>
        public ImageSource? TTileCellImage(string map, int c, int r) => (TTileCellFill(map, c, r) as ImageBrush)?.ImageSource;
        /// <summary>The brush a breakable tilemap's cell is drawn with (null = no cell).</summary>
        public Brush? TTileCellFill(string map, int c, int r)
        {
            var e = TE(map);
            if (e?.TileGrid == null || c < 0 || r < 0 || c >= e.TileCols || r >= e.TileRows) return null;
            return e.TileGrid[r * e.TileCols + c]?.Visual?.Fill;
        }
        /// <summary>The brush every drawn tile of a tilemap uses, grouped: how many distinct brushes are on screen.</summary>
        public int TTileDistinctFills(string map)
        {
            var e = TE(map); if (e?.Tiles == null) return -1;
            var set = new HashSet<Brush>();
            foreach (var cell in e.Tiles) if (!cell.Broken && cell.Visual?.Fill is Brush b) set.Add(b);
            return set.Count;
        }
    }
}
