using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using static EasyEngine.DemoKit;
using static EasyEngine.GameAssets;

namespace EasyEngine
{
    /// <summary>
    /// Builds the "Boss Demo" project: a swamp arena, an intro cutscene (cinema bars, the camera visits the boss, its
    /// title, a few lines), THE SLIME KING (Boss AI, 3 phases: charge + bullet ring → jump slams + aimed fireballs →
    /// a furious combo with minions), its death sequence, and a victory cutscene that opens the gate. The player has a
    /// sword (click near the boss to swing).
    /// </summary>
    public static class BossDemo
    {
        public const string ProjectName = "Boss Demo";
        private const double GroundY = 640;

        public static string Build(string parentFolder)
        {
            var project = ProjectIO.CreateNew(ProjectName, parentFolder);
            project.Scenes.Clear();
            project.StartScene = "Arena";
            project.Variables["bossAwake"] = 0;
            project.Variables["bossDefeated"] = 0;
            project.Variables["gateOpen"] = 0;

            var scene = new Scene { Name = "Arena", Width = 1900, Height = 720, BackgroundColor = "#2B2340" };
            var bg = new Layer { Name = "Background", Kind = LayerKind.Background };
            var baseL = new Layer { Name = "Base", Kind = LayerKind.Base };
            var ui = new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true };
            int id = 1;
            GameObjectData Id(GameObjectData o) { o.Id = id++; return o; }
            var snd = GameAssets.SaveToolSounds(project);

            // ---- backdrop: a pale moon, dark pillars, hanging vines ----
            bg.Objects.Add(Id(Rounded(Box("Moon", 1250, 70, 110, 110, "#E8E2C8"), 55)));
            foreach (var (n, x) in new[] { ("Pillar1", 220.0), ("Pillar2", 700), ("Pillar3", 1150) })
                bg.Objects.Add(Id(Box(n, x, 120, 70, GroundY - 120, "#3A3052")));
            foreach (var (n, x, h) in new[] { ("Vine1", 420.0, 160.0), ("Vine2", 980, 220), ("Vine3", 1420, 130) })
                bg.Objects.Add(Id(Box(n, x, 0, 8, h, "#3F6B3A")));

            // ---- the arena: ground, walls, three jump-through ledges, the exit gate ----
            baseL.Objects.Add(Id(Solid("Ground", 0, GroundY, scene.Width, 80, "#3E3550")));
            baseL.Objects.Add(Id(Solid("WallL", 0, 0, 40, GroundY, "#4A3F60")));
            baseL.Objects.Add(Id(Solid("WallR", 1860, 0, 40, GroundY, "#4A3F60")));
            foreach (var (n, x, y) in new[] { ("Ledge1", 300.0, 525.0), ("Ledge2", 740, 420), ("Ledge3", 1180, 525) })
            {
                var p = Solid(n, x, y, 180, 18, "#6B5A80");
                p.Behaviors.First(b => b.Kind == BehaviorKind.Solid).Nums["oneWay"] = 1;
                baseL.Objects.Add(Id(p));
            }
            var gate = Box("Gate", 1560, 0, 40, GroundY, "#7A5A3A");
            gate.Physics.Mode = PhysicsMode.Static; gate.CornerRadius = 0;
            var gb = B(BehaviorKind.Gate); gb.Texts["var"] = "gateOpen"; gb.Texts["op"] = ">="; gb.Nums["value"] = 1;
            gate.Behaviors.Add(gb);
            baseL.Objects.Add(Id(gate));

            // ---- behind the gate: the crown and the end ----
            var crown = Rounded(Box("Crown", 1720, GroundY - 40, 36, 30, "#FFD24D"), 6);
            crown.Behaviors.Add(B(BehaviorKind.Collectible));
            crown.Behaviors.Add(Script("[{\"when\":\"touch\",\"name\":\"Player\",\"do\":\"say\",\"text\":\"You took the Slime King's crown. THE END — thanks for playing!\"}]"));
            baseL.Objects.Add(Id(crown));
            baseL.Objects.Add(Id(Label("EndSign", 1640, GroundY - 120, "★ The swamp is free ★", "#FFE9A8", 16, 220)));

            // ---- the knight (with a checkpoint at the entrance) ----
            var flag = Box("Checkpoint", 90, GroundY - 50, 26, 50, "#FFD24D");
            flag.Behaviors.Add(B(BehaviorKind.Checkpoint));
            baseL.Objects.Add(Id(flag));
            baseL.Objects.Add(Id(Player(150, GroundY - 50, snd)));

            // ---- THE SLIME KING ----
            baseL.Objects.Add(Id(Boss(project, 1300, GroundY - 96)));

            // ---- cutscenes (invisible markers) ----
            baseL.Objects.Add(Id(Cutscene("Intro", "start", "", IntroSteps())));
            baseL.Objects.Add(Id(Cutscene("Victory", "var", "bossDefeated", VictorySteps())));

            // ---- HUD ----
            ui.Objects.Add(Id(Label("HpLabel", 16, 62, "HP", "#FF8A8A", 14, 40)));
            var hpBar = Box("HpBar", 46, 64, 180, 16, "#00000000");
            var barB = B(BehaviorKind.Bar); barB.Texts["var"] = "hp"; barB.Nums["max"] = 5; barB.Texts["color"] = "#E5484D";
            hpBar.Behaviors.Add(barB);
            ui.Objects.Add(Id(hpBar));
            ui.Objects.Add(Id(Label("Hint", 16, 116, "A/D move · Space jump · click near the boss to swing your sword · Enter: next line · Esc: skip a cutscene · watch for the glow before each attack!", "#D8DEEA", 13, 900)));

            scene.Layers.Add(bg); scene.Layers.Add(baseL); scene.Layers.Add(ui);
            project.Scenes.Add(scene);

            // ---- what the boss spawns ----
            project.Prefabs.Add(new PrefabDef { Name = "Fireball", Category = "Boss", Template = Fireball() });
            project.Prefabs.Add(new PrefabDef { Name = "Minion", Category = "Boss", Template = Minion(project) });

            ProjectIO.Save(project);
            return project.FilePath!;
        }

        // ================================================================== the scripts (built from the step palette)
        private static SeqStep S(SeqMode mode, string id, params (string key, object value)[] ps)
        {
            var step = StepListControl.NewStep(StepCatalog.Find(mode, id) ?? throw new ArgumentException("unknown step " + id));
            foreach (var (k, v) in ps) step.P[k] = Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
            return step;
        }
        private static SeqStep C(string id, params (string, object)[] ps) => S(SeqMode.Cutscene, id, ps);
        private static SeqStep K(string id, params (string, object)[] ps) => S(SeqMode.Boss, id, ps);

        private static List<SeqStep> IntroSteps() => new()
        {
            C("wait", ("time", 0.6)),
            C("camera", ("object", "SlimeKing"), ("time", 1.4)),
            C("title", ("text", "THE SLIME KING"), ("time", 2.2)),
            C("shake", ("strength", 6), ("time", 0.6)),
            C("say", ("text", "Who dares to enter my swamp?"), ("speaker", "Slime King")),
            C("say", ("text", "I will squish you flat, little knight!"), ("speaker", "Slime King")),
            C("cameraback", ("time", 1.2)),
            C("say", ("text", "Not today, your Sliminess. (Click near it to swing my sword!)"), ("speaker", "Knight")),
            C("unlock"),
            C("setvar", ("var", "bossAwake"), ("to", 1)),
        };

        private static List<SeqStep> VictorySteps() => new()
        {
            C("wait", ("time", 0.4)),
            C("title", ("text", "VICTORY!"), ("time", 2.4)),
            C("camera", ("object", "Gate"), ("time", 1)),
            C("setvar", ("var", "gateOpen"), ("to", 1)),
            C("emit", ("at", "Gate"), ("count", 40), ("color", "#FFE9A8")),
            C("shake", ("strength", 5), ("time", 0.5)),
            C("wait", ("time", 0.8)),
            C("cameraback", ("time", 1)),
            C("say", ("text", "The gate is open. The crown is waiting!"), ("speaker", "Knight")),
        };

        public static string BossScriptJson()
        {
            var b = new BossScript();
            void Attack(string name, params SeqStep[] steps) => b.Attacks.Add(new BossAttack { Name = name, Steps = steps.ToList() });
            Attack("Charge", K("telegraph", ("time", 0.7), ("color", "#FF4040")), K("dash", ("speed", 640), ("time", 0.75)), K("wait", ("time", 0.2)));
            Attack("Ring", K("telegraph", ("time", 0.5), ("color", "#FFB020")), K("shoot", ("pattern", "ring"), ("count", 12), ("speed", 260), ("life", 3.2)));
            Attack("Jump slam", K("telegraph", ("time", 0.4), ("color", "#7CFC9A")), K("jump", ("height", 260), ("toward", 1), ("shockwave", 1)));
            Attack("Fireballs", K("telegraph", ("time", 0.4), ("color", "#FF8040")),
                   K("shoot", ("pattern", "aimed"), ("count", 5), ("spread", 50), ("speed", 360), ("bullet", "Fireball"), ("volleys", 2), ("every", 0.45)));
            Attack("Fury", K("telegraph", ("time", 0.35), ("color", "#FF2020")), K("dash", ("speed", 780), ("time", 0.5)),
                   K("jump", ("height", 220), ("toward", 1), ("shockwave", 1)),
                   K("shoot", ("pattern", "ring"), ("count", 14), ("speed", 300), ("volleys", 2), ("every", 0.35), ("turn", 13)));

            b.Phases.Add(new BossPhase { Name = "Phase 1", When = "start", Attacks = { "Charge", "Ring" }, Order = "random", Pause = 1.1 });
            b.Phases.Add(new BossPhase { Name = "Phase 2", When = "health", Value = 60, Attacks = { "Charge", "Ring", "Jump slam", "Fireballs" }, Order = "random", Pause = 0.75,
                Enter = { K("telegraph", ("time", 0.6), ("color", "#FFB020")), K("shake", ("strength", 8), ("time", 0.6)) } });
            b.Phases.Add(new BossPhase { Name = "Phase 3", When = "health", Value = 25, Attacks = { "Fury", "Jump slam", "Fireballs", "Charge" }, Order = "random", Pause = 0.45,
                Enter = { K("invulnerable", ("time", 1.5)), K("telegraph", ("time", 1), ("color", "#FF2020")), K("summon", ("object", "Minion"), ("count", 2), ("radius", 160)), K("shake", ("strength", 10), ("time", 0.5)) } });

            b.Death.AddRange(new[]
            {
                K("shake", ("strength", 14), ("time", 1.2)),
                K("emit", ("at", "self"), ("count", 50), ("color", "#7CFC9A")),
                K("wait", ("time", 0.4)),
                K("emit", ("at", "self"), ("count", 60), ("color", "#FFFFFF")),
                K("wait", ("time", 0.4)),
                K("say", ("text", "Noooo… my beautiful swamp…"), ("speaker", "Slime King")),
                K("emit", ("at", "self"), ("count", 80), ("color", "#7CFC9A")),
                K("setvar", ("var", "bossDefeated"), ("to", 1)),
            });
            return SequenceJson.Serialize(b);
        }

        // ================================================================== objects
        private static GameObjectData Solid(string name, double x, double y, double w, double h, string color)
        {
            var o = Box(name, x, y, w, h, color);
            o.Physics.Mode = PhysicsMode.Static; o.CornerRadius = 0;
            o.Behaviors.Add(B(BehaviorKind.Solid));
            return o;
        }

        private static GameObjectData Cutscene(string name, string trigger, string var, List<SeqStep> steps)
        {
            var o = Box(name, 0, 0, 10, 10, "#00000000");
            var c = B(BehaviorKind.Cutscene);
            c.Texts["trigger"] = trigger; c.Texts["var"] = var;
            c.Texts["script"] = SequenceJson.Serialize(new CutsceneScript { Steps = steps });
            o.Behaviors.Add(c);
            return o;
        }

        private static GameObjectData Player(double x, double y, (string swing, string chop, string fall, string denied) snd)
        {
            var o = new GameObjectData { Name = "Player", Type = "Player", X = x, Y = y, Width = 34, Height = 50, Color = "#3BC97A", Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            var plat = B(BehaviorKind.Platformer); plat.Nums["speed"] = 280; plat.Nums["jump"] = 720; o.Behaviors.Add(plat); // jumps higher than the boss (96 px)
            o.Behaviors.Add(B(BehaviorKind.FollowCamera));
            var hp = B(BehaviorKind.Health); hp.Nums["max"] = 5; hp.Nums["respawn"] = 1; hp.Nums["invincible"] = 1; hp.Texts["var"] = "hp"; o.Behaviors.Add(hp);
            var tool = B(BehaviorKind.ToolUse);
            tool.Texts["tool"] = "sword"; tool.Texts["itemDamage"] = "sword=2"; tool.Nums["damage"] = 2;
            tool.Nums["reach"] = 90; tool.Nums["cooldown"] = 0.3; tool.Nums["knockback"] = 200;
            tool.Texts["swingSound"] = snd.swing; tool.Texts["hitSound"] = snd.chop;
            o.Behaviors.Add(tool);
            return o;
        }

        private static GameObjectData Boss(Project project, double x, double y)
        {
            var o = new GameObjectData { Name = "SlimeKing", Type = "Enemy", X = x, Y = y, Width = 120, Height = 96, Color = "#4CC26A", Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            o.SpritePath = ProjectIO.SaveSprite(project, Draw(120, 96, DrawSlimeKing), "slime_king");
            var hp = B(BehaviorKind.Health); hp.Nums["max"] = 36; hp.Nums["respawn"] = 0; hp.Nums["invincible"] = 0.5; hp.Texts["var"] = "bossHP"; o.Behaviors.Add(hp);
            var dmg = B(BehaviorKind.Damager); dmg.Texts["target"] = "Player"; dmg.Nums["damage"] = 1; dmg.Nums["knockback"] = 380; o.Behaviors.Add(dmg);
            var ai = B(BehaviorKind.BossAI);
            ai.Texts["moves"] = "ground"; ai.Texts["starts"] = "var"; ai.Texts["var"] = "bossAwake"; ai.Texts["target"] = "Player";
            ai.Nums["heavy"] = 1; ai.Nums["hpbar"] = 1; ai.Texts["title"] = "THE SLIME KING";
            ai.Texts["script"] = BossScriptJson();
            o.Behaviors.Add(ai);
            // the Custom 'death' rule runs after the death sequence: one last splash
            o.Behaviors.Add(Script("[{\"when\":\"death\",\"do\":\"emit\",\"count\":40,\"color\":\"#B6FFC8\"}]"));
            return o;
        }

        private static GameObjectData Fireball()
        {
            var o = new GameObjectData { Name = "Fireball", Type = "Bullet", Width = 20, Height = 20, Color = "#FF7A2E", BorderColor = "#FFE08A", BorderWidth = 3, CornerRadius = 10 };
            var d = B(BehaviorKind.Damager); d.Texts["target"] = "Player"; d.Nums["damage"] = 1; d.Nums["knockback"] = 260; d.Nums["vanish"] = 1;
            o.Behaviors.Add(d);
            return o;
        }

        private static GameObjectData Minion(Project project)
        {
            var o = new GameObjectData { Name = "Minion", Type = "Enemy", Width = 36, Height = 28, Color = "#5FD47E", Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            o.SpritePath = ProjectIO.SaveSprite(project, Draw(36, 28, DrawMinion), "minion");
            var plat = B(BehaviorKind.Platformer); plat.Nums["controls"] = 0; o.Behaviors.Add(plat);
            var dmg = B(BehaviorKind.Damager); dmg.Texts["target"] = "Player"; dmg.Nums["damage"] = 1; dmg.Nums["knockback"] = 260; o.Behaviors.Add(dmg);
            var hp = B(BehaviorKind.Health); hp.Nums["max"] = 2; hp.Nums["respawn"] = 0; hp.Texts["var"] = "minionHP"; o.Behaviors.Add(hp);
            o.Behaviors.Add(Script("[{\"when\":\"always\",\"do\":\"set\",\"prop\":\"vx\",\"expr\":\"sign(Player.cx - self.cx) * 95\"}," +
                                   "{\"when\":\"death\",\"do\":\"emit\",\"count\":16,\"color\":\"#7CFC9A\"}]"));
            return o;
        }

        // ================================================================== art
        // A slime blob: a dome on a flat base with a shine.
        private static void Slime(DrawingContext dc, double w, double h, double top, string body, string edge)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(2, h - 2), true, true);
                c.BezierTo(new Point(0, top + (h - top) * 0.2), new Point(w * 0.2, top), new Point(w / 2, top), true, false);
                c.BezierTo(new Point(w * 0.8, top), new Point(w, top + (h - top) * 0.2), new Point(w - 2, h - 2), true, false);
            }
            dc.DrawGeometry(Br(body), new Pen(Br(edge), 2.5), g);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), null, new Point(w * 0.32, top + (h - top) * 0.3), w * 0.09, (h - top) * 0.08);
        }

        private static void DrawSlimeKing(DrawingContext dc, int w, int h)
        {
            Slime(dc, w, h, 26, "#4CC26A", "#24753A");
            foreach (double ex in new[] { 0.38, 0.62 })
            {
                dc.DrawEllipse(Brushes.White, new Pen(Br("#1C3A22"), 1.5), new Point(w * ex, h * 0.6), 9, 11);
                dc.DrawEllipse(Br("#1C1C22"), null, new Point(w * ex + 2, h * 0.62), 4.5, 6);
            }
            dc.DrawLine(new Pen(Br("#1C3A22"), 3), new Point(w * 0.27, h * 0.45), new Point(w * 0.43, h * 0.5)); // angry brows
            dc.DrawLine(new Pen(Br("#1C3A22"), 3), new Point(w * 0.73, h * 0.45), new Point(w * 0.57, h * 0.5));
            var crown = new StreamGeometry();
            using (var c = crown.Open())
            {
                double cx = w / 2.0, y0 = 30, y1 = 6;
                c.BeginFigure(new Point(cx - 24, y0), true, true);
                foreach (var (x, y) in new[] { (cx - 26, y1 + 6), (cx - 12, y0 - 10), (cx, y1), (cx + 12, y0 - 10), (cx + 26, y1 + 6), (cx + 24, y0) })
                    c.LineTo(new Point(x, y), true, false);
            }
            dc.DrawGeometry(Br("#FFD24D"), new Pen(Br("#A87A10"), 2), crown);
            dc.DrawEllipse(Br("#E5484D"), null, new Point(w / 2.0, 22), 3.5, 3.5);
        }

        private static void DrawMinion(DrawingContext dc, int w, int h)
        {
            Slime(dc, w, h, 4, "#5FD47E", "#2E8A48");
            dc.DrawEllipse(Br("#1C1C22"), null, new Point(w * 0.36, h * 0.55), 2.5, 3.5);
            dc.DrawEllipse(Br("#1C1C22"), null, new Point(w * 0.64, h * 0.55), 2.5, 3.5);
        }
    }
}
