using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyEngine;

// Regression tests for fixes after v0.10.0: steps aimed at "a point" keep it when saved, an AI answer that arrives
// after an undo isn't written into the dead scene, decimal commas / "%" numbers, the active tab readable on hover,
// and old projects getting the settings their behaviors gained since.
public partial class App
{
    partial void FeatureTestsFixes(FullTestKit t)
    {
        static string J(string s) => s.Replace('\'', '"');

        // ---------------- 1) "" = a point (x, y) for camera.object / spawn.at / moveto.to — kept by Serialize
        t.Test("fix: cutscene camera + spawn aimed at a point keep it through save → load", () =>
        {
            var c = SequenceJson.ParseCutscene(J("{'steps':[{'do':'camera','object':'','x':600,'y':120},{'do':'spawn','object':'Box','at':'','x':5,'y':6}]}"), out var err);
            var back = SequenceJson.ParseCutscene(SequenceJson.Serialize(c), out var err2).Steps;
            return err == null && err2 == null && back.Count == 2 && back[0].Str("object", "?") == "" && back[0].Num("x") == 600 && back[0].Num("y") == 120
                   && back[1].Str("at", "?") == "" && back[1].Num("x") == 5;
        });
        t.Test("fix: a boss 'Glide to (600,120)' still glides to the point after save → load (was onto the player)", () =>
        {
            var b = SequenceJson.ParseBoss(J("{'attacks':{'Glide':[{'do':'moveto','to':'','x':600,'y':120}]}}"), out _);
            var step = SequenceJson.ParseBoss(SequenceJson.Serialize(b), out _).Attack("Glide")?.Steps.Single();
            return step?.Str("to", "?") == "" && step.Num("x") == 600 && step.Num("y") == 120;
        });
        t.Test("fix: fields left at their default / empty texts with an empty default are still omitted", () =>
        {
            var json = SequenceJson.StepJson(new SeqStep { Do = "camera", P = { ["object"] = "self", ["x"] = "0", ["y"] = "0", ["time"] = "1" } }, SeqMode.Cutscene);
            var say = SequenceJson.StepJson(new SeqStep { Do = "say", P = { ["text"] = "Hi", ["speaker"] = "" } }, SeqMode.Cutscene);
            return json.Contains("\"object\":\"self\"") && !say.Contains("speaker");
        });

        // ---------------- 3) numbers: decimal comma, no thousands separator, "%" only where it's a percentage
        t.Test("fix: \"0,5\" / \"1,5\" read as 0.5 / 1.5 (were 5 / 15); \"1.5\" unchanged; no thousands separators", () =>
        {
            using var d = JsonDocument.Parse("[\"0,5\",\"1,5\",\"1.5\",\" 2 \"]");
            var v = d.RootElement.EnumerateArray().Select(e => LooseJson.TryNumber(e, out var n) ? n : double.NaN).ToArray();
            return v.SequenceEqual(new[] { 0.5, 1.5, 1.5, 2.0 }) && !LooseJson.TryParse("1,000.5", out _);
        });
        t.Test("fix: \"25%\" is rejected as a plain number but read as 25 where a percentage is meant", () =>
        {
            using var d = JsonDocument.Parse("\"25%\"");
            return !LooseJson.TryNumber(d.RootElement, out _) && LooseJson.TryNumber(d.RootElement, out var p, percent: true) && p == 25;
        });
        t.Test("fix: boss phase health \"40%\" → 40; step values \"0,5\" → 0.5 and \"50%\" seconds → the default", () =>
        {
            var b = SequenceJson.ParseBoss(J("{'attacks':{'A':[{'do':'wait','time':'0,5'},{'do':'wait','time':'50%'}]},'phases':[{'name':'P','health':'40%','attacks':['A']}]}"), out _);
            var steps = b.Attack("A")!.Steps;
            var saved = SequenceJson.StepJson(steps[0], SeqMode.Boss);
            return b.Phases.Single().When == "health" && b.Phases[0].Value == 40 && steps[0].Num("time") == 0.5 && steps[1].Num("time", 1) == 1
                   && saved.Contains("\"time\":0.5");
        });

        // ---------------- 2) an AI answer arriving after an undo isn't written into the scene that was undone
        t.Test("fix: 'Make it' finishing after an undo is not applied, keeps redo, and says why", () =>
        {
            var temp = Path.Combine(Path.GetTempPath(), "EE_FullFix_" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(temp);
            var project = ProjectIO.CreateNew("Fix", temp);
            var ed = new MainWindow(project);
            ed.TestApplySpec("{\"create\":[{\"name\":\"Golem\",\"behaviors\":[{\"kind\":\"bossai\"}]}]}");
            var bh = ed.TestFindObject("Golem")!.Behaviors.First(b => b.Kind == BehaviorKind.BossAI);
            var ok = new SeqAiResult(true, "{\"attacks\":{\"Stomp\":[{\"do\":\"wait\"}]}}", new List<string>(), "", 1);
            var (undo0, _) = ed.TestHistory;
            ed.TestUndo(); // the scene is replaced while the AI "works"
            var (undo1, redo1) = ed.TestHistory;
            var stale = ed.TestApplySeqAi(bh, SeqMode.Boss, ok);
            var (undo2, redo2) = ed.TestHistory;
            bool notApplied = stale.Contains("not applied") && undo2 == undo1 && redo2 == redo1 && redo1 > 0 && undo0 > undo1;
            ed.TestRedo();
            var live = ed.TestFindObject("Golem")!.Behaviors.First(b => b.Kind == BehaviorKind.BossAI);
            var applied = ed.TestApplySeqAi(live, SeqMode.Boss, ok);
            return notApplied && applied.StartsWith("✓") && live.Text("script").Contains("Stomp");
        });

        // ---------------- 5) the active (green) tab / mode button keeps a green background on hover
        t.Test("fix: CardButton hovered while active (Accent) turns AccentHover, not dark Bg3 (dark text vanished)", () =>
        {
            var st = Current.TryFindResource("CardButton") as Style;
            var tpl = st?.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == Control.TemplateProperty)?.Value as ControlTemplate;
            var accent = Current.TryFindResource("Accent");
            return tpl != null && tpl.Triggers.OfType<MultiTrigger>().Any(m =>
                m.Conditions.Any(c => c.Property == UIElement.IsMouseOverProperty && Equals(c.Value, true))
                && m.Conditions.Any(c => c.Property == Control.BackgroundProperty && ReferenceEquals(c.Value, accent))
                && m.Setters.OfType<Setter>().Any(s => s.Property == Border.BackgroundProperty && ReferenceEquals(s.Value, Current.TryFindResource("AccentHover"))));
        });

        // ---------------- 6) old projects: behavior keys added since are filled with the catalog default on load
        t.Test("fix: an old Minimap without \"maxKey\" gets \"M\" when the editor opens it; set values are kept", () =>
        {
            var p = new Project();
            var s = new Scene(); var l = new Layer();
            var old = new BehaviorData { Kind = BehaviorKind.Minimap, Nums = { ["canMax"] = 1 } };
            var custom = BehaviorCatalog.Info(BehaviorKind.Minimap).CreateData(); custom.Texts["maxKey"] = "Tab";
            l.Objects.Add(new GameObjectData { Behaviors = { old, custom } });
            s.Layers.Add(l); p.Scenes.Add(s);
            p.Prefabs.Add(new PrefabDef { Template = new GameObjectData { Behaviors = { new BehaviorData { Kind = BehaviorKind.Minimap } } } });
            EasyEngine.MainWindow.FillMissingBehaviorKeys(p);
            return old.Text("maxKey") == "M" && old.Num("canMax") == 1 && custom.Text("maxKey") == "Tab"
                   && p.Prefabs[0].Template.Behaviors[0].Text("maxKey") == "M";
        });
    }
}
