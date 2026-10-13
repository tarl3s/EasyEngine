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
        private class RuleModel
        {
            public string When = "keypress";
            public string Do = "jump";
            public string Cond = "";
            // trigger (WHEN) params and action (DO) params are kept apart so a trigger
            // field and an action field can share a name (e.g. ifvar's "var" + setvar's "var").
            public Dictionary<string, string> WhenP = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> DoP = new(StringComparer.OrdinalIgnoreCase);

            public RuleModel Copy() => new()
            {
                When = When, Do = Do, Cond = Cond,
                WhenP = new(WhenP, StringComparer.OrdinalIgnoreCase), DoP = new(DoP, StringComparer.OrdinalIgnoreCase)
            };
        }

        private readonly Project _project;
        private readonly Scene _scene;
        private readonly GameObjectData? _obj; // the object whose rules these are (its own variables), when known
        private readonly List<RuleModel> _rules = new();
        private StackPanel _list = null!;
        private ScrollViewer _scroll = null!;
        public string ResultJson { get; private set; } = "[]";
        public string SerializeTest() => Serialize(); // headless round-trip check

        /// <param name="generate">asks the AI (prompt, system) → its reply; null hides "✨ Ask the AI".</param>
        public BlockEditorWindow(Project project, Scene scene, string json, GameObjectData? obj = null, Func<string, string, Task<string>>? generate = null)
        {
            _project = project; _scene = scene; _obj = obj; _generate = generate;
            Title = L.T("Build behavior with blocks");
            Width = 720; Height = generate != null ? 690 : 620; // room for "✨ Ask the AI"
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Theme.Brush("Bg0", "#15151A");
            ParseJson(json);
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
            if (_generate != null) root.Children.Add(Docked(BuildAiBox(), Dock.Top));

            // bottom buttons
            var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var cancel = new Button { Content = L.T("Cancel"), Height = 32, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 0, 8, 0), Style = Theme.Style("GhostButton") };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            var done = new Button { Content = L.T("Done"), Height = 32, Padding = new Thickness(20, 0, 20, 0), Style = Theme.Style("AccentButton") };
            done.Click += (s, e) => { ResultJson = Serialize(); DialogResult = true; Close(); };
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
            _list.Children.Clear();
            if (_rules.Count == 0)
                _list.Children.Add(new TextBlock { Text = L.T("No blocks yet — click \"Add a block\" to start."), Foreground = Theme.Brush("TextDim", "#888"), FontSize = 12, Margin = new Thickness(2, 6, 0, 0) });
            for (int i = 0; i < _rules.Count; i++) _list.Children.Add(BuildCard(i));
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

            // WHEN row
            panel.Children.Add(Pill(L.T("WHEN"), Theme.IsLight ? "#A35A00" : "#F4A63B"));
            var whenCombo = DefCombo(Events, rule.When);
            whenCombo.SelectionChanged += (s, e) => { if (whenCombo.SelectedItem is ComboBoxItem it) { rule.When = (string)it.Tag; RenderRules(); } };
            panel.Children.Add(whenCombo);
            AddHint(panel, DefOf(Events, rule.When).Hint);
            panel.Children.Add(FieldsPanel(DefOf(Events, rule.When), rule.WhenP));

            // DO row
            panel.Children.Add(Pill(L.T("→ DO"), Theme.IsLight ? "#13784A" : "#2BB673"));
            var doCombo = DefCombo(Actions, rule.Do);
            doCombo.SelectionChanged += (s, e) => { if (doCombo.SelectedItem is ComboBoxItem it) { rule.Do = (string)it.Tag; RenderRules(); } };
            panel.Children.Add(doCombo);
            AddHint(panel, DefOf(Actions, rule.Do).Hint);
            panel.Children.Add(FieldsPanel(DefOf(Actions, rule.Do), rule.DoP));

            // optional condition (guided builder)
            BuildCond(panel, rule);

            return new Border { Background = Theme.Brush("Bg1", "#1E1E26"), BorderBrush = Theme.Brush("Border", "#333"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10), Child = panel };
        }

        private static (string label, string sym)[] CondOps => new[] // labels in the editor's language; read back by index
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

            var m = System.Text.RegularExpressions.Regex.Match(rule.Cond ?? "", @"^\s*([A-Za-z_][\w:.]*)\s*(>=|<=|==|!=|>|<)\s*(.+?)\s*$");

            // the variable is picked (or "＋ New…"); a name that isn't in the list (item:wood…) is kept and shown
            string condVar = m.Success ? m.Groups[1].Value : "";
            Action? varPicked = null;
            var varBox = Dropdown.Make(new[] { ("", L.T("(none)")) }.Concat(NumberVarNames().Select(n => (n, n))), condVar,
                                       v => { condVar = v; varPicked?.Invoke(); }, L.T("New variable name:"), this);
            varBox.Height = 28;
            var opCb = new ComboBox { Height = 28 };
            foreach (var (lbl, _) in CondOps) opCb.Items.Add(lbl);
            opCb.SelectedIndex = m.Success ? Math.Max(0, Array.FindIndex(CondOps, o => o.sym == m.Groups[2].Value)) : 0;
            var valBox = new TextBox { Height = 28, Text = m.Success ? m.Groups[3].Value : "" };

            var row = new WrapPanel { Margin = new Thickness(2, 0, 0, 0) };
            row.Children.Add(Labeled(L.T("variable"), varBox, 120));
            row.Children.Add(Labeled(L.T("is…"), opCb, 110));
            row.Children.Add(Labeled(L.T("value"), valBox, 70));
            panel.Children.Add(row);
            panel.Children.Add(new TextBlock { Text = L.T("Leave empty to always run.  Example:  coins  is at least  15"), Foreground = Theme.Brush("TextDim", "#8a8a93"), FontSize = 10.5, Margin = new Thickness(2, 1, 0, 0) });

            var adv = new TextBox { Height = 26, Margin = new Thickness(0, 3, 0, 0), Text = m.Success ? "" : (rule.Cond ?? "") };
            panel.Children.Add(new TextBlock { Text = L.T("…or an advanced condition (expression — overrides the boxes above)"), Foreground = Theme.Brush("TextDim", "#8a8a93"), FontSize = 10, Margin = new Thickness(0, 4, 0, 1) });
            panel.Children.Add(adv);

            void Rebuild()
            {
                if (!string.IsNullOrWhiteSpace(adv.Text)) { rule.Cond = adv.Text.Trim(); return; }
                var v = condVar.Trim();
                if (string.IsNullOrEmpty(v)) { rule.Cond = ""; return; }
                var sym = CondOps[Math.Max(0, opCb.SelectedIndex)].sym;
                var val = string.IsNullOrWhiteSpace(valBox.Text) ? "1" : valBox.Text.Trim();
                rule.Cond = $"{v} {sym} {val}";
            }
            varPicked = Rebuild;
            opCb.SelectionChanged += (s, e) => Rebuild();
            valBox.TextChanged += (s, e) => Rebuild();
            adv.TextChanged += (s, e) => Rebuild();
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
                var it = new ComboBoxItem { Content = d.Label, Tag = d.Id };
                cb.Items.Add(it);
                if (d.Id == selectedId) cb.SelectedItem = it;
            }
            if (cb.SelectedItem == null && cb.Items.Count > 0) cb.SelectedIndex = 0;
            return cb;
        }

        private static BlockDef DefOf(BlockDef[] defs, string id) => Array.Find(defs, d => d.Id == id) ?? defs[0];

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
            string cur = rule.TryGetValue(f.Key, out var v) ? v : f.Default;
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
            else if (f.Type == "choice")
            {
                // shown in the editor's language, saved by value (the item's Tag)
                var cb = new ComboBox { Height = 28, MinWidth = 80 };
                foreach (var o in f.Options ?? Array.Empty<string>()) cb.Items.Add(new ComboBoxItem { Content = OptionLabel(o), Tag = o });
                if (!cb.Items.Cast<ComboBoxItem>().Any(i => (string)i.Tag == cur)) // a value written by hand or by the AI is kept; none shows so
                    cb.Items.Add(cur.Length > 0 ? L.Keep(new ComboBoxItem { Content = cur, Tag = cur }) : new ComboBoxItem { Content = L.T("(none)"), Tag = cur });
                cb.SelectedItem = cb.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == cur);
                cb.SelectionChanged += (s, e) => rule[f.Key] = (cb.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                ctrl = cb;
            }
            else if (f.Type is "obj" or "aim" or "scene" or "key" or "sound")
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
            "sound" => Names(ParamSources.Sounds(_project)).ToList(), // the relative paths the game plays
            "aim" => new[] { ("", L.T("(none)")) }.Concat(ParamSources.ObjectOrPrefabOptions(_scene, _project)).ToList(),
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
                if (r.When == when && r.WhenP.TryGetValue("var", out var a) && a.Trim().Length > 0) set.Add(a.Trim());
                if (dos.Contains(r.Do) && r.DoP.TryGetValue("var", out var b) && b.Trim().Length > 0) set.Add(b.Trim());
            }
        }


        // ---------------- JSON <-> rules ----------------
        private void ParseJson(string json)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json, LooseJson.Options);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return;
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                    var r = new RuleModel { When = "always", Do = "" };
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
                        if (name == "when") { r.When = val.ToLowerInvariant(); continue; }
                        if (name == "do") { r.Do = val.ToLowerInvariant(); seenDo = true; continue; }
                        if (name == "cond") { r.Cond = val; continue; }
                        if (name is "if" or "ifop" or "ifval") { guards[name] = val; continue; }
                        others.Add((name, val, !seenDo));
                    }
                    // the runtime's other names for a block ("keydown", "hover", "accel"…) open as that block — never as the
                    // first block of the list, which saving would then write over the rule
                    if (r.When == "keydown") r.When = "key";
                    if (!Array.Exists(Events, d => d.Id == r.When) && AiSpecFix.WhenSynonyms.TryGetValue(r.When, out var ws) && Array.Exists(Events, d => d.Id == ws)) r.When = ws;
                    if (!Array.Exists(Actions, d => d.Id == r.Do) && AiSpecFix.DoSynonyms.TryGetValue(r.Do, out var ds) && Array.Exists(Actions, d => d.Id == ds)) r.Do = ds;

                    // pass 2: bucket each param by WHICH block defines it (not just its side of "do"),
                    // so an action param written before "do" (or vice-versa) still lands in the right bucket.
                    // A key the game reads under another name ("aim" for shoot's "at"…, KeyAliases) lands in that field.
                    var evDef = DefOf(Events, r.When);
                    var acDef = DefOf(Actions, r.Do);
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
                        if (name == "expr" && !(FieldKey(evDef, r.When, "expr") != null && beforeDo)) { actionExpr = val; continue; } // action-side expression value
                        string? ev = FieldKey(evDef, r.When, name), ac = FieldKey(acDef, r.Do, name);
                        if (ev != null && ac == null) r.WhenP[ev] = val;
                        else if (ac != null && ev == null) r.DoP[ac] = val;
                        else if (beforeDo) r.WhenP[ev ?? name] = val; else r.DoP[ac ?? name] = val; // shared/unknown key → use its side of "do"
                    }
                    // an action-side expression ("expr") belongs in that action's value field (kept as it is when it has none)
                    if (actionExpr != null) r.DoP[Array.Find(acDef.Fields, f => f.Type == "val")?.Key ?? "expr"] = actionExpr;
                    // a field the rule leaves out: what the game does then (not always the default a new block starts with)
                    foreach (var (def, bag) in new[] { (evDef, r.WhenP), (acDef, r.DoP) })
                        foreach (var f in def.Fields)
                            if (f.Absent != null && !bag.ContainsKey(f.Key)) bag[f.Key] = f.Absent;
                    // upgrade the legacy if/ifop/ifval guard into the "Only if" condition
                    if (guards.TryGetValue("if", out var gv))
                    {
                        var op = guards.TryGetValue("ifop", out var go) ? go : ">=";
                        var val = guards.TryGetValue("ifval", out var gval) ? gval : "0";
                        var cond = $"{gv} {op} {val}";
                        r.Cond = string.IsNullOrWhiteSpace(r.Cond) ? cond : $"({r.Cond}) && ({cond})";
                    }
                    _rules.Add(r);
                }
            }
            catch { }
        }

        private string Serialize()
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < _rules.Count; i++)
            {
                var r = _rules[i];
                if (i > 0) sb.Append(',');
                sb.Append('{');

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

                // trigger params go before "do", action params after — so the runtime can
                // tell them apart even when a trigger and an action field share a name.
                var whenParts = new List<string>();
                Emit(whenParts, DefOf(Events, r.When), r.WhenP, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "when", "do", "cond" });
                var doParts = new List<string>();
                Emit(doParts, DefOf(Actions, r.Do), r.DoP, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "when", "do", "cond" });

                var parts = new List<string> { $"\"when\":{Q(r.When)}" };
                parts.AddRange(whenParts);
                parts.Add($"\"do\":{Q(r.Do)}");
                parts.AddRange(doParts);
                if (!string.IsNullOrWhiteSpace(r.Cond)) parts.Add($"\"cond\":{Q(r.Cond)}");

                sb.Append(string.Join(",", parts));
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        // a number as JSON ("0,5" is 0.5), or null when the text isn't one
        private static string? Number(string s) => LooseJson.TryParse(s, out var d) ? d.ToString(CultureInfo.InvariantCulture) : null;

        private static bool IsTrue(string? v) => (v ?? "").Trim().ToLowerInvariant() is "true" or "1" or "yes";

        private static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    }
}
