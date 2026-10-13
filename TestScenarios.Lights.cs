using static EasyEngine.DemoKit;

namespace EasyEngine
{
    public static partial class TestScenarios
    {
        // ============================================================ LIGHT SHAPES TEST
        // A dark room and every kind of Light: the player's flashlight (beam, follows where it faces), a lit window (area),
        // a pink neon tube that pulses (bar), a wall lamp lighting downwards (half circle), a flickering torch and a red
        // alarm that blinks. Walk left and back right with the flashlight, reach the window, Y confirms the look; R restarts.
        public const string LightShapesScene = "Light Shapes Test";

        private static Scene LightShapesTest()
        {
            var scene = new Scene { Name = LightShapesScene, Width = 1600, Height = 600, BackgroundColor = "#202838", AmbientLight = 0.04 };
            foreach (var v in new[] { "wentLeft", "backRight", "sawWindow", "confirmLook" }) scene.Variables[v] = 0;
            var bg = new Layer { Name = "Background", Kind = LayerKind.Background };
            var baseL = new Layer { Name = "Base", Kind = LayerKind.Base };
            var ui = new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true };
            int id = 1;
            GameObjectData Id(GameObjectData o) { o.Id = id++; return o; }
            BehaviorData Light(string shape, string color, double radius, double intensity = 0.95, string direction = "facing", string effect = "none", double speed = 1)
            {
                var l = B(BehaviorKind.LightSource);
                l.Texts["shape"] = shape; l.Texts["color"] = color; l.Texts["direction"] = direction; l.Texts["effect"] = effect;
                l.Nums["radius"] = radius; l.Nums["intensity"] = intensity; l.Nums["speed"] = speed;
                return l;
            }

            // the room: a wall with things on it, so the lights have something to show
            bg.Objects.Add(Id(Box("RoomWall", 0, 0, 1600, 500, "#6E7C99")));
            int n = 0;
            foreach (var (x, y, w, h, c) in new[] { (80, 300, 90, 120, "#8A5A3A"), (260, 120, 70, 90, "#C9A24B"), (420, 360, 140, 60, "#5B8F5B"),
                                                     (880, 330, 60, 90, "#8A5A3A"), (1120, 360, 160, 50, "#A0525A"), (1420, 300, 80, 120, "#5A6FA0") })
                bg.Objects.Add(Id(Box("Furniture" + ++n, x, y, w, h, c)));
            var ground = Box("Ground", 0, 500, 1600, 100, "#4A3A2E");
            ground.Physics.Mode = PhysicsMode.Static; ground.Behaviors.Add(B(BehaviorKind.Solid));
            baseL.Objects.Add(Id(ground));

            // the lights
            var window = Box("Window", 640, 150, 130, 100, "#9CC3FF"); window.Behaviors.Add(Light("area", "#BFD9FF", 50, 0.85)); baseL.Objects.Add(Id(window));
            var neon = Box("Neon", 950, 170, 240, 8, "#FF4FD8"); neon.CornerRadius = 4; neon.Behaviors.Add(Light("bar", "#FF4FD8", 90, 0.9, effect: "pulse")); baseL.Objects.Add(Id(neon));
            var lamp = Box("WallLamp", 1300, 240, 44, 16, "#FFE7A0"); lamp.CornerRadius = 6; lamp.Behaviors.Add(Light("half", "#FFE7A0", 200, 0.95, direction: "down")); baseL.Objects.Add(Id(lamp));
            var torch = Box("Torch", 470, 420, 12, 40, "#FFA040"); torch.Behaviors.Add(Light("circle", "#FFA040", 150, 0.9, effect: "flicker")); baseL.Objects.Add(Id(torch));
            var alarm = Box("Alarm", 1520, 110, 26, 26, "#FF3030"); alarm.CornerRadius = 13; alarm.Behaviors.Add(Light("circle", "#FF3030", 130, 0.8, effect: "blink")); baseL.Objects.Add(Id(alarm));

            // the player with a flashlight that points where it faces
            var pl = new GameObjectData { Name = "Player", Type = "Player", X = 150, Y = 440, Width = 34, Height = 52, Color = "#3BC97A", Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            var plat = B(BehaviorKind.Platformer); plat.Nums["speed"] = 240; plat.Nums["jump"] = 620; pl.Behaviors.Add(plat);
            pl.Behaviors.Add(B(BehaviorKind.FollowCamera));
            var flash = Light("beam", "#FFF2C0", 360, 1, direction: "facing"); flash.Nums["spread"] = 40;
            pl.Behaviors.Add(flash);
            baseL.Objects.Add(Id(pl));

            var ctl = Box("Controller", 0, 0, 4, 4, "#00000000");
            ctl.Behaviors.Add(Script(
                "[{\"when\":\"expr\",\"expr\":\"Player.facing < 0\",\"do\":\"setvar\",\"var\":\"wentLeft\",\"to\":1}," +
                "{\"when\":\"expr\",\"expr\":\"wentLeft >= 1 && Player.facing > 0\",\"do\":\"setvar\",\"var\":\"backRight\",\"to\":1}," +
                "{\"when\":\"expr\",\"expr\":\"Player.x > 600\",\"do\":\"setvar\",\"var\":\"sawWindow\",\"to\":1}," +
                "{\"when\":\"keypress\",\"key\":\"Y\",\"do\":\"setvar\",\"var\":\"confirmLook\",\"to\":1}," +
                "{\"when\":\"keypress\",\"key\":\"R\",\"do\":\"goscene\",\"scene\":\"" + LightShapesScene + "\"}]"));
            baseL.Objects.Add(Id(ctl));

            var panel = Box("HelpPanel", 12, 62, 640, 84, "#B0101018"); panel.CornerRadius = 8;
            ui.Objects.Add(Id(panel));
            ui.Objects.Add(Id(Label("Help1", 24, 68, "LIGHT SHAPES TEST   A / D walk: your flashlight points where you face", "#FFFFFF", 14)));
            ui.Objects.Add(Id(Label("Help2", 24, 90, "Window = area · pink neon = bar (pulses) · wall lamp = half circle · torch flickers · alarm blinks", "#FFFFFF", 13)));
            ui.Objects.Add(Id(Label("Help3", 24, 112, "Looks right? Y  ·  F1 console  ·  R restart", "#FFD86B", 13)));

            var checks = Box("Checks", 0, 0, 4, 4, "#00000000");
            var cl = B(BehaviorKind.Checklist);
            cl.Texts["title"] = "Light shapes test";
            cl.Texts["items"] = string.Join("\n",
                "Walk left: the flashlight turns with you => wentLeft >= 1",
                "Walk back right: it turns back => backRight >= 1",
                "Reach the lit window => sawWindow >= 1",
                "It all looks right — press Y => confirmLook >= 1");
            cl.Nums["width"] = 320;
            checks.Behaviors.Add(cl);
            ui.Objects.Add(Id(checks));

            scene.Layers.Add(bg); scene.Layers.Add(baseL); scene.Layers.Add(ui);
            return scene;
        }
    }
}
