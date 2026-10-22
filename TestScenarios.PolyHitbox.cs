using static EasyEngine.DemoKit;

namespace EasyEngine
{
    public static partial class TestScenarios
    {
        // ============================================================ SLOPE TEST
        // Polygon hitboxes (⬠ Hitbox shape): a hill drawn as a triangle you walk up and down, and a rock whose left side is
        // nearly vertical — a wall, not a slope. The boxes of both are much bigger than their shapes.
        private static Scene SlopeTest(Project p)
        {
            var scene = new Scene { Name = "Slope Test", Width = 1300, Height = 600, BackgroundColor = "#1E2633", AmbientLight = 1 };
            foreach (var v in new[] { "onHill", "topReached", "atRock", "confirmLook" }) scene.Variables[v] = 0;

            var baseL = new Layer { Name = "Base", Kind = LayerKind.Base };
            var ui = new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true };
            int id = 1;
            GameObjectData Id(GameObjectData o) { o.Id = id++; return o; }
            GameObjectData Solid(string name, double x, double y, double w, double h, string color, params double[] shape)
            {
                var o = Box(name, x, y, w, h, color);
                o.Physics.Mode = PhysicsMode.Static; o.Behaviors.Add(B(BehaviorKind.Solid));
                if (shape.Length >= 6)
                {
                    o.Physics.Shape = new();
                    for (int i = 0; i < shape.Length; i += 2) o.Physics.Shape.Add(new PolyPoint(shape[i], shape[i + 1]));
                }
                return o;
            }

            baseL.Objects.Add(Id(Solid("Ground", 0, 500, 1300, 100, "#3A3A46")));
            baseL.Objects.Add(Id(Solid("Hill", 400, 350, 400, 150, "#40304A", 0, 1, 0.5, 0, 1, 1)));        // up to x 600, then down
            baseL.Objects.Add(Id(Solid("Rock", 950, 300, 150, 200, "#4A3A30", 0, 1, 0.08, 0, 1, 0, 1, 1))); // a steep left side

            var hero = new GameObjectData { Name = "Hero", Type = "Player", X = 200, Y = 444, Width = 40, Height = 56, Color = "#3BC97A", Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            hero.Behaviors.Add(B(BehaviorKind.Platformer));
            baseL.Objects.Add(Id(hero));

            var ctl = Box("Controller", 0, 0, 4, 4, "#00000000");
            ctl.Behaviors.Add(Script(
                "[{\"when\":\"expr\",\"expr\":\"Hero.y + Hero.h < 470 && Hero.x > 420 && Hero.x < 760\",\"do\":\"setvar\",\"var\":\"onHill\",\"to\":1}," +
                "{\"when\":\"expr\",\"expr\":\"Hero.y + Hero.h <= 362\",\"do\":\"setvar\",\"var\":\"topReached\",\"to\":1}," +
                "{\"when\":\"expr\",\"expr\":\"Hero.x + Hero.w >= 946 && Hero.y + Hero.h >= 499\",\"do\":\"setvar\",\"var\":\"atRock\",\"to\":1}," +
                "{\"when\":\"keypress\",\"key\":\"Y\",\"do\":\"setvar\",\"var\":\"confirmLook\",\"to\":1}," +
                "{\"when\":\"keypress\",\"key\":\"R\",\"do\":\"goscene\",\"scene\":\"Slope Test\"}]"));
            baseL.Objects.Add(Id(ctl));

            var panel = Box("HelpPanel", 12, 62, 640, 104, "#B0101018"); panel.CornerRadius = 8;
            ui.Objects.Add(Id(panel));
            ui.Objects.Add(Id(Label("Help1", 24, 68, "SLOPES   The hill and the rock have hitboxes drawn as shapes (⬠ Hitbox shape).", "#FFFFFF", 14)));
            ui.Objects.Add(Id(Label("Help2", 24, 90, "D = walk right: up the hill, over the top, down the other side", "#FFFFFF", 13)));
            ui.Objects.Add(Id(Label("Help3", 24, 110, "Keep going: the rock's steep side stops you (it's a wall, not a slope)", "#FFFFFF", 13)));
            ui.Objects.Add(Id(Label("Help4", 24, 130, "Looks right? Y  ·  F1 console  ·  R restart", "#FFFFFF", 13)));

            var checks = Box("Checks", 0, 0, 4, 4, "#00000000");
            var cl = B(BehaviorKind.Checklist);
            cl.Texts["title"] = "Hitbox shapes";
            cl.Texts["items"] = string.Join("\n",
                "Walk up the hill (D): you stand on its slope => onHill >= 1",
                "Reach the top of the hill => topReached >= 1",
                "Down the other side, the rock's steep side stops you => atRock >= 1",
                "You walked on the drawn shapes, not their boxes — press Y => confirmLook >= 1");
            cl.Nums["width"] = 360;
            checks.Behaviors.Add(cl);
            ui.Objects.Add(Id(checks));

            scene.Layers.Add(new Layer { Name = "Background", Kind = LayerKind.Background });
            scene.Layers.Add(baseL); scene.Layers.Add(ui);
            return scene;
        }
    }
}
