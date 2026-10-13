using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media;

namespace EasyEngine
{
    /// <summary>The fonts a game's texts can use (Label, Interact/Area/UI Button messages, Cutscene, an object's font):
    /// "" = the game's font (the project theme's), a font installed on Windows (by its family name), or a font file the
    /// person added to the project — stored as its project-relative path (e.g. <c>Assets\fonts\Pixel.ttf</c>) and shown by
    /// the family name read from the file. Shared by the editor's Font picker and the game.</summary>
    public static class GameFonts
    {
        public const string Folder = "fonts"; // Assets\fonts
        private static readonly string[] Extensions = { ".ttf", ".otf", ".ttc" };

        /// <summary>The value is a font file (a path), not a family name.</summary>
        public static bool IsFile(string? value) =>
            !string.IsNullOrWhiteSpace(value) && Extensions.Any(e => value.Trim().EndsWith(e, StringComparison.OrdinalIgnoreCase));

        /// <summary>A Label's font: its card's Font, else the object's older Font setting (projects from before the card had one).</summary>
        public static string LabelFont(BehaviorData label, GameObjectData obj)
        {
            var f = label.Text("font", "").Trim();
            return f.Length > 0 ? f : obj.FontFamily ?? "";
        }

        /// <summary>The project's own font files (Assets\fonts), as project-relative paths.</summary>
        public static List<string> ProjectFonts(Project p)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(p.FolderPath)) return list;
            var dir = Path.Combine(p.FolderPath, "Assets", Folder);
            try
            {
                if (Directory.Exists(dir))
                    foreach (var f in Directory.EnumerateFiles(dir).Where(f => IsFile(f)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                        list.Add(Path.Combine("Assets", Folder, Path.GetFileName(f)));
            }
            catch (Exception) { }
            return list;
        }

        // family names read from files, per path + write time (a replaced file is read again)
        private static readonly Dictionary<string, string?> _families = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The family name inside a font file (English if it has one), or null when it isn't a font WPF can read.</summary>
        public static string? FamilyOf(string absPath)
        {
            string key;
            try { key = absPath + "|" + File.GetLastWriteTimeUtc(absPath).Ticks; } catch (Exception) { return null; }
            if (_families.TryGetValue(key, out var known)) return known;
            string? name = null;
            try
            {
                if (File.Exists(absPath))
                {
                    var gt = new GlyphTypeface(new Uri(absPath));
                    var names = gt.FamilyNames;
                    if (!names.TryGetValue(CultureInfo.GetCultureInfo("en-US"), out name)) name = names.Values.FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(name)) name = null;
                }
            }
            catch (Exception) { name = null; }
            _families[key] = name;
            return name;
        }

        /// <summary>How a font value is shown in the picker.</summary>
        public static string Label(Project p, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return L.T("The game's font");
            if (!IsFile(value)) return value;
            var abs = ProjectIO.AbsoluteAsset(p, value.Trim());
            var fam = abs != null ? FamilyOf(abs) : null;
            return fam ?? L.F("{0} (missing font file)", Path.GetFileName(value.Trim()));
        }

        /// <summary>The Font picker's choices: the game's font, the project's font files, then the fonts every Windows has.
        /// A current value that is none of them (another installed font, a missing file) is kept in the list.</summary>
        public static List<(string value, string label)> Options(Project p, string? current)
        {
            var opts = new List<(string value, string label)> { ("", L.T("The game's font")) };
            foreach (var f in ProjectFonts(p)) opts.Add((f, Label(p, f)));
            foreach (var o in BehaviorCatalog.DialogFonts.Select(ParamSources.Option))
                if (o.value.Length > 0) opts.Add((o.value, o.value));
            current = (current ?? "").Trim();
            if (current.Length > 0 && !opts.Any(o => o.value.Equals(current, StringComparison.OrdinalIgnoreCase)))
                opts.Insert(1, (current, Label(p, current)));
            return opts;
        }

        // ready fonts per value (a family name, or a file's absolute path + time)
        private static readonly Dictionary<string, FontFamily> _made = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The font for a value; null = use the fallback (empty value, or a font file that is missing or not a font —
        /// <paramref name="problem"/> then says why).</summary>
        public static FontFamily? Make(Project p, string? value, out string? problem)
        {
            problem = null;
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = value.Trim();
            if (!IsFile(value))
            {
                if (_made.TryGetValue(value, out var sys)) return sys;
                try { return _made[value] = new FontFamily(value); } catch (Exception) { problem = $"The font \"{value}\" can't be used"; return null; }
            }
            var abs = ProjectIO.AbsoluteAsset(p, value);
            if (abs == null) { problem = $"The font file {value} is missing"; return null; }
            var fam = FamilyOf(abs);
            if (fam == null) { problem = $"{value} isn't a font file the game can read"; return null; }
            string key;
            try { key = abs + "|" + File.GetLastWriteTimeUtc(abs).Ticks; } catch (Exception) { key = abs; }
            if (_made.TryGetValue(key, out var made)) return made;
            try
            {
                // "<folder>/<file>#<family>": that one file's family (not any other font in the folder or on Windows)
                var folder = new Uri(Path.GetDirectoryName(abs)! + Path.DirectorySeparatorChar);
                var ff = new FontFamily(folder, "./" + Uri.EscapeDataString(Path.GetFileName(abs)) + "#" + fam);
                return _made[key] = ff;
            }
            catch (Exception ex) { problem = $"{value} isn't a font file the game can read ({ex.Message})"; return null; }
        }

        /// <summary>The file a font resolves to (tests): the regular face's file path, or "" when it isn't a file font.</summary>
        public static string FileOf(FontFamily ff)
        {
            try
            {
                var tf = new Typeface(ff, System.Windows.FontStyles.Normal, System.Windows.FontWeights.Normal, System.Windows.FontStretches.Normal);
                return tf.TryGetGlyphTypeface(out var gt) && gt.FontUri.IsFile ? gt.FontUri.LocalPath : "";
            }
            catch (Exception) { return ""; }
        }
    }
}
