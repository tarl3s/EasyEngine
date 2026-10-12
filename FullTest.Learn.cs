using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EasyEngine;

// M4 — Learn mode: the AI explains how to use things from the REAL behavior catalog + the how-to guides, never edits
// the project. Headless editor, no real AI: the prompt, the question → behavior matching, the "?" helper and the modes.
public partial class App
{
    partial void FeatureTestsLearn(FullTestKit t)
    {
        var temp = Path.Combine(Path.GetTempPath(), "EE_FullLearn_" + Guid.NewGuid().ToString("N").Substring(0, 6)); // cleaned with EE_Full*
        Directory.CreateDirectory(temp);
        var project = ProjectIO.CreateNew("Learn", temp);
        var editor = new MainWindow(project); // headless (not shown)

        var par = BehaviorCatalog.Info(BehaviorKind.Parallax);
        bool HasGuide(string prompt, BehaviorKind k) { var g = BehaviorGuides.All[k].Split('\n'); return prompt.Contains(g[0]) && prompt.Contains(g[^1]); }
        bool Matches(string q, params BehaviorKind[] kinds) => kinds.All(EasyEngine.MainWindow.TestMatchBehaviors(q).Contains);

        var full = editor.TestTeachPrompt("how do I use parallax", compact: false);
        t.Test("learn: the full teach prompt has Parallax's description, its guide and \"＋ Add behavior\"", () =>
            full.Contains($"{par.Icon} {par.Name} — {par.Desc}") && HasGuide(full, BehaviorKind.Parallax) && full.Contains("＋ Add behavior"));
        t.Test("learn: fields are shown as friendly labels (no \"(0/1)\"/\"(0-1)\", choices by their labels)", () =>
            full.Contains("Depth (0 = far, 1 = near)") && !full.Contains("(0/1)") && !full.Contains("(0-1)") && full.Contains("Respawn (for the player) / Disappear (for enemies)"));
        t.Test("learn: hidden (legacy) behaviors are left out, every visible one is in", () =>
            !full.Contains("Melee Attack") && BehaviorCatalog.All.Where(i => !i.Hidden).All(i => full.Contains($"{i.Icon} {i.Name} — ")));
        t.Test("learn: the prompt has the editor basics and the reply rules", () =>
            full.Contains("SCENE LAYERS") && full.Contains("F1") && full.Contains("🧩 Build with blocks") && full.Contains("user writes in") && full.Contains("No JSON"));

        t.Test("learn: \"como uso o paralaxe?\" / \"how do I use parallax\" / \"fundo que mexe\" → Parallax", () =>
            Matches("como uso o paralaxe?", BehaviorKind.Parallax) && Matches("how do I use parallax", BehaviorKind.Parallax) && Matches("fundo que mexe", BehaviorKind.Parallax));
        t.Test("learn: \"como faço um chefe?\" → Boss AI; \"dia e noite\" → Day/Night; \"machado\" → Use Item + Hotbar", () =>
            Matches("como faço um chefe?", BehaviorKind.BossAI) && Matches("dia e noite", BehaviorKind.DayNight) && Matches("como uso o machado?", BehaviorKind.ToolUse, BehaviorKind.Hotbar));
        t.Test("learn: names, kinds and loose words match too (\"Trigger Area\", \"DayNight\", \"lantern\")", () =>
            Matches("what is a trigger area", BehaviorKind.Area) && Matches("daynight?", BehaviorKind.DayNight) && Matches("a lantern", BehaviorKind.LightSource));
        t.Test("learn: an unrelated question matches nothing", () => EasyEngine.MainWindow.TestMatchBehaviors("hello there").Count == 0);

        var compact = editor.TestTeachPrompt("como uso o paralaxe?", compact: true);
        t.Test($"learn: the compact (local AI) prompt is much smaller ({compact.Length} vs {full.Length} chars) but keeps the matched guide", () =>
            compact.Length * 2 < full.Length && HasGuide(compact, BehaviorKind.Parallax) && !HasGuide(compact, BehaviorKind.Harvestable)
            && compact.Contains("All behaviors:") && compact.Contains(BehaviorCatalog.Info(BehaviorKind.Harvestable).Name));
        t.Test("learn: a compact prompt with no match falls back to the common behaviors' guides", () =>
        {
            var p = editor.TestTeachPrompt("hello there", compact: true);
            return HasGuide(p, BehaviorKind.Platformer) && HasGuide(p, BehaviorKind.FollowCamera) && !HasGuide(p, BehaviorKind.Parallax);
        });

        var mountains = new GameObjectData { Name = "Mountains" };
        mountains.Behaviors.Add(par.CreateData());
        editor.TestInspect(mountains);
        t.Test("learn: the selected object and its behaviors are in the prompt", () =>
            editor.TestTeachPrompt("what does this do?", compact: true).Contains("Selected object: \"Mountains\" with behaviors: 🏔 Parallax"));

        t.Test("learn: the \"?\" on a behavior card asks about it in Learn mode", () =>
            editor.TestAskHowTo(BehaviorKind.Parallax) == "How do I use 🏔 Parallax? Explain step by step for a beginner."
            && editor.TestAiMode == AiMode.Learn && editor.AiPanel.Visibility == System.Windows.Visibility.Visible);

        foreach (var (mode, send, hint) in new[] { (AiMode.Learn, "Ask", "Ask how to use anything"), (AiMode.Brainstorm, "Send", "Chat about ideas"), (AiMode.Build, "Build it", "Describe what to build") })
            t.Test($"learn: switching to {mode} sets the button to \"{send}\" and its hint", () =>
            {
                editor.TestSetAiMode(mode);
                return editor.TestAiMode == mode && (string)editor.AiSend.Content == send && editor.AiHint.Text.StartsWith(hint);
            });

        t.Test("learn: a Learn answer never changes the project (even if the AI replies with build JSON)", () =>
        {
            var before = JsonSerializer.Serialize(project);
            var (ans, _) = editor.TestTeach("how do I add an enemy?", "{\"create\":[{\"name\":\"Evil\",\"behaviors\":[{\"kind\":\"Chase\"}]}]}");
            return ans.Contains("Evil") && JsonSerializer.Serialize(project) == before && editor.TestFindObject("Evil") == null;
        });
        t.Test("learn: follow-ups get the last 3 Q/A pairs, each trimmed", () =>
        {
            editor.TestTeach("second question", new string('x', 2000));
            var (_, prompt) = editor.TestTeach("third question", "ok");
            editor.TestTeach("fourth?", "ok");
            var (_, later) = editor.TestTeach("fifth?", "ok");
            return prompt.StartsWith("Earlier in this conversation:") && prompt.Contains("Q: how do I add an enemy?") && prompt.EndsWith("New question: third question")
                && !prompt.Contains(new string('x', 601)) && !later.Contains("how do I add an enemy?") && later.Contains("Q: second question");
        });
    }
}
