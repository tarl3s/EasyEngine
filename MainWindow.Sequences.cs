using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyEngine
{
    // The Boss AI / Cutscene card in the inspector: a summary, "🧩 Advanced Scratch…" (the block editor), the JSON itself
    // (checked with the same parser the game uses), and "✨ Ask the AI" to make or change it from a description.
    public partial class MainWindow
    {
        private readonly ConditionalWeakTable<BehaviorData, string> _seqAiStatus = new(); // survives the card being rebuilt, not the behavior
        private readonly HashSet<BehaviorData> _seqAiBusy = new(); // behaviors with an AI request in flight (one at a time)

        private bool SceneHasBehavior(BehaviorData bh) => _scene.Layers.Any(l => l.Objects.Any(o => o.Behaviors.Contains(bh)));

        partial void SequenceCard(GameObjectData obj, BehaviorData bh, Panel panel)
        {
            var mode = bh.Kind == BehaviorKind.BossAI ? SeqMode.Boss : SeqMode.Cutscene;
            var starter = BehaviorCatalog.Info(bh.Kind).CreateData().Text("script");
            string Script() => bh.Text("script", starter);

            var summary = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6), Tag = "seq-summary" };
            void ShowSummary()
            {
                summary.Text = SequenceJson.Summary(mode, Script(), out bool ok);
                summary.Foreground = (SolidColorBrush)FindResource(ok ? "Accent" : "Danger");
            }
            ShowSummary();
            panel.Children.Add(summary);

            var scratch = new Button { Content = "🧩  Advanced Scratch…", Style = (Style)FindResource("AccentButton"), Height = 36, Margin = new Thickness(0, 0, 0, 8), HorizontalContentAlignment = HorizontalAlignment.Center, Tag = "seq-scratch" };
            scratch.Click += (s, e) =>
            {
                var win = new SequenceEditorWindow(_project, _scene, mode, Script(), obj.Name) { Owner = this };
                if (win.ShowDialog() != true) return;
                PushUndo(); bh.Texts["script"] = win.ResultJson; Dirty();
                RefreshAfterChoiceAlways(obj);
            };
            panel.Children.Add(scratch);

            panel.Children.Add(new TextBlock { Text = mode == SeqMode.Boss ? "Boss (JSON) — the blocks above edit this for you" : "Steps (JSON) — the blocks above edit this for you", Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11, Margin = new Thickness(0, 2, 0, 3) });
            var json = new TextBox
            {
                Text = Script(), AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, Height = 150, Tag = "seq-json",
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"), FontSize = 11, VerticalContentAlignment = VerticalAlignment.Top
            };
            json.LostFocus += (s, e) =>
            {
                if (json.Text == Script()) return;
                PushUndo(); bh.Texts["script"] = json.Text; Dirty(); // kept even when it doesn't parse — the summary says why
                ShowSummary();
            };
            panel.Children.Add(json);

            // ---- ✨ Ask the AI
            panel.Children.Add(new TextBlock { Text = "✨ Ask the AI", FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(0, 10, 0, 4) });
            var ask = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 54, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Tag = "seq-ask", VerticalContentAlignment = VerticalAlignment.Top };
            var placeholder = new TextBlock
            {
                Text = mode == SeqMode.Boss ? "Describe the boss you want… (e.g. a fire golem that charges, spits fireballs and gets faster below half health)"
                                            : "Describe the scene you want… (e.g. the camera shows the castle, the king says hello, then back to the player)",
                Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(7, 5, 7, 0), IsHitTestVisible = false
            };
            ask.TextChanged += (s, e) => placeholder.Visibility = ask.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var askHost = new Grid();
            askHost.Children.Add(ask); askHost.Children.Add(placeholder);
            panel.Children.Add(askHost);

            var make = new Button { Content = "Make it", Style = (Style)FindResource("AccentButton"), Height = 32, Padding = new Thickness(18, 0, 18, 0), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Tag = "seq-make" };
            var status = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Foreground = (SolidColorBrush)FindResource("TextDim"), Text = _seqAiStatus.TryGetValue(bh, out var st) ? st : "" };
            // While the AI works on this behavior (the card may be rebuilt meanwhile) nothing else may edit its script.
            void SetBusy(bool busy) { make.IsEnabled = ask.IsEnabled = json.IsEnabled = scratch.IsEnabled = !busy; }
            if (_seqAiBusy.Contains(bh)) { SetBusy(true); status.Text = $"Asking {ActiveModelLabel}…"; }
            make.Click += async (s, e) =>
            {
                var request = ask.Text.Trim();
                if (request.Length == 0) { status.Text = "Write what you want first."; return; }
                if (AiNotReadyReason() is string notReady) { status.Text = notReady + "."; return; }
                if (!_seqAiBusy.Add(bh)) return;
                SetBusy(true);
                status.Text = $"Asking {ActiveModelLabel}…";
                string outcome;
                try
                {
                    var result = await SequenceAi.MakeAsync((user, system) => AiGenerateAsync(user, system, json: true, temperature: 0.2),
                                                            mode, request, Script(), _project, _scene, compact: !_useCloud);
                    outcome = ApplySeqAi(bh, mode, result);
                }
                catch (AiException ex) { outcome = "⚠ " + ex.Message; }
                finally { _seqAiBusy.Remove(bh); SetBusy(false); }
                _seqAiStatus.AddOrUpdate(bh, outcome);
                status.Text = outcome;
                RefreshAfterChoiceAlways(obj);
            };
            panel.Children.Add(make);
            panel.Children.Add(status);
        }

        /// <summary>Writes the AI's script into <paramref name="bh"/> — unless an undo/redo or a scene switch during the
        /// call left it outside the scene (writing then would lose the result and clear redo). Returns the status line.</summary>
        private string ApplySeqAi(BehaviorData bh, SeqMode mode, SeqAiResult result)
        {
            if (!result.Ok) return "⚠ The AI's answer couldn't be used: " + result.Error + ". Try describing it differently.";
            if (!SceneHasBehavior(bh))
            {
                const string stale = "⚠ The scene changed while the AI was working — not applied.";
                Status(stale);
                return stale;
            }
            PushUndo(); bh.Texts["script"] = result.Json; Dirty();
            return "✓ Done — " + SequenceJson.Summary(mode, result.Json, out _).TrimStart('✓', ' ')
                   + (result.Notes.Count > 0 ? "\nFixed up: " + string.Join("; ", result.Notes.Take(4)) : "");
        }

        internal string TestApplySeqAi(BehaviorData bh, SeqMode mode, SeqAiResult result) => ApplySeqAi(bh, mode, result);
        internal void TestUndo() => Undo();
        internal void TestRedo() => Redo();
        internal (int undo, int redo) TestHistory => (_undo.Count, _redo.Count);

        /// <summary>Test hook: builds the inspector for an object and reports what its Boss AI / Cutscene card shows.</summary>
        public (bool scratchButton, bool jsonBox, bool askBox, bool makeButton, string summary) TestSequenceCard(GameObjectData obj)
        {
            TestInspect(obj);
            var all = new List<FrameworkElement>();
            void Walk(object node)
            {
                if (node is FrameworkElement fe && fe.Tag is string tag && tag.StartsWith("seq-")) all.Add(fe);
                if (node is DependencyObject d) foreach (var c in LogicalTreeHelper.GetChildren(d)) Walk(c);
            }
            foreach (var child in InspectorRoot.Children) Walk(child);
            bool Has(string tag) => all.Any(f => (string)f.Tag == tag);
            return (Has("seq-scratch"), Has("seq-json"), Has("seq-ask"), Has("seq-make"), all.OfType<TextBlock>().FirstOrDefault(t => (string)t.Tag == "seq-summary")?.Text ?? "");
        }
    }
}
