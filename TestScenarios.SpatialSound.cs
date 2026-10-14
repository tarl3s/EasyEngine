using static EasyEngine.DemoKit;

namespace EasyEngine
{
    public static partial class TestScenarios
    {
        // ============================================================ SPATIAL SOUND TEST
        // Two radios far apart, each with a looping Sound "Only heard nearby" (600 px) and "Left / right in the headphones":
        // the left one plays the tune, the right one the same tune lower. Walk next to each, then to the middle where
        // neither is heard; with headphones the radio on your left is in your left ear. Y confirms what you heard; R restarts.
        public const string SpatialSoundScene = "Spatial Sound";
        public const double SpatialRadius = 600;

        private static Scene SpatialSoundTest(Project p)
        {
            var tune = ProjectIO.SaveSound(p, SoundSynth.Tune(), "radio_left");
            var low = ProjectIO.SaveSound(p, AudioEdit.ToWav16(AudioEdit.ShiftPitch(AudioEdit.ReadWav(SoundSynth.Tune()), -7)), "radio_right");
            var scene = new Scene { Name = SpatialSoundScene, Width = 2400, Height = 600, BackgroundColor = "#1F2A36", AmbientLight = 1 };
            var vars = VarScope.Of(scene);
            foreach (var v in new[] { "nearLeft", "nearRight", "farBoth", "confirmEars" }) vars.SetNumber(v, 0);

            var baseL = new Layer { Name = "Base", Kind = LayerKind.Base };
            var ui = new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true };
            int id = 1;
            GameObjectData Id(GameObjectData o) { o.Id = id++; return o; }

            var ground = Box("Ground", 0, 500, 2400, 100, "#3A3A46");
            ground.Physics.Mode = PhysicsMode.Static; ground.Behaviors.Add(B(BehaviorKind.Solid));
            baseL.Objects.Add(Id(ground));

            // a radio: a looping Sound heard only within 600 px, in the ear on its side; its circle shows how far it reaches
            GameObjectData Radio(string name, double x, string sound, string color)
            {
                var r = Box(name, x, 440, 70, 60, color); r.CornerRadius = 8;
                var s = B(BehaviorKind.SoundEmitter);
                s.Texts["asset"] = sound; s.Texts["channel"] = "music"; s.Nums["loop"] = 1; s.Nums["volume"] = 0.7;
                s.Nums["range"] = 1; s.Nums["radius"] = SpatialRadius; s.Nums["pan"] = 1; s.Texts["listener"] = "Player";
                r.Behaviors.Add(s);
                return r;
            }
            baseL.Objects.Add(Id(Radio("RadioLeft", 265, tune, "#8A5A3A")));
            baseL.Objects.Add(Id(Radio("RadioRight", 2065, low, "#3A6A8A")));
            baseL.Objects.Add(Id(DemoKit.Label("LeftText", 200, 400, "♪ tune · heard up to 600 px", "#FFD86B", 14, width: 220)));
            baseL.Objects.Add(Id(DemoKit.Label("RightText", 1990, 400, "♪ low tune · heard up to 600 px", "#8FD3FF", 14, width: 240)));
            baseL.Objects.Add(Id(DemoKit.Label("MiddleText", 1110, 400, "too far from both: silence", "#C8C8D0", 14, width: 200)));

            var pl = new GameObjectData { Name = "Player", Type = "Player", X = 1180, Y = 440, Width = 34, Height = 52, Color = "#3BC97A", Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            var plat = B(BehaviorKind.Platformer); plat.Nums["speed"] = 320; pl.Behaviors.Add(plat);
            pl.Behaviors.Add(B(BehaviorKind.FollowCamera));
            baseL.Objects.Add(Id(pl));

            var ctl = Box("Controller", 0, 0, 4, 4, "#00000000");
            ctl.Behaviors.Add(Script(
                "[{\"when\":\"expr\",\"expr\":\"dist(Player.cx,Player.cy,RadioLeft.cx,RadioLeft.cy) < 150\",\"do\":\"setvar\",\"var\":\"nearLeft\",\"to\":1}," +
                "{\"when\":\"expr\",\"expr\":\"dist(Player.cx,Player.cy,RadioRight.cx,RadioRight.cy) < 150\",\"do\":\"setvar\",\"var\":\"nearRight\",\"to\":1}," +
                "{\"when\":\"expr\",\"expr\":\"nearLeft >= 1 && nearRight >= 1 && dist(Player.cx,Player.cy,RadioLeft.cx,RadioLeft.cy) > 600 && dist(Player.cx,Player.cy,RadioRight.cx,RadioRight.cy) > 600\",\"do\":\"setvar\",\"var\":\"farBoth\",\"to\":1}," +
                "{\"when\":\"keypress\",\"key\":\"Y\",\"do\":\"setvar\",\"var\":\"confirmEars\",\"to\":1}," +
                "{\"when\":\"keypress\",\"key\":\"R\",\"do\":\"goscene\",\"scene\":\"" + SpatialSoundScene + "\"}]"));
            baseL.Objects.Add(Id(ctl));

            var panel = Box("HelpPanel", 12, 62, 640, 84, "#B0101018"); panel.CornerRadius = 8;
            ui.Objects.Add(Id(panel));
            ui.Objects.Add(Id(Label("Help1", 24, 68, "SPATIAL SOUND   Two radios, each heard only nearby, left / right by position", "#FFFFFF", 14)));
            ui.Objects.Add(Id(Label("Help2", 24, 90, "Put headphones on and walk: the radio on your left is in your left ear", "#FFFFFF", 13)));
            ui.Objects.Add(Id(Label("Help3", 24, 110, "Heard it right? Y  ·  F1 console  ·  R restart", "#FFFFFF", 13)));

            var checks = Box("Checks", 0, 0, 4, 4, "#00000000");
            var cl = B(BehaviorKind.Checklist);
            cl.Texts["title"] = "Spatial sound";
            cl.Texts["items"] = string.Join("\n",
                "Walk next to the left radio: its tune is loud => nearLeft >= 1",
                "Walk next to the right radio: the low tune => nearRight >= 1",
                "Back in the middle, far from both: silence => farBoth >= 1",
                "The left radio was in your left ear, the right one in your right ear — press Y => confirmEars >= 1");
            cl.Nums["width"] = 340;
            checks.Behaviors.Add(cl);
            ui.Objects.Add(Id(checks));

            scene.Layers.Add(new Layer { Name = "Background", Kind = LayerKind.Background });
            scene.Layers.Add(baseL); scene.Layers.Add(ui);
            return scene;
        }
    }
}
