using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
            panel.Children.Add(new TextBlock { Text = L.T("Also show on the map"), Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 12, Margin = new Thickness(0, 2, 0, 4) });
            var markers = PlayWindow.ParseMapMarkers(bh.Text("markers", ""));
            void Save() { bh.Texts["markers"] = string.Join(",", markers.Select(m => m.color.Length > 0 ? $"{m.name}:{m.color}" : m.name)); Dirty(); }

            // who can be marked: objects in this scene, prefabs (every copy), and the groups used in this scene
            var options = ParamSources.ObjectOrPrefabOptions(_scene, _project);
            foreach (var g in _scene.Layers.SelectMany(l => l.Objects).Select(o => o.Group).Distinct())
                if (!string.IsNullOrWhiteSpace(g) && !options.Any(o => o.value.Equals(g, StringComparison.OrdinalIgnoreCase))) options.Add((g, L.F("{0}  (group)", g)));

            for (int idx = 0; idx < markers.Count; idx++)
            {
                int i = idx;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 5) };
                foreach (var w in new[] { -1.0, 32, 30 }) row.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });

                var who = Dropdown.Make(options, markers[i].name, v => { PushUndo(); markers[i] = (v, markers[i].color); Save(); });
                who.Height = 28; who.VerticalAlignment = VerticalAlignment.Center;
                L.SetSkip(who, true); // object / prefab / group names
                who.ToolTip = L.T("What to show on the map (an object, every copy of a prefab, or a whole group)");
                var swatch = SwatchButton(markers[i].color, MarkerGray, 12, L.T("Colour of its dot on the map"), hex => { markers[i] = (markers[i].name, hex); Save(); });
                var del = DeleteButton(obj, L.T("Don't show this on the map"), () => { markers.RemoveAt(i); Save(); });

                Grid.SetColumn(who, 0); Grid.SetColumn(swatch, 1); Grid.SetColumn(del, 2);
                row.Children.Add(who); row.Children.Add(swatch); row.Children.Add(del);
                panel.Children.Add(row);
            }
            if (markers.Count == 0) EmptyHint(panel, L.T("Just the player — add things to show below."));

            var add = new Button { Content = L.T("＋ Add"), Height = 28, Margin = new Thickness(0, 2, 0, 4), Style = (Style)FindResource("CardButton"), ToolTip = L.T("Show one more thing on the map, with its own colour") };
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
