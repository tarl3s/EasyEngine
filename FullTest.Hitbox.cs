using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EasyEngine;

// "Hitbox: fit to the drawing": the visible-pixel bounds of a PNG; the runtime collides with the drawing (a wall stops
// the drawing, not the box) while the picture keeps its place and size; facing left mirrors the margins; an animation
// uses the union of its frames; a tiled image and the option off keep the whole box; old projects load with it off;
// "Trim box to drawing" shrinks the box and crops the images (undoable); the editor outline; the AI's "fitHitbox".
public partial class App
{
    /// <summary>A w×h sprite, transparent except an opaque rectangle (exact pixels — no anti-aliasing).</summary>
    internal static BitmapSource HitboxTestSprite(int w, int h, Int32Rect opaque, string hex = "#E5484B")
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        var px = new byte[w * h * 4];
        for (int y = opaque.Y; y < opaque.Y + opaque.Height; y++)
            for (int x = opaque.X; x < opaque.X + opaque.Width; x++)
            {
                int i = (y * w + x) * 4;
                px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R; px[i + 3] = 255;
            }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        bmp.Freeze();
        return bmp;
    }

    partial void FeatureTestsHitbox(FullTestKit t)
    {
        static bool Near(double a, double b, double tol = 0.6) => Math.Abs(a - b) <= tol;
        static bool RectNear(Rect a, double x, double y, double w, double h, double tol = 0.6) => Near(a.X, x, tol) && Near(a.Y, y, tol) && Near(a.Width, w, tol) && Near(a.Height, h, tol);
        // the test sprite: 64×64, visible from x 16..40 and y 8..64 → margins left 16, right 24, top 8, bottom 0
        var heroPixels = new Int32Rect(16, 8, 24, 56);

        // a sim with a Platformer "Hero" (its picture has transparent margins) on a floor, between two walls
        PlayWindow HeroSim(bool fit, bool tile = false, Action<Project, GameObjectData>? tweak = null) => t.Sim("[]", PhysicsMode.None, (p, s) =>
        {
            var layer = s.Layers.First(l => l.Kind == LayerKind.Base);
            var hero = new GameObjectData { Id = 10, Name = "Hero", Type = "Player", X = 200, Y = 536, Width = 64, Height = 64, Physics = new PhysicsData { Mode = PhysicsMode.Dynamic, FitHitbox = fit } };
            hero.SpritePath = ProjectIO.SaveSprite(p, HitboxTestSprite(64, 64, heroPixels), "hero");
            hero.TileImage = tile;
            hero.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Platformer).CreateData());
            tweak?.Invoke(p, hero);
            layer.Objects.Add(hero);
            GameObjectData Solid(int id, string name, double x, double y, double w, double h)
            {
                var o = new GameObjectData { Id = id, Name = name, X = x, Y = y, Width = w, Height = h, Physics = new PhysicsData { Mode = PhysicsMode.Static } };
                o.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Solid).CreateData());
                return o;
            }
            layer.Objects.Add(Solid(11, "Floor", 0, 600, 2000, 80));
            layer.Objects.Add(Solid(12, "WallR", 400, 300, 40, 300));
            layer.Objects.Add(Solid(13, "WallL", 60, 300, 40, 300));
        });
        void Hold(PlayWindow p, string key, int frames) { p.TKey(key, true); p.TSteps(frames); p.TKey(key, false); p.TSteps(3); }

        // ---------------- the bounds calculation
        t.Test("hitbox: the visible pixels of a PNG with transparent margins (and none / all visible)", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "EE_Full_Hitbox_" + Guid.NewGuid().ToString("N")[..6]);
            var p = ProjectIO.CreateNew("HB", dir);
            var abs = ProjectIO.AbsoluteAsset(p, ProjectIO.SaveSprite(p, HitboxTestSprite(64, 64, heroPixels), "margins"));
            var f = HitboxFit.OpaqueFile(abs);
            var none = HitboxFit.OpaqueFile(ProjectIO.AbsoluteAsset(p, ProjectIO.SaveSprite(p, HitboxTestSprite(20, 10, new Int32Rect(0, 0, 0, 0)), "empty")));
            var full = HitboxFit.OpaqueFile(ProjectIO.AbsoluteAsset(p, ProjectIO.SaveSprite(p, HitboxTestSprite(20, 10, new Int32Rect(0, 0, 20, 10)), "full")));
            return f is Rect r && RectNear(r, 16 / 64.0, 8 / 64.0, 24 / 64.0, 56 / 64.0, 1e-9) && none == null && full is Rect w && HitboxFit.IsWhole(w)
                   && HitboxFit.OpaqueFile(abs) == f; // cached
        });

        // ---------------- model + old files
        t.Test("hitbox: off by default; an old project without the field loads with it off; it saves", () =>
        {
            var old = JsonSerializer.Deserialize<GameObjectData>("{\"Name\":\"A\",\"SpritePath\":\"a.png\",\"Physics\":{\"Mode\":2,\"GravityScale\":1,\"Bounciness\":0}}")!;
            var on = new GameObjectData { SpritePath = "a.png" }; on.Physics.FitHitbox = true;
            var back = JsonSerializer.Deserialize<GameObjectData>(JsonSerializer.Serialize(on))!;
            return !new PhysicsData().FitHitbox && !old.Physics.FitHitbox && !HitboxFit.Applies(old) && back.Physics.FitHitbox && HitboxFit.Applies(back);
        });

        // ---------------- runtime
        t.Test("hitbox: on → the object collides with the drawing's part of its box; the picture keeps its place and size", () =>
        {
            var p = HeroSim(true);
            var hb = p.THitbox("Hero"); var pic = p.TPictureAt("Hero");
            var o = p.TOrigins("Hero")[0];
            return p.TFitted("Hero") && RectNear(hb, 216, 544, 24, 56) && Near(pic.X, 200) && Near(pic.Y, 536)
                   && Near(o.sx, 216) && Near(o.sy, 544) && p.TW("Hero") < 25;
        });
        t.Test("hitbox: walking into a wall stops at the drawing's edge, not the box's (off = the old box)", () =>
        {
            var on = HeroSim(true); Hold(on, "D", 90);
            var off = HeroSim(false); Hold(off, "D", 90);
            var hb = on.THitbox("Hero");
            return Near(hb.Right, 400, 1) && Near(on.TPictureAt("Hero").X, hb.X - 16, 0.01) && on.TPictureAt("Hero").X + 64 > 420 // the empty margin overlaps the wall
                   && !off.TFitted("Hero") && Near(off.TX("Hero") + 64, 400, 1) && Near(off.THitbox("Hero").Width, 64, 0.01) && on.TGrounded("Hero");
        });
        t.Test("hitbox: facing left mirrors the margins — the drawing stays over the hitbox (and the left wall stops the drawing)", () =>
        {
            var p = HeroSim(true);
            Hold(p, "D", 5);
            var hb1 = p.THitbox("Hero"); double right = p.TPictureAt("Hero").X;
            bool faceRight = p.TFacing("Hero") == 1 && Near(right, hb1.X - 16, 0.01); // left margin 16 on the left
            Hold(p, "A", 120);
            var hb2 = p.THitbox("Hero"); double left = p.TPictureAt("Hero").X;
            // mirrored: the 24-px right margin is now on the left, so picture + 24 = the drawing's left edge = the hitbox's
            return faceRight && p.TFacing("Hero") == -1 && Near(left, hb2.X - 24, 0.01) && Near(hb2.X, 100, 1) && Near(hb2.Width, 24, 0.01);
        });
        t.Test("hitbox: with an animation the box is the union of the default animation's frames (no jumping between frames)", () =>
        {
            Rect? editorView = null;
            var p = HeroSim(true, tweak: (proj, hero) =>
            {
                var a = ProjectIO.SaveSprite(proj, HitboxTestSprite(64, 64, new Int32Rect(16, 8, 24, 56)), "walk1");
                var b = ProjectIO.SaveSprite(proj, HitboxTestSprite(64, 64, new Int32Rect(8, 16, 24, 48)), "walk2");
                var big = ProjectIO.SaveSprite(proj, HitboxTestSprite(64, 64, new Int32Rect(0, 0, 64, 64)), "attack");
                hero.Animations.Add(new SpriteAnimation { Name = "idle", Frames = { a, b } });
                hero.Animations.Add(new SpriteAnimation { Name = "attack", Frames = { big } }); // not the default: ignored
                hero.DefaultAnimation = "idle";
                editorView = HitboxFit.Fraction(proj, hero);
            });
            return RectNear(p.THitbox("Hero"), 208, 544, 32, 56) && editorView is Rect f && RectNear(f, 8 / 64.0, 8 / 64.0, 32 / 64.0, 56 / 64.0, 1e-9);
        });
        t.Test("hitbox: a tiled (repeating) image is not trimmed", () =>
        {
            var p = HeroSim(true, tile: true);
            return !p.TFitted("Hero") && RectNear(p.THitbox("Hero"), 200, 536, 64, 64);
        });
        t.Test("hitbox: copies spawned during play and a respawn after a fall use the fitted box", () =>
        {
            var p = HeroSim(true, tweak: (proj, hero) => hero.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Health).CreateData()));
            var o = p.TOrigins("Hero")[0];
            p.TPlace("Hero", 1000, 4400); p.TSteps(2); // below the world → respawn where it started
            var back = p.THitbox("Hero");
            bool respawn = Near(o.rx, 216) && Near(o.ry, 544) && Near(back.X, 216, 1) && back.Y < 600 && Near(back.Width, 24, 0.01);

            var s = t.Sim("[{\"when\":\"start\",\"do\":\"spawn\",\"object\":\"Slime\"}]", PhysicsMode.None, (proj, sc) =>
            {
                var tpl = new GameObjectData { Id = 990, Name = "Slime", Width = 64, Height = 64, Physics = new PhysicsData { FitHitbox = true } };
                tpl.SpritePath = ProjectIO.SaveSprite(proj, HitboxTestSprite(64, 64, heroPixels), "slime");
                proj.Prefabs.Add(new PrefabDef { Name = "Slime", Template = tpl });
            });
            s.TSteps(2);
            var sb = s.THitbox("Slime"); var spic = s.TPictureAt("Slime");
            bool spawned = s.TAlive("Slime") == 1 && s.TFitted("Slime") && Near(sb.Width, 24, 0.01) && Near(sb.Height, 56, 0.01)
                           && Near(sb.X - spic.X, 16, 0.01) && Near(sb.Y - spic.Y, 8, 0.01);
            return respawn && spawned;
        });

        // ---------------- editor
        t.Test("hitbox: the editor outlines the hitbox only when the option is on; Trim box to drawing shrinks the box + crops (undoable)", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "EE_Full_HitboxEd_" + Guid.NewGuid().ToString("N")[..6]);
            var project = ProjectIO.CreateNew("HBE", dir);
            var layer = project.GetStartScene().Layers.First(l => l.Kind == LayerKind.Base);
            var hero = new GameObjectData { Id = 50, Name = "Hero", X = 200, Y = 200, Width = 128, Height = 128 }; // the 64-px image drawn ×2
            hero.SpritePath = ProjectIO.SaveSprite(project, HitboxTestSprite(64, 64, heroPixels), "hero");
            var plain = new GameObjectData { Id = 51, Name = "Plain", X = 400, Y = 200, Width = 64, Height = 64, SpritePath = hero.SpritePath };
            hero.Physics.FitHitbox = true;
            layer.Objects.Add(hero); layer.Objects.Add(plain);
            var ed = new MainWindow(project);
            ed.TestSelect(hero);
            bool outline = ed.TestHasHitboxOutline(hero) && !ed.TestHasHitboxOutline(plain);
            bool hasButton = ed.TestInspectorItems().OfType<System.Windows.Controls.WrapPanel>()
                .Any(w => w.Children.OfType<System.Windows.Controls.Button>().Any(b => (b.Content as string)?.Contains("Trim") == true));
            var oldSprite = hero.SpritePath;
            ed.TestTrim(hero);
            var trimmed = ed.TestFindObject("Hero")!;
            var abs = ProjectIO.AbsoluteAsset(project, trimmed.SpritePath);
            var img = abs == null ? null : HitboxFit.LoadBitmap(abs);
            bool shrunk = Near(trimmed.X, 232, 0.01) && Near(trimmed.Y, 216, 0.01) && Near(trimmed.Width, 48, 0.01) && Near(trimmed.Height, 112, 0.01)
                          && trimmed.SpritePath != oldSprite && img != null && img.PixelWidth == 24 && img.PixelHeight == 56
                          && HitboxFit.OpaqueFile(abs) is Rect w && HitboxFit.IsWhole(w)
                          && ProjectIO.AbsoluteAsset(project, oldSprite) != null; // the original (maybe shared) image is untouched
            ed.TestUndo();
            var undone = ed.TestFindObject("Hero")!;
            bool restored = Near(undone.X, 200) && Near(undone.Width, 128) && undone.SpritePath == oldSprite;
            return outline && hasButton && shrunk && restored;
        });
        t.Test("hitbox: the AI's \"fitHitbox\":true turns the option on (and the build prompt explains it)", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "EE_Full_HitboxAi_" + Guid.NewGuid().ToString("N")[..6]);
            var project = ProjectIO.CreateNew("HBA", dir);
            var ed = new MainWindow(project);
            var (ok, _) = ed.TestApplySpec("{\"create\":[{\"name\":\"Slime\",\"x\":100,\"y\":100,\"width\":64,\"height\":64,\"fitHitbox\":\"true\"}]}");
            return ok && ed.TestFindObject("Slime")?.Physics.FitHitbox == true && ed.TestBuildPrompt("its picture has empty space around it").Contains("\"fitHitbox\":true");
        });
    }
}
