using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace EasyEngine;

// M7 — the Terrain Generator: a seeded block world (hills, dirt, stone, caves, ore, bedrock, trees) streamed in 16×16
// chunks around the camera, that collides through SolidsNear, can be mined, and can be endless.
public partial class App
{
    partial void FeatureTestsTerrain(FullTestKit t)
    {
        const int Seed = 12345;

        // A test world: "A" (a Platformer with a follow camera, optionally a fixed Use Item tool) + a Terrain object.
        PlayWindow World(Action<BehaviorData>? terrain = null, string script = "[]", string tool = "", double speed = 260, double reach = 100,
                         Action<Project, Scene>? more = null)
            => t.Sim(script, PhysicsMode.Dynamic, (p, s) =>
            {
                var a = s.Layers.SelectMany(l => l.Objects).First(o => o.Name == "A");
                a.X = 300; a.Y = 100; a.Width = 28; a.Height = 46;
                var plat = BehaviorCatalog.Info(BehaviorKind.Platformer).CreateData(); plat.Nums["speed"] = speed; a.Behaviors.Add(plat);
                a.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.FollowCamera).CreateData());
                if (tool.Length > 0)
                {
                    var tu = BehaviorCatalog.Info(BehaviorKind.ToolUse).CreateData();
                    tu.Texts["tool"] = tool; tu.Nums["reach"] = reach;
                    tu.Nums["blockedByWalls"] = reach > 100 ? 0 : 1; // a long test reach hits any block, even one behind others
                    a.Behaviors.Add(tu);
                }
                var tb = BehaviorCatalog.Info(BehaviorKind.Terrain).CreateData();
                tb.Nums["seed"] = Seed; tb.Texts["startOn"] = "A";
                terrain?.Invoke(tb);
                var land = new GameObjectData { Id = 50, Name = "Land", Width = 64, Height = 64, Color = "#00000000" };
                land.Behaviors.Add(tb);
                s.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(land);
                var tree = new GameObjectData { Name = "Tree", Width = 96, Height = 192, Color = "#3C8C3C" };
                var hv = BehaviorCatalog.Info(BehaviorKind.Harvestable).CreateData(); hv.Texts["tool"] = "axe"; hv.Texts["drop"] = "wood";
                tree.Behaviors.Add(hv);
                p.Prefabs.Add(new PrefabDef { Name = "Tree", Template = tree });
                more?.Invoke(p, s);
            });

        // the column / row of A's feet, and a click on a block (each click = one swing; wait for the impact + cooldown)
        int Col(PlayWindow w) => w.TTerrainColumn(w.TX("A") + w.TTerrainBox("A").Width / 2);
        void Hit(PlayWindow w, int c, int r) { var p = w.TTerrainCellCenter(c, r); w.TClick(p.X, p.Y); w.TSteps(25); }
        void Teleport(PlayWindow w, double x) { w.TPlace("A", x, (w.TSurfaceAt(x) ?? 0) - 46); w.TSteps(20); }

        // ---------------- catalog ----------------
        var info = BehaviorCatalog.Info(BehaviorKind.Terrain);
        t.Test("terrain: the Terrain Generator is offered in the editor and to the AI (not hidden), with a beginner description", () =>
            !info.Hidden && info.Icon == "⛰" && info.Desc.Contains("seed") && info.Desc.Contains("near") && info.Nums.Count >= 14 && info.Texts.Count >= 10);
        t.Test("terrain: every text parameter is picked (colors, items, requirement, tree, start object) — none typed", () =>
            info.Texts.All(p => p.Pick is not (PickKind.Text or PickKind.LongText))
            && info.Texts.Where(p => p.Key is "grass" or "dirt" or "stone" or "ore").All(p => p.Pick == PickKind.Color)
            && info.Texts.First(p => p.Key == "requires").Pick == PickKind.Requirement && info.Texts.First(p => p.Key == "tree").Pick == PickKind.ObjectOrPrefab
            && info.Texts.First(p => p.Key == "startOn").Pick == PickKind.Object && info.Texts.Where(p => p.Key.EndsWith("Drop")).All(p => p.Pick == PickKind.Item));
        t.Test("terrain: block size and hills are lists; flags are checkboxes; amounts are sliders; dependent fields hide", () =>
            info.Nums.First(n => n.Key == "tileSize").Options!.Length == 3 && info.Nums.First(n => n.Key == "hills").Options!.Length == 4
            && info.Nums.Where(n => n.Key is "caves" or "ores" or "endless" or "mineable" or "trees").All(n => n.Label.EndsWith("(0/1)"))
            && info.Nums.Where(n => n.Key is "caveAmount" or "treeDensity").All(n => n.Label.EndsWith("(0-1)") && n.ShowIf != null)
            && info.Nums.First(n => n.Key == "width").ShowIf != null && info.Texts.Where(p => p.Key is "requires" or "dirtDrop" or "tree").All(p => p.ShowIf != null));

        // ---------------- generation ----------------
        var g1 = World(); var g2 = World(); var g3 = World(tb => tb.Nums["seed"] = 999);
        int rows = g1.TTerrainRows;
        var sample = (from c in Enumerable.Range(-300, 120).Select(i => i * 5) from r in Enumerable.Range(0, rows) select (c, r)).ToList();
        t.Test("terrain: the same seed builds the same world (block by block, far left to far right)", () =>
            sample.All(p => g1.TTerrainBlock(p.c, p.r) == g2.TTerrainBlock(p.c, p.r)));
        t.Test("terrain: a different seed builds a different world", () =>
            sample.Count(p => g1.TTerrainBlock(p.c, p.r) != g3.TTerrainBlock(p.c, p.r)) > sample.Count / 20);
        t.Test("terrain: a seed of 0 picks a new world once and keeps it in seed:terrain:<name>", () =>
        {
            var w = World(tb => tb.Nums["seed"] = 0);
            return w.TTerrainSeed != 0 && Math.Abs(w.TVar("seed:terrain:Land") - w.TTerrainSeed) < 0.5;
        });
        t.Test("terrain: 'Normal' hills are walkable — neighbouring columns differ by at most 2 blocks", () =>
            Enumerable.Range(-2000, 4000).All(c => Math.Abs(g1.TTerrainSurfaceRow(c + 1) - g1.TTerrainSurfaceRow(c)) <= 2)
            && Enumerable.Range(-2000, 4000).Select(g1.TTerrainSurfaceRow).Distinct().Count() > 5); // …and it really is hilly
        t.Test("terrain: 'Flat' is flat", () => { var w = World(tb => tb.Nums["hills"] = 0); return Enumerable.Range(-500, 1000).All(c => w.TTerrainSurfaceRow(c) == w.TTerrainSurfaceRow(0)); });
        t.Test("terrain: layers — grass on top, then dirt, then stone/ore; the bottom row is bedrock everywhere", () =>
            Enumerable.Range(-200, 400).All(c =>
            {
                int s = g1.TTerrainSurfaceRow(c);
                return g1.TTerrainBlock(c, s - 1) == 0 && g1.TTerrainBlock(c, s) == 1 && Enumerable.Range(1, 3).All(d => g1.TTerrainBlock(c, s + d) == 2)
                       && g1.TTerrainBlock(c, rows - 1) == 5 && g1.TTerrainBlock(c, rows) == 0;
            }));
        int caveCells = 0, oreCells = 0, deepCells = 0;
        for (int c = -400; c < 400; c++)
            for (int r = g1.TTerrainSurfaceRow(c) + 5; r < rows - 2; r++) { deepCells++; int b = g1.TTerrainBlock(c, r); if (b == 0) caveCells++; if (b == 4) oreCells++; }
        t.Test($"terrain: caves never open in the top 4 blocks, but do exist deeper ({100.0 * caveCells / deepCells:0.0}% air), ore too ({100.0 * oreCells / deepCells:0.0}%)", () =>
            Enumerable.Range(-1000, 2000).All(c => Enumerable.Range(0, 4).All(d => g1.TTerrainBlock(c, g1.TTerrainSurfaceRow(c) + d) != 0))
            && caveCells > deepCells / 20 && caveCells < deepCells / 2 && oreCells > deepCells / 100);
        t.Test("terrain: caves off / ores off really turn them off", () =>
        {
            var w = World(tb => { tb.Nums["caves"] = 0; tb.Nums["ores"] = 0; });
            return Enumerable.Range(-100, 200).All(c => Enumerable.Range(w.TTerrainSurfaceRow(c), rows - w.TTerrainSurfaceRow(c)).All(r => w.TTerrainBlock(c, r) is 1 or 2 or 3 or 5));
        });

        // ---------------- start, drawing order, collision ----------------
        t.Test("terrain: 'Start this object on the ground' puts A's feet on the top block at its X", () =>
        {
            var w = World();
            double top = Math.Min(g1.TTerrainSurfaceRow(w.TTerrainColumn(w.TX("A"))), g1.TTerrainSurfaceRow(w.TTerrainColumn(w.TX("A") + 27.9))) * w.TTerrainTs;
            bool placed = Math.Abs(w.TY("A") + 46 - top) < 0.01;
            w.TSteps(60);
            return placed && w.TGrounded("A") && Math.Abs(w.TY("A") + 46 - top) < 0.5;
        });
        t.Test("terrain: chunks are loaded around the player before the first frame; the terrain layer is drawn behind everything (trees too)", () =>
        {
            var w = World();
            return w.TTerrainChunks >= 9 && w.TTerrainChunkLoaded(0, 1) && w.TTerrainLayerIndex == 0
                   && w.TWorldIndexOf("A") > 0 && w.TWorldIndexOfTree() > 0 && w.TWorldIndexOfTree() < w.TWorldIndexOf("A");
        });
        t.Test("terrain: a Background-layer picture stays behind the terrain, the player in front (even listed before 'Land')", () =>
        {
            var w = World(more: (p, s) =>
                s.Layers.First(l => l.Kind == LayerKind.Background).Objects.Add(new GameObjectData { Id = 60, Name = "Hills", Width = 800, Height = 300, Color = "#557799" }));
            int bg = w.TWorldIndexOf("Hills"), layer = w.TTerrainLayerIndex, player = w.TWorldIndexOf("A");
            return bg >= 0 && bg < layer && layer < player;
        });
        t.Test("terrain: a Terrain Generator placed in the Background layer is drawn after that layer's pictures, still behind the player", () =>
        {
            var w = World(more: (p, s) =>
            {
                var baseL = s.Layers.First(l => l.Kind == LayerKind.Base);
                var land = baseL.Objects.First(o => o.Name == "Land");
                baseL.Objects.Remove(land);
                var bg = s.Layers.First(l => l.Kind == LayerKind.Background);
                bg.Objects.Add(new GameObjectData { Id = 60, Name = "Hills", Width = 800, Height = 300, Color = "#557799" });
                bg.Objects.Add(land);
            });
            int hills = w.TWorldIndexOf("Hills"), layer = w.TTerrainLayerIndex, player = w.TWorldIndexOf("A");
            return w.THasTerrain && hills >= 0 && hills < layer && layer < player;
        });
        t.Test("terrain: colliders are only made for the blocks in the asked box", () =>
        {
            var w = World(tb => tb.Nums["hills"] = 0);
            double y = w.TTerrainSurfaceRow(0) * w.TTerrainTs;
            return w.TTerrainCollidersNear(300, y + 1, 60, 60) is >= 4 and <= 9 && w.TTerrainCollidersNear(300, y - 200, 60, 60) == 0;
        });
        t.Test("terrain: a fast fall (capped at 1500 px/s, even in 0.05 s hitches) lands ON the ground, never inside it", () =>
        {
            var w = World();
            double top = w.TY("A") + 46, maxVy = 0;
            w.TPlace("A", w.TX("A"), top - 46 - 3000);
            for (int i = 0; i < 200 && !w.TGrounded("A"); i++) { w.TStep(0.05); maxVy = Math.Max(maxVy, w.TVY("A")); }
            return w.TGrounded("A") && Math.Abs(w.TY("A") + 46 - top) < 0.5 && maxVy <= 1500.001 && maxVy > 1400;
        });
        t.Test("physics: falls are capped at 1500 px/s (no terrain)", () =>
        {
            var p = t.Sim("[]", PhysicsMode.Dynamic, null);
            double m = 0; for (int i = 0; i < 90; i++) { p.TStep(); m = Math.Max(m, p.TVY("A")); }
            return m <= 1500.001 && m > 1400;
        });
        t.Test("terrain: deep underground (below the scene's height) still isn't 'off the world'", () =>
        {
            var w = World(more: (p, s) => s.Height = 900);
            w.TPlace("A", 300, (rows - 4) * w.TTerrainTs); w.TSteps(5);
            return !w.TDead("A");
        });

        // ---------------- streaming ----------------
        t.Test("terrain: walking 5000 px right streams chunks in and out, visuals stay bounded, coming back rebuilds the same blocks", () =>
        {
            var w = World(tb => tb.Nums["hills"] = 0);
            int c0 = Col(w), s0 = w.TTerrainSurfaceRow(c0);
            var before = Enumerable.Range(c0 - 8, 16).SelectMany(c => Enumerable.Range(s0, 30).Select(r => w.TTerrainLive(c, r))).ToList();
            double x0 = w.TX("A"); int maxVis = 0, maxChunks = 0;
            w.TKey("D", true);
            for (int i = 0; i < 1250; i++) { w.TStep(); maxVis = Math.Max(maxVis, w.TTerrainVisuals); maxChunks = Math.Max(maxChunks, w.TTerrainChunks); }
            w.TKey("D", false);
            bool walked = w.TX("A") - x0 > 5000, startGone = !w.TTerrainChunkLoaded(0, 1), hereLoaded = w.TTerrainChunkLoaded(Col(w) / 16, w.TTerrainSurfaceRow(Col(w)) / 16);
            Teleport(w, x0);
            var after = Enumerable.Range(c0 - 8, 16).SelectMany(c => Enumerable.Range(s0, 30).Select(r => w.TTerrainLive(c, r))).ToList();
            return walked && startGone && hereLoaded && maxVis < 4000 && maxChunks <= 35 && before.SequenceEqual(after) && before.Count(b => b) > 300;
        });
        t.Test("terrain: chunks stream in at most 2 per tick (nearest first) after a jump far away; the player never falls meanwhile", () =>
        {
            var w = World();
            double x = w.TX("A") + 20000;
            w.TPlace("A", x, (w.TSurfaceAt(x) ?? 0) - 46);
            int n0 = w.TTerrainBuilt; w.TStep(); int n1 = w.TTerrainBuilt; // the chunks under A load at once (≤ 4), plus ≤ 2 of the view
            w.TStep(); int n2 = w.TTerrainBuilt;
            w.TSteps(30);
            return n1 - n0 is > 0 and <= 6 && n2 - n1 <= 2 && w.TGrounded("A") && Math.Abs(w.TY("A") + 46 - (w.TSurfaceAt(w.TX("A") + 14) ?? -1)) < 33;
        });
        t.Test("terrain: the chunks loaded around a player far from the corner stay while the live camera (still at 0, 0) eases there", () =>
        {
            var w = World(more: (p, s) => s.Layers.SelectMany(l => l.Objects).First(o => o.Name == "A").X = 6000);
            int cx = Col(w) / 16, cy = w.TTerrainSurfaceRow(Col(w)) / 16, before = w.TTerrainChunks;
            bool around = w.TTerrainChunkLoaded(cx - 1, cy) && w.TTerrainChunkLoaded(cx + 1, cy);
            w.TTerrainStreamLive(new System.Windows.Rect(0, 0, 960, 540)); // the first live tick: the camera hasn't moved yet
            return around && w.TTerrainChunks >= before && w.TTerrainChunkLoaded(cx - 1, cy) && w.TTerrainChunkLoaded(cx + 1, cy);
        });
        t.Test("terrain: a far-away walker over unloaded ground waits instead of falling through", () =>
        {
            var w = World(more: (p, s) => s.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(
                new GameObjectData { Name = "Far", X = 30000, Y = 200, Width = 30, Height = 30, Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } }));
            w.TSteps(60);
            return Math.Abs(w.TY("Far") - 200) < 0.01 && !w.TDead("Far");
        });

        // ---------------- mining ----------------
        t.Test("terrain: Use Item on the block under your feet digs it (grass → +1 dirt) and A drops into the hole", () =>
        {
            var w = World(tool: "pickaxe");
            w.TSteps(10);
            int c = Col(w), s = w.TTerrainSurfaceRow(c);
            Hit(w, c, s);
            return !w.TTerrainLive(c, s) && Math.Abs(w.TItem("dirt") - 1) < 0.01 && w.TLogHas("Broke tile");
        });
        t.Test("terrain: stone takes 2 hits (+1 stone), ore 3 (+1 ore); bedrock never breaks", () =>
        {
            var w = World(tool: "pickaxe", reach: 3000);
            w.TSteps(10);
            var cells = Enumerable.Range(Col(w) - 25, 50).SelectMany(c => Enumerable.Range(0, rows).Select(r => (c, r))).Where(p => w.TTerrainLive(p.c, p.r)).ToList();
            var (sc, sr) = cells.First(p => w.TTerrainBlock(p.c, p.r) == 3);
            var (oc, or) = cells.First(p => w.TTerrainBlock(p.c, p.r) == 4);
            Hit(w, sc, sr); bool stoneHeld = w.TTerrainLive(sc, sr);
            Hit(w, sc, sr); bool stoneGone = !w.TTerrainLive(sc, sr) && Math.Abs(w.TItem("stone") - 1) < 0.01;
            Hit(w, oc, or); Hit(w, oc, or); bool oreHeld = w.TTerrainLive(oc, or);
            Hit(w, oc, or); bool oreGone = !w.TTerrainLive(oc, or) && Math.Abs(w.TItem("ore") - 1) < 0.01;
            w.TPlace("A", w.TX("A"), (rows - 3) * w.TTerrainTs); w.TSteps(3); // bring the bottom chunks in
            int bc = Col(w);
            for (int k = 0; k < 4; k++) Hit(w, bc, rows - 1);
            return stoneHeld && stoneGone && oreHeld && oreGone && w.TTerrainLive(bc, rows - 1) && w.TLogHas("bedrock");
        });
        t.Test("terrain: 'Only breaks when' holding:pickaxe refuses without it, digs with it", () =>
        {
            var w = World(tb => tb.Texts["requires"] = "holding:pickaxe", tool: "pickaxe");
            w.TSteps(10);
            int c = Col(w), s = w.TTerrainSurfaceRow(c);
            Hit(w, c, s); bool refused = w.TTerrainLive(c, s) && w.TLogHas("needs holding:pickaxe");
            w.TSetVar("holding:pickaxe", 1);
            Hit(w, c, s);
            return refused && !w.TTerrainLive(c, s);
        });
        t.Test("terrain: the 'Break a tile' block digs below / at the mouse with the material's drop; bullets dig too", () =>
        {
            var w = World(script: "[{\"when\":\"keypress\",\"key\":\"K\",\"do\":\"breaktile\",\"where\":\"below\"}," +
                                  "{\"when\":\"keypress\",\"key\":\"J\",\"do\":\"breaktile\",\"where\":\"atmouse\"}," +
                                  "{\"when\":\"keypress\",\"key\":\"B\",\"do\":\"shoot\",\"directions\":4,\"speed\":600}]");
            w.TSteps(10);
            int c = Col(w);
            w.TKey("K", true); w.TStep(); w.TKey("K", false); w.TSteps(30);
            var d = w.TTerrainDugCells;
            bool below = d.Count == 1 && Math.Abs(d[0].c - c) <= 1 && d[0].r == w.TTerrainSurfaceRow(d[0].c) && !w.TTerrainLive(d[0].c, d[0].r) && Math.Abs(w.TItem("dirt") - 1) < 0.01;
            int s2 = w.TTerrainSurfaceRow(c + 3);
            var p = w.TTerrainCellCenter(c + 3, s2 + 1);
            w.TMouseAt(p.X, p.Y); w.TKey("J", true); w.TStep(); w.TKey("J", false); w.TStep();
            bool atMouse = !w.TTerrainLive(c + 3, s2 + 1) && Math.Abs(w.TItem("dirt") - 2) < 0.01;
            int dug = w.TTerrainDug;
            w.TKey("B", true); w.TStep(); w.TKey("B", false); w.TSteps(30); // 4 bullets in a cross: the one going down digs
            return below && atMouse && w.TTerrainDug > dug;
        });
        t.Test("terrain: dug blocks stay dug after their chunk unloads and comes back", () =>
        {
            var w = World(script: "[{\"when\":\"keypress\",\"key\":\"K\",\"do\":\"breaktile\",\"where\":\"below\"}]");
            w.TSteps(10);
            w.TKey("K", true); w.TStep(); w.TKey("K", false); w.TSteps(20);
            var (c, s) = w.TTerrainDugCells.Single();
            double x0 = w.TX("A");
            Teleport(w, x0 + 8000);
            bool unloaded = !w.TTerrainChunkLoaded(c / 16, s / 16) && !w.TTerrainLive(c - 2, s);
            Teleport(w, x0 + 200);
            return unloaded && w.TTerrainChunkLoaded(c / 16, s / 16) && !w.TTerrainLive(c, s) && w.TTerrainLive(c - 2, w.TTerrainSurfaceRow(c - 2)) && w.TTerrainDug == 1;
        });

        // ---------------- trees ----------------
        t.Test("terrain: trees grow on the grass deterministically, at least 3 columns apart, standing on the ground", () =>
        {
            var a = World(); var b = World();
            var ca = a.TTerrainTreeCols.OrderBy(x => x).ToList(); var cb = b.TTerrainTreeCols.OrderBy(x => x).ToList();
            var tr = a.TTerrainTreeNear(a.TX("A"));
            int tc = a.TTerrainColumn(tr.X + tr.Width / 2);
            return ca.Count >= 2 && ca.SequenceEqual(cb) && ca.Zip(ca.Skip(1)).All(p => p.Second - p.First >= 3)
                   && Math.Abs(tr.Bottom - a.TTerrainSurfaceRow(tc) * a.TTerrainTs) < 0.01;
        });
        t.Test("terrain: a felled tree doesn't grow back when its chunk returns (the others do)", () =>
        {
            var w = World(tool: "axe");
            w.TSteps(10);
            var tr = w.TTerrainTreeNear(w.TX("A"));
            int col = w.TTerrainColumn(tr.X + tr.Width / 2);
            Teleport(w, tr.X - 40);
            for (int k = 0; k < 4; k++) { w.TClick(tr.X + tr.Width / 2, tr.Bottom - 30); w.TSteps(25); }
            w.TSteps(45);
            bool felled = Math.Abs(w.TItem("wood") - 5) < 0.01;
            var others = w.TTerrainTreeCols.Where(x => x != col && Math.Abs(x - Col(w)) < 20).ToList();
            double x0 = w.TX("A");
            Teleport(w, x0 + 9000); Teleport(w, x0);
            var back = w.TTerrainTreeCols;
            return felled && !back.Contains(col) && others.All(back.Contains) && w.TTerrainFelledCount == 1;
        });

        // ---------------- spawn near, endless camera, edges ----------------
        static void AddMob(Project p, Scene s) => p.Prefabs.Add(new PrefabDef { Name = "Mob", Template = new GameObjectData { Name = "Mob", Width = 20, Height = 20, Color = "#FF5555" } });
        t.Test("terrain: spawn at:\"near\" puts the object 300-500 px to the side, standing on the ground", () =>
        {
            var w = World(script: "[{\"when\":\"keypress\",\"key\":\"N\",\"cooldown\":0,\"do\":\"spawn\",\"object\":\"Mob\",\"at\":\"near\"}]", more: AddMob);
            w.TSteps(10);
            for (int k = 0; k < 40 && w.TAlive("Mob") < 8; k++) { w.TKey("N", true); w.TSteps(2); w.TKey("N", false); w.TSteps(2); }
            double ax = w.TX("A") + 14;
            bool OnGroundNear(System.Windows.Rect en)
            {
                double dx = Math.Abs(en.X + en.Width / 2 - ax);
                double top = Enumerable.Range(w.TTerrainColumn(en.X), w.TTerrainColumn(en.Right - 0.001) - w.TTerrainColumn(en.X) + 1).Min(c => w.TTerrainSurfaceRow(c)) * w.TTerrainTs;
                return dx >= 299 && dx <= 501 && Math.Abs(en.Bottom - top) < 0.01;
            }
            var all = w.TTerrainBoxes("Mob");
            return all.Count == 8 && all.All(OnGroundNear);
        });
        t.Test("terrain: spawn at:\"near\" without a terrain lands at the spawner's feet level", () =>
        {
            var p = t.Sim("[{\"when\":\"start\",\"do\":\"spawn\",\"object\":\"Mob\",\"at\":\"near\"}]", PhysicsMode.None, AddMob);
            p.TSteps(2);
            var en = p.TTerrainBox("Mob");
            double dx = Math.Abs(en.X + en.Width / 2 - (p.TX("A") + 32));
            return p.TAlive("Mob") == 1 && dx >= 299 && dx <= 501 && Math.Abs(en.Bottom - (p.TY("A") + 64)) < 0.01;
        });
        t.Test("terrain: endless → the camera X is free (left of 0 and past the scene width), Y stays in the scene", () =>
        {
            var w = World();
            bool endless = w.TEndlessX;
            Teleport(w, -3000); w.TUpdateCamera(200); double left = w.TCamX;
            Teleport(w, 9000); w.TUpdateCamera(200); double right = w.TCamX;
            return endless && left > 2000 && right < -8000 && w.TCamY <= 0;
        });
        t.Test("terrain: a world that isn't endless has edges (nothing past its width, the camera stays clamped)", () =>
        {
            var w = World(tb => { tb.Nums["endless"] = 0; tb.Nums["width"] = 40; });
            int s = w.TTerrainSurfaceRow(10);
            bool edges = w.TTerrainBlock(-1, s + 2) == 0 && w.TTerrainBlock(40, s + 2) == 0 && w.TTerrainBlock(39, w.TTerrainSurfaceRow(39) + 1) != 0
                         && w.TSurfaceAt(-5) == null && w.TSurfaceAt(40 * 32 + 5) == null && w.TSurfaceAt(100) != null;
            w.TPlace("A", -3000, 0); w.TUpdateCamera(200);
            return edges && !w.TEndlessX && w.TCamX <= 0 && !w.TTerrainChunkLoaded(-1, 1) && !w.TTerrainChunkLoaded(3, 1);
        });

        t.Test("terrain: the camera follows the player onto ground below a small scene, and out to a finite world's right edge", () =>
        {
            var w = World(tb => { tb.Nums["hills"] = 0; tb.Nums["endless"] = 0; tb.Nums["width"] = 80; }, more: (p, s) => { s.Width = 960; s.Height = 540; });
            w.TUpdateCamera(200);
            bool down = w.TY("A") + 46 > 540 && Math.Abs(w.TCamY + w.TY("A") + 23) < 1; // the default ground (600 px) is below the 540 px scene
            Teleport(w, 2000); w.TUpdateCamera(200);
            return down && Math.Abs(w.TCamX + w.TX("A") + 14) < 1;
        });

        // ---------------- performance ----------------
        {
            var w = World(speed: 600, more: (p, s) =>
            {
                var layer = s.Layers.First(l => l.Kind == LayerKind.Base);
                for (int i = 0; i < 10; i++)
                {
                    var sl = new GameObjectData { Name = "Slime", X = 500 + i * 70, Y = 200, Width = 40, Height = 30, Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
                    var pl = BehaviorCatalog.Info(BehaviorKind.Platformer).CreateData(); pl.Nums["controls"] = 0; sl.Behaviors.Add(pl);
                    sl.Behaviors.Add(new BehaviorData { Kind = BehaviorKind.Custom, Texts = { ["script"] = "[{\"when\":\"always\",\"do\":\"set\",\"prop\":\"vx\",\"expr\":\"sign(A.cx - self.cx) * 300\"},{\"when\":\"timer\",\"every\":0.6,\"do\":\"jump\",\"force\":560}]" } });
                    layer.Objects.Add(sl);
                }
            });
            double x0 = w.TX("A"); int maxVis = 0, maxChunks = 0;
            w.TKey("D", true);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 600; i++)
            {
                if (i % 30 == 0) w.TKey("Space", true); else if (i % 30 == 10) w.TKey("Space", false); // hop over the hills
                w.TStep();
                maxVis = Math.Max(maxVis, w.TTerrainVisuals); maxChunks = Math.Max(maxChunks, w.TTerrainChunks);
            }
            sw.Stop();
            w.TClearKeys();
            double ms = sw.Elapsed.TotalMilliseconds / 600, chunksCrossed = (w.TX("A") - x0) / (16 * w.TTerrainTs);
            t.Test($"terrain perf: 600 headless frames, player running across {chunksCrossed:0.0} chunks with 10 slimes → {ms:0.00} ms/frame " +
                   $"(streaming avg {w.TTerrainMs:0.00} ms, worst {w.TTerrainMsMax:0.0} ms), ≤ {maxVis} blocks drawn in ≤ {maxChunks} chunks", () =>
                chunksCrossed >= 10 && ms < 8 && maxVis < 6000 && w.TAlive("Slime") == 10);

        // ---------------- "More blocks" and the world's size ----------------
        string Blocks(params TerrainBlockSpec[] b) => TerrainBlockSpec.Serialize(b);
        t.Test("terrain blocks: the list survives saving, and a hand-written one is read tolerantly (numbers in quotes, odd words, no name = 'Block')", () =>
        {
            var cloud = TerrainBlockSpec.For("sky", "Cloud", "#FFFFFF");
            var back = TerrainBlockSpec.Parse(Blocks(cloud)).Single();
            var loose = TerrainBlockSpec.Parse("[{\"name\":\"Gem\",\"where\":\"deep\",\"yFrom\":\"-20\",\"yTo\":-40,\"xOn\":1,\"hits\":\"99\",},{\"color\":\"#fff\"}]");
            return back.Where == "sky" && back.Collide == "platform" && back.YFrom == 4 && back.Name == "Cloud"
                   && loose.Count == 2 && loose[1].Name == "Block" && loose[1].Color == "#fff" && loose[0].Where == "underground" && loose[0].YFrom == -20 && loose[0].XOn && loose[0].Hits == 10
                   && TerrainBlockSpec.Parse("not json").Count == 0 && TerrainBlockSpec.Parse("").Count == 0;
        });
        t.Test("terrain blocks: sky blocks fill their height box above the ground (up = +) and nowhere else; platforms you stand on", () =>
        {
            var cloud = TerrainBlockSpec.For("sky", "Cloud", "#FFFFFF"); cloud.YFrom = 3; cloud.YTo = 6; cloud.Amount = "all";
            var w = World(tb => { tb.Texts["blocks"] = Blocks(cloud); tb.Nums["hills"] = 0; tb.Nums["trees"] = 0; });
            int c = Col(w), g = w.TTerrainBaseRow;
            bool box = Enumerable.Range(3, 4).All(y => w.TTerrainBlock(c, g - y) == 6);
            bool outside = w.TTerrainBlock(c, g - 2) == 0 && w.TTerrainBlock(c, g - 7) == 0 && w.TTerrainBlock(c, g) == 1;
            w.TTerrainStreamLive(new System.Windows.Rect(w.TX("A") - 400, 0, 800, g * 32 + 200));
            var cell = w.TTerrainCellInfo(c, g - 4);
            return box && outside && cell is { solid: true, oneWay: true, hits: 1 };
        });
        t.Test("terrain blocks: underground veins stay in their depth; an area replaces only the ground between its columns", () =>
        {
            var gem = TerrainBlockSpec.For("underground", "Gem", "#5FD4E8"); gem.YFrom = -6; gem.YTo = -9; gem.Amount = "all"; gem.Hits = 5;
            var sand = TerrainBlockSpec.For("area", "Sand", "#E3CF8A"); sand.YFrom = 30; sand.YTo = -3; sand.Amount = "all"; sand.XFrom = 2; sand.XTo = 6; sand.Collide = "none";
            var w = World(tb => { tb.Texts["blocks"] = Blocks(gem, sand); tb.Nums["hills"] = 0; tb.Nums["caves"] = 0; tb.Nums["trees"] = 0; });
            int g = w.TTerrainBaseRow;
            bool veins = Enumerable.Range(6, 4).All(d => w.TTerrainBlock(20, g + d) == 6) && w.TTerrainBlock(20, g + 5) != 6 && w.TTerrainBlock(20, g + 10) != 6;
            bool area = Enumerable.Range(2, 5).All(c => w.TTerrainBlock(c, g) == 7 && w.TTerrainBlock(c, g + 3) == 7)
                        && w.TTerrainBlock(7, g) == 1 && w.TTerrainBlock(2, g - 1) == 0;   // not past its columns, not into the air
            // see-through sand doesn't collide (the generator's own blocks still do)
            w.TTerrainStreamLive(new System.Windows.Rect(-200, 0, 1200, (g + 20) * 32));
            return veins && area && w.TTerrainCellInfo(4, g) is { solid: false } && w.TTerrainCellInfo(20, g + 7) is { hits: 5 };
        });
        t.Test("terrain size: a sized world grows left and right of the generator and ends in invisible walls (the camera stops)", () =>
        {
            var w = World(tb => { tb.Nums["endless"] = 0; tb.Nums["left"] = 10; tb.Nums["width"] = 20; tb.Nums["hills"] = 0; });
            int g = w.TTerrainBaseRow;
            bool span = w.TTerrainBlock(-10, g) == 1 && w.TTerrainBlock(-11, g) == 0 && w.TTerrainBlock(19, g) == 1 && w.TTerrainBlock(20, g) == 0;
            double leftEdge = w.TTerrainCellCenter(-10, g).X - 16;
            Teleport(w, leftEdge + 40); w.TKey("Left", true); w.TSteps(120); w.TKey("Left", false);
            w.TTerrainSnapCamera();
            return span && !w.TEndlessX && w.TEdgeWalls == 2 && w.TX("A") >= leftEdge - 0.5 && -w.TCamX >= leftEdge - 0.5;
        });
        t.Test("terrain size: with open ends the player walks off the edge and falls out of the world (no walls)", () =>
        {
            var w = World(tb => { tb.Nums["endless"] = 0; tb.Nums["left"] = 0; tb.Nums["width"] = 12; tb.Nums["edge"] = 1; tb.Nums["hills"] = 0; });
            double leftEdge = w.TTerrainCellCenter(0, w.TTerrainBaseRow).X - 16;
            Teleport(w, leftEdge + 40); w.TKey("Left", true); w.TSteps(60); w.TKey("Left", false);
            bool past = w.TX("A") < leftEdge;
            w.TSteps(30);
            return w.TEdgeWalls == 0 && past && w.TTerrainBox("A").Y > w.TTerrainCellCenter(0, w.TTerrainBaseRow).Y;
        });
        }
    }
}
