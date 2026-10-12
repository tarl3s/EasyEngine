using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace EasyEngine
{
    // Headless test hooks for the Terrain Generator (PlayWindow.Terrain.cs).
    public partial class PlayWindow
    {
        public bool THasTerrain => _terrain != null;
        public int TTerrainSeed => _terrain?.Seed ?? 0;
        public int TTerrainChunks => _terrain?.Chunks.Count ?? 0;
        public int TTerrainBuilt => _terrain?.Built ?? 0;
        public int TTerrainVisuals => _terrain == null ? 0 : TerrainBlockCount(_terrain);
        public bool TTerrainChunkLoaded(int cx, int cy) => _terrain?.Chunks.ContainsKey((cx, cy)) ?? false;
        public int TTerrainRows => _terrain?.Rows ?? 0;
        public double TTerrainTs => _terrain?.Ts ?? 0;
        public int TTerrainColumn(double x) => _terrain?.Column(x) ?? 0;
        /// <summary>The generated material at (c, r): 0 air, 1 grass, 2 dirt, 3 stone, 4 ore, 5 bedrock (ignores digging).</summary>
        public int TTerrainBlock(int c, int r) => (int)(_terrain?.BlockAt(c, r) ?? Block.Air);
        public int TTerrainSurfaceRow(int c) => _terrain?.SurfaceRow(c) ?? -1;
        /// <summary>A loaded, unbroken block is at (c, r).</summary>
        public bool TTerrainLive(int c, int r) => _terrain?.Cell(c, r) is { Broken: false };
        /// <summary>World centre of block (c, r).</summary>
        public Point TTerrainCellCenter(int c, int r) => _terrain == null ? new Point() : new Point(_terrain.X0 + (c + 0.5) * _terrain.Ts, (r + 0.5) * _terrain.Ts);
        public int TTerrainDug => _terrain?.Dug.Count ?? 0;
        public List<(int c, int r)> TTerrainDugCells => _terrain?.Dug.ToList() ?? new();
        public int TTerrainFelledCount => _terrain?.Felled.Count ?? 0;
        public int TTerrainCollidersNear(double x, double y, double w, double h) => TerrainSolidsNear(x, y, w, h).Count();
        public bool TEndlessX => _endlessX;
        public double? TSurfaceAt(double x) => _terrainSurfaceAt?.Invoke(x);
        public double TTerrainMs => _terrainMs;
        public double TTerrainMsMax => _terrainMsMax;

        /// <summary>Columns of the planted trees still standing.</summary>
        public List<int> TTerrainTreeCols => _entities.Where(e => !e.Dead && !e.Felled && e.TerrainTreeCol != null).Select(e => e.TerrainTreeCol!.Value).ToList();
        /// <summary>Box of the standing planted tree nearest to x (empty if none).</summary>
        public Rect TTerrainTreeNear(double x)
        {
            var t = _entities.Where(e => !e.Dead && !e.Felled && e.TerrainTreeCol != null).OrderBy(e => System.Math.Abs(e.CX - x)).FirstOrDefault();
            return t == null ? Rect.Empty : new Rect(t.X, t.Y, t.W, t.H);
        }
        public Rect TTerrainBox(string name) { var e = TE(name); return e == null ? Rect.Empty : new Rect(e.X, e.Y, e.W, e.H); }
        public List<Rect> TTerrainBoxes(string name) => _entities.Where(e => !e.Dead && e.Data.Name == name).Select(e => new Rect(e.X, e.Y, e.W, e.H)).ToList();

        // drawing order: the terrain layer is World's first child (behind everything)
        public int TTerrainLayerIndex => _terrainLayer == null ? -1 : World.Children.IndexOf(_terrainLayer);
        public int TWorldIndexOf(string name) { var e = TE(name); return e == null ? -1 : World.Children.IndexOf(e.Visual); }
        public int TWorldIndexOfTree() { var e = _entities.FirstOrDefault(z => !z.Dead && z.TerrainTreeCol != null); return e == null ? -1 : World.Children.IndexOf(e.Visual); }

        /// <summary>Runs n live frames back to back in a shown window — logic, camera, render, cleanup and WPF layout, all
        /// on this thread — and returns the average ms per frame (the CPU side of a frame; the GPU composes it afterwards).</summary>
        public double TMeasureFrames(int n)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < n; i++) { StepLogic(0.016); UpdateCamera(0.016); Render(0.016); Cleanup(); UpdateLayout(); }
            return sw.Elapsed.TotalMilliseconds / n;
        }

        /// <summary>Headless runs never render, so the camera transform that Destroy Off-screen reads never moves: put the
        /// camera on its target (like a long live run would).</summary>
        public void TTerrainSnapCamera()
        {
            for (int i = 0; i < 120; i++) UpdateCamera(0.016);
            _camTransform.X = _camX; _camTransform.Y = _camY;
        }
    }
}
