using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static EasyEngine.DemoKit;

namespace EasyEngine
{
    /// <summary>
    /// Builds the "EasyEngine Tests" project: one scene per mechanic, each testable in isolation, with an
    /// on-screen checklist that ticks itself and the F1 console open from the start.
    /// First scenario: "Axe Test". Everything is made with normal engine behaviors (nothing hard-coded).
    /// </summary>
    public static partial class TestScenarios
    {
        public const string ProjectName = "EasyEngine Tests";

        public static string Build(string parentFolder)
        {
            // always rebuilt from scratch (also drops any save.json that would leak old state into a test)
            var dir = Path.Combine(parentFolder, ProjectName);
            TryDeleteDir(dir);

            var project = ProjectIO.CreateNew(ProjectName, parentFolder);
            project.Scenes.Clear();
            project.ConsoleOnStart = true;
            project.Scenes.Add(AxeTest(project));
            project.Scenes.Add(LightingTest());
            project.Scenes.Add(VariableTypesTest()); // TestScenarios.VarTypes.cs
            project.Scenes.Add(HitboxTest(project)); // TestScenarios.Hitbox.cs
            project.Scenes.Add(TilesTest(project));  // TestScenarios.Tiles.cs
            project.StartScene = "Axe Test";
            ProjectIO.Save(project);
            return project.FilePath!;
        }

        // ============================================================ AXE TEST
        private static Scene AxeTest(Project p)
        {
            var (swing, chop, fall, denied) = GameAssets.SaveToolSounds(p);
            var tree = GameAssets.TreeSprite(p);
            var rock = GameAssets.RockSprite(p);

            var scene = new Scene { Name = "Axe Test", Width = 1600, Height = 600, BackgroundColor = "#8EC5F0", AmbientLight = 1 };
            // reset on every load (R restarts the scene): tools owned, resources and counters at zero
            foreach (var t in new[] { "pickaxe", "axe", "sword" }) scene.Variables["item:" + t] = 1;
            foreach (var v in new[] { "item:wood", "item:stone", "stat:swings", "stat:hits", "stat:felled", "stat:denied", "stat:missed", "stat:enemyHits" }) scene.Variables[v] = 0;

            var bg = new Layer { Name = "Background", Kind = LayerKind.Background };
            var baseL = new Layer { Name = "Base", Kind = LayerKind.Base };
            var ui = new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true };
            int id = 1;
            GameObjectData Id(GameObjectData o) { o.Id = id++; return o; }

            // sky (explicit, so this test doesn't depend on the background colour — that's the lighting test)
            bg.Objects.Add(Id(Box("Sky", 0, 0, 1600, 600, "#8EC5F0")));

            // ground
            var ground = Box("Ground", 0, 500, 1600, 100, "#6B4A2E");
            ground.Physics.Mode = PhysicsMode.Static; ground.Behaviors.Add(B(BehaviorKind.Solid));
            baseL.Objects.Add(Id(ground));
            baseL.Objects.Add(Id(Box("Grass", 0, 494, 1600, 8, "#5BA85C")));

            // trees (before the player, so the player and the tool draw on top)
            foreach (var (name, x) in new[] { ("Tree1", 420.0), ("Tree2", 760.0), ("Tree3", 1100.0) })
            {
                baseL.Objects.Add(Id(Tree(name, x, 500 - 192, 96, 192, tree, chop, fall)));
            }
            // a rock that only the pickaxe can break (proves the axe is refused)
            var r = Box("Rock", 1380, 500 - 48, 64, 48, "#00000000");
            r.SpritePath = rock;
            var rh = B(BehaviorKind.Harvestable);
            rh.Texts["tool"] = "pickaxe"; rh.Texts["drop"] = "stone"; rh.Nums["hits"] = 3; rh.Nums["amount"] = 3;
            rh.Texts["hitSound"] = chop; rh.Texts["fellSound"] = fall;
            r.Behaviors.Add(rh);
            baseL.Objects.Add(Id(r));

            // a training dummy for the sword (anything with Health is hit by items); it never dies
            var dummy = Box("Dummy", 230, 500 - 60, 32, 60, "#C8A06A"); dummy.CornerRadius = 6;
            var dh = B(BehaviorKind.Health); dh.Nums["max"] = 99; dh.Nums["respawn"] = 1; dh.Nums["invincible"] = 0.3; dh.Texts["var"] = "dummyHp";
            dummy.Behaviors.Add(dh);
            baseL.Objects.Add(Id(dummy));

            // the player: hotbar (starts on the pickaxe, so picking the axe is part of the test) + tool use
            var pl = new GameObjectData { Name = "Player", Type = "Player", X = 100, Y = 440, Width = 34, Height = 52, Color = "#3BC97A", Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            var plat = B(BehaviorKind.Platformer); plat.Nums["speed"] = 240; plat.Nums["jump"] = 620; pl.Behaviors.Add(plat);
            pl.Behaviors.Add(B(BehaviorKind.FollowCamera));
            var hot = B(BehaviorKind.Hotbar); hot.Texts["items"] = "pickaxe:#8D949E,axe:#B9C2CC,sword:#DDEBFF"; hot.Texts["player"] = "Player"; hot.Nums["slot"] = 0;
            pl.Behaviors.Add(hot);
            var tool = B(BehaviorKind.ToolUse); tool.Nums["reach"] = 80; tool.Texts["swingSound"] = swing; tool.Texts["deniedSound"] = denied;
            pl.Behaviors.Add(tool);
            baseL.Objects.Add(Id(pl));

            // R restarts the scene (resets every counter)
            var restart = Box("Restart", 0, 0, 4, 4, "#00000000");
            restart.Behaviors.Add(Script("[{\"when\":\"keypress\",\"key\":\"R\",\"do\":\"goscene\",\"scene\":\"Axe Test\"}]"));
            baseL.Objects.Add(Id(restart));

            // HUD: instructions + live counters (left), checklist (right)
            var panel = Box("HelpPanel", 12, 62, 600, 104, "#B0101018"); panel.CornerRadius = 8;
            ui.Objects.Add(Id(panel));
            ui.Objects.Add(Id(Label("Help1", 24, 68, "AXE TEST   1) Pick the axe: mouse wheel, key 2, or click its slot", "#FFFFFF", 14)));
            ui.Objects.Add(Id(Label("Help2", 24, 90, "2) Click ANYWHERE to swing   3) Walk next to a tree and click: 4 hits fell it", "#FFFFFF", 13)));
            ui.Objects.Add(Id(Label("Help3", 24, 110, "4) Axe on the grey rock (should refuse)  ·  5) Sword (key 3) on the dummy  ·  F1 console  ·  R restart", "#FFFFFF", 13)));
            ui.Objects.Add(Id(Label("Counters", 24, 136, "Wood {item:wood}  ·  Stone {item:stone}  ·  Swings {stat:swings}  ·  Hits {stat:hits}  ·  Felled {stat:felled}  ·  Refused {stat:denied}", "#FFD86B", 13)));

            var checks = Box("Checks", 0, 0, 4, 4, "#00000000");
            var cl = B(BehaviorKind.Checklist);
            cl.Texts["title"] = "Axe test";
            cl.Texts["items"] = string.Join("\n",
                "Pick the axe => holding:axe >= 1",
                "Swing anywhere (click) => stat:swings >= 1",
                "Swing far from trees (miss) => stat:missed >= 1",
                "Hit a tree => stat:hits >= 1",
                "Fell a tree (4 hits) => stat:felled >= 1",
                "Wood in the bag (5+) => item:wood >= 5",
                "The rock refuses the axe => stat:denied >= 1",
                "Bonus: the pickaxe breaks the rock => item:stone >= 3",
                "Bonus: the sword hits the dummy => stat:enemyHits >= 1");
            cl.Nums["width"] = 310;
            checks.Behaviors.Add(cl);
            ui.Objects.Add(Id(checks));

            scene.Layers.Add(bg); scene.Layers.Add(baseL); scene.Layers.Add(ui);
            return scene;
        }

        // ============================================================ LIGHTING TEST
        // Day/night + lights. NO sky rectangle on purpose: the blue comes from the scene's background colour.
        private static Scene LightingTest()
        {
            var scene = new Scene { Name = "Lighting Test", Width = 1400, Height = 600, BackgroundColor = "#8EC5F0", AmbientLight = 1 };
            foreach (var v in new[] { "dn:nights", "dn:days", "confirmLook" }) scene.Variables[v] = 0;
            var baseL = new Layer { Name = "Base", Kind = LayerKind.Base };
            var ui = new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true };
            int id = 1;
            GameObjectData Id(GameObjectData o) { o.Id = id++; return o; }

            var ground = Box("Ground", 0, 500, 1400, 100, "#6B4A2E");
            ground.Physics.Mode = PhysicsMode.Static; ground.Behaviors.Add(B(BehaviorKind.Solid));
            baseL.Objects.Add(Id(ground));
            baseL.Objects.Add(Id(Box("Grass", 0, 494, 1400, 8, "#5BA85C")));

            // a lamp post: its light only matters at night
            baseL.Objects.Add(Id(Box("LampPole", 716, 380, 8, 120, "#3A3A46")));
            var lamp = Box("Lamp", 704, 362, 32, 22, "#FFE7A0"); lamp.CornerRadius = 6;
            var ll = B(BehaviorKind.LightSource); ll.Nums["radius"] = 210; ll.Nums["intensity"] = 0.95; ll.Texts["color"] = "#FFD27A";
            lamp.Behaviors.Add(ll);
            baseL.Objects.Add(Id(lamp));

            // the player carries a torch
            var pl = new GameObjectData { Name = "Player", Type = "Player", X = 200, Y = 440, Width = 34, Height = 52, Color = "#3BC97A", Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            var plat = B(BehaviorKind.Platformer); plat.Nums["speed"] = 240; plat.Nums["jump"] = 620; pl.Behaviors.Add(plat);
            pl.Behaviors.Add(B(BehaviorKind.FollowCamera));
            var torch = B(BehaviorKind.LightSource); torch.Nums["radius"] = 170; torch.Nums["intensity"] = 0.95; torch.Texts["color"] = "#FFA040";
            pl.Behaviors.Add(torch);
            baseL.Objects.Add(Id(pl));

            // the cycle (short, so a whole day+night fits in ~35 s) + keys to jump around
            var dn = Box("DayNight", 0, 0, 4, 4, "#00000000");
            var dnB = B(BehaviorKind.DayNight);
            dnB.Nums["dayLength"] = 20; dnB.Nums["nightLength"] = 15; dnB.Nums["transition"] = 3; dnB.Nums["nightLight"] = 0.12; dnB.Nums["arcHeight"] = 150;
            dnB.Texts["sun"] = "Sun"; dnB.Texts["moon"] = "Moon";
            dn.Behaviors.Add(dnB);
            dn.Behaviors.Add(Script("[{\"when\":\"keypress\",\"key\":\"N\",\"do\":\"settime\",\"time\":\"night\"}," +
                                    "{\"when\":\"keypress\",\"key\":\"M\",\"do\":\"settime\",\"time\":\"day\"}," +
                                    "{\"when\":\"keypress\",\"key\":\"Y\",\"do\":\"setvar\",\"var\":\"confirmLook\",\"to\":1}," +
                                    "{\"when\":\"keypress\",\"key\":\"R\",\"do\":\"goscene\",\"scene\":\"Lighting Test\"}]"));
            baseL.Objects.Add(Id(dn));

            var panel = Box("HelpPanel", 12, 62, 600, 104, "#B0101018"); panel.CornerRadius = 8;
            ui.Objects.Add(Id(panel));
            ui.Objects.Add(Id(Label("Help1", 24, 68, "LIGHTING TEST   Day ~20 s, night ~15 s (or press N = night, M = morning)", "#FFFFFF", 14)));
            ui.Objects.Add(Id(Label("Help2", 24, 90, "By day: blue sky, no glow on you.  At night: dark-blue sky + the moon", "#FFFFFF", 13)));
            ui.Objects.Add(Id(Label("Help3", 24, 110, "+ soft light around you and the lamp.  Looks right? Y  ·  F1 console  ·  R restart", "#FFFFFF", 13)));
            ui.Objects.Add(Id(Label("Counters", 24, 136, "Nights {dn:nights}  ·  Days {dn:days}  ·  Night now {isNight}", "#FFD86B", 13)));

            var checks = Box("Checks", 0, 0, 4, 4, "#00000000");
            var cl = B(BehaviorKind.Checklist);
            cl.Texts["title"] = "Lighting test";
            cl.Texts["items"] = string.Join("\n",
                "Starts in full daylight => isDay >= 1 && dayness >= 0.99",
                "Night falls (wait, or press N) => dn:nights >= 1",
                "Day comes back (wait, or press M) => dn:days >= 1",
                "It all looks right — press Y => confirmLook >= 1");
            cl.Nums["width"] = 320;
            checks.Behaviors.Add(cl);
            ui.Objects.Add(Id(checks));

            scene.Layers.Add(new Layer { Name = "Background", Kind = LayerKind.Background });
            scene.Layers.Add(baseL); scene.Layers.Add(ui);
            SkySetup.Ensure(scene); // same as adding Day/Night in the editor: Sky layer at the bottom with the Sun and Moon
            return scene;
        }

        // ============================================================ helpers
        private static GameObjectData Label(string name, double x, double y, string text, string color, double size)
            => DemoKit.Label(name, x, y, text, color, size, pad: 8); // a little tighter than the demos' HUD lines

        // OneDrive can briefly lock files: retry, then fail with a clear message.
        internal static void TryDeleteDir(string dir)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (!Directory.Exists(dir)) return;
                try { Directory.Delete(dir, true); return; }
                catch when (attempt < 2) { Thread.Sleep(400); }
                catch (Exception ex) { throw new IOException($"Couldn't replace the old '{ProjectName}' folder (is a file open or OneDrive syncing?): {ex.Message}", ex); }
            }
        }
    }

    /// <summary>Generated art + sounds shared by the test scenarios and the demo games (tree, rock, tool sounds).</summary>
    public static class GameAssets
    {
        public static (string swing, string chop, string fall, string denied) SaveToolSounds(Project p) => (
            ProjectIO.SaveSound(p, SoundSynth.Whoosh(), "swing"),
            ProjectIO.SaveSound(p, SoundSynth.Chop(), "chop"),
            ProjectIO.SaveSound(p, SoundSynth.TreeFall(), "fall"),
            ProjectIO.SaveSound(p, SoundSynth.Denied(), "denied"));

        public static string TreeSprite(Project p) => ProjectIO.SaveSprite(p, Draw(96, 192, DrawTree), "tree");
        public static string RockSprite(Project p) => ProjectIO.SaveSprite(p, Draw(64, 48, DrawRock), "rock");

        public static BitmapSource Draw(int w, int h, Action<DrawingContext, int, int> draw)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) draw(dc, w, h);
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        public static SolidColorBrush Br(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

        private static void DrawTree(DrawingContext dc, int w, int h)
        {
            // trunk
            dc.DrawRoundedRectangle(Br("#7A5230"), new Pen(Br("#4A311C"), 2), new Rect(w / 2.0 - 11, h * 0.45, 22, h * 0.55), 4, 4);
            dc.DrawLine(new Pen(Br("#5E3E22"), 2), new Point(w / 2.0 - 3, h * 0.55), new Point(w / 2.0 - 3, h * 0.92));
            // canopy: a few overlapping circles
            var leaf = Br("#3C8C3C"); var leafDark = Br("#2E6E2E"); var edge = new Pen(Br("#245A24"), 2);
            dc.DrawEllipse(leafDark, edge, new Point(w * 0.30, h * 0.34), w * 0.26, h * 0.13);
            dc.DrawEllipse(leafDark, edge, new Point(w * 0.70, h * 0.34), w * 0.26, h * 0.13);
            dc.DrawEllipse(leaf, edge, new Point(w * 0.50, h * 0.22), w * 0.34, h * 0.17);
            dc.DrawEllipse(Br("#56A856"), null, new Point(w * 0.42, h * 0.17), w * 0.10, h * 0.05);
        }

        private static void DrawRock(DrawingContext dc, int w, int h)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(4, h - 2), true, true);
                c.LineTo(new Point(8, h * 0.45), true, false);
                c.LineTo(new Point(w * 0.35, 6), true, false);
                c.LineTo(new Point(w * 0.70, 4), true, false);
                c.LineTo(new Point(w - 5, h * 0.40), true, false);
                c.LineTo(new Point(w - 3, h - 2), true, false);
            }
            dc.DrawGeometry(Br("#8A8F99"), new Pen(Br("#5A5F69"), 2), g);
            dc.DrawLine(new Pen(Br("#A9AEB8"), 2), new Point(w * 0.35, 12), new Point(w * 0.55, h * 0.45));
        }
    }
}
