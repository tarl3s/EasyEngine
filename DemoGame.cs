using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static EasyEngine.DemoKit;
using static EasyEngine.GameAssets;

namespace EasyEngine
{
    /// <summary>Builds a complete 5-level platformer demo (with drawn sprites) so the engine can be tested end-to-end.</summary>
    public static class DemoGame
    {
        private class Sprites
        {
            public List<string> HeroIdle = new(), HeroWalk = new(), Enemy = new(), Coin = new();
            public string Tile = "", Spike = "", Shop = "", Goal = "", Heart = "";
        }

        public static string Build(string parentFolder)
        {
            var project = ProjectIO.CreateNew("Platformer Demo", parentFolder);
            var dir = Path.Combine(project.FolderPath!, "Assets", "sprites");
            Directory.CreateDirectory(dir);
            var s = GenerateSprites(dir);

            project.Scenes.Clear();
            string[] names = { "Level 1", "Level 2", "Level 3", "Level 4", "Level 5" };
            for (int i = 0; i < names.Length; i++)
            {
                string next = i < names.Length - 1 ? names[i + 1] : "You Win";
                project.Scenes.Add(BuildLevel(i + 1, names[i], next, s));
            }
            project.Scenes.Add(BuildWinScene());
            project.StartScene = "Level 1";
            project.Variables["score"] = 0;
            project.Variables["coins"] = 0;

            ProjectIO.Save(project);
            return project.FilePath!;
        }

        // ============================================================
        //  LEVELS
        // ============================================================
        private static Scene BuildLevel(int n, string name, string next, Sprites s)
        {
            string[] skies = { "#1B2A4A", "#2A1B3A", "#102A20", "#2A2410", "#301A1A" };
            var scene = new Scene { Name = name, Width = 4400, Height = 540, BackgroundColor = skies[(n - 1) % skies.Length] };
            var bg = new Layer { Name = "Background", Kind = LayerKind.Background };
            var baseL = new Layer { Name = "Base", Kind = LayerKind.Base };
            var ui = new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true };
            int id = 1;
            GameObjectData Id(GameObjectData o) { o.Id = id++; return o; }

            double groundY = 480, gH = 60;
            // ground segments with a few pits
            (double x, double w)[] grounds = { (0, 1000), (1200, 1000), (2400, 900), (3500, 900) };
            foreach (var g in grounds) baseL.Objects.Add(Id(Solid("Ground", g.x, groundY, g.w, gH, s.Tile)));

            // player
            var player = Id(Player(90, 360, s));
            baseL.Objects.Add(player);

            // shop (levels 1 and 3) — buy a speed boost with B (logic is on the player)
            if (n == 1 || n == 3)
                baseL.Objects.Add(Id(Shopkeeper(360, groundY - 64, s)));

            // floating platforms + coins + enemies spread across the level
            var rng = new Random(1000 + n);
            double x = 650;
            int enemyCount = 3 + n;
            int placedEnemies = 0;
            while (x < scene.Width - 500)
            {
                double py = groundY - (120 + rng.Next(0, 4) * 50);
                double pw = 160 + rng.Next(0, 3) * 40;
                bool moving = n >= 2 && rng.NextDouble() < 0.35;
                if (moving) baseL.Objects.Add(Id(MovingPlatform(x, py, pw, s)));
                else baseL.Objects.Add(Id(Solid("Platform", x, py, pw, 24, s.Tile)));

                // coins arc over the platform
                for (int c = 0; c < 3; c++) baseL.Objects.Add(Id(Coin(x + 20 + c * 45, py - 55, s)));

                // enemy on the platform sometimes
                if (placedEnemies < enemyCount && rng.NextDouble() < 0.7)
                { baseL.Objects.Add(Id(Enemy(x + pw / 2 - 20, py - 42, s))); placedEnemies++; }

                x += pw + 180 + rng.Next(0, 160);
            }

            // some ground enemies + spikes
            for (int i = 0; i < n + 1; i++)
            {
                double ex = 700 + i * 700;
                if (ex < scene.Width - 400) baseL.Objects.Add(Id(Enemy(ex, groundY - 42, s)));
            }
            for (int i = 0; i < n; i++)
            {
                double sx = 950 + i * 800;
                if (sx < scene.Width - 400) baseL.Objects.Add(Id(Spike(sx, groundY - 30, s)));
            }

            // goal at the end
            baseL.Objects.Add(Id(Goal(scene.Width - 160, groundY - 70, next, s)));

            // HUD (fixed on screen)
            ui.Objects.Add(Id(HudLabel("ScoreHUD", 16, 14, "Score: {score}", "#FFD86B")));
            ui.Objects.Add(Id(HudLabel("CoinsHUD", 230, 14, "Coins: {coins}", "#FFD86B")));
            ui.Objects.Add(Id(HudLabel("HpHUD", 440, 14, "HP: {health}", "#FF6B6B")));
            ui.Objects.Add(Id(HudLabel("Hint", 16, 500, "Arrows/A-D move · Space jump · Click attack · B buy (near shop) · Esc quit", "#A9B0C0", 14)));

            scene.Layers.Add(bg); scene.Layers.Add(baseL); scene.Layers.Add(ui);
            return scene;
        }

        private static Scene BuildWinScene()
        {
            var scene = new Scene { Name = "You Win", Width = 960, Height = 540, BackgroundColor = "#10241A" };
            var baseL = new Layer { Name = "Base", Kind = LayerKind.Base };
            var ui = new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true };
            baseL.Objects.Add(new GameObjectData { Id = 1, Name = "Ground", Type = "Object", X = 0, Y = 480, Width = 960, Height = 60, Color = "#2A3A2E", Physics = new PhysicsData { Mode = PhysicsMode.Static }, Behaviors = { B(BehaviorKind.Solid) } });
            ui.Objects.Add(HudLabel("Win", 300, 220, "YOU WIN!  Score: {score}", "#FFD86B", 40));
            ui.Objects.Add(HudLabel("Win2", 330, 300, "Thanks for playing the demo!", "#FFFFFF", 18));
            scene.Layers.Add(baseL); scene.Layers.Add(ui);
            return scene;
        }

        // ============================================================
        //  OBJECT FACTORIES
        // ============================================================
        private static GameObjectData Player(double x, double y, Sprites s)
        {
            var o = new GameObjectData { Name = "Player", Type = "Player", X = x, Y = y, Width = 36, Height = 52, Color = "#3BC97A", Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            var plat = B(BehaviorKind.Platformer); plat.Nums["speed"] = 250; plat.Nums["jump"] = 640; plat.Nums["doubleJump"] = 1; o.Behaviors.Add(plat);
            o.Behaviors.Add(B(BehaviorKind.FollowCamera));
            var hp = B(BehaviorKind.Health); hp.Nums["max"] = 3; hp.Nums["respawn"] = 1; hp.Texts["var"] = "health"; o.Behaviors.Add(hp);
            var sword = B(BehaviorKind.ToolUse); sword.Texts["tool"] = "sword"; sword.Nums["reach"] = 60; o.Behaviors.Add(sword); // click to attack
            // shop buy logic lives on the player (B = spend 15 coins for a lasting speed boost)
            o.Behaviors.Add(Script("[{\"when\":\"keypress\",\"key\":\"B\",\"cooldown\":1,\"if\":\"coins\",\"ifop\":\">=\",\"ifval\":15,\"do\":\"boost\",\"factor\":1.4}," +
                                   "{\"when\":\"keypress\",\"key\":\"B\",\"cooldown\":1,\"if\":\"coins\",\"ifop\":\">=\",\"ifval\":15,\"do\":\"addvar\",\"var\":\"coins\",\"amount\":-15}," +
                                   "{\"when\":\"keypress\",\"key\":\"B\",\"cooldown\":1,\"if\":\"coins\",\"ifop\":\">=\",\"ifval\":15,\"do\":\"say\",\"text\":\"Speed boost bought!\",\"speaker\":\"Shop\"}]"));
            o.Animations.Add(new SpriteAnimation { Name = "idle", Fps = 3, Frames = new List<string>(s.HeroIdle) });
            o.Animations.Add(new SpriteAnimation { Name = "walk", Fps = 11, Frames = new List<string>(s.HeroWalk) });
            o.DefaultAnimation = "idle";
            return o;
        }

        private static GameObjectData Enemy(double x, double y, Sprites s)
        {
            var o = new GameObjectData { Name = "Enemy", Type = "Enemy", X = x, Y = y, Width = 40, Height = 40, Color = "#E5484B", Physics = new PhysicsData { Mode = PhysicsMode.None } };
            var pat = B(BehaviorKind.Patrol); pat.Nums["speed"] = 70; pat.Nums["distance"] = 150; o.Behaviors.Add(pat);
            var dmg = B(BehaviorKind.Damager); dmg.Texts["target"] = "Player"; dmg.Nums["damage"] = 1; o.Behaviors.Add(dmg);
            var hp = B(BehaviorKind.Health); hp.Nums["max"] = 1; hp.Nums["respawn"] = 0; o.Behaviors.Add(hp);
            o.Animations.Add(new SpriteAnimation { Name = "walk", Fps = 6, Frames = new List<string>(s.Enemy) });
            o.DefaultAnimation = "walk";
            return o;
        }

        private static GameObjectData Coin(double x, double y, Sprites s)
        {
            var o = new GameObjectData { Name = "Coin", Type = "Object", X = x, Y = y, Width = 26, Height = 26, Color = "#FFD86B", Physics = new PhysicsData { Mode = PhysicsMode.None } };
            o.Behaviors.Add(Script("[{\"when\":\"touch\",\"name\":\"Player\",\"do\":\"addvar\",\"var\":\"coins\",\"amount\":1}," +
                                   "{\"when\":\"touch\",\"name\":\"Player\",\"do\":\"addvar\",\"var\":\"score\",\"amount\":10}," +
                                   "{\"when\":\"touch\",\"name\":\"Player\",\"do\":\"destroy\"}]"));
            o.Animations.Add(new SpriteAnimation { Name = "spin", Fps = 10, Frames = new List<string>(s.Coin) });
            o.DefaultAnimation = "spin";
            return o;
        }

        private static GameObjectData Spike(double x, double y, Sprites s)
        {
            var o = new GameObjectData { Name = "Spike", Type = "Object", X = x, Y = y, Width = 36, Height = 30, Color = "#9AA0B0", SpritePath = s.Spike, Physics = new PhysicsData { Mode = PhysicsMode.None } };
            var dmg = B(BehaviorKind.Damager); dmg.Texts["target"] = "Player"; dmg.Nums["damage"] = 1; dmg.Nums["knockback"] = 360; o.Behaviors.Add(dmg);
            return o;
        }

        private static GameObjectData Shopkeeper(double x, double y, Sprites s)
        {
            var o = new GameObjectData { Name = "Shop", Type = "Object", X = x, Y = y, Width = 40, Height = 56, Color = "#8B6FD6", SpritePath = s.Shop, Physics = new PhysicsData { Mode = PhysicsMode.None } };
            o.Behaviors.Add(Script("[{\"when\":\"touch\",\"name\":\"Player\",\"do\":\"say\",\"text\":\"Welcome! Press B to buy a speed boost for 15 coins.\",\"speaker\":\"Shop\"}]"));
            return o;
        }

        private static GameObjectData Goal(double x, double y, string next, Sprites s)
        {
            var o = new GameObjectData { Name = "Goal", Type = "Object", X = x, Y = y, Width = 40, Height = 70, Color = "#3BC97A", SpritePath = s.Goal, Physics = new PhysicsData { Mode = PhysicsMode.None } };
            var gs = B(BehaviorKind.GoToScene); gs.Texts["scene"] = next; gs.Texts["trigger"] = "Player"; o.Behaviors.Add(gs);
            return o;
        }

        private static GameObjectData Solid(string name, double x, double y, double w, double h, string tile)
        {
            var o = new GameObjectData { Name = name, Type = "Object", X = x, Y = y, Width = w, Height = h, Color = "#3A3A46", SpritePath = tile, TileImage = true, Physics = new PhysicsData { Mode = PhysicsMode.Static } };
            o.Behaviors.Add(B(BehaviorKind.Solid));
            return o;
        }

        private static GameObjectData MovingPlatform(double x, double y, double w, Sprites s)
        {
            var o = new GameObjectData { Name = "MovingPlatform", Type = "Object", X = x, Y = y, Width = w, Height = 24, Color = "#5A6A7A", SpritePath = s.Tile, TileImage = true, Physics = new PhysicsData { Mode = PhysicsMode.None } };
            o.Behaviors.Add(B(BehaviorKind.Solid));
            var pat = B(BehaviorKind.Patrol); pat.Nums["speed"] = 60; pat.Nums["distance"] = 200; pat.Nums["axis"] = 0; o.Behaviors.Add(pat);
            return o;
        }

        private static GameObjectData HudLabel(string name, double x, double y, string text, string color, double size = 22)
        {
            var o = new GameObjectData { Name = name, Type = "Object", X = x, Y = y, Width = 420, Height = size + 12, Color = "#00000000", Physics = new PhysicsData { Mode = PhysicsMode.None } };
            var lb = B(BehaviorKind.Label); lb.Texts["text"] = text; lb.Texts["color"] = color; lb.Nums["fontSize"] = size; o.Behaviors.Add(lb);
            return o;
        }

        // ============================================================
        //  SPRITE GENERATION (drawn with WPF, saved as PNG)
        // ============================================================
        private static Sprites GenerateSprites(string dir)
        {
            var s = new Sprites();
            // hero idle (2) + walk (4)
            for (int f = 0; f < 2; f++) s.HeroIdle.Add(Save(dir, $"hero_idle_{f}.png", 40, 56, (dc, w, h) => DrawHero(dc, w, h, 0, f == 1 ? -1 : 0)));
            for (int f = 0; f < 4; f++) s.HeroWalk.Add(Save(dir, $"hero_walk_{f}.png", 40, 56, (dc, w, h) => DrawHero(dc, w, h, f, (f % 2 == 1) ? -1 : 0)));
            // enemy (2)
            for (int f = 0; f < 2; f++) s.Enemy.Add(Save(dir, $"enemy_{f}.png", 44, 44, (dc, w, h) => DrawEnemy(dc, w, h, f)));
            // coin (4 — spin)
            for (int f = 0; f < 4; f++) s.Coin.Add(Save(dir, $"coin_{f}.png", 30, 30, (dc, w, h) => DrawCoin(dc, w, h, f)));
            s.Tile = Save(dir, "tile.png", 48, 48, DrawTile);
            s.Spike = Save(dir, "spike.png", 40, 34, DrawSpike);
            s.Shop = Save(dir, "shop.png", 44, 60, DrawShop);
            s.Goal = Save(dir, "goal.png", 44, 74, DrawGoal);
            s.Heart = Save(dir, "heart.png", 28, 26, DrawHeart);
            return s;
        }

        private static string Save(string dir, string name, int w, int h, Action<DrawingContext, int, int> draw)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(Draw(w, h, draw)));
            using (var fs = File.Create(Path.Combine(dir, name))) enc.Save(fs);
            return "Assets/sprites/" + name;
        }

        private static Pen P(string hex, double t) => new(Br(hex), t);

        private static void DrawHero(DrawingContext dc, int w, int h, int legFrame, int bob)
        {
            double cx = w / 2.0; double top = 6 + bob;
            // legs
            var legBrush = Br("#2B5E8C");
            double legSwing = legFrame % 2 == 0 ? 3 : -3;
            dc.DrawRoundedRectangle(legBrush, null, new Rect(cx - 10 + legSwing, h - 14, 8, 12), 3, 3);
            dc.DrawRoundedRectangle(legBrush, null, new Rect(cx + 2 - legSwing, h - 14, 8, 12), 3, 3);
            // body
            dc.DrawRoundedRectangle(Br("#3BC97A"), P("#2A9A5E", 1.5), new Rect(cx - 13, top + 16, 26, 26), 8, 8);
            // head
            dc.DrawEllipse(Br("#F4C9A0"), P("#C99A6E", 1.2), new Point(cx, top + 10), 11, 11);
            // eyes
            dc.DrawEllipse(Br("#222"), null, new Point(cx - 4, top + 9), 1.8, 2.4);
            dc.DrawEllipse(Br("#222"), null, new Point(cx + 4, top + 9), 1.8, 2.4);
            // little smile
            dc.DrawLine(P("#8A5A3A", 1.2), new Point(cx - 3, top + 14), new Point(cx + 3, top + 14));
        }

        private static void DrawEnemy(DrawingContext dc, int w, int h, int frame)
        {
            double cx = w / 2.0; double squash = frame == 1 ? 3 : 0;
            dc.DrawRoundedRectangle(Br("#E5484B"), P("#A02E30", 1.5), new Rect(6, 8 + squash, w - 12, h - 14 - squash), 10, 10);
            // feet
            dc.DrawEllipse(Br("#7A1F20"), null, new Point(cx - 8, h - 5), 4, 3);
            dc.DrawEllipse(Br("#7A1F20"), null, new Point(cx + 8, h - 5), 4, 3);
            // angry eyes
            dc.DrawEllipse(Br("#FFF"), null, new Point(cx - 7, h / 2.0), 5, 5);
            dc.DrawEllipse(Br("#FFF"), null, new Point(cx + 7, h / 2.0), 5, 5);
            dc.DrawEllipse(Br("#111"), null, new Point(cx - 6, h / 2.0 + 1), 2.2, 2.2);
            dc.DrawEllipse(Br("#111"), null, new Point(cx + 8, h / 2.0 + 1), 2.2, 2.2);
            dc.DrawLine(P("#7A1F20", 2), new Point(cx - 12, h / 2.0 - 7), new Point(cx - 3, h / 2.0 - 3));
            dc.DrawLine(P("#7A1F20", 2), new Point(cx + 12, h / 2.0 - 7), new Point(cx + 3, h / 2.0 - 3));
        }

        private static void DrawCoin(DrawingContext dc, int w, int h, int frame)
        {
            double cx = w / 2.0, cy = h / 2.0;
            double[] rx = { 11, 7, 2.5, 7 };
            double r = rx[frame % 4];
            dc.DrawEllipse(Br("#E8A91E"), P("#B07E10", 1.5), new Point(cx, cy), r, 11);
            if (r > 4)
            {
                dc.DrawEllipse(Br("#FFD86B"), null, new Point(cx, cy), r * 0.6, 7);
                dc.DrawEllipse(Br("#FFF2C0"), null, new Point(cx - r * 0.2, cy - 3), 1.6, 2.6);
            }
        }

        private static void DrawTile(DrawingContext dc, int w, int h)
        {
            dc.DrawRectangle(Br("#6B4A2E"), null, new Rect(0, 0, w, h));           // dirt
            dc.DrawRectangle(Br("#5BA85C"), null, new Rect(0, 0, w, 12));           // grass top
            dc.DrawRectangle(Br("#4A8F4C"), null, new Rect(0, 10, w, 4));
            // specks
            dc.DrawEllipse(Br("#5A3E26"), null, new Point(12, 28), 2, 2);
            dc.DrawEllipse(Br("#5A3E26"), null, new Point(34, 38), 2.5, 2.5);
            dc.DrawRectangle(null, P("#3A2A1A", 1), new Rect(0.5, 0.5, w - 1, h - 1));
        }

        private static void DrawSpike(DrawingContext dc, int w, int h)
        {
            var b = Br("#B9C0D0"); var pen = P("#7A8092", 1);
            for (int i = 0; i < 3; i++)
            {
                double x0 = i * (w / 3.0);
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(x0, h), true, true);
                    c.LineTo(new Point(x0 + w / 6.0, 2), true, false);
                    c.LineTo(new Point(x0 + w / 3.0, h), true, false);
                }
                dc.DrawGeometry(b, pen, g);
            }
        }

        private static void DrawShop(DrawingContext dc, int w, int h)
        {
            double cx = w / 2.0;
            dc.DrawRoundedRectangle(Br("#8B6FD6"), P("#5E4AA0", 1.5), new Rect(6, 18, w - 12, h - 24), 8, 8);
            dc.DrawEllipse(Br("#F4C9A0"), P("#C99A6E", 1), new Point(cx, 14), 10, 10);
            // hat
            dc.DrawRectangle(Br("#3A2A5A"), null, new Rect(cx - 12, 4, 24, 5));
            dc.DrawRectangle(Br("#3A2A5A"), null, new Rect(cx - 7, -2, 14, 8));
            // eyes
            dc.DrawEllipse(Br("#222"), null, new Point(cx - 4, 14), 1.6, 2);
            dc.DrawEllipse(Br("#222"), null, new Point(cx + 4, 14), 1.6, 2);
            // coin on belly
            dc.DrawEllipse(Br("#FFD86B"), P("#B07E10", 1), new Point(cx, h - 16), 7, 7);
        }

        private static void DrawGoal(DrawingContext dc, int w, int h)
        {
            dc.DrawRectangle(Br("#8A8F9C"), null, new Rect(6, 4, 5, h - 6));       // pole
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(11, 6), true, true);
                c.LineTo(new Point(w - 4, 16), true, false);
                c.LineTo(new Point(11, 26), true, false);
            }
            dc.DrawGeometry(Br("#3BC97A"), P("#2A9A5E", 1), g);
        }

        private static void DrawHeart(DrawingContext dc, int w, int h)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(w / 2.0, h - 4), true, true);
                c.BezierTo(new Point(-2, h / 2.5), new Point(w / 4.0, -2), new Point(w / 2.0, h / 3.5), true, false);
                c.BezierTo(new Point(w * 3 / 4.0, -2), new Point(w + 2, h / 2.5), new Point(w / 2.0, h - 4), true, false);
            }
            dc.DrawGeometry(Br("#E5484B"), P("#A02E30", 1), g);
        }
    }
}
