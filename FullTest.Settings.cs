using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyEngine;

// Settings: languages (Loc.cs + Lang\*.json), themes (Theme.cs), clearing temporary files (CacheCleaner.cs).
public partial class App
{
    partial void FeatureTestsSettings(FullTestKit t)
    {
        // ---------------- languages ----------------
        var fake = new Dictionary<string, string> { ["Save"] = "Salvar", ["Open Project"] = "Abrir projeto", ["{0} objects"] = "{0} objetos", ["Broken {0}"] = "Quebrado {1}" };
        t.Test("language: a text is looked up whole, then without its icon and ending (💾 Save…, Open Project…, Panels ▾)", () =>
        {
            L.UseTableForTests("pt-BR", fake);
            bool ok = L.T("Save") == "Salvar" && L.T("💾 Save") == "💾 Salvar" && L.T("📂  Open Project…") == "📂  Abrir projeto…"
                      && L.T("Save:") == "Salvar:" && L.T("Unknown words") == "Unknown words" && L.T("") == "" && L.T("🎮") == "🎮";
            L.UseTableForTests("en", null);
            return ok && L.T("Save") == "Save";
        });
        t.Test("language: formats keep their numbers; a translation with wrong placeholders falls back to English", () =>
        {
            L.UseTableForTests("pt-BR", fake);
            bool ok = L.F("{0} objects", 3) == "3 objetos" && L.F("Broken {0}", 7) == "Broken 7";
            L.UseTableForTests("en", null);
            return ok;
        });
        t.Test("language: what loads is translated, the person's own names (marked Skip) are not, and English comes back", () =>
        {
            L.UseTableForTests("pt-BR", fake);
            var panel = new StackPanel();
            var button = new Button { Content = "Save", ToolTip = "Open Project" };
            var name = new TextBlock { Text = "Save" }; L.SetSkip(name, true);       // e.g. an object the person called "Save"
            var label = new TextBlock { Text = "Open Project" };
            panel.Children.Add(button); panel.Children.Add(name); panel.Children.Add(label);
            L.TranslateTree(panel);
            bool translated = (string)button.Content == "Salvar" && (string)button.ToolTip == "Abrir projeto" && label.Text == "Abrir projeto" && name.Text == "Save";
            L.UseTableForTests("en", null);
            L.TranslateTree(panel);
            return translated && (string)button.Content == "Save" && label.Text == "Open Project";
        });
        t.Test("language: every language has its table, covers the English texts and keeps the {0} placeholders", () =>
        {
            var en = L.LoadTable("en"); // the list of every English text (Lang\en.json)
            if (en.Count < 100) return false;
            var problems = new List<string>();
            foreach (var lang in L.All.Skip(1))
            {
                var table = L.LoadTable(lang.Code);
                int covered = en.Keys.Count(table.ContainsKey);
                if (covered < en.Count * 0.97) problems.Add($"{lang.Code}: {covered}/{en.Count}");
                foreach (var (k, v) in table)
                    if (!Placeholders(k).SetEquals(Placeholders(v))) problems.Add($"{lang.Code}: '{k}' → '{v}'");
            }
            SettingsCheckLog("languages", problems);
            return problems.Count == 0;
        });
        t.Test("language: the AI is told to answer in the chosen language (nothing added in English)", () =>
        {
            L.UseTableForTests("ja", fake);
            bool ok = L.AiReplyRule.Contains("Japanese");
            L.UseTableForTests("en", null);
            return ok && L.AiReplyRule == "";
        });

        // ---------------- themes ----------------
        t.Test("theme: the palette comes from Theme (tests run dark) and the styles use those very brushes", () =>
        {
            var dark = Theme.PaletteOf("dark");
            bool palette = dark.All(kv => Current.Resources[kv.Key] is SolidColorBrush b && b.Color == kv.Value) && !Theme.IsLight;
            // a style loaded after the palette (Styles.xaml) holds the same brush object as the resources
            var accentButton = Current.TryFindResource("AccentButton") as Style;
            var bg = accentButton?.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == Control.BackgroundProperty)?.Value;
            return palette && ReferenceEquals(bg, Current.Resources["Accent"]);
        });
        t.Test("theme: in both themes, text stays readable on every background (Text/TextDim/Danger on Bg0-3, hover and the selected row, text on the accent)", () =>
        {
            var low = new List<string>();
            foreach (var theme in Theme.Names)
            {
                var c = Theme.PaletteOf(theme);
                foreach (var bgKey in new[] { "Bg0", "Bg1", "Bg2", "Bg3", "Hover", "Selection" })
                    foreach (var fg in new[] { "Text", "TextDim", "Danger" })
                        if (Contrast.Ratio(c[fg], c[bgKey]) < 4.5) low.Add($"{theme}: {fg} on {bgKey}: {Contrast.Ratio(c[fg], c[bgKey]):0.0}");
                if (Contrast.Ratio(c["AccentText"], c["Accent"]) < 4.5) low.Add($"{theme}: AccentText on Accent");
                if (Contrast.Ratio(c["Accent"], c["Bg2"]) < 4.5) low.Add($"{theme}: Accent as text on Bg2");
            }
            SettingsCheckLog("themes", low);
            return low.Count == 0;
        });

        // ---------------- temporary files ----------------
        t.Test("temporary files: only EasyEngine's logs, test projects and downloads go — settings, projects and others' files stay", () =>
        {
            var temp = Path.Combine(Path.GetTempPath(), "EE_Full_Clean_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(temp);
            void F(string rel) { var p = Path.Combine(temp, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, "x"); }
            F("easyengine_fulltest.log"); F("easyengine_snap_editor.png"); F("EE_SelfTest_ab12cd/My.eeproj"); F("EasyEngine_publish/EasyEngine.exe");
            F("settings.json"); F("My Game/My Game.eeproj"); F("other_app.log"); F("EE_notes.txt"); F("EasyEngine Samples/Boss Demo/Boss Demo.eeproj");
            var (count, bytes) = CacheCleaner.Clean(temp);
            bool ok = count == 4 && bytes > 0
                      && !File.Exists(Path.Combine(temp, "easyengine_fulltest.log")) && !Directory.Exists(Path.Combine(temp, "EE_SelfTest_ab12cd"))
                      && File.Exists(Path.Combine(temp, "settings.json")) && File.Exists(Path.Combine(temp, "My Game", "My Game.eeproj"))
                      && File.Exists(Path.Combine(temp, "other_app.log")) && File.Exists(Path.Combine(temp, "EE_notes.txt"))
                      && File.Exists(Path.Combine(temp, "EasyEngine Samples", "Boss Demo", "Boss Demo.eeproj"));
            TestScenarios.TryDeleteDir(temp);
            return ok && CacheCleaner.Pretty(1536) == $"{1.5:0.#} KB";
        });
    }

    // details of a failed settings check (the fulltest log only has the check's name)
    private static void SettingsCheckLog(string what, List<string> problems)
    {
        if (problems.Count == 0) return;
        try { File.AppendAllLines(Path.Combine(Path.GetTempPath(), "easyengine_settings_check.log"), new[] { $"{DateTime.Now} {what}:" }.Concat(problems.Take(60).Select(p => "   " + p))); } catch { }
    }

    private static HashSet<string> Placeholders(string s) => Regex.Matches(s, @"\{\d+[^}]*\}").Select(m => m.Value).ToHashSet();
}
