using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyEngine
{
    // Minimap "Also show": one row per marked thing — [who ▾] [colour] [✕] — plus "＋ Add". Saved as "Name:#RRGGBB,…".
    // A group is stored by its plain name too: at runtime a marker matches an object's name OR its group.
    public partial class MainWindow
    {
        private static readonly string[] MarkerPalette = { "#9B59F0", "#5BC77A", "#FFD24D", "#E05A5A", "#4DB8FF", "#FF9F43", "#F06BC8", "#FFFFFF" };
        private const string MarkerGray = "#B4B4BE"; // what an entry without a colour looks like on the map

        partial void MapMarkersEditor(GameObjectData obj, BehaviorData bh, Panel panel)
        {
            panel.Children.Add(new TextBlock { Text = "Also show on the map", Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 12, Margin = new Thickness(0, 2, 0, 4) });
            var markers = PlayWindow.ParseMapMarkers(bh.Text("markers", ""));
            void Save() { bh.Texts["markers"] = string.Join(",", markers.Select(m => m.color.Length > 0 ? $"{m.name}:{m.color}" : m.name)); Dirty(); }

            // who can be marked: objects in this scene, prefabs (every copy), and the groups used in this scene
            var options = new List<(string value, string label)>();
            void Opt(string v, string l) { if (!string.IsNullOrWhiteSpace(v) && !options.Any(o => o.value.Equals(v, StringComparison.OrdinalIgnoreCase))) options.Add((v, l)); }
            foreach (var n in ParamSources.Objects(_scene)) Opt(n, n);
            foreach (var n in ParamSources.Prefabs(_project)) Opt(n, n + "  (prefab)");
            foreach (var g in _scene.Layers.SelectMany(l => l.Objects).Select(o => o.Group).Distinct()) Opt(g, g + "  (group)");

            for (int idx = 0; idx < markers.Count; idx++)
            {
                int i = idx;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 5) };
                foreach (var w in new[] { -1.0, 32, 30 }) row.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });

                var who = new ComboBox { Height = 28, VerticalAlignment = VerticalAlignment.Center, MaxDropDownHeight = 320, ToolTip = "What to show on the map (an object, every copy of a prefab, or a whole group)" };
                foreach (var (v, l) in options) who.Items.Add(new ComboBoxItem { Content = l, Tag = v });
                if (!options.Any(o => o.value.Equals(markers[i].name, StringComparison.OrdinalIgnoreCase)))
                    who.Items.Add(new ComboBoxItem { Content = markers[i].name, Tag = markers[i].name });
                who.SelectedItem = who.Items.Cast<ComboBoxItem>().First(c => ((string)c.Tag!).Equals(markers[i].name, StringComparison.OrdinalIgnoreCase));
                who.SelectionChanged += (s, e) =>
                {
                    if (who.SelectedItem is not ComboBoxItem ci) return;
                    PushUndo(); markers[i] = ((string)ci.Tag!, markers[i].color); Save();
                };

                var swatch = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = BrushFrom(markers[i].color, MarkerGray), BorderBrush = (Brush)FindResource("Border"), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = "Colour of its dot on the map", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                swatch.MouseLeftButtonUp += (s, e) => ShowColorPicker(swatch, markers[i].color.Length > 0 ? markers[i].color : MarkerGray, hex =>
                {
                    markers[i] = (markers[i].name, hex); swatch.Background = BrushFrom(hex, MarkerGray); Save();
                });

                var del = new Button { Content = "✕", Width = 26, Height = 26, Style = (Style)FindResource("CardButton"), Foreground = (SolidColorBrush)FindResource("Danger"), ToolTip = "Don't show this on the map", HorizontalContentAlignment = HorizontalAlignment.Center };
                del.Click += (s, e) => { PushUndo(); markers.RemoveAt(i); Save(); RefreshAfterChoiceAlways(obj); };

                Grid.SetColumn(who, 0); Grid.SetColumn(swatch, 1); Grid.SetColumn(del, 2);
                row.Children.Add(who); row.Children.Add(swatch); row.Children.Add(del);
                panel.Children.Add(row);
            }
            if (markers.Count == 0) panel.Children.Add(new TextBlock { Text = "Just the player — add things to show below.", Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });

            var add = new Button { Content = "＋ Add", Height = 28, Margin = new Thickness(0, 2, 0, 4), Style = (Style)FindResource("CardButton"), ToolTip = "Show one more thing on the map, with its own colour" };
            var free = options.FirstOrDefault(o => !markers.Any(m => m.name.Equals(o.value, StringComparison.OrdinalIgnoreCase)));
            add.IsEnabled = free.value != null;
            add.Click += (s, e) =>
            {
                PushUndo();
                markers.Add((free.value!, MarkerPalette.FirstOrDefault(c => !markers.Any(m => m.color.Equals(c, StringComparison.OrdinalIgnoreCase))) ?? MarkerPalette[markers.Count % MarkerPalette.Length]));
                Save(); RefreshAfterChoiceAlways(obj);
            };
            panel.Children.Add(add);
        }
    }
}
