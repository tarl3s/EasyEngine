using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyEngine
{
    /// <summary>What the step blocks offer in their dropdowns: the scene's objects, the prefabs, sounds, variables, scenes.</summary>
    public class StepContext
    {
        public Project Project = null!;
        public Scene Scene = null!;
        public Window? Owner;

        public List<string> Objects => ParamSources.Objects(Scene);
        public List<string> Prefabs => ParamSources.Prefabs(Project);
        public List<string> Variables => ParamSources.Variables(Project);
        public List<string> Sounds => ParamSources.Sounds(Project);

        internal static Brush Res(string key, string fallback)
            => Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
        internal static Style? St(string key) => Application.Current?.TryFindResource(key) as Style;
        internal static SolidColorBrush Hex(string hex, byte alpha = 255)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        }
    }

    /// <summary>An ordered list of steps as coloured blocks (colour = category): each block shows its fields inline
    /// (number boxes, dropdowns, colour buttons, checkboxes), with ↑ ↓ ✕, and "＋ Add step" opens the palette.</summary>
    public class StepListControl : StackPanel
    {
        private readonly List<SeqStep> _steps;
        private readonly SeqMode _mode;
        private readonly StepContext _ctx;
        private const string NewVar = "\u0001new";

        public StepListControl(List<SeqStep> steps, SeqMode mode, StepContext ctx)
        {
            _steps = steps; _mode = mode; _ctx = ctx;
            Render();
        }

        public int BlockCount => _steps.Count;

        private void Render()
        {
            Children.Clear();
            if (_steps.Count == 0)
                Children.Add(new TextBlock { Text = "No steps yet — add one below.", Foreground = StepContext.Res("TextDim", "#8C8C97"), FontSize = 12, Margin = new Thickness(2, 2, 0, 6) });
            for (int i = 0; i < _steps.Count; i++) Children.Add(Block(i));
            var add = new Button { Content = "＋ Add step", Height = 30, Padding = new Thickness(12, 0, 12, 0), HorizontalAlignment = HorizontalAlignment.Left, Style = StepContext.St("CardButton"), Margin = new Thickness(0, 2, 0, 4) };
            add.Click += (s, e) => { var m = AddMenu(); m.PlacementTarget = add; m.IsOpen = true; };
            Children.Add(add);
        }

        /// <summary>The palette: every step of this mode, grouped under its category title.</summary>
        public ContextMenu AddMenu()
        {
            var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MaxHeight = 560 };
            foreach (var (cat, color) in StepCatalog.Categories)
            {
                var defs = StepCatalog.For(_mode).Where(d => d.Category == cat).ToList();
                if (defs.Count == 0) continue;
                if (menu.Items.Count > 0) menu.Items.Add(new Separator());
                menu.Items.Add(new MenuItem { Header = cat.ToUpperInvariant(), IsEnabled = false, FontWeight = FontWeights.Bold, Tag = "category" });
                foreach (var d in defs)
                {
                    var mi = new MenuItem { Header = d.Label, ToolTip = d.Hint.Length > 0 ? d.Hint : null, Icon = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Background = StepContext.Hex(color) } };
                    mi.Click += (s, e) => { _steps.Add(NewStep(d)); Render(); };
                    menu.Items.Add(mi);
                }
            }
            return menu;
        }

        public static SeqStep NewStep(StepDef d)
        {
            var s = new SeqStep { Do = d.Id };
            foreach (var f in d.Fields) s.P[f.Key] = f.Default;
            return s;
        }

        private Border Block(int index)
        {
            var step = _steps[index];
            var def = StepCatalog.Find(_mode, step.Do);
            string color = StepCatalog.ColorOf(def?.Category ?? "");

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock { Text = $"{index + 1}.  {def?.Label ?? step.Do}", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, ToolTip = def?.Hint.Length > 0 ? def.Hint : null });
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            var up = Small("↑", "Move up"); up.Click += (s, e) => { if (index > 0) { (_steps[index], _steps[index - 1]) = (_steps[index - 1], _steps[index]); Render(); } };
            var dn = Small("↓", "Move down"); dn.Click += (s, e) => { if (index < _steps.Count - 1) { (_steps[index], _steps[index + 1]) = (_steps[index + 1], _steps[index]); Render(); } };
            var del = Small("✕", "Remove this step"); del.Foreground = StepContext.Res("Danger", "#E5484D"); del.Click += (s, e) => { _steps.RemoveAt(index); Render(); };
            btns.Children.Add(up); btns.Children.Add(dn); btns.Children.Add(del);
            Grid.SetColumn(btns, 1); header.Children.Add(btns);

            var body = new StackPanel();
            body.Children.Add(header);
            if (def != null && def.Fields.Length > 0)
            {
                var wrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                foreach (var f in def.Fields)
                    if (f.OnlyIfEmpty == null || step.Str(f.OnlyIfEmpty).Length == 0) wrap.Children.Add(FieldControl(step, f));
                body.Children.Add(wrap);
            }
            return new Border
            {
                Background = StepContext.Hex(color, 0x30), BorderBrush = StepContext.Hex(color), BorderThickness = new Thickness(5, 1, 1, 1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 8, 6), Margin = new Thickness(0, 0, 0, 6), Child = body, Tag = "step"
            };
        }

        private static Button Small(string t, string tip) => new()
        {
            Content = t, Width = 26, Height = 24, FontSize = 12, Margin = new Thickness(3, 0, 0, 0), Padding = new Thickness(0), ToolTip = tip,
            Style = StepContext.St("GhostButton"), Foreground = StepContext.Res("Text", "#EEEEEE")
        };

        private FrameworkElement FieldControl(SeqStep step, StepField f)
        {
            string cur = step.Str(f.Key, f.Default);
            void Set(string v) => step.P[f.Key] = v;
            if (f.Type == "bool")
            {
                var chk = new CheckBox { Content = f.Label, IsChecked = step.On(f.Key, f.Default == "1"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 14, 12, 4) };
                chk.Checked += (s, e) => Set("1"); chk.Unchecked += (s, e) => Set("0");
                return chk;
            }

            FrameworkElement ctrl;
            switch (f.Type)
            {
                case "choice":
                    ctrl = Combo((f.Options ?? Array.Empty<string>()).Select(ParamSources.Option).ToList(), cur, Set);
                    break;
                case "object":
                {
                    var opts = (f.Options ?? Array.Empty<string>()).Select(ParamSources.Option).ToList();
                    opts.Add(("self", _mode == SeqMode.Boss ? "The boss (self)" : "This object (self)"));
                    opts.Add(("player", _mode == SeqMode.Boss ? "Its target (player)" : "The player"));
                    opts.AddRange(_ctx.Objects.Select(o => (o, o)));
                    bool pointField = f.Options?.Any(o => o.StartsWith("|")) == true;
                    ctrl = Combo(opts, cur, v => { Set(v); if (pointField) Render(); }); // picking "a point" shows the x / y boxes
                    break;
                }
                case "spawn":
                {
                    var opts = new List<(string, string)> { ("", f.Key == "bullet" ? "(a round bullet)" : f.Key == "wave" ? "(a ground wave)" : "(none)") };
                    opts.AddRange(_ctx.Objects.Select(o => (o, o)));
                    opts.AddRange(_ctx.Prefabs.Where(p => !_ctx.Objects.Contains(p)).Select(p => (p, p + "  (prefab)")));
                    ctrl = Combo(opts, cur, Set);
                    break;
                }
                case "sound":
                    ctrl = Combo(new[] { ("", "(none)") }.Concat(_ctx.Sounds.Select(sn => (sn, System.IO.Path.GetFileName(sn)))).ToList(), cur, Set);
                    break;
                case "scene":
                    ctrl = Combo(_ctx.Project.Scenes.Select(sc => (sc.Name, sc.Name)).ToList(), cur, Set);
                    break;
                case "key":
                    ctrl = Combo(ParamSources.Keys.ToList(), cur, Set);
                    break;
                case "var":
                {
                    var opts = _ctx.Variables.Select(v => (v, v)).ToList();
                    opts.Add((NewVar, "＋ New…"));
                    ComboBox? cb = null;
                    cb = Combo(opts, cur, v =>
                    {
                        if (v != NewVar) { Set(v); return; }
                        var name = _ctx.Owner != null ? InputDialog.Prompt(_ctx.Owner, "New", "New variable name:", "") : null;
                        if (name == null) { cb!.SelectedItem = cb.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == step.Str(f.Key)); return; }
                        Set(name.Trim()); Render();
                    });
                    ctrl = cb;
                    break;
                }
                case "color":
                {
                    var swatch = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(3), Background = SafeHex(cur), BorderBrush = StepContext.Res("Border", "#35353F"), BorderThickness = new Thickness(1) };
                    var hex = new TextBlock { Text = cur, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    var sp = new StackPanel { Orientation = Orientation.Horizontal }; sp.Children.Add(swatch); sp.Children.Add(hex);
                    var btn = new Button { Content = sp, Height = 28, Padding = new Thickness(6, 0, 8, 0), Style = StepContext.St("CardButton"), ToolTip = "Click to pick a colour" };
                    btn.Click += (s, e) => ColorPickerPopup.Open(btn, step.Str(f.Key, f.Default), v => { Set(v); hex.Text = v; swatch.Background = SafeHex(v); });
                    ctrl = btn;
                    break;
                }
                default: // num, val, text, longtext
                {
                    double width = f.Type switch { "num" => 64, "val" => 96, "longtext" => 320, _ => 150 };
                    var tb = new TextBox { Text = cur, Width = width, Height = f.Type == "longtext" ? double.NaN : 28, MinHeight = 28, VerticalContentAlignment = VerticalAlignment.Center,
                                           TextWrapping = f.Type == "longtext" ? TextWrapping.Wrap : TextWrapping.NoWrap };
                    tb.TextChanged += (s, e) => Set(f.Type is "num" ? tb.Text.Replace(',', '.').Trim() : tb.Text);
                    ctrl = tb;
                    break;
                }
            }
            var stack = new StackPanel { Margin = new Thickness(0, 0, 10, 4) };
            stack.Children.Add(new TextBlock { Text = f.Label, Foreground = StepContext.Res("TextDim", "#8C8C97"), FontSize = 10.5, Margin = new Thickness(2, 0, 0, 1) });
            stack.Children.Add(ctrl);
            return stack;
        }

        private static Brush SafeHex(string hex) { try { return StepContext.Hex(hex); } catch { return Brushes.Gray; } }

        /// <summary>A dropdown of (value, label); a current value that isn't in the list is kept and shown.</summary>
        internal static ComboBox Combo(List<(string value, string label)> options, string current, Action<string> set)
        {
            var cb = new ComboBox { Height = 28, MinWidth = 110, MaxDropDownHeight = 320 };
            foreach (var (v, l) in options)
                if (!cb.Items.Cast<ComboBoxItem>().Any(i => ((string)i.Tag).Equals(v, StringComparison.OrdinalIgnoreCase)))
                    cb.Items.Add(new ComboBoxItem { Content = l, Tag = v });
            if (!cb.Items.Cast<ComboBoxItem>().Any(i => ((string)i.Tag).Equals(current, StringComparison.OrdinalIgnoreCase)))
                cb.Items.Insert(0, new ComboBoxItem { Content = current.Length > 0 ? current : "(none)", Tag = current });
            cb.SelectedItem = cb.Items.Cast<ComboBoxItem>().First(i => ((string)i.Tag).Equals(current, StringComparison.OrdinalIgnoreCase));
            cb.SelectionChanged += (s, e) => { if (cb.SelectedItem is ComboBoxItem it) set((string)it.Tag); };
            return cb;
        }
    }

    /// <summary>"Advanced Scratch": edits a Cutscene's steps, or a Boss AI's attacks, phases and death sequence, as
    /// blocks. Reads and writes the same JSON the game runs (SequenceJson), so JSON → editor → JSON is stable.</summary>
    public class SequenceEditorWindow : Window
    {
        private readonly SeqMode _mode;
        private readonly StepContext _ctx;
        private readonly CutsceneScript _cut = new();
        private readonly BossScript _boss = new();
        private readonly ContentControl _page = new();
        private readonly List<Button> _tabs = new();
        public string ResultJson { get; private set; } = "";

        public SequenceEditorWindow(Project project, Scene scene, SeqMode mode, string json, string objectName = "")
        {
            _mode = mode;
            _ctx = new StepContext { Project = project, Scene = scene, Owner = this };
            string? err;
            if (mode == SeqMode.Cutscene) _cut = SequenceJson.ParseCutscene(json, out err);
            else _boss = SequenceJson.ParseBoss(json, out err);
            Title = (mode == SeqMode.Boss ? "👑 Boss — Advanced Scratch" : "🎬 Cutscene — Advanced Scratch") + (objectName.Length > 0 ? $" · {objectName}" : "");
            Width = 900; Height = 700;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = StepContext.Res("Bg0", "#141418");
            BuildUi(err);
        }

        public string SerializeTest() => Serialize();
        private string Serialize() => _mode == SeqMode.Boss ? SequenceJson.Serialize(_boss) : SequenceJson.Serialize(_cut);

        /// <summary>How many step blocks the visible page shows (selftest).</summary>
        public int TestBlocks => Descendants(_page).OfType<StepListControl>().Sum(l => l.BlockCount);
        public int TestTabs => _tabs.Count;
        public void TestShowTab(int i) => ShowTab(i);

        private void BuildUi(string? error)
        {
            var root = new DockPanel { Margin = new Thickness(14) };
            var intro = _mode == SeqMode.Boss
                ? "ATTACKS are lists of steps. PHASES choose which attacks the boss uses — the next phase starts when its health drops to a percentage. WHEN IT DIES plays at 0 health."
                : "The steps run one after another, top to bottom. Steps like Say, Wait, Move or Camera wait until they're done.";
            root.Children.Add(Docked(new TextBlock { Text = intro, Foreground = StepContext.Res("TextDim", "#8C8C97"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }, Dock.Top));
            if (error != null)
                root.Children.Add(Docked(new TextBlock { Text = "⚠ The JSON couldn't be read (" + error + "). Saving here replaces it.", Foreground = StepContext.Res("Danger", "#E5484D"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }, Dock.Top));

            var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var cancel = new Button { Content = "Cancel", Height = 32, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 0, 8, 0), Style = StepContext.St("GhostButton") };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            var ok = new Button { Content = "OK", Height = 32, Padding = new Thickness(24, 0, 24, 0), Style = StepContext.St("AccentButton") };
            ok.Click += (s, e) => { ResultJson = Serialize(); DialogResult = true; Close(); };
            bottom.Children.Add(cancel); bottom.Children.Add(ok);
            root.Children.Add(Docked(bottom, Dock.Bottom));

            if (_mode == SeqMode.Boss)
            {
                var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                foreach (var (title, i) in new[] { ("⚔ Attacks", 0), ("📈 Phases", 1), ("💀 When it dies", 2) })
                {
                    var b = new Button { Content = title, Height = 32, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 0, 6, 0), Style = StepContext.St("CardButton") };
                    b.Click += (s, e) => ShowTab(i);
                    _tabs.Add(b); strip.Children.Add(b);
                }
                root.Children.Add(Docked(strip, Dock.Top));
            }
            root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _page });
            Content = root;
            ShowTab(0);
        }

        private static FrameworkElement Docked(FrameworkElement el, Dock d) { DockPanel.SetDock(el, d); return el; }

        // Each page is rebuilt when shown, so the phases always list the attacks as they are now named.
        private void ShowTab(int i)
        {
            for (int k = 0; k < _tabs.Count; k++)
            {
                _tabs[k].Background = k == i ? StepContext.Res("Accent", "#18B98A") : StepContext.Res("Bg2", "#24242B");
                _tabs[k].Foreground = k == i ? StepContext.Res("AccentText", "#08130F") : StepContext.Res("Text", "#EEEEEE");
            }
            _page.Content = _mode == SeqMode.Cutscene ? new StepListControl(_cut.Steps, SeqMode.Cutscene, _ctx)
                          : i == 0 ? AttacksPage() : i == 1 ? PhasesPage() : DeathPage();
        }

        private FrameworkElement AttacksPage()
        {
            var panel = new StackPanel();
            foreach (var atk in _boss.Attacks.ToList())
            {
                var card = new StackPanel();
                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var del = new Button { Content = "✕ Delete attack", Height = 28, Padding = new Thickness(10, 0, 10, 0), Style = StepContext.St("DangerButton") };
                del.Click += (s, e) => { _boss.Attacks.Remove(atk); foreach (var p in _boss.Phases) p.Attacks.RemoveAll(n => n == atk.Name); ShowTab(0); };
                DockPanel.SetDock(del, Dock.Right); head.Children.Add(del);
                head.Children.Add(new TextBlock { Text = "Attack", Foreground = StepContext.Res("TextDim", "#8C8C97"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                var name = new TextBox { Text = atk.Name, Width = 220, Height = 28, HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
                name.TextChanged += (s, e) => Rename(atk, name.Text.Trim());
                head.Children.Add(name);
                card.Children.Add(head);
                card.Children.Add(new StepListControl(atk.Steps, SeqMode.Boss, _ctx));
                panel.Children.Add(Card(card));
            }
            if (_boss.Attacks.Count == 0) panel.Children.Add(Hint("No attacks yet."));
            var add = new Button { Content = "＋ New attack", Height = 32, Padding = new Thickness(14, 0, 14, 0), HorizontalAlignment = HorizontalAlignment.Left, Style = StepContext.St("AccentButton") };
            add.Click += (s, e) =>
            {
                int n = _boss.Attacks.Count + 1;
                while (_boss.Attack($"Attack {n}") != null) n++;
                _boss.Attacks.Add(new BossAttack { Name = $"Attack {n}", Steps = { StepListControl.NewStep(StepCatalog.Find(SeqMode.Boss, "telegraph")!) } });
                ShowTab(0);
            };
            panel.Children.Add(add);
            return panel;
        }

        // Renaming an attack also renames it in every phase that uses it (names must stay unique and non-empty).
        private void Rename(BossAttack atk, string name)
        {
            if (name.Length == 0 || name == atk.Name || _boss.Attacks.Any(a => a != atk && a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
            foreach (var p in _boss.Phases) for (int i = 0; i < p.Attacks.Count; i++) if (p.Attacks[i] == atk.Name) p.Attacks[i] = name;
            atk.Name = name;
        }

        private FrameworkElement PhasesPage()
        {
            var panel = new StackPanel();
            for (int idx = 0; idx < _boss.Phases.Count; idx++)
            {
                int i = idx;
                var ph = _boss.Phases[i];
                var card = new StackPanel();

                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var btns = new StackPanel { Orientation = Orientation.Horizontal };
                var up = new Button { Content = "↑", Width = 28, Height = 28, Style = StepContext.St("GhostButton"), ToolTip = "Earlier" };
                up.Click += (s, e) => { if (i > 0) { (_boss.Phases[i], _boss.Phases[i - 1]) = (_boss.Phases[i - 1], _boss.Phases[i]); ShowTab(1); } };
                var dn = new Button { Content = "↓", Width = 28, Height = 28, Style = StepContext.St("GhostButton"), ToolTip = "Later" };
                dn.Click += (s, e) => { if (i < _boss.Phases.Count - 1) { (_boss.Phases[i], _boss.Phases[i + 1]) = (_boss.Phases[i + 1], _boss.Phases[i]); ShowTab(1); } };
                var del = new Button { Content = "✕ Delete phase", Height = 28, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(6, 0, 0, 0), Style = StepContext.St("DangerButton") };
                del.Click += (s, e) => { _boss.Phases.RemoveAt(i); ShowTab(1); };
                btns.Children.Add(up); btns.Children.Add(dn); btns.Children.Add(del);
                DockPanel.SetDock(btns, Dock.Right); head.Children.Add(btns);
                var name = new TextBox { Text = ph.Name, Width = 220, Height = 28, HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
                name.TextChanged += (s, e) => { if (name.Text.Trim().Length > 0) ph.Name = name.Text.Trim(); };
                head.Children.Add(name);
                card.Children.Add(head);

                // starts when: at the start / health at or below N %
                var when = new WrapPanel();
                var pct = NumBox(ph.Value, v => ph.Value = Math.Clamp(v, 0, 100));
                pct.Visibility = ph.When == "health" ? Visibility.Visible : Visibility.Collapsed;
                var pctLabel = new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0), Visibility = pct.Visibility };
                var whenCb = StepListControl.Combo(new List<(string, string)> { ("start", "At the start"), ("health", "When its health is at or below") }, ph.When, v =>
                {
                    ph.When = v; pct.Visibility = pctLabel.Visibility = v == "health" ? Visibility.Visible : Visibility.Collapsed;
                });
                whenCb.MinWidth = 230;
                when.Children.Add(whenCb); when.Children.Add(pct); when.Children.Add(pctLabel);
                card.Children.Add(Row("Starts", when));

                var uses = new WrapPanel();
                foreach (var atk in _boss.Attacks)
                {
                    var chk = new CheckBox { Content = atk.Name, IsChecked = ph.Attacks.Contains(atk.Name), Margin = new Thickness(0, 4, 14, 4) };
                    string n = atk.Name;
                    chk.Checked += (s, e) => { if (!ph.Attacks.Contains(n)) ph.Attacks = _boss.Attacks.Select(a => a.Name).Where(a => a == n || ph.Attacks.Contains(a)).ToList(); };
                    chk.Unchecked += (s, e) => ph.Attacks.Remove(n);
                    uses.Children.Add(chk);
                }
                if (_boss.Attacks.Count == 0) uses.Children.Add(Hint("Make an attack first (Attacks tab)."));
                card.Children.Add(Row("Uses attacks", uses));

                var order = StepListControl.Combo(new List<(string, string)> { ("random", "At random (never twice in a row)"), ("inorder", "In order") }, ph.Order, v => ph.Order = v);
                order.MinWidth = 260; order.HorizontalAlignment = HorizontalAlignment.Left;
                card.Children.Add(Row("Order", order));
                var pause = NumBox(ph.Pause, v => ph.Pause = Math.Max(0, v));
                pause.HorizontalAlignment = HorizontalAlignment.Left;
                card.Children.Add(Row("Pause between attacks (s)", pause));

                card.Children.Add(new TextBlock { Text = "When this phase starts", Foreground = StepContext.Res("TextDim", "#8C8C97"), Margin = new Thickness(0, 8, 0, 4) });
                card.Children.Add(new StepListControl(ph.Enter, SeqMode.Boss, _ctx));
                panel.Children.Add(Card(card));
            }
            if (_boss.Phases.Count == 0) panel.Children.Add(Hint("No phases yet — without one, the boss uses every attack at random."));
            var add = new Button { Content = "＋ New phase", Height = 32, Padding = new Thickness(14, 0, 14, 0), HorizontalAlignment = HorizontalAlignment.Left, Style = StepContext.St("AccentButton") };
            add.Click += (s, e) =>
            {
                var last = _boss.Phases.LastOrDefault();
                double next = last == null ? 100 : Math.Max(5, (last.When == "health" ? last.Value : 100) - 35);
                _boss.Phases.Add(new BossPhase { Name = $"Phase {_boss.Phases.Count + 1}", When = last == null ? "start" : "health", Value = next, Attacks = _boss.Attacks.Select(a => a.Name).ToList() });
                ShowTab(1);
            };
            panel.Children.Add(add);
            return panel;
        }

        private FrameworkElement DeathPage()
        {
            var panel = new StackPanel();
            panel.Children.Add(Hint("These steps play when its health reaches 0 (it can't be hurt and doesn't hurt anymore). Then its Custom 'death' rules run and it disappears. Leave it empty to make it disappear at once."));
            panel.Children.Add(new StepListControl(_boss.Death, SeqMode.Boss, _ctx));
            return panel;
        }

        private static Border Card(UIElement child) => new()
        {
            Background = StepContext.Res("Bg1", "#1C1C22"), BorderBrush = StepContext.Res("Border", "#35353F"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10), Child = child
        };

        private static TextBlock Hint(string text) => new() { Text = text, Foreground = StepContext.Res("TextDim", "#8C8C97"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 8) };

        private static FrameworkElement Row(string label, UIElement content)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var l = new TextBlock { Text = label, Foreground = StepContext.Res("TextDim", "#8C8C97"), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(l);
            Grid.SetColumn(content, 1); g.Children.Add(content);
            return g;
        }

        private static TextBox NumBox(double value, Action<double> set)
        {
            var tb = new TextBox { Text = value.ToString("0.##", CultureInfo.InvariantCulture), Width = 64, Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
            tb.TextChanged += (s, e) => { if (double.TryParse(tb.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) set(v); };
            return tb;
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            foreach (var c in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                yield return c;
                foreach (var d in Descendants(c)) yield return d;
            }
        }
    }
}
