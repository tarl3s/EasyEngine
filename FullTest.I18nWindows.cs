using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace EasyEngine;

// Languages in the windows other than the main editor: what they show is translated, what they save stays English, and
// the person's own names are never translated.
public partial class App
{
    partial void FeatureTestsI18nWindows(FullTestKit t)
    {
        var dir = Path.Combine(Path.GetTempPath(), "EE_Full_I18n_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        var project = ProjectIO.CreateNew("I18n", dir);
        var scene = project.GetStartScene();

        // runs check with a fake table, always going back to English
        bool With(Dictionary<string, string> table, Func<bool> check)
        {
            L.UseTableForTests("pt-BR", table);
            try { return check(); }
            finally { L.UseTableForTests("en", null); }
        }

        t.Test("language (windows): the block editor shows translated blocks but saves the very same English rules", () =>
        {
            var js = J("[{'when':'keypress','key':'Space','cooldown':0.3,'do':'spawn','object':'self','at':''},{'when':'always','do':'breaktile','where':'below','cond':'coins >= 3'}]");
            var english = new BlockEditorWindow(project, scene, js).SerializeTest();
            var table = new Dictionary<string, string>
            {
                ["When a key is pressed"] = "Quando uma tecla é pressionada", ["(none)"] = "(nenhum)", ["below"] = "abaixo", ["near"] = "perto",
                ["Block {0}"] = "Bloco {0}", ["self"] = "ele mesmo", ["is at least"] = "é pelo menos", ["Space"] = "Espaço",
            };
            return With(table, () =>
            {
                var w = new BlockEditorWindow(project, scene, js);
                var texts = UiTexts(w);
                bool same = w.SerializeTest() == english;
                bool shown = texts.Contains("Quando uma tecla é pressionada") && texts.Contains("(nenhum)") && texts.Contains("abaixo")
                             && texts.Contains("Bloco 1") && texts.Contains("ele mesmo") && texts.Contains("é pelo menos")
                             && texts.Contains("Space") && !texts.Contains("Espaço"); // a key is a value, never translated
                // picking a translated option saves its English value
                var at = Descendants(w).OfType<ComboBox>().First(c => c.Items.OfType<ComboBoxItem>().Any(i => (string?)i.Tag == "near"));
                at.SelectedItem = at.Items.OfType<ComboBoxItem>().First(i => (string?)i.Tag == "near");
                bool picked = w.SerializeTest().Contains("\"at\":\"near\"");
                w.Close();
                return same && shown && picked;
            });
        });

        t.Test("language (windows): Advanced Scratch shows translated steps; cutscenes and the Boss Demo's boss round-trip unchanged", () =>
        {
            var cut = J("{'steps':[{'do':'say','text':'Hi','speaker':'K'},{'do':'camera','object':'self','time':1},{'do':'settime','time':'night'}]}");
            string Rt(SeqMode mode, string json) { var w = new SequenceEditorWindow(project, scene, mode, json); var r = w.SerializeTest(); w.Close(); return r; }
            var cutEn = Rt(SeqMode.Cutscene, cut);
            var bossEn = Rt(SeqMode.Boss, BossDemo.BossScriptJson());
            var say = StepCatalog.Find(SeqMode.Cutscene, "say")!.Label;
            var table = new Dictionary<string, string> { [say] = "DIZER", ["This object (self)"] = "Este objeto (self)", ["Add step"] = "Adicionar passo" };
            return With(table, () =>
            {
                var w = new SequenceEditorWindow(project, scene, SeqMode.Cutscene, cut);
                var texts = UiTexts(w);
                bool shown = texts.Any(s => s.EndsWith("DIZER")) && texts.Contains("Este objeto (self)") && texts.Contains("＋ Adicionar passo");
                bool same = w.SerializeTest() == cutEn && Rt(SeqMode.Boss, BossDemo.BossScriptJson()) == bossEn;
                w.Close();
                return shown && same;
            });
        });

        t.Test("language (windows): a dropdown translates its labels and (none) / ＋ New…, never the names, and keeps the values", () =>
        {
            var table = new Dictionary<string, string> { ["Alpha"] = "Alfa", ["Coin"] = "Moeda", ["(none)"] = "(nenhum)", ["New"] = "Novo" };
            return With(table, () =>
            {
                string? picked = null;
                var cb = Dropdown.Make(new[] { ("a", "Alpha"), ("Coin", "Coin") }, "", v => picked = v, "Name:");
                L.TranslateTree(cb);
                var items = cb.Items.Cast<ComboBoxItem>().ToList();
                bool shown = items.Select(i => i.Content as string).SequenceEqual(new[] { "Alfa", "Coin", "(nenhum)", "＋ Novo…" });
                bool values = items.Take(3).Select(i => i.Tag as string).SequenceEqual(new[] { "a", "Coin", "" });
                cb.SelectedItem = items[0];
                return shown && values && picked == "a";
            });
        });

        t.Test("language (windows): variable and animation names stay as the person wrote them; the window's own texts translate", () =>
        {
            project.Variables["Save"] = 1;
            var table = new Dictionary<string, string> { ["Save"] = "Salvar", ["Global"] = "Geral", ["No variables yet."] = "Nenhuma variável ainda." };
            return With(table, () =>
            {
                var vw = new VariablesWindow(project, scene, null);
                L.TranslateTree(vw);
                var texts = UiTexts(vw);
                vw.Close();
                project.Variables.Remove("Save");
                return texts.Contains("Save") && !texts.Contains("Salvar") && texts.Contains("GERAL") && texts.Contains("Nenhuma variável ainda.");
            });
        });

        t.Test("language (windows): the sprite editor's shape is picked by its translated name and drawn by its English id", () =>
            With(new Dictionary<string, string> { ["Heart"] = "Coração" }, () =>
            {
                var w = new SpriteEditorWindow(project, 16);
                var shapes = Descendants(w).OfType<ComboBox>().First(c => c.Items.OfType<ComboBoxItem>().Any(i => (string?)i.Tag == "Heart"));
                var heart = shapes.Items.OfType<ComboBoxItem>().First(i => (string?)i.Tag == "Heart");
                shapes.SelectedItem = heart;
                bool ok = (string)heart.Content == "Coração" && w.TestShape == "Heart";
                w.Close();
                return ok;
            }));

        t.Test("language (windows): key pickers show translated key names but keep the key values (the game's labels stay English)", () =>
            With(new Dictionary<string, string> { ["Space"] = "Espaço", ["Up arrow"] = "Seta para cima" }, () =>
            {
                var keys = ParamSources.KeyOptions();
                return keys.Contains(("Space", "Espaço")) && keys.Contains(("Up", "↑ Seta para cima")) && keys.Contains(("A", "A"))
                       && ParamSources.Keys.Contains(("Space", "Space")) && keys.Count == ParamSources.Keys.Length;
            }));

        TestScenarios.TryDeleteDir(dir);
    }

    // every text a window shows (text blocks, button/checkbox/item contents, headers, tooltips, plain combo items)
    private static List<string> UiTexts(DependencyObject root)
    {
        var list = new List<string>();
        var stack = new Stack<object>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var o = stack.Pop();
            switch (o)
            {
                case string s: list.Add(s); continue;
                case TextBlock tb: list.Add(tb.Text); break;
                case HeaderedItemsControl h when h.Header is string hs: list.Add(hs); break;
                case ContentControl cc when cc.Content is string cs: list.Add(cs); break;
            }
            if (o is FrameworkElement fe && fe.ToolTip is string tip) list.Add(tip);
            if (o is DependencyObject d) foreach (var c in LogicalTreeHelper.GetChildren(d)) stack.Push(c);
        }
        return list;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var c in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }
}
