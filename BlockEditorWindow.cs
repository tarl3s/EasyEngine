using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyEngine
{
    /// <summary>
    /// A Scratch-style visual builder for the Custom behavior. Each block is "WHEN something → DO something",
    /// and it reads/writes the exact same rule JSON the runtime already understands.
    /// </summary>
    public partial class BlockEditorWindow : Window
    {
        // one WHEN or DO row of a block: its block id and its fields. Trigger (WHEN) params and action (DO) params live in
        // their own rows, so a trigger field and an action field can share a name (e.g. ifvar's "var" + setvar's "var").
        private class Part
        {
            public string Id = "";
            public Dictionary<string, string> P = new(StringComparer.OrdinalIgnoreCase);
            public Part Copy() => new() { Id = Id, P = new(P, StringComparer.OrdinalIgnoreCase) };
        }

        // one "Only run if" row: a variable, a comparison and a value — or a formula of its own
        private class CondRow
        {
            public string Var = "", Op = ">=", Val = "", Adv = "";
            public CondRow Copy() => (CondRow)MemberwiseClone();
            public string Text()
            {
                if (!string.IsNullOrWhiteSpace(Adv)) return Adv.Trim();
                var v = Var.Trim();
                return v.Length == 0 ? "" : $"{v} {Op} {(string.IsNullOrWhiteSpace(Val) ? "1" : Val.Trim())}";
            }
        }

        // one block: WHEN any of its events happens → DO each of its actions, in order, if its conditions allow.
        // Saved as consecutive rules sharing "block" (see Serialize); one WHEN + one DO is one plain rule, as always.
        private class RuleModel
        {
            public List<Part> Whens = new() { new Part { Id = "keypress" } };
            public List<Part> Dos = new() { new Part { Id = "jump" } };
            public string Cond = "";                       // what is saved: the rows joined, kept as written until a row changes
            public List<CondRow> Conds = new() { new CondRow() };
            public bool Any;                               // the rows join with || (any of them) instead of && (all of them)

            public RuleModel Copy() => new()
            {
                Whens = Whens.Select(p => p.Copy()).ToList(), Dos = Dos.Select(p => p.Copy()).ToList(),
                Cond = Cond, Conds = Conds.Select(c => c.Copy()).ToList(), Any = Any,
            };
        }

        private readonly Project _project;
        private readonly Scene _scene;
        private readonly GameObjectData? _obj; // the object whose rules these are (its own variables), when known
        private readonly List<RuleModel> _rules = new();
        private StackPanel _list = null!;
        private ScrollViewer _scroll = null!;
        private string _original = "[]";   // the rules as they came in
        private bool _unreadable;           // they couldn't be read: kept as they are unless blocks are added
        public string ResultJson { get; private set; } = "[]";
        public string SerializeTest() => Result(); // headless round-trip check

        /// <param name="generate">asks the AI (prompt, system) → its reply; null hides "✨ Ask the AI".</param>
        public BlockEditorWindow(Project project, Scene scene, string json, GameObjectData? obj = null, Func<string, string, Task<string>>? generate = null)
        {
            _project = project; _scene = scene; _obj = obj; _generate = generate;
            Title = L.T("Build behavior with blocks");
            Width = 720; Height = generate != null ? 690 : 620; // room for "✨ Ask the AI"
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Theme.Brush("Bg0", "#15151A");
            _original = json ?? "[]";
            ParseJson(_original);
            _unreadable = _rules.Count == 0 && !ReadableRules(_original);
            BuildUi();
            RenderRules();
        }

        private void BuildUi()
        {
            var root = new DockPanel { Margin = new Thickness(12) };

            root.Children.Add(Docked(new TextBlock
            {
                Text = L.T("Each block runs:  WHEN something happens  →  DO something.  Add as many as you like."),
                Foreground = Theme.Brush("TextDim", "#AAA"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2)
            }, Dock.Top));
            root.Children.Add(Docked(new TextBlock
            {
                Text = L.T("Number boxes also take a formula, e.g. rand(100, 300) or score * 2."),
                Foreground = Theme.Brush("TextDim", "#AAA"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            }, Dock.Top));
            if (_unreadable) root.Children.Add(Docked(new TextBlock
            {
                Text = L.T("⚠ These rules couldn't be read (the JSON has a mistake), so they show no blocks. Done keeps them as they are — fix them in the raw rules box, or add blocks here to start over."),
                Foreground = Theme.Brush("Text", "#EEE"), FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            }, Dock.Top));
            if (_generate != null) root.Children.Add(Docked(BuildAiBox(), Dock.Top));

            // bottom buttons
            var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var cancel = new Button { Content = L.T("Cancel"), Height = 32, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 0, 8, 0), Style = Theme.Style("GhostButton") };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            var done = new Button { Content = L.T("Done"), Height = 32, Padding = new Thickness(20, 0, 20, 0), Style = Theme.Style("AccentButton") };
            done.Click += (s, e) => { ResultJson = Result(); DialogResult = true; Close(); };
            bottom.Children.Add(cancel); bottom.Children.Add(done);
            root.Children.Add(Docked(bottom, Dock.Bottom));

            var add = new Button { Content = L.T("➕  Add a block"), Height = 34, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 0, 14, 0), Style = Theme.Style("CardButton") };
            add.Click += (s, e) => { _rules.Add(new RuleModel()); RenderRules(); };
            var addRow = new StackPanel { Orientation = Orientation.Horizontal };
            addRow.Children.Add(add); addRow.Children.Add(RecipesButton()); // ready-made blocks (BlockEditorWindow.Recipes.cs)
            root.Children.Add(Docked(addRow, Dock.Bottom));

            _list = new StackPanel();
            root.Children.Add(_scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list });

            Content = root;
        }

        private static FrameworkElement Docked(FrameworkElement el, Dock d) { DockPanel.SetDock(el, d); return el; }

        private void RenderRules()
        {
            double offset = _scroll?.VerticalOffset ?? 0; // adding a row redraws the cards: stay where the person was
            _list.Children.Clear();
            if (_rules.Count == 0)
                _list.Children.Add(new TextBlock { Text = L.T("No blocks yet — click \"Add a block\" to start."), Foreground = Theme.Brush("TextDim", "#888"), FontSize = 12, Margin = new Thickness(2, 6, 0, 0) });
            for (int i = 0; i < _rules.Count; i++) _list.Children.Add(BuildCard(i));
            if (offset > 0 && _scroll != null) { _scroll.UpdateLayout(); _scroll.ScrollToVerticalOffset(offset); }
        }

        private Border BuildCard(int index)
        {
            var rule = _rules[index];
            var panel = new StackPanel();

            // header: number + move/duplicate/delete
            var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock { Text = L.F("Block {0}", index + 1), Foreground = Theme.Brush("Text", "#EEE"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var hbtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var up = Small("▲", L.T("Move up")); up.IsEnabled = index > 0;
            up.Click += (s, e) => { if (index > 0) { (_rules[index], _rules[index - 1]) = (_rules[index - 1], _rules[index]); RenderRules(); } };
            var dn = Small("▼", L.T("Move down")); dn.IsEnabled = index < _rules.Count - 1;
            dn.Click += (s, e) => { if (index < _rules.Count - 1) { (_rules[index], _rules[index + 1]) = (_rules[index + 1], _rules[index]); RenderRules(); } };
            var dup = Small("⧉", L.T("Duplicate")); dup.Click += (s, e) => { _rules.Insert(index + 1, rule.Copy()); RenderRules(); };
            var del = Small("✕", L.T("Delete")); del.Foreground = Theme.Brush("Danger", "#E5534B"); del.Click += (s, e) => { _rules.RemoveAt(index); RenderRules(); };
            hbtns.Children.Add(up); hbtns.Children.Add(dn); hbtns.Children.Add(dup); hbtns.Children.Add(del);
            Grid.SetColumn(hbtns, 1); header.Children.Add(hbtns);
            panel.Children.Add(header);

            // WHEN rows: any of them starts the block
            panel.Children.Add(Pill(L.T("WHEN"), Theme.IsLight ? "#A35A00" : "#F4A63B"));
            AddParts(panel, rule.Whens, Events, L.T("or when"), L.T("Remove this event"));
            panel.Children.Add(AddRowButton(L.T("＋ Or when…"), L.T("Also run this block when something else happens"),
                () => rule.Whens.Add(new Part { Id = "click" })));

            // DO rows: each runs, in order
            panel.Children.Add(Pill(L.T("→ DO"), Theme.IsLight ? "#13784A" : "#2BB673"));
            AddParts(panel, rule.Dos, Actions, L.T("then"), L.T("Remove this action"));
            panel.Children.Add(AddRowButton(L.T("＋ And do…"), L.T("Do one more thing in this block"),
                () => rule.Dos.Add(new Part { Id = rule.Dos.Count > 0 ? rule.Dos[^1].Id : "jump" })));

            // optional conditions (guided builder)
            BuildCond(panel, rule);

            return new Border { Background = Theme.Brush("Bg1", "#1E1E26"), BorderBrush = Theme.Brush("Border", "#333"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10), Child = panel };
        }

        // the WHEN (or DO) rows of a block: each with its block picker, its ✕ (when there is more than one), its hint and its
        // fields, with a small word between them
        private void AddParts(StackPanel panel, List<Part> parts, BlockDef[] defs, string between, string removeTip)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (i > 0) panel.Children.Add(new TextBlock { Text = between, Foreground = Theme.Brush("TextDim", "#AAA"), FontSize = 10.5, FontStyle = FontStyles.Italic, Margin = new Thickness(2, 4, 0, 2) });
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var combo = DefCombo(defs, part.Id);
                combo.SelectionChanged += (s, e) => { if (combo.SelectedItem is ComboBoxItem it) { part.Id = (string)it.Tag; RenderRules(); } };
                row.Children.Add(combo);
                if (defs == Actions && PlayWindow.ReversibleActions.Contains(part.Id))
                {
                    var rev = new CheckBox { Content = L.T("↔ Reverse"), ToolTip = L.T("Do the opposite: show hides, give takes away, add subtracts, heal hurts, on turns off, play a sound stops it"),
                        IsChecked = part.P.TryGetValue("reverse", out var rv) && IsTrue(rv), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Foreground = Theme.Brush("Text", "#EEE") };
                    rev.Click += (s, e) => { if (rev.IsChecked == true) part.P["reverse"] = "true"; else part.P.Remove("reverse"); };
                    row.Children.Add(rev);
                }
                if (parts.Count > 1)
                {
                    var x = Small("✕", removeTip); x.Margin = new Thickness(6, 0, 0, 2); x.Foreground = Theme.Brush("Danger", "#E5534B");
                    x.Click += (s, e) => { parts.Remove(part); RenderRules(); };
                    row.Children.Add(x);
                }
                panel.Children.Add(row);
                AddHint(panel, DefOf(defs, part.Id).Hint);
                panel.Children.Add(FieldsPanel(DefOf(defs, part.Id), part.P));
            }
        }

        private Button AddRowButton(string text, string tip, Action add)
        {
            var b = new Button { Content = text, ToolTip = tip, Height = 24, FontSize = 11, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(2, 2, 0, 4), HorizontalAlignment = HorizontalAlignment.Left, Style = Theme.Style("GhostButton"), Foreground = Theme.Brush("TextDim", "#AAA") };
            b.Click += (s, e) => { add(); RenderRules(); };
            return b;
        }

        internal static (string label, string sym)[] CondOps => new[] // labels in the editor's language; read back by index
        {
            (L.T("is at least"), ">="), (L.T("is more than"), ">"), (L.T("is"), "=="), (L.T("is not"), "!="), (L.T("is less than"), "<"), (L.T("is at most"), "<=")
        };

        private FrameworkElement Labeled(string label, FrameworkElement ctrl, double minW = 90)
        {
            var s = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            s.Children.Add(new TextBlock { Text = label, Foreground = Theme.Brush("TextDim", "#AAA"), FontSize = 10, Margin = new Thickness(2, 0, 0, 1) });
            ctrl.MinWidth = minW;
            s.Children.Add(ctrl);
            return s;
        }

        private void BuildCond(StackPanel panel, RuleModel rule)
        {
            panel.Children.Add(new TextBlock { Text = L.T("Only run if… (optional)"), Foreground = Theme.Brush("TextDim", "#AAA"), FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 3) });
            if (rule.Conds.Count == 0) rule.Conds.Add(new CondRow());

            // with two or more conditions: all of them, or any of them
            if (rule.Conds.Count > 1)
            {
                var join = new ComboBox { Height = 26, MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 4) };
                join.Items.Add(new ComboBoxItem { Content = L.T("all of these are true"), Tag = "all" });
                join.Items.Add(new ComboBoxItem { Content = L.T("any of these is true"), Tag = "any" });
                join.SelectedIndex = rule.Any ? 1 : 0;
                join.SelectionChanged += (s, e) => { rule.Any = join.SelectedIndex == 1; rule.Cond = JoinConds(rule); RenderRules(); };
                panel.Children.Add(join);
            }

            for (int i = 0; i < rule.Conds.Count; i++)
            {
                if (i > 0) panel.Children.Add(new TextBlock { Text = rule.Any ? L.T("or") : L.T("and"), Foreground = Theme.Brush("TextDim", "#AAA"), FontSize = 10.5, FontStyle = FontStyles.Italic, Margin = new Thickness(2, 4, 0, 2) });
                BuildCondRow(panel, rule, rule.Conds[i]);
                if (i == 0) panel.Children.Add(new TextBlock { Text = L.T("Leave empty to always run.  Example:  coins  is at least  15"), Foreground = Theme.Brush("TextDim", "#8a8a93"), FontSize = 10.5, Margin = new Thickness(2, 1, 0, 0) });
            }
            panel.Children.Add(AddRowButton(L.T("＋ Add condition"), L.T("Another thing that must be true (or may be true) for the block to run"), () => rule.Conds.Add(new CondRow())));
        }

        // one condition: variable / comparison / value, or an advanced formula that overrides them
        private void BuildCondRow(StackPanel panel, RuleModel rule, CondRow c)
        {
            void Changed() => rule.Cond = JoinConds(rule);

            // the variable is picked (or "＋ New…"); a name that isn't in the list (item:wood…) is kept and shown
            var varBox = Dropdown.Make(new[] { ("", L.T("(none)")) }.Concat(NumberVarNames().Select(n => (n, n))), c.Var,
                                       v => { c.Var = v; Changed(); }, L.T("New variable name:"), this);
            varBox.Height = 28;
            var opCb = new ComboBox { Height = 28 };
            foreach (var (lbl, _) in CondOps) opCb.Items.Add(lbl);
            opCb.SelectedIndex = Math.Max(0, Array.FindIndex(CondOps, o => o.sym == c.Op));
            var valBox = new TextBox { Height = 28, Text = c.Val };

            var row = new WrapPanel { Margin = new Thickness(2, 0, 0, 0) };
            row.Children.Add(Labeled(L.T("variable"), varBox, 120));
            row.Children.Add(Labeled(L.T("is…"), opCb, 110));
            row.Children.Add(Labeled(L.T("value"), valBox, 70));
            if (rule.Conds.Count > 1)
            {
                var x = Small("✕", L.T("Remove this condition")); x.Foreground = Theme.Brush("Danger", "#E5534B"); x.VerticalAlignment = VerticalAlignment.Bottom; x.Margin = new Thickness(0, 0, 0, 2);
                x.Click += (s, e) => { rule.Conds.Remove(c); Changed(); RenderRules(); };
                row.Children.Add(x);
            }
            panel.Children.Add(row);

            var adv = new TextBox { Height = 26, Margin = new Thickness(0, 3, 0, 0), Text = c.Adv };
            panel.Children.Add(new TextBlock { Text = L.T("…or an advanced condition (expression — overrides the boxes above)"), Foreground = Theme.Brush("TextDim", "#8a8a93"), FontSize = 10, Margin = new Thickness(0, 4, 0, 1) });
            panel.Children.Add(adv);

            opCb.SelectionChanged += (s, e) => { c.Op = CondOps[Math.Max(0, opCb.SelectedIndex)].sym; Changed(); };
            valBox.TextChanged += (s, e) => { c.Val = valBox.Text; Changed(); };
            adv.TextChanged += (s, e) => { c.Adv = adv.Text; Changed(); };
        }

        // ---- conditions <-> one "cond" formula: the rows joined with && (all) or || (any), each in ( ) when it needs them
        private static string JoinConds(RuleModel rule)
        {
            var parts = rule.Conds.Select(c => c.Text()).Where(t => t.Length > 0).ToList();
            if (parts.Count <= 1) return parts.Count == 1 ? parts[0] : "";
            return string.Join(rule.Any ? " || " : " && ", parts.Select(p => SplitTop(p).ops.Count > 0 ? "(" + p + ")" : p));
        }

        // a saved formula as rows: split at the top-level && (or ||) when only one of them is used; else one row
        private static (List<CondRow> rows, bool any) SplitConds(string cond)
        {
            if (string.IsNullOrWhiteSpace(cond)) return (new List<CondRow> { new() }, false);
            var (parts, ops) = SplitTop(cond);
            if (parts.Count > 1 && ops.Count == 1 && parts.All(p => p.Length > 0))
                return (parts.Select(p => RowOf(StripOuter(p))).ToList(), ops.Contains("||"));
            return (new List<CondRow> { RowOf(cond.Trim()) }, false);
        }

        private static CondRow RowOf(string text)
        {
            var m = System.Text.RegularExpressions.Regex.Match(text, @"^\s*([A-Za-z_][\w:.]*)\s*(>=|<=|==|!=|>|<)\s*(.+?)\s*$");
            return m.Success && SplitTop(text).ops.Count == 0
                ? new CondRow { Var = m.Groups[1].Value, Op = m.Groups[2].Value, Val = m.Groups[3].Value }
                : new CondRow { Adv = text };
        }

        // the parts of a formula between its top-level && / || (outside any parentheses), and which of the two it uses
        private static (List<string> parts, HashSet<string> ops) SplitTop(string s)
        {
            var parts = new List<string>(); var ops = new HashSet<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '(') depth++;
                else if (ch == ')') depth = Math.Max(0, depth - 1);
                else if (depth == 0 && i + 1 < s.Length && (ch == '&' && s[i + 1] == '&' || ch == '|' && s[i + 1] == '|'))
                {
                    ops.Add(s.Substring(i, 2)); parts.Add(s[start..i].Trim()); start = i + 2; i++;
                }
            }
            parts.Add(s[start..].Trim());
            return (parts, ops);
        }

        // "(a || b)" → "a || b" (only when those outer parentheses wrap the whole of it)
        private static string StripOuter(string p)
        {
            while (p.Length >= 2 && p[0] == '(' && p[^1] == ')')
            {
                int depth = 0, close = -1;
                for (int i = 0; i < p.Length; i++)
                {
                    if (p[i] == '(') depth++;
                    else if (p[i] == ')' && --depth == 0) { close = i; break; }
                }
                if (close != p.Length - 1) break;
                p = p[1..^1].Trim();
            }
            return p;
        }

        private void AddHint(Panel panel, string hint)
        {
            if (string.IsNullOrEmpty(hint)) return;
            panel.Children.Add(new TextBlock { Text = hint, Foreground = Theme.Brush("TextDim", "#8a8a93"), FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 2) });
        }

        private Button Small(string t, string tip) => new() { Content = t, ToolTip = tip, Width = 26, Height = 24, FontSize = 11, Margin = new Thickness(3, 0, 0, 0), Padding = new Thickness(0), Style = Theme.Style("GhostButton"), Foreground = Theme.Brush("Text", "#EEE") };

        private TextBlock Pill(string text, string color) => new() { Text = text, Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(0, 4, 0, 4) };

        private ComboBox DefCombo(BlockDef[] defs, string selectedId)
        {
            var cb = new ComboBox { Height = 30, Margin = new Thickness(0, 0, 0, 2), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 260 };
            foreach (var d in defs)
            {
                if (!Offered(d) && d.Id != selectedId) continue; // a block for a behavior this object doesn't have (one already chosen stays)
                var it = new ComboBoxItem { Content = d.Label, Tag = d.Id };
                cb.Items.Add(it);
                if (d.Id == selectedId) cb.SelectedItem = it;
            }
            if (cb.SelectedItem == null && selectedId.Length > 0) // shown as it is (and kept), never as another block
                cb.Items.Insert(0, cb.SelectedItem = L.Keep(new ComboBoxItem { Content = L.F("⚠ Unknown block: {0}", selectedId), Tag = selectedId }));
            if (cb.SelectedItem == null && cb.Items.Count > 0) cb.SelectedIndex = 0;
            return cb;
        }

        // a block for some behaviors (Needs) is offered when the object has one of them (or when the object isn't known)
        private bool Offered(BlockDef d) => d.Needs == null || _obj == null || Array.Exists(d.Needs, k => _obj.Behaviors.Exists(b => b.Kind == k));

        // the behaviors a "behavior" block can switch on this object (all of them when the object isn't known)
        private string[] SwitchKinds(Field f)
        {
            var all = f.Options ?? Array.Empty<string>();
            if (_obj == null) return all;
            var own = all.Where(o => _obj.Behaviors.Exists(b => b.Kind.ToString() == o)).ToArray();
            return own.Length > 0 ? own : all;
        }

        // a block id the palette doesn't have (a typo, a newer version's block) has no fields: its rule's params are kept as they are
        private static BlockDef DefOf(BlockDef[] defs, string id) => Array.Find(defs, d => d.Id == id) ?? new BlockDef(id, id, Array.Empty<Field>());

        private FrameworkElement FieldsPanel(BlockDef def, Dictionary<string, string> bag)
        {
            var wrap = new WrapPanel { Margin = new Thickness(6, 2, 0, 2) };
            foreach (var f in def.Fields) wrap.Children.Add(FieldControl(f, bag));
            return wrap;
        }

        private FrameworkElement FieldControl(Field f, Dictionary<string, string> rule)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 10, 4) };
            stack.Children.Add(new TextBlock { Text = f.Label, Foreground = Theme.Brush("TextDim", "#AAA"), FontSize = 10, Margin = new Thickness(2, 0, 0, 1) });
            string cur = rule.TryGetValue(f.Key, out var v) ? v : f.Type == "behkind" ? SwitchKinds(f).FirstOrDefault() ?? f.Default : f.Default;
            rule[f.Key] = cur; // ensure defaults are saved

            if (f.Type == "bool")
            {
                var chk = new CheckBox { Content = f.Label, IsChecked = IsTrue(cur), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 14, 10, 4) };
                chk.Checked += (s, e) => rule[f.Key] = "true"; chk.Unchecked += (s, e) => rule[f.Key] = "";
                return chk;
            }

            if (f.Type == "color") { stack.Children.Add(ColorField(f, rule, cur)); return stack; }

            Control ctrl;
            if (f.Type is "textvar" or "flagvar" or "colorvar" or "listvar" or "numvar" or "localvar" or "item")
            {
                // variables (or items) of the right kind, plus "＋ New…"
                var (options, prompt) = f.Type switch
                {
                    "textvar" => (Names(ParamSources.TextVariables(_project)), L.T("New text variable name:")),
                    "colorvar" => (Names(ParamSources.ColorVariables(_project)), L.T("New colour variable name:")),
                    "listvar" => (Names(ParamSources.ListVariables(_project)), L.T("New list name:")),
                    "numvar" => (Names(NumberVarNames()), L.T("New variable name:")),
                    "localvar" => (Names(LocalVarNames()), L.T("New variable name:")),
                    "item" => (ParamSources.Items(_project).Select(i => (i, ToolArt.Title(i))), L.T("New item name (e.g. wood, key, shovel):")),
                    _ => (Names(ParamSources.YesNoVariables(_project)), L.T("New Yes/No variable name:")),
                };
                var cb = Dropdown.Make(options, cur, v => rule[f.Key] = v, prompt, this);
                cb.Height = 28; cb.MinWidth = 140;
                ctrl = cb;
            }
            else if (f.Type is "choice" or "behkind")
            {
                // shown in the editor's language, saved by value (the item's Tag)
                var cb = new ComboBox { Height = 28, MinWidth = 80 };
                foreach (var o in f.Type == "behkind" ? SwitchKinds(f) : f.Options ?? Array.Empty<string>()) cb.Items.Add(new ComboBoxItem { Content = f.Type == "behkind" ? KindLabel(o) : OptionLabel(o), Tag = o });
                if (!cb.Items.Cast<ComboBoxItem>().Any(i => (string)i.Tag == cur)) // a value written by hand or by the AI is kept; none shows so
                    cb.Items.Add(cur.Length > 0 ? L.Keep(new ComboBoxItem { Content = cur, Tag = cur }) : new ComboBoxItem { Content = L.T("(none)"), Tag = cur });
                cb.SelectedItem = cb.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == cur);
                cb.SelectionChanged += (s, e) => rule[f.Key] = (cb.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                ctrl = cb;
            }
            else if (f.Type is "obj" or "who" or "aim" or "scene" or "layer" or "key" or "sound" or "anim")
            {
                // names, keys and files are the person's own (shown as they are); a value that isn't in the list is kept,
                // an empty one shows "(none)" (and is saved as left out) until one is picked
                var cb = Dropdown.Make(PickOptions(f.Type), cur, v => rule[f.Key] = v);
                cb.Height = 28; cb.MinWidth = 120;
                ctrl = cb;
            }
            else // num, val (a number or a formula), text
            {
                var tb = new TextBox { Height = 28, MinWidth = f.Type == "text" ? 140 : 70, Text = cur, VerticalContentAlignment = VerticalAlignment.Center };
                if (f.Type is "num" or "val") tb.ToolTip = L.T("A number, or a formula like rand(100, 300) or score * 2");
                tb.TextChanged += (s, e) => rule[f.Key] = tb.Text;
                ctrl = tb;
            }
            stack.Children.Add(ctrl);
            return stack;
        }

        // a colour: a swatch button that opens the colour picker, and next to it the colour variables ("{name}" is saved)
        private FrameworkElement ColorField(Field f, Dictionary<string, string> rule, string cur)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var swatch = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(3), BorderBrush = Theme.Brush("Border", "#35353F"), BorderThickness = new Thickness(1) };
            var hex = L.Keep(new TextBlock { Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var sp = new StackPanel { Orientation = Orientation.Horizontal }; sp.Children.Add(swatch); sp.Children.Add(hex);
            var btn = new Button { Content = sp, Height = 28, Padding = new Thickness(6, 0, 8, 0), Style = Theme.Style("CardButton"), ToolTip = L.T("Click to pick a colour") };
            var vars = new ComboBox { Height = 28, MinWidth = 110, Margin = new Thickness(6, 0, 0, 0), ToolTip = L.T("Or use a colour variable") };
            vars.Items.Add(new ComboBoxItem { Content = L.T("(a colour)"), Tag = "" });
            foreach (var n in ParamSources.ColorVariables(_project)) vars.Items.Add(L.Keep(new ComboBoxItem { Content = n, Tag = "{" + n + "}" })); // the person's own names
            bool sync = false;
            void Show(string v)
            {
                rule[f.Key] = v;
                bool isVar = v.Length > 0 && VarScope.ColorHex(v) == null; // none yet: "(a colour)" with an empty swatch
                swatch.Background = isVar || v.Length == 0 ? Brushes.Transparent : Theme.Hex(v);
                hex.Text = isVar || v.Length == 0 ? "—" : v;
                sync = true;
                var item = vars.Items.Cast<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, isVar ? v : "", StringComparison.OrdinalIgnoreCase));
                if (item == null && isVar) vars.Items.Add(item = L.Keep(new ComboBoxItem { Content = v.Trim('{', '}'), Tag = v })); // a variable that isn't declared anywhere
                vars.SelectedItem = item;
                sync = false;
            }
            btn.Click += (s, e) => ColorPickerPopup.Open(btn, VarScope.ColorHex(rule[f.Key]) ?? VarScope.White, Show);
            vars.SelectionChanged += (s, e) =>
            {
                if (sync || vars.SelectedItem is not ComboBoxItem ci) return;
                var tag = (string)ci.Tag;
                Show(tag.Length > 0 ? tag : (VarScope.ColorHex(hex.Text) ?? f.Default));
            };
            Show(cur);
            row.Children.Add(btn); row.Children.Add(vars);
            return row;
        }

        // what an obj / aim / scene / key / sound field offers: (saved value, what it shows)
        private List<(string value, string label)> PickOptions(string type) => type switch
        {
            "key" => ParamSources.Keys.Select(k => (k.value, k.value)).ToList(), // a key is a value: shown as the game names it
            "scene" => Names(_project.Scenes.Select(s => s.Name)).ToList(),
            "layer" => new[] { ("", L.T("(none)")) }.Concat(Names(_scene.Layers.Select(l => l.Name))).ToList(), // the scene's layers (the person's own names)
            "sound" => Names(ParamSources.Sounds(_project)).ToList(), // the relative paths the game plays
            "anim" => new[] { ("", L.T("(none)")) }.Concat(Names(_obj?.Animations.Select(a => a.Name).Distinct() ?? Enumerable.Empty<string>())).ToList(), // this object's own animations
            "aim" => new[] { ("", L.T("(none)")) }.Concat(ParamSources.ObjectOrPrefabOptions(_scene, _project)).ToList(),
            "who" => new[] { ("", L.T("self")), ("other", L.T("what it touched")) }.Concat(ParamSources.ObjectOrPrefabOptions(_scene, _project)).ToList(), // destroy: itself (saved as nothing), the touched one, or a name
            _ => new[] { ("self", L.T("self")) }.Concat(ParamSources.ObjectOrPrefabOptions(_scene, _project)).ToList(), // obj
        };

        private static IEnumerable<(string, string)> Names(IEnumerable<string> names) => names.Select(n => (n, n));

        // the number variables a picker offers: the project's, plus the ones only rules use (here and in other objects' rules)
        private List<string> NumberVarNames()
        {
            var set = new SortedSet<string>(ParamSources.Variables(_project), StringComparer.OrdinalIgnoreCase);
            foreach (var b in _project.Scenes.SelectMany(s => s.Layers).SelectMany(l => l.Objects).SelectMany(o => o.Behaviors))
                if (b.Kind == BehaviorKind.Custom) foreach (var v in ParamSources.RuleVars(b.Text("script", ""), "setvar", "addvar", "ifvar")) set.Add(v);
            AddRuleVars(set, "ifvar", "setvar", "addvar");
            return set.ToList();
        }

        // this object's own variables: the ones it declares, the ones its rules use (saved and in this window)
        private List<string> LocalVarNames()
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_obj != null)
            {
                foreach (var k in _obj.Variables.Keys) set.Add(k);
                foreach (var b in _obj.Behaviors)
                    if (b.Kind == BehaviorKind.Custom) foreach (var v in ParamSources.RuleVars(b.Text("script", ""), "setlocal", "addlocal", "iflocal")) set.Add(v);
            }
            AddRuleVars(set, "iflocal", "setlocal", "addlocal");
            return set.ToList();
        }

        private void AddRuleVars(SortedSet<string> set, string when, params string[] dos)
        {
            foreach (var r in _rules)
            {
                foreach (var w in r.Whens) if (w.Id == when && w.P.TryGetValue("var", out var a) && a.Trim().Length > 0) set.Add(a.Trim());
                foreach (var d in r.Dos) if (dos.Contains(d.Id) && d.P.TryGetValue("var", out var b) && b.Trim().Length > 0) set.Add(b.Trim());
            }
        }


        // ---------------- JSON <-> rules ----------------
        // One stored rule (a WHEN, maybe a DO, its condition and the block it belongs to).
        private record FlatRule(Part When, Part? Do, string Cond, int Block);

        private static readonly BlockDef NoAction = new("", "", Array.Empty<Field>());

        private void ParseJson(string json)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json, LooseJson.Options);
                var root = doc.RootElement;
                // one rule written on its own ({"when":…,"do":…}, by hand) is a list of one
                IEnumerable<System.Text.Json.JsonElement> items = root.ValueKind == System.Text.Json.JsonValueKind.Array ? root.EnumerateArray()
                    : root.ValueKind == System.Text.Json.JsonValueKind.Object && (root.TryGetProperty("when", out _) || root.TryGetProperty("do", out _)) ? new[] { root }
                    : Array.Empty<System.Text.Json.JsonElement>();
                RuleModel? cur = null; int curBlock = 0;
                foreach (var el in items)
                {
                    if (el.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                    var f = ParseRule(el);
                    // consecutive rules of one block ("block": n) are its WHENs and DOs (see Serialize)
                    if (cur != null && f.Block != 0 && f.Block == curBlock)
                    {
                        if (f.Do != null) cur.Dos.Add(f.Do);
                        if (!cur.Whens.Exists(w => SamePart(w, f.When))) cur.Whens.Add(f.When);
                        continue;
                    }
                    var (rows, any) = SplitConds(f.Cond);
                    cur = new RuleModel { Whens = new() { f.When }, Dos = f.Do != null ? new() { f.Do } : new(), Cond = f.Cond, Conds = rows, Any = any };
                    curBlock = f.Block;
                    _rules.Add(cur);
                }
            }
            catch { }
        }

        // what Done saves: the blocks — or, when the rules couldn't be read and no block was added, the rules as they were
        private string Result() => _unreadable && _rules.Count == 0 ? _original : Serialize();

        // rules the blocks can open: nothing at all, a list, or one rule (anything else is a mistake the person must see)
        private static bool ReadableRules(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return true;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json, LooseJson.Options);
                return doc.RootElement.ValueKind is System.Text.Json.JsonValueKind.Array or System.Text.Json.JsonValueKind.Object;
            }
            catch (System.Text.Json.JsonException) { return false; }
        }

        private static bool SamePart(Part a, Part b)
            => a.Id == b.Id && a.P.Count == b.P.Count && a.P.All(kv => b.P.TryGetValue(kv.Key, out var v) && v == kv.Value);

        private FlatRule ParseRule(System.Text.Json.JsonElement el)
        {
            string when = "always", act = "", cond = ""; int block = 0;
            var whenP = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var doP = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // pass 1: read the structural keys and collect the rest (with their side of "do")
            bool seenDo = false;
            string? actionExpr = null;
            var guards = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var others = new List<(string name, string val, bool beforeDo)>();
            foreach (var p in el.EnumerateObject())
            {
                var name = p.Name.ToLowerInvariant();
                string val = p.Value.ValueKind == System.Text.Json.JsonValueKind.String ? (p.Value.GetString() ?? "")
                           : p.Value.ValueKind == System.Text.Json.JsonValueKind.Number ? p.Value.GetDouble().ToString(CultureInfo.InvariantCulture)
                           : p.Value.ValueKind == System.Text.Json.JsonValueKind.True ? "true"
                           : p.Value.ValueKind == System.Text.Json.JsonValueKind.False ? "false"
                           : p.Value.ToString();
                val = AiSpecFix.UnwrapExpr(val) ?? val; // "expr: a - b" is the formula a - b (the game reads it so too)
                if (name == "when") { when = val.ToLowerInvariant(); continue; }
                if (name == "do") { act = val.ToLowerInvariant(); seenDo = true; continue; }
                if (name == "cond") { cond = val; continue; }
                if (name == "block") { block = int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) ? b : 0; continue; }
                if (name is "if" or "ifop" or "ifval") { guards[name] = val; continue; }
                others.Add((name, val, !seenDo));
            }
            // the runtime's other names for a block ("keydown", "hover", "accel"…) open as that block — never as the
            // first block of the list, which saving would then write over the rule
            if (when == "keydown") when = "key";
            if (!Array.Exists(Events, d => d.Id == when) && AiSpecFix.WhenSynonyms.TryGetValue(when, out var ws) && Array.Exists(Events, d => d.Id == ws)) when = ws;
            if (!Array.Exists(Actions, d => d.Id == act) && AiSpecFix.DoSynonyms.TryGetValue(act, out var ds) && Array.Exists(Actions, d => d.Id == ds)) act = ds;

            // pass 2: bucket each param by WHICH block defines it (not just its side of "do"),
            // so an action param written before "do" (or vice-versa) still lands in the right bucket.
            // A key the game reads under another name ("aim" for shoot's "at"…, KeyAliases) lands in that field.
            // (A block's extra WHEN has no "do": all of its params are the event's.)
            var evDef = DefOf(Events, when);
            var acDef = seenDo ? DefOf(Actions, act) : NoAction;
            bool Given(string k) => others.Exists(o => o.name == k);
            string? FieldKey(BlockDef def, string id, string k)
            {
                static bool Has(BlockDef d, string key) => Array.Exists(d.Fields, f => f.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (Has(def, k)) return k;
                if (!KeyAliases.TryGetValue(id, out var aliases)) return null;
                var key = Array.Find(aliases, a => a.alias == k).key;
                if (key == null || !Has(def, key) || Given(key)) return null;
                return Array.Find(aliases, a => a.key == key && Given(a.alias)).alias == k ? key : null; // two of them: the one the game prefers
            }
            foreach (var (name, val, beforeDo) in others)
            {
                if (name == "expr" && seenDo && !(FieldKey(evDef, when, "expr") != null && beforeDo)) { actionExpr = val; continue; } // action-side expression value
                string? ev = FieldKey(evDef, when, name), ac = seenDo ? FieldKey(acDef, act, name) : null;
                if (ev != null && ac == null) whenP[ev] = val;
                else if (ac != null && ev == null) doP[ac] = val;
                else if (beforeDo) whenP[ev ?? name] = val; else doP[ac ?? name] = val; // shared/unknown key → use its side of "do"
            }
            // an action-side expression ("expr") belongs in that action's value field (kept as it is when it has none)
            if (actionExpr != null) doP[Array.Find(acDef.Fields, f => f.Type == "val")?.Key ?? "expr"] = actionExpr;
            // a field the rule leaves out: what the game does then (not always the default a new block starts with)
            foreach (var (def, bag) in new[] { (evDef, whenP), (acDef, doP) })
                foreach (var f in def.Fields)
                    if (f.Absent != null && !bag.ContainsKey(f.Key)) bag[f.Key] = f.Absent;
            // upgrade the legacy if/ifop/ifval guard into the "Only if" condition
            if (guards.TryGetValue("if", out var gv))
            {
                var op = guards.TryGetValue("ifop", out var go) ? go : ">=";
                var val = guards.TryGetValue("ifval", out var gval) ? gval : "0";
                var legacy = $"{gv} {op} {val}";
                cond = string.IsNullOrWhiteSpace(cond) ? legacy : $"({cond}) && ({legacy})";
            }
            return new FlatRule(new Part { Id = when, P = whenP }, seenDo ? new Part { Id = act, P = doP } : null, cond, block);
        }

        // Each block is saved as plain rules the game (and every older reader) understands. One WHEN + one DO: one rule, as it
        // always was. WHENs W1..Wn and DOs D1..Dm: (W1,D1)…(W1,Dm), then W2…Wn without a "do" — all with the same "cond" and
        // the same "block": n, which the game reads as "any of these WHENs fires → every DO runs once".
        private string Serialize()
        {
            var rules = new List<string>();
            int blockNo = 0;
            foreach (var r in _rules)
            {
                if (r.Whens.Count == 0) continue;
                int block = r.Whens.Count > 1 || r.Dos.Count > 1 ? ++blockNo : 0;
                if (r.Dos.Count == 0) rules.Add(RuleJson(r.Whens[0], null, r.Cond, block));
                foreach (var d in r.Dos) rules.Add(RuleJson(r.Whens[0], d, r.Cond, block));
                foreach (var w in r.Whens.Skip(1)) rules.Add(RuleJson(w, null, r.Cond, block));
            }
            return "[" + string.Join(",", rules) + "]";
        }

        private string RuleJson(Part when, Part? act, string cond, int block)
        {
            void Emit(List<string> into, BlockDef def, Dictionary<string, string> bag, HashSet<string> used)
            {
                foreach (var f in def.Fields)
                {
                    used.Add(f.Key);
                    string raw = bag.TryGetValue(f.Key, out var v) ? v : f.Default;
                    if (f.Type == "bool") { if (IsTrue(raw)) into.Add($"\"{f.Key}\":true"); continue; }
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    if (f.Type is "num" or "val") raw = raw.Trim().ToLowerInvariant() switch { "true" => "1", "false" => "0", _ => raw.Trim() }; // a Yes/No value
                    if (f.Type is "num" or "val" && Number(raw) is string n) into.Add($"\"{f.Key}\":{n}");
                    else if (f.Type == "num") into.Add($"\"{f.Key}\":{Q(raw)}"); // a formula: the game works it out
                    else if (f.Type == "val") { into.Add($"\"expr\":{Q(raw)}"); used.Add("expr"); } // expression value
                    else into.Add($"\"{f.Key}\":{Q(raw)}");
                }
                // preserve any params this block doesn't model (so AI-written rules survive editing)
                foreach (var kv in bag)
                {
                    if (used.Contains(kv.Key) || string.IsNullOrWhiteSpace(kv.Value)) continue;
                    used.Add(kv.Key);
                    if (Number(kv.Value) is string n) into.Add($"\"{kv.Key}\":{n}");
                    else if (kv.Value is "true" or "false") into.Add($"\"{kv.Key}\":{kv.Value}"); // a JSON true/false stays one
                    else into.Add($"\"{kv.Key}\":{Q(kv.Value)}");
                }
            }
            HashSet<string> Reserved() => new(StringComparer.OrdinalIgnoreCase) { "when", "do", "cond", "block" };

            // trigger params go before "do", action params after — so the runtime can
            // tell them apart even when a trigger and an action field share a name.
            var parts = new List<string> { $"\"when\":{Q(when.Id)}" };
            Emit(parts, DefOf(Events, when.Id), when.P, Reserved());
            if (act != null)
            {
                parts.Add($"\"do\":{Q(act.Id)}");
                Emit(parts, DefOf(Actions, act.Id), act.P, Reserved());
            }
            if (!string.IsNullOrWhiteSpace(cond)) parts.Add($"\"cond\":{Q(cond)}");
            if (block > 0) parts.Add($"\"block\":{block}");
            return "{" + string.Join(",", parts) + "}";
        }

        // a number as JSON ("0,5" is 0.5), or null when the text isn't one
        private static string? Number(string s) => LooseJson.TryParse(s, out var d) ? d.ToString(CultureInfo.InvariantCulture) : null;

        private static bool IsTrue(string? v) => (v ?? "").Trim().ToLowerInvariant() is "true" or "1" or "yes";

        private static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    }
}
