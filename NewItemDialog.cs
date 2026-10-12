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
            if (name.Length == 0) throw new ArgumentException("The item needs a name.");

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

            return $"Item \"{name}\" added to the hotbar" + (addedUse ? $" (and Use Item added to {player.Name})" : "") +
                   (it.StartOwned ? " — the player starts with it" : $" — give it with item:{name} = 1 (shop, pickup…)");
        }

        private static string Slot(string name, string color, string look) => look.Length > 0 ? $"{name}:{color}:{look}" : $"{name}:{color}";
    }

    /// <summary>"＋ New item" on a Hotbar: make your own tool or weapon in a few seconds, with a live preview.</summary>
    public class NewItemDialog : Window
    {
        public ItemSpec? Result { get; private set; }

        private readonly Project _project;
        private readonly TextBox _name = new() { Text = "shovel", Height = 30, FontSize = 14 };
        private readonly ComboBox _look = new() { Height = 30 };
        private readonly TextBox _color = new() { Text = "#B0B0B0", Height = 30, FontSize = 13 };
        private readonly TextBox _damage = new() { Text = "1", Height = 30, FontSize = 13 };
        private readonly CheckBox _owned = new() { Content = "The player starts with it", IsChecked = true, Margin = new Thickness(0, 10, 0, 0) };
        private readonly Border _preview = new() { Width = 84, Height = 84, CornerRadius = new CornerRadius(8) };
        private string _picture = ""; // project-relative path of the item's own picture, if chosen

        public NewItemDialog(Project project)
        {
            _project = project;
            Title = "New item"; Width = 440; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; WindowStyle = WindowStyle.ToolWindow;
            Background = Res("Bg0") as Brush ?? Brushes.Black; FontFamily = new FontFamily("Segoe UI");

            foreach (var l in ToolArt.Looks) _look.Items.Add(l == "tile" ? "tile (coloured square + letter)" : l);
            _look.Items.Add("my picture…");
            _look.SelectedIndex = Array.IndexOf(ToolArt.Looks, "shovel");
            _preview.Background = new SolidColorBrush(Color.FromRgb(24, 24, 30));

            var root = new StackPanel { Margin = new Thickness(20) };
            root.Children.Add(Text("Make your own tool or weapon. It goes into this hotbar, and clicking uses it like the axe or the sword.", 12, true));

            var top = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var fields = new StackPanel();
            fields.Children.Add(Text("Name (also what Harvestable 'tool' asks for)", 12)); fields.Children.Add(_name);
            fields.Children.Add(Text("Look", 12, top: 8)); fields.Children.Add(_look);
            Grid.SetColumn(fields, 0); top.Children.Add(fields);
            _preview.Margin = new Thickness(14, 18, 0, 0); _preview.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(_preview, 1); top.Children.Add(_preview);
            root.Children.Add(top);

            root.Children.Add(Text("Colour (of the metal part)", 12, top: 8));
            var colorRow = new StackPanel { Orientation = Orientation.Horizontal };
            _color.Width = 110; colorRow.Children.Add(_color);
            foreach (var hex in new[] { "#B0B0B0", "#8B5A2B", "#DDEBFF", "#FFD24D", "#E05A5A", "#5BC77A", "#7A8CFF" })
            {
                var sw = new Button { Width = 24, Height = 24, Margin = new Thickness(6, 0, 0, 0), Background = (Brush)new BrushConverter().ConvertFromString(hex)!, ToolTip = hex };
                sw.Click += (s, e) => _color.Text = hex;
                colorRow.Children.Add(sw);
            }
            root.Children.Add(colorRow);

            root.Children.Add(Text("Damage to enemies", 12, top: 8));
            _damage.Width = 110; _damage.HorizontalAlignment = HorizontalAlignment.Left; root.Children.Add(_damage);
            root.Children.Add(_owned);
            root.Children.Add(Text("Tip: to make something need it, give that object a Harvestable and set its Tool to this name (e.g. a dirt mound that only the shovel digs).", 11, true, top: 12));

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = new Button { Content = "Cancel", Width = 90, Height = 34, Margin = new Thickness(0, 0, 8, 0), Style = Res("CardButton") as Style };
            var ok = new Button { Content = "Create", Width = 90, Height = 34, IsDefault = true, Style = Res("AccentButton") as Style };
            cancel.Click += (s, e) => { DialogResult = false; };
            ok.Click += (s, e) => Create();
            buttons.Children.Add(cancel); buttons.Children.Add(ok);
            root.Children.Add(buttons);
            Content = root;

            _name.TextChanged += (s, e) => Preview();
            _color.TextChanged += (s, e) => Preview();
            _look.SelectionChanged += (s, e) => LookChanged();
            Loaded += (s, e) => { _name.Focus(); _name.SelectAll(); Preview(); };
        }

        private void LookChanged()
        {
            if (_look.SelectedIndex == ToolArt.Looks.Length) // "my picture…"
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Picture of the item", Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp" };
                if (dlg.ShowDialog(this) == true)
                {
                    try { _picture = ProjectIO.ImportAsset(_project, dlg.FileName, "sprites"); }
                    catch (Exception ex) { MessageBox.Show(this, "Couldn't use that picture: " + ex.Message); _picture = ""; }
                }
                if (_picture.Length == 0) { _look.SelectedIndex = 0; return; }
            }
            else _picture = "";
            Preview();
        }

        private string CurrentLook() => _picture.Length > 0 ? _picture : ToolArt.Looks[Math.Clamp(_look.SelectedIndex, 0, ToolArt.Looks.Length - 1)];

        private void Preview()
        {
            ImageSource? pic = null;
            if (_picture.Length > 0 && _project.FolderPath != null)
                try { pic = new BitmapImage(new Uri(System.IO.Path.Combine(_project.FolderPath, _picture))); } catch { }
            _preview.Child = ToolArt.Make(_name.Text, _color.Text, 64, out _, pic == null ? CurrentLook() : null, pic);
            ((FrameworkElement)_preview.Child).Margin = new Thickness(10);
        }

        private void Create()
        {
            var name = _name.Text.Trim();
            if (name.Length == 0) { MessageBox.Show(this, "Give the item a name."); return; }
            if (!double.TryParse(_damage.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var dmg)) dmg = 1;
            var color = _color.Text.Trim();
            try { ColorConverter.ConvertFromString(color); } catch { color = "#B0B0B0"; }
            Result = new ItemSpec(name, CurrentLook(), color, dmg, _owned.IsChecked == true);
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
