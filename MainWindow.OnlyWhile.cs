using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace EasyEngine
{
    /// <summary>
    /// "Only while" (texts.cond) as picked choices: a key is held, a variable is…, the player is near, the player has an item,
    /// night/day, on the ground, the mouse button is held — or a formula. It is still ONE formula string (the runtime, the AI
    /// and old projects read the same thing); this only writes the shapes below and reads them back. Anything else is
    /// "Formula (advanced)", kept exactly as it is. Two choices may be joined with &amp;&amp; ("and also…").
    /// </summary>
    internal static class OnlyWhile
    {
        public enum Kind { Always, Key, Variable, Near, Item, Night, Day, Ground, Mouse, Formula }

        /// <summary>One choice. A = key / variable / who / item (or the formula's text); Op = comparison; B = value / distance.</summary>
        public record struct Cond(Kind Kind, string A = "", string Op = ">=", string B = "");

        public static readonly double[] NearDistances = { 100, 200, 300, 500 };

        public static string Format(Cond c) => c.Kind switch
        {
            Kind.Key => $"key:{c.A} >= 1",
            Kind.Variable => $"{c.A} {c.Op} {c.B}",
            Kind.Near => $"dist(self.cx,self.cy,{c.A}.cx,{c.A}.cy) < {c.B}",
            Kind.Item => $"item:{c.A} >= 1",
            Kind.Night => "isNight >= 1",
            Kind.Day => "isNight < 1",
            Kind.Ground => "self.grounded >= 1",
            Kind.Mouse => "mousedown >= 1",
            Kind.Formula => c.A.Trim(),
            _ => "",
        };

        /// <summary>The whole formula: the choices joined with &amp;&amp; ("" = always).</summary>
        public static string Format(IReadOnlyList<Cond> parts) =>
            string.Join(" && ", parts.Where(p => p.Kind != Kind.Always).Select(Format).Where(f => f.Length > 0));

        /// <summary>A saved formula as choices: empty = none (always); one or two known shapes; else one Formula with the text as is.</summary>
        public static List<Cond> Parse(string? formula)
        {
            var f = (formula ?? "").Trim();
            if (f.Length == 0) return new List<Cond>();
            var one = ParseOne(f);
            if (one.Kind != Kind.Formula) return new List<Cond> { one };
            var halves = f.Split("&&");
            if (halves.Length == 2)
            {
                var a = ParseOne(halves[0].Trim()); var b = ParseOne(halves[1].Trim());
                if (a.Kind is not (Kind.Formula or Kind.Always) && b.Kind is not (Kind.Formula or Kind.Always)) return new List<Cond> { a, b };
            }
            return new List<Cond> { new(Kind.Formula, f) };
        }

        private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        private static readonly Regex KeyRx = new(@"^key:(\w+)(\s*(>=\s*1|==\s*1|>\s*0))?$", Opt);
        private static readonly Regex ItemRx = new(@"^item:(\w+)\s*(>=\s*1|>\s*0)$", Opt);
        private static readonly Regex NearRx = new(@"^dist\(\s*self\.cx\s*,\s*self\.cy\s*,\s*(\w+)\.cx\s*,\s*(\w+)\.cy\s*\)\s*<\s*(\d+(?:\.\d+)?)$", Opt);
        private static readonly Regex NightRx = new(@"^isNight\s*(>=\s*1|>=\s*0\.5|==\s*1|>\s*0)$", Opt);
        private static readonly Regex DayRx = new(@"^isNight\s*(<\s*1|<\s*0\.5|==\s*0)$", Opt);
        private static readonly Regex GroundRx = new(@"^self\.grounded(\s*(>=\s*1|==\s*1))?$", Opt);
        private static readonly Regex MouseRx = new(@"^mousedown(\s*(>=\s*1|==\s*1))?$", Opt);
        private static readonly Regex VarRx = new(@"^((?:local\.)?[^\W\d]\w*)\s*(>=|<=|==|!=|>|<)\s*(-?\d+(?:\.\d+)?)$", Opt);

        private static Cond ParseOne(string f)
        {
            Match m;
            if ((m = KeyRx.Match(f)).Success && PlayWindow.KeyByName(m.Groups[1].Value) != null) return new(Kind.Key, m.Groups[1].Value);
            if ((m = ItemRx.Match(f)).Success) return new(Kind.Item, m.Groups[1].Value);
            if ((m = NearRx.Match(f)).Success && m.Groups[1].Value.Equals(m.Groups[2].Value, StringComparison.OrdinalIgnoreCase)
                && !m.Groups[1].Value.Equals("self", StringComparison.OrdinalIgnoreCase))
                return new(Kind.Near, m.Groups[1].Value, "<", m.Groups[3].Value);
            if (NightRx.IsMatch(f)) return new(Kind.Night);
            if (DayRx.IsMatch(f)) return new(Kind.Day);
            if (GroundRx.IsMatch(f)) return new(Kind.Ground);
            if (MouseRx.IsMatch(f)) return new(Kind.Mouse);
            if ((m = VarRx.Match(f)).Success) return new(Kind.Variable, m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
            return new(Kind.Formula, f);
        }

        /// <summary>A name a formula can read as one word (letters, digits, _): "Big Door" can't be written in one.</summary>
        public static bool IsName(string? s) =>
            !string.IsNullOrEmpty(s) && (char.IsLetter(s[0]) || s[0] == '_') && s.All(ch => char.IsLetterOrDigit(ch) || ch == '_');

        public static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }

    public partial class MainWindow
    {
        private const string OnlyWhileTag = "only-while";

        // the choices of the "Only while…" dropdown (values = the Kind names; labels translated where they're shown)
        private static List<(string value, string label)> OnlyWhileChoices(bool first, bool ground)
        {
            var list = new List<(string, string)>();
            list.Add(first ? (nameof(OnlyWhile.Kind.Always), L.T("Always")) : ("", L.T("(nothing else)")));
            list.Add((nameof(OnlyWhile.Kind.Key), L.T("a key is held")));
            list.Add((nameof(OnlyWhile.Kind.Variable), L.T("a variable is…")));
            list.Add((nameof(OnlyWhile.Kind.Near), L.T("the player (or someone) is near")));
            list.Add((nameof(OnlyWhile.Kind.Item), L.T("the player has an item")));
            list.Add((nameof(OnlyWhile.Kind.Night), L.T("it is night")));
            list.Add((nameof(OnlyWhile.Kind.Day), L.T("it is day")));
            if (ground) list.Add((nameof(OnlyWhile.Kind.Ground), L.T("it is on the ground")));
            list.Add((nameof(OnlyWhile.Kind.Mouse), L.T("the mouse button is held")));
            if (first) list.Add((nameof(OnlyWhile.Kind.Formula), L.T("Formula (advanced)")));
            return list;
        }

        /// <summary>The "Only while" rows of a behavior card (every kind in <see cref="PlayWindow.OnlyWhileKinds"/>).</summary>
        private void OnlyWhileRow(GameObjectData obj, BehaviorData bh, Panel panel)
        {
            var host = new StackPanel { Tag = OnlyWhileTag };
            panel.Children.Add(host);
            var parts = OnlyWhile.Parse(bh.Text("cond", ""));
            bool ground = obj.Physics.Mode == PhysicsMode.Dynamic || obj.Behaviors.Any(b => b.Kind is BehaviorKind.Platformer or BehaviorKind.AdvancedPatrol)
                          || parts.Any(p => p.Kind == OnlyWhile.Kind.Ground);

            void Save()
            {
                var f = OnlyWhile.Format(parts);
                if (f.Length == 0) bh.Texts.Remove("cond"); else bh.Texts["cond"] = f;
                Dirty();
            }
            // after the click that changed the choice finishes (the dropdown being clicked is one of the rows rebuilt)
            void Rerender() => Dispatcher.BeginInvoke(new Action(Render), DispatcherPriority.Background);
            // a choice row (its values are the Kind names; "" = the second row's "nothing else")
            void ChoiceDropdown(string label, List<(string value, string label)> options, string current, Action<string> set)
                => PickRow(label, host, options, current, false, null, set);

            void Render()
            {
                host.Children.Clear();
                var first = parts.Count > 0 ? parts[0] : new OnlyWhile.Cond(OnlyWhile.Kind.Always);
                ChoiceDropdown(L.T("Only while"), OnlyWhileChoices(true, ground), first.Kind.ToString(), v =>
                {
                    var kind = Enum.Parse<OnlyWhile.Kind>(v);
                    if (kind == OnlyWhile.Kind.Always) parts.Clear();
                    else if (kind == OnlyWhile.Kind.Formula) { var text = OnlyWhile.Format(parts); parts.Clear(); parts.Add(new(OnlyWhile.Kind.Formula, text)); }
                    else if (parts.Count == 0 || parts[0].Kind == OnlyWhile.Kind.Formula) { parts.Clear(); parts.Add(DefaultCond(kind, obj)); }
                    else parts[0] = DefaultCond(kind, obj);
                    Save(); Rerender();
                });
                if (parts.Count == 0) return;
                DetailRows(0);
                if (parts[0].Kind == OnlyWhile.Kind.Formula) return;

                // "and also…": a second choice joined with &&
                var second = parts.Count > 1 ? parts[1].Kind.ToString() : "";
                ChoiceDropdown(L.T("And also"), OnlyWhileChoices(false, ground), second, v =>
                {
                    if (parts.Count > 1) parts.RemoveAt(1);
                    if (v.Length > 0) parts.Add(DefaultCond(Enum.Parse<OnlyWhile.Kind>(v), obj));
                    Save(); Rerender();
                });
                if (parts.Count > 1) DetailRows(1);
            }

            // what the chosen kind needs: a key, a variable + comparison + value, who and how near, an item, or the formula
            void DetailRows(int i)
            {
                var c = parts[i];
                void Set(OnlyWhile.Cond n) { parts[i] = n; Save(); }
                switch (c.Kind)
                {
                    case OnlyWhile.Kind.Key:
                        PickRow(L.T("Key"), host, ParamSources.KeyOptions(), c.A, false, null, v => Set(c = c with { A = v }));
                        break;
                    case OnlyWhile.Kind.Item:
                        PickRow(L.T("Item"), host, ParamSources.Items(_project).Where(OnlyWhile.IsName).Select(n => (n, ToolArt.Title(n))).ToList(), c.A, false,
                                L.T("New item name (e.g. wood, key, shovel):"), v => Set(c = c with { A = OneWord(v) }), names: true);
                        break;
                    case OnlyWhile.Kind.Near:
                        PickRow(L.T("Who"), host, ParamSources.Objects(_scene).Where(OnlyWhile.IsName).Select(n => (n, n)).ToList(), c.A, false, null,
                                v => Set(c = c with { A = v }), names: true);
                        PickRow(L.T("Closer than"), host, OnlyWhile.NearDistances.Select(d => (OnlyWhile.Num(d), L.F("{0} px", OnlyWhile.Num(d)))).ToList(), c.B, false, null,
                                v => Set(c = c with { B = v }));
                        break;
                    case OnlyWhile.Kind.Variable:
                    {
                        PickRow(L.T("Variable"), host, OnlyWhileVariables(obj), c.A, false, L.T("New variable name:"), v => Set(c = c with { A = OneWord(v) }), names: true);
                        var g = RowGrid(L.T("Its value"), host);
                        var inner = new Grid();
                        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
                        var ops = BlockEditorWindow.CondOps;
                        var op = Dropdown.Make(ops.Select(o => (o.sym, o.label)), c.Op, v => { PushUndo(); Set(c = c with { Op = v }); });
                        op.Height = 30; op.Margin = new Thickness(0, 0, 6, 0);
                        var val = new TextBox { Text = c.B, Height = 30, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = L.T("A number") };
                        void Commit()
                        {
                            if (!double.TryParse(val.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) { val.Text = c.B; return; }
                            var s = OnlyWhile.Num(n);
                            if (s == c.B) return;
                            PushUndo(); Set(c = c with { B = s }); val.Text = s;
                        }
                        val.LostFocus += (s, e) => Commit();
                        val.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) Commit(); };
                        Grid.SetColumn(op, 0); Grid.SetColumn(val, 1);
                        inner.Children.Add(op); inner.Children.Add(val);
                        Grid.SetColumn(inner, 1); g.Children.Add(inner);
                        break;
                    }
                    case OnlyWhile.Kind.Formula:
                        TextRow(L.T("Formula"), c.A, v =>
                        {
                            if (string.IsNullOrWhiteSpace(v)) parts.Clear(); else parts[0] = c with { A = v.Trim() };
                            Save();
                        }, host);
                        host.Children.Add(new TextBlock { Text = L.T("e.g.  Player.cx < self.cx   or   coins >= 10 && key:E >= 1"), TextWrapping = TextWrapping.Wrap, FontSize = 10.5,
                                                          Foreground = Theme.Brush("TextDim", "#8a8a93"), Margin = new Thickness(0, -2, 0, 6) });
                        break;
                }
            }

            Render();
        }

        // a new choice starts with something that works: key E, the first variable >= 1, the Player within 200 px, an item
        private OnlyWhile.Cond DefaultCond(OnlyWhile.Kind kind, GameObjectData obj) => kind switch
        {
            OnlyWhile.Kind.Key => new(kind, "E"),
            OnlyWhile.Kind.Variable => new(kind, OnlyWhileVariables(obj).Select(v => v.value).FirstOrDefault(v => !ParamSources.EngineVariables.Contains(v)) ?? "score", ">=", "1"),
            OnlyWhile.Kind.Near => new(kind, ParamSources.Objects(_scene).Where(OnlyWhile.IsName).FirstOrDefault(n => n.Equals("Player", StringComparison.OrdinalIgnoreCase))
                                             ?? ParamSources.Objects(_scene).Where(OnlyWhile.IsName).FirstOrDefault(n => n != obj.Name) ?? "Player", "<", "200"),
            OnlyWhile.Kind.Item => new(kind, ParamSources.Items(_project).FirstOrDefault(OnlyWhile.IsName) ?? "key"),
            _ => new(kind),
        };

        // the variables a condition can read: the project's and scene's (by name) and this object's own ones (local.name)
        private List<(string value, string label)> OnlyWhileVariables(GameObjectData obj)
        {
            var own = obj.Variables.Keys.Where(OnlyWhile.IsName).ToList();
            var list = ParamSources.Variables(_project).Where(v => OnlyWhile.IsName(v) && !own.Contains(v, StringComparer.OrdinalIgnoreCase)).Select(v => (v, v)).ToList();
            list.AddRange(own.Select(v => ("local." + v, L.F("{0} (this object's)", v))));
            return list;
        }

        /// <summary>Test hook: the inspector for <paramref name="obj"/>; for each "Only while" builder (one per card that has
        /// it), its dropdowns and text boxes in order.</summary>
        public List<(List<ComboBox> combos, List<TextBox> boxes)> TestOnlyWhile(GameObjectData obj)
        {
            TestInspect(obj);
            var found = new List<(List<ComboBox>, List<TextBox>)>();
            void Walk(object node)
            {
                if (node is StackPanel sp && sp.Tag as string == OnlyWhileTag)
                {
                    var combos = new List<ComboBox>(); var boxes = new List<TextBox>();
                    void Inner(object n)
                    {
                        if (n is ComboBox cb) combos.Add(cb);
                        if (n is TextBox tb) boxes.Add(tb);
                        if (n is DependencyObject d) foreach (var c in LogicalTreeHelper.GetChildren(d)) Inner(c);
                    }
                    Inner(sp);
                    found.Add((combos, boxes));
                    return;
                }
                if (node is DependencyObject dd) foreach (var c in LogicalTreeHelper.GetChildren(dd)) Walk(c);
            }
            Walk(InspectorRoot);
            return found;
        }

        private static string OneWord(string v) => Regex.Replace(v.Trim(), @"[^\w]", "_");
    }
}
