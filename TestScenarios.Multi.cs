using static EasyEngine.DemoKit;

namespace EasyEngine
{
    public static partial class TestScenarios
    {
        // ============================================================ RADIO TOGGLE TEST
        // Several behaviors of one kind on one object (BehaviorInfo.Repeatable): the radio has TWO Interacts — the first
        // "only while radioOn < 1" turns it on, the second "only while radioOn >= 1" turns it off — and a looping Sound
        // "only while radioOn >= 1". One press of E changes it exactly once; the music plays only while it is on.
        private static Scene RadioToggleTest(Project p)
        {
            var tune = ProjectIO.SaveSound(p, SoundSynth.Tune(), "radio_tune");
            var scene = new Scene { Name = "Radio Toggle", Width = 1200, Height = 600, BackgroundColor = "#26303F", AmbientLight = 1 };
            var vars = VarScope.Of(scene);
            foreach (var v in new[] { "radioOn", "uses", "radioWasOn", "radioOffAgain", "onAgain", "confirmMusic" }) vars.SetNumber(v, 0);

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

            // the radio: Interact 1 turns it on, Interact 2 turns it off, the Sound plays while it is on
            var radio = Box("Radio", 700, 440, 70, 60, "#8A5A3A"); radio.CornerRadius = 8;
            var on = B(BehaviorKind.Interact);
            on.Texts["action"] = "setvar"; on.Texts["var"] = "radioOn"; on.Nums["to"] = 1; on.Nums["radius"] = 90; on.Texts["cond"] = "radioOn < 1";
            var off = B(BehaviorKind.Interact);
            off.Texts["action"] = "setvar"; off.Texts["var"] = "radioOn"; off.Nums["to"] = 0; off.Nums["radius"] = 90; off.Texts["cond"] = "radioOn >= 1";
            var music = B(BehaviorKind.SoundEmitter);
            music.Texts["asset"] = tune; music.Texts["channel"] = "music"; music.Nums["loop"] = 1; music.Nums["volume"] = 0.5; music.Texts["cond"] = "radioOn >= 1";
            radio.Behaviors.Add(on); radio.Behaviors.Add(off); radio.Behaviors.Add(music);
            radio.Behaviors.Add(Script("[{\"when\":\"interact\",\"do\":\"addvar\",\"var\":\"uses\",\"amount\":1}]")); // "When someone interacts with it"
            baseL.Objects.Add(Id(radio));
            baseL.Objects.Add(Id(DemoKit.Label("RadioText", 620, 390, "Radio on: {radioOn}  ·  presses: {uses}", "#FFD86B", 16, width: 230)));

            var ctl = Box("Controller", 0, 0, 4, 4, "#00000000");
            ctl.Behaviors.Add(Script(
                "[{\"when\":\"ifvar\",\"var\":\"radioOn\",\"op\":\">=\",\"value\":1,\"do\":\"setvar\",\"var\":\"radioWasOn\",\"to\":1}," +
                "{\"when\":\"ifvar\",\"var\":\"radioOn\",\"op\":\"<\",\"value\":1,\"cond\":\"radioWasOn >= 1\",\"do\":\"setvar\",\"var\":\"radioOffAgain\",\"to\":1}," +
                "{\"when\":\"ifvar\",\"var\":\"uses\",\"op\":\">=\",\"value\":3,\"cond\":\"radioOn >= 1\",\"do\":\"setvar\",\"var\":\"onAgain\",\"to\":1}," +
                "{\"when\":\"keypress\",\"key\":\"Y\",\"do\":\"setvar\",\"var\":\"confirmMusic\",\"to\":1}," +
                "{\"when\":\"keypress\",\"key\":\"R\",\"do\":\"goscene\",\"scene\":\"Radio Toggle\"}]"));
            baseL.Objects.Add(Id(ctl));

            var panel = Box("HelpPanel", 12, 62, 600, 84, "#B0101018"); panel.CornerRadius = 8;
            ui.Objects.Add(Id(panel));
            ui.Objects.Add(Id(Label("Help1", 24, 68, "RADIO TOGGLE   The radio has two Interacts and a Sound", "#FFFFFF", 14)));
            ui.Objects.Add(Id(Label("Help2", 24, 90, "Walk to it and press E: on (music)  ·  E again: off (silence)", "#FFFFFF", 13)));
            ui.Objects.Add(Id(Label("Help3", 24, 110, "Heard it right? Y  ·  F1 console  ·  R restart", "#FFFFFF", 13)));

            var checks = Box("Checks", 0, 0, 4, 4, "#00000000");
            var cl = B(BehaviorKind.Checklist);
            cl.Texts["title"] = "Radio toggle";
            cl.Texts["items"] = string.Join("\n",
                "Press E next to the radio: it turns on => radioWasOn >= 1",
                "Press E again: it turns off => radioOffAgain >= 1",
                "Press E a third time: on again (one change per press) => onAgain >= 1",
                "The music plays only while it is on — press Y => confirmMusic >= 1");
            cl.Nums["width"] = 320;
            checks.Behaviors.Add(cl);
            ui.Objects.Add(Id(checks));

            scene.Layers.Add(new Layer { Name = "Background", Kind = LayerKind.Background });
            scene.Layers.Add(baseL); scene.Layers.Add(ui);
            return scene;
        }
    }
}
