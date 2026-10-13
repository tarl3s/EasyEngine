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
            using var d = JsonDocument.Parse(rulesJson, LooseJson.Options);
            var raws = new List<string>();
            AiSpecFix.CollectRules(d.RootElement, raws);
            return App.AiEvalKit.RulesOf(AiSpecFix.Rules(raws, notes ?? new List<string>()));
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
            var (ok, _) = ed.TestApplySpec("{\"create\":[{\"name\":\"Lift\",\"behaviors\":[{\"kind\":\"Solid\"},{\"kind\":\"Patrol\",\"nums\":{\"axis\":\"y\",\"Distance\":\"150\"}}]}," +
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
            ed.TestApplySpec("{\"create\":[{\"name\":\"PlayerHealthBar\",\"layer\":\"UI\",\"behaviors\":[{\"kind\":\"Bar\",\"texts\":{\"var\":\"health\"}}]}]}", "add a health bar for the player");
            bool bar = ed.TestFindObject("PlayerHealthBar")?.Behaviors.Any(b => b.Kind == BehaviorKind.Bar) == true && !player.Behaviors.Any(b => b.Kind == BehaviorKind.Bar);
            return ok && bar && ed.TestFindObject("Enemy")?.Behaviors.Any(b => b.Kind == BehaviorKind.Chase) == true && !player.Behaviors.Any(b => b.Kind == BehaviorKind.Chase)
                   && ed.TestFindObject("Rock") == null;
        });
        t.Test("ai review: trailing commas / a key written twice never crash the build; a bouncy platform stays put", () =>
        {
            var ed = NewEditor(s => s.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(new GameObjectData
            {
                Id = 93, Name = "Platform", Physics = new PhysicsData { Mode = PhysicsMode.Static }, Behaviors = { BehaviorCatalog.Info(BehaviorKind.Solid).CreateData() }
            }));
            var (ok1, _) = ed.TestApplySpec("{\"edit\":[{\"name\":\"Player\",\"behaviors\":[{\"kind\":\"Health\",\"nums\":{\"max\":5,},}],\"custom\":[{\"when\":\"start\",\"do\":\"jump\",},]}]}");
            var (ok2, _) = ed.TestApplySpec("{\"edit\":[{\"name\":\"Player\",\"custom\":[{\"when\":\"ifvar\",\"var\":\"a\",\"op\":\">=\",\"value\":1,\"do\":\"setvar\",\"var\":\"b\",\"to\":2}]}]}");
            var (ok3, _) = ed.TestApplySpec("{\"edit\":[{\"name\":\"Platform\",\"physics\":{\"bounciness\":0.8}}]}");
            var plat = ed.TestFindObject("Platform")!;
            return ok1 && ed.TestFindObject("Player")!.Behaviors.FirstOrDefault(b => b.Kind == BehaviorKind.Health)?.Num("max") == 5 && ok2 && ok3
                   && plat.Physics.Mode == PhysicsMode.Static && plat.Behaviors.Any(b => b.Kind == BehaviorKind.Solid) && plat.Physics.Bounciness == 0.8;
        });
        t.Test("ai review: two new rules alike both stay (only an old one is retuned)", () =>
        {
            using var d = JsonDocument.Parse("[{\"when\":\"after\",\"seconds\":2,\"do\":\"tween\",\"prop\":\"opacity\",\"to\":0},{\"when\":\"after\",\"seconds\":4,\"do\":\"tween\",\"prop\":\"opacity\",\"to\":1}]");
            var merged = App.AiEvalKit.RulesOf(EasyEngine.MainWindow.MergeRules("[{\"when\":\"after\",\"seconds\":1,\"do\":\"tween\",\"prop\":\"opacity\",\"to\":0.5}]", d.RootElement));
            return merged.Count == 2 && Is(merged[0], "seconds", "2") && Is(merged[1], "seconds", "4");
        });
        t.Test("ai review: synonyms read their params (remove+item, speed+factor, load+scene, text+var); any case; 'exprCount' stays", () =>
        {
            var r = Fix("[{\"when\":\"touch\",\"name\":\"Door\",\"do\":\"remove\",\"item\":\"Key\"},{\"when\":\"start\",\"do\":\"speed\",\"factor\":2}," +
                        "{\"when\":\"start\",\"do\":\"load\",\"scene\":\"Level2\"},{\"when\":\"start\",\"do\":\"text\",\"var\":\"msg\",\"value\":\"Hi\"}," +
                        "{\"When\":\"Timer\",\"interval\":2,\"Do\":\"Jump\"},{\"when\":\"start\",\"do\":\"setvar\",\"var\":\"n\",\"to\":\"exprCount + 1\"},{\"when\":\"start\",\"do\":\"setvar\",\"var\":\"m\",\"to\":\"expr: 1 + 1\"}]");
            return r.Count == 7 && Is(r[0], "do", "removeitem") && Is(r[1], "do", "boost") && Is(r[2], "do", "goscene") && Is(r[3], "do", "settext") && Is(r[3], "to", "Hi")
                   && Is(r[4], "when", "timer") && Is(r[4], "every", "2") && Is(r[4], "do", "jump") && Is(r[5], "to", "exprCount + 1") && Is(r[6], "to", "1 + 1");
        });
        t.Test("cloud: 'try again in 7.1s' / '1m3s' / '250ms' are read; a retired Gemini model is replaced by the newest flash", () =>
            Math.Abs(CloudClient.RetryAfterSeconds("Rate limit. Please try again in 7.147s.") - 7.147) < 0.001
            && Math.Abs(CloudClient.RetryAfterSeconds("try again in 1m3s") - 63) < 0.001 && Math.Abs(CloudClient.RetryAfterSeconds("retry in 250ms") - 0.25) < 0.001
            && CloudClient.RetryAfterSeconds("try again in .s") == 0
            && CloudClient.PickModel("gemini", new[] { "gemini-2.5-flash", "gemini-3.8-flash", "gemini-3.8-flash-tts", "gemini-flash-latest", "gemini-3.1-pro-preview" }) == "gemini-3.8-flash"
            && CloudClient.PickModel("openrouter", new[] { "x/paid", "google/gemma-4-31b-it:free", "nvidia/nemotron-3-super-120b-a12b:free" }) == "nvidia/nemotron-3-super-120b-a12b:free");
        t.Test("behaviors: 'only while' (texts.cond) — a Shooter fires only while A is on its left; a Patrol waits until A stands on it", () =>
        {
            var s = t.Sim("[]", PhysicsMode.Dynamic, (p, sc) =>
            {
                var layer = sc.Layers.First(l => l.Kind == LayerKind.Base);
                var turret = new GameObjectData { Id = 30, Name = "Turret", X = 600, Y = 100, Width = 40, Height = 40 };
                var sh = BehaviorCatalog.Info(BehaviorKind.Shooter).CreateData();
                sh.Texts["key"] = ""; sh.Texts["aim"] = "A"; sh.Nums["directions"] = 1; sh.Nums["cooldown"] = 0.5; sh.Texts["cond"] = "A.cx < self.cx";
                turret.Behaviors.Add(sh);
                var plat = new GameObjectData { Id = 31, Name = "Lift", X = 150, Y = 600, Width = 200, Height = 20, Physics = new PhysicsData { Mode = PhysicsMode.Static } };
                plat.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Solid).CreateData());
                var pat = BehaviorCatalog.Info(BehaviorKind.Patrol).CreateData(); pat.Nums["axis"] = 1; pat.Nums["distance"] = 100; pat.Texts["cond"] = "local.go >= 1";
                plat.Behaviors.Add(pat);
                plat.Behaviors.Add(new BehaviorData { Kind = BehaviorKind.Custom, Texts = { ["script"] = "[{\"when\":\"touch\",\"name\":\"A\",\"do\":\"setlocal\",\"var\":\"go\",\"to\":1}]" } });
                layer.Objects.Add(turret); layer.Objects.Add(plat);
                var a = sc.Layers.SelectMany(l => l.Objects).First(o => o.Name == "A"); a.X = 1200; a.Y = 100; // far right, falling nowhere at first
            });
            int Shots(PlayWindow p, int frames) { var seen = new HashSet<object>(); p.TCollect(seen); int n0 = seen.Count; for (int i = 0; i < frames; i++) { p.TStep(); p.TCollect(seen); } return seen.Count - n0; }
            s.TPlace("A", 1200, 100); int right = Shots(s, 60);
            double ly = s.TY("Lift"); s.TSteps(30); bool liftWaits = Math.Abs(s.TY("Lift") - ly) < 1;
            s.TPlace("A", 200, 536); int left = Shots(s, 60); // standing on the Lift (its top is y 600): touching it starts it
            double lo = 1e9, hi = -1e9; for (int i = 0; i < 120; i++) { s.TStep(); lo = Math.Min(lo, s.TY("Lift")); hi = Math.Max(hi, s.TY("Lift")); }
            return right == 0 && left >= 1 && liftWaits && hi - lo > 20;
        });
        t.Test("rules: those started by the same key press decide first (pay 10 coins + get a Sword both happen); 'always' rules still chain", () =>
        {
            var s = t.Sim("[{\"when\":\"keypress\",\"key\":\"E\",\"cond\":\"coins >= 10\",\"do\":\"addvar\",\"var\":\"coins\",\"amount\":-10}," +
                          "{\"when\":\"keypress\",\"key\":\"E\",\"cond\":\"coins >= 10\",\"do\":\"additem\",\"item\":\"Sword\"}]", PhysicsMode.None, null);
            s.TSetVar("coins", 15); s.TSteps(2); s.TKey("E", true); s.TSteps(2); s.TKey("E", false); s.TSteps(25);
            bool bought = s.TVar("coins") == 5 && s.TItem("Sword") == 1;
            s.TKey("E", true); s.TSteps(2); s.TKey("E", false); s.TSteps(2);
            return bought && s.TVar("coins") == 5 && s.TItem("Sword") == 1;
        });
        t.Test("rules: \"local\":\"yes\" is a yes (a word in a number param is not a variable)", () =>
        {
            var s = t.Sim("[{\"when\":\"start\",\"do\":\"addlocal\",\"var\":\"k\",\"amount\":\"2\"},{\"when\":\"start\",\"do\":\"toggle\",\"var\":\"lit\",\"local\":\"yes\"}]", PhysicsMode.None, null);
            s.TSteps(3);
            return s.TLocal("A", "k") == 2;
        });
        t.Test("ai: habits copied from the examples are dropped (a follow-camera on a box, a Spawner key nobody asked for); physics inside a behavior is used", () =>
        {
            var ed = NewEditor(s => s.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(new GameObjectData { Id = 94, Name = "Box" }));
            ed.TestApplySpec("{\"edit\":[{\"name\":\"Box\",\"behaviors\":[{\"kind\":\"FollowCamera\"}]}]}", "make the Box follow the mouse");
            bool noCam = !ed.TestFindObject("Box")!.Behaviors.Any(b => b.Kind == BehaviorKind.FollowCamera);
            ed.TestApplySpec("{\"create\":[{\"name\":\"Gen\",\"behaviors\":[{\"kind\":\"Spawner\",\"texts\":{\"target\":\"Enemy\",\"key\":\"Y\"},\"nums\":{\"interval\":2}}]}]}", "spawn an enemy every 2 seconds");
            bool noKey = ed.TestFindObject("Gen")?.Behaviors.First(b => b.Kind == BehaviorKind.Spawner).Text("key", "?") == "";
            ed.TestApplySpec("{\"create\":[{\"name\":\"Gun\",\"behaviors\":[{\"kind\":\"Shooter\",\"texts\":{\"key\":\"J\"}}]}]}", "make a turret that shoots when I press J");
            bool keptKey = ed.TestFindObject("Gun")?.Behaviors.First(b => b.Kind == BehaviorKind.Shooter).Text("key", "") == "J";
            ed.TestApplySpec("{\"create\":[{\"name\":\"Ball\",\"physics\":\"Dynamic\",\"behaviors\":[{\"kind\":\"Solid\",\"nums\":{\"bounciness\":0.8}}]}]}", "make a ball that bounces");
            var ball = ed.TestFindObject("Ball");
            var tint = Fix("[{\"when\":\"start\",\"do\":\"setcolor\",\"var\":\"color\",\"to\":\"#0000FF\"}]");
            return noCam && noKey && keptKey && ball?.Physics.Mode == PhysicsMode.Dynamic && ball.Physics.Bounciness == 0.8
                   && Is(tint[0], "do", "tint") && Is(tint[0], "color", "#0000FF") && !tint[0].ContainsKey("var");
        });
        t.Test("blocks: a rule using the game's other names (hover, keydown, accel) opens as that block, not as the first one", () =>
        {
            var r = App.AiEvalKit.RulesOf(t.RT("[{\"when\":\"hover\",\"do\":\"accel\",\"ax\":100,\"ay\":0},{\"when\":\"keydown\",\"key\":\"R\",\"do\":\"rotate\",\"speed\":90}]"));
            return r.Count == 2 && Is(r[0], "when", "mouseover") && Is(r[0], "do", "accelerate") && Is(r[0], "ax", "100") && Is(r[1], "when", "key") && Is(r[1], "key", "R");
        });
        t.Test("blocks: every 📚 recipe survives the block editor, and they do what they say (blink, circle, countdown)", () =>
        {
            var recipes = BlockEditorWindow.Recipes().Select(r => r.json).ToArray();
            if (recipes.Any(j => App.AiEvalKit.RulesOf(t.RT(j)).Count != App.AiEvalKit.RulesOf(j).Count)) return false;
            var blink = t.Sim(t.RT(recipes[1]), PhysicsMode.None, null); double lo = 1;
            for (int i = 0; i < 60; i++) { blink.TStep(); lo = Math.Min(lo, blink.TOpacity("A")); }
            var circle = t.Sim(t.RT(recipes[4]), PhysicsMode.None, null); double x0 = 1e9, x1 = -1e9, y0 = 1e9, y1 = -1e9;
            for (int i = 0; i < 200; i++) { circle.TStep(); x0 = Math.Min(x0, circle.TX("A")); x1 = Math.Max(x1, circle.TX("A")); y0 = Math.Min(y0, circle.TY("A")); y1 = Math.Max(y1, circle.TY("A")); }
            var count = t.Sim(t.RT(recipes[12]), PhysicsMode.None, null);
            count.TSteps(30); double start = count.TVar("timer"); count.TSteps(40);
            return lo < 0.2 && x1 - x0 > 100 && y1 - y0 > 100 && start == 60 && count.TVar("timer") == 59;
        });
        t.Test("ai: a button / door whose scene came under another name (\"to\":\"Level2\") still goes there", () =>
        {
            var ed = NewEditor();
            ed.TestApplySpec("{\"create\":[{\"name\":\"StartButton\",\"behaviors\":[{\"kind\":\"UIButton\",\"nums\":{\"to\":\"Level2\"}}]},{\"name\":\"Door\",\"behaviors\":[{\"kind\":\"GoToScene\",\"texts\":{\"level\":\"Level3\"}}]}]}");
            ed.TestApplySpec("{\"create\":[{\"name\":\"Go\",\"behaviors\":[{\"kind\":\"Button\",\"nums\":{\"onClick\":\"goscene Level4\"}}]}],\"edit\":[{\"name\":\"ScoreText\",\"layer\":\"UI\",\"behaviors\":[{\"kind\":\"Label\",\"texts\":{\"text\":\"Score: {score}\"}}]}]}");
            return ed.TestFindObject("Go")?.Behaviors.First(b => b.Kind == BehaviorKind.UIButton).Text("scene", "") == "Level4"
                   && ed.TestFindObject("ScoreText")?.Behaviors.Any(b => b.Kind == BehaviorKind.Label) == true // an edit of a missing object makes it
                   && ed.TestFindObject("StartButton")?.Behaviors.First(b => b.Kind == BehaviorKind.UIButton).Text("scene", "") == "Level2"
                   && ed.TestFindObject("Door")?.Behaviors.First(b => b.Kind == BehaviorKind.GoToScene).Text("scene", "") == "Level3";
        });
        t.Test("ai: {\"level\":…} lays out a playable level (ground, reachable platforms with coins, standing enemies, a door, a score label)", () =>
        {
            var ed = NewEditor(s => s.Layers.Add(new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true }));
            var (ok, summary) = ed.TestApplySpec("{\"level\":{\"platforms\":3,\"coins\":5,\"enemies\":2,\"spikes\":1,\"door\":\"Level2\"}}", "build a level");
            var all = ed.TestAllObjects();
            var ground = all.FirstOrDefault(o => o.Name == "Ground");
            var plats = all.Where(o => o.Name.StartsWith("Platform")).OrderBy(o => o.X).ToList();
            bool reach = ground != null && plats.Count == 3 && ground.Y - plats.Min(p => p.Y) <= 160
                         && plats.All(p => ground.Y - p.Y <= 100 || plats.Any(q => q != p && q.Y - p.Y is > 0 and <= 100)); // each one a jump from the ground or a lower one
            bool counts = all.Count(o => o.Behaviors.Any(b => b.Kind == BehaviorKind.Collectible)) == 5 && all.Count(o => o.Name.StartsWith("Enemy")) == 2
                          && all.Any(o => o.Behaviors.Any(b => b.Kind == BehaviorKind.GoToScene && b.Text("scene", "") == "Level2"))
                          && all.Any(o => o.Behaviors.Any(b => b.Kind == BehaviorKind.Label && b.Text("text", "").Contains("{score}")));
            var top = ed.TestAllObjects().First(o => o.Name == "Ground").Y;
            var pw = new PlayWindow(ed.EvalProject, ed.EvalScene.Name); pw.TSeed(1); pw.TLoad(); t.Sims.Add(pw); pw.TSteps(120);
            bool standing = Math.Abs(pw.TY("Enemy") + 48 - top) < 4;
            return ok && summary.Contains("level") && reach && counts && standing;
        });
        t.Test("ai review 2: the player's gun keeps its key; 'Scene 2' stays whole; a setvar button keeps its value; an edit of a missing 'PlayerHealthBar' makes it", () =>
        {
            var ed = NewEditor();
            ed.TestApplySpec("{\"edit\":[{\"name\":\"Player\",\"behaviors\":[{\"kind\":\"Shooter\",\"nums\":{\"speed\":600}}]}]}", "make the player's bullets faster");
            bool gun = ed.TestFindObject("Player")!.Behaviors.First(b => b.Kind == BehaviorKind.Shooter).Text("key", "") == "T";
            ed.TestApplySpec("{\"create\":[{\"name\":\"Next\",\"behaviors\":[{\"kind\":\"UIButton\",\"texts\":{\"to\":\"Scene 2\"}}]},{\"name\":\"Mute\",\"behaviors\":[{\"kind\":\"UIButton\",\"texts\":{\"action\":\"setvar\",\"var\":\"music\"},\"nums\":{\"to\":0}}]}]}", "menu buttons");
            bool scene2 = ed.TestFindObject("Next")?.Behaviors.First(b => b.Kind == BehaviorKind.UIButton).Text("scene", "") == "Scene 2";
            var mute = ed.TestFindObject("Mute")?.Behaviors.First(b => b.Kind == BehaviorKind.UIButton);
            bool setvar = mute != null && mute.Text("scene", "") == "" && mute.Num("to", 1) == 0;
            ed.TestApplySpec("{\"edit\":[{\"name\":\"PlayerHealthBar\",\"layer\":\"UI\",\"behaviors\":[{\"kind\":\"Bar\",\"texts\":{\"var\":\"health\"}}]}]}", "show the player's health as a bar");
            bool bar = ed.TestFindObject("PlayerHealthBar") != null && !ed.TestFindObject("Player")!.Behaviors.Any(b => b.Kind == BehaviorKind.Bar);
            return gun && scene2 && setvar && bar;
        });
        t.Test("ai review 2: level spikes hurt and its door works", () =>
        {
            var ed = NewEditor(s => { s.Layers.Add(new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true }); });
            ed.TestFindObject("Player")!.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Health).CreateData());
            ed.EvalProject.Scenes.Add(new Scene { Name = "Level2", Layers = { new Layer { Name = "Base", Kind = LayerKind.Base } } });
            ed.TestApplySpec("{\"level\":{\"platforms\":2,\"spikes\":1,\"door\":\"Level2\"}}", "build a level");
            var spike = ed.TestFindObject("Spike")!; var door = ed.TestFindObject("Door")!;
            var pw = new PlayWindow(ed.EvalProject, ed.EvalScene.Name); pw.TSeed(1); pw.TLoad(); t.Sims.Add(pw);
            pw.TSteps(20); double hp = pw.THp("Player"); pw.TPlace("Player", spike.X - 8, spike.Y - 50); pw.TSteps(10);
            bool hurt = pw.THp("Player") < hp;
            pw.TPlace("Player", door.X + 4, door.Y + 20); pw.TSteps(10);
            return hurt && pw.TScene == "Level2";
        });
        t.Test("ai: the level a request describes is read from its words (EN/PT, numbers or words, stairs, rooms, a door)", () =>
        {
            var scenes = new[] { "Scene 1", "Level2" };
            string Of(string p) { var d = EasyEngine.MainWindow.LevelFromPrompt(p, scenes); return d == null ? "null" : string.Join(",", d.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value)); }
            return Of("build a simple platformer level with 3 floating platforms, 5 coins, 2 enemies that patrol and a door to Level2 at the end") == "coins=5,door=Level2,enemies=2,platforms=3"
                && Of("monte uma fase com chão, 4 plataformas, 3 moedas e um inimigo que patrulha") == "coins=3,enemies=1,platforms=4"
                && Of("make a staircase of 5 platforms going up to the right") == "stairs=5"
                && Of("make a top-down room surrounded by walls") == "style=topdown"
                && Of("put 10 coins in a row above the ground") == "coins=10"
                && Of("make a coin that gives 10 points") == "null" && Of("create an enemy that chases the player") == "null" && Of("give the player 5 hearts") == "null";
        });
        t.Test("ai: when the reply doesn't deliver the level asked for, the engine lays it out (a reply that does is kept)", () =>
        {
            var ed = NewEditor(s => s.Layers.Add(new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true }));
            ed.EvalProject.Scenes.Add(new Scene { Name = "Level2", Layers = { new Layer { Name = "Base", Kind = LayerKind.Base } } });
            ed.TestApplySpec("{\"create\":[{\"name\":\"Ground\",\"x\":0,\"y\":0,\"width\":800,\"height\":100},{\"name\":\"Platform1\",\"x\":100,\"y\":100,\"width\":200,\"height\":20}]}",
                             "build a level with 3 platforms, 5 coins, 2 enemies and a door to Level2");
            var all = ed.TestAllObjects();
            bool built = all.Count(o => o.Name.StartsWith("Platform") && o.Behaviors.Any(b => b.Kind == BehaviorKind.Solid)) == 3 && all.Count(o => o.Name.StartsWith("Coin")) == 5
                         && all.Count(o => o.Name.StartsWith("Enemy")) == 2 && all.Any(o => o.Behaviors.Any(b => b.Kind == BehaviorKind.GoToScene)) && !all.Any(o => o.Name == "Ground" && o.Y == 0);
            var ed2 = NewEditor();
            ed2.TestApplySpec("{\"create\":[{\"name\":\"P1\",\"x\":300,\"y\":380,\"behaviors\":[{\"kind\":\"Solid\"}]},{\"name\":\"P2\",\"x\":500,\"y\":330,\"behaviors\":[{\"kind\":\"Solid\"}]},{\"name\":\"C1\",\"behaviors\":[{\"kind\":\"Collectible\"}]},{\"name\":\"C2\",\"behaviors\":[{\"kind\":\"Collectible\"}]}]}",
                              "make a level with 2 platforms and 2 coins");
            bool kept = ed2.TestFindObject("P1") != null && !ed2.TestAllObjects().Any(o => o.Name.StartsWith("Platform"));
            return built && kept;
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
        t.Test("ai fix: a second action written into a rule becomes its own rule (same event and cond); cond in nums goes to texts", () =>
        {
            var r = Fix("[{\"when\":\"keypress\",\"key\":\"E\",\"cond\":\"coins >= 10\",\"do\":\"addvar\",\"var\":\"coins\",\"amount\":-10,\"additem\":\"Sword\"}," +
                        "{\"when\":\"touch\",\"name\":\"Player\",\"do\":\"addvar\",\"var\":\"score\",\"amount\":1,\"destroy\":true}]");
            var ed = NewEditor();
            ed.TestApplySpec("{\"create\":[{\"name\":\"Spawner1\",\"behaviors\":[{\"kind\":\"Spawner\",\"nums\":{\"interval\":2,\"cond\":\"isnight\"}}]}]}");
            var sp = ed.TestFindObject("Spawner1")?.Behaviors.FirstOrDefault(b => b.Kind == BehaviorKind.Spawner);
            return r.Count == 4 && Is(r[0], "do", "addvar") && !r[0].ContainsKey("additem") && Is(r[1], "when", "keypress") && Is(r[1], "key", "E") && Is(r[1], "cond", "coins >= 10")
                   && Is(r[1], "do", "additem") && Is(r[1], "item", "Sword") && Is(r[3], "when", "touch") && Is(r[3], "name", "Player") && Is(r[3], "do", "destroy")
                   && sp?.Text("cond", "") == "isNight >= 1" && !sp.Nums.ContainsKey("cond");
        });
        t.Test("ai fix: 'after 5 seconds' isn't 'a second one' (create of an existing name edits it); a Gate asked with a key gets one", () =>
        {
            var ed = NewEditor(s => s.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(new GameObjectData { Id = 92, Name = "Box", X = 400, Y = 200, Width = 64, Height = 64 }));
            ed.TestApplySpec("{\"create\":[{\"name\":\"Box\",\"custom\":[{\"when\":\"after\",\"seconds\":5,\"do\":\"tween\",\"prop\":\"opacity\",\"to\":0}]}]}", "after 5 seconds, fade the Box out");
            bool noCopy = ed.TestFindObject("Box_2") == null && App.AiEvalKit.Rules(ed.TestFindObject("Box")).Count == 1;
            ed.TestApplySpec("{\"create\":[{\"name\":\"LockedDoor\",\"x\":700,\"y\":364,\"behaviors\":[{\"kind\":\"Gate\",\"texts\":{\"var\":\"hasKey\"}}]}]}", "make a locked door that opens when the player picks up a key");
            var key = ed.TestFindObject("Key");
            return noCopy && key?.Behaviors.Any(b => b.Kind == BehaviorKind.AbilityPickup && b.Text("var", "") == "hasKey") == true;
        });
        t.Test("ai fix: a level keeps only the pieces the request asks for, with its numbers", () =>
        {
            var ed = NewEditor();
            ed.TestApplySpec("{\"level\":{\"platforms\":3,\"coins\":5,\"enemies\":1,\"spikes\":1,\"door\":\"Level2\"}}", "build a level with 2 platforms and 3 coins");
            var all = ed.TestAllObjects();
            return all.Count(o => o.Name.StartsWith("Coin")) == 3 && !all.Any(o => o.Name.StartsWith("Spike")) && !all.Any(o => o.Behaviors.Any(b => b.Kind == BehaviorKind.GoToScene))
                   && !all.Any(o => o.Behaviors.Any(b => b.Kind == BehaviorKind.Patrol && o.Name.StartsWith("Enemy")));
        });
    }
}
