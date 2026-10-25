using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace EasyEngine;

// Tilemap tiles with pictures: the palette format (old colour-only palettes read and save back unchanged; pictures and
// frames round-trip, tolerantly), the runtime (colour tiles as before, pictured tiles share one brush per kind, an
// animated kind's frames follow one shared clock, pictured tiles still collide / break / drop), and the editor
// (a tilemap's APPEARANCE has no Animations / Static image rows; the palette swatch and the scene show the picture).
public partial class App
{
    partial void FeatureTestsTiles(FullTestKit t)
    {
        // a tilemap (cols × rows, every cell = palette entry 1 unless cells are given) with a palette made in the project
        Action<Project, Scene> Map(double x, double y, int cols, int rows, Func<Project, TileKind[]> palette, int[]? cells = null,
                                   bool breakable = true, string drop = "", string name = "Map") => (p, s) =>
        {
            var l = s.Layers.First(z => z.Kind == LayerKind.Base);
            cells ??= Enumerable.Repeat(1, cols * rows).ToArray();
            var bh = BehaviorCatalog.Info(BehaviorKind.Tilemap).CreateData();
            bh.Texts["palette"] = TilePalette.Serialize(palette(p));
            bh.Texts["data"] = JsonSerializer.Serialize(new { cols, rows, cells });
            bh.Nums["tileSize"] = 32; bh.Nums["breakable"] = breakable ? 1 : 0; bh.Texts["drop"] = drop;
            l.Objects.Add(new GameObjectData { Id = 95, Name = name, Type = "Object", X = x, Y = y, Width = cols * 32, Height = rows * 32, Physics = new PhysicsData { Mode = PhysicsMode.None }, Behaviors = { bh } });
        };
        static string Pic(Project p, string name, int size, string hex = "#C08040") => ProjectIO.SaveSprite(p, HitboxTestSprite(size, size, new Int32Rect(0, 0, size, size), hex), name);
        static TileKind Still(Project p, bool solid = true) { var k = new TileKind("#AA5533", solid); k.SetPicture(Pic(p, "brick", 32)); return k; }
        static TileKind Water(Project p) { var k = new TileKind("#2E6FD8", false); k.SetFrames(new[] { Pic(p, "w1", 32, "#2050C0"), Pic(p, "w2", 32, "#3060D0"), Pic(p, "w3", 32, "#4070E0") }, 4); return k; }

        // ---------------- the saved format
        t.Test("tiles: an old colour-only palette reads and saves back byte-for-byte as before", () =>
        {
            const string old = "[{\"color\":\"#5BA862\",\"solid\":true},{\"color\":\"#6B5A45\",\"solid\":false},{\"color\":\"#3A3A46\",\"solid\":1}]";
            var pal = TilePalette.Parse(old);
            var legacy = JsonSerializer.Serialize(pal.Select(k => new { color = k.Color, solid = k.Solid })); // what older versions wrote
            return pal.Count == 3 && pal[0].Solid && !pal[1].Solid && pal[2].Solid && pal.All(k => !k.HasPicture && !k.Animated)
                   && TilePalette.Serialize(pal) == legacy && TilePalette.Serialize(TilePalette.Parse(legacy)) == legacy
                   && TilePalette.Parse("").Count == 0 && TilePalette.Parse("not json").Count == 0;
        });
        t.Test("tiles: a pictured tile and an animated tile save and load (frames, speed); reading is tolerant", () =>
        {
            var brick = new TileKind("#112233", true); brick.SetPicture("sprites/brick.png");
            var water = new TileKind("#0000FF", false); water.SetFrames(new[] { "sprites/w1.png", "sprites/w2.png" }, 6);
            var back = TilePalette.Parse(TilePalette.Serialize(new[] { brick, water }));
            bool round = back.Count == 2 && back[0].Image == "sprites/brick.png" && back[0].Solid && back[0].Color == "#112233" && !back[0].Animated
                         && back[1].Animated && back[1].Frames.SequenceEqual(new[] { "sprites/w1.png", "sprites/w2.png" }) && back[1].Fps == 6
                         && back[1].FirstPicture == "sprites/w1.png" && back[1].Image == null;
            var loose = TilePalette.Parse("[ /* a tile */ {'color':'#fff','solid':'1','frames':['a.png','b.png',],'fps':'2,5','unknown':3,}, ]".Replace('\'', '"'));
            bool tolerant = loose.Count == 1 && loose[0].Solid && loose[0].Frames.Count == 2 && Math.Abs(loose[0].Fps - 2.5) < 1e-9;
            var one = new TileKind(); one.SetFrames(new[] { "x.png" }, 5);
            var none = new TileKind(); none.SetPicture("y.png"); none.SetFrames(Array.Empty<string>(), 5);
            return round && tolerant && one.Image == "x.png" && !one.Animated && one.Frames.Count == 0 && !none.HasPicture
                   && !TilePalette.Serialize(new[] { new TileKind("#123456", true) }).Contains("image");
        });

        // ---------------- runtime
        t.Test("tiles: an old colour-only tilemap plays as before (each tile its colour, one shared brush, collision)", () =>
        {
            var p = t.Sim("[]", PhysicsMode.None, Combine(BreakMap(200, 600, 3, 1), AddFaller(230, 400)));
            p.TSteps(90);
            var fill = p.TTileCellFill("Map", 1, 0) as SolidColorBrush;
            return fill != null && fill.Color == (Color)ColorConverter.ConvertFromString("#888888") && p.TTileDistinctFills("Map") == 1
                   && p.TTiles("Map") == 3 && Math.Abs(p.TY("Faller") + 20 - 600) < 1.5;
        });
        t.Test("tiles: a pictured tile draws its picture; a picture that's missing falls back to the tile's colour", () =>
        {
            var p = t.Sim("[]", PhysicsMode.None, Map(600, 600, 3, 1, pr =>
            {
                var gone = new TileKind("#00FF00", true); gone.SetPicture("sprites/missing.png");
                return new[] { Still(pr), gone };
            }, new[] { 1, 2, 1 }));
            var img = p.TTileCellImage("Map", 0, 0) as System.Windows.Media.Imaging.BitmapSource;
            var miss = p.TTileCellFill("Map", 1, 0) as SolidColorBrush;
            return img != null && img.PixelWidth == 32 && ReferenceEquals(p.TTileCellFill("Map", 0, 0), p.TTileCellFill("Map", 2, 0))
                   && miss != null && miss.Color == Colors.Lime && p.TTileDistinctFills("Map") == 2;
        });
        t.Test("tiles: an animated tile plays its frames on one shared clock — every copy shows the same frame", () =>
        {
            var p = t.Sim("[]", PhysicsMode.None, Map(600, 600, 4, 3, pr => new[] { Water(pr) }, breakable: true));
            ImageSource?[] Shown() => Enumerable.Range(0, 12).Select(i => p.TTileCellImage("Map", i % 4, i / 4)).ToArray();
            bool Same(ImageSource?[] a) => a[0] != null && a.All(x => ReferenceEquals(x, a[0]));
            var f0 = Shown(); var k0 = p.TTileAnimFrames();
            p.TSteps(20); var f1 = Shown(); var k1 = p.TTileAnimFrames();   // 0.32 s at 4 fps → frame 1
            p.TSteps(16); var f2 = Shown(); var k2 = p.TTileAnimFrames();   // 0.58 s → frame 2
            p.TSteps(14); var k3 = p.TTileAnimFrames();                     // 0.80 s → frame 3 = back to 0
            return Same(f0) && Same(f1) && Same(f2) && !ReferenceEquals(f0[0], f1[0]) && !ReferenceEquals(f1[0], f2[0])
                   && k0.SequenceEqual(new[] { 0 }) && k1.SequenceEqual(new[] { 1 }) && k2.SequenceEqual(new[] { 2 }) && k3.SequenceEqual(new[] { 0 })
                   && p.TTileDistinctFills("Map") == 1;
        });
        t.Test("tiles: pictured tiles still collide, break with the 'Break a tile' block and drop their item", () =>
        {
            var land = t.Sim("[]", PhysicsMode.None, Combine(Map(200, 600, 3, 1, pr => new[] { Still(pr) }, breakable: false), AddFaller(230, 400)));
            land.TSteps(90);
            var brk = t.Sim("[{\"when\":\"start\",\"do\":\"breaktile\",\"where\":\"touching\"}]", PhysicsMode.None, Map(200, 200, 3, 3, pr => new[] { Still(pr) }, drop: "brick"));
            brk.TSteps(2);
            var anim = t.Sim("[{\"when\":\"start\",\"do\":\"breaktile\",\"where\":\"touching\"}]", PhysicsMode.None, Map(200, 200, 3, 3, pr => { var w = Water(pr); w.Solid = true; return new[] { w }; }, drop: "ice"));
            anim.TSteps(30);
            return Math.Abs(land.TY("Faller") + 20 - 600) < 1.5 && brk.TTiles("Map") == 8 && Math.Abs(brk.TItem("brick") - 1) < 0.01 && brk.TLogHas("Broke tile")
                   && anim.TTiles("Map") == 8 && Math.Abs(anim.TItem("ice") - 1) < 0.01 && anim.TLog.Errors == 0;
        });

        // ---------------- editor
        (MainWindow ed, GameObjectData map, GameObjectData plain) Editor()
        {
            var dir = Path.Combine(Path.GetTempPath(), "EE_Full_Tiles_" + Guid.NewGuid().ToString("N")[..6]);
            var project = ProjectIO.CreateNew("Tiles", dir);
            var scene = project.GetStartScene();
            Map(0, 0, 4, 2, pr => new[] { Still(pr), new TileKind("#5BA862", true), Water(pr) }, new[] { 1, 2, 3, 0, 1, 1, 1, 1 })(project, scene);
            var plain = new GameObjectData { Id = 96, Name = "Crate", X = 300, Y = 300, Width = 32, Height = 32, SpritePath = Pic(project, "crate", 32) };
            scene.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(plain);
            var ed = new MainWindow(project);
            return (ed, ed.TestFindObject("Map")!, ed.TestFindObject("Crate")!);
        }
        t.Test("tiles: a tilemap's APPEARANCE has no Animations / Static image / Image fit / Copy look (Opacity stays)", () =>
        {
            var (ed, map, plain) = Editor();
            try
            {
                ed.TestSelect(plain);
                var normal = ed.TestAppearanceLabels().ToList();
                ed.TestSelect(map);
                var tiles = ed.TestAppearanceLabels().ToList();
                string[] gone = { L.T("Animations"), L.T("Static image"), L.T("Image fit"), L.T("📋 Copy look"), L.T("Corner radius"), L.T("Color"), L.T("Border color") };
                return gone.All(normal.Contains) && normal.Contains(L.T("Opacity"))
                       && !tiles.Any(gone.Contains) && tiles.Contains(L.T("Opacity"));
            }
            finally { ed.Close(); }
        });
        t.Test("tiles: right-click erasing a tile takes it off the scene right away (not only in the game), and painting puts it back", () =>
        {
            var (ed, map, _) = Editor();
            try
            {
                ed.TestSelect(map);                       // just selected, as by a click: no redraw by hand
                bool there = ed.TestTilesDrawnAt(0, 0) == 1;
                ed.TestPaintCell(0, 0, erase: true);
                bool erased = ed.TestTilesDrawnAt(0, 0) == 0;
                ed.TestPaintCell(0, 0, erase: false);
                return there && erased && ed.TestTilesDrawnAt(0, 0) == 1;
            }
            finally { ed.Close(); }
        });
        t.Test("tiles: the palette swatch and the scene show the tile's picture (an animated tile its first frame); ✕ goes back to the colour", () =>
        {
            var (ed, map, _) = Editor();
            try
            {
                ed.TestSelect(map);
                ed.TestActiveTile = 2; ed.TestRedraw(); // re-render the scene with the map being edited
                var pal = ed.TestTilePalette;
                ImageSource? Src(Brush? b) => (b as ImageBrush)?.ImageSource;
                bool swatches = Src(ed.TestTileSwatchBackground(1)) is System.Windows.Media.Imaging.BitmapSource s1 && s1.PixelWidth == 32
                                && ed.TestTileSwatchBackground(2) is SolidColorBrush
                                && Src(ed.TestTileSwatchBackground(3)) != null;
                bool scene = ed.TestSceneTileFill(0, 0) is ImageBrush && ed.TestSceneTileFill(1, 0) is SolidColorBrush && ed.TestSceneTileFill(2, 0) is ImageBrush
                             && ReferenceEquals(ed.TestSceneTileFill(0, 0), ed.TestSceneTileFill(0, 1)); // one shared brush per kind
                ed.TestActiveTile = 1; ed.TestChangeTile(map, k => k.ClearPicture());
                var saved = TilePalette.Parse(map.Behaviors.First(b => b.Kind == BehaviorKind.Tilemap).Text("palette", ""));
                bool cleared = ed.TestTileSwatchBackground(1) is SolidColorBrush && ed.TestSceneTileFill(0, 0) is SolidColorBrush
                               && !saved[0].HasPicture && saved[2].Animated && saved[0].Color == "#AA5533" && pal.Count == 3;
                return swatches && scene && cleared;
            }
            finally { ed.Close(); }
        });
        t.Test("tiles: the animator opens focused on one tile (its frames, no animation list) and hands back frames + speed", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "EE_Full_TilesAnim_" + Guid.NewGuid().ToString("N")[..6]);
            var project = ProjectIO.CreateNew("TilesAnim", dir);
            var w = AnimationWindow.ForTile(project, 32, new[] { "sprites/a.png", "sprites/b.png" }, 5);
            try
            {
                return w.TestTileMode && w.TileFrames.SequenceEqual(new[] { "sprites/a.png", "sprites/b.png" }) && w.TileFps == 5
                       && w.ListPane.Visibility == Visibility.Collapsed && w.HeaderButtons.Visibility == Visibility.Collapsed && w.Title == L.T("Animated tile");
            }
            finally { w.Close(); }
        });
    }
}
