using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BE = BuilderEngine.Script; // its Project/Scene clash with ours

namespace EasyEngine
{
    // Behavior packages in the editor (BehaviorPackages.cs): offered in "＋ Add behavior…" next to the built-ins (📦), shown
    // as a card like a built-in — every setting with the picker its type gets (a number with its limits and step, on/off,
    // a choice, an object, a scene, a key, a colour, a sound, a picture, an animation, a list, a path); the author's labels
    // and help (a tooltip) are never translated, "show if" hides a setting. Adding one copies it into the project.
    public partial class MainWindow
    {
        private static readonly HashSet<string> PackageProblemsShown = new(StringComparer.Ordinal);

        /// <summary>Package files that couldn't be read: said once, in the status bar (the F1 console says it when playing).</summary>
        private void ReportPackageProblems()
        {
            var set = BehaviorPackages.For(_project);
            var problems = set.Problems.Where(p => PackageProblemsShown.Add(set.ProjectFolder + "|" + p)).ToList();
            if (problems.Count > 0)
                Status(L.F("⚠ {0} behavior package(s) couldn't be read: {1}", problems.Count, string.Join(" · ", problems)));
        }

        // ---------------------------------------------------------------- ＋ Add behavior…
        private void AddPackageItems(ComboBox combo, GameObjectData obj, bool single)
        {
            ReportPackageProblems();
            var packages = BehaviorPackages.For(_project).All.Where(p => !single || BehaviorPackages.CanAdd(obj, p)).ToList();
            if (packages.Count == 0) return;
            combo.Items.Add(new ComboBoxItem { Content = L.T("📦 Behavior packages"), IsEnabled = false, Tag = "\u0001packages", FontWeight = FontWeights.SemiBold });
            foreach (var pkg in packages)
            {
                var it = L.Keep(new ComboBoxItem
                {
                    Content = $"{pkg.Icon}  {pkg.Name}   📦",
                    Tag = pkg,
                    ToolTip = (pkg.Description.Length > 0 ? pkg.Description + "\n" : "") + L.F("📦 Behavior package · {0}", pkg.Category) + " · " + BehaviorPackages.OriginText(pkg),
                });
                TextSearch.SetText(it, $"{pkg.Name} {pkg.Category} package {string.Join(' ', pkg.AiKeywords)}");
                combo.Items.Add(it);
            }
            combo.SelectionChanged += (s, e) =>
            {
                if (combo.SelectedItem is ComboBoxItem { Tag: BE.BehaviorPackage pkg }) AddPackageTo(obj, pkg);
            };
        }

        private void AddPackageTo(GameObjectData obj, BE.BehaviorPackage pkg)
        {
            PushUndo();
            var targets = _selection.Count > 1 ? _selection.Where(o => !IsLockedObj(o) && !BuilderLink.IsBuilt(o)).ToList() : new List<GameObjectData> { obj };
            int added = 0;
            foreach (var t in targets)
            {
                if (!BehaviorPackages.CanAdd(t, pkg)) continue;
                t.Behaviors.Add(BehaviorPackages.CreateData(pkg));
                added++;
            }
            bool copied = false;
            try { copied = added > 0 && BehaviorPackages.EnsureInProject(_project, pkg); }
            catch (Exception ex) { Status(L.F("Couldn't copy the package into the project: {0}", ex.Message)); }
            BuildInspector(obj); RenderScene(); Dirty();
            if (targets.Count > 1) Status(L.F("Added {0} to {1} of {2} selected object(s)", pkg.Name, added, targets.Count));
            else if (copied) Status(L.F("📦 {0} added — a copy is now in the project's behaviors folder, so the game works on any computer", pkg.Name));
        }

        // ---------------------------------------------------------------- the card
        private void PackageRows(GameObjectData obj, BehaviorData bh, BE.BehaviorPackage? pkg, Panel panel)
        {
            if (pkg == null)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = L.F("This behavior package isn't on this computer. Put {0}.behavior in the project's behaviors folder — its settings are kept.", BehaviorPackages.NameOf(bh)),
                    TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = (SolidColorBrush)FindResource("TextDim"), Tag = "package-missing",
                });
                return;
            }
            var about = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            if (pkg.Description.Length > 0)
                about.Children.Add(L.Keep(new TextBlock { Text = pkg.Description, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = (SolidColorBrush)FindResource("TextDim") }));
            string meta = L.F("📦 Behavior package · {0}", pkg.Category) + " · " + BehaviorPackages.OriginText(pkg) + (pkg.Author.Length > 0 ? " · " + L.F("by {0}", pkg.Author) : "") + " · v" + pkg.PackageVersion;
            about.Children.Add(new TextBlock { Text = meta, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = (SolidColorBrush)FindResource("TextDim"), Margin = new Thickness(0, 2, 0, 0) });
            panel.Children.Add(about);

            var all = pkg.AllParams();
            bool anyShowIf = all.Any(p => p.ShowIf != null);
            void Changed() { Dirty(); if (anyShowIf) RefreshAfterChoiceAlways(obj); }
            foreach (var p in all)
            {
                if (!BehaviorPackages.Shows(pkg, p, bh)) continue;
                int before = panel.Children.Count;
                PackageParamRow(obj, bh, p, panel, Changed);
                for (int i = before; i < panel.Children.Count; i++)
                {
                    if (panel.Children[i] is not FrameworkElement row) continue;
                    row.Tag = "pkg:" + p.Name;
                    // the author's words: never translated; the help is the label's tooltip
                    if (row is Grid { Children.Count: > 0 } g && g.Children[0] is TextBlock label)
                    {
                        L.SetSkip(label, true);
                        if (p.Help.Length > 0) label.ToolTip = label.Text + "\n" + p.Help;
                    }
                    else if (row is StackPanel { Children.Count: > 0 } sp && sp.Children[0] is TextBlock head)
                    {
                        L.SetSkip(head, true);
                        if (p.Help.Length > 0) head.ToolTip = p.Help;
                    }
                }
            }
        }

        private void PackageParamRow(GameObjectData obj, BehaviorData bh, BE.BehaviorPackage.PackageParam p, Panel panel, Action changed)
        {
            string label = p.Label.Length > 0 ? p.Label : p.Name.Replace('_', ' ');
            string text = BehaviorPackages.Text(bh, p);
            void SetText(string v) { bh.Texts[p.Name] = v; changed(); }
            switch (p.Kind)
            {
                case BE.ParamKind.Number:
                    PackageNumberRow(label, BehaviorPackages.Number(bh, p), p.Min, p.Max, p.Step, BE.ParamTypes.Base(p.Type) == "seconds",
                                     v => { bh.Nums[p.Name] = v; changed(); }, panel);
                    break;
                case BE.ParamKind.Bool:
                    CheckRow(label, BehaviorPackages.Number(bh, p) >= 0.5, v => { bh.Nums[p.Name] = v ? 1 : 0; changed(); }, panel);
                    break;
                case BE.ParamKind.Choice:
                    PickRow(label, panel, p.Options.Select(o => (o, o)).ToList(), text, false, null, SetText, names: true);
                    break;
                case BE.ParamKind.Object:
                    PickRow(label, panel, ParamSources.Objects(_scene).Select(n => (n, n)).ToList(), text, true, null, SetText, names: true);
                    break;
                case BE.ParamKind.Scene:
                    PickRow(label, panel, _project.Scenes.Select(s => (s.Name, s.Name)).ToList(), text, true, null, SetText, names: true);
                    break;
                case BE.ParamKind.Animation:
                    PickRow(label, panel, obj.Animations.Select(a => (a.Name, a.Name)).ToList(), text, true, null, SetText, names: true);
                    break;
                case BE.ParamKind.Key:
                    PackageKeyRow(label, BehaviorPackages.EeKey(text), SetText, panel);
                    break;
                case BE.ParamKind.Color:
                    ColorRow(label, text.Length > 0 ? text : "#FFFFFF", SetText, panel);
                    break;
                case BE.ParamKind.Sound:
                    SoundRow(obj, bh, panel, p.Name, label);
                    break;
                case BE.ParamKind.Image:
                {
                    var g = RowGrid(label, panel);
                    var cb = PicturePick.Make(_project, text, v => { PushUndo(); SetText(v); }, this);
                    cb.Height = 30; cb.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(cb, 1); g.Children.Add(cb);
                    break;
                }
                case BE.ParamKind.List:
                    PackageListEditor(obj, label, BE.ParamTypes.ItemType(p.Type), text, SetText, panel);
                    break;
                case BE.ParamKind.Path:
                    PackagePathEditor(obj, label, text, SetText, panel);
                    break;
                default: // text, and a type this version doesn't know: the person types it
                    TextRow(label, text, SetText, panel);
                    break;
            }
        }

        private static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        /// <summary>A number with − / + (by its step), kept between its min and max; the mouse wheel steps it the same way.</summary>
        private void PackageNumberRow(string label, double value, double? min, double? max, double? step, bool seconds, Action<double> set, Panel panel)
        {
            var grid = RowGrid(label, panel);
            var inner = new Grid { Tag = "spinner" };
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tb = new TextBox { Text = Num(value), Height = 30, ToolTip = L.T("Click it, then roll the mouse wheel to change it and see it live (Shift: faster · Ctrl: finer)") };
            NumberWheel.SetRange(tb, min, max, step);
            double Clamp(double v) { if (min is double a) v = Math.Max(a, v); if (max is double b) v = Math.Min(b, v); return v; }
            double Current() => double.TryParse(tb.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : value;
            void Commit(double v)
            {
                v = Clamp(v);
                tb.Text = Num(v);
                if (v == value) return;
                value = v; PushUndo(); set(v);
            }
            tb.LostFocus += (s, e) => Commit(Current());
            tb.KeyDown += (s, e) => { if (e.Key == Key.Enter) Commit(Current()); };
            double by = step is double st && st > 0 ? st : 1;
            Button Nudge(string sign, double dir, string tip)
            {
                var b = new Button { Content = sign, Width = 30, Height = 30, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0), Style = (Style)FindResource("CardButton"), HorizontalContentAlignment = HorizontalAlignment.Center, ToolTip = tip };
                b.Click += (s, e) => Commit(Math.Round(Current() + dir * by, 6));
                return b;
            }
            var down = Nudge("−", -1, L.T("Less"));
            var up = Nudge("＋", 1, L.T("More"));
            Grid.SetColumn(tb, 0); Grid.SetColumn(down, 1); Grid.SetColumn(up, 2);
            inner.Children.Add(tb); inner.Children.Add(down); inner.Children.Add(up);
            if (seconds)
            {
                var unit = new TextBlock { Text = L.T("seconds"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Foreground = (SolidColorBrush)FindResource("TextDim") };
                Grid.SetColumn(unit, 3); inner.Children.Add(unit);
            }
            if (min != null || max != null)
                inner.ToolTip = L.F("From {0} to {1}", min is double a ? Num(a) : "…", max is double b ? Num(b) : "…");
            Grid.SetColumn(inner, 1); grid.Children.Add(inner);
        }

        /// <summary>A key: picked from the list, or "⌨" then press it.</summary>
        private void PackageKeyRow(string label, string current, Action<string> set, Panel panel)
        {
            var g = RowGrid(label, panel);
            var inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var cb = Dropdown.Make(ParamSources.KeyOptions(), current, v => { PushUndo(); set(v); }, null, this);
            cb.Height = 30;
            var press = new Button { Content = "⌨", Width = 30, Height = 30, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0), Style = (Style)FindResource("CardButton"), HorizontalContentAlignment = HorizontalAlignment.Center,
                                     ToolTip = L.T("Click, then press the key") };
            bool waiting = false;
            press.Click += (s, e) => { waiting = true; press.Content = "…"; press.Focus(); };
            press.LostFocus += (s, e) => { waiting = false; press.Content = "⌨"; };
            press.PreviewKeyDown += (s, e) =>
            {
                if (!waiting) return;
                e.Handled = true;
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                waiting = false; press.Content = "⌨";
                if (key == Key.Escape) return;
                PushUndo(); set(BehaviorPackages.EeKey(key.ToString()));
                if (_selected != null) RefreshAfterChoiceAlways(_selected);
            };
            Grid.SetColumn(cb, 0); Grid.SetColumn(press, 1);
            inner.Children.Add(cb); inner.Children.Add(press);
            Grid.SetColumn(inner, 1); g.Children.Add(inner);
        }

        // ---------------------------------------------------------------- lists and paths
        private static List<JsonNode?> ParseList(string json)
        {
            try { if (JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json) is JsonArray a) return a.Select(n => n?.DeepClone()).ToList(); }
            catch (System.Text.Json.JsonException) { }
            return new List<JsonNode?>();
        }

        private static string ListJson(IEnumerable<JsonNode?> items) => new JsonArray(items.Select(n => n?.DeepClone()).ToArray()).ToJsonString();

        private StackPanel PackageListBox(string label, Panel panel)
        {
            var box = new StackPanel { Margin = new Thickness(0, 2, 0, 6) };
            box.Children.Add(new TextBlock { Text = label, Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 12, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(box);
            return box;
        }

        private Grid PackageListLine(FrameworkElement editor, Action remove)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var del = new Button { Content = "✕", Width = 28, Height = 28, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0), Style = (Style)FindResource("CardButton"), Foreground = (SolidColorBrush)FindResource("Danger"), ToolTip = L.T("Remove"), HorizontalContentAlignment = HorizontalAlignment.Center };
            del.Click += (s, e) => remove();
            Grid.SetColumn(editor, 0); Grid.SetColumn(del, 1);
            row.Children.Add(editor); row.Children.Add(del);
            return row;
        }

        private Button AddButton(string text, Action add)
        {
            var b = new Button { Content = text, Height = 28, Style = (Style)FindResource("CardButton"), HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
            b.Click += (s, e) => add();
            return b;
        }

        /// <summary>A "List of …" setting: one row per item, each with the picker its item type gets, ✕ to remove, ＋ to add.</summary>
        private void PackageListEditor(GameObjectData obj, string label, string itemType, string json, Action<string> set, Panel panel)
        {
            var box = PackageListBox(label, panel);
            ((FrameworkElement)box.Children[0]).Tag = "list";
            var items = ParseList(json);
            var kind = BE.ParamTypes.Kind(itemType);
            void Save() { PushUndo(); set(ListJson(items)); RefreshAfterChoiceAlways(obj); }
            for (int idx = 0; idx < items.Count; idx++)
            {
                int i = idx;
                box.Children.Add(PackageListLine(ListItemEditor(obj, kind, itemType, items[i], v => { items[i] = v; PushUndo(); set(ListJson(items)); }), () => { items.RemoveAt(i); Save(); }));
            }
            if (items.Count == 0) box.Children.Add(new TextBlock { Text = L.T("Empty — add one below."), FontSize = 11, Foreground = (SolidColorBrush)FindResource("TextDim"), Margin = new Thickness(0, 0, 0, 4) });
            box.Children.Add(AddButton(L.T("＋ Add"), () =>
            {
                JsonNode? item = kind switch
                {
                    BE.ParamKind.Number => JsonValue.Create(0.0),
                    BE.ParamKind.Bool => JsonValue.Create(false),
                    BE.ParamKind.Choice => JsonValue.Create(BE.BehaviorPackage.ChoiceOptions(itemType).FirstOrDefault() ?? ""),
                    BE.ParamKind.Color => JsonValue.Create("#FFFFFF"),
                    _ => JsonValue.Create(""),
                };
                items.Add(item); Save();
            }));
        }

        private FrameworkElement ListItemEditor(GameObjectData obj, BE.ParamKind kind, string itemType, JsonNode? value, Action<JsonNode?> set)
        {
            string text = value is JsonValue v && v.TryGetValue<string>(out var s) ? s : value?.ToJsonString() ?? "";
            ComboBox Pick(List<(string, string)> options, bool keep = true)
            {
                var cb = Dropdown.Make(options, text, x => set(JsonValue.Create(x)), null, this);
                cb.Height = 28;
                if (keep) L.SetSkip(cb, true);
                return cb;
            }
            switch (kind)
            {
                case BE.ParamKind.Number:
                {
                    var tb = new TextBox { Text = value is JsonValue nv && nv.TryGetValue<double>(out var d) ? Num(d) : "0", Height = 28 };
                    tb.LostFocus += (s2, e) => { if (double.TryParse(tb.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var x)) set(JsonValue.Create(x)); };
                    return tb;
                }
                case BE.ParamKind.Bool:
                {
                    var chk = new CheckBox { IsChecked = value is JsonValue bv && bv.TryGetValue<bool>(out var b) && b, VerticalAlignment = VerticalAlignment.Center, Foreground = (SolidColorBrush)FindResource("Text") };
                    chk.Checked += (s2, e) => set(JsonValue.Create(true));
                    chk.Unchecked += (s2, e) => set(JsonValue.Create(false));
                    return chk;
                }
                case BE.ParamKind.Choice: return Pick(BE.BehaviorPackage.ChoiceOptions(itemType).Select(o => (o, o)).ToList());
                case BE.ParamKind.Object: return Pick(ParamSources.Objects(_scene).Select(n => (n, n)).ToList());
                case BE.ParamKind.Scene: return Pick(_project.Scenes.Select(sc => (sc.Name, sc.Name)).ToList());
                case BE.ParamKind.Animation: return Pick(obj.Animations.Select(a => (a.Name, a.Name)).ToList());
                case BE.ParamKind.Key: text = BehaviorPackages.EeKey(text); return Pick(ParamSources.KeyOptions(), keep: false);
                case BE.ParamKind.Sound: { var cb = SoundPick.Make(_project, text, x => set(JsonValue.Create(x)), this); cb.Height = 28; return cb; }
                case BE.ParamKind.Image: { var cb = PicturePick.Make(_project, text, x => set(JsonValue.Create(x)), this); cb.Height = 28; return cb; }
                case BE.ParamKind.Color:
                {
                    var holder = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
                    holder.Children.Add(SwatchButton(text, "#FFFFFF", 4, L.T("Click to pick a color"), hex => set(JsonValue.Create(hex))));
                    return holder;
                }
                default:
                {
                    var tb = L.Keep(new TextBox { Text = text, Height = 28 });
                    tb.LostFocus += (s2, e) => { if (tb.Text != text) { text = tb.Text; set(JsonValue.Create(tb.Text)); } };
                    return tb;
                }
            }
        }

        /// <summary>A "Path" setting: its points (scene pixels), each with X and Y; ＋ adds one a step past the last (or at the object).</summary>
        private void PackagePathEditor(GameObjectData obj, string label, string json, Action<string> set, Panel panel)
        {
            var box = PackageListBox(label, panel);
            ((FrameworkElement)box.Children[0]).Tag = "path";
            var points = ParseList(json).Select(n => n is JsonArray { Count: >= 2 } a && a[0] is JsonValue x && a[1] is JsonValue y
                                                   && x.TryGetValue<double>(out var px) && y.TryGetValue<double>(out var py) ? (px, py) : (double.NaN, double.NaN))
                                        .Where(p => !double.IsNaN(p.Item1)).ToList();
            string Json() => new JsonArray(points.Select(p => (JsonNode?)new JsonArray(JsonValue.Create(p.Item1), JsonValue.Create(p.Item2))).ToArray()).ToJsonString();
            void Save() { PushUndo(); set(Json()); RefreshAfterChoiceAlways(obj); }
            for (int idx = 0; idx < points.Count; idx++)
            {
                int i = idx;
                var line = new Grid();
                foreach (var w in new[] { 20.0, -1, 24, -1 }) line.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
                TextBox Coord(double v, Action<double> to)
                {
                    var tb = new TextBox { Text = Num(v), Height = 28 };
                    tb.LostFocus += (s, e) => { if (double.TryParse(tb.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var n) && n != v) { v = n; to(n); PushUndo(); set(Json()); } };
                    return tb;
                }
                var lx = new TextBlock { Text = "X", VerticalAlignment = VerticalAlignment.Center, Foreground = (SolidColorBrush)FindResource("TextDim") };
                var ly = new TextBlock { Text = "Y", VerticalAlignment = VerticalAlignment.Center, Foreground = (SolidColorBrush)FindResource("TextDim"), Margin = new Thickness(8, 0, 0, 0) };
                var bx = Coord(points[i].Item1, n => points[i] = (n, points[i].Item2));
                var by = Coord(points[i].Item2, n => points[i] = (points[i].Item1, n));
                Grid.SetColumn(lx, 0); Grid.SetColumn(bx, 1); Grid.SetColumn(ly, 2); Grid.SetColumn(by, 3);
                line.Children.Add(lx); line.Children.Add(bx); line.Children.Add(ly); line.Children.Add(by);
                box.Children.Add(PackageListLine(line, () => { points.RemoveAt(i); Save(); }));
            }
            if (points.Count == 0) box.Children.Add(new TextBlock { Text = L.T("No points yet — add one below."), FontSize = 11, Foreground = (SolidColorBrush)FindResource("TextDim"), Margin = new Thickness(0, 0, 0, 4) });
            box.Children.Add(AddButton(L.T("＋ Add a point"), () =>
            {
                var last = points.Count > 0 ? points[^1] : (obj.X, obj.Y);
                points.Add(points.Count > 0 ? (last.Item1 + 100, last.Item2) : (Math.Round(obj.X), Math.Round(obj.Y)));
                Save();
            }));
        }

        // ---------------------------------------------------------------- test hooks
        /// <summary>The package card's settings of one behavior: setting name → the controls drawn for it (ComboBox, CheckBox,
        /// TextBox, Button, spinner, list, path…); hidden settings are missing.</summary>
        internal Dictionary<string, List<string>> TestPackageRows(GameObjectData obj, BehaviorData bh)
        {
            _selected = obj;
            var panel = new StackPanel();
            PackageRows(obj, bh, BehaviorPackages.Of(_project, bh), panel);
            var rows = new Dictionary<string, List<string>>();
            foreach (var child in panel.Children.OfType<FrameworkElement>())
            {
                if (child.Tag is not string tag || !tag.StartsWith("pkg:")) continue;
                var kinds = new List<string>();
                void Walk(object node)
                {
                    if (node is FrameworkElement fe && fe.Tag is "spinner" or "list" or "path") kinds.Add((string)fe.Tag);
                    if (node is ComboBox) kinds.Add("ComboBox");
                    else if (node is CheckBox) kinds.Add("CheckBox");
                    else if (node is TextBox) kinds.Add("TextBox");
                    else if (node is Button b) kinds.Add(b.Content is StackPanel ? "ColorButton" : "Button");
                    if (node is DependencyObject d && node is not ComboBox) foreach (var c in LogicalTreeHelper.GetChildren(d)) Walk(c);
                }
                Walk(child);
                rows[tag[4..]] = kinds;
            }
            return rows;
        }

        /// <summary>The packages "＋ Add behavior…" offers for the inspected object.</summary>
        internal List<string> TestAddablePackages() =>
            AddBehaviorCombo()?.Items.OfType<ComboBoxItem>().Select(i => i.Tag).OfType<BE.BehaviorPackage>().Select(p => p.Name).ToList() ?? new List<string>();

        /// <summary>Picks a package in "＋ Add behavior…" as a click would; false when it isn't offered.</summary>
        internal bool TestAddPackage(string name)
        {
            var combo = AddBehaviorCombo();
            var item = combo?.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is BE.BehaviorPackage p && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (combo == null || item == null) return false;
            combo.SelectedItem = item;
            return true;
        }

        /// <summary>The game files of an export: the project folder (CopyDir), plus every behavior package it uses that only
        /// lives in the installed folder — so the exported game has all of them in its behaviors/.</summary>
        internal static void CopyGameFiles(Project p, string src, string dst)
        {
            CopyDir(src, dst);
            BehaviorPackages.CopyUsedInto(p, dst);
        }

        internal string TestStatus => StatusText.Text;
    }
}
