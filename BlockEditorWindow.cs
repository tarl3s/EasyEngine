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
        // field types: num, text, choice, obj, scene, key, val (number OR expression), textvar / flagvar / colorvar / listvar
        // (a text / Yes-No / colour / list variable, picked), numvar / localvar (a number variable / this object's own one,
        // picked), item (picked), color (a colour picker, or a colour variable as "{name}"), bool (a checkbox, saved as true
        // or left out). Every picked kind is saved as plain text, exactly like a typed one.
        private record Field(string Key, string Label, string Type, string Default = "", string[]? Options = null);
        private record BlockDef(string Id, string Label, Field[] Fields, string Hint = "");

        private static readonly string[] Ops = { ">=", ">", "<=", "<", "==", "!=" };
        private static readonly string[] TextOps = { "is", "isnot", "contains", "empty", "notempty" };
        private static readonly string[] ListOps = { "contains", "notcontains", "empty", "notempty", "countis", "countatleast" };
        private static readonly string[] Props = { "x", "y", "vx", "vy", "rot", "facing", "health", "w", "h", "speed" };
        private static readonly string[] CommonKeys = { "Space", "Up", "Down", "Left", "Right", "W", "A", "S", "D", "E", "F", "J", "K", "Enter", "LeftShift" };

        // the blocks, with their labels in the editor's language (ids, keys, types and values stay English: they are saved)
        private readonly BlockDef[] Events = MakeEvents(), Actions = MakeActions();

        private static BlockDef[] MakeEvents() => new BlockDef[]
        {
            new("start",      L.T("When it spawns"), Array.Empty<Field>()),
            new("always",     L.T("Every frame (always)"), Array.Empty<Field>()),
            new("key",        L.T("While a key is held"), new[] { new Field("key", L.T("Key"), "key", "Space") }),
            new("keypress",   L.T("When a key is pressed"), new[] { new Field("key", L.T("Key"), "key", "Space"), new Field("cooldown", L.T("Cooldown (s)"), "num", "0.3") }, L.T("Fires once per press. Cooldown stops it repeating too fast while held.")),
            new("timer",      L.T("Every few seconds"), new[] { new Field("every", L.T("Seconds"), "num", "1") }, L.T("Repeats forever on this interval.")),
            new("after",      L.T("After some seconds"), new[] { new Field("seconds", L.T("Seconds"), "num", "5") }, L.T("Fires once, that many seconds after it appears.")),
            new("touch",      L.T("When it touches an object"), new[] { new Field("name", L.T("Object"), "obj") }, L.T("Fires the moment it starts touching that object (once per touch).")),
            new("death",      L.T("When it dies"), Array.Empty<Field>()),
            new("ifvar",      L.T("While a variable is…"), new[] { new Field("var", L.T("Variable"), "numvar"), new Field("op", L.T("is"), "choice", ">=", Ops), new Field("value", L.T("Value"), "num", "1") }),
            new("iflocal",    L.T("While its own variable is…"), new[] { new Field("var", L.T("Variable"), "localvar"), new Field("op", L.T("is"), "choice", ">=", Ops), new Field("value", L.T("Value"), "num", "1") }),
            new("hasitem",    L.T("While it has an item"), new[] { new Field("item", L.T("Item"), "item") }),
            new("iftext",     L.T("While a text variable…"), new[] { new Field("var", L.T("Text variable"), "textvar"), new Field("op", L.T("is"), "choice", "is", TextOps), new Field("value", L.T("Text"), "text"), new Field("local", L.T("Its own"), "bool") },
                L.T("Compares words (capital letters don't matter). \"Its own\" reads this object's own text variable.")),
            new("iflist",     L.T("While a list…"), new[] { new Field("var", L.T("List"), "listvar"), new Field("op", L.T("is"), "choice", "contains", ListOps), new Field("value", L.T("Item or number"), "text"), new Field("local", L.T("Its own"), "bool") },
                L.T("Checks a list: has an item (capital letters don't matter), is empty, or has a number of items.")),
            new("isnight",    L.T("While it's night"), Array.Empty<Field>(), L.T("Needs a Day/Night cycle object in the scene. Great for enemies that only come out at night.")),
            new("isday",      L.T("While it's day"), Array.Empty<Field>(), L.T("Needs a Day/Night cycle object in the scene.")),
            new("wheelup",    L.T("When the mouse wheel scrolls up"), Array.Empty<Field>()),
            new("wheeldown",  L.T("When the mouse wheel scrolls down"), Array.Empty<Field>()),
            new("expr",       L.T("While a condition is true"), new[] { new Field("expr", L.T("Condition"), "text") }),
            new("click",      L.T("When clicked"), Array.Empty<Field>()),
            new("rightclick", L.T("When right-clicked"), Array.Empty<Field>()),
            new("mouseover",  L.T("While the mouse is over it"), Array.Empty<Field>()),
            new("mousedown",  L.T("While the mouse is pressed on it"), Array.Empty<Field>()),
            new("mousepress", L.T("When the mouse is clicked (anywhere)"), Array.Empty<Field>()),
        };

        private static readonly string[] TweenProps = { "x", "y", "opacity", "rot", "w", "h", "scale" };
        private static readonly string[] Eases = { "linear", "easein", "easeout", "easeinout" };
        private static readonly string[] YesNo = { "0", "1" };

        private static BlockDef[] MakeActions() => new BlockDef[]
        {
            new("velocity",  L.T("Move at a speed"), new[] { new Field("vx", L.T("→ sideways"), "num", "0"), new Field("vy", L.T("↓ up / down"), "num", "0") },
                L.T("Speed in each direction.  sideways: + = right, − = left.   up/down: + = down, − = up.   e.g. −250 sideways = move left.")),
            new("move",      L.T("Nudge (shift position)"), new[] { new Field("dx", L.T("→ sideways / s"), "num", "0"), new Field("dy", L.T("↓ up-down / s"), "num", "0") },
                L.T("Shifts its position a little each second, ignoring physics.")),
            new("accelerate",L.T("Speed up over time"), new[] { new Field("ax", L.T("→ sideways"), "num", "0"), new Field("ay", L.T("↓ up-down"), "num", "0") },
                L.T("Adds to its speed gradually (not instant). + up/down makes it fall faster.")),
            new("jump",      L.T("Jump"), new[] { new Field("force", L.T("Strength"), "num", "600") }, L.T("Only works while standing on the ground. Bigger = higher.")),
            new("rotate",    L.T("Spin"), new[] { new Field("speed", L.T("Degrees / s"), "num", "90") }, L.T("Rotates continuously. Negative spins the other way.")),
            new("shoot",     L.T("Shoot"), new[] { new Field("directions", L.T("Directions"), "num", "1"), new Field("speed", L.T("Speed"), "num", "420"), new Field("life", L.T("Life (s)"), "num", "2"), new Field("bullet", L.T("Bullet"), "obj", "Bullet") },
                L.T("Fires projectiles. 1 direction = straight ahead; more = spread all around.")),
            new("spawn",     L.T("Spawn an object"), new[] { new Field("object", L.T("Object"), "obj"), new Field("at", L.T("From"), "choice", "", new[] { "", "top", "near" }) },
                L.T("From (empty) = at this object · top = falling from the top of the screen · near = on the ground, a little to the left or right.")),
            new("destroy",   L.T("Destroy itself"), Array.Empty<Field>()),
            new("hide",      L.T("Hide"), new[] { new Field("target", L.T("Object (or self)"), "obj", "self") }, L.T("Makes it invisible — it is still there and still touches things. \"Show\" brings it back.")),
            new("show",      L.T("Show"), new[] { new Field("target", L.T("Object (or self)"), "obj", "self") }, L.T("Makes a hidden object visible again.")),
            new("goscene",   L.T("Go to another scene"), new[] { new Field("scene", L.T("Scene"), "scene") }),
            new("setvar",    L.T("Set a variable"), new[] { new Field("var", L.T("Variable"), "numvar"), new Field("to", L.T("To"), "val", "0") }),
            new("addvar",    L.T("Add to a variable"), new[] { new Field("var", L.T("Variable"), "numvar"), new Field("amount", L.T("Amount"), "val", "1") }),
            new("setlocal",  L.T("Set its own variable"), new[] { new Field("var", L.T("Variable"), "localvar"), new Field("to", L.T("To"), "val", "0") }),
            new("addlocal",  L.T("Add to its own variable"), new[] { new Field("var", L.T("Variable"), "localvar"), new Field("amount", L.T("Amount"), "val", "1") }),
            new("settext",   L.T("Set a text"), new[] { new Field("var", L.T("Text variable"), "textvar"), new Field("to", L.T("To"), "text"), new Field("local", L.T("Its own"), "bool") },
                L.T("Puts words in a text variable. {name} inside is replaced by that variable's value, e.g. Hello {playerName}!")),
            new("addtext",   L.T("Add to a text"), new[] { new Field("var", L.T("Text variable"), "textvar"), new Field("to", L.T("Words to add"), "text"), new Field("local", L.T("Its own"), "bool") },
                L.T("Adds words to the end of a text variable.")),
            new("toggle",    L.T("Switch a Yes/No"), new[] { new Field("var", L.T("Yes/No variable"), "flagvar"), new Field("local", L.T("Its own"), "bool") },
                L.T("Yes becomes No and No becomes Yes.")),
            new("setcolor",  L.T("Set a colour"), new[] { new Field("var", L.T("Colour variable"), "colorvar"), new Field("to", L.T("Colour"), "color", "#FFFFFF"), new Field("local", L.T("Its own"), "bool") },
                L.T("Puts a colour in a colour variable. Use it in \"Change the colour\" or in particles.")),
            new("tint",      L.T("Change the colour"), new[] { new Field("target", L.T("Object (or self)"), "obj", "self"), new Field("color", L.T("Colour"), "color", "#FFFFFF") },
                L.T("Paints this object (or another one) with a colour or a colour variable. An object drawn with a picture keeps its picture.")),
            new("listadd",   L.T("Add to a list"), new[] { new Field("var", L.T("List"), "listvar"), new Field("value", L.T("Item"), "text"), new Field("local", L.T("Its own"), "bool") },
                L.T("Adds an item to the end of a list. {name} inside is replaced by that variable's value.")),
            new("listremove",L.T("Remove from a list"), new[] { new Field("var", L.T("List"), "listvar"), new Field("value", L.T("Item"), "text"), new Field("local", L.T("Its own"), "bool") },
                L.T("Removes the first item equal to it (capital letters don't matter).")),
            new("listclear", L.T("Empty a list"), new[] { new Field("var", L.T("List"), "listvar"), new Field("local", L.T("Its own"), "bool") }),
            new("set",      L.T("Set a property"), new[] { new Field("target", L.T("Object (or self)"), "obj", "self"), new Field("prop", L.T("Property"), "choice", "x", Props), new Field("to", L.T("To"), "val", "0") }),
            new("additem",   L.T("Give an item"), new[] { new Field("item", L.T("Item"), "item"), new Field("amount", L.T("Amount"), "num", "1") }),
            new("removeitem",L.T("Remove an item"), new[] { new Field("item", L.T("Item"), "item"), new Field("amount", L.T("Amount"), "num", "1") }),
            new("boost",     L.T("Multiply its speed"), new[] { new Field("factor", L.T("Times"), "num", "2") }, L.T("2 = twice as fast, 0.5 = half. Affects Platformer/Top-down speed.")),
            new("setspeed",  L.T("Set its speed level"), new[] { new Field("factor", L.T("Level"), "num", "1") }, L.T("1 = normal, 0.5 = half, 2 = double. Resets the boost.")),
            new("say",       L.T("Show a message"), new[] { new Field("text", L.T("Text"), "text"), new Field("speaker", L.T("Speaker (optional)"), "text") }),
            new("emit",      L.T("Burst of particles"), new[] { new Field("count", L.T("How many"), "num", "16"), new Field("speed", L.T("Speed"), "num", "180"), new Field("spread", L.T("Spread (°)"), "num", "360"), new Field("angle", L.T("Angle (°)"), "num", "0"), new Field("life", L.T("Life (s)"), "num", "0.8"), new Field("size", L.T("Size"), "num", "8"), new Field("gravity", L.T("Gravity"), "num", "400"), new Field("color", L.T("Color"), "text", "#FFD24D") },
                L.T("A one-off puff of particles (sparks, dust, explosions).")),
            new("tween",     L.T("Glide a property"), new[] { new Field("prop", L.T("Property"), "choice", "x", TweenProps), new Field("to", L.T("To"), "val", "0"), new Field("duration", L.T("Over (s)"), "num", "0.5"), new Field("ease", L.T("Ease"), "choice", "linear", Eases), new Field("loop", L.T("Loop (0/1)"), "num", "0"), new Field("yoyo", L.T("Yoyo (0/1)"), "num", "0") },
                L.T("Smoothly animates a property to a value. Loop repeats; yoyo bounces back and forth.")),
            new("setvolume", L.T("Set volume"), new[] { new Field("channel", L.T("Channel"), "choice", "master", new[] { "master", "music", "sfx" }), new Field("to", L.T("Level (0-1)"), "num", "1") }),
            new("breaktile", L.T("Break a tile"), new[] { new Field("where", L.T("Which"), "choice", "touching", new[] { "touching", "below", "above", "left", "right", "front", "atmouse" }) },
                L.T("Breaks a tile of a breakable tilemap: any it overlaps (touching), the one below/above/beside/in front of it, or the one at the mouse.")),
            new("settime",   L.T("Set time of day"), new[] { new Field("time", L.T("Time"), "choice", "night", new[] { "day", "noon", "dusk", "night", "midnight" }) },
                L.T("Jumps the Day/Night cycle to that moment (needs a Day/Night cycle object in the scene).")),
            new("playsound", L.T("Play a sound"), new[] { new Field("sound", L.T("Sound"), "sound"), new Field("volume", L.T("Volume (0-1)"), "num", "0.8") },
                L.T("Plays a sound file from the project's Assets/sounds folder.")),
            new("log",       L.T("Write to the console"), new[] { new Field("text", L.T("Message"), "text"), new Field("level", L.T("Level"), "choice", "info", new[] { "info", "warn", "error" }) },
                L.T("Shows a line in the debug console (F1 while playing). Use {variable} to print a value, e.g. Coins: {coins}.")),
            new("save",      L.T("Save the game"), Array.Empty<Field>()),
            new("load",      L.T("Load the game"), Array.Empty<Field>()),
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
                Foreground = Theme.Brush("TextDim", "#AAA"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
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
            root.Children.Add(Docked(add, Dock.Bottom));

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
                if (cur.Length > 0 && !cb.Items.Cast<ComboBoxItem>().Any(i => (string)i.Tag == cur))
                    cb.Items.Add(L.Keep(new ComboBoxItem { Content = cur, Tag = cur })); // a value written by hand or by the AI is kept
                cb.SelectedItem = cb.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == cur);
                if (cb.SelectedItem == null && cb.Items.Count > 0) cb.SelectedIndex = 0;
                cb.SelectionChanged += (s, e) => rule[f.Key] = (cb.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                ctrl = cb;
            }
            else if (f.Type == "obj" || f.Type == "scene" || f.Type == "key" || f.Type == "sound")
            {
                var cb = new ComboBox { Height = 28, MinWidth = 120 };
                var items = SuggestionsFor(f.Type).ToList();
                if (!string.IsNullOrEmpty(cur) && !items.Contains(cur)) items.Insert(0, cur); // keep a value that isn't in the list
                foreach (var o in items)
                    cb.Items.Add(o == "self" && f.Type == "obj" ? new ComboBoxItem { Content = L.T("self"), Tag = o }
                                 : L.Keep(new ComboBoxItem { Content = o, Tag = o })); // names, keys and files: the person's own
                cb.SelectedItem = cb.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == cur);
                if (cb.SelectedItem == null && cb.Items.Count > 0) { cb.SelectedIndex = 0; rule[f.Key] = (string)((ComboBoxItem)cb.Items[0]).Tag; }
                cb.SelectionChanged += (s, e) => rule[f.Key] = (cb.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
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
                bool isVar = VarScope.ColorHex(v) == null;
                swatch.Background = isVar ? Brushes.Transparent : Theme.Hex(v);
                hex.Text = isVar ? "—" : v;
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

        private IEnumerable<string> SuggestionsFor(string type)
        {
            if (type == "key") return CommonKeys;
            if (type == "scene") return _project.Scenes.Select(s => s.Name);
            if (type == "sound") return ParamSources.Sounds(_project); // the relative paths the runtime plays
            // obj: names of objects in the scene (+ "self" for the set action)
            var names = _scene.Layers.SelectMany(l => l.Objects).Select(o => o.Name).Distinct();
            return new[] { "self" }.Concat(names);
        }

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

        // what a choice option shows (the value is what's saved; symbols and property names show as they are)
        private static string OptionLabel(string o) => o switch
        {
            "" => L.T("(none)"),
            "top" => L.T("top"), "near" => L.T("near"),
            "touching" => L.T("touching"), "below" => L.T("below"), "above" => L.T("above"), "left" => L.T("left"), "right" => L.T("right"),
            "front" => L.T("front"), "atmouse" => L.T("at the mouse"),
            "day" => L.T("day"), "noon" => L.T("noon"), "dusk" => L.T("dusk"), "night" => L.T("night"), "midnight" => L.T("midnight"),
            "info" => L.T("info"), "warn" => L.T("warn"), "error" => L.T("error"),
            "is" => L.T("is"), "isnot" => L.T("is not"), "contains" => L.T("contains"), "empty" => L.T("is empty"), "notempty" => L.T("is not empty"),
            "notcontains" => L.T("doesn't contain"), "countis" => L.T("has exactly (items)"), "countatleast" => L.T("has at least (items)"),
            "master" => L.T("All sound"), "music" => L.T("Music"), "sfx" => L.T("Sound effects"),
            "linear" => L.T("linear"), "easein" => L.T("ease in"), "easeout" => L.T("ease out"), "easeinout" => L.T("ease in and out"),
            _ => o,
        };

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
                        if (f.Type == "bool") { if (IsTrue(raw)) into.Add($"\"{f.Key}\":true"); continue; }
                        if (string.IsNullOrWhiteSpace(raw) && f.Type != "num") continue;
                        if (f.Type is "num" or "val" && raw.Trim().ToLowerInvariant() is "true" or "false") raw = raw.Trim().ToLowerInvariant() == "true" ? "1" : "0"; // a Yes/No value
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

        private static bool IsTrue(string? v) => (v ?? "").Trim().ToLowerInvariant() is "true" or "1" or "yes";

        private static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    }
}
