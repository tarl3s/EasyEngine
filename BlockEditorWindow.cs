using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyEngine
{
    /// <summary>
    /// A Scratch-style visual builder for the Custom behavior. Each block is "WHEN something → DO something",
    /// and it reads/writes the exact same rule JSON the runtime already understands.
    /// </summary>
    public class BlockEditorWindow : Window
    {
        // field types: num, text, choice, obj, scene, key, val (number OR expression)
        private record Field(string Key, string Label, string Type, string Default = "", string[]? Options = null);
        private record BlockDef(string Id, string Label, Field[] Fields, string Hint = "");

        private static readonly string[] Ops = { ">=", ">", "<=", "<", "==", "!=" };
        private static readonly string[] Props = { "x", "y", "vx", "vy", "rot", "facing", "health", "w", "h", "speed" };
        private static readonly string[] CommonKeys = { "Space", "Up", "Down", "Left", "Right", "W", "A", "S", "D", "E", "F", "J", "K", "Enter", "LeftShift" };

        private static readonly BlockDef[] Events =
        {
            new("start",      "When it spawns", Array.Empty<Field>()),
            new("always",     "Every frame (always)", Array.Empty<Field>()),
            new("key",        "While a key is held", new[] { new Field("key", "Key", "key", "Space") }),
            new("keypress",   "When a key is pressed", new[] { new Field("key", "Key", "key", "Space"), new Field("cooldown", "Cooldown (s)", "num", "0.3") }, "Fires once per press. Cooldown stops it repeating too fast while held."),
            new("timer",      "Every few seconds", new[] { new Field("every", "Seconds", "num", "1") }, "Repeats forever on this interval."),
            new("touch",      "When it touches an object", new[] { new Field("name", "Object", "obj") }, "Fires the moment it starts touching that object (once per touch)."),
            new("death",      "When it dies", Array.Empty<Field>()),
            new("ifvar",      "While a variable is…", new[] { new Field("var", "Variable", "text"), new Field("op", "is", "choice", ">=", Ops), new Field("value", "Value", "num", "1") }),
            new("iflocal",    "While its own variable is…", new[] { new Field("var", "Variable", "text"), new Field("op", "is", "choice", ">=", Ops), new Field("value", "Value", "num", "1") }),
            new("hasitem",    "While it has an item", new[] { new Field("item", "Item", "text") }),
            new("isnight",    "While it's night", Array.Empty<Field>(), "Needs a Day/Night cycle object in the scene. Great for enemies that only come out at night."),
            new("isday",      "While it's day", Array.Empty<Field>(), "Needs a Day/Night cycle object in the scene."),
            new("wheelup",    "When the mouse wheel scrolls up", Array.Empty<Field>()),
            new("wheeldown",  "When the mouse wheel scrolls down", Array.Empty<Field>()),
            new("expr",       "While a condition is true", new[] { new Field("expr", "Condition", "text") }),
            new("click",      "When clicked", Array.Empty<Field>()),
            new("rightclick", "When right-clicked", Array.Empty<Field>()),
            new("mouseover",  "While the mouse is over it", Array.Empty<Field>()),
            new("mousedown",  "While the mouse is pressed on it", Array.Empty<Field>()),
            new("mousepress", "When the mouse is clicked (anywhere)", Array.Empty<Field>()),
        };

        private static readonly string[] TweenProps = { "x", "y", "opacity", "rot", "w", "h", "scale" };
        private static readonly string[] Eases = { "linear", "easein", "easeout", "easeinout" };
        private static readonly string[] YesNo = { "0", "1" };

        private static readonly BlockDef[] Actions =
        {
            new("velocity",  "Move at a speed", new[] { new Field("vx", "→ sideways", "num", "0"), new Field("vy", "↓ up / down", "num", "0") },
                "Speed in each direction.  sideways: + = right, − = left.   up/down: + = down, − = up.   e.g. −250 sideways = move left."),
            new("move",      "Nudge (shift position)", new[] { new Field("dx", "→ sideways / s", "num", "0"), new Field("dy", "↓ up-down / s", "num", "0") },
                "Shifts its position a little each second, ignoring physics."),
            new("accelerate","Speed up over time", new[] { new Field("ax", "→ sideways", "num", "0"), new Field("ay", "↓ up-down", "num", "0") },
                "Adds to its speed gradually (not instant). + up/down makes it fall faster."),
            new("jump",      "Jump", new[] { new Field("force", "Strength", "num", "600") }, "Only works while standing on the ground. Bigger = higher."),
            new("rotate",    "Spin", new[] { new Field("speed", "Degrees / s", "num", "90") }, "Rotates continuously. Negative spins the other way."),
            new("shoot",     "Shoot", new[] { new Field("directions", "Directions", "num", "1"), new Field("speed", "Speed", "num", "420"), new Field("life", "Life (s)", "num", "2"), new Field("bullet", "Bullet", "obj", "Bullet") },
                "Fires projectiles. 1 direction = straight ahead; more = spread all around."),
            new("spawn",     "Spawn an object", new[] { new Field("object", "Object", "obj"), new Field("at", "From", "choice", "", new[] { "", "top", "near" }) },
                "From (empty) = at this object · top = falling from the top of the screen · near = on the ground, a little to the left or right."),
            new("destroy",   "Destroy itself", Array.Empty<Field>()),
            new("goscene",   "Go to another scene", new[] { new Field("scene", "Scene", "scene") }),
            new("setvar",    "Set a variable", new[] { new Field("var", "Variable", "text"), new Field("to", "To", "val", "0") }),
            new("addvar",    "Add to a variable", new[] { new Field("var", "Variable", "text"), new Field("amount", "Amount", "val", "1") }),
            new("setlocal",  "Set its own variable", new[] { new Field("var", "Variable", "text"), new Field("to", "To", "val", "0") }),
            new("addlocal",  "Add to its own variable", new[] { new Field("var", "Variable", "text"), new Field("amount", "Amount", "val", "1") }),
            new("set",       "Set a property", new[] { new Field("target", "Object (or self)", "obj", "self"), new Field("prop", "Property", "choice", "x", Props), new Field("to", "To", "val", "0") }),
            new("additem",   "Give an item", new[] { new Field("item", "Item", "text"), new Field("amount", "Amount", "num", "1") }),
            new("removeitem","Remove an item", new[] { new Field("item", "Item", "text"), new Field("amount", "Amount", "num", "1") }),
            new("boost",     "Multiply its speed", new[] { new Field("factor", "Times", "num", "2") }, "2 = twice as fast, 0.5 = half. Affects Platformer/Top-down speed."),
            new("setspeed",  "Set its speed level", new[] { new Field("factor", "Level", "num", "1") }, "1 = normal, 0.5 = half, 2 = double. Resets the boost."),
            new("say",       "Show a message", new[] { new Field("text", "Text", "text"), new Field("speaker", "Speaker (optional)", "text") }),
            new("emit",      "Burst of particles", new[] { new Field("count", "How many", "num", "16"), new Field("speed", "Speed", "num", "180"), new Field("spread", "Spread (°)", "num", "360"), new Field("angle", "Angle (°)", "num", "0"), new Field("life", "Life (s)", "num", "0.8"), new Field("size", "Size", "num", "8"), new Field("gravity", "Gravity", "num", "400"), new Field("color", "Color", "text", "#FFD24D") },
                "A one-off puff of particles (sparks, dust, explosions)."),
            new("tween",     "Glide a property", new[] { new Field("prop", "Property", "choice", "x", TweenProps), new Field("to", "To", "val", "0"), new Field("duration", "Over (s)", "num", "0.5"), new Field("ease", "Ease", "choice", "linear", Eases), new Field("loop", "Loop (0/1)", "num", "0"), new Field("yoyo", "Yoyo (0/1)", "num", "0") },
                "Smoothly animates a property to a value. Loop repeats; yoyo bounces back and forth."),
            new("setvolume", "Set volume", new[] { new Field("channel", "Channel", "text", "master"), new Field("to", "Level (0-1)", "num", "1") }),
            new("breaktile", "Break a tile", new[] { new Field("where", "Which", "choice", "touching", new[] { "touching", "below", "above", "left", "right", "front", "atmouse" }) },
                "Breaks a tile of a breakable tilemap: any it overlaps (touching), the one below/above/beside/in front of it, or the one at the mouse."),
            new("settime",   "Set time of day", new[] { new Field("time", "Time", "choice", "night", new[] { "day", "noon", "dusk", "night", "midnight" }) },
                "Jumps the Day/Night cycle to that moment (needs a Day/Night cycle object in the scene)."),
            new("playsound", "Play a sound", new[] { new Field("sound", "Sound", "sound"), new Field("volume", "Volume (0-1)", "num", "0.8") },
                "Plays a sound file from the project's Assets/sounds folder."),
            new("log",       "Write to the console", new[] { new Field("text", "Message", "text"), new Field("level", "Level", "choice", "info", new[] { "info", "warn", "error" }) },
                "Shows a line in the debug console (F1 while playing). Use {variable} to print a value, e.g. Coins: {coins}."),
            new("save",      "Save the game", Array.Empty<Field>()),
            new("load",      "Load the game", Array.Empty<Field>()),
        };

        private class RuleModel
        {
            public string When = "keypress";
            public string Do = "jump";
            public string Cond = "";
            // trigger (WHEN) params and action (DO) params are kept apart so a trigger
            // field and an action field can share a name (e.g. ifvar's "var" + setvar's "var").
            public Dictionary<string, string> WhenP = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> DoP = new(StringComparer.OrdinalIgnoreCase);
        }

        private readonly Project _project;
        private readonly Scene _scene;
        private readonly List<RuleModel> _rules = new();
        private StackPanel _list = null!;
        public string ResultJson { get; private set; } = "[]";
        public string SerializeTest() => Serialize(); // headless round-trip check

        public BlockEditorWindow(Project project, Scene scene, string json)
        {
            _project = project; _scene = scene;
            Title = "Build behavior with blocks";
            Width = 720; Height = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = B("Bg0", "#15151A");
            ParseJson(json);
            BuildUi();
            RenderRules();
        }

        private Brush B(string key, string fallback)
        {
            if (Application.Current.TryFindResource(key) is Brush b) return b;
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)); } catch { return Brushes.Gray; }
        }
        private Style? St(string key) => Application.Current.TryFindResource(key) as Style;

        private void BuildUi()
        {
            var root = new DockPanel { Margin = new Thickness(12) };

            root.Children.Add(Docked(new TextBlock
            {
                Text = "Each block runs:  WHEN something happens  →  DO something.  Add as many as you like.",
                Foreground = B("TextDim", "#AAA"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            }, Dock.Top));

            // bottom buttons
            var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var cancel = new Button { Content = "Cancel", Height = 32, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 0, 8, 0), Style = St("GhostButton") };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            var done = new Button { Content = "Done", Height = 32, Padding = new Thickness(20, 0, 20, 0), Style = St("AccentButton") };
            done.Click += (s, e) => { ResultJson = Serialize(); DialogResult = true; Close(); };
            bottom.Children.Add(cancel); bottom.Children.Add(done);
            root.Children.Add(Docked(bottom, Dock.Bottom));

            var add = new Button { Content = "➕  Add a block", Height = 34, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 0, 14, 0), Style = St("CardButton") };
            add.Click += (s, e) => { _rules.Add(new RuleModel()); RenderRules(); };
            root.Children.Add(Docked(add, Dock.Bottom));

            _list = new StackPanel();
            root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list });

            Content = root;
        }

        private static FrameworkElement Docked(FrameworkElement el, Dock d) { DockPanel.SetDock(el, d); return el; }

        private void RenderRules()
        {
            _list.Children.Clear();
            if (_rules.Count == 0)
                _list.Children.Add(new TextBlock { Text = "No blocks yet — click \"Add a block\" to start.", Foreground = B("TextDim", "#888"), FontSize = 12, Margin = new Thickness(2, 6, 0, 0) });
            for (int i = 0; i < _rules.Count; i++) _list.Children.Add(BuildCard(i));
        }

        private Border BuildCard(int index)
        {
            var rule = _rules[index];
            var panel = new StackPanel();

            // header: number + move/delete
            var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock { Text = $"Block {index + 1}", Foreground = B("Text", "#EEE"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var hbtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var up = Small("▲"); up.Click += (s, e) => { if (index > 0) { (_rules[index], _rules[index - 1]) = (_rules[index - 1], _rules[index]); RenderRules(); } };
            var dn = Small("▼"); dn.Click += (s, e) => { if (index < _rules.Count - 1) { (_rules[index], _rules[index + 1]) = (_rules[index + 1], _rules[index]); RenderRules(); } };
            var del = Small("✕"); del.Foreground = B("Danger", "#E5534B"); del.Click += (s, e) => { _rules.RemoveAt(index); RenderRules(); };
            hbtns.Children.Add(up); hbtns.Children.Add(dn); hbtns.Children.Add(del);
            Grid.SetColumn(hbtns, 1); header.Children.Add(hbtns);
            panel.Children.Add(header);

            // WHEN row
            panel.Children.Add(Pill("WHEN", "#F4A63B"));
            var whenCombo = DefCombo(Events, rule.When);
            whenCombo.SelectionChanged += (s, e) => { if (whenCombo.SelectedItem is ComboBoxItem it) { rule.When = (string)it.Tag; RenderRules(); } };
            panel.Children.Add(whenCombo);
            AddHint(panel, DefOf(Events, rule.When).Hint);
            panel.Children.Add(FieldsPanel(DefOf(Events, rule.When), rule.WhenP));

            // DO row
            panel.Children.Add(Pill("→ DO", "#2BB673"));
            var doCombo = DefCombo(Actions, rule.Do);
            doCombo.SelectionChanged += (s, e) => { if (doCombo.SelectedItem is ComboBoxItem it) { rule.Do = (string)it.Tag; RenderRules(); } };
            panel.Children.Add(doCombo);
            AddHint(panel, DefOf(Actions, rule.Do).Hint);
            panel.Children.Add(FieldsPanel(DefOf(Actions, rule.Do), rule.DoP));

            // optional condition (guided builder)
            BuildCond(panel, rule);

            return new Border { Background = B("Bg1", "#1E1E26"), BorderBrush = B("Border", "#333"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10), Child = panel };
        }

        private static readonly (string label, string sym)[] CondOps =
        {
            ("is at least", ">="), ("is more than", ">"), ("is", "=="), ("is not", "!="), ("is less than", "<"), ("is at most", "<=")
        };

        private FrameworkElement Labeled(string label, FrameworkElement ctrl, double minW = 90)
        {
            var s = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            s.Children.Add(new TextBlock { Text = label, Foreground = B("TextDim", "#AAA"), FontSize = 10, Margin = new Thickness(2, 0, 0, 1) });
            ctrl.MinWidth = minW;
            s.Children.Add(ctrl);
            return s;
        }

        private void BuildCond(StackPanel panel, RuleModel rule)
        {
            panel.Children.Add(new TextBlock { Text = "Only run if… (optional)", Foreground = B("TextDim", "#AAA"), FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 3) });

            var m = System.Text.RegularExpressions.Regex.Match(rule.Cond ?? "", @"^\s*([A-Za-z_][\w:.]*)\s*(>=|<=|==|!=|>|<)\s*(.+?)\s*$");

            var varBox = new TextBox { Height = 28, Text = m.Success ? m.Groups[1].Value : "" };
            var opCb = new ComboBox { Height = 28 };
            foreach (var (lbl, _) in CondOps) opCb.Items.Add(lbl);
            opCb.SelectedIndex = m.Success ? Math.Max(0, Array.FindIndex(CondOps, o => o.sym == m.Groups[2].Value)) : 0;
            var valBox = new TextBox { Height = 28, Text = m.Success ? m.Groups[3].Value : "" };

            var row = new WrapPanel { Margin = new Thickness(2, 0, 0, 0) };
            row.Children.Add(Labeled("variable", varBox, 120));
            row.Children.Add(Labeled("is…", opCb, 110));
            row.Children.Add(Labeled("value", valBox, 70));
            panel.Children.Add(row);
            panel.Children.Add(new TextBlock { Text = "Leave empty to always run.  Example:  coins  is at least  15", Foreground = B("TextDim", "#8a8a93"), FontSize = 10.5, Margin = new Thickness(2, 1, 0, 0) });

            var adv = new TextBox { Height = 26, Margin = new Thickness(0, 3, 0, 0), Text = m.Success ? "" : (rule.Cond ?? "") };
            panel.Children.Add(new TextBlock { Text = "…or an advanced condition (expression — overrides the boxes above)", Foreground = B("TextDim", "#8a8a93"), FontSize = 10, Margin = new Thickness(0, 4, 0, 1) });
            panel.Children.Add(adv);

            void Rebuild()
            {
                if (!string.IsNullOrWhiteSpace(adv.Text)) { rule.Cond = adv.Text.Trim(); return; }
                var v = varBox.Text.Trim();
                if (string.IsNullOrEmpty(v)) { rule.Cond = ""; return; }
                var sym = CondOps[Math.Max(0, opCb.SelectedIndex)].sym;
                var val = string.IsNullOrWhiteSpace(valBox.Text) ? "1" : valBox.Text.Trim();
                rule.Cond = $"{v} {sym} {val}";
            }
            varBox.TextChanged += (s, e) => Rebuild();
            opCb.SelectionChanged += (s, e) => Rebuild();
            valBox.TextChanged += (s, e) => Rebuild();
            adv.TextChanged += (s, e) => Rebuild();
        }

        private void AddHint(Panel panel, string hint)
        {
            if (string.IsNullOrEmpty(hint)) return;
            panel.Children.Add(new TextBlock { Text = hint, Foreground = B("TextDim", "#8a8a93"), FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 2) });
        }

        private Button Small(string t) => new() { Content = t, Width = 26, Height = 24, FontSize = 11, Margin = new Thickness(3, 0, 0, 0), Padding = new Thickness(0), Style = St("GhostButton"), Foreground = B("Text", "#EEE") };

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
            stack.Children.Add(new TextBlock { Text = f.Label, Foreground = B("TextDim", "#AAA"), FontSize = 10, Margin = new Thickness(2, 0, 0, 1) });
            string cur = rule.TryGetValue(f.Key, out var v) ? v : f.Default;
            rule[f.Key] = cur; // ensure defaults are saved

            Control ctrl;
            if (f.Type == "choice")
            {
                var cb = new ComboBox { Height = 28, MinWidth = 80 };
                foreach (var o in f.Options ?? Array.Empty<string>()) cb.Items.Add(o.Length == 0 ? "(none)" : o);
                cb.SelectedItem = cur.Length == 0 ? "(none)" : cur;
                if (cb.SelectedItem == null && cb.Items.Count > 0) cb.SelectedIndex = 0;
                cb.SelectionChanged += (s, e) => { var sel = cb.SelectedItem as string ?? ""; rule[f.Key] = sel == "(none)" ? "" : sel; };
                ctrl = cb;
            }
            else if (f.Type == "obj" || f.Type == "scene" || f.Type == "key" || f.Type == "sound")
            {
                var cb = new ComboBox { Height = 28, MinWidth = 120 };
                var items = SuggestionsFor(f.Type).ToList();
                if (!string.IsNullOrEmpty(cur) && !items.Contains(cur)) items.Insert(0, cur); // keep a value that isn't in the list
                foreach (var o in items) cb.Items.Add(o);
                cb.SelectedItem = cur;
                if (cb.SelectedItem == null && cb.Items.Count > 0) { cb.SelectedIndex = 0; rule[f.Key] = (string)cb.Items[0]; }
                cb.SelectionChanged += (s, e) => rule[f.Key] = cb.SelectedItem as string ?? "";
                ctrl = cb;
            }
            else // num, text, val
            {
                var tb = new TextBox { Height = 28, MinWidth = f.Type == "text" ? 140 : 70, Text = cur, VerticalContentAlignment = VerticalAlignment.Center };
                tb.TextChanged += (s, e) => rule[f.Key] = tb.Text;
                ctrl = tb;
            }
            stack.Children.Add(ctrl);
            return stack;
        }

        private IEnumerable<string> SuggestionsFor(string type)
        {
            if (type == "key") return CommonKeys;
            if (type == "scene") return _project.Scenes.Select(s => s.Name);
            if (type == "sound") return ParamSources.Sounds(_project); // the relative paths the runtime plays
            // obj: names of objects in the scene (+ "self" for the set action)
            var names = _scene.Layers.SelectMany(l => l.Objects).Select(o => o.Name).Distinct();
            return new[] { "self" }.Concat(names);
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
                                   : p.Value.ToString();
                        if (name == "when") { r.When = val.ToLowerInvariant(); continue; }
                        if (name == "do") { r.Do = val.ToLowerInvariant(); seenDo = true; continue; }
                        if (name == "cond") { r.Cond = val; continue; }
                        if (name is "if" or "ifop" or "ifval") { guards[name] = val; continue; }
                        others.Add((name, val, !seenDo));
                    }
                    if (r.When == "keydown") r.When = "key";
                    if (r.Do == "accel") r.Do = "accelerate";

                    // pass 2: bucket each param by WHICH block defines it (not just its side of "do"),
                    // so an action param written before "do" (or vice-versa) still lands in the right bucket.
                    var evDef = DefOf(Events, r.When);
                    var acDef = DefOf(Actions, r.Do);
                    bool EvHas(string k) => Array.Exists(evDef.Fields, f => f.Key.Equals(k, StringComparison.OrdinalIgnoreCase));
                    bool AcHas(string k) => Array.Exists(acDef.Fields, f => f.Key.Equals(k, StringComparison.OrdinalIgnoreCase));
                    foreach (var (name, val, beforeDo) in others)
                    {
                        if (name == "expr" && !(EvHas("expr") && beforeDo)) { actionExpr = val; continue; } // action-side expression value
                        bool ev = EvHas(name), ac = AcHas(name);
                        if (ev && !ac) r.WhenP[name] = val;
                        else if (ac && !ev) r.DoP[name] = val;
                        else if (beforeDo) r.WhenP[name] = val; else r.DoP[name] = val; // shared/unknown key → use its side of "do"
                    }
                    // an action-side expression ("expr") belongs in that action's value field
                    if (actionExpr != null)
                    {
                        var valField = Array.Find(acDef.Fields, f => f.Type == "val");
                        if (valField != null) r.DoP[valField.Key] = actionExpr;
                    }
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
                        if (string.IsNullOrWhiteSpace(raw) && f.Type != "num") continue;
                        if (f.Type == "num")
                        {
                            if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) into.Add($"\"{f.Key}\":{d.ToString(CultureInfo.InvariantCulture)}");
                        }
                        else if (f.Type == "val")
                        {
                            if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) into.Add($"\"{f.Key}\":{d.ToString(CultureInfo.InvariantCulture)}");
                            else { into.Add($"\"expr\":{Q(raw)}"); used.Add("expr"); } // expression value
                        }
                        else into.Add($"\"{f.Key}\":{Q(raw)}");
                    }
                    // preserve any params this block doesn't model (so AI-written rules survive editing)
                    foreach (var kv in bag)
                    {
                        if (used.Contains(kv.Key) || string.IsNullOrWhiteSpace(kv.Value)) continue;
                        used.Add(kv.Key);
                        if (double.TryParse(kv.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) into.Add($"\"{kv.Key}\":{d.ToString(CultureInfo.InvariantCulture)}");
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

        private static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    }
}
