using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace EasyEngine;

// A review of the Custom rules ("Scratch"): bugs found in the rule runtime, the block editor and its AI reader (each with
// the check that failed before the fix), and the blocks / behavior added for what beginners kept failing to build.
public partial class App
{
    partial void FeatureTestsScratchReview(FullTestKit t)
    {
        static string J(string s) => s.Replace('\'', '"');
        var dir = Path.Combine(Path.GetTempPath(), "EE_Full_Scratch_" + Guid.NewGuid().ToString("N")[..6]);
        var project = ProjectIO.CreateNew("ScratchReview", dir);
        var scene = project.GetStartScene();

        // ---------------- bugs: the game
        t.Test("scratch review: a 'when it dies' rule obeys its 'Only run if' (it used to run anyway)", () =>
        {
            var no = t.Sim(J("[{'when':'death','cond':'allow >= 1','do':'setvar','var':'died','to':1}]"), PhysicsMode.None, MakeKillable);
            no.TSteps(5);
            var yes = t.Sim(J("[{'when':'death','cond':'allow >= 1','do':'setvar','var':'died','to':1}]"), PhysicsMode.None, MakeKillable);
            yes.TSetVar("allow", 1); yes.TSteps(5);
            var block = t.Sim(J("[{'when':'click','cond':'allow >= 1','do':'setvar','var':'d1','to':1,'block':1},{'when':'death','cond':'allow >= 1','block':1}]"), PhysicsMode.None, MakeKillable);
            block.TSteps(5);
            return no.TDead("A") && no.TVar("died") == 0 && yes.TVar("died") == 1 && block.TDead("A") && block.TVar("d1") == 0;
        });
        t.Test("scratch review: 'touch' sees an object that disappeared this very frame (the Heart's own rule ran first)", () =>
        {
            // the Heart comes first in the scene, so its "touch A → destroy" runs before A's "touch Heart → …"
            var p = t.Sim(J("[{'when':'touch','name':'Heart','do':'addvar','var':'healed','amount':1}]"), PhysicsMode.None, (pr, s) =>
                s.Layers.First(l => l.Kind == LayerKind.Base).Objects.Insert(0, ScriptObj(5, "Heart", 230, 230, J("[{'when':'touch','name':'A','do':'destroy'}]"))));
            p.TSteps(4);
            // a Collectible coin (collected by its own behavior) also still counts for the player's touch rule
            var c = t.Sim(J("[{'when':'touch','name':'Coin','do':'addvar','var':'got','amount':1}]"), PhysicsMode.None, (pr, s) =>
                s.Layers.First(l => l.Kind == LayerKind.Base).Objects.Insert(0, new GameObjectData { Id = 6, Name = "Coin", X = 230, Y = 230, Width = 20, Height = 20,
                    Behaviors = { new BehaviorData { Kind = BehaviorKind.Collectible, Texts = { ["collector"] = "A" } } } }));
            c.TSteps(4);
            return p.TDead("Heart") && p.TVar("healed") == 1 && c.TDead("Coin") && c.TVar("got") == 1;
        });
        t.Test("scratch review: an empty 'Only run if' means no condition (a rule with \"cond\":\"\" used to never run)", () =>
        {
            var p = t.Sim(J("[{'when':'start','cond':'','do':'setvar','var':'ran','to':1},{'when':'start','cond':'  ','do':'setvar','var':'ran2','to':1}]"), PhysicsMode.None, null);
            p.TSteps(2);
            return p.TVar("ran") == 1 && p.TVar("ran2") == 1;
        });
        t.Test("scratch review: conditions written with and / or / not / a single = work (they were silently cut short)", () =>
        {
            var p = t.Sim("[]", PhysicsMode.None, null);
            p.TSetVar("a", 1); p.TSetVar("b", 0);
            return p.TEval("a >= 1 and b >= 1") == 0 && p.TEval("a >= 1 or b >= 1") == 1 && p.TEval("not b") == 1 && p.TEval("a = 1") == 1
                   && p.TEval("a = 2") == 0 && p.TEval("a => 1") == 1 && p.TEval("b =< 0") == 1 && p.TEval("a <> b") == 1
                   && p.TEval("a AND not b") == 1 && p.TEval("android + 1") == 1; // a name that starts with "and" stays a name
        });
        t.Test("scratch review: an object with two Custom behaviors runs both rule lists once a frame (the first used to run twice)", () =>
        {
            var p = t.Sim(J("[{'when':'always','do':'addvar','var':'n','amount':1}]"), PhysicsMode.None, (pr, s) =>
                s.Layers.First(l => l.Kind == LayerKind.Base).Objects.First(o => o.Name == "A").Behaviors.Add(
                    new BehaviorData { Kind = BehaviorKind.Custom, Texts = { ["script"] = J("[{'when':'always','do':'addvar','var':'m','amount':1}]") } }));
            p.TSteps(5);
            return p.TVar("n") == 5 && p.TVar("m") == 5;
        });
        t.Test("scratch review: 'when it gets hurt' also fires for the hit that knocks out a character who comes back", () =>
        {
            var p = t.Sim(J("[{'when':'hurt','do':'addvar','var':'hurts','amount':1}]"), PhysicsMode.None, (pr, s) =>
            {
                MakeKillable(pr, s);
                var a = s.Layers.First(l => l.Kind == LayerKind.Base).Objects.First(o => o.Name == "A");
                a.Behaviors.First(b => b.Kind == BehaviorKind.Health).Nums["respawn"] = 1;
            });
            p.TSteps(5);
            return p.TVar("hurts") == 1 && !p.TDead("A");
        });

        // ---------------- bugs: the block editor and its AI reader
        t.Test("scratch review: rules the blocks can't read are kept as they are on Done (they used to be wiped)", () =>
        {
            const string broken = "[{\"when\":\"start\" \"do\":\"jump\"";
            var w = new BlockEditorWindow(project, scene, broken);
            var kept = w.SerializeTest();
            bool warned = Descendants(w).OfType<TextBlock>().Any(b => b.Text.Contains("couldn't be read"));
            w.Close();
            return kept == broken && warned;
        });
        t.Test("scratch review: the blocks' AI reader keeps a block's 'or when' rows and its grouping", () =>
        {
            var got = BlockEditorWindow.ReadAiReply(J("{'rules':[{'when':'keypress','key':'E','do':'addvar','var':'n','amount':1,'block':1},{'when':'click','block':1},"
                                                    + "{'when':'start','do':'jump','force':600}]}"));
            var w = new BlockEditorWindow(project, scene, "[]");
            int blocks = w.AddRules(got.Json);
            var json = w.SerializeTest(); w.Close();
            return got.Dropped.Count == 0 && blocks == 2
                   && json == J("[{'when':'keypress','key':'E','cooldown':0.3,'do':'addvar','var':'n','amount':1,'block':1},{'when':'click','block':1},{'when':'start','do':'jump','force':600}]");
        });
        t.Test("scratch review: the AI retuning a rule of a block keeps it in its block", () =>
        {
            var old = J("[{'when':'start','do':'jump','force':600},{'when':'timer','every':2,'do':'say','text':'Hi','block':1},{'when':'touch','name':'B','block':1}]");
            using var added = System.Text.Json.JsonDocument.Parse(J("[{'when':'timer','every':3,'do':'say','text':'Hi'}]"));
            var merged = EasyEngine.MainWindow.MergeRules(old, added.RootElement);
            using var doc = System.Text.Json.JsonDocument.Parse(merged);
            var rules = doc.RootElement.EnumerateArray().ToList();
            return rules.Count == 3 && rules[1].GetProperty("every").GetDouble() == 3 && rules[1].TryGetProperty("block", out var b) && b.GetInt32() == 1;
        });

        // ---------------- new events: what beginners kept failing to build
        void Press(PlayWindow p, string k, int held = 4, int after = 8) { p.TKey(k, true); p.TSteps(held); p.TKey(k, false); p.TSteps(after); }
        t.Test("scratch review: 'When a key is pressed near an object' — a shop: pay 10, get a Sword, only near and only with the coins", () =>
        {
            var shop = J("[{'when':'keynear','key':'E','name':'P','radius':100,'cond':'coins >= 10','do':'addvar','var':'coins','amount':-10,'block':1},"
                       + "{'when':'keynear','key':'E','name':'P','radius':100,'cond':'coins >= 10','do':'additem','item':'Sword','amount':1,'block':1}]");
            var p = t.Sim(shop, PhysicsMode.None, (pr, s) => s.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(
                new GameObjectData { Id = 7, Name = "P", Type = "Object", X = 900, Y = 200, Width = 40, Height = 40 }));
            p.TSetVar("coins", 15);
            Press(p, "E"); bool far = p.TVar("coins") == 15 && p.TItem("Sword") == 0;
            p.TPlace("P", 280, 200);
            p.TKey("E", true); p.TSteps(30); p.TKey("E", false); p.TSteps(3); // held: once
            bool bought = p.TVar("coins") == 5 && p.TItem("Sword") == 1;
            Press(p, "E"); // only 5 coins left
            return far && bought && p.TVar("coins") == 5 && p.TItem("Sword") == 1;
        });
        t.Test("scratch review: 'When a key hasn't been pressed for a while' + 'When a variable becomes…' — a combo", () =>
        {
            var p = t.Sim(J("[{'when':'keypress','key':'J','cooldown':0.1,'do':'addvar','var':'combo','amount':1},{'when':'keyidle','key':'J','seconds':1,'do':'setvar','var':'combo','to':0},"
                          + "{'when':'keyidle','key':'J','seconds':1,'do':'addvar','var':'resets','amount':1},"
                          + "{'when':'becomes','var':'combo','op':'>=','value':3,'do':'addvar','var':'combos','amount':1,'block':1},{'when':'becomes','var':'combo','op':'>=','value':3,'do':'setvar','var':'combo','to':0,'block':1}]"),
                          PhysicsMode.None, null);
            p.TSteps(80); bool quiet = p.TVar("resets") == 0; // never pressed: nothing to reset
            Press(p, "J"); Press(p, "J"); bool two = p.TVar("combo") == 2;
            p.TSteps(75); bool reset = p.TVar("combo") == 0 && p.TVar("resets") == 1;
            p.TSteps(120); bool once = p.TVar("resets") == 1; // fires once per pause, not again and again
            Press(p, "J"); Press(p, "J"); Press(p, "J");
            return quiet && two && reset && once && p.TVar("combos") == 1 && p.TVar("combo") == 0;
        });
        t.Test("scratch review: 'When a variable becomes…' fires once when it gets there, again only after it was false ('While…' keeps firing)", () =>
        {
            var p = t.Sim(J("[{'when':'becomes','var':'score','op':'>=','value':10,'do':'addvar','var':'once','amount':1},{'when':'ifvar','var':'score','op':'>=','value':10,'do':'addvar','var':'every','amount':1}]"), PhysicsMode.None, null);
            p.TSteps(3); p.TSetVar("score", 12); p.TSteps(10);
            bool first = p.TVar("once") == 1 && p.TVar("every") >= 10;
            p.TSetVar("score", 15); p.TSteps(5); bool still = p.TVar("once") == 1;
            p.TSetVar("score", 2); p.TSteps(2); p.TSetVar("score", 10); p.TSteps(2);
            return first && still && p.TVar("once") == 2;
        });
        t.Test("scratch review: 'When none of an object are left' — all the coins (Coin, Coin_2, Coin3) collected → once", () =>
        {
            var p = t.Sim(J("[{'when':'allgone','name':'Coin','do':'addvar','var':'done','amount':1}]"), PhysicsMode.None, (pr, s) =>
            {
                var l = s.Layers.First(x => x.Kind == LayerKind.Base);
                int id = 10;
                foreach (var n in new[] { "Coin", "Coin_2", "Coin3", "Coins", "Coinage" })
                    l.Objects.Add(new GameObjectData { Id = id++, Name = n, Type = "Object", X = 1000 + id * 50, Y = 600, Width = 20, Height = 20 });
            });
            p.TSteps(3); p.TDestroyAll("Coin"); p.TDestroyAll("Coin3"); p.TSteps(3);
            bool notYet = p.TVar("done") == 0;
            p.TDestroyAll("Coin_2"); p.TSteps(5);
            return notYet && p.TVar("done") == 1 && PlayWindow.InFamily("coin_12", "Coin") && !PlayWindow.InFamily("Coins", "Coin") && !PlayWindow.InFamily("Coin_", "Coin");
        });
        t.Test("scratch review: a Heart heals the Player who touches it (never above the maximum) and disappears", () =>
        {
            var heart = J("[{'when':'touch','name':'A','do':'heal','target':'A','amount':1,'block':1},{'when':'touch','name':'A','do':'destroy','block':1}]");
            var p = t.Sim("[]", PhysicsMode.None, (pr, s) =>
            {
                HealthA(pr, s);
                s.Layers.First(l => l.Kind == LayerKind.Base).Objects.Insert(0, ScriptObj(8, "Heart", 900, 200, heart));
                s.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(ScriptObj(9, "Heart2", 900, 600, heart));
                s.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(ScriptObj(10, "Heart3", 900, 1000, heart));
            });
            p.TSteps(2); p.TSetHp("A", 1);
            p.TPlace("A", 890, 190); p.TSteps(3);
            bool healed = p.THp("A") == 2 && p.TDead("Heart");
            p.TPlace("A", 890, 590); p.TSteps(3); bool two = p.THp("A") == 3 && p.TDead("Heart2");
            p.TPlace("A", 890, 990); p.TSteps(3); // already full: stays at its maximum
            return healed && two && p.THp("A") == 3 && p.TDead("Heart3");
        });
        t.Test("scratch review: the 'start moving after the Player steps on it' recipe — a Patrol platform waits, then moves", () =>
        {
            var recipe = BlockEditorWindow.Recipes().First(r => r.json.Contains("\"behavior\"") && r.json.Contains("touch")).json;
            var p = t.Sim(t.RT(recipe), PhysicsMode.None, (pr, s) =>
            {
                var a = s.Layers.First(l => l.Kind == LayerKind.Base).Objects.First(o => o.Name == "A");
                a.Physics.Mode = PhysicsMode.Static; a.Behaviors.Insert(0, new BehaviorData { Kind = BehaviorKind.Solid });
                a.Behaviors.Insert(1, new BehaviorData { Kind = BehaviorKind.Patrol, Nums = { ["axis"] = 1, ["distance"] = 120, ["speed"] = 80 } });
                s.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(new GameObjectData { Id = 5, Name = "Player", Type = "Object", X = 900, Y = 100, Width = 30, Height = 40 });
            });
            p.TSteps(5); double y0 = p.TY("A"); p.TSteps(60); bool waits = Math.Abs(p.TY("A") - y0) < 3;
            p.TPlace("Player", 210, p.TY("A") - 40); p.TSteps(60); // standing on it
            return waits && Math.Abs(p.TY("A") - y0) > 20;
        });

        // ---------------- the Lives behavior
        static void LivesA(Project pr, Scene s, string scene = "S2", string back = "start", bool health = false)
        {
            var l = s.Layers.First(x => x.Kind == LayerKind.Base);
            var a = l.Objects.First(o => o.Name == "A");
            var lives = new BehaviorData { Kind = BehaviorKind.Lives, Nums = { ["lives"] = 3, ["invincible"] = 0.5, ["show"] = 1 }, Texts = { ["var"] = "lives", ["hazard"] = "Lava", ["back"] = back, ["scene"] = scene } };
            a.Behaviors.Insert(0, lives);
            if (health) a.Behaviors.Insert(0, new BehaviorData { Kind = BehaviorKind.Health, Nums = { ["max"] = 2, ["respawn"] = 1, ["invincible"] = 0.2 } });
            l.Objects.Add(new GameObjectData { Id = 20, Name = "Lava", Type = "Object", X = 1000, Y = 200, Width = 120, Height = 30 });
            l.Objects.Add(new GameObjectData { Id = 21, Name = "Lava_2", Type = "Object", X = 1400, Y = 200, Width = 120, Height = 30 });
            AddScene2(pr, s);
        }
        t.Test("scratch review: Lives — touching the hazard (or a copy) costs one and sends it back; hearts show the count; none left → the scene", () =>
        {
            var p = t.Sim("[]", PhysicsMode.None, (pr, s) => LivesA(pr, s));
            p.TSteps(2);
            bool start = p.TVar("lives") == 3 && p.TLivesHud("A") == "♥ ♥ ♥";
            p.TPlace("A", 1010, 190); p.TSteps(2);
            bool lost = p.TVar("lives") == 2 && p.TX("A") == 200 && p.TY("A") == 200 && p.TLivesHud("A") == "♥ ♥";
            p.TPlace("A", 1410, 190); p.TSteps(2); bool safe = p.TVar("lives") == 2; // the safe time after losing one
            p.TPlace("A", 600, 600); p.TSteps(40); p.TPlace("A", 1410, 190); p.TSteps(2);
            bool copy = p.TVar("lives") == 1 && p.TX("A") == 200;
            p.TPlace("A", 600, 600); p.TSteps(40); p.TPlace("A", 1010, 190); p.TSteps(3);
            return start && lost && safe && copy && p.TScene == "S2" && p.TLogHas("No lives left");
        });
        t.Test("scratch review: Lives — Health running out costs a life (and 'when it dies' still runs); a rule taking 1 works too", () =>
        {
            var p = t.Sim(J("[{'when':'death','do':'addvar','var':'deaths','amount':1}]"), PhysicsMode.None, (pr, s) => LivesA(pr, s, health: true));
            p.TSteps(2); p.TPlace("A", 400, 400);
            p.THurt("A", 1, 0); p.TSteps(25); p.THurt("A", 1, 0); p.TSteps(2);
            bool byHealth = p.TVar("lives") == 2 && p.THp("A") == 2 && p.TX("A") == 200 && p.TVar("deaths") == 1 && !p.TDead("A");
            return byHealth && p.TX("A") == 200;
            return byHealth && p.TX("A") == 200;
        });
        t.Test("scratch review: Lives — falling out of the level costs one; with no scene picked, none left starts the scene again with full lives", () =>
        {
            var p = t.Sim("[]", PhysicsMode.Dynamic, (pr, s) => LivesA(pr, s, scene: ""));
            p.TSteps(2); p.TPlace("A", 200, 4000 + 260); p.TSteps(1);
            bool fell = p.TVar("lives") == 2 && p.TY("A") < 400 && !p.TDead("A");
            p.TSetVar("lives", 0); p.TSteps(3);
            return fell && p.TVar("lives") == 3 && p.TLogHas("the scene starts again") && p.TExists("A") && p.TScene != "S2";
        });
        t.Test("scratch review: Lives is in the catalog with every field picked (none typed), a how-to guide and the AI's hint", () =>
        {
            var info = BehaviorCatalog.Info(BehaviorKind.Lives);
            return info.Texts.All(x => x.Pick != PickKind.Text && x.Pick != PickKind.LongText) && info.Nums.Count == 3
                   && BehaviorGuides.All.ContainsKey(BehaviorKind.Lives) && EasyEngine.MainWindow.HintsFor("the player has 3 lives").Any(h => h.Kinds.Contains(BehaviorKind.Lives));
        });

        // ---------------- the AI: synonyms for the new events, "when it reaches" means the moment
        t.Test("scratch review: AI synonyms for the new events (reaches, all collected, press near, idle) and their fields", () =>
        {
            var notes = new List<string>();
            var r = AiEvalKit.RulesOf(AiSpecFix.Rules(new[] { J("{'when':'reaches','var':'score','op':'atleast','value':10,'do':'say','text':'Yes'}"),
                J("{'when':'all collected','object':'Coin','do':'goscene','scene':'S2'}"), J("{'when':'press near','key':'E','target':'Player','distance':80,'do':'additem','item':'Sword'}"),
                J("{'when':'idle','key':'J','time':1,'do':'setvar','var':'combo','value':0}") }, notes));
            var blocks = BlockEditorWindow.ReadAiReply(J("[{'when':'pressnear','key':'E','object':'Player','distance':80,'do':'say','text':'Hi'}]")).Json;
            var fixedIfvar = AiEvalKit.RulesOf(AiSpecFix.ForRequest(J("[{'when':'ifvar','var':'combo','op':'>=','value':3,'do':'say','text':'Combo!'},{'when':'ifvar','var':'gold','op':'>=','value':20,'do':'goscene','scene':'S2'}]"), "when combo reaches 3 say Combo", "Player"));
            return notes.Count == 0 && r.Count == 4
                   && AiEvalKit.Is(r[0], "when", "becomes") && AiEvalKit.Is(r[0], "op", ">=")
                   && AiEvalKit.Is(r[1], "when", "allgone") && AiEvalKit.Is(r[1], "name", "Coin")
                   && AiEvalKit.Is(r[2], "when", "keynear") && AiEvalKit.Is(r[2], "name", "Player") && AiEvalKit.Is(r[2], "radius", "80")
                   && AiEvalKit.Is(r[3], "when", "keyidle") && AiEvalKit.Is(r[3], "seconds", "1") && AiEvalKit.Is(r[3], "to", "0")
                   && blocks == J("[{'when':'keynear','key':'E','name':'Player','radius':80,'do':'say','text':'Hi'}]")
                   && AiEvalKit.Is(fixedIfvar[0], "when", "becomes") && AiEvalKit.Is(fixedIfvar[1], "when", "ifvar");
        });

        // ---------------- the "Lives & Blocks Test" scene of the EasyEngine Tests project, played step by step
        foreach (var (step, ok, lines) in RunLivesBlocksE2E())
            t.Test("scratch review e2e: " + step + (ok ? "" : "  ⟶ " + string.Join(" | ", lines.TakeLast(3))), () => ok);

        TestScenarios.TryDeleteDir(dir);
    }

    // an object with a Custom script (for scene setups)
    private static GameObjectData ScriptObj(int id, string name, double x, double y, string script) =>
        new() { Id = id, Name = name, Type = "Object", X = x, Y = y, Width = 30, Height = 30, Color = "#FF4D6D", Physics = new PhysicsData { Mode = PhysicsMode.None },
                Behaviors = { new BehaviorData { Kind = BehaviorKind.Custom, Texts = { ["script"] = script } } } };
}
