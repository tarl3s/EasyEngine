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

        // Scanned once per editor window (whole-project scans, a folder listing): the blocks are rebuilt on every change.
        private List<string>? _objects, _variables, _textVariables, _colorVariables, _listVariables, _sounds;
        private List<(string value, string label)>? _objectsAndPrefabs;
        public List<string> Objects => _objects ??= ParamSources.Objects(Scene);
        public List<(string value, string label)> ObjectsAndPrefabs => _objectsAndPrefabs ??= ParamSources.ObjectOrPrefabOptions(Scene, Project);
        public List<string> Variables => _variables ??= ParamSources.Variables(Project);
        public List<string> TextVariables => _textVariables ??= ParamSources.TextVariables(Project);
        public List<string> ColorVariables => _colorVariables ??= ParamSources.ColorVariables(Project);
        public List<string> ListVariables => _listVariables ??= ParamSources.ListVariables(Project);
        public List<string> Sounds => _sounds ??= ParamSources.Sounds(Project);
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
                Children.Add(new TextBlock { Text = L.T("No steps yet — add one below."), Foreground = Theme.Brush("TextDim", "#8C8C97"), FontSize = 12, Margin = new Thickness(2, 2, 0, 6) });
            for (int i = 0; i < _steps.Count; i++) Children.Add(Block(i));
            var add = new Button { Content = L.T("＋ Add step"), Height = 30, Padding = new Thickness(12, 0, 12, 0), HorizontalAlignment = HorizontalAlignment.Left, Style = Theme.Style("CardButton"), Margin = new Thickness(0, 2, 0, 4) };
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
                menu.Items.Add(new MenuItem { Header = L.T(cat).ToUpperInvariant(), IsEnabled = false, FontWeight = FontWeights.Bold, Tag = "category" });
                foreach (var d in defs)
                {
                    var mi = new MenuItem { Header = L.T(d.Label), ToolTip = d.Hint.Length > 0 ? L.T(d.Hint) : null, Icon = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Background = Theme.Hex(color) } };
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
            header.Children.Add(new TextBlock { Text = $"{index + 1}.  {(def != null ? L.T(def.Label) : step.Do)}", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, ToolTip = def?.Hint.Length > 0 ? L.T(def.Hint) : null });
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            var up = Small("↑", L.T("Move up"), () => { if (Swap(_steps, index, index - 1)) Render(); });
            var dn = Small("↓", L.T("Move down"), () => { if (Swap(_steps, index, index + 1)) Render(); });
            var del = Small("✕", L.T("Remove this step"), () => { _steps.RemoveAt(index); Render(); }); del.Foreground = Theme.Brush("Danger", "#E5484D");
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
                Background = Theme.Hex(color, alpha: Theme.IsLight ? (byte)0x24 : (byte)0x30), BorderBrush = Theme.Hex(color), BorderThickness = new Thickness(5, 1, 1, 1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 8, 6), Margin = new Thickness(0, 0, 0, 6), Child = body, Tag = "step"
            };
        }

        /// <summary>A small ghost button (↑ ↓ ✕ on a block or a phase).</summary>
        internal static Button Small(string t, string tip, Action onClick)
        {
            var b = new Button
            {
                Content = t, Width = 26, Height = 24, FontSize = 12, Margin = new Thickness(3, 0, 0, 0), Padding = new Thickness(0), ToolTip = tip,
                Style = Theme.Style("GhostButton"), Foreground = Theme.Brush("Text", "#EEEEEE")
            };
            b.Click += (s, e) => onClick();
            return b;
        }

        /// <summary>Swaps items i and j; false (nothing done) when j is outside the list.</summary>
        internal static bool Swap<T>(List<T> list, int i, int j)
        {
            if (j < 0 || j >= list.Count) return false;
            (list[i], list[j]) = (list[j], list[i]);
            return true;
        }

        private FrameworkElement FieldControl(SeqStep step, StepField f)
        {
            string cur = step.Str(f.Key, f.Default);
            void Set(string v) => step.P[f.Key] = v;
            if (f.Type == "bool")
            {
                var chk = new CheckBox { Content = L.T(f.Label), IsChecked = step.On(f.Key, f.Default == "1"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 14, 12, 4) };
                chk.Checked += (s, e) => Set("1"); chk.Unchecked += (s, e) => Set("0");
                return chk;
            }

            FrameworkElement ctrl;
            switch (f.Type)
            {
                case "choice":
                    ctrl = Combo(Options(f), cur, Set);
                    break;
                case "object":
                {
                    var opts = Options(f);
                    opts.Add(("self", _mode == SeqMode.Boss ? L.T("The boss (self)") : L.T("This object (self)")));
                    opts.Add(("player", _mode == SeqMode.Boss ? L.T("Its target (player)") : L.T("The player")));
                    opts.AddRange(_ctx.Objects.Select(o => (o, o)));
                    bool pointField = f.Options?.Any(o => o.StartsWith("|")) == true;
                    ctrl = Combo(opts, cur, v => { Set(v); if (pointField) Render(); }); // picking "a point" shows the x / y boxes
                    break;
                }
                case "spawn":
                {
                    var opts = new List<(string, string)> { ("", f.Key == "bullet" ? L.T("(a round bullet)") : f.Key == "wave" ? L.T("(a ground wave)") : L.T("(none)")) };
                    opts.AddRange(_ctx.ObjectsAndPrefabs);
                    ctrl = Combo(opts, cur, Set);
                    break;
                }
                case "sound":
                    ctrl = Combo(new[] { ("", L.T("(none)")) }.Concat(_ctx.Sounds.Select(sn => (sn, System.IO.Path.GetFileName(sn)))).ToList(), cur, Set);
                    break;
                case "scene":
                    ctrl = Combo(_ctx.Project.Scenes.Select(sc => (sc.Name, sc.Name)).ToList(), cur, Set);
                    break;
                case "key":
                    ctrl = Combo(ParamSources.KeyOptions(), cur, Set);
                    break;
                case "var":
                    ctrl = Combo(_ctx.Variables.Select(v => (v, v)).ToList(), cur, Set, L.T("New variable name:"), _ctx.Owner);
                    break;
                case "textvar":
                    ctrl = Combo(_ctx.TextVariables.Select(v => (v, v)).ToList(), cur, Set, L.T("New text variable name:"), _ctx.Owner);
                    break;
                case "colorvar":
                    ctrl = Combo(_ctx.ColorVariables.Select(v => (v, v)).ToList(), cur, Set, L.T("New colour variable name:"), _ctx.Owner);
                    break;
                case "listvar":
                    ctrl = Combo(_ctx.ListVariables.Select(v => (v, v)).ToList(), cur, Set, L.T("New list name:"), _ctx.Owner);
                    break;
                case "color":
                {
                    var swatch = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(3), Background = Theme.Hex(cur), BorderBrush = Theme.Brush("Border", "#35353F"), BorderThickness = new Thickness(1) };
                    var hex = L.Keep(new TextBlock { Text = cur, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                    var sp = new StackPanel { Orientation = Orientation.Horizontal }; sp.Children.Add(swatch); sp.Children.Add(hex);
                    var btn = new Button { Content = sp, Height = 28, Padding = new Thickness(6, 0, 8, 0), Style = Theme.Style("CardButton"), ToolTip = L.T("Click to pick a colour") };
                    btn.Click += (s, e) => ColorPickerPopup.Open(btn, step.Str(f.Key, f.Default), v => { Set(v); hex.Text = v; swatch.Background = Theme.Hex(v); });
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
            stack.Children.Add(new TextBlock { Text = L.T(f.Label), Foreground = Theme.Brush("TextDim", "#8C8C97"), FontSize = 10.5, Margin = new Thickness(2, 0, 0, 1) });
            stack.Children.Add(ctrl);
            return stack;
        }

        /// <summary>A <see cref="Dropdown"/> sized for the blocks.</summary>
        internal static ComboBox Combo(List<(string value, string label)> options, string current, Action<string> set, string? newPrompt = null, Window? owner = null)
        {
            var cb = Dropdown.Make(options, current, set, newPrompt, owner);
            cb.Height = 28; cb.MinWidth = 110;
            return cb;
        }

        /// <summary>A field's "value|Label" options, labels in the editor's language (the values are what's saved).</summary>
        private static List<(string value, string label)> Options(StepField f) =>
            (f.Options ?? Array.Empty<string>()).Select(ParamSources.Option).Select(o => (o.value, L.T(o.label))).ToList();
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
            Title = (mode == SeqMode.Boss ? L.T("👑 Boss — Advanced Scratch") : L.T("🎬 Cutscene — Advanced Scratch")) + (objectName.Length > 0 ? $" · {objectName}" : "");
            Width = 900; Height = 700;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Theme.Brush("Bg0", "#141418");
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
                ? L.T("ATTACKS are lists of steps. PHASES choose which attacks the boss uses — the next phase starts when its health drops to a percentage. WHEN IT DIES plays at 0 health.")
                : L.T("The steps run one after another, top to bottom. Steps like Say, Wait, Move or Camera wait until they're done.");
            root.Children.Add(Docked(new TextBlock { Text = intro, Foreground = Theme.Brush("TextDim", "#8C8C97"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }, Dock.Top));
            if (error != null)
                root.Children.Add(Docked(new TextBlock { Text = L.F("⚠ The JSON couldn't be read ({0}). Saving here replaces it.", error), Foreground = Theme.Brush("Danger", "#E5484D"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }, Dock.Top));

            var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var cancel = new Button { Content = L.T("Cancel"), Height = 32, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 0, 8, 0), Style = Theme.Style("GhostButton") };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            var ok = new Button { Content = L.T("OK"), Height = 32, Padding = new Thickness(24, 0, 24, 0), Style = Theme.Style("AccentButton") };
            ok.Click += (s, e) => { ResultJson = Serialize(); DialogResult = true; Close(); };
            bottom.Children.Add(cancel); bottom.Children.Add(ok);
            root.Children.Add(Docked(bottom, Dock.Bottom));

            if (_mode == SeqMode.Boss)
            {
                var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                foreach (var (title, i) in new[] { (L.T("⚔ Attacks"), 0), (L.T("📈 Phases"), 1), (L.T("💀 When it dies"), 2) })
                {
                    var b = new Button { Content = title, Height = 32, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 0, 6, 0), Style = Theme.Style("CardButton") };
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
                _tabs[k].Background = k == i ? Theme.Brush("Accent", "#18B98A") : Theme.Brush("Bg2", "#24242B");
                _tabs[k].Foreground = k == i ? Theme.Brush("AccentText", "#08130F") : Theme.Brush("Text", "#EEEEEE");
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
                var del = new Button { Content = L.T("✕ Delete attack"), Height = 28, Padding = new Thickness(10, 0, 10, 0), Style = Theme.Style("DangerButton") };
                del.Click += (s, e) => { _boss.Attacks.Remove(atk); foreach (var p in _boss.Phases) p.Attacks.RemoveAll(n => n == atk.Name); ShowTab(0); };
                DockPanel.SetDock(del, Dock.Right); head.Children.Add(del);
                head.Children.Add(new TextBlock { Text = L.T("Attack"), Foreground = Theme.Brush("TextDim", "#8C8C97"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                var name = new TextBox { Text = atk.Name, Width = 220, Height = 28, HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
                name.TextChanged += (s, e) => Rename(atk, name.Text.Trim());
                head.Children.Add(name);
                card.Children.Add(head);
                card.Children.Add(new StepListControl(atk.Steps, SeqMode.Boss, _ctx));
                panel.Children.Add(Card(card));
            }
            if (_boss.Attacks.Count == 0) panel.Children.Add(Hint(L.T("No attacks yet.")));
            var add = new Button { Content = L.T("＋ New attack"), Height = 32, Padding = new Thickness(14, 0, 14, 0), HorizontalAlignment = HorizontalAlignment.Left, Style = Theme.Style("AccentButton") };
            add.Click += (s, e) =>
            {
                int n = _boss.Attacks.Count + 1;
                while (_boss.Attack(L.F("Attack {0}", n)) != null) n++;
                _boss.Attacks.Add(new BossAttack { Name = L.F("Attack {0}", n), Steps = { StepListControl.NewStep(StepCatalog.Find(SeqMode.Boss, "telegraph")!) } });
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
                var up = StepListControl.Small("↑", L.T("Earlier"), () => { if (StepListControl.Swap(_boss.Phases, i, i - 1)) ShowTab(1); });
                var dn = StepListControl.Small("↓", L.T("Later"), () => { if (StepListControl.Swap(_boss.Phases, i, i + 1)) ShowTab(1); });
                var del = new Button { Content = L.T("✕ Delete phase"), Height = 28, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(6, 0, 0, 0), Style = Theme.Style("DangerButton") };
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
                var whenCb = StepListControl.Combo(new List<(string, string)> { ("start", L.T("At the start")), ("health", L.T("When its health is at or below")) }, ph.When, v =>
                {
                    ph.When = v; pct.Visibility = pctLabel.Visibility = v == "health" ? Visibility.Visible : Visibility.Collapsed;
                });
                whenCb.MinWidth = 230;
                when.Children.Add(whenCb); when.Children.Add(pct); when.Children.Add(pctLabel);
                card.Children.Add(Row(L.T("Starts"), when));

                var uses = new WrapPanel();
                foreach (var atk in _boss.Attacks)
                {
                    var chk = L.Keep(new CheckBox { Content = atk.Name, IsChecked = ph.Attacks.Contains(atk.Name), Margin = new Thickness(0, 4, 14, 4) }); // the attack's name
                    string n = atk.Name;
                    chk.Checked += (s, e) => { if (!ph.Attacks.Contains(n)) ph.Attacks = _boss.Attacks.Select(a => a.Name).Where(a => a == n || ph.Attacks.Contains(a)).ToList(); };
                    chk.Unchecked += (s, e) => ph.Attacks.Remove(n);
                    uses.Children.Add(chk);
                }
                if (_boss.Attacks.Count == 0) uses.Children.Add(Hint(L.T("Make an attack first (Attacks tab).")));
                card.Children.Add(Row(L.T("Uses attacks"), uses));

                var order = StepListControl.Combo(new List<(string, string)> { ("random", L.T("At random (never twice in a row)")), ("inorder", L.T("In order")) }, ph.Order, v => ph.Order = v);
                order.MinWidth = 260; order.HorizontalAlignment = HorizontalAlignment.Left;
                card.Children.Add(Row(L.T("Order"), order));
                var pause = NumBox(ph.Pause, v => ph.Pause = Math.Max(0, v));
                pause.HorizontalAlignment = HorizontalAlignment.Left;
                card.Children.Add(Row(L.T("Pause between attacks (s)"), pause));

                card.Children.Add(new TextBlock { Text = L.T("When this phase starts"), Foreground = Theme.Brush("TextDim", "#8C8C97"), Margin = new Thickness(0, 8, 0, 4) });
                card.Children.Add(new StepListControl(ph.Enter, SeqMode.Boss, _ctx));
                panel.Children.Add(Card(card));
            }
            if (_boss.Phases.Count == 0) panel.Children.Add(Hint(L.T("No phases yet — without one, the boss uses every attack at random.")));
            var add = new Button { Content = L.T("＋ New phase"), Height = 32, Padding = new Thickness(14, 0, 14, 0), HorizontalAlignment = HorizontalAlignment.Left, Style = Theme.Style("AccentButton") };
            add.Click += (s, e) =>
            {
                var last = _boss.Phases.LastOrDefault();
                double next = last == null ? 100 : Math.Max(5, (last.When == "health" ? last.Value : 100) - 35);
                _boss.Phases.Add(new BossPhase { Name = L.F("Phase {0}", _boss.Phases.Count + 1), When = last == null ? "start" : "health", Value = next, Attacks = _boss.Attacks.Select(a => a.Name).ToList() });
                ShowTab(1);
            };
            panel.Children.Add(add);
            return panel;
        }

        private FrameworkElement DeathPage()
        {
            var panel = new StackPanel();
            panel.Children.Add(Hint(L.T("These steps play when its health reaches 0 (it can't be hurt and doesn't hurt anymore). Then its Custom 'death' rules run and it disappears. Leave it empty to make it disappear at once.")));
            panel.Children.Add(new StepListControl(_boss.Death, SeqMode.Boss, _ctx));
            return panel;
        }

        private static Border Card(UIElement child) => new()
        {
            Background = Theme.Brush("Bg1", "#1C1C22"), BorderBrush = Theme.Brush("Border", "#35353F"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10), Child = child
        };

        private static TextBlock Hint(string text) => new() { Text = text, Foreground = Theme.Brush("TextDim", "#8C8C97"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 8) };

        private static FrameworkElement Row(string label, UIElement content)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var l = new TextBlock { Text = label, Foreground = Theme.Brush("TextDim", "#8C8C97"), VerticalAlignment = VerticalAlignment.Center };
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
