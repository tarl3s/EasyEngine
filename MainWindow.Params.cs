using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace EasyEngine
{
    // Behavior parameters in the inspector: everything is PICKED (dropdowns, pickers, checklists, the item editor);
    // typing is only for free text. The values are still the same strings the game and the AI use.
    public partial class MainWindow
    {
        private const string NewTag = "\u0001new";

        /// <summary>Test hook: builds the inspector for an object; returns the dropdowns, item-design pickers and the text boxes
        /// (and what they contain) found in its behavior cards.</summary>
        public (int combos, int lookPickers, List<string> textBoxes) TestInspect(GameObjectData obj)
        {
            _selected = obj;
            BuildInspector(obj);
            InspectorRoot.UpdateLayout();
            int combos = 0, looks = 0; var texts = new List<string>();
            bool inCards = false;
            void Walk(object node)
            {
                if (node is ComboBox cb) { combos++; if (cb.ToolTip as string == "How the item looks (in the hand and on the hotbar)") looks++; }
                if (node is TextBox tb) texts.Add(tb.Text);
                if (node is DependencyObject d) foreach (var c in LogicalTreeHelper.GetChildren(d)) Walk(c);
            }
            foreach (var child in InspectorRoot.Children)
            {
                if (child is TextBlock t && t.Text == "BEHAVIORS") inCards = true;
                if (inCards) Walk(child);
            }
            return (combos, looks, texts);
        }

        private static bool HasShowIf(BehaviorInfo info) => info.Texts.Any(t => t.ShowIf != null) || info.Nums.Any(n => n.ShowIf != null);

        // Rebuild the card after a choice that shows/hides other fields — after the click finishes, keeping the scroll.
        private void RefreshAfterChoice(GameObjectData obj, BehaviorInfo info)
        {
            if (!HasShowIf(info)) return;
            double off = InspectorScroll.VerticalOffset;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_selected != obj) return;
                BuildInspector(obj);
                Dispatcher.BeginInvoke(new Action(() => InspectorScroll.ScrollToVerticalOffset(off)), DispatcherPriority.Loaded);
            }), DispatcherPriority.Background);
        }

        // ---------------------------------------------------------------- numbers
        private void NumParamRow(GameObjectData obj, BehaviorData bh, BehaviorInfo info, NumParam np, Panel panel)
        {
            if (np.ShowIf != null && !np.ShowIf(new ParamCtx(bh, obj, _scene))) return;
            double cur = bh.Num(np.Key, np.Default);

            if (bh.Kind == BehaviorKind.Hotbar && np.Key == "slot") // "Starts with: Pickaxe"
            {
                var items = PlayWindow.ParseHotbarItems(bh.Text("items", ""));
                if (items.Count > 0)
                {
                    var opts = items.Select((it, i) => (i.ToString(CultureInfo.InvariantCulture), ToolArt.Title(it.name))).ToList();
                    PickRow("Starts with", panel, opts, Math.Clamp((int)Math.Round(cur), 0, items.Count - 1).ToString(CultureInfo.InvariantCulture), false, null,
                        v => { bh.Nums["slot"] = double.Parse(v, CultureInfo.InvariantCulture); Dirty(); });
                    return;
                }
            }
            if (np.Options != null)
            {
                var opts = np.Options.Select(ParamSources.Option).ToList();
                string curS = opts.Select(o => o.value).FirstOrDefault(v => Math.Abs(double.Parse(v, CultureInfo.InvariantCulture) - cur) < 1e-6)
                              ?? cur.ToString(CultureInfo.InvariantCulture);
                PickRow(np.Label, panel, opts, curS, false, null, v => { bh.Nums[np.Key] = double.Parse(v, CultureInfo.InvariantCulture); Dirty(); RefreshAfterChoice(obj, info); });
                return;
            }
            if (np.Label.Contains("(0/1)"))
            {
                CheckRow(np.Label.Replace("(0/1)", "").Trim(), cur >= 0.5, v => { bh.Nums[np.Key] = v ? 1 : 0; Dirty(); RefreshAfterChoice(obj, info); }, panel);
                return;
            }
            if (np.Label.Contains("(0-1)"))
            {
                SliderRow(np.Label.Replace("(0-1)", "").Trim(), cur, 0, 1, v => { bh.Nums[np.Key] = Math.Round(v, 2); Dirty(); }, panel, percent: true);
                return;
            }
            NumRow(np.Label, cur, v => { bh.Nums[np.Key] = v; Dirty(); }, panel);
        }

        // ---------------------------------------------------------------- texts
        private void ParamRow(GameObjectData obj, BehaviorData bh, BehaviorInfo info, TextParam tp, Panel panel)
        {
            if (tp.ShowIf != null && !tp.ShowIf(new ParamCtx(bh, obj, _scene))) return;
            string cur = bh.Text(tp.Key, tp.Default);
            void Set(string v) { bh.Texts[tp.Key] = v; Dirty(); }
            void SetAndRefresh(string v) { Set(v); RefreshAfterChoice(obj, info); }

            switch (tp.Pick)
            {
                case PickKind.Choice:
                    PickRow(tp.Label, panel, (tp.Options ?? Array.Empty<string>()).Select(ParamSources.Option).ToList(), cur, tp.AllowNone, null, SetAndRefresh);
                    break;
                case PickKind.Object:
                    PickRow(tp.Label, panel, ParamSources.Objects(_scene).Select(n => (n, n)).ToList(), cur, tp.AllowNone, null, Set);
                    break;
                case PickKind.ObjectOrPrefab:
                    PickRow(tp.Label, panel, ParamSources.Objects(_scene).Select(n => (n, n))
                        .Concat(ParamSources.Prefabs(_project).Where(p => !ParamSources.Objects(_scene).Contains(p)).Select(p => (p, p + "  (prefab)"))).ToList(),
                        cur, tp.AllowNone, null, Set);
                    break;
                case PickKind.Scene:
                    PickRow(tp.Label, panel, _project.Scenes.Select(s => (s.Name, s.Name)).ToList(), cur, true, null, Set);
                    break;
                case PickKind.Key:
                    PickRow(tp.Label, panel, ParamSources.Keys.ToList(), cur, tp.AllowNone, null, Set);
                    break;
                case PickKind.Item:
                    PickRow(tp.Label, panel, ParamSources.Items(_project).Select(i => (i, ToolArt.Title(i))).ToList(), cur, tp.AllowNone, "New item name (e.g. wood, key, shovel):", Set);
                    break;
                case PickKind.Variable:
                    PickRow(tp.Label, panel, ParamSources.Variables(_project).Select(v => (v, v)).ToList(), cur, tp.AllowNone, "New variable name:", Set);
                    break;
                case PickKind.Currency:
                {
                    var opts = ParamSources.Variables(_project).Where(v => !v.StartsWith("is") && v != "timeOfDay" && v != "dayness").Select(v => (v, v))
                        .Concat(ParamSources.Items(_project).Select(i => ("item:" + i, ToolArt.Title(i) + "  (item)"))).ToList();
                    if (!opts.Any(o => o.Item1 == "coins")) opts.Insert(0, ("coins", "coins"));
                    PickRow(tp.Label, panel, opts, cur, false, "New currency (a variable name, e.g. gems):", Set);
                    break;
                }
                case PickKind.Requirement:
                {
                    var opts = new List<(string, string)>();
                    foreach (var i in ParamSources.Items(_project)) { opts.Add(("holding:" + i, $"Holding the {i}")); opts.Add(("item:" + i, $"Owns {A(i)}")); }
                    foreach (var v in ParamSources.Variables(_project)) opts.Add((v, $"Variable {v} is on"));
                    PickRow(tp.Label, panel, opts, cur, true, null, Set, noneLabel: "Always");
                    break;
                }
                case PickKind.Objects:
                    MultiPickRow(tp.Label, panel, ParamSources.Objects(_scene).Select(n => (n, n)).ToList(), cur, "Nothing else", Set);
                    break;
                case PickKind.Prefabs:
                    MultiPickRow(tp.Label, panel, ParamSources.Prefabs(_project).Concat(ParamSources.Objects(_scene)).Distinct().Select(n => (n, n)).ToList(), cur, "Nothing", Set);
                    break;
                case PickKind.Items:
                    MultiPickRow(tp.Label, panel, ItemsForTool(obj).Select(i => (i, ToolArt.Title(i))).ToList(), cur, "All items", Set);
                    break;
                case PickKind.Color:
                    ColorRow(tp.Label, cur, Set, panel);
                    break;
                case PickKind.Sound:
                    SoundRow(obj, bh, panel, tp.Key, tp.Label);
                    break;
                case PickKind.Look:
                {
                    var g = RowGrid(tp.Label, panel);
                    var picker = LookPicker.Make(_project, bh.Text("tool", "sword"), bh.Text("toolColor", "#DDEBFF"), cur, v => { PushUndo(); Set(v); }, this);
                    Grid.SetColumn(picker, 1); g.Children.Add(picker);
                    break;
                }
                case PickKind.HotbarItems:
                    HotbarItemsEditor(obj, bh, panel);
                    break;
                case PickKind.ItemDamage:
                    ItemDamageEditor(obj, bh, panel);
                    break;
                case PickKind.MapMarkers:
                    MapMarkersEditor(obj, bh, panel);
                    break;
                case PickKind.Special:
                    if (bh.Kind == BehaviorKind.Custom && tp.Key == "script") ScriptRow(bh, panel);
                    else if (bh.Kind == BehaviorKind.AdvancedPatrol && tp.Key == "path") AdvancedPatrolRow(obj, bh, panel);
                    else if (bh.Kind is BehaviorKind.BossAI or BehaviorKind.Cutscene) SequenceCard(obj, bh, panel);
                    break;
                case PickKind.LongText:
                    LongTextRow(tp.Label, cur, Set, panel);
                    break;
                default:
                    TextRow(tp.Label, cur, Set, panel);
                    break;
            }
        }

        // feature editors, each in its own file: minimap markers (MainWindow.Minimap.cs),
        // Boss AI / Cutscene card = Advanced Scratch + JSON + "Ask the AI" (MainWindow.Sequences.cs)
        partial void MapMarkersEditor(GameObjectData obj, BehaviorData bh, Panel panel);
        partial void SequenceCard(GameObjectData obj, BehaviorData bh, Panel panel);

        private static string A(string w) => (w.Length > 0 && "aeiouAEIOU".IndexOf(w[0]) >= 0 ? "an " : "a ") + w;

        // The items a Use Item can swing: the scene's hotbar items (or every known item if there's no hotbar).
        private List<string> ItemsForTool(GameObjectData obj)
        {
            var hb = _scene.Layers.SelectMany(l => l.Objects).SelectMany(o => o.Behaviors).FirstOrDefault(b => b.Kind == BehaviorKind.Hotbar);
            var list = hb != null ? PlayWindow.ParseHotbarItems(hb.Text("items", "")).Select(i => i.name).ToList() : ParamSources.Items(_project);
            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ---------------------------------------------------------------- generic pickers
        /// <summary>A dropdown (never typed). The current value is always shown, even if it isn't in the list.
        /// With <paramref name="newPrompt"/>, the last entry is "＋ New…" which asks for a new name.</summary>
        private void PickRow(string label, Panel panel, List<(string value, string label)> options, string current, bool allowNone, string? newPrompt,
                             Action<string> set, string noneLabel = "(none)")
        {
            var g = RowGrid(label, panel);
            var cb = new ComboBox { Height = 30, VerticalAlignment = VerticalAlignment.Center, MaxDropDownHeight = 320 };
            if (allowNone) cb.Items.Add(new ComboBoxItem { Content = noneLabel, Tag = "" });
            foreach (var (v, l) in options)
                if (!cb.Items.Cast<ComboBoxItem>().Any(i => ((string)i.Tag!).Equals(v, StringComparison.OrdinalIgnoreCase)))
                    cb.Items.Add(new ComboBoxItem { Content = l, Tag = v });
            if (!string.IsNullOrEmpty(current) && !cb.Items.Cast<ComboBoxItem>().Any(i => ((string)i.Tag!).Equals(current, StringComparison.OrdinalIgnoreCase)))
                cb.Items.Add(new ComboBoxItem { Content = current, Tag = current });
            if (newPrompt != null) cb.Items.Add(new ComboBoxItem { Content = "＋ New…", Tag = NewTag, FontStyle = FontStyles.Italic });

            cb.SelectedItem = cb.Items.Cast<ComboBoxItem>().FirstOrDefault(i => ((string)i.Tag!).Equals(current ?? "", StringComparison.OrdinalIgnoreCase))
                              ?? (cb.Items.Count > 0 ? cb.Items[0] : null);
            object? last = cb.SelectedItem;
            cb.SelectionChanged += (s, e) =>
            {
                if (cb.SelectedItem is not ComboBoxItem ci || ReferenceEquals(ci, last)) return;
                var v = (string)ci.Tag!;
                if (v == NewTag)
                {
                    var name = InputDialog.Prompt(this, "New", newPrompt!, "");
                    if (name == null) { cb.SelectedItem = last; return; }
                    name = name.Trim();
                    var item = new ComboBoxItem { Content = name, Tag = name };
                    cb.Items.Insert(cb.Items.Count - 1, item);
                    last = item; cb.SelectedItem = item;
                    PushUndo(); set(name);
                    return;
                }
                last = ci;
                PushUndo(); set(v);
            };
            Grid.SetColumn(cb, 1); g.Children.Add(cb);
        }

        /// <summary>Several choices at once: a button with the summary that opens a list of checkboxes. Stored comma-separated.</summary>
        private void MultiPickRow(string label, Panel panel, List<(string value, string label)> options, string current, string noneLabel, Action<string> set)
        {
            var g = RowGrid(label, panel);
            var chosen = (current ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            foreach (var c in chosen) if (!options.Any(o => o.value.Equals(c, StringComparison.OrdinalIgnoreCase))) options.Add((c, c));
            var btn = new Button { Height = 30, Style = (Style)FindResource("CardButton"), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 0, 8, 0) };
            void Summary() => btn.Content = (chosen.Count == 0 ? noneLabel : string.Join(", ", chosen.Select(c => options.First(o => o.value.Equals(c, StringComparison.OrdinalIgnoreCase)).label))) + "   ▾";
            Summary();
            var menu = new ContextMenu { PlacementTarget = btn, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var (v, l) in options)
            {
                var mi = new MenuItem { Header = l, IsCheckable = true, StaysOpenOnClick = true, IsChecked = chosen.Any(c => c.Equals(v, StringComparison.OrdinalIgnoreCase)) };
                mi.Click += (s, e) =>
                {
                    PushUndo();
                    chosen.RemoveAll(c => c.Equals(v, StringComparison.OrdinalIgnoreCase));
                    if (mi.IsChecked) chosen.Add(v);
                    chosen = options.Select(o => o.value).Where(o => chosen.Any(c => c.Equals(o, StringComparison.OrdinalIgnoreCase))).ToList(); // keep list order
                    set(string.Join(",", chosen)); Summary();
                };
                menu.Items.Add(mi);
            }
            if (options.Count == 0) menu.Items.Add(new MenuItem { Header = "(nothing to choose yet)", IsEnabled = false });
            btn.Click += (s, e) => menu.IsOpen = true;
            Grid.SetColumn(btn, 1); g.Children.Add(btn);
        }

        private void LongTextRow(string label, string value, Action<string> onChange, Panel panel)
        {
            panel.Children.Add(new TextBlock { Text = label, Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 12, Margin = new Thickness(0, 2, 0, 3), TextWrapping = TextWrapping.Wrap });
            var tb = new TextBox { Text = value, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 54, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 6) };
            tb.LostFocus += (s, e) => { if (tb.Text != value) { PushUndo(); value = tb.Text; onChange(tb.Text); } };
            panel.Children.Add(tb);
        }

        // ---------------------------------------------------------------- Hotbar items: one row per item, with its drawing
        private void HotbarItemsEditor(GameObjectData obj, BehaviorData bh, Panel panel)
        {
            panel.Children.Add(new TextBlock { Text = "Items", Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 12, Margin = new Thickness(0, 2, 0, 4) });
            var items = PlayWindow.ParseHotbarItems(bh.Text("items", ""));
            void Save() { bh.Texts["items"] = string.Join(",", items.Select(i => i.look.Length > 0 ? $"{i.name}:{i.color}:{i.look}" : $"{i.name}:{i.color}")); Dirty(); }

            for (int idx = 0; idx < items.Count; idx++)
            {
                int i = idx;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 5) };
                foreach (var w in new[] { 34.0, -1, 30, 118, 28 }) row.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });

                var iconHost = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(Color.FromRgb(24, 24, 30)), Padding = new Thickness(3) };
                void Redraw() => iconHost.Child = LookPicker.Icon(_project, items[i].name, items[i].color, items[i].look, 24);
                Redraw();

                var name = new TextBlock { Text = ToolArt.Title(items[i].name), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0), Foreground = (SolidColorBrush)FindResource("Text"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = items[i].name };

                var swatch = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(4), Background = BrushFrom(items[i].color, "#CCCCCC"), BorderBrush = (Brush)FindResource("Border"), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = "Colour of the metal part", VerticalAlignment = VerticalAlignment.Center };
                swatch.MouseLeftButtonUp += (s, e) => ShowColorPicker(swatch, items[i].color, hex =>
                {
                    items[i] = (items[i].name, hex, items[i].look); swatch.Background = BrushFrom(hex, "#CCCCCC"); Redraw(); Save();
                });

                var look = LookPicker.Make(_project, items[i].name, items[i].color, items[i].look, v => { PushUndo(); items[i] = (items[i].name, items[i].color, v); Redraw(); Save(); }, this);
                look.Margin = new Thickness(4, 0, 4, 0);

                var del = new Button { Content = "✕", Width = 26, Height = 26, Style = (Style)FindResource("CardButton"), Foreground = (SolidColorBrush)FindResource("Danger"), ToolTip = "Remove this item from the hotbar", HorizontalContentAlignment = HorizontalAlignment.Center };
                del.Click += (s, e) => { PushUndo(); items.RemoveAt(i); Save(); RefreshAfterChoiceAlways(obj); };

                Grid.SetColumn(iconHost, 0); Grid.SetColumn(name, 1); Grid.SetColumn(swatch, 2); Grid.SetColumn(look, 3); Grid.SetColumn(del, 4);
                row.Children.Add(iconHost); row.Children.Add(name); row.Children.Add(swatch); row.Children.Add(look); row.Children.Add(del);
                panel.Children.Add(row);
            }
            if (items.Count == 0) panel.Children.Add(new TextBlock { Text = "No items yet — add one below.", Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
            NewItemButton(obj, bh, panel);
        }

        private void RefreshAfterChoiceAlways(GameObjectData obj)
        {
            double off = InspectorScroll.VerticalOffset;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_selected != obj) return;
                BuildInspector(obj);
                Dispatcher.BeginInvoke(new Action(() => InspectorScroll.ScrollToVerticalOffset(off)), DispatcherPriority.Loaded);
            }), DispatcherPriority.Background);
        }

        // ---------------------------------------------------------------- Use Item: damage per item, one row each
        private void ItemDamageEditor(GameObjectData obj, BehaviorData bh, Panel panel)
        {
            panel.Children.Add(new TextBlock { Text = "Damage to enemies, per item", Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 12, Margin = new Thickness(0, 4, 0, 4) });
            var hb = _scene.Layers.SelectMany(l => l.Objects).SelectMany(o => o.Behaviors).FirstOrDefault(b => b.Kind == BehaviorKind.Hotbar);
            var slots = hb != null ? PlayWindow.ParseHotbarItems(hb.Text("items", "")) : new List<(string name, string color, string look)>();
            var names = slots.Select(s => s.name).ToList();
            if (hb == null) names.Add(bh.Text("tool", "sword"));
            foreach (var (item, _) in ParamSources.ParseDamage(bh.Text("itemDamage", "")))
                if (!names.Any(n => n.Equals(item, StringComparison.OrdinalIgnoreCase))) names.Add(item);

            foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var slot = slots.FirstOrDefault(s => s.name.Equals(name, StringComparison.OrdinalIgnoreCase));
                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                var icon = LookPicker.Icon(_project, name, slot.name != null ? slot.color : bh.Text("toolColor", "#DDEBFF"), slot.name != null ? slot.look : bh.Text("toolLook", ""), 22);
                icon.VerticalAlignment = VerticalAlignment.Center;
                var lbl = new TextBlock { Text = ToolArt.Title(name), VerticalAlignment = VerticalAlignment.Center, Foreground = (SolidColorBrush)FindResource("Text"), Margin = new Thickness(4, 0, 0, 0) };
                var entry = ParamSources.ParseDamage(bh.Text("itemDamage", "")).FirstOrDefault(d => d.item.Equals(name, StringComparison.OrdinalIgnoreCase));
                double dmg = entry.item != null ? entry.dmg : bh.Num("damage", 1);
                var box = new TextBox { Text = dmg.ToString("0.##", CultureInfo.InvariantCulture), Height = 28, ToolTip = "Damage this item does to anything with Health" };
                string n = name;
                void Commit()
                {
                    if (!double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return;
                    PushUndo(); bh.Texts["itemDamage"] = ParamSources.SetDamage(bh.Text("itemDamage", ""), n, v); Dirty();
                }
                box.LostFocus += (s, e) => Commit();
                box.KeyDown += (s, e) => { if (e.Key == Key.Enter) Commit(); };
                Grid.SetColumn(icon, 0); Grid.SetColumn(lbl, 1); Grid.SetColumn(box, 2);
                row.Children.Add(icon); row.Children.Add(lbl); row.Children.Add(box);
                panel.Children.Add(row);
            }
        }
    }
}
