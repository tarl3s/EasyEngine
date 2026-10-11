using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyEngine
{
    // On-screen test checklist. Each line is "Text => condition"; a line ticks itself (and stays ticked)
    // as soon as its condition is true. Reusable for every isolated test scenario.
    public partial class PlayWindow
    {
        private static List<(string text, string cond)> ParseChecklist(string spec, List<string> bad)
        {
            var list = new List<(string, string)>();
            foreach (var raw in (spec ?? "").Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                int k = line.LastIndexOf("=>", StringComparison.Ordinal);
                if (k <= 0 || k >= line.Length - 2) { bad.Add(line); continue; }
                list.Add((line[..k].Trim(), line[(k + 2)..].Trim()));
            }
            return list;
        }

        private Rect ChecklistRect(BehaviorData bh, int count, bool allDone)
        {
            double w = Math.Max(160, bh.Num("width", 300));
            double h = 16 + 24 + count * 22 + (allDone ? 28 : 0);
            double W = Root.ActualWidth > 10 ? Root.ActualWidth : 960;
            bool left = bh.Text("corner", "tr").Trim().Equals("tl", StringComparison.OrdinalIgnoreCase);
            return new Rect(left ? 12 : W - w - 12, 64, w, h); // y 64: below the top bar
        }

        private void UpdateChecklist(Entity en, BehaviorData bh, double dt)
        {
            if (en.CheckItems == null)
            {
                var bad = new List<string>();
                en.CheckItems = ParseChecklist(bh.Text("items", ""), bad);
                en.CheckDone = new bool[en.CheckItems.Count];
                foreach (var b in bad) Warn("Checklist", en.Data.Name, $"Ignored a line without '=>': \"{b}\"", "Write each line as:  Text => condition");
            }
            var items = en.CheckItems; var done = en.CheckDone!;
            bool changed = false;
            for (int i = 0; i < items.Count; i++)
            {
                if (done[i]) continue;
                bool ok;
                try { ok = EvalExpr(items[i].cond, en) != 0; }
                catch (Exception ex) { ok = false; LogThrottled("check-bad:" + i, 5, LogLevel.Warn, "Checklist", en.Data.Name, $"Can't evaluate \"{items[i].cond}\"", ex.Message); }
                if (!ok) continue;
                done[i] = true; changed = true;
                Info("Checklist", "", $"✓ {items[i].text}", $"condition: {items[i].cond} · at {_gameTime:0.0}s");
            }
            int n = done.Count(d => d);
            bool all = items.Count > 0 && n == items.Count;
            _vars["check:done"] = n; _vars["check:total"] = items.Count; _vars["check:all"] = all ? 1 : 0;
            if (changed && all) Info("Checklist", "", "ALL PASSED ✓", $"{items.Count} of {items.Count} checks at {_gameTime:0.0}s");

            var rect = ChecklistRect(bh, items.Count, all);
            _uiRects["the checklist"] = rect;
            var sig = new string(done.Select(d => d ? '1' : '0').ToArray()) + $"|{rect.X:0}";
            if (en.ChecklistPanel == null || sig != en.ChecklistSig) { RenderChecklist(en, bh, items, done, rect, all, n); en.ChecklistSig = sig; }
        }

        private void RenderChecklist(Entity en, BehaviorData bh, List<(string text, string cond)> items, bool[] done, Rect rect, bool all, int n)
        {
            if (en.ChecklistPanel == null)
            {
                en.ChecklistPanel = new Border
                {
                    CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8), IsHitTestVisible = false,
                    Background = new SolidColorBrush(Color.FromArgb(215, 16, 16, 24)), BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), BorderThickness = new Thickness(1)
                };
                Hud.Children.Add(en.ChecklistPanel);
            }
            var panel = en.ChecklistPanel;
            panel.Width = rect.Width;
            Canvas.SetLeft(panel, rect.X); Canvas.SetTop(panel, rect.Y);

            var green = new SolidColorBrush(Color.FromRgb(0x3B, 0xC9, 0x7A));
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = $"{bh.Text("title", "Test checklist")}   {n}/{items.Count}", Foreground = Brushes.White,
                FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 0, 0, 4)
            });
            for (int i = 0; i < items.Count; i++)
            {
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                var box = new TextBlock { Text = done[i] ? "✓" : "☐", Width = 20, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = done[i] ? green : new SolidColorBrush(Color.FromRgb(0x8E, 0x95, 0xA8)) };
                DockPanel.SetDock(box, Dock.Left); row.Children.Add(box);
                row.Children.Add(new TextBlock
                {
                    Text = items[i].text, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = done[i] ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xB8, 0xBE, 0xCC))
                });
                sp.Children.Add(row);
            }
            if (all) sp.Children.Add(new TextBlock { Text = "ALL PASSED ✓", Foreground = green, FontWeight = FontWeights.Bold, FontSize = 15, Margin = new Thickness(0, 6, 0, 0) });
            panel.Child = sp;
        }
    }
}
