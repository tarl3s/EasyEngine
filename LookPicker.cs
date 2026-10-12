using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EasyEngine
{
    /// <summary>
    /// A dropdown that shows every tool design as a picture (pickaxe, axe, sword, shovel, hammer, hoe, a lettered
    /// square) plus "My picture…" to use your own image. Used by the Hotbar item list, Use Item and "New item".
    /// </summary>
    public static class LookPicker
    {
        private const string PictureTag = "\u0001picture";

        /// <param name="look">current look: a built-in name, a picture path, or "" (= guessed from the item's name).</param>
        public static ComboBox Make(Project project, string itemName, string color, string look, Action<string> onChange, Window? owner = null)
        {
            var combo = new ComboBox { Height = 30, MinWidth = 110, ToolTip = "How the item looks (in the hand and on the hotbar)" };
            foreach (var l in ToolArt.Looks)
                combo.Items.Add(new ComboBoxItem { Content = Entry(ToolArt.Make(itemName, color, 20, out _, l), ToolArt.LookTitle(l)), Tag = l });
            if (ToolArt.IsPicture(look))
                combo.Items.Add(new ComboBoxItem { Content = Entry(Picture(project, look, 20), Path.GetFileName(look)), Tag = look });
            combo.Items.Add(new ComboBoxItem { Content = Entry(new TextBlock { Text = "🖼", FontSize = 14, Width = 20 }, "My picture…"), Tag = PictureTag });

            string current = ToolArt.IsPicture(look) ? look : ToolArt.Shape(string.IsNullOrWhiteSpace(look) ? itemName : look);
            foreach (ComboBoxItem it in combo.Items) if ((string)it.Tag! == current) { combo.SelectedItem = it; break; }
            object? last = combo.SelectedItem;

            combo.SelectionChanged += (s, e) =>
            {
                if (combo.SelectedItem is not ComboBoxItem ci) return;
                var tag = (string)ci.Tag!;
                if (tag == PictureTag)
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Picture of the item", Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp" };
                    string? rel = null;
                    if (dlg.ShowDialog(owner ?? Application.Current.MainWindow) == true)
                        try { rel = ProjectIO.ImportAsset(project, dlg.FileName, "sprites"); } catch (Exception ex) { MessageBox.Show("Couldn't use that picture: " + ex.Message); }
                    if (rel == null) { combo.SelectedItem = last; return; }
                    var item = new ComboBoxItem { Content = Entry(Picture(project, rel, 20), Path.GetFileName(rel)), Tag = rel };
                    combo.Items.Insert(combo.Items.Count - 1, item);
                    last = item; combo.SelectedItem = item;
                    onChange(rel);
                    return;
                }
                last = ci;
                onChange(tag);
            };
            return combo;
        }

        /// <summary>The item drawn as it will look (built-in drawing or its picture).</summary>
        public static FrameworkElement Icon(Project project, string itemName, string color, string look, double size)
            => ToolArt.IsPicture(look) ? Picture(project, look, size) : ToolArt.Make(itemName, color, size, out _, look);

        private static FrameworkElement Picture(Project project, string rel, double size)
        {
            var img = new Image { Width = size, Height = size, Stretch = Stretch.Uniform };
            try
            {
                var path = Path.IsPathRooted(rel) ? rel : Path.Combine(project.FolderPath ?? "", rel);
                var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(path); bmp.EndInit();
                img.Source = bmp;
            }
            catch { }
            return img;
        }

        private static StackPanel Entry(FrameworkElement icon, string text)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            icon.Margin = new Thickness(0, 0, 8, 0); icon.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(icon);
            sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }
    }
}
