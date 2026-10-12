using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyEngine;

// M4 on the real editor: the three AI mode buttons (active = green with dark text), Learn's hint and "Ask" button,
// and a Parallax card's "?" filling the question — without sending anything to an AI.
public partial class App
{
    partial void SelfTestLearn(SelfTestContext c)
    {
        var ed = c.Editor;
        Brush B(string key) => (Brush)Current.FindResource(key);
        var buttons = new[] { (ed.AiModeBuild, AiMode.Build), (ed.AiModeBrainstorm, AiMode.Brainstorm), (ed.AiModeLearn, AiMode.Learn) };
        string? Colours(AiMode active) => buttons
            .Where(b => b.Item1.Background != B(b.Item2 == active ? "Accent" : "Bg2") || b.Item1.Foreground != B(b.Item2 == active ? "AccentText" : "Text"))
            .Select(b => $"the {b.Item2} button has the wrong colours with {active} active").FirstOrDefault();
        bool HasHelpButton(DependencyObject d) =>
            d is Button { Content: "?", ToolTip: "How does this work? (asks the AI in Learn mode)" } || LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>().Any(HasHelpButton);

        ed.TestSetAiMode(AiMode.Learn);
        var err = Colours(AiMode.Learn)
            ?? ((string)ed.AiSend.Content != "Ask" ? "Learn's send button doesn't say Ask" : null)
            ?? (!ed.AiHint.Text.StartsWith("Ask how to use anything") ? "Learn's hint is wrong: " + ed.AiHint.Text : null);

        // a real Parallax card has its "?"; the helper switches to Learn and fills the question (not sent)
        var mountains = new GameObjectData { Name = "Mountains" };
        mountains.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Parallax).CreateData());
        ed.TestInspect(mountains);
        bool hasHelp = HasHelpButton(ed.InspectorRoot);
        ed.TestSetAiMode(AiMode.Build);
        var q = ed.TestAskHowTo(BehaviorKind.Parallax);
        err ??= !hasHelp ? "the behavior card has no \"?\" button"
              : q != "How do I use 🏔 Parallax? Explain step by step for a beginner." ? "the \"?\" question is wrong: " + q
              : Colours(AiMode.Learn);
        c.Write($"learn mode: buttons/hint/send/\"?\" {(err == null ? "OK" : "FAILED")} — \"?\" asks \"{q}\"");

        // leave the editor as it was: Build mode, empty box, nothing selected
        ed.AiInput.Clear();
        ed.TestSetAiMode(AiMode.Build);
        ed.TestInspect(null!);
        err ??= Colours(AiMode.Build);
        c.LateChecks.Add(() => err == null ? null : "learn mode: " + err);
    }
}
