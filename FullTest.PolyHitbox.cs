using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEngine;

// Polygon hitboxes (PolyHitbox.cs, PlayWindow.PolyHitbox.cs): a solid's outline is the ground — land on a ramp, walk up
// and down it, stop at a steep side — and a shape decides what touches; saved only when there is one.
public partial class App
{
    partial void FeatureTestsPolyHitbox(FullTestKit t)
    {
        static List<PolyPoint> P(params double[] v) { var l = new List<PolyPoint>(); for (int i = 0; i < v.Length; i += 2) l.Add(new PolyPoint(v[i], v[i + 1])); return l; }
        // a ramp 300 wide and 150 tall rising to the right, its foot at x 400 on the floor (y 600)
        static double RampTop(double x) => 600 - 150 * Math.Clamp((x - 400) / 300, 0, 1);

        PlayWindow World(List<PolyPoint> shape, double heroX, double heroY, bool platformer = true) => t.Sim("[]", PhysicsMode.None, (p, s) =>
        {
            var layer = s.Layers.First(l => l.Kind == LayerKind.Base);
            GameObjectData Solid(int id, string name, double x, double y, double w, double h, List<PolyPoint>? sh = null)
            {
                var o = new GameObjectData { Id = id, Name = name, X = x, Y = y, Width = w, Height = h, Physics = new PhysicsData { Mode = PhysicsMode.Static, Shape = sh } };
                o.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Solid).CreateData());
                return o;
            }
            layer.Objects.Add(Solid(11, "Floor", 0, 600, 2000, 80));
            layer.Objects.Add(Solid(12, "Ramp", 400, 450, 300, 150, shape));
            var hero = new GameObjectData { Id = 10, Name = "Hero", Type = "Player", X = heroX, Y = heroY, Width = 32, Height = 48, Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            if (platformer) hero.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Platformer).CreateData());
            layer.Objects.Add(hero);
        });
        var ramp = P(0, 1, 1, 0, 1, 1);

        t.Test("polygon hitbox: dropped over a ramp it lands on the slope, not on the top of its box", () =>
        {
            var p = World(ramp, 550, 300); p.TSteps(90);
            double feet = p.TY("Hero") + 48, want = RampTop(550 + 32);
            return Math.Abs(feet - want) < 2 && p.TGrounded("Hero") && feet > 450 + 20;
        });
        t.Test("polygon hitbox: walking right it climbs the ramp, and walks back down it without hopping", () =>
        {
            var p = World(ramp, 300, 552); p.TSteps(5);
            p.TKey("D", true); p.TSteps(70); p.TKey("D", false); p.TSteps(5);
            double x = p.TX("Hero"), feet = p.TY("Hero") + 48;
            bool up = x > 420 && Math.Abs(feet - RampTop(x + 32)) < 3 && feet < 590;
            int air = 0; p.TKey("A", true);
            for (int i = 0; i < 25; i++) { p.TStep(); if (!p.TGrounded("Hero")) air++; }
            p.TKey("A", false);
            return up && air <= 2 && p.TX("Hero") < x - 40;
        });
        t.Test("polygon hitbox: a steep side is a wall — it stops there instead of climbing", () =>
        {
            var cliff = P(0, 1, 0.1, 0, 1, 0, 1, 1); // its left side rises 150 over 30 px
            var p = World(cliff, 300, 552); p.TSteps(5);
            p.TKey("D", true); p.TSteps(60); p.TKey("D", false); p.TSteps(3);
            double right = p.TX("Hero") + 32, feet = p.TY("Hero") + 48;
            return right <= 410 && right > 395 && Math.Abs(feet - 600) < 1;
        });
        t.Test("polygon hitbox: what touches is the shape — a box in its empty corner isn't touching it", () =>
        {
            var p = t.Sim("[]", PhysicsMode.None, (pr, s) =>
            {
                var layer = s.Layers.First(l => l.Kind == LayerKind.Base);
                var gem = new GameObjectData { Id = 20, Name = "Gem", X = 600, Y = 300, Width = 100, Height = 100, Physics = new PhysicsData { Shape = P(0, 0, 1, 0, 0, 1) } };
                gem.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Collectible).CreateData());
                layer.Objects.Add(gem);
                layer.Objects.Add(new GameObjectData { Id = 21, Name = "Player", Type = "Player", X = 670, Y = 370, Width = 20, Height = 20 });
            });
            p.TSteps(5);
            bool apart = p.TExists("Gem");
            p.TPlace("Player", 610, 310); p.TSteps(5);
            return apart && p.THasShape("Gem") && !p.TExists("Gem");
        });
        t.Test("polygon hitbox: saved with the object and left out when there is none; a 2-point shape is the box", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "EE_Full_Poly_" + Guid.NewGuid().ToString("N")[..6]);
            try
            {
                var pr = ProjectIO.CreateNew("Poly", dir);
                var layer = pr.GetStartScene().Layers.First(l => l.Kind == LayerKind.Base);
                layer.Objects.Add(new GameObjectData { Id = 30, Name = "Rock", Physics = new PhysicsData { Shape = P(0, 1, 0.5, 0, 1, 1) } });
                ProjectIO.Save(pr);
                var text = File.ReadAllText(pr.FilePath!);
                var back = ProjectIO.Load(pr.FilePath!).GetStartScene().Layers.SelectMany(l => l.Objects).First(o => o.Name == "Rock");
                return back.Physics.Shape is { Count: 3 } sh && sh[1].X == 0.5 && sh[1].Y == 0 && System.Text.RegularExpressions.Regex.Matches(text, "\"Shape\"").Count == 1
                       && !PolyHitbox.Valid(P(0, 0, 1, 1)) && PolyHitbox.Valid(ramp);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        });
        t.Test("hitbox shape editor: click adds corners, a click on a side goes into it, drag moves, remove; trace hugs the drawing", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "EE_Full_PolyEd_" + Guid.NewGuid().ToString("N")[..6]);
            try
            {
                var pr = ProjectIO.CreateNew("PolyEd", dir);
                var rock = new GameObjectData { Id = 40, Name = "Rock", Width = 100, Height = 50, Color = "#888888" };
                var w = new HitboxShapeWindow(pr, rock);
                w.TestClick(0, 1); w.TestClick(0.5, 0); w.TestClick(1, 1);
                bool three = w.TestPoints.Count == 3 && w.TestInfo.Contains("3");
                w.TestClick(0.5, 1);                       // on the bottom side (1,1)→(0,1): goes in between
                bool inserted = w.TestPoints.Count == 4 && w.TestPoints[3].X == 0.5 && w.TestPoints[3].Y == 1;
                w.TestDrag(1, 0.4, -0.5);                  // dragged past the box: kept inside it
                bool dragged = w.TestPoints[1].X == 0.4 && w.TestPoints[1].Y == 0;
                w.TestRemove(3);
                var result = w.TestConfirm(); w.Close();
                // a drawing: opaque from x 16..40, y 8..64 of a 64×64 picture → traced to about that rectangle
                var traced = HitboxShapeWindow.Trace(HitboxTestSprite(64, 64, new System.Windows.Int32Rect(16, 8, 24, 56)));
                bool trace = traced.Count >= 4 && traced.Count <= 8 && traced.All(p => p.X >= 0.24 && p.X <= 0.63 && p.Y >= 0.12 && p.Y <= 1.0)
                             && traced.Min(p => p.X) < 0.26 && traced.Max(p => p.X) > 0.61;
                return three && inserted && dragged && result is { Count: 3 } && trace;
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        });
        t.Test("hitbox shape: set from the inspector it shows its outline in the scene, and Ctrl+Z takes it back", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "EE_Full_PolyIns_" + Guid.NewGuid().ToString("N")[..6]);
            try
            {
                var pr = ProjectIO.CreateNew("PolyIns", dir);
                var scene = pr.GetStartScene();
                scene.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(new GameObjectData { Id = 50, Name = "Hill", X = 100, Y = 300, Width = 200, Height = 100, Color = "#666666", Physics = new PhysicsData { Mode = PhysicsMode.Static } });
                var ed = new MainWindow(pr);
                GameObjectData Hill() => ed.TestScene.Layers.SelectMany(l => l.Objects).First(o => o.Name == "Hill");
                ed.TestSetShape(Hill(), P(0, 1, 0.5, 0, 1, 1));
                bool set = Hill().Physics.Shape?.Count == 3 && ed.TestHasShapeOutline(Hill());
                ed.TestUndo();
                bool undone = Hill().Physics.Shape == null && !ed.TestHasShapeOutline(Hill());
                ed.Close();
                return set && undone;
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        });
        t.Test("polygon hitbox: a boss's shape is mirrored when it turns, like its picture", () =>
        {
            var p = t.Sim("[]", PhysicsMode.None, BossArena(OneAttack("{'do':'wait','time':5}"), more: (pr, s, l) =>
                l.Objects.First(o => o.Name == "Boss").Physics.Shape = P(0.5, 0, 1, 0, 1, 1, 0.5, 1))); // its right half
            p.TSteps(20);
            double x = p.TX("Boss");
            return p.TFacing("Boss") == -1 && Math.Abs(p.TShapeLeft("Boss") - x) < 0.01; // facing the player on its left: the left half
        });
        t.Test("polygon hitbox: ✂ Trim box to drawing keeps the shape on the same part of the drawing", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "EE_Full_PolyTrim_" + Guid.NewGuid().ToString("N")[..6]);
            try
            {
                var pr = ProjectIO.CreateNew("PolyTrim", dir);
                var o = new GameObjectData { Id = 60, Name = "Rock", X = 100, Y = 100, Width = 64, Height = 64 };
                o.SpritePath = ProjectIO.SaveSprite(pr, HitboxTestSprite(64, 64, new System.Windows.Int32Rect(16, 8, 24, 56)), "rock"); // drawing: x 16..40, y 8..64
                o.Physics.Shape = P(28 / 64.0, 8 / 64.0, 16 / 64.0, 1, 40 / 64.0, 1);                                                 // a triangle over it
                pr.GetStartScene().Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(o);
                var ed = new MainWindow(pr);
                var rock = ed.TestScene.Layers.SelectMany(l => l.Objects).First(x => x.Name == "Rock");
                ed.TestTrim(rock); ed.Close();
                var s = rock.Physics.Shape!;
                bool Near(PolyPoint q, double x, double y) => Math.Abs(q.X - x) < 0.001 && Math.Abs(q.Y - y) < 0.001;
                return rock.Width == 24 && rock.Height == 56 && Near(s[0], 0.5, 0) && Near(s[1], 0, 1) && Near(s[2], 1, 1);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        });
        t.Test("start menu: the shortcut it makes points at the engine and is found again", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "EE_Full_Lnk_" + Guid.NewGuid().ToString("N")[..6]);
            try
            {
                var link = Path.Combine(dir, StartMenu.Name);
                var exe = Environment.ProcessPath!;
                return StartMenu.Make(link, exe) && string.Equals(StartMenu.TargetOf(link), exe, StringComparison.OrdinalIgnoreCase);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        });
        t.Test("polygon hitbox: the geometry — overlap, a box resting on it isn't overlapping, the surface over a band", () =>
        {
            double[] xs = { 0, 100, 100 }, ys = { 100, 0, 100 }; // the ramp shape on a 100×100 box
            return PolyHitbox.OverlapsBox(xs, ys, 3, 80, 60, 10, 10) && !PolyHitbox.OverlapsBox(xs, ys, 3, 0, 0, 30, 30)
                   && !PolyHitbox.OverlapsBox(xs, ys, 3, 40, 40, 10, 10) // its corner (50,50) is on the slope: resting, not inside
                   && Math.Abs(PolyHitbox.TopIn(xs, ys, 3, 40, 60, -1) - 40) < 1e-9 && double.IsPositiveInfinity(PolyHitbox.TopIn(xs, ys, 3, 200, 300, -1))
                   && Math.Abs(PolyHitbox.LeftIn(xs, ys, 3, 50, 60, -1) - 40) < 1e-9;
        });
    }
}
