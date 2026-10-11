using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EasyEngine
{
    /// <summary>
    /// Builds a small playable project whose character is drawn in code with the same
    /// pixel/PNG pipeline the built-in sprite editor uses (BGRA buffer -> ProjectIO.SaveSprite).
    /// Animations: idle, walk, jump, crouch. Player has Platformer movement + a follow camera.
    /// </summary>
    public static class SpriteTestGame
    {
        const int W = 32, H = 40;

        // colors (R,G,B)
        static readonly (byte r, byte g, byte b) Skin = (0xF1, 0xC2, 0x7D);
        static readonly (byte r, byte g, byte b) Shirt = (0x3B, 0x6E, 0xF4);
        static readonly (byte r, byte g, byte b) Pants = (0x33, 0x37, 0x40);
        static readonly (byte r, byte g, byte b) Hair = (0x5A, 0x3A, 0x22);
        static readonly (byte r, byte g, byte b) Black = (0x10, 0x10, 0x14);

        public static string Build(string parentFolder)
        {
            var project = ProjectIO.CreateNew("Sprite Test", parentFolder);
            var scene = project.GetStartScene();
            scene.Name = "Playground";
            scene.Width = 1280; scene.Height = 560;
            scene.BackgroundColor = "#1b2330";

            var baseLayer = scene.Layers.Find(l => l.Kind == LayerKind.Base) ?? scene.Layers[0];
            baseLayer.Objects.Clear();

            // ground + a couple of platforms to jump on
            baseLayer.Objects.Add(Solid(100, "Ground", 0, 500, 1280, 60, "#3a5a40"));
            baseLayer.Objects.Add(Solid(101, "Platform1", 360, 400, 180, 24, "#5a7a50"));
            baseLayer.Objects.Add(Solid(102, "Platform2", 640, 320, 180, 24, "#5a7a50"));
            baseLayer.Objects.Add(Solid(103, "Platform3", 920, 410, 180, 24, "#5a7a50"));

            // the hero
            var hero = new GameObjectData
            {
                Id = 1,
                Name = "Hero",
                Type = "Player",
                X = 120, Y = 440, Width = W, Height = H,
                Color = "#00000000",
                Physics = new PhysicsData { Mode = PhysicsMode.Dynamic }
            };
            hero.Behaviors.Add(new BehaviorData { Kind = BehaviorKind.Platformer, Nums = { ["controls"] = 1, ["speed"] = 200, ["jump"] = 620 } });
            hero.Behaviors.Add(new BehaviorData { Kind = BehaviorKind.FollowCamera, Nums = { ["clamp"] = 1 } });

            hero.Animations.Add(MakeAnim(project, "idle", 3, true, new[] { Idle(0), Idle(1) }));
            hero.Animations.Add(MakeAnim(project, "walk", 10, true, new[] { Walk(0), Walk(1), Walk(2), Walk(3) }));
            hero.Animations.Add(MakeAnim(project, "jump", 1, true, new[] { Jump() }));
            hero.Animations.Add(MakeAnim(project, "crouch", 1, true, new[] { Crouch() }));
            hero.DefaultAnimation = "idle";
            baseLayer.Objects.Add(hero);

            // a HUD hint
            var hud = new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true };
            var hint = new GameObjectData { Id = 2, Name = "Hint", Type = "Object", X = 16, Y = 16, Width = 520, Height = 28, Color = "#00000000" };
            hint.Behaviors.Add(new BehaviorData { Kind = BehaviorKind.Label, Texts = { ["text"] = "Arrows / A-D = walk   •   Space / Up = jump   •   Down = crouch" } });
            hud.Objects.Add(hint);
            scene.Layers.Add(hud);

            ProjectIO.Save(project);
            return project.FilePath!;
        }

        static GameObjectData Solid(int id, string name, double x, double y, double w, double h, string color)
        {
            var o = new GameObjectData { Id = id, Name = name, Type = "Object", X = x, Y = y, Width = w, Height = h, Color = color, Physics = new PhysicsData { Mode = PhysicsMode.Static } };
            o.Behaviors.Add(new BehaviorData { Kind = BehaviorKind.Solid });
            return o;
        }

        static SpriteAnimation MakeAnim(Project project, string name, double fps, bool loop, byte[][] frames)
        {
            var anim = new SpriteAnimation { Name = name, Fps = fps, Loop = loop };
            for (int i = 0; i < frames.Length; i++)
            {
                var bmp = BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, frames[i], W * 4);
                anim.Frames.Add(ProjectIO.SaveSprite(project, bmp, $"{name}_{i}"));
            }
            return anim;
        }

        // ---------------- pixel drawing (BGRA, same format as the sprite editor) ----------------
        static byte[] New() => new byte[W * H * 4];

        static void Px(byte[] d, int x, int y, (byte r, byte g, byte b) c)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            int i = (y * W + x) * 4;
            d[i] = c.b; d[i + 1] = c.g; d[i + 2] = c.r; d[i + 3] = 255;
        }

        static void Rect(byte[] d, int x0, int y0, int x1, int y1, (byte, byte, byte) c)
        {
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) Px(d, x, y, c);
        }

        static void Circle(byte[] d, int cx, int cy, int r, (byte, byte, byte) c)
        {
            for (int y = -r; y <= r; y++) for (int x = -r; x <= r; x++) if (x * x + y * y <= r * r) Px(d, cx + x, cy + y, c);
        }

        static void ThickLine(byte[] d, int x0, int y0, int x1, int y1, int t, (byte, byte, byte) c)
        {
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx - dy;
            int h = t / 2;
            while (true)
            {
                for (int oy = -h; oy <= h; oy++) for (int ox = -h; ox <= h; ox++) Px(d, x0 + ox, y0 + oy, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }

        // Head + torso + arms, shared by standing frames. bob shifts the upper body down a pixel.
        static void UpperBody(byte[] d, int bob, int lHandX, int lHandY, int rHandX, int rHandY)
        {
            int hy = 8 + bob;
            Circle(d, 16, hy, 5, Skin);
            Rect(d, 11, hy - 6, 21, hy - 3, Hair); // hair cap
            Px(d, 14, hy, Black); Px(d, 18, hy, Black); // eyes
            Rect(d, 12, 14 + bob, 20, 27, Shirt);      // torso
            ThickLine(d, 12, 15 + bob, lHandX, lHandY, 2, Skin); // left arm
            ThickLine(d, 20, 15 + bob, rHandX, rHandY, 2, Skin); // right arm
        }

        static byte[] Idle(int frame)
        {
            var d = New();
            int bob = frame == 1 ? 1 : 0;
            UpperBody(d, bob, 10, 26, 22, 26);
            ThickLine(d, 14, 27, 14, 38, 3, Pants); // legs straight
            ThickLine(d, 18, 27, 18, 38, 3, Pants);
            return d;
        }

        static byte[] Walk(int frame)
        {
            var d = New();
            // leg + opposite-arm swing per frame
            (int lFoot, int rFoot, int lHand, int rHand) = frame switch
            {
                0 => (18, 14, 22, 11),  // left leg fwd, right arm fwd
                1 => (16, 16, 12, 20),  // passing
                2 => (14, 18, 11, 22),  // right leg fwd, left arm fwd
                _ => (16, 16, 12, 20),  // passing
            };
            UpperBody(d, 0, lHand, 26, rHand, 26);
            ThickLine(d, 14, 27, lFoot, 38, 3, Pants);
            ThickLine(d, 18, 27, rFoot, 38, 3, Pants);
            return d;
        }

        static byte[] Jump()
        {
            var d = New();
            UpperBody(d, 0, 8, 8, 24, 8);   // arms up
            ThickLine(d, 14, 27, 12, 34, 3, Pants); // legs tucked/bent
            ThickLine(d, 18, 27, 20, 34, 3, Pants);
            return d;
        }

        static byte[] Crouch()
        {
            var d = New();
            int hy = 15;
            Circle(d, 16, hy, 5, Skin);
            Rect(d, 11, hy - 6, 21, hy - 3, Hair);
            Px(d, 14, hy, Black); Px(d, 18, hy, Black);
            Rect(d, 10, 20, 22, 29, Shirt);            // short, wide torso
            ThickLine(d, 11, 21, 8, 29, 2, Skin);      // arms low
            ThickLine(d, 21, 21, 24, 29, 2, Skin);
            ThickLine(d, 13, 30, 10, 37, 3, Pants);    // bent legs
            ThickLine(d, 19, 30, 22, 37, 3, Pants);
            return d;
        }
    }
}
