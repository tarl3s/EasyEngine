using System.Windows;
using System.Windows.Media;
using static EasyEngine.DemoKit;

namespace EasyEngine
{
    public static partial class TestScenarios
    {
        // ============================================================ HITBOX TEST
        // "Hitbox: fit to the drawing": the hero's picture is 96×96 but the drawing is only the middle-left part of it (wide
        // transparent margins, more on the right). With the option on, walking into a wall stops the DRAWING at the wall
        // (the empty margin overlaps it); turning left mirrors the margins, so the drawing still touches the left wall.
        private static Scene HitboxTest(Project p)
        {
            var scene = new Scene { Name = "Hitbox Test", Width = 1200, Height = 600, BackgroundColor = "#1E2633", AmbientLight = 1 };
            foreach (var v in new[] { "touchR", "touchL", "confirmLook" }) scene.Variables[v] = 0;

            var baseL = new Layer { Name = "Base", Kind = LayerKind.Base };
            var ui = new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true };
            int id = 1;
            GameObjectData Id(GameObjectData o) { o.Id = id++; return o; }
            GameObjectData Solid(string name, double x, double y, double w, double h, string color)
            {
                var o = Box(name, x, y, w, h, color);
                o.Physics.Mode = PhysicsMode.Static; o.Behaviors.Add(B(BehaviorKind.Solid));
                return o;
            }

            baseL.Objects.Add(Id(Solid("Ground", 0, 500, 1200, 100, "#3A3A46")));
            baseL.Objects.Add(Id(Solid("WallRight", 900, 200, 40, 300, "#8A5A3A")));
            baseL.Objects.Add(Id(Solid("WallLeft", 60, 200, 40, 300, "#8A5A3A")));

            var hero = new GameObjectData { Name = "Hero", Type = "Player", X = 400, Y = 404, Width = 96, Height = 96, Color = "#3BC97A", Physics = new PhysicsData { Mode = PhysicsMode.Dynamic, FitHitbox = true } };
            hero.SpritePath = HitboxHeroSprite(p);
            hero.Behaviors.Add(B(BehaviorKind.Platformer));
            baseL.Objects.Add(Id(hero));

            var ctl = Box("Controller", 0, 0, 4, 4, "#00000000");
            ctl.Behaviors.Add(Script(
                "[{\"when\":\"expr\",\"expr\":\"Hero.x + Hero.w >= 899\",\"do\":\"setvar\",\"var\":\"touchR\",\"to\":1}," +
                "{\"when\":\"expr\",\"expr\":\"Hero.x <= 101 && Hero.facing < 0\",\"do\":\"setvar\",\"var\":\"touchL\",\"to\":1}," +
                "{\"when\":\"keypress\",\"key\":\"Y\",\"do\":\"setvar\",\"var\":\"confirmLook\",\"to\":1}," +
                "{\"when\":\"keypress\",\"key\":\"R\",\"do\":\"goscene\",\"scene\":\"Hitbox Test\"}]"));
            baseL.Objects.Add(Id(ctl));

            var panel = Box("HelpPanel", 12, 62, 640, 104, "#B0101018"); panel.CornerRadius = 8;
            ui.Objects.Add(Id(panel));
            ui.Objects.Add(Id(Label("Help1", 24, 68, "HITBOX   The hero's picture has wide empty margins; its hitbox fits the drawing.", "#FFFFFF", 14)));
            ui.Objects.Add(Id(Label("Help2", 24, 90, "D = walk right into the wall: the DRAWING touches it, the empty margin overlaps it", "#FFFFFF", 13)));
            ui.Objects.Add(Id(Label("Help3", 24, 110, "A = turn and walk left: the margins flip with the picture, the drawing touches the wall", "#FFFFFF", 13)));
            ui.Objects.Add(Id(Label("Help4", 24, 130, "Looks right? Y  ·  F1 console  ·  R restart", "#FFFFFF", 13)));

            var checks = Box("Checks", 0, 0, 4, 4, "#00000000");
            var cl = B(BehaviorKind.Checklist);
            cl.Texts["title"] = "Hitbox fits the drawing";
            cl.Texts["items"] = string.Join("\n",
                "The hitbox is narrower than the picture => Hero.w < 60",
                "Walk right (D): the drawing reaches the wall => touchR >= 1",
                "Walk left (A): the drawing reaches the other wall => touchL >= 1",
                "The drawing (not empty space) touches both walls — press Y => confirmLook >= 1");
            cl.Nums["width"] = 340;
            checks.Behaviors.Add(cl);
            ui.Objects.Add(Id(checks));

            scene.Layers.Add(new Layer { Name = "Background", Kind = LayerKind.Background });
            scene.Layers.Add(baseL); scene.Layers.Add(ui);
            return scene;
        }

        /// <summary>The off-centre hero picture (96×96, wide empty margins) saved in the project; also used by the selftest.</summary>
        internal static string HitboxHeroSprite(Project p) => ProjectIO.SaveSprite(p, GameAssets.Draw(96, 96, DrawOffCenterHero), "hitbox_hero");

        // a little hero drawn in the left half of a 96×96 picture: body x 20..60, head on top (y from 18); the rest is empty
        private static void DrawOffCenterHero(DrawingContext dc, int w, int h)
        {
            var outline = new Pen(GameAssets.Br("#1E5A3A"), 2);
            dc.DrawRoundedRectangle(GameAssets.Br("#3BC97A"), outline, new Rect(21, 44, 38, 51), 8, 8);   // body
            dc.DrawEllipse(GameAssets.Br("#F2C79A"), outline, new Point(40, 32), 14, 13);                  // head
            dc.DrawEllipse(GameAssets.Br("#1A1A20"), null, new Point(46, 30), 2.5, 2.5);                   // eye (looks right)
            dc.DrawRectangle(GameAssets.Br("#2A8A5A"), null, new Rect(26, 70, 28, 6));                     // belt
        }
    }
}
