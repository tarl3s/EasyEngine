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

        TestScenarios.TryDeleteDir(dir);
    }

    // an object with a Custom script (for scene setups)
    private static GameObjectData ScriptObj(int id, string name, double x, double y, string script) =>
        new() { Id = id, Name = name, Type = "Object", X = x, Y = y, Width = 30, Height = 30, Color = "#FF4D6D", Physics = new PhysicsData { Mode = PhysicsMode.None },
                Behaviors = { new BehaviorData { Kind = BehaviorKind.Custom, Texts = { ["script"] = script } } } };
}
