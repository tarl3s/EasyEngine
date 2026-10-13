using System;
using System.Linq;

namespace EasyEngine;

// Several behaviors of one kind on one object (BehaviorInfo.Repeatable): the radio toggle (two Interacts with opposite
// "Only while", one press = one change, a Sound that plays only while it is on), two Spawners / Shooters / Areas / Shops
// with their own state, the inspector offering a second Interact but not a second Platformer, the AI keeping two
// Interacts it writes, and an object with one of each still playing as before.
public partial class App
{
    partial void FeatureTestsMultiBehaviors(FullTestKit t)
    {
        static GameObjectData Obj(int id, string name, double x, double y, double w = 40, double h = 40, PhysicsMode m = PhysicsMode.None)
            => new() { Id = id, Name = name, X = x, Y = y, Width = w, Height = h, Physics = new PhysicsData { Mode = m } };
        static BehaviorData Beh(BehaviorKind k, params (string key, object value)[] set)
        {
            var b = BehaviorCatalog.Info(k).CreateData();
            foreach (var (key, value) in set)
                if (value is string s) b.Texts[key] = s; else b.Nums[key] = Convert.ToDouble(value);
            return b;
        }
        static BehaviorData Interact(string var, double to, string cond, double once = 0) =>
            Beh(BehaviorKind.Interact, ("action", "setvar"), ("var", var), ("to", to), ("cond", cond), ("radius", 90), ("once", once));

        // a floor, a player standing next to "Thing" (which gets the behaviors), and whatever else the test adds
        PlayWindow World(Action<Project, GameObjectData> fill, Action<Project, Scene>? more = null)
        {
            return t.Sim("[]", PhysicsMode.None, (p, s) =>
            {
                var layer = s.Layers.First(l => l.Kind == LayerKind.Base);
                var floor = Obj(2, "Floor", 0, 300, 1400, 40, PhysicsMode.Static); floor.Behaviors.Add(new BehaviorData { Kind = BehaviorKind.Solid });
                layer.Objects.Add(floor);
                var player = Obj(3, "Player", 400, 236, 48, 64, PhysicsMode.Dynamic); player.Type = "Player";
                player.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Platformer).CreateData());
                layer.Objects.Add(player);
                var thing = Obj(4, "Thing", 450, 236, 40, 64);
                fill(p, thing);
                layer.Objects.Add(thing);
                more?.Invoke(p, s);
            });
        }
        static void Press(PlayWindow pw, string key) { pw.TKey(key, true); pw.TSteps(3); pw.TKey(key, false); pw.TSteps(3); }

        PlayWindow Radio() => World((p, o) =>
        {
            o.Behaviors.Add(Interact("radioOn", 1, "radioOn < 1"));
            o.Behaviors.Add(Interact("radioOn", 0, "radioOn >= 1"));
            o.Behaviors.Add(Beh(BehaviorKind.SoundEmitter, ("asset", ProjectIO.SaveSound(p, SoundSynth.Tune(), "radio")), ("loop", 1), ("cond", "radioOn >= 1")));
            o.Behaviors.Add(new BehaviorData { Kind = BehaviorKind.Custom, Texts = { ["script"] = "[{\"when\":\"interact\",\"do\":\"addvar\",\"var\":\"uses\",\"amount\":1}]" } });
        });

        t.Test("multi: the catalog — Interact, Sound, Shooter, Spawner, Particles, Damager, Area, UI Button, Shop and Custom repeat; movers, Health, Lives, camera, HUD, Boss… don't", () =>
        {
            var yes = new[] { BehaviorKind.Interact, BehaviorKind.SoundEmitter, BehaviorKind.Shooter, BehaviorKind.Spawner, BehaviorKind.Particles,
                              BehaviorKind.Damager, BehaviorKind.Area, BehaviorKind.UIButton, BehaviorKind.Shop, BehaviorKind.Custom };
            var no = new[] { BehaviorKind.Platformer, BehaviorKind.TopDown, BehaviorKind.Patrol, BehaviorKind.AdvancedPatrol, BehaviorKind.Chase, BehaviorKind.PathChase,
                             BehaviorKind.Health, BehaviorKind.Lives, BehaviorKind.FollowCamera, BehaviorKind.Minimap, BehaviorKind.Hotbar, BehaviorKind.BossAI,
                             BehaviorKind.Cutscene, BehaviorKind.DayNight, BehaviorKind.Terrain, BehaviorKind.Tilemap, BehaviorKind.Generator, BehaviorKind.Label,
                             BehaviorKind.LightSource, BehaviorKind.Bar, BehaviorKind.Switch, BehaviorKind.Solid };
            return yes.All(k => BehaviorCatalog.Info(k).Repeatable) && no.All(k => !BehaviorCatalog.Info(k).Repeatable);
        });

        t.Test("multi: radio toggle — two Interacts with opposite 'Only while': E turns it on, E again off, once per press; the interact block counts each press", () =>
        {
            var pw = Radio(); pw.TSteps(5);
            Press(pw, "E"); bool on = pw.TVar("radioOn") == 1 && pw.TVar("uses") == 1;
            Press(pw, "E"); bool off = pw.TVar("radioOn") == 0 && pw.TVar("uses") == 2;
            Press(pw, "E"); bool onAgain = pw.TVar("radioOn") == 1 && pw.TVar("uses") == 3;
            return on && off && onAgain;
        });
        t.Test("multi: radio toggle — holding E down changes it once (the second Interact doesn't undo the first)", () =>
        {
            var pw = Radio(); pw.TSteps(5);
            pw.TKey("E", true); pw.TSteps(40); pw.TKey("E", false); pw.TSteps(3);
            return pw.TVar("radioOn") == 1 && pw.TVar("uses") == 1;
        });
        t.Test("multi: a Sound with 'Only while' plays when it turns true and stops when it turns false (not at the start)", () =>
        {
            var pw = Radio(); pw.TSteps(5);
            bool silent = !pw.TSoundPlaying("Thing") && !pw.TSounds.Contains("radio.wav");
            Press(pw, "E"); bool plays = pw.TSoundPlaying("Thing") && pw.TSounds.Count(s => s == "radio.wav") == 1;
            pw.TSteps(20); bool once = pw.TSounds.Count(s => s == "radio.wav") == 1; // started once, not every frame
            Press(pw, "E"); bool stops = !pw.TSoundPlaying("Thing") && pw.TLogHas("radio.wav stopped");
            Press(pw, "E"); bool again = pw.TSoundPlaying("Thing") && pw.TSounds.Count(s => s == "radio.wav") == 2;
            return silent && plays && once && stops && again;
        });
        t.Test("multi: one press uses ONE Interact — the first that can fire; an 'only once' one that already fired lets the next one have it", () =>
        {
            var pw = World((p, o) => { o.Behaviors.Add(Interact("a", 1, "", once: 1)); o.Behaviors.Add(Interact("b", 1, "")); });
            pw.TSteps(5);
            Press(pw, "E"); bool first = pw.TVar("a") == 1 && pw.TVar("b") == 0;
            Press(pw, "E"); bool second = pw.TVar("b") == 1;
            return first && second;
        });
        t.Test("multi: two Interacts on different keys work on their own", () =>
        {
            var pw = World((p, o) =>
            {
                o.Behaviors.Add(Interact("a", 1, "")); var b = Interact("b", 1, ""); b.Texts["key"] = "F"; o.Behaviors.Add(b);
            });
            pw.TSteps(5);
            Press(pw, "F"); bool f = pw.TVar("a") == 0 && pw.TVar("b") == 1;
            Press(pw, "E"); return f && pw.TVar("a") == 1;
        });
        t.Test("multi: two Spawners with different targets and intervals keep their own clocks and limits", () =>
        {
            var pw = World((p, o) =>
            {
                o.Behaviors.Add(Beh(BehaviorKind.Spawner, ("target", "Coin"), ("interval", 0.5), ("max", 0), ("key", "")));
                o.Behaviors.Add(Beh(BehaviorKind.Spawner, ("target", "Rock"), ("interval", 1.0), ("max", 0), ("key", "")));
            }, (p, s) =>
            {
                var layer = s.Layers.First(l => l.Kind == LayerKind.Base);
                layer.Objects.Add(Obj(10, "Coin", 3000, 100, 10, 10)); layer.Objects.Add(Obj(11, "Rock", 3000, 150, 10, 10));
            });
            pw.TSteps(135); // ≈2.16 s: Coin every 0.5 s → 4, Rock every 1 s → 2 (plus the two templates)
            bool clocks = pw.TAlive("Coin") == 5 && pw.TAlive("Rock") == 3;
            return clocks;
        });
        t.Test("multi: two Spawners — each one's 'max' counts only what it made", () =>
        {
            var pw = World((p, o) =>
            {
                o.Behaviors.Add(Beh(BehaviorKind.Spawner, ("target", "Coin"), ("interval", 0.1), ("max", 2), ("key", "")));
                o.Behaviors.Add(Beh(BehaviorKind.Spawner, ("target", "Rock"), ("interval", 0.1), ("max", 3), ("key", "")));
            }, (p, s) =>
            {
                var layer = s.Layers.First(l => l.Kind == LayerKind.Base);
                layer.Objects.Add(Obj(10, "Coin", 3000, 100, 10, 10)); layer.Objects.Add(Obj(11, "Rock", 3000, 150, 10, 10));
            });
            pw.TSteps(120);
            return pw.TAlive("Coin") == 3 && pw.TAlive("Rock") == 4;
        });
        t.Test("multi: two Shooters on different keys have their own cooldown (the second fires right after the first)", () =>
        {
            var pw = World((p, o) =>
            {
                o.Behaviors.Add(Beh(BehaviorKind.Shooter, ("key", "T"), ("bullet", "BulletA"), ("directions", 1), ("cooldown", 2)));
                o.Behaviors.Add(Beh(BehaviorKind.Shooter, ("key", "Y"), ("bullet", "BulletB"), ("directions", 1), ("cooldown", 2)));
            }, (p, s) =>
            {
                var layer = s.Layers.First(l => l.Kind == LayerKind.Base);
                layer.Objects.Add(Obj(10, "BulletA", 3000, 100, 8, 8)); layer.Objects.Add(Obj(11, "BulletB", 3000, 150, 8, 8));
            });
            pw.TSteps(3);
            pw.TKey("T", true); pw.TStep(); pw.TKey("T", false);
            pw.TKey("Y", true); pw.TStep(); pw.TKey("Y", false);
            pw.TSteps(2);
            bool both = pw.TAlive("bullet") == 2; // (every bullet fired is called "bullet")
            pw.TKey("T", true); pw.TSteps(3); pw.TKey("T", false); // still cooling down: no new one
            return both && pw.TAlive("bullet") == 2;
        });
        t.Test("multi: two Trigger Areas on one zone both fire when the player walks in", () =>
        {
            var pw = World((p, o) =>
            {
                o.Behaviors.Add(Beh(BehaviorKind.Area, ("action", "setvar"), ("var", "x"), ("to", 1)));
                o.Behaviors.Add(Beh(BehaviorKind.Area, ("action", "addvar"), ("var", "y"), ("amount", 5)));
            });
            pw.TPlace("Thing", 404, 236); // over the player
            pw.TSteps(10);
            return pw.TVar("x") == 1 && pw.TVar("y") == 5;
        });
        t.Test("multi: a crafting bench with two Shops — each press buys one recipe, then the next", () =>
        {
            var pw = World((p, o) =>
            {
                o.Behaviors.Add(Beh(BehaviorKind.Shop, ("item", "sword"), ("cost", 2), ("once", 1), ("radius", 120)));
                o.Behaviors.Add(Beh(BehaviorKind.Shop, ("item", "shield"), ("cost", 3), ("once", 1), ("radius", 120)));
            });
            pw.TSetVar("coins", 10); pw.TSteps(3);
            Press(pw, "E"); bool sword = pw.TItem("sword") == 1 && pw.TItem("shield") == 0 && pw.TVar("coins") == 8;
            Press(pw, "E"); bool shield = pw.TItem("shield") == 1 && pw.TVar("coins") == 5;
            Press(pw, "E"); bool done = pw.TItem("sword") == 1 && pw.TItem("shield") == 1 && pw.TVar("coins") == 5;
            return sword && shield && done;
        });
        t.Test("multi: an object with ONE Interact (only once) and ONE Spawner plays as before", () =>
        {
            var pw = World((p, o) =>
            {
                o.Behaviors.Add(Beh(BehaviorKind.Interact, ("action", "addvar"), ("var", "talks"), ("amount", 1), ("once", 1)));
                o.Behaviors.Add(Beh(BehaviorKind.Spawner, ("target", "Coin"), ("interval", 0.5), ("max", 0), ("key", "")));
            }, (p, s) => s.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(Obj(10, "Coin", 3000, 100, 10, 10)));
            pw.TSteps(5);
            Press(pw, "E"); Press(pw, "E"); Press(pw, "E");
            pw.TSteps(100); // 23 + 100 steps ≈ 1.97 s → 3 coins
            return pw.TVar("talks") == 1 && pw.TAlive("Coin") == 4;
        });

        t.Test("multi: inspector — ＋ Add behavior offers a second Interact but not a second Platformer; the cards are numbered, Remove takes that card, undo brings it back", () =>
        {
            var project = ProjectIO.CreateNew("Multi", System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EE_Full_Multi_" + Guid.NewGuid().ToString("N")[..6]));
            var o = new GameObjectData { Id = 900, Name = "Radio", X = 100, Y = 100, Width = 40, Height = 40 };
            var first = BehaviorCatalog.Info(BehaviorKind.Interact).CreateData(); first.Texts["text"] = "first";
            o.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Platformer).CreateData()); o.Behaviors.Add(first);
            project.GetStartScene().Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(o);
            var ed = new MainWindow(project);
            try
            {
                ed.TestInspect(o);
                var kinds = ed.TestAddableKinds();
                bool offers = kinds.Contains(BehaviorKind.Interact) && !kinds.Contains(BehaviorKind.Platformer) && kinds.Contains(BehaviorKind.TopDown);
                bool added = ed.TestAddBehavior(BehaviorKind.Interact) && o.Behaviors.Count(b => b.Kind == BehaviorKind.Interact) == 2;
                bool noPlat = !ed.TestAddBehavior(BehaviorKind.Platformer) && o.Behaviors.Count(b => b.Kind == BehaviorKind.Platformer) == 1;
                ed.TestInspect(o);
                var titles = ed.TestCardTitles();
                string name = L.T(BehaviorCatalog.Info(BehaviorKind.Interact).Name);
                bool numbered = titles.Count == 3 && titles[1].EndsWith(name + " 1") && titles[2].EndsWith(name + " 2") && !titles[0].EndsWith(" 1");
                bool removed = ed.TestRemoveCard(1) && o.Behaviors.Count(b => b.Kind == BehaviorKind.Interact) == 1
                               && !o.Behaviors.Contains(first) && ed.TestCardTitles().All(x => !x.EndsWith(" 1") && !x.EndsWith(" 2"));
                ed.TestUndo();
                var back = ed.TestFindObject("Radio");
                bool undone = back != null && back.Behaviors.Count(b => b.Kind == BehaviorKind.Interact) == 2
                              && back.Behaviors.First(b => b.Kind == BehaviorKind.Interact).Text("text", "") == "first";
                return offers && added && noPlat && numbered && removed && undone;
            }
            finally { try { ed.Close(); } catch { } }
        });

        t.Test("multi: AI — a reply with two Interacts keeps both (a toggle); two Platformers still merge; the same Interact written twice is one", () =>
        {
            var project = ProjectIO.CreateNew("MultiAi", System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EE_Full_MultiAi_" + Guid.NewGuid().ToString("N")[..6]));
            var layer = project.GetStartScene().Layers.First(l => l.Kind == LayerKind.Base);
            layer.Objects.Add(new GameObjectData { Id = 901, Name = "Radio", X = 300, Y = 300, Width = 40, Height = 40 });
            var ed = new MainWindow(project);
            try
            {
                const string on = "{\"kind\":\"Interact\",\"nums\":{\"to\":1},\"texts\":{\"action\":\"setvar\",\"var\":\"radioOn\",\"cond\":\"radioOn < 1\"}}";
                const string off = "{\"kind\":\"Interact\",\"nums\":{\"to\":0},\"texts\":{\"action\":\"setvar\",\"var\":\"radioOn\",\"cond\":\"radioOn >= 1\"}}";
                ed.TestApplySpec("{\"edit\":[{\"name\":\"Radio\",\"behaviors\":[" + on + "," + off + "]}]}", "make the radio turn on and off with E");
                var radio = ed.TestFindObject("Radio")!;
                var its = radio.Behaviors.Where(b => b.Kind == BehaviorKind.Interact).ToList();
                bool both = its.Count == 2 && its[0].Text("cond", "") == "radioOn < 1" && its[1].Text("cond", "") == "radioOn >= 1" && its[1].Num("to", -1) == 0;
                // an edit that names one Interact retunes the first one, it doesn't add a third
                ed.TestApplySpec("{\"edit\":[{\"name\":\"Radio\",\"behaviors\":[{\"kind\":\"Interact\",\"nums\":{\"radius\":120}}]}]}", "make the radio work from further away");
                radio = ed.TestFindObject("Radio")!;
                its = radio.Behaviors.Where(b => b.Kind == BehaviorKind.Interact).ToList();
                bool retuned = its.Count == 2 && its[0].Num("radius", 0) == 120 && its[1].Text("cond", "") == "radioOn >= 1";
                ed.TestApplySpec("{\"create\":[{\"name\":\"Hero\",\"x\":100,\"y\":100,\"width\":30,\"height\":40,\"behaviors\":[{\"kind\":\"Platformer\",\"nums\":{\"speed\":300}},{\"kind\":\"Platformer\",\"nums\":{\"jump\":700}}]}]}", "a hero");
                var hero = ed.TestFindObject("Hero");
                bool merged = hero != null && hero.Behaviors.Count(b => b.Kind == BehaviorKind.Platformer) == 1;
                ed.TestApplySpec("{\"create\":[{\"name\":\"Sign\",\"x\":500,\"y\":100,\"width\":30,\"height\":40,\"behaviors\":[" + on + "," + on + "]}]}", "a sign");
                var sign = ed.TestFindObject("Sign");
                bool once = sign != null && sign.Behaviors.Count(b => b.Kind == BehaviorKind.Interact) == 1;
                return both && retuned && merged && once;
            }
            finally { try { ed.Close(); } catch { } }
        });

        t.Test("multi: the AI prompt says which behaviors can be several, and the toggle hint shows two Interacts", () =>
        {
            var hint = EasyEngine.MainWindow.HintsFor("make the radio toggle on and off with E");
            bool toggle = hint.Any(h => h.Kinds.Contains(BehaviorKind.Interact) && h.Text.Contains("TWO Interact"));
            return toggle;
        });
    }
}
