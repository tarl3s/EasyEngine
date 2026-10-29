using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace EasyEngine
{
    /// <summary>A picker of the project's pictures (Assets\sprites) with "＋ Add a picture…", which copies a file in.
    /// Used by every "Box picture" (Interact, Trigger Area, a Cutscene's or a boss's dialogue step).</summary>
    public static class PicturePick
    {
        public static List<(string value, string label)> Options(Project p)
        {
            var list = new List<(string, string)> { ("", L.T("(none)")) };
            var dir = string.IsNullOrEmpty(p.FolderPath) ? null : Path.Combine(p.FolderPath, "Assets", "sprites");
            if (dir != null && Directory.Exists(dir))
                list.AddRange(Directory.GetFiles(dir).Where(IsPicture).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .Select(f => (Path.Combine("Assets", "sprites", Path.GetFileName(f)), Path.GetFileName(f))));
            return list;
        }

        private static bool IsPicture(string f) =>
            new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp" }.Contains(Path.GetExtension(f).ToLowerInvariant());

        public static ComboBox Make(Project p, string current, Action<string> set, Window? owner)
        {
            var cb = Dropdown.Make(Options(p), current, set, owner: owner, create: () => Import(p, owner), createLabel: L.T("＋ Add a picture…"));
            L.SetSkip(cb, true); // file names; "(none)" and the add entry are already translated
            return cb;
        }

        private static (string value, string label)? Import(Project p, Window? owner)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = L.T("Picture of the dialogue box"), Filter = L.T("Images") + "|*.png;*.jpg;*.jpeg;*.gif;*.bmp" };
            if (dlg.ShowDialog(owner ?? Application.Current.MainWindow) != true) return null;
            try { var rel = ProjectIO.ImportAsset(p, dlg.FileName, "sprites"); return (rel, Path.GetFileName(rel)); }
            catch (Exception ex) { MessageBox.Show(L.T("Couldn't use that picture:") + " " + ex.Message); return null; }
        }
    }
}
