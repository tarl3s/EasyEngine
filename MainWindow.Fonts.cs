using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace EasyEngine
{
    // The Font picker (PickKind.Font) of every text behavior — Label, Interact / Trigger Area / UI Button messages, Cutscene:
    // the game's font, the project's own font files (Assets\fonts), the fonts every Windows has, and "＋ Add a font file…"
    // which copies a .ttf / .otf into the project (backed up like any asset) so the exported game carries it.
    public partial class MainWindow
    {
        private void FontParamRow(GameObjectData obj, BehaviorData bh, TextParam tp, string label, string cur, Panel panel)
        {
            // a Label from before its card had a Font shows (and replaces) the object's older Font setting
            bool label0 = bh.Kind == BehaviorKind.Label && tp.Key == "font";
            if (label0) cur = GameFonts.LabelFont(bh, obj);
            FontRow(label, panel, cur, v =>
            {
                bh.Texts[tp.Key] = v;
                if (label0) obj.FontFamily = null;
                Dirty();
            });
        }

        private void FontRow(string label, Panel panel, string current, Action<string> set)
        {
            var g = RowGrid(label, panel);
            var cb = Dropdown.Make(GameFonts.Options(_project, current), current, v => { PushUndo(); set(v); }, owner: this,
                                   create: () => PickFontFile(), createLabel: L.T("＋ Add a font file…"));
            cb.Name = FontPickerName;
            L.SetSkip(cb, true); // font names and the person's files; the fixed labels are already translated
            cb.Height = 30; cb.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(cb, 1); g.Children.Add(cb);
        }
        private const string FontPickerName = "FontPicker";

        // "＋ Add a font file…": a file dialog, then the file is copied into Assets\fonts
        private (string value, string label)? PickFontFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = L.T("Add a font file"), Filter = L.T("Font files") + " (*.ttf, *.otf)|*.ttf;*.otf;*.ttc" };
            return dlg.ShowDialog(this) == true ? AddFontFile(dlg.FileName) : null;
        }

        /// <summary>Copies a font file into the project (Assets\fonts) and returns its value (the project-relative path) and
        /// family name; null (with a status message) when it isn't a font or the project has no folder yet.</summary>
        private (string value, string label)? AddFontFile(string file)
        {
            var family = GameFonts.FamilyOf(file);
            if (family == null) { Status(L.F("\"{0}\" isn't a font file the game can use", Path.GetFileName(file))); return null; }
            try
            {
                var rel = ProjectIO.ImportAsset(_project, file, GameFonts.Folder);
                Status(L.F("Font \"{0}\" added to the project", family));
                return (rel, GameFonts.Label(_project, rel));
            }
            catch (Exception ex) { Status(ex.Message); return null; }
        }

        // ---- test hooks ----
        internal (string value, string label)? TestAddFontFile(string file) => AddFontFile(file);
        /// <summary>The Font pickers in the inspector built for <paramref name="obj"/>: each one's (value, label) entries and
        /// the selected value.</summary>
        internal System.Collections.Generic.List<(System.Collections.Generic.List<(string value, string label)> items, string selected)> TestFontPickers(GameObjectData obj)
        {
            TestInspect(obj);
            var found = new System.Collections.Generic.List<(System.Collections.Generic.List<(string, string)>, string)>();
            void Walk(object node)
            {
                if (node is ComboBox cb && cb.Name == FontPickerName)
                    found.Add((cb.Items.OfType<ComboBoxItem>().Select(i => ((string)i.Tag!, i.Content as string ?? "")).ToList(), (cb.SelectedItem as ComboBoxItem)?.Tag as string ?? ""));
                if (node is DependencyObject d) foreach (var c in LogicalTreeHelper.GetChildren(d)) Walk(c);
            }
            foreach (var child in InspectorRoot.Children) Walk(child);
            return found;
        }
        /// <summary>Picks a value in the first Font picker of the inspector built for <paramref name="obj"/> (as a click would).</summary>
        internal bool TestPickFont(GameObjectData obj, string value)
        {
            TestInspect(obj);
            ComboBox? hit = null;
            void Walk(object node)
            {
                if (hit == null && node is ComboBox cb && cb.Name == FontPickerName) hit = cb;
                if (node is DependencyObject d) foreach (var c in LogicalTreeHelper.GetChildren(d)) Walk(c);
            }
            foreach (var child in InspectorRoot.Children) Walk(child);
            var item = hit?.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, value, StringComparison.OrdinalIgnoreCase));
            if (item == null) return false;
            hit!.SelectedItem = item;
            return true;
        }
    }
}
