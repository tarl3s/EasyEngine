using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EasyEngine;

// The AI's JSON is cleaned up before it is applied (AiSpecFix), custom rules are added (not replaced), the AI can
// delete objects, and the runtime reads what models naturally write: expressions and quoted numbers as values,
// hide/show, a Shooter with no key, a coin worth points, "when it dies" on a player who respawns. No AI needed.
public partial class App
{
    partial void FeatureTestsAiFix(FullTestKit t)
    {
        static List<Dictionary<string, string>> Fix(string rulesJson, List<string>? notes = null)
        {
            using var d = JsonDocument.Parse(rulesJson);
            return App.AiEvalKit.RulesOf(AiSpecFix.Rules(d.RootElement, notes ?? new List<string>()));
        }
        static bool Is(Dictionary<string, string> r, string k, string v) => r.TryGetValue(k, out var x) && x.Equals(v, StringComparison.OrdinalIgnoreCase);
        MainWindow NewEditor(Action<Scene>? fill = null)
        {
            var project = ProjectIO.CreateNew("AiFix", Path.Combine(Path.GetTempPath(), "EE_Full_AiFix_" + Guid.NewGuid().ToString("N")[..6]));
            var scene = project.GetStartScene();
            var layer = scene.Layers.First(l => l.Kind == LayerKind.Base);
            var player = new GameObjectData { Id = 90, Name = "Player", X = 100, Y = 100, Width = 48, Height = 64 };
            player.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Platformer).CreateData());
            layer.Objects.Add(player);
            fill?.Invoke(scene);
            return new MainWindow(project);
        }

        t.Test("ai fix: event/action synonyms (wait→after, fadeout→tween opacity 0, onclick→click, kill→destroy)", () =>
        {
            var r = Fix("[{\"when\":\"wait\",\"time\":5,\"do\":\"fadeout\",\"duration\":2},{\"when\":\"onclick\",\"do\":\"kill\"}]");
            return r.Count == 2 && Is(r[0], "when", "after") && Is(r[0], "seconds", "5") && Is(r[0], "do", "tween") && Is(r[0], "prop", "opacity") && Is(r[0], "to", "0")
                   && Is(r[1], "when", "click") && Is(r[1], "do", "destroy");
        });
        t.Test("ai fix: touch 'target' → name, 'is' → ==, teleport x/y → two set rules, an unknown action is dropped with a note", () =>
        {
            var notes = new List<string>();
            var r = Fix("[{\"when\":\"touch\",\"target\":\"Coin\",\"do\":\"addvar\",\"var\":\"score\",\"value\":1}," +
                        "{\"when\":\"ifvar\",\"var\":\"lives\",\"op\":\"is\",\"value\":0,\"do\":\"goscene\",\"to\":\"Over\"}," +
                        "{\"when\":\"keypress\",\"key\":\"T\",\"do\":\"teleport\",\"x\":600,\"y\":100},{\"when\":\"start\",\"do\":\"dance\"}]", notes);
            return r.Count == 4 && Is(r[0], "name", "Coin") && Is(r[0], "amount", "1") && Is(r[1], "op", "==") && Is(r[1], "scene", "Over")
                   && Is(r[2], "do", "set") && Is(r[2], "prop", "x") && Is(r[2], "to", "600") && Is(r[3], "prop", "y") && notes.Count == 1 && notes[0].Contains("dance");
        });
        t.Test("ai fix: behaviors — axis \"y\" → 1, a variable in nums → texts, a door 'on death' becomes a rule, Custom written as a behavior", () =>
        {
            var ed = NewEditor();
            var (ok, _) = ed.TestApplySpec("{\"create\":[{\"name\":\"Lift\",\"behaviors\":[{\"kind\":\"Solid\"},{\"kind\":\"Patrol\",\"nums\":{\"axis\":\"y\",\"distance\":\"150\"}}]}," +
                "{\"name\":\"Coin\",\"behaviors\":[{\"kind\":\"Collectible\",\"nums\":{\"var\":\"score\",\"points\":10}}]}," +
                "{\"name\":\"Boss\",\"behaviors\":[{\"kind\":\"Health\",\"nums\":{\"respawn\":0}},{\"kind\":\"GoToScene\",\"texts\":{\"scene\":\"Win\",\"trigger\":\"death\"}},{\"kind\":\"Custom\",\"texts\":{\"when\":\"start\",\"do\":\"say\",\"text\":\"Hi\"}}]}]}");
            var patrol = ed.TestFindObject("Lift")?.Behaviors.FirstOrDefault(b => b.Kind == BehaviorKind.Patrol);
            var coin = ed.TestFindObject("Coin")?.Behaviors.FirstOrDefault(b => b.Kind == BehaviorKind.Collectible);
            var boss = ed.TestFindObject("Boss");
            var rules = App.AiEvalKit.Rules(boss);
            return ok && patrol?.Num("axis") == 1 && patrol.Num("distance") == 150 && coin?.Text("var", "") == "score" && coin.Num("points") == 10
                   && boss != null && !boss.Behaviors.Any(b => b.Kind == BehaviorKind.GoToScene)
                   && rules.Any(r => Is(r, "when", "death") && Is(r, "do", "goscene") && Is(r, "scene", "Win")) && rules.Any(r => Is(r, "do", "say"));
        });
        t.Test("ai: new custom rules are ADDED to an object's rules; one with the same event+action retunes the old one", () =>
        {
            var ed = NewEditor(s => s.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(new GameObjectData
            {
                Id = 91, Name = "Enemy", Behaviors = { new BehaviorData { Kind = BehaviorKind.Custom, Texts = { ["script"] = "[{\"when\":\"timer\",\"every\":2,\"do\":\"jump\",\"force\":500}]" } } }
            }));
            ed.TestApplySpec("{\"edit\":[{\"name\":\"Enemy\",\"custom\":[{\"when\":\"key\",\"key\":\"R\",\"do\":\"rotate\",\"speed\":90}]}]}");
            var after1 = App.AiEvalKit.Rules(ed.TestFindObject("Enemy"));
            ed.TestApplySpec("{\"edit\":[{\"name\":\"Enemy\",\"custom\":[{\"when\":\"timer\",\"every\":3,\"do\":\"jump\",\"force\":500}]}]}");
            var after2 = App.AiEvalKit.Rules(ed.TestFindObject("Enemy"));
            return after1.Count == 2 && after2.Count == 2 && after2.Any(r => Is(r, "every", "3")) && !after2.Any(r => Is(r, "every", "2"));
        });
        t.Test("ai: 'an enemy that chases the player' creates the enemy (not an edit of the player); \"delete\" removes objects", () =>
        {
            var ed = NewEditor(s => s.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(new GameObjectData { Id = 92, Name = "Rock" }));
            var (ok, _) = ed.TestApplySpec("{\"create\":[{\"name\":\"Enemy\",\"behaviors\":[{\"kind\":\"Chase\",\"texts\":{\"target\":\"Player\"}}]}]}", "add an enemy that chases the player");
            var player = ed.TestFindObject("Player")!;
            ed.TestApplySpec("{\"delete\":[\"Rock\"]}", "delete the rock");
            return ok && ed.TestFindObject("Enemy")?.Behaviors.Any(b => b.Kind == BehaviorKind.Chase) == true && !player.Behaviors.Any(b => b.Kind == BehaviorKind.Chase)
                   && ed.TestFindObject("Rock") == null;
        });
        t.Test("rules: a value can be an expression or a quoted number; addvar takes \"value\"", () =>
        {
            var s = t.Sim("[{\"when\":\"start\",\"do\":\"set\",\"prop\":\"x\",\"to\":\"100 + 50\"},{\"when\":\"always\",\"do\":\"move\",\"dy\":\"60\"}," +
                          "{\"when\":\"start\",\"do\":\"addvar\",\"var\":\"n\",\"value\":-1},{\"when\":\"always\",\"do\":\"set\",\"prop\":\"vx\",\"expr\":\"expr: 2 * 3\"}]", PhysicsMode.None, null);
            double y0 = s.TY("A"); s.TSteps(60);
            return Math.Abs(s.TX("A") - (150 + 6)) < 2 && Math.Abs(s.TY("A") - y0 - 60) < 3 && s.TVar("n") == -1;
        });
        t.Test("rules: 'every N seconds' fires the first time N seconds after it appears (a countdown shows its start value)", () =>
        {
            var s = t.Sim("[{\"when\":\"timer\",\"every\":1,\"do\":\"addvar\",\"var\":\"t\",\"amount\":1}]", PhysicsMode.None, null);
            s.TSteps(30); double half = s.TVar("t");
            s.TSteps(36); double one = s.TVar("t");
            s.TSteps(60); double two = s.TVar("t");
            return half == 0 && one == 1 && two == 2;
        });
        t.Test("rules: hide / show make an object invisible and visible again", () =>
        {
            var s = t.Sim("[{\"when\":\"start\",\"do\":\"hide\"},{\"when\":\"after\",\"seconds\":0.5,\"do\":\"show\"}]", PhysicsMode.None, null);
            s.TSteps(5); double hidden = s.TOpacity("A"); s.TSteps(40);
            return hidden < 0.01 && s.TOpacity("A") > 0.99;
        });
        t.Test("shooter with no key fires by itself, aimed at its target; a shoot rule's \"at\" aims too", () =>
        {
            var s = t.Sim("[]", PhysicsMode.None, (p, sc) =>
            {
                var layer = sc.Layers.First(l => l.Kind == LayerKind.Base);
                var turret = new GameObjectData { Id = 20, Name = "Turret", X = 600, Y = 200, Width = 40, Height = 40 };
                var sh = BehaviorCatalog.Info(BehaviorKind.Shooter).CreateData();
                sh.Texts["key"] = ""; sh.Texts["aim"] = "A"; sh.Nums["directions"] = 1; sh.Nums["cooldown"] = 0.5;
                turret.Behaviors.Add(sh);
                layer.Objects.Add(turret);
            });
            var seen = new HashSet<object>(); s.TCollect(seen); int before = seen.Count;
            for (int i = 0; i < 70; i++) { s.TStep(); s.TCollect(seen); }
            var bullets = seen.Count - before;
            return bullets >= 2 && s.TLiveCount >= 3 && s.TBulletVXs().Any(vx => vx < -100); // flying left, toward A (x 200)
        });
        t.Test("collectible: a coin can add its points to a variable", () =>
        {
            var s = t.Sim("[]", PhysicsMode.None, (p, sc) =>
            {
                var coin = new GameObjectData { Id = 21, Name = "Coin", X = 210, Y = 210, Width = 20, Height = 20 };
                var c = BehaviorCatalog.Info(BehaviorKind.Collectible).CreateData(); c.Texts["collector"] = "A"; c.Texts["var"] = "score"; c.Nums["points"] = 10;
                coin.Behaviors.Add(c);
                sc.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(coin);
            });
            s.TSteps(3);
            return s.TVar("score") == 10 && s.TDead("Coin");
        });
        t.Test("'when it dies' also fires for one that respawns (the player); a projectile with lifetime 0 flies on", () =>
        {
            var s = t.Sim("[{\"when\":\"death\",\"do\":\"addvar\",\"var\":\"deaths\",\"amount\":1}]", PhysicsMode.None, (p, sc) =>
            {
                var a = sc.Layers.SelectMany(l => l.Objects).First(o => o.Name == "A");
                var hp = BehaviorCatalog.Info(BehaviorKind.Health).CreateData(); hp.Nums["respawn"] = 1; a.Behaviors.Add(hp);
                var arrow = new GameObjectData { Id = 22, Name = "Arrow", X = 0, Y = 900, Width = 10, Height = 4 };
                var pr = BehaviorCatalog.Info(BehaviorKind.Projectile).CreateData(); pr.Nums["life"] = 0; pr.Nums["speed"] = 300;
                arrow.Behaviors.Add(pr);
                sc.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(arrow);
            });
            s.TSteps(2); s.TSetHp("A", 0); s.TSteps(240);
            return s.TVar("deaths") == 1 && !s.TDead("A") && !s.TDead("Arrow") && s.TX("Arrow") > 1000;
        });
    }
}
