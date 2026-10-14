using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace EasyEngine;

// A Sound heard only nearby (range + radius) and placed left / right in the headphones (pan): the pure math
// (SpatialSound.cs), the volume and balance a Sound plays at while the player walks around it (read through test hooks —
// nothing is heard in tests), older projects (no new keys) sounding exactly as before, the catalog's picked fields and the
// AI's loose words for it.
public partial class App
{
    partial void FeatureTestsSpatialSound(FullTestKit t)
    {
        static GameObjectData Obj(int id, string name, double x, double y, double w = 40, double h = 40, PhysicsMode m = PhysicsMode.None)
            => new() { Id = id, Name = name, X = x, Y = y, Width = w, Height = h, Physics = new PhysicsData { Mode = m } };
        static bool Near(double a, double b, double eps = 0.01) => Math.Abs(a - b) <= eps;

        // a floor, the player and a "Radio" (centre x 720) whose Sound is set up by the test; "Hero" is another object
        PlayWindow World(Action<Project, BehaviorData> setup, bool fullCatalog = true)
        {
            var pw = t.Sim("[]", PhysicsMode.None, (p, s) =>
            {
                var layer = s.Layers.First(l => l.Kind == LayerKind.Base);
                var floor = Obj(2, "Floor", 0, 300, 3000, 40, PhysicsMode.Static); floor.Behaviors.Add(new BehaviorData { Kind = BehaviorKind.Solid });
                layer.Objects.Add(floor);
                var player = Obj(3, "Player", 676, 236, 48, 64, PhysicsMode.Dynamic); player.Type = "Player";
                player.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Platformer).CreateData());
                layer.Objects.Add(player);
                layer.Objects.Add(Obj(5, "Hero", 1500, 236, 40, 64));
                var radio = Obj(4, "Radio", 700, 236, 40, 64);
                BehaviorData snd;
                if (fullCatalog) snd = BehaviorCatalog.Info(BehaviorKind.SoundEmitter).CreateData();
                else snd = new BehaviorData { Kind = BehaviorKind.SoundEmitter }; // an older project: none of the new keys
                snd.Texts["asset"] = ProjectIO.SaveSound(p, SoundSynth.Tune(), "spatial_tune");
                snd.Nums["loop"] = 1; snd.Nums["volume"] = 0.8;
                setup(p, snd);
                radio.Behaviors.Add(snd);
                layer.Objects.Add(radio);
            });
            pw.TSteps(3);
            return pw;
        }
        // the player's centre at that x (the radio's centre is x 720), then time to glide there
        static void PlayerAt(PlayWindow pw, double cx, int steps = 45) { pw.TPlace("Player", cx - 24, pw.TY("Player")); pw.TSteps(steps); pw.TPlace("Player", cx - 24, pw.TY("Player")); pw.TSteps(1); }

        // ---------------- the math ----------------
        t.Test("spatial sound math: full volume near (up to a quarter of the radius), 0 at and past the radius, half way down the curve in its middle", () =>
            SpatialSound.Falloff(0, 600) == 1 && SpatialSound.Falloff(150, 600) == 1 && SpatialSound.Falloff(600, 600) == 0 && SpatialSound.Falloff(5000, 600) == 0
            && Near(SpatialSound.Falloff(375, 600), 0.5, 1e-9) && SpatialSound.Falloff(300, 600) > 0.5 && SpatialSound.Falloff(500, 600) < 0.5);
        t.Test("spatial sound math: the falloff never gets louder going away (monotonic) and stays within 0..1", () =>
        {
            double prev = 2;
            for (double d = 0; d <= 1500; d += 3)
            {
                double v = SpatialSound.Falloff(d, 900);
                if (v > prev + 1e-12 || v < 0 || v > 1) return false;
                prev = v;
            }
            return true;
        });
        t.Test("spatial sound math: balance — a sound on the right is in the right ear (+), on the left in the left ear (−), clamped to ±1, centred when no scale", () =>
            Near(SpatialSound.Balance(200, 400), 0.5, 1e-9) && Near(SpatialSound.Balance(-100, 400), -0.25, 1e-9) && SpatialSound.Balance(5000, 400) == 1
            && SpatialSound.Balance(-5000, 400) == -1 && SpatialSound.Balance(0, 400) == 0 && SpatialSound.Balance(100, 0) == 0 && SpatialSound.Balance(double.NaN, 400) == 0);
        t.Test("spatial sound math: Mix — no radius = full volume; pan off = centred; pan scales by the radius, else by half the screen", () =>
            SpatialSound.Mix(2000, 0, 0, false, 480) == (1, 0) && SpatialSound.Mix(-300, 0, 600, false, 480).balance == 0
            && Near(SpatialSound.Mix(300, 0, 600, true, 480).balance, 0.5, 1e-9) && Near(SpatialSound.Mix(240, 0, 0, true, 480).balance, 0.5, 1e-9)
            && Near(SpatialSound.Mix(300, 400, 1000, false, 480).volume, SpatialSound.Falloff(500, 1000), 1e-9));

        // ---------------- the catalog ----------------
        t.Test("spatial sound catalog: range and pan default off; 'Heard up to' is picked from a list and shows only with range on; 'Heard by' (an object, the Player) with range or pan", () =>
        {
            var info = BehaviorCatalog.Info(BehaviorKind.SoundEmitter);
            var d = info.CreateData();
            var radius = info.Nums.First(n => n.Key == "radius");
            var listener = info.Texts.First(x => x.Key == "listener");
            var scene = new Scene();
            bool Shown(Func<ParamCtx, bool>? f, BehaviorData b) => f == null || f(new ParamCtx(b, new GameObjectData(), scene));
            var on = info.CreateData(); on.Nums["range"] = 1;
            var panned = info.CreateData(); panned.Nums["pan"] = 1;
            return d.Num("range", -1) == 0 && d.Num("pan", -1) == 0 && d.Text("listener", "") == "Player"
                   && radius.Options is { Length: >= 4 } && radius.Options.Any(o => o.StartsWith("600|")) && radius.Default == 600
                   && listener.Pick == PickKind.Object && info.Nums.First(n => n.Key == "range").Label.Contains("(0/1)") && info.Nums.First(n => n.Key == "pan").Label.Contains("(0/1)")
                   && !Shown(radius.ShowIf, d) && Shown(radius.ShowIf, on) && !Shown(listener.ShowIf, d) && Shown(listener.ShowIf, on) && Shown(listener.ShowIf, panned) && !Shown(radius.ShowIf, panned)
                   && info.Desc.Contains("nearby") && info.Desc.Contains("headphones") && BehaviorGuides.For(BehaviorKind.SoundEmitter) is string g && g.Contains("Heard up to");
        });

        // ---------------- playing ----------------
        t.Test("spatial sound off (the default): the volume is its own and it is centred wherever the player is — as before", () =>
        {
            var pw = World((p, s) => { });
            var a = pw.TSoundMix("Radio");
            PlayerAt(pw, 720 + 1200); var b = pw.TSoundMix("Radio");
            PlayerAt(pw, 720 + 2200); var c = pw.TSoundMix("Radio");
            return pw.TSoundPlaying("Radio") && a == (0.8, 0) && b == (0.8, 0) && c == (0.8, 0) && !pw.TLogHas("left/right");
        });
        t.Test("spatial sound: an older project's Sound (no range / pan / radius / listener keys) plays at its volume, centred", () =>
        {
            var pw = World((p, s) => { }, fullCatalog: false);
            PlayerAt(pw, 100); var a = pw.TSoundMix("Radio");
            return pw.TSoundPlaying("Radio") && a == (0.8, 0);
        });
        t.Test("spatial sound range: next to it = full volume, far away = silent but still playing, half way = in between; walking back brings it in", () =>
        {
            var pw = World((p, s) => { s.Nums["range"] = 1; s.Nums["radius"] = 600; });
            bool start = Near(pw.TSoundMix("Radio").volume, 0.8);                 // the player starts on it
            PlayerAt(pw, 720 - 100); bool near = Near(pw.TSoundMix("Radio").volume, 0.8);
            PlayerAt(pw, 720 - 375); double mid = pw.TSoundMix("Radio").volume;
            PlayerAt(pw, 720 + 900); double far = pw.TSoundMix("Radio").volume; bool stillPlaying = pw.TSoundPlaying("Radio");
            PlayerAt(pw, 720 - 50); bool back = Near(pw.TSoundMix("Radio").volume, 0.8);
            return start && near && Near(mid, 0.4, 0.03) && far < 0.005 && stillPlaying && back && pw.TSoundMix("Radio").balance == 0;
        });
        t.Test("spatial sound pan: the radio on the player's right is in the right ear, on the left in the left ear, centred on top of it; fully on one side at the radius", () =>
        {
            var pw = World((p, s) => { s.Nums["range"] = 1; s.Nums["radius"] = 600; s.Nums["pan"] = 1; });
            PlayerAt(pw, 720 - 300); double right = pw.TSoundMix("Radio").balance; // the player on the left → the radio on the right
            PlayerAt(pw, 720 + 300); double left = pw.TSoundMix("Radio").balance;
            PlayerAt(pw, 720); double centre = pw.TSoundMix("Radio").balance;
            PlayerAt(pw, 720 + 1000); double edge = pw.TSoundMix("Radio").balance;
            return Near(right, 0.5, 0.03) && Near(left, -0.5, 0.03) && Math.Abs(centre) < 0.02 && edge < -0.97 && edge >= -1;
        });
        t.Test("spatial sound pan without range: heard everywhere at full volume, the side scaled by half the screen", () =>
        {
            var pw = World((p, s) => { s.Nums["pan"] = 1; });
            double half = pw.TViewWidth() / 2;
            PlayerAt(pw, 720 - half / 2); var m = pw.TSoundMix("Radio");
            PlayerAt(pw, 720 + 2200); var far = pw.TSoundMix("Radio");
            return Near(m.volume, 0.8) && Near(m.balance, 0.5, 0.03) && Near(far.volume, 0.8) && far.balance < -0.97;
        });
        t.Test("spatial sound: 'Heard by' another object — the distance is measured from it, not from the player", () =>
        {
            var pw = World((p, s) => { s.Nums["range"] = 1; s.Nums["radius"] = 400; s.Nums["pan"] = 1; s.Texts["listener"] = "Hero"; });
            PlayerAt(pw, 720); // the player on top of the radio; Hero is 800 px away on its right
            var m = pw.TSoundMix("Radio");
            return m.volume < 0.005 && m.balance < -0.97;
        });
        t.Test("spatial sound: 'Heard by' an object that isn't there — heard from the middle of the screen, no error", () =>
        {
            var pw = World((p, s) => { s.Nums["range"] = 1; s.Nums["radius"] = 1400; s.Nums["pan"] = 1; s.Texts["listener"] = "Nobody"; });
            pw.TSteps(40);
            var m = pw.TSoundMix("Radio");
            double cx = pw.TViewWidth() / 2; // the camera hasn't moved in a headless test
            return !double.IsNaN(m.volume) && m.volume > 0 && m.volume <= 0.8 && Near(m.balance, Math.Clamp((720 - cx) / 1400, -1, 1), 0.03) && !pw.TLogHas("rror");
        });
        t.Test("spatial sound one-shot (no loop): set once when it starts — far away it starts silent, panned, and the log says left/right", () =>
        {
            var pw = World((p, s) => { s.Nums["loop"] = 0; s.Nums["range"] = 1; s.Nums["radius"] = 200; s.Nums["pan"] = 1; s.Texts["listener"] = "Hero"; });
            var m = pw.TSoundMix("Radio");
            return m.volume < 0.005 && m.balance == -1 && pw.TLogHas("left/right -1") && pw.TSounds.Contains("spatial_tune.wav");
        });
        t.Test("spatial sound + 'Only while': it stops and starts again with its formula, placed where it is when it starts", () =>
        {
            var pw = World((p, s) => { s.Nums["range"] = 1; s.Nums["radius"] = 600; s.Nums["pan"] = 1; s.Texts["cond"] = "radioOn >= 1"; });
            bool silentAtStart = !pw.TSoundPlaying("Radio");
            PlayerAt(pw, 720 + 300); pw.TSetVar("radioOn", 1); pw.TSteps(2);
            var m = pw.TSoundMix("Radio");
            bool on = pw.TSoundPlaying("Radio") && m.balance < -0.4 && m.volume > 0.2 && m.volume < 0.8;
            pw.TSetVar("radioOn", 0); pw.TSteps(2);
            return silentAtStart && on && !pw.TSoundPlaying("Radio");
        });

        // ---------------- the AI's words ----------------
        JsonObject Fix(string json)
        {
            var item = JsonNode.Parse(json)!.AsObject();
            AiSpecFix.Behavior(BehaviorCatalog.Info(BehaviorKind.SoundEmitter), item, new List<string>());
            return item;
        }
        static double N(JsonObject item, string k) => item["nums"]?[k] is JsonNode n && LooseJson.TryParse(n.ToJsonString().Trim('"'), out var d) ? d : double.NaN;
        t.Test("AI fix: a Sound written loosely — stereo:true, maxDistance:500, heardBy:Hero — becomes pan 1, range 1, radius 500, listener Hero", () =>
        {
            var it = Fix("{\"kind\":\"SoundEmitter\",\"stereo\":true,\"nums\":{\"maxDistance\":500},\"texts\":{\"heardBy\":\"Hero\"}}");
            return N(it, "pan") == 1 && N(it, "range") == 1 && N(it, "radius") == 500 && (string?)it["texts"]?["listener"] == "Hero"
                   && it["stereo"] == null && it["nums"]?["maxDistance"] == null;
        });
        t.Test("AI fix: a Sound with \"range\":800 (a distance) → range on, radius 800; \"3d\":\"yes\" → heard nearby and left/right; a radius alone turns range on", () =>
        {
            var a = Fix("{\"kind\":\"SoundEmitter\",\"nums\":{\"range\":800}}");
            var b = Fix("{\"kind\":\"SoundEmitter\",\"texts\":{\"3d\":\"yes\"}}");
            var c = Fix("{\"kind\":\"SoundEmitter\",\"radius\":400}");
            var d = Fix("{\"kind\":\"SoundEmitter\",\"nums\":{\"volume\":0.5,\"loop\":1}}");
            return N(a, "range") == 1 && N(a, "radius") == 800 && N(b, "range") == 1 && N(b, "pan") == 1 && N(c, "range") == 1 && N(c, "radius") == 400
                   && double.IsNaN(N(d, "range")) && double.IsNaN(N(d, "pan"));
        });

        // ---------------- the "Spatial Sound" scene of the EasyEngine Tests project, played step by step ----------------
        foreach (var (step, ok, lines) in RunSpatialSoundE2E())
            t.Test("e2e spatial sound: " + step + (ok ? "" : "  ⟶ " + string.Join(" | ", lines.TakeLast(4))), () => ok);
    }
}
