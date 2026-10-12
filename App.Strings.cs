using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EasyEngine;

// --strings (dev tool): collects every English text the editor can show and writes Lang\en.json (the list the other
// languages translate) plus Lang\_context.tsv (where each text appears, to help translators). Sources:
//   - L.T("…") / L.F("…") literals in the .cs files,
//   - Text / Content / Header / ToolTip / Title attributes in the .xaml files,
//   - the behavior catalog and the Boss/Cutscene step palette (names, descriptions, labels, option labels, hints),
//     which the editor shows through L.T at the display site.
// Run it from a source checkout (bin\Debug or a scratch build of this folder): EasyEngine.exe --strings [sourceDir]
public partial class App
{
    private void RunStrings(string[] args)
    {
        var log = Path.Combine(Path.GetTempPath(), "easyengine_strings.log");
        try
        {
            int i = Array.IndexOf(args, "--strings");
            var src = i + 1 < args.Length && Directory.Exists(args[i + 1]) ? args[i + 1] : FindSourceDir();
            if (src == null) throw new DirectoryNotFoundException("No EasyEngine.csproj above the exe; pass the source folder: --strings <folder>");
            var (texts, where) = CollectStrings(src);
            var lang = Path.Combine(src, "Lang");
            Directory.CreateDirectory(lang);
            var sorted = texts.OrderBy(s => s, StringComparer.Ordinal).ToList();
            var json = JsonSerializer.Serialize(sorted.ToDictionary(s => s, s => s),
                new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            File.WriteAllText(Path.Combine(lang, "en.json"), json + "\n", new UTF8Encoding(false));
            File.WriteAllLines(Path.Combine(lang, "_context.tsv"),
                sorted.Select(s => Escape(s) + "\t" + string.Join(", ", where[s].OrderBy(x => x))), new UTF8Encoding(false));
            File.WriteAllText(log, $"{DateTime.Now} {sorted.Count} texts → {lang}\\en.json\n");
        }
        catch (Exception ex) { File.WriteAllText(log, "FAILED: " + ex); }
        Shutdown();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");

    private static string? FindSourceDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && d != null; i++, d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "EasyEngine.csproj"))) return d.FullName;
        return null;
    }

    // a C# string literal right after L.T( / L.F( (escapes allowed); interpolated $"…" is never collected
    private static readonly Regex CodeText = new(@"\bL\.[TF]\(\s*""((?:[^""\\\r\n]|\\.)*)""", RegexOptions.Compiled);
    private static readonly Regex XamlText = new(@"\b(?:Text|Content|Header|ToolTip|Title)=""([^""{][^""]*)""", RegexOptions.Compiled);

    /// <summary>Every English text of the editor found in <paramref name="src"/>, and the files/places it comes from.</summary>
    public static (HashSet<string> Texts, Dictionary<string, HashSet<string>> Where) CollectStrings(string src)
    {
        var texts = new HashSet<string>(StringComparer.Ordinal);
        var where = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Add(string? s, string from)
        {
            if (string.IsNullOrWhiteSpace(s) || !s.Any(char.IsLetter)) return;
            s = s.Trim();
            texts.Add(s);
            if (!where.TryGetValue(s, out var set)) where[s] = set = new();
            set.Add(from);
        }

        foreach (var file in Directory.GetFiles(src, "*.cs"))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("FullTest") || name.StartsWith("E2ETests") || name.StartsWith("App.SelfTest") || name == "App.Strings.cs") continue;
            foreach (Match m in CodeText.Matches(File.ReadAllText(file))) Add(Unescape(m.Groups[1].Value), name);
        }
        foreach (var file in Directory.GetFiles(src, "*.xaml"))
        {
            var name = Path.GetFileName(file);
            if (name == "PlayWindow.xaml") continue; // the game's own window is never translated
            foreach (Match m in XamlText.Matches(File.ReadAllText(file))) Add(WebUtility.HtmlDecode(m.Groups[1].Value), name);
        }

        static string? LabelOf(string option) => option.Contains('|') ? option[(option.IndexOf('|') + 1)..] : option;
        foreach (var b in BehaviorCatalog.All.Where(b => !b.Hidden))
        {
            var at = "behavior " + b.Kind;
            Add(b.Name, at); Add(b.Desc, at);
            foreach (var n in b.Nums) { Add(n.Label, at); foreach (var o in n.Options ?? Array.Empty<string>()) Add(LabelOf(o), at); }
            foreach (var t in b.Texts) { Add(t.Label, at); foreach (var o in t.Options ?? Array.Empty<string>()) Add(LabelOf(o), at); }
        }
        foreach (var (cat, _) in StepCatalog.Categories) Add(cat, "step category");
        foreach (var s in StepCatalog.Boss.Concat(StepCatalog.Cutscene).Distinct())
        {
            var at = "step " + s.Id;
            Add(s.Label, at); Add(s.Hint, at);
            foreach (var f in s.Fields) { Add(f.Label, at); foreach (var o in f.Options ?? Array.Empty<string>()) Add(LabelOf(o), at); }
        }
        return (texts, where);
    }

    private static string Unescape(string s)
    {
        if (!s.Contains('\\')) return s;
        try { return Regex.Unescape(s); } catch { return s; }
    }
}
