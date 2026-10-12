using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyEngine
{
    // Procedural terrain (BehaviorKind.Terrain): seeded generation, chunks streamed around the camera, cells that
    // collide through SolidsNear (not the _solids list), mining, trees.
    //
    // The world is a grid of blocks: column 0 starts at the terrain object's X, row 0 at world Y 0. Every block is a pure
    // function of (seed, column, row), so a chunk can be dropped when it's far away and rebuilt identically later; the
    // only things remembered are the blocks dug out and the trees felled.
    public partial class PlayWindow
    {
        private partial class Entity
        {
            public TerrainState? Terrain;   // the Terrain Generator's object: its world (cells answer like a breakable tilemap's)
            public int? TerrainTreeCol;     // a tree the terrain planted: its column (a felled one doesn't grow back)
        }

        /// <summary>True while an endless terrain is loaded: the world has no left/right edge (camera X unclamped,
        /// the minimap follows the player instead of showing the whole scene).</summary>
        private bool _endlessX;

        /// <summary>World Y of the ground surface at a world X, while a terrain is loaded (null = no terrain there).
        /// Used by the minimap (ground line) and by "spawn near".</summary>
        private Func<double, double?>? _terrainSurfaceAt;

        private const int ChunkSize = 16;    // blocks per chunk side
        private TerrainState? _terrain;      // the scene's terrain (one per scene)
        private Canvas? _terrainLayer;       // in World, right after the Background layer: one child canvas per loaded chunk
        private int _worldBackgroundEnd;     // World.Children count after the Background layer's objects (set in LoadScene)
        private double _terrainBottom;       // world Y of the terrain's bottom (the fall-off-world line is below it)
        private double _terrainMs, _terrainMsMax; // streaming cost per tick (smoothed / worst)
        // per terrain ("scene/object"): blocks dug out + tree columns felled, kept for the whole play session
        private readonly Dictionary<string, (HashSet<(int c, int r)> dug, HashSet<int> felled)> _terrainMemory = new();

        private enum Block : byte { Air, Grass, Dirt, Stone, Ore, Bedrock }
        private static readonly int[] BlockHits = { 0, 1, 1, 2, 3, 0 }; // hits to break each material (bedrock never breaks)

        private sealed class Chunk
        {
            public readonly TileCell?[] Cells = new TileCell?[ChunkSize * ChunkSize]; // [row * ChunkSize + col], null = air
            // cached as one bitmap: a moving camera then composes a few dozen chunk pictures instead of thousands of blocks
            // (digging a block re-draws only its chunk)
            public readonly Canvas Canvas = new() { IsHitTestVisible = false, CacheMode = new BitmapCache() };
            public readonly List<Entity> Trees = new();
        }

        private sealed class TerrainState
        {
            public Entity Host = null!; public BehaviorData Bh = null!;
            public Entity? Focus;                         // the "start on the ground" object: the headless view follows it
            public int Seed, Hills, DirtDepth, BaseRow, Rows, Cols; // Cols 0 = endless
            public double Ts, X0, CaveThreshold, TreeDensity;
            public bool Caves, Ores, Mineable;
            public string TreeName = "";
            public readonly string[] Colors = new string[6];
            public readonly Brush[] Fills = new Brush[12];  // two shades per material (frozen, shared by every block)
            public readonly string?[] Drops = new string?[6];
            public Brush WallDirt = null!, WallStone = null!; // the dark back wall of caves and dug-out blocks
            public readonly Dictionary<(int cx, int cy), Chunk> Chunks = new();
            public int Built;                             // chunks built so far (loads, including reloads)
            public HashSet<(int c, int r)> Dug = null!;
            public HashSet<int> Felled = null!;
            public readonly List<(int cx, int cy)> Want = new(), Drop = new(); // StreamTerrain's scratch lists, reused every tick
            public readonly HashSet<(int cx, int cy)> Under = new();

            // hill shapes: (wavelength in blocks, height in blocks) per octave, for Flat / Gentle / Normal / Rough
            private static readonly (double wl, double amp)[][] HillOctaves =
            {
                Array.Empty<(double, double)>(),
                new[] { (64.0, 3.0), (24.0, 1.0) },
                new[] { (48.0, 6.0), (16.0, 2.0), (6.0, 0.6) },
                new[] { (40.0, 10.0), (14.0, 4.0), (5.0, 1.2) },
            };

            public bool InWorld(int c) => Cols <= 0 || (c >= 0 && c < Cols);
            public int Column(double x) => (int)Math.Floor((x - X0) / Ts);
            public bool ChunkExists(int cx, int cy) => cy >= 0 && cy * ChunkSize < Rows && (Cols <= 0 || (cx >= 0 && cx * ChunkSize < Cols));

            /// <summary>The loaded cell at (c, r) — null for air, outside the world, or a chunk that isn't loaded.</summary>
            public TileCell? Cell(int c, int r)
            {
                if (r < 0 || r >= Rows) return null;
                int cx = FloorDiv(c, ChunkSize), cy = r / ChunkSize;
                return Chunks.TryGetValue((cx, cy), out var ch) ? ch.Cells[(r - cy * ChunkSize) * ChunkSize + c - cx * ChunkSize] : null;
            }

            /// <summary>Row of the top (grass) block of column c: fractal value noise around the ground level.</summary>
            public int SurfaceRow(int c)
            {
                double off = 0; int o = 0;
                foreach (var (wl, amp) in HillOctaves[Hills]) off += amp * (Noise1(c / wl, Seed + 101 * ++o) * 2 - 1);
                return Math.Clamp(BaseRow + (int)Math.Round(off), 2, Rows - 6);
            }

            public Block BlockAt(int c, int r) => BlockAt(c, r, SurfaceRow(c));

            public Block BlockAt(int c, int r, int surf)
            {
                if (!InWorld(c) || r < surf || r >= Rows) return Block.Air;
                if (r == Rows - 1) return Block.Bedrock;
                int below = r - surf;
                // caves: never in the top layers (the ground you walk on stays whole) nor right above the bedrock
                if (Caves && below > 3 && r < Rows - 2 && CaveNoise(c, r) > CaveThreshold) return Block.Air;
                if (below == 0) return Block.Grass;
                if (below <= DirtDepth) return Block.Dirt;
                return Ores && IsOre(c, r) ? Block.Ore : Block.Stone;
            }

            private double CaveNoise(int c, int r) => 0.65 * Noise2(c / 16.0, r / 9.0, Seed + 17) + 0.35 * Noise2(c / 6.0, r / 5.0, Seed + 29);

            // small veins: ~1 in 8 of the 4×4 regions holds a cross-shaped cluster with a few random extra blocks
            private bool IsOre(int c, int r)
            {
                int rx = FloorDiv(c, 4), ry = FloorDiv(r, 4);
                uint h = Hash(rx, ry, Seed + 53);
                if (h % 100 >= 12) return false;
                int oc = rx * 4 + 1 + (int)((h >> 8) & 1), or = ry * 4 + 1 + (int)((h >> 9) & 1);
                int d = Math.Abs(c - oc) + Math.Abs(r - or);
                return d <= 1 || (d == 2 && Rand01(c, r, Seed + 59) < 0.35);
            }

            /// <summary>A tree grows on column c: its hash is under the density and the smallest within 2 columns
            /// (so trees are at least 3 apart), on ground that isn't a slope it would sink into.</summary>
            public bool TreeAt(int c)
            {
                double h = Rand01(c, 0, Seed + 83);
                if (h >= TreeDensity) return false;
                for (int d = -2; d <= 2; d++)
                {
                    double o = Rand01(c + d, 0, Seed + 83);
                    if (d != 0 && o < TreeDensity && (o < h || (o == h && d < 0))) return false;
                }
                int s = SurfaceRow(c);
                return SurfaceRow(c - 1) >= s && SurfaceRow(c + 1) >= s;
            }

            public int ShadeOf(int c, int r) => (int)(Hash(c, r, Seed + 71) & 1);
            public Brush WallFill(int below) => below <= DirtDepth ? WallDirt : WallStone;
        }

        // ---------------------------------------------------------------- noise (integers + a good hash → same world every run)
        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : (a - b + 1) / b;

        private static uint Mix(uint h)
        {
            unchecked { h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16; return h; }
        }

        private static uint Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = Mix((uint)seed ^ 0x9E3779B9u);
                h = Mix(h ^ (uint)x * 0x85EBCA6Bu);
                return Mix(h ^ (uint)y * 0xC2B2AE35u);
            }
        }

        private static double Rand01(int x, int y, int seed) => Hash(x, y, seed) / 4294967296.0;
        private static double Smooth(double f) => f * f * (3 - 2 * f);

        // 1D / 2D value noise in 0..1 (random values on integer points, smoothly blended between them)
        private static double Noise1(double x, int seed)
        {
            int i = (int)Math.Floor(x); double u = Smooth(x - i);
            double a = Rand01(i, 0, seed), b = Rand01(i + 1, 0, seed);
            return a + (b - a) * u;
        }

        private static double Noise2(double x, double y, int seed)
        {
            int i = (int)Math.Floor(x), j = (int)Math.Floor(y);
            double u = Smooth(x - i), v = Smooth(y - j);
            double top = Rand01(i, j, seed) + (Rand01(i + 1, j, seed) - Rand01(i, j, seed)) * u;
            double bottom = Rand01(i, j + 1, seed) + (Rand01(i + 1, j + 1, seed) - Rand01(i, j + 1, seed)) * u;
            return top + (bottom - top) * v;
        }

        // ---------------------------------------------------------------- scene hooks
        partial void TerrainSceneReset()
        {
            _terrain = null; _terrainLayer = null; // World was just cleared: the chunk canvases and trees went with it
            _endlessX = false; _terrainSurfaceAt = null; _terrainBottom = 0;
            _terrainMs = _terrainMsMax = 0;
        }

        partial void TerrainEntityAdded(Entity en)
        {
            var bh = en.Beh(BehaviorKind.Terrain);
            if (bh == null) return;
            if (_terrain != null)
            {
                Warn("Terrain", en.Data.Name, "Only one Terrain Generator per scene — this one is ignored", $"'{_terrain.Host.Data.Name}' already builds the world.");
                return;
            }
            // what this terrain was saved without (an option added later) reads its catalog default, like a new one
            var info = BehaviorCatalog.Info(BehaviorKind.Terrain);
            foreach (var p in info.Nums) bh.Nums.TryAdd(p.Key, p.Default);
            foreach (var p in info.Texts) bh.Texts.TryAdd(p.Key, p.Default);
            double ts = Math.Clamp(bh.Num("tileSize", 32), 8, 128);
            int baseRow = Math.Max(2, (int)Math.Round(bh.Num("surface", 600) / ts));
            string memKey = _sceneName + "/" + en.Data.Name;
            if (!_terrainMemory.TryGetValue(memKey, out var mem)) _terrainMemory[memKey] = mem = (new(), new());
            var t = new TerrainState
            {
                Host = en, Bh = bh, Seed = TerrainSeed(en, bh), Ts = ts, X0 = en.X,
                Hills = Math.Clamp((int)Math.Round(bh.Num("hills", 2)), 0, 3),
                DirtDepth = Math.Max(0, (int)Math.Round(bh.Num("dirtDepth", 4))),
                BaseRow = baseRow, Rows = baseRow + Math.Max(8, (int)Math.Round(bh.Num("depth", 60))),
                Cols = bh.Num("endless", 1) >= 0.5 ? 0 : Math.Max(1, (int)Math.Round(bh.Num("width", 80))),
                Caves = bh.Num("caves", 1) >= 0.5, CaveThreshold = 0.74 - 0.3 * Math.Clamp(bh.Num("caveAmount", 0.35), 0, 1),
                Ores = bh.Num("ores", 1) >= 0.5, Mineable = bh.Num("mineable", 1) >= 0.5,
                TreeDensity = bh.Num("trees", 1) >= 0.5 ? Math.Clamp(bh.Num("treeDensity", 0.25), 0, 1) : 0,
                TreeName = bh.Text("tree", "Tree"), Dug = mem.dug, Felled = mem.felled
            };
            string[] colors = { "", bh.Text("grass", "#5FB548"), bh.Text("dirt", "#8A5A32"), bh.Text("stone", "#767680"), bh.Text("ore", "#E2B23C"), "#2B2B33" };
            static SolidColorBrush Frozen(Color c, double f) => FrozenBrush(Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f)));
            for (int b = 1; b < colors.Length; b++)
            {
                var col = ParseColor(colors[b], Color.FromRgb(0x9A, 0x9A, 0xA0));
                t.Colors[b] = colors[b];
                t.Fills[b * 2] = Frozen(col, 1); t.Fills[b * 2 + 1] = Frozen(col, 0.9);
                if (b == (int)Block.Dirt) t.WallDirt = Frozen(col, 0.4);
                if (b == (int)Block.Stone) t.WallStone = Frozen(col, 0.4);
            }
            t.Drops[(int)Block.Grass] = t.Drops[(int)Block.Dirt] = bh.Text("dirtDrop", "dirt");
            t.Drops[(int)Block.Stone] = bh.Text("stoneDrop", "stone");
            t.Drops[(int)Block.Ore] = bh.Text("oreDrop", "ore");

            en.Terrain = _terrain = t;
            en.TileTS = ts; en.TileX0 = t.X0; en.TileY0 = 0; // so the breakable-tile code (CellAt / BreakOverlap) addresses its cells
            en.Visual.Opacity = 0; _solids.Remove(en);      // the marker object itself is invisible and never collides
            _terrainLayer = new Canvas { IsHitTestVisible = false };
            // right after the Background layer's pictures (sky, parallax hills): behind the player and every other object
            World.Children.Insert(Math.Min(_worldBackgroundEnd, World.Children.Count), _terrainLayer);
            _endlessX = t.Cols == 0;
            _terrainSurfaceAt = x => t.InWorld(t.Column(x)) ? t.SurfaceRow(t.Column(x)) * t.Ts : null;
            _terrainBottom = t.Rows * ts;
        }

        // seed 0 = a new world every game, chosen once and kept in a variable (like the Generator's "once" maps)
        private int TerrainSeed(Entity en, BehaviorData bh)
        {
            int seed = (int)Math.Clamp(Math.Round(bh.Num("seed", 0)), int.MinValue, int.MaxValue);
            return seed != 0 ? seed : PersistedSeed("seed:terrain:" + en.Data.Name);
        }

        partial void TerrainSceneLoaded()
        {
            var t = _terrain;
            if (t == null) return;
            var who = t.Bh.Text("startOn", "Player");
            t.Focus = who.Length == 0 ? null : FindNamed(who, t.Host);
            if (t.Focus != null && TerrainTopUnder(t.Focus.X, t.Focus.W) is double top)
            {
                var f = t.Focus;
                f.Y = top - f.H; f.VY = 0; f.RespawnX = f.X; f.RespawnY = f.Y; // feet on the top block (and respawn there)
            }
            StreamTerrain(t, TerrainView(t, false), int.MaxValue); // everything around the player right now: nothing falls on frame 1
            Info("Terrain", t.Host.Data.Name, $"World seed {t.Seed} · {(t.Cols == 0 ? "endless" : t.Cols + " blocks wide")} · {t.Chunks.Count} chunks loaded",
                 $"blocks {t.Ts:0}px · ground at row {t.BaseRow} · {t.Rows} rows deep" + (t.Focus != null ? $" · {t.Focus.Data.Name} placed on the ground at ({t.Focus.X:0}, {t.Focus.Y:0})" : ""));
        }

        partial void TerrainEntityRemoved(Entity en)
        {
            if (en.TerrainTreeCol is int col) _terrain?.Felled.Add(col); // a planted tree that died (felled) doesn't grow back
        }

        partial void CameraClampOverride(ref bool clampX, ref bool clampY)
        {
            if (_endlessX) clampX = false; // no left/right edge to stop at; Y stays inside the scene
        }

        partial void TerrainBeforeLogic(double dt)
        {
            var t = _terrain;
            if (t == null) return;
            long t0 = Stopwatch.GetTimestamp();
            StreamTerrain(t, TerrainView(t, true), 2);
            double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            _terrainMs = _terrainMs * 0.95 + ms * 0.05; _terrainMsMax = Math.Max(_terrainMsMax, ms);
        }

        // ---------------------------------------------------------------- streaming
        // What the player sees: the camera's view live; headless (tests) a 960×540 view around the followed object.
        private Rect TerrainView(TerrainState t, bool useCamera)
        {
            if (useCamera && !_headless && Root.ActualWidth > 10) return new Rect(-_camX, -_camY, Root.ActualWidth, Root.ActualHeight);
            var f = _cameraTarget ?? t.Focus;
            double cx = f?.CX ?? t.X0, cy = f?.CY ?? t.BaseRow * t.Ts;
            return new Rect(cx - 480, cy - 270, 960, 540);
        }

        // Load the chunks covering the view + 1 chunk around it (at most `budget` per call, nearest first), load the chunks
        // under anything that moves at once (so nothing ever falls into an unloaded chunk) and drop the chunks more than
        // 2 chunks outside the view.
        private void StreamTerrain(TerrainState t, Rect view, int budget)
        {
            int vx0 = FloorDiv(t.Column(view.Left), ChunkSize), vx1 = FloorDiv(t.Column(view.Right), ChunkSize);
            int vy0 = FloorDiv((int)Math.Floor(view.Top / t.Ts), ChunkSize), vy1 = FloorDiv((int)Math.Floor(view.Bottom / t.Ts), ChunkSize);
            int loaded = 0, unloaded = 0;

            var want = t.Want; want.Clear();
            for (int cy = vy0 - 1; cy <= vy1 + 1; cy++)
                for (int cx = vx0 - 1; cx <= vx1 + 1; cx++)
                    if (t.ChunkExists(cx, cy) && !t.Chunks.ContainsKey((cx, cy))) want.Add((cx, cy));
            if (want.Count > 0)
            {
                double mx = (vx0 + vx1) / 2.0, my = (vy0 + vy1) / 2.0;
                foreach (var k in want.OrderBy(k => Math.Abs(k.cx - mx) + Math.Abs(k.cy - my)).Take(budget)) { LoadChunk(t, k); loaded++; }
            }

            // the chunks under things that move near the view (and under the player wherever it is) load right away;
            // movers further out wait in place until the view comes back (TerrainFrozen)
            bool Kept(int cx, int cy) => cx >= vx0 - 2 && cx <= vx1 + 2 && cy >= vy0 - 2 && cy <= vy1 + 2;
            var under = t.Under; under.Clear();
            foreach (var e in _entities)
            {
                if (e.Dead || e.ScreenSpace || !IsMover(e)) continue;
                var (c0, c1, r0, r1) = ChunksUnder(t, e, 2 * t.Ts);
                if (e != _cameraTarget && e != t.Focus && (c1 < vx0 - 2 || c0 > vx1 + 2 || r1 < vy0 - 2 || r0 > vy1 + 2)) continue;
                for (int cy = r0; cy <= r1; cy++)
                    for (int cx = c0; cx <= c1; cx++)
                        if (t.ChunkExists(cx, cy)) under.Add((cx, cy));
            }
            foreach (var k in under)
                if (!t.Chunks.ContainsKey(k)) { LoadChunk(t, k); loaded++; }

            var drop = t.Drop; drop.Clear();
            foreach (var k in t.Chunks.Keys)
                if (!Kept(k.cx, k.cy) && !under.Contains(k)) drop.Add(k);
            foreach (var k in drop) { UnloadChunk(t, k); unloaded++; }

            if (loaded + unloaded > 0)
                LogThrottled("terrain-stream", 2.0, LogLevel.Info, "Terrain", t.Host.Data.Name, $"Streaming: {t.Chunks.Count} chunks in memory ({TerrainVisualCount(t)} rectangles drawn)",
                             $"this tick +{loaded} / −{unloaded} chunks · view chunks x {vx0}..{vx1}, y {vy0}..{vy1}");
        }

        private static int TerrainVisualCount(TerrainState t) => t.Chunks.Values.Sum(ch => ch.Canvas.Children.Count);

        // Something that falls or walks into walls, by the physics' own per-frame classification (UpdatePhysics). A new object
        // is classified in its first tick, and TerrainFrozen holds it there until the ground under it has loaded.
        private static bool IsMover(Entity e) => e.Terrain == null && (e.FrameDynamic || e.FrameTopDown);

        // Chunk range under an entity's box grown by `margin` + one hitchy frame (0.05 s) of its travel.
        private static (int c0, int c1, int r0, int r1) ChunksUnder(TerrainState t, Entity e, double margin)
        {
            double mx = margin + Math.Abs(e.VX) * 0.05, my = margin + Math.Abs(e.VY) * 0.05;
            return (FloorDiv(t.Column(e.X - mx), ChunkSize), FloorDiv(t.Column(e.X + e.W + mx), ChunkSize),
                    FloorDiv((int)Math.Floor((e.Y - my) / t.Ts), ChunkSize), FloorDiv((int)Math.Floor((e.Y + e.H + my) / t.Ts), ChunkSize));
        }

        /// <summary>A mover over terrain that isn't loaded (far from the view) waits where it is (no physics this tick)
        /// instead of falling through the missing ground.</summary>
        private bool TerrainFrozen(Entity en)
        {
            var t = _terrain;
            if (t == null) return false;
            var (c0, c1, r0, r1) = ChunksUnder(t, en, t.Ts);
            for (int cy = r0; cy <= r1; cy++)
                for (int cx = c0; cx <= c1; cx++)
                    if (t.ChunkExists(cx, cy) && !t.Chunks.ContainsKey((cx, cy))) return true;
            return false;
        }

        private void LoadChunk(TerrainState t, (int cx, int cy) k)
        {
            var ch = new Chunk();
            int r0 = k.cy * ChunkSize;
            double ts = t.Ts;
            for (int lc = 0; lc < ChunkSize; lc++)
            {
                int c = k.cx * ChunkSize + lc;
                if (!t.InWorld(c)) continue;
                int surf = t.SurfaceRow(c);
                double x = t.X0 + c * ts;
                // underground air (caves, dug-out blocks) shows a dark back wall, one rectangle per run of rows
                int wallFrom = 0; Brush? wallFill = null;
                for (int lr = 0; lr <= ChunkSize; lr++) // one row past the chunk closes the last run
                {
                    int r = r0 + lr;
                    var b = lr < ChunkSize ? t.BlockAt(c, r, surf) : Block.Air;
                    if (t.Dug.Contains((c, r))) b = Block.Air;
                    var wall = lr < ChunkSize && b == Block.Air && r > surf && r < t.Rows ? t.WallFill(r - surf) : null;
                    if (wall != wallFill)
                    {
                        if (wallFill != null) ch.Canvas.Children.Add(BlockRect(x, wallFrom * ts, ts, (r - wallFrom) * ts, wallFill));
                        wallFill = wall; wallFrom = r;
                    }
                    if (b == Block.Air) continue;
                    double y = r * ts;
                    var rect = BlockRect(x, y, ts, ts, t.Fills[(int)b * 2 + t.ShadeOf(c, r)]);
                    ch.Canvas.Children.Add(rect);
                    ch.Cells[lr * ChunkSize + lc] = new TileCell
                    {
                        C = c, R = r, X = x, Y = y, W = ts, H = ts, Solid = true, Hp = BlockHits[(int)b],
                        Unbreakable = b == Block.Bedrock, Drop = t.Drops[(int)b], Color = t.Colors[(int)b], Visual = rect
                    };
                }
                if (surf >= r0 && surf < r0 + ChunkSize) PlantTree(t, ch, c, surf);
            }
            t.Chunks[k] = ch; t.Built++;
            _terrainLayer?.Children.Add(ch.Canvas);
        }

        private static System.Windows.Shapes.Rectangle BlockRect(double x, double y, double w, double h, Brush fill)
        {
            // +1 px overlap: no hairline seams between blocks, nor between the cached chunk pictures
            var rect = new System.Windows.Shapes.Rectangle { Width = w + 1, Height = h + 1, Fill = fill };
            Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y);
            return rect;
        }

        // A block was dug out (DamageCell): remembered, and below the grass its rectangle becomes the dark back wall.
        private static void TerrainCellDug(TerrainState t, TileCell cell)
        {
            t.Dug.Add((cell.C, cell.R));
            int below = cell.R - t.SurfaceRow(cell.C);
            if (below > 0 && cell.Visual != null) cell.Visual.Fill = t.WallFill(below);
            else if (cell.Visual?.Parent is Panel host) host.Children.Remove(cell.Visual);
        }

        private void PlantTree(TerrainState t, Chunk ch, int c, int surf)
        {
            if (t.TreeDensity <= 0 || t.TreeName.Length == 0 || t.Felled.Contains(c) || t.Dug.Contains((c, surf)) || !t.TreeAt(c)) return;
            var tree = SpawnAt(t.TreeName, 0, 0);
            if (tree == null)
            {
                LogThrottled("terrain-tree", 5.0, LogLevel.Warn, "Terrain", t.Host.Data.Name, $"No object or prefab named '{t.TreeName}' to plant as a tree", "Pick the tree in the Terrain Generator's 'Tree', or turn Trees off.");
                return;
            }
            tree.X = tree.StartX = t.X0 + (c + 0.5) * t.Ts - tree.W / 2;
            tree.Y = tree.StartY = surf * t.Ts - tree.H;
            tree.TerrainTreeCol = c;
            // runtime visuals are appended on top of everything: move the tree just above the terrain, behind the player
            World.Children.Remove(tree.Visual);
            World.Children.Insert(World.Children.IndexOf(_terrainLayer) + 1, tree.Visual);
            ch.Trees.Add(tree);
        }

        private void UnloadChunk(TerrainState t, (int cx, int cy) k)
        {
            var ch = t.Chunks[k];
            _terrainLayer?.Children.Remove(ch.Canvas);
            foreach (var tree in ch.Trees)
            {
                if (tree.TerrainTreeCol is int col && (tree.Dead || tree.Felled)) t.Felled.Add(col);
                tree.TerrainTreeCol = null; // gone because it's far away, not felled: it grows back when the chunk returns
                tree.Dead = true;
            }
            t.Chunks.Remove(k);
        }

        // A terrain cell as a collider entity (SolidsNear): made on first use and cached in the cell.
        private static Entity TerrainCollider(TerrainState t, TileCell cell)
            => cell.Collider ??= new Entity { Data = t.Host.Data, X = cell.X, Y = cell.Y, W = cell.W, H = cell.H };

        // World Y of the highest generated ground top under the span [x, x + w] (null = no terrain there).
        private double? TerrainTopUnder(double x, double w)
        {
            var t = _terrain;
            if (t == null) return null;
            int best = int.MaxValue;
            for (int c = t.Column(x), c1 = t.Column(x + Math.Max(0, w - 0.001)); c <= c1; c++)
                if (t.InWorld(c)) best = Math.Min(best, t.SurfaceRow(c));
            return best == int.MaxValue ? null : best * t.Ts;
        }

        // Custom "spawn" with at:"near": 300-500 px to the left or right of the spawner, standing on the ground there
        // (at the spawner's feet level when there is no terrain).
        private void SpawnNear(Entity spawner, string name)
        {
            var template = FindTemplateOrPrefab(name);
            if (template == null) return;
            double side = _rng.Next(2) == 0 ? -1 : 1, d = 300 + _rng.NextDouble() * 200;
            double x = spawner.CX + side * d - template.Width / 2;
            double? top = TerrainTopUnder(x, template.Width);
            if (top == null && _terrain != null) { x = spawner.CX - side * d - template.Width / 2; top = TerrainTopUnder(x, template.Width); } // the world's edge: the other side
            SpawnAt(name, x, (top ?? spawner.Y + spawner.H) - template.Height);
        }
    }
}
