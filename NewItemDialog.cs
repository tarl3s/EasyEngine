using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EasyEngine
{
    /// <summary>A custom item (tool / weapon) for a Hotbar: name, look, colour, damage, and whether the player starts with it.</summary>
    public record ItemSpec(string Name, string Look, string Color, double Damage, bool StartOwned);

    /// <summary>Adds a custom item to a Hotbar in one go — the slot, its damage in the player's Use Item, and (optionally) owning it.</summary>
    public static class ItemMaker
    {
        public static string Add(Project project, Scene scene, GameObjectData hotbarObj, BehaviorData hotbar, ItemSpec it)
        {
            string name = it.Name.Trim().Replace(",", "").Replace(":", "");
            if (name.Length == 0) throw new ArgumentException(L.T("The item needs a name."));

            // 1) the slot (replaces a slot with the same name)
            var slots = PlayWindow.ParseHotbarItems(hotbar.Text("items", "")).Where(s => !s.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        .Select(s => Slot(s.name, s.color, s.look)).ToList();
            slots.Add(Slot(name, it.Color, it.Look));
            hotbar.Texts["items"] = string.Join(",", slots);

            // 2) its damage, on the player's Use Item (added if the player has none, so clicking uses the item)
            var all = scene.Layers.SelectMany(l => l.Objects).ToList();
            var player = all.FirstOrDefault(o => o.Name.Equals(hotbar.Text("player", "Player"), StringComparison.OrdinalIgnoreCase)) ?? hotbarObj;
            var use = player.Behaviors.FirstOrDefault(b => b.Kind == BehaviorKind.ToolUse);
            bool addedUse = use == null;
            if (use == null) { use = BehaviorCatalog.Info(BehaviorKind.ToolUse).CreateData(); use.Texts["itemDamage"] = ""; player.Behaviors.Add(use); }
            var dmg = use.Text("itemDamage", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                      .Where(p => !p.Split('=')[0].Trim().Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            dmg.Add($"{name}={it.Damage.ToString(CultureInfo.InvariantCulture)}");
            use.Texts["itemDamage"] = string.Join(",", dmg);

            // 3) owning it from the start
            if (it.StartOwned) project.Variables["item:" + name] = 1;

            return (addedUse, it.StartOwned) switch
            {
                (false, true) => L.F("Item \"{0}\" added to the hotbar — the player starts with it", name),
                (false, false) => L.F("Item \"{0}\" added to the hotbar — give it with item:{0} = 1 (shop, pickup…)", name),
                (true, true) => L.F("Item \"{0}\" added to the hotbar (and Use Item added to {1}) — the player starts with it", name, player.Name),
                (true, false) => L.F("Item \"{0}\" added to the hotbar (and Use Item added to {1}) — give it with item:{0} = 1 (shop, pickup…)", name, player.Name),
            };
        }

        private static string Slot(string name, string color, string look) => look.Length > 0 ? $"{name}:{color}:{look}" : $"{name}:{color}";
    }

    /// <summary>"＋ New item" on a Hotbar: make your own tool or weapon in a few seconds, with a live preview.</summary>
    public class NewItemDialog : Window
    {
        public ItemSpec? Result { get; private set; }

        private readonly Project _project;
        private readonly TextBox _name = new() { Text = "shovel", Height = 30, FontSize = 14 };
        private readonly ContentControl _lookHost = new();
        private readonly TextBox _damage = new() { Text = "1", Height = 30, FontSize = 13 };
        private readonly CheckBox _owned = new() { Content = L.T("The player starts with it"), IsChecked = true, Margin = new Thickness(0, 10, 0, 0) };
        private readonly Border _preview = new() { Width = 84, Height = 84, CornerRadius = new CornerRadius(8) };
        private readonly Border _colorShown = new() { Width = 30, Height = 24, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(2), BorderBrush = Brushes.White };
        private string _look = "shovel";   // a built-in look, or the project-relative path of the item's own picture
        private string _color = "#B0B0B0";

        public NewItemDialog(Project project)
        {
            _project = project;
            Title = L.T("New item"); Width = 460; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; WindowStyle = WindowStyle.ToolWindow;
            Background = Res("Bg0") as Brush ?? Brushes.Black; FontFamily = new FontFamily("Segoe UI");
            _preview.Background = new SolidColorBrush(Color.FromRgb(24, 24, 30));
            RebuildLookPicker();

            var root = new StackPanel { Margin = new Thickness(20) };
            root.Children.Add(Text(L.T("Make your own tool or weapon. It goes into this hotbar, and clicking uses it like the axe or the sword."), 12, true));

            var top = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var fields = new StackPanel();
            fields.Children.Add(Text(L.T("Name (also what Harvestable 'tool' asks for)"), 12)); fields.Children.Add(_name);
            fields.Children.Add(Text(L.T("Design"), 12, top: 8)); fields.Children.Add(_lookHost);
            Grid.SetColumn(fields, 0); top.Children.Add(fields);
            _preview.Margin = new Thickness(14, 18, 0, 0); _preview.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(_preview, 1); top.Children.Add(_preview);
            root.Children.Add(top);

            root.Children.Add(Text(L.T("Colour (of the metal part)"), 12, top: 8));
            var colorRow = new StackPanel { Orientation = Orientation.Horizontal };
            colorRow.Children.Add(_colorShown);
            foreach (var hex in new[] { "#B0B0B0", "#8B5A2B", "#DDEBFF", "#FFD24D", "#E05A5A", "#5BC77A", "#7A8CFF" })
            {
                var sw = new Button { Width = 24, Height = 24, Margin = new Thickness(6, 0, 0, 0), Background = (Brush)new BrushConverter().ConvertFromString(hex)!, ToolTip = hex };
                sw.Click += (s, e) => SetColor(hex);
                colorRow.Children.Add(sw);
            }
            var more = new Button { Content = L.T("More…"), Height = 24, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 0, 8, 0), Style = Res("CardButton") as Style };
            more.Click += (s, e) => ColorPickerPopup.Open(more, _color, SetColor);
            colorRow.Children.Add(more);
            root.Children.Add(colorRow);
            SetColor(_color);

            root.Children.Add(Text(L.T("Damage to enemies"), 12, top: 8));
            _damage.Width = 110; _damage.HorizontalAlignment = HorizontalAlignment.Left; root.Children.Add(_damage);
            root.Children.Add(_owned);
            root.Children.Add(Text(L.T("Tip: to make something need it, give that object a Harvestable and set its Tool to this name (e.g. a dirt mound that only the shovel digs)."), 11, true, top: 12));

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = new Button { Content = L.T("Cancel"), Width = 90, Height = 34, Margin = new Thickness(0, 0, 8, 0), Style = Res("CardButton") as Style };
            var ok = new Button { Content = L.T("Create"), Width = 90, Height = 34, IsDefault = true, Style = Res("AccentButton") as Style };
            cancel.Click += (s, e) => { DialogResult = false; };
            ok.Click += (s, e) => Create();
            buttons.Children.Add(cancel); buttons.Children.Add(ok);
            root.Children.Add(buttons);
            Content = root;

            _name.TextChanged += (s, e) => Preview();
            Loaded += (s, e) => { _name.Focus(); _name.SelectAll(); Preview(); };
        }

        // the design dropdown draws every option in the chosen colour, so it's rebuilt when the colour changes
        private void RebuildLookPicker() =>
            _lookHost.Content = LookPicker.Make(_project, _name.Text, _color, _look, v => { _look = v; Preview(); }, this);

        private void SetColor(string hex)
        {
            try { ColorConverter.ConvertFromString(hex); } catch { return; }
            _color = hex;
            _colorShown.Background = (Brush)new BrushConverter().ConvertFromString(hex)!;
            RebuildLookPicker();
            Preview();
        }

        private void Preview()
        {
            var icon = LookPicker.Icon(_project, _name.Text, _color, _look, 64);
            icon.Margin = new Thickness(10);
            _preview.Child = icon;
        }

        private void Create()
        {
            var name = _name.Text.Trim();
            if (name.Length == 0) { MessageBox.Show(this, L.T("Give the item a name.")); return; }
            if (!double.TryParse(_damage.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var dmg)) dmg = 1;
            Result = new ItemSpec(name, _look, _color, dmg, _owned.IsChecked == true);
            DialogResult = true;
        }

        private TextBlock Text(string s, double size, bool wrap = false, double top = 0) => new()
        {
            Text = s, FontSize = size, Foreground = Res("Text") as Brush ?? Brushes.White, Margin = new Thickness(0, top, 0, 4),
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap
        };

        private static object? Res(string key) => Application.Current.TryFindResource(key);
    }
}
