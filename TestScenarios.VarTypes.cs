using static EasyEngine.DemoKit;

namespace EasyEngine
{
    public static partial class TestScenarios
    {
        // ============================================================ VARIABLE TYPES TEST
        // A sign shows a Text variable ("Hello {playerName}!"), N renames the player, T switches a Yes/No lamp on and off.
        private static Scene VariableTypesTest()
        {
            var scene = new Scene { Name = "Variable Types", Width = 1200, Height = 600, BackgroundColor = "#1E2633", AmbientLight = 1 };
            var vars = VarScope.Of(scene);
            vars.SetText("playerName", "Hero");
            vars.SetYesNo("lampOn", false);
            foreach (var v in new[] { "sawHero", "renamed", "lampWasOn", "lampOffAgain", "confirmLook" }) vars.SetNumber(v, 0);

            var baseL = new Layer { Name = "Base", Kind = LayerKind.Base };
            var ui = new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true };
            int id = 1;
            GameObjectData Id(GameObjectData o) { o.Id = id++; return o; }

            var ground = Box("Ground", 0, 500, 1200, 100, "#3A3A46");
            ground.Physics.Mode = PhysicsMode.Static; ground.Behaviors.Add(B(BehaviorKind.Solid));
            baseL.Objects.Add(Id(ground));

            var pl = new GameObjectData { Name = "Player", Type = "Player", X = 160, Y = 440, Width = 34, Height = 52, Color = "#3BC97A", Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            pl.Behaviors.Add(B(BehaviorKind.Platformer));
            baseL.Objects.Add(Id(pl));

            // the sign: a Label that shows the Text variable
            var post = Box("SignPost", 590, 420, 12, 80, "#7A5A3A");
            baseL.Objects.Add(Id(post));
            var sign = DemoKit.Label("Sign", 470, 360, "Hello {playerName}!", "#FFFFFF", 22, width: 260);
            sign.Color = "#7A5A3A"; sign.CornerRadius = 6;
            sign.Behaviors[0].Texts["align"] = "center";
            baseL.Objects.Add(Id(sign));

            // the lamp: its Label shows the Yes/No (1 = on)
            baseL.Objects.Add(Id(DemoKit.Label("Lamp", 820, 400, "Lamp on: {lampOn}", "#FFD86B", 18, width: 220)));

            // the rules: keys change the variables; conditions on them tick the checklist
            var ctl = Box("Controller", 0, 0, 4, 4, "#00000000");
            ctl.Behaviors.Add(Script(
                "[{\"when\":\"iftext\",\"var\":\"playerName\",\"op\":\"is\",\"value\":\"Hero\",\"do\":\"setvar\",\"var\":\"sawHero\",\"to\":1}," +
                "{\"when\":\"keypress\",\"key\":\"N\",\"do\":\"settext\",\"var\":\"playerName\",\"to\":\"Ana\"}," +
                "{\"when\":\"iftext\",\"var\":\"playerName\",\"op\":\"is\",\"value\":\"Ana\",\"do\":\"setvar\",\"var\":\"renamed\",\"to\":1}," +
                "{\"when\":\"keypress\",\"key\":\"T\",\"do\":\"toggle\",\"var\":\"lampOn\"}," +
                "{\"when\":\"ifvar\",\"var\":\"lampOn\",\"op\":\">=\",\"value\":1,\"do\":\"setvar\",\"var\":\"lampWasOn\",\"to\":1}," +
                "{\"when\":\"ifvar\",\"var\":\"lampOn\",\"op\":\"<\",\"value\":1,\"cond\":\"lampWasOn >= 1\",\"do\":\"setvar\",\"var\":\"lampOffAgain\",\"to\":1}," +
                "{\"when\":\"keypress\",\"key\":\"Y\",\"do\":\"setvar\",\"var\":\"confirmLook\",\"to\":1}," +
                "{\"when\":\"keypress\",\"key\":\"R\",\"do\":\"goscene\",\"scene\":\"Variable Types\"}]"));
            baseL.Objects.Add(Id(ctl));

            var panel = Box("HelpPanel", 12, 62, 600, 84, "#B0101018"); panel.CornerRadius = 8;
            ui.Objects.Add(Id(panel));
            ui.Objects.Add(Id(Label("Help1", 24, 68, "VARIABLE TYPES   The sign shows a Text variable: Hello {playerName}!", "#FFFFFF", 14)));
            ui.Objects.Add(Id(Label("Help2", 24, 90, "N = rename the player to Ana  ·  T = switch the lamp (a Yes/No) on / off", "#FFFFFF", 13)));
            ui.Objects.Add(Id(Label("Help3", 24, 110, "Looks right? Y  ·  F1 console  ·  R restart", "#FFFFFF", 13)));

            var checks = Box("Checks", 0, 0, 4, 4, "#00000000");
            var cl = B(BehaviorKind.Checklist);
            cl.Texts["title"] = "Variable types";
            cl.Texts["items"] = string.Join("\n",
                "The sign greets Hero => sawHero >= 1",
                "Press N: the name becomes Ana => renamed >= 1",
                "Press T: the lamp switches on => lampWasOn >= 1",
                "Press T again: it switches off => lampOffAgain >= 1",
                "The sign and the lamp look right — press Y => confirmLook >= 1");
            cl.Nums["width"] = 320;
            checks.Behaviors.Add(cl);
            ui.Objects.Add(Id(checks));

            scene.Layers.Add(new Layer { Name = "Background", Kind = LayerKind.Background });
            scene.Layers.Add(baseL); scene.Layers.Add(ui);
            return scene;
        }
    }
}
