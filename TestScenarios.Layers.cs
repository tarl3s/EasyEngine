using static EasyEngine.DemoKit;

namespace EasyEngine
{
    public static partial class TestScenarios
    {
        // ============================================================ LAYERS TEST
        // Layers and objects on/off in the game, and layer opacity: the Bridge (an object that is off) and the "Secret"
        // layer (off: two gems and a ledge) are not there at the start; B / N turn the bridge on / off and L / K the whole
        // Secret layer, with the "setactive" rule. The "Mist" layer is drawn at 50% opacity in front of the hero.
        private static Scene LayersTest()
        {
            var scene = new Scene { Name = "Layers Test", Width = 1200, Height = 600, BackgroundColor = "#1E2633", AmbientLight = 1 };
            foreach (var v in new[] { "startOk", "bridgeSeen", "bridgeGone", "secretSeen", "secretGone", "confirmMist" }) scene.Variables[v] = 0;

            var back = new Layer { Name = "Background", Kind = LayerKind.Background };
            var baseL = new Layer { Name = "Base", Kind = LayerKind.Base };
            var mist = new Layer { Name = "Mist", Kind = LayerKind.Custom, Opacity = 0.5 };
            var secret = new Layer { Name = "Secret", Kind = LayerKind.Custom, Active = false };
            var ui = new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true };
            int id = 1;
            GameObjectData Id(GameObjectData o) { o.Id = id++; return o; }
            GameObjectData Solid(string name, double x, double y, double w, double h, string color)
            {
                var o = Box(name, x, y, w, h, color);
                o.Physics.Mode = PhysicsMode.Static; o.Behaviors.Add(B(BehaviorKind.Solid));
                return o;
            }

            back.Objects.Add(Id(Box("Hill", 700, 380, 360, 120, "#2E3A4E")));
            baseL.Objects.Add(Id(Solid("GroundLeft", 0, 500, 520, 100, "#3A3A46")));
            baseL.Objects.Add(Id(Solid("GroundRight", 700, 500, 500, 100, "#3A3A46")));
            var bridge = Solid("Bridge", 520, 500, 180, 24, "#B07A3A"); bridge.Active = false; // off: comes in with B
            baseL.Objects.Add(Id(bridge));

            var hero = new GameObjectData { Name = "Hero", Type = "Player", X = 200, Y = 436, Width = 48, Height = 64, Color = "#3BC97A", Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            hero.Behaviors.Add(B(BehaviorKind.Platformer));
            baseL.Objects.Add(Id(hero));

            var ctl = Box("Controller", 0, 0, 4, 4, "#00000000");
            ctl.Behaviors.Add(Script(
                "[{\"when\":\"start\",\"cond\":\"Bridge.exists < 1 && Gem1.exists < 1 && Fog.exists >= 1\",\"do\":\"setvar\",\"var\":\"startOk\",\"to\":1}," +
                "{\"when\":\"keypress\",\"key\":\"B\",\"do\":\"setactive\",\"target\":\"Bridge\",\"on\":\"on\"}," +
                "{\"when\":\"keypress\",\"key\":\"N\",\"do\":\"setactive\",\"target\":\"Bridge\",\"on\":\"off\"}," +
                "{\"when\":\"keypress\",\"key\":\"L\",\"do\":\"setactive\",\"layer\":\"Secret\",\"on\":\"on\"}," +
                "{\"when\":\"keypress\",\"key\":\"K\",\"do\":\"setactive\",\"layer\":\"Secret\",\"on\":\"off\"}," +
                "{\"when\":\"expr\",\"expr\":\"Bridge.exists >= 1\",\"do\":\"setvar\",\"var\":\"bridgeSeen\",\"to\":1}," +
                "{\"when\":\"expr\",\"expr\":\"bridgeSeen >= 1 && Bridge.exists < 1\",\"do\":\"setvar\",\"var\":\"bridgeGone\",\"to\":1}," +
                "{\"when\":\"expr\",\"expr\":\"Gem1.exists >= 1 && Gem2.exists >= 1\",\"do\":\"setvar\",\"var\":\"secretSeen\",\"to\":1}," +
                "{\"when\":\"expr\",\"expr\":\"secretSeen >= 1 && Gem1.exists < 1 && Gem2.exists < 1\",\"do\":\"setvar\",\"var\":\"secretGone\",\"to\":1}," +
                "{\"when\":\"keypress\",\"key\":\"Y\",\"do\":\"setvar\",\"var\":\"confirmMist\",\"to\":1}," +
                "{\"when\":\"keypress\",\"key\":\"R\",\"do\":\"goscene\",\"scene\":\"Layers Test\"}]"));
            baseL.Objects.Add(Id(ctl));

            mist.Objects.Add(Id(Box("Fog", 120, 360, 300, 140, "#DDE6F0"))); // drawn in front of the hero, at the layer's 50%

            secret.Objects.Add(Id(Solid("Ledge", 820, 380, 160, 20, "#8A5A3A")));
            secret.Objects.Add(Id(Box("Gem1", 860, 330, 28, 28, "#E5484D")));
            secret.Objects.Add(Id(Box("Gem2", 920, 330, 28, 28, "#4DC8E5")));

            var panel = Box("HelpPanel", 12, 62, 660, 104, "#B0101018"); panel.CornerRadius = 8;
            ui.Objects.Add(Id(panel));
            ui.Objects.Add(Id(Label("Help1", 24, 68, "LAYERS   The bridge and the Secret layer are OFF in the game at the start.", "#FFFFFF", 14)));
            ui.Objects.Add(Id(Label("Help2", 24, 90, "B = bridge on · N = bridge off · L = Secret layer on · K = Secret layer off", "#FFFFFF", 13)));
            ui.Objects.Add(Id(Label("Help3", 24, 110, "The pale Mist layer is at 50% opacity: you see the hero through it", "#FFFFFF", 13)));
            ui.Objects.Add(Id(Label("Help4", 24, 130, "Looks right? Y  ·  F1 console  ·  R restart", "#FFFFFF", 13)));

            var checks = Box("Checks", 0, 0, 4, 4, "#00000000");
            var cl = B(BehaviorKind.Checklist);
            cl.Texts["title"] = "Layers on/off and opacity";
            cl.Texts["items"] = string.Join("\n",
                "At the start the bridge and the gems are not there => startOk >= 1",
                "B: the bridge appears => bridgeSeen >= 1",
                "N: the bridge goes away => bridgeGone >= 1",
                "L: the Secret layer comes in (two gems and a ledge) => secretSeen >= 1",
                "K: the whole Secret layer goes away => secretGone >= 1",
                "The mist is see-through (you see the hero behind it) — press Y => confirmMist >= 1");
            cl.Nums["width"] = 360;
            checks.Behaviors.Add(cl);
            ui.Objects.Add(Id(checks));

            scene.Layers.Add(back); scene.Layers.Add(baseL); scene.Layers.Add(mist); scene.Layers.Add(secret); scene.Layers.Add(ui);
            return scene;
        }
    }
}
