using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace EasyEngine;

// Scratch blocks with several WHEN / DO / conditions (saved as consecutive rules sharing "block"), and the blocks that
// belong to a behavior the object has (controls, ability, setjump, heal, behavior; landed, hurt, interact).
public partial class App
{
    partial void FeatureTestsMultiBlocks(FullTestKit t)
    {
        static string J(string s) => s.Replace('\'', '"');
        var dir = Path.Combine(Path.GetTempPath(), "EE_Full_Multi_" + Guid.NewGuid().ToString("N")[..6]);
        var project = ProjectIO.CreateNew("Multi", dir);
        var scene = project.GetStartScene();

        // ---------------- the game: one block = any WHEN → every DO, once
        t.Test("blocks: a block with two WHENs (E or click) runs each of its DOs once per event, and once when both happen together", () =>
        {
            var p = t.Sim(J("[{'when':'keypress','key':'E','cooldown':0.3,'do':'addvar','var':'n','amount':1,'block':1},"
                          + "{'when':'keypress','key':'E','cooldown':0.3,'do':'addvar','var':'m','amount':10,'block':1},{'when':'click','block':1}]"), PhysicsMode.None, null);
            p.TKey("E", true); p.TStep(); p.TKey("E", false); p.TSteps(30);
            bool byKey = p.TVar("n") == 1 && p.TVar("m") == 10;
            p.TMouseAt(232, 232); p.TMouseDown(true); p.TStep(); p.TMouseDown(false); p.TSteps(2);
            bool byClick = p.TVar("n") == 2 && p.TVar("m") == 20;
            p.TKey("E", true); p.TMouseDown(true); p.TStep(); p.TKey("E", false); p.TMouseDown(false); p.TSteps(2);
            return byKey && byClick && p.TVar("n") == 3 && p.TVar("m") == 30 && !p.TLogHas("Unknown action");
        });
        t.Test("blocks: a WHEN with no action of its own is fine (no 'Unknown action'), and blocks next to each other stay apart", () =>
        {
            var p = t.Sim(J("[{'when':'start'},{'when':'start','do':'addvar','var':'a','amount':1,'block':1},{'when':'click','block':1},"
                          + "{'when':'start','do':'addvar','var':'b','amount':1,'block':2},{'when':'click','block':2}]"), PhysicsMode.None, null);
            p.TSteps(3);
            return !p.TLogHas("Unknown action") && p.TVar("a") == 1 && p.TVar("b") == 1;
        });
        t.Test("blocks: conditions joined with AND need all of them, with OR any of them", () =>
        {
            double Run(string cond, double a, double b)
            {
                var p = t.Sim(J("[{'when':'always','do':'setvar','var':'r','to':1,'cond':'" + cond + "'}]"), PhysicsMode.None, null);
                p.TSetVar("a", a); p.TSetVar("b", b); p.TSteps(2);
                return p.TVar("r");
            }
            return Run("a >= 1 && b >= 1", 1, 0) == 0 && Run("a >= 1 && b >= 1", 1, 1) == 1
                   && Run("a >= 1 || b >= 1", 0, 1) == 1 && Run("a >= 1 || b >= 1", 0, 0) == 0;
        });
        t.Test("blocks: 'when it dies' as a block's second WHEN runs all of the block's DOs", () =>
        {
            var p = t.Sim(J("[{'when':'click','do':'addvar','var':'d1','amount':1,'block':1},{'when':'click','do':'addvar','var':'d2','amount':1,'block':1},{'when':'death','block':1}]"),
                          PhysicsMode.None, MakeKillable);
            p.TSteps(5);
            return p.TDead("A") && p.TVar("d1") == 1 && p.TVar("d2") == 1;
        });

        // ---------------- the editor: grouped blocks round-trip; single blocks don't change
        var grouped = J("[{'when':'keypress','key':'E','cooldown':0.3,'do':'addvar','var':'n','amount':1,'cond':'a >= 1 || b >= 1','block':1},"
                      + "{'when':'keypress','key':'E','cooldown':0.3,'do':'addvar','var':'m','amount':2,'cond':'a >= 1 || b >= 1','block':1},"
                      + "{'when':'click','cond':'a >= 1 || b >= 1','block':1},{'when':'start','do':'jump','force':600},"
                      + "{'when':'timer','every':2,'do':'say','text':'Hi','block':2},{'when':'touch','name':'B','block':2}]");
        var single = J("[{'when':'start','do':'jump','force':600},{'when':'click','do':'addvar','var':'s','amount':1,'cond':'coins >= 15 && lives > 0'},{'when':'always','do':'destroy','cond':'(a || b) && c'}]");
        t.Test("blocks editor: grouped blocks round-trip byte for byte (renumbered in order), single blocks are saved as before", () =>
            t.RT(grouped) == grouped && t.RT(single) == single
            && t.RT(J("[{'when':'start','do':'jump','force':600,'block':7},{'when':'click','block':7}]")) == J("[{'when':'start','do':'jump','force':600,'block':1},{'when':'click','block':1}]"));
        t.Test("blocks editor: a grouped block plays the same after the blocks", () =>
        {
            string Play(string s) { var p = t.Sim(s, PhysicsMode.None, AddTouchTarget); p.TSetVar("a", 1); p.TKey("E", true); p.TMouseAt(232, 232); p.TMouseDown(true); p.TSteps(5); return p.TFingerprint(); }
            return Play(grouped) == Play(t.RT(grouped));
        });

        System.Windows.Controls.Button Btn(Window w, string tip, int n = 0) => Descendants(w).OfType<System.Windows.Controls.Button>().Where(b => (b.ToolTip as string) == tip).ElementAt(n);
        void Click(System.Windows.Controls.Button b) => b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        t.Test("blocks editor: ＋ Or when / ＋ And do add rows, ✕ removes them; one WHEN + one DO is a plain rule again", () =>
        {
            var w = new BlockEditorWindow(project, scene, J("[{'when':'start','do':'jump','force':600}]"));
            bool noX = !Descendants(w).OfType<System.Windows.Controls.Button>().Any(b => (b.ToolTip as string) is "Remove this event" or "Remove this action");
            Click(Btn(w, "Also run this block when something else happens"));
            var two = w.SerializeTest();
            Click(Btn(w, "Do one more thing in this block"));
            var three = w.SerializeTest();
            Click(Btn(w, "Remove this event", 1));
            var back = w.SerializeTest();
            Click(Btn(w, "Remove this action", 1));
            var plain = w.SerializeTest();
            w.Close();
            return noX && two == J("[{'when':'start','do':'jump','force':600,'block':1},{'when':'click','block':1}]")
                   && three == J("[{'when':'start','do':'jump','force':600,'block':1},{'when':'start','do':'jump','force':600,'block':1},{'when':'click','block':1}]")
                   && back == J("[{'when':'start','do':'jump','force':600,'block':1},{'when':'start','do':'jump','force':600,'block':1}]")
                   && plain == J("[{'when':'start','do':'jump','force':600}]");
        });
        t.Test("blocks editor: conditions open as rows (split at the top-level && or ||), and rows join back with all / any", () =>
        {
            var w = new BlockEditorWindow(project, scene, J("[{'when':'always','do':'destroy','cond':'coins >= 15'}]"));
            Click(Btn(w, "Another thing that must be true (or may be true) for the block to run"));
            var boxes = Descendants(w).OfType<TextBox>().ToList();
            boxes[^1].Text = "lives > 0"; // the new row's advanced formula
            var and = w.SerializeTest();
            var join = Descendants(w).OfType<ComboBox>().First(c => c.Items.OfType<ComboBoxItem>().Any(i => (i.Tag as string) == "any"));
            join.SelectedItem = join.Items.OfType<ComboBoxItem>().First(i => (i.Tag as string) == "any");
            var or = w.SerializeTest();
            w.Close();
            var w2 = new BlockEditorWindow(project, scene, J("[{'when':'always','do':'destroy','cond':'(x > 1 || y > 1) && z == 2'}]"));
            int rows = Descendants(w2).OfType<System.Windows.Controls.Button>().Count(b => (b.ToolTip as string) == "Remove this condition");
            var kept = w2.SerializeTest();
            w2.Close();
            return and == J("[{'when':'always','do':'destroy','cond':'coins >= 15 && lives > 0'}]") && or == J("[{'when':'always','do':'destroy','cond':'coins >= 15 || lives > 0'}]")
                   && rows == 2 && kept == J("[{'when':'always','do':'destroy','cond':'(x > 1 || y > 1) && z == 2'}]");
        });
        t.Test("blocks editor: Duplicate copies the whole block", () =>
        {
            var w = new BlockEditorWindow(project, scene, J("[{'when':'start','do':'jump','force':600,'block':1},{'when':'click','block':1}]"));
            Click(Btn(w, "Duplicate"));
            var json = w.SerializeTest(); w.Close();
            return json == J("[{'when':'start','do':'jump','force':600,'block':1},{'when':'click','block':1},{'when':'start','do':'jump','force':600,'block':2},{'when':'click','block':2}]");
        });

        // ---------------- the palette: behavior blocks only for objects with that behavior
        t.Test("blocks palette: behavior blocks show only for an object that has the behavior (a block already chosen stays)", () =>
        {
            List<string> Tags(ComboBox c) => c.Items.OfType<ComboBoxItem>().Select(i => i.Tag as string ?? "").ToList();
            (List<string> whens, List<string> dos) Offer(GameObjectData? o, string json = "[{'when':'start','do':'jump'}]")
            {
                var w = new BlockEditorWindow(project, scene, J(json), o);
                var combos = Descendants(w).OfType<ComboBox>().ToList();
                var r = (Tags(combos.First(c => Tags(c).Contains("start"))), Tags(combos.First(c => Tags(c).Contains("velocity"))));
                w.Close();
                return r;
            }
            var box = new GameObjectData { Name = "Box" };
            var hero = new GameObjectData { Name = "Hero", Behaviors = { new BehaviorData { Kind = BehaviorKind.Platformer }, new BehaviorData { Kind = BehaviorKind.Health } } };
            var bare = Offer(box); var plat = Offer(hero); var any = Offer(null); var kept = Offer(box, "[{'when':'landed','do':'controls','on':'off'}]");
            string[] behaviorBlocks = { "controls", "ability", "setjump", "behavior" }; // (heal can heal another object: offered everywhere)
            return !bare.dos.Intersect(behaviorBlocks).Any() && bare.dos.Contains("heal") && !bare.whens.Contains("landed") && !bare.whens.Contains("death") && !bare.whens.Contains("hurt")
                   && behaviorBlocks.All(plat.dos.Contains) && plat.whens.Contains("landed") && plat.whens.Contains("hurt") && plat.whens.Contains("death") && !plat.whens.Contains("interact")
                   && behaviorBlocks.All(any.dos.Contains) && any.whens.Contains("interact")
                   && kept.dos.Contains("controls") && kept.whens.Contains("landed")
                   && t.RT(J("[{'when':'landed','do':'controls','on':'off'}]")) == J("[{'when':'landed','do':'controls','on':'off'}]");
        });
        t.Test("blocks palette: the 'behavior' block offers the object's own behaviors", () =>
        {
            var enemy = new GameObjectData { Name = "Enemy", Behaviors = { new BehaviorData { Kind = BehaviorKind.Patrol }, new BehaviorData { Kind = BehaviorKind.Shooter }, new BehaviorData { Kind = BehaviorKind.Health } } };
            var w = new BlockEditorWindow(project, scene, J("[{'when':'start','do':'behavior'}]"), enemy);
            var kinds = Descendants(w).OfType<ComboBox>().Select(c => c.Items.OfType<ComboBoxItem>().Select(i => i.Tag as string ?? "").ToList()).First(l => l.Contains("Patrol"));
            var json = w.SerializeTest(); w.Close();
            return kinds.SequenceEqual(new[] { "Patrol", "Shooter" }) && json == J("[{'when':'start','do':'behavior','kind':'Patrol','on':'off'}]");
        });

        // ---------------- the behavior blocks play
        t.Test("behavior block: controls off ⇒ the arrow keys don't move it; on ⇒ they do (Platformer and Top-down)", () =>
        {
            double Moved(string on, bool topdown)
            {
                var p = t.Sim(J("[{'when':'start','do':'controls','on':'" + on + "'}]"), PhysicsMode.None, topdown ? TopDownA : PlatformerA);
                p.TSteps(30); double x0 = p.TX("A");
                p.TKey("Right", true); p.TSteps(15);
                return p.TX("A") - x0;
            }
            return Math.Abs(Moved("off", false)) < 1 && Moved("on", false) > 20 && Math.Abs(Moved("off", true)) < 1 && Moved("on", true) > 20;
        });
        t.Test("behavior block: ability turns double jump / dash on and off; setjump changes the jump strength (and it jumps that high)", () =>
        {
            var p = t.Sim(J("[{'when':'start','do':'ability','ability':'doubleJump','on':'on'},{'when':'start','do':'ability','ability':'dash'},"
                          + "{'when':'start','do':'setjump','jump':900},{'when':'keypress','key':'K','do':'ability','ability':'doubleJump','on':'off'}]"), PhysicsMode.None, PlatformerA);
            p.TSteps(30);
            bool on = p.TBehNum("A", BehaviorKind.Platformer, "doubleJump") == 1 && p.TBehNum("A", BehaviorKind.Platformer, "dash") == 1 && p.TBehNum("A", BehaviorKind.Platformer, "jump") == 900;
            bool grounded = p.TGrounded("A");
            p.TKey("Space", true); p.TStep();
            bool high = p.TVY("A") < -800;
            p.TKey("Space", false); p.TKey("K", true); p.TStep();
            return on && grounded && high && p.TBehNum("A", BehaviorKind.Platformer, "doubleJump") == 0;
        });
        t.Test("behavior block: heal gives health back, never above its maximum", () =>
        {
            var p = t.Sim(J("[{'when':'keypress','key':'H','do':'heal','amount':1},{'when':'keypress','key':'J','do':'heal','amount':5}]"), PhysicsMode.None, HealthA);
            p.TSteps(2); p.TSetHp("A", 1);
            p.TKey("H", true); p.TStep(); p.TKey("H", false);
            double one = p.THp("A");
            p.TKey("J", true); p.TStep(); p.TKey("J", false);
            return one == 2 && p.THp("A") == 3;
        });
        t.Test("behavior block: behavior off ⇒ its Patrol stops (and on again ⇒ it walks)", () =>
        {
            var p = t.Sim(J("[{'when':'after','seconds':0.2,'do':'behavior','kind':'Patrol','on':'off'},{'when':'keypress','key':'P','do':'behavior','kind':'patrol','on':'on'}]"), PhysicsMode.None, PatrolA);
            p.TSteps(3); bool walks = Math.Abs(p.TVX("A")) > 1;
            p.TSteps(20); double x0 = p.TX("A"); p.TSteps(10);
            bool stopped = Math.Abs(p.TVX("A")) < 0.01 && Math.Abs(p.TX("A") - x0) < 0.01;
            p.TKey("P", true); p.TSteps(3);
            return walks && stopped && Math.Abs(p.TVX("A")) > 1;
        });
        t.Test("behavior event: landed fires once when it lands (not while standing, not for one placed on the floor)", () =>
        {
            var p = t.Sim(J("[{'when':'landed','do':'addvar','var':'lands','amount':1}]"), PhysicsMode.None, PlatformerA);
            p.TSteps(60); double once = p.TVar("lands"); p.TSteps(30);
            var q = t.Sim(J("[{'when':'landed','do':'addvar','var':'lands','amount':1}]"), PhysicsMode.None, (pr, s) => { PlatformerA(pr, s); s.Layers.SelectMany(l => l.Objects).First(o => o.Name == "A").Y = 236; });
            q.TSteps(40);
            return once == 1 && p.TVar("lands") == 1 && p.TGrounded("A") && q.TVar("lands") == 0 && q.TGrounded("A");
        });
        t.Test("behavior event: hurt fires the moment its health goes down", () =>
        {
            var p = t.Sim(J("[{'when':'hurt','do':'addvar','var':'hurts','amount':1}]"), PhysicsMode.None, HealthA);
            p.TSteps(3); bool none = p.TVar("hurts") == 0;
            p.THurt("A", 1, 0); p.TSteps(5);
            return none && p.TVar("hurts") == 1 && p.THp("A") == 2;
        });
        t.Test("behavior event: interact fires when its Interact is used (once per press)", () =>
        {
            var p = t.Sim(J("[{'when':'interact','do':'addvar','var':'talks','amount':1}]"), PhysicsMode.None, InteractA);
            p.TSteps(2);
            p.TKey("E", true); p.TSteps(5); double one = p.TVar("talks");
            p.TKey("E", false); p.TStep(); p.TKey("E", true); p.TSteps(2);
            return one == 1 && p.TVar("talks") == 2;
        });

        // ---------------- the AI: synonyms, "on" as a switch, a list of actions = one block
        t.Test("blocks AI: new block ids and synonyms (lands, cure, disable controls), \"on\":\"off\" is a switch, \"block\" is kept", () =>
        {
            var notes = new List<string>();
            var r = AiEvalKit.RulesOf(AiSpecFix.Rules(new[] { J("{'when':'lands','do':'cure','amount':2,'block':3}"), J("{'when':'start','do':'disable controls'}"),
                                                               J("{'when':'damaged','do':'controls','on':'off'}") }, notes));
            var blockAi = BlockEditorWindow.ReadAiReply(J("[{'when':'start','do':'controls','on':false},{'when':'landed','do':'ability','ability':'double jump','on':'yes'},{'when':'start','do':'behavior','kind':'patrol','on':'off'}]")).Json;
            return notes.Count == 0 && r.Count == 3 && AiEvalKit.Is(r[0], "when", "landed") && AiEvalKit.Is(r[0], "do", "heal") && AiEvalKit.Is(r[0], "block", "3")
                   && AiEvalKit.Is(r[1], "do", "controls") && AiEvalKit.Is(r[1], "on", "off") && AiEvalKit.Is(r[2], "when", "hurt") && AiEvalKit.Is(r[2], "on", "off")
                   && blockAi == J("[{'when':'start','do':'controls','on':'off'},{'when':'landed','do':'ability','ability':'doubleJump','on':'on'},{'when':'start','do':'behavior','kind':'Patrol','on':'off'}]");
        });
        t.Test("blocks AI: a block the Build AI adds doesn't join the object's last block", () =>
        {
            using var d = System.Text.Json.JsonDocument.Parse(J("[{'when':'click','do':'destroy','block':1},{'when':'start','block':1}]"));
            var merged = EasyEngine.MainWindow.MergeRules(J("[{'when':'start','do':'jump','block':1},{'when':'click','block':1}]"), d.RootElement);
            return merged == J("[{'when':'start','do':'jump','block':1},{'when':'click','block':1},{'when':'click','do':'destroy','block':2},{'when':'start','block':2}]");
        });
        t.Test("blocks AI: the prompt says what the object has", () =>
        {
            var hero = new GameObjectData { Name = "Hero", Behaviors = { new BehaviorData { Kind = BehaviorKind.Platformer } } };
            var w = new BlockEditorWindow(project, scene, "[]", hero, (u, s) => System.Threading.Tasks.Task.FromResult("[]"));
            var prompt = w.AiSystemPrompt("turn off its controls"); w.Close();
            return prompt.Contains("This object has: Platformer.") && prompt.Contains("controls (Platformer/TopDown)");
        });

        TestScenarios.TryDeleteDir(dir);
    }

    // A: a Platformer (keyboard on) above a floor, falling onto it
    private static void PlatformerA(Project p, Scene s)
    {
        var layer = s.Layers.First(l => l.Kind == LayerKind.Base);
        var a = layer.Objects.First(o => o.Name == "A");
        a.Physics.Mode = PhysicsMode.Dynamic; a.Y = 200;
        a.Behaviors.Insert(0, new BehaviorData { Kind = BehaviorKind.Platformer, Nums = { ["controls"] = 1 } });
        layer.Objects.Add(new GameObjectData { Id = 2, Name = "Floor", Type = "Object", X = 100, Y = 300, Width = 900, Height = 40, Color = "#555555", Physics = new PhysicsData { Mode = PhysicsMode.Static }, Behaviors = { new BehaviorData { Kind = BehaviorKind.Solid } } });
    }

    private static void TopDownA(Project p, Scene s)
        => s.Layers.First(l => l.Kind == LayerKind.Base).Objects.First(o => o.Name == "A").Behaviors.Insert(0, new BehaviorData { Kind = BehaviorKind.TopDown, Nums = { ["controls"] = 1 } });

    private static void HealthA(Project p, Scene s)
        => s.Layers.First(l => l.Kind == LayerKind.Base).Objects.First(o => o.Name == "A").Behaviors.Insert(0, new BehaviorData { Kind = BehaviorKind.Health, Nums = { ["max"] = 3, ["respawn"] = 0 }, Texts = { ["var"] = "ahp" } });

    private static void PatrolA(Project p, Scene s)
        => s.Layers.First(l => l.Kind == LayerKind.Base).Objects.First(o => o.Name == "A").Behaviors.Insert(0, new BehaviorData { Kind = BehaviorKind.Patrol, Nums = { ["speed"] = 90, ["distance"] = 400 } });

    // A: something to talk to (E near it adds to "x"); the Player stands next to it
    private static void InteractA(Project p, Scene s)
    {
        var layer = s.Layers.First(l => l.Kind == LayerKind.Base);
        layer.Objects.First(o => o.Name == "A").Behaviors.Insert(0, new BehaviorData { Kind = BehaviorKind.Interact, Nums = { ["radius"] = 80 }, Texts = { ["trigger"] = "Player", ["key"] = "E", ["action"] = "addvar", ["var"] = "x" } });
        layer.Objects.Add(new GameObjectData { Id = 3, Name = "Player", Type = "Object", X = 240, Y = 200, Width = 40, Height = 40, Color = "#3BC97A", Physics = new PhysicsData { Mode = PhysicsMode.None } });
    }
}
