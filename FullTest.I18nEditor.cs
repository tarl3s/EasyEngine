using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace EasyEngine;

// The editor window in other languages: its XAML and code-built texts are translated, the person's own names (objects,
// layers, scenes, project, prefabs) never are, a language switch rebuilds the panels, and nothing reads a shown text back.
public partial class App
{
    partial void FeatureTestsI18nEditor(FullTestKit t)
    {
        var temp = Path.Combine(Path.GetTempPath(), "EE_FullI18nEd_" + Guid.NewGuid().ToString("N").Substring(0, 6)); // cleaned with EE_Full*
        Directory.CreateDirectory(temp);
        // everything named like a UI word: an object "Save", a layer "Player", a scene "Delete", the project "Inspector"
        var project = ProjectIO.CreateNew("Inspector", temp);
        var scene = project.Scenes[0];
        var player = scene.Layers.SelectMany(l => l.Objects).First(o => o.Behaviors.Any(b => b.Kind == BehaviorKind.Platformer));
        player.Name = "Save";
        scene.Layers.First(l => l.Kind == LayerKind.Base).Name = "Player";
        scene.Name = project.StartScene = "Delete";
        var editor = new MainWindow(project); // headless (not shown)
        editor.TestSelect(player);

        var fake = new Dictionary<string, string>
        {
            ["Save"] = "Salvar", ["Player"] = "Jogador", ["Delete"] = "Excluir", ["Inspector"] = "Inspetor", ["Background"] = "Fundo",
            ["Build it"] = "Construir", ["OBJECT"] = "OBJETO", ["BEHAVIORS"] = "COMPORTAMENTOS", ["Name"] = "Nome", ["Grid: off"] = "Grade: desligada",
            ["Platformer Character"] = "Personagem de plataforma", ["Add behavior"] = "Adicionar comportamento", ["Prefabs"] = "Pré-fabs",
            ["Target: {0} (AI edits only this)."] = "Alvo: {0} (a IA só edita este).", ["Edited \"{0}\""] = "Editado: {0}",
            ["Created \"{0}\" [{1}]"] = "Criado: {0} [{1}]", ["shown"] = "visível",
        };
        void ToLanguage(Dictionary<string, string>? table)
        {
            L.UseTableForTests(table == null ? "en" : "pt-BR", table);
            L.TranslateTree(editor);       // what SetLanguage does to every open window…
            editor.TestLanguageChanged();  // …and then L.Changed (the code-built panels)
        }

        t.Test("editor language: toolbar, tabs and code-built texts are translated (Save → Salvar, Build it → Construir, Grid: off…)", () =>
        {
            try
            {
                ToLanguage(fake);
                var all = I18nTexts(editor);
                return (string)editor.TabInspector.Content == "Inspetor" && (string)editor.AiSend.Content == "Construir"
                       && (string)editor.GridToggle.Content == "Grade: desligada" && all.Contains("Salvar") && all.Contains("Excluir")
                       && editor.AiTarget.Text == "Alvo: Save (a IA só edita este)." && editor.TestPanelsMenu().Any(h => h.StartsWith("Pré-fabs"));
            }
            finally { ToLanguage(null); }
        });
        t.Test("editor language: the inspector translates its sections, labels, buttons and behavior names (catalog stays English)", () =>
        {
            try
            {
                ToLanguage(fake);
                var texts = I18nTexts(editor.InspectorRoot);
                return texts.Contains("OBJETO") && texts.Contains("Nome") && texts.Contains("Excluir") && texts.Contains("COMPORTAMENTOS")
                       && texts.Any(x => x.EndsWith("Personagem de plataforma")) && texts.Any(x => x.Contains("Adicionar comportamento"))
                       && BehaviorCatalog.Info(BehaviorKind.Platformer).Name == "Platformer Character";
            }
            finally { ToLanguage(null); }
        });
        t.Test("editor language: names the person chose keep their words (object \"Save\", layer \"Player\", scene \"Delete\", project \"Inspector\")", () =>
        {
            try
            {
                ToLanguage(fake);
                var layers = I18nTexts(editor.LayersList);
                var objects = I18nTexts(editor.ObjectsList);
                var scenes = I18nTexts(editor.SceneCombo);
                var prefabs = I18nTexts(editor.PaletteList);
                return layers.Contains("Player") && !layers.Contains("Jogador") && layers.Contains("Fundo") /* the layer KIND is UI */
                       && layers.Contains("Background") /* …the layer called Background is a name */ && layers.Contains("visível")
                       && objects.Any(x => x.StartsWith("Save ")) && scenes.Any(x => x.EndsWith("Delete")) && !scenes.Any(x => x.Contains("Excluir"))
                       && editor.ProjectNameText.Text == "Inspector" && prefabs.Any(x => x.EndsWith("Player")) && !prefabs.Any(x => x.Contains("Jogador"));
            }
            finally { ToLanguage(null); }
        });
        t.Test("editor language: switching back to English restores every text", () =>
        {
            ToLanguage(fake);
            ToLanguage(null);
            var all = I18nTexts(editor);
            var inspector = I18nTexts(editor.InspectorRoot);
            return (string)editor.TabInspector.Content == "Inspector" && (string)editor.AiSend.Content == "Build it" && (string)editor.GridToggle.Content == "Grid: off"
                   && inspector.Contains("OBJECT") && inspector.Contains("BEHAVIORS") && inspector.Any(x => x.EndsWith("Platformer Character"))
                   && !all.Any(x => x.Contains("Salvar") || x.Contains("Excluir") || x.Contains("Inspetor") || x.Contains("Fundo"));
        });
        t.Test("editor language: logic never reads shown text (inspector cards found, AI change objects, unsaved flag)", () =>
        {
            try
            {
                ToLanguage(fake);
                editor.TestSelect(null); // no selection: the AI creates and edits freely
                var (combos, _, _) = editor.TestInspect(player); // finds the BEHAVIORS section by the translated title
                var (ok, summary) = editor.TestApplySpec("{\"create\":[{\"name\":\"Coin\",\"behaviors\":[{\"kind\":\"Collectible\"}]}],\"edit\":[{\"name\":\"Save\",\"behaviors\":[{\"kind\":\"Health\"}]}]}");
                var touched = editor.TestAiSpecObjects.ToList();
                editor.TestMarkDirty();
                editor.Title = "renamed";
                return combos > 0 && ok && summary.Contains("Criado: Coin") && summary.Contains("Editado: Save")
                       && touched.Contains("Coin") && touched.Contains("Save") && editor.TestIsDirty;
            }
            finally { ToLanguage(null); }
        });

        TestScenarios.TryDeleteDir(temp);
    }

    // every string shown under root: TextBlock texts, string contents/headers/tooltips (logical tree)
    private static List<string> I18nTexts(DependencyObject root)
    {
        var list = new List<string>();
        void Walk(object node)
        {
            switch (node)
            {
                case TextBlock tb: list.Add(tb.Text); break;
                case HeaderedItemsControl h when h.Header is string s: list.Add(s); break;
                case ContentControl cc when cc.Content is string s: list.Add(s); break;
            }
            if (node is FrameworkElement fe && fe.ToolTip is string tip) list.Add(tip);
            if (node is DependencyObject d) foreach (var c in LogicalTreeHelper.GetChildren(d)) Walk(c);
        }
        Walk(root);
        return list;
    }
}
