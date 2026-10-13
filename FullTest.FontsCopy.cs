using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyEngine;

// Fonts: every text behavior (Label, Interact / Trigger Area / UI Button messages, Cutscene) picks its font — the game's,
// a Windows font, or a font file added to the project (Assets\fonts, shown by its family name); old projects look the same.
// Ctrl+C / Ctrl+V: the selected objects are copied and pasted into the active layer (new ids and names, a little further
// each time, one undo step, refused in a locked layer or scene, works in another scene); text fields keep their own copy.
public partial class App
{
    partial void FeatureTestsFontsCopy(FullTestKit t)
    {
        // a real font file to stand in for a "custom" one (the test checks it is read from the project, not from Windows)
        string? WinFont() => new[] { "consola.ttf", "arial.ttf", "verdana.ttf", "georgia.ttf" }
            .Select(f => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), f)).FirstOrDefault(File.Exists);
        string TempDir(string tag) => Path.Combine(Path.GetTempPath(), "EE_Full_Fonts " + tag + " Área_" + Guid.NewGuid().ToString("N")[..6]); // spaces + accent, like a Desktop in Portuguese
        static bool SameFile(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        // copies the stand-in into a project's Assets\fonts under another name, as "＋ Add a font file…" does
        string AddFont(Project p, string name = "MyFont.ttf")
        {
            var dir = Path.Combine(p.FolderPath!, "Assets", GameFonts.Folder); Directory.CreateDirectory(dir);
            File.Copy(WinFont()!, Path.Combine(dir, name), true);
            return Path.Combine("Assets", GameFonts.Folder, name);
        }
        GameObjectData LabelObj(string font = "", string? objFont = null)
        {
            var o = new GameObjectData { Id = 50, Name = "Title", X = 20, Y = 20, Width = 200, Height = 40, FontFamily = objFont };
            var lb = BehaviorCatalog.Info(BehaviorKind.Label).CreateData(); lb.Texts["text"] = "Hello";
            if (font.Length > 0) lb.Texts["font"] = font; else lb.Texts.Remove("font"); // an old project has no "font" key
            o.Behaviors.Add(lb);
            return o;
        }

        // ---------------- the picker
        t.Test("fonts: every text behavior has a Font picker (Label, Interact/Area/UI Button messages, Cutscene), empty = the game's font", () =>
            new[] { BehaviorKind.Label, BehaviorKind.Interact, BehaviorKind.Area, BehaviorKind.UIButton, BehaviorKind.Cutscene }
                .All(k => BehaviorCatalog.Info(k).Texts.Any(p => p.Key == "font" && p.Pick == PickKind.Font && p.Default == "")));

        t.Test("fonts: the picker offers the game's font, the project's font files by family name, Windows fonts and ＋ Add a font file…", () =>
        {
            if (WinFont() is not string src) return false;
            var p = ProjectIO.CreateNew("Fonts", TempDir("pick"));
            var ed = new MainWindow(p);
            try
            {
                var added = ed.TestAddFontFile(src);
                var family = GameFonts.FamilyOf(src);
                var obj = LabelObj(); p.GetStartScene().Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(obj);
                var pickers = ed.TestFontPickers(obj);
                if (added is not (string rel, string label) || pickers.Count != 1) return false;
                var items = pickers[0].items;
                bool bad = ed.TestAddFontFile(p.FilePath!) == null; // not a font: refused
                return family != null && label == family && rel.StartsWith(Path.Combine("Assets", "fonts") + Path.DirectorySeparatorChar)
                       && File.Exists(Path.Combine(p.FolderPath!, rel))
                       && items[0] == ("", L.T("The game's font")) && items.Contains((rel, family)) && items.Any(i => i.value == "Consolas")
                       && items[^1].label == L.T("＋ Add a font file…") && pickers[0].selected == "" && bad
                       && !GameFonts.ProjectFonts(p).Any(f => f.EndsWith(".eeproj"));
            }
            finally { try { ed.Close(); } catch { } }
        });

        t.Test("fonts: a project font file resolves to its own family, read from that file (a folder with spaces and accents)", () =>
        {
            if (WinFont() is null) return false;
            var p = ProjectIO.CreateNew("Fonts", TempDir("file"));
            var rel = AddFont(p, "My Pixel Font.ttf");
            var ff = GameFonts.Make(p, rel, out var problem);
            File.WriteAllText(Path.Combine(p.FolderPath!, "Assets", "fonts", "Broken.ttf"), "not a font");
            var broken = GameFonts.Make(p, Path.Combine("Assets", "fonts", "Broken.ttf"), out var why);
            var missing = GameFonts.Make(p, Path.Combine("Assets", "fonts", "Gone.ttf"), out var why2);
            return ff != null && problem == null && SameFile(GameFonts.FileOf(ff), Path.Combine(p.FolderPath!, rel))
                   && GameFonts.Label(p, rel) == GameFonts.FamilyOf(Path.Combine(p.FolderPath!, rel))
                   && broken == null && why != null && missing == null && why2 != null
                   && GameFonts.Make(p, "", out _) == null && GameFonts.Make(p, "Consolas", out _)?.Source == "Consolas";
        });

        // ---------------- in the game
        t.Test("fonts: a Label with a font file draws with it in the game; a missing file falls back to the game's font with a console warning", () =>
        {
            if (WinFont() is null) return false;
            string rel = "";
            var pw = PlayWindow.NewScriptSim("[]", out var p, extra: (proj, s) =>
            {
                rel = AddFont(proj);
                var layer = s.Layers.First(l => l.Kind == LayerKind.Base);
                layer.Objects.Add(LabelObj(rel));
                var gone = LabelObj(Path.Combine("Assets", "fonts", "Gone.ttf")); gone.Id = 51; gone.Name = "Gone"; layer.Objects.Add(gone);
            });
            t.Sims.Add(pw);
            pw.TSteps(2);
            var f = pw.TLabelFont("Title"); var g = pw.TLabelFont("Gone");
            return f != null && SameFile(GameFonts.FileOf(f), Path.Combine(p.FolderPath!, rel))
                   && g?.Source == (p.Theme?.FontFamily ?? "Segoe UI") && pw.TLogCount("Font", "Gone.ttf") == 1;
        });

        t.Test("fonts: an old Label (font set on the object) looks the same, its card shows that font, and picking another moves it to the card", () =>
        {
            var pw = PlayWindow.NewScriptSim("[]", out _, extra: (proj, s) => s.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(LabelObj(objFont: "Georgia")));
            t.Sims.Add(pw); pw.TSteps(2);
            bool same = pw.TLabelFont("Title")?.Source == "Georgia";
            var p = ProjectIO.CreateNew("Fonts", TempDir("old"));
            var obj = LabelObj(objFont: "Georgia"); p.GetStartScene().Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(obj);
            var ed = new MainWindow(p);
            try
            {
                var shown = ed.TestFontPickers(obj).FirstOrDefault().selected;
                bool picked = ed.TestPickFont(obj, "Consolas");
                var lb = obj.Behaviors[0];
                return same && shown == "Georgia" && picked && lb.Text("font") == "Consolas" && obj.FontFamily == null
                       && GameFonts.LabelFont(lb, obj) == "Consolas";
            }
            finally { try { ed.Close(); } catch { } }
        });

        t.Test("fonts: an Interact's message shows in a project font file; the next plain message is back to the game's font", () =>
        {
            if (WinFont() is null) return false;
            string rel = "";
            var pw = PlayWindow.NewScriptSim("[]", out var p, extra: (proj, s) =>
            {
                rel = AddFont(proj);
                var layer = s.Layers.First(l => l.Kind == LayerKind.Base);
                var player = new GameObjectData { Id = 3, Name = "Player", Type = "Player", X = 400, Y = 236, Width = 48, Height = 64 };
                player.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.TopDown).CreateData());
                layer.Objects.Add(player);
                var npc = new GameObjectData { Id = 4, Name = "NPC", X = 440, Y = 236, Width = 40, Height = 64 };
                var it = BehaviorCatalog.Info(BehaviorKind.Interact).CreateData();
                it.Texts["text"] = "Hi!"; it.Texts["font"] = rel;
                npc.Behaviors.Add(it); layer.Objects.Add(npc);
            });
            t.Sims.Add(pw); pw.TSteps(5);
            pw.TKey("E", true); pw.TSteps(3); pw.TKey("E", false); pw.TSteps(3);
            bool open = pw.TDialog; var font = pw.TDialogFontFamily;
            while (pw.TDialog) pw.TAdvanceDialog();
            pw.TSay("plain", "");
            return open && SameFile(GameFonts.FileOf(font), Path.Combine(p.FolderPath!, rel)) && pw.TDialogFont == (p.Theme?.FontFamily ?? "Segoe UI");
        });

        t.Test("fonts: a Cutscene's Font is used by its dialogue and its big title; a Cutscene without one looks as before", () =>
        {
            PlayWindow Cut(string font, string steps)
            {
                var pw = PlayWindow.NewScriptSim("[]", out _, extra: (proj, s) =>
                {
                    var o = new GameObjectData { Id = 9, Name = "Intro", X = 0, Y = 0, Width = 10, Height = 10 };
                    var c = BehaviorCatalog.Info(BehaviorKind.Cutscene).CreateData();
                    c.Texts["script"] = "{\"steps\":[" + steps + "]}"; c.Texts["font"] = font; c.Nums["bars"] = 0;
                    o.Behaviors.Add(c); s.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(o);
                });
                t.Sims.Add(pw); pw.TSteps(5);
                return pw;
            }
            var say = Cut("Consolas", "{\"do\":\"say\",\"text\":\"Long ago…\"}");
            bool sayFont = say.TDialog && say.TDialogFont == "Consolas";
            var title = Cut("Georgia", "{\"do\":\"title\",\"text\":\"CHAPTER 1\",\"time\":3}");
            var plain = Cut("", "{\"do\":\"title\",\"text\":\"CHAPTER 1\",\"time\":3}");
            return sayFont && title.TTitleFont.Source == "Georgia" && plain.TTitleFont.Source == "Segoe UI";
        });

        // ---------------- Ctrl+C / Ctrl+V
        (Project p, Layer layer, MainWindow ed) Editor(string tag)
        {
            var p = ProjectIO.CreateNew("Copy", Path.Combine(Path.GetTempPath(), "EE_Full_Copy_" + tag + "_" + Guid.NewGuid().ToString("N")[..6]));
            var layer = p.GetStartScene().Layers.First(l => l.Kind == LayerKind.Base);
            layer.Objects.Clear();
            layer.Objects.Add(new GameObjectData { Id = 70, Name = "Coin", X = 100, Y = 100, Width = 20, Height = 20, Behaviors = { BehaviorCatalog.Info(BehaviorKind.Collectible).CreateData() } });
            layer.Objects.Add(new GameObjectData { Id = 71, Name = "Rock", X = 200, Y = 150, Width = 40, Height = 40, Locked = true });
            layer.Objects.Add(new GameObjectData { Id = 72, Name = "Tree", X = 400, Y = 100, Width = 40, Height = 80 });
            EasyEngine.MainWindow.TestClearClipboard();
            return (p, layer, new MainWindow(p));
        }

        t.Test("copy/paste: two selected objects are pasted with new ids and names, 20 px further each paste, selected; one undo removes them", () =>
        {
            var (p, layer, ed) = Editor("A");
            try
            {
                var coin = ed.TestFindObject("Coin")!; var rock = ed.TestFindObject("Rock")!;
                ed.TestSelectMany(coin, rock);
                ed.TestCopy();
                coin.X = 999; // later changes to the original don't change what was copied
                ed.TestPaste();
                var all = ed.TestAllObjects();
                var c2 = all.FirstOrDefault(o => o.Name == "Coin_2"); var r2 = all.FirstOrDefault(o => o.Name == "Rock_2");
                var sel = ed.TestSelection;
                int ids = all.Select(o => o.Id).Distinct().Count();
                ed.TestPaste();
                var c3 = ed.TestAllObjects().FirstOrDefault(o => o.Name == "Coin_3");
                int afterTwo = ed.TestAllObjects().Count;
                ed.TestUndo();
                int afterUndo = ed.TestAllObjects().Count;
                return c2 != null && r2 != null && c2.X == 120 && c2.Y == 120 && r2.X == 220 && r2.Y == 170 && !r2.Locked
                       && c2.Behaviors.Count == 1 && c2.Behaviors[0].Kind == BehaviorKind.Collectible
                       && ids == all.Count && sel.Count == 2 && sel.Contains(c2) && sel.Contains(r2)
                       && c3 != null && c3.X == 140 && afterTwo == 7 && afterUndo == 5 && ed.TestFindObject("Coin_3") == null;
            }
            finally { try { ed.Close(); } catch { } }
        });

        t.Test("copy/paste: pasting into a locked layer or a locked scene is refused with a message", () =>
        {
            var (p, layer, ed) = Editor("B");
            try
            {
                ed.TestSelect(ed.TestFindObject("Tree")); ed.TestCopy();
                ed.TestSetLayerLocked(layer, true);
                ed.TestPaste();
                bool layerRefused = ed.TestAllObjects().Count == 3 && ed.StatusText.Text.Contains(layer.Name);
                ed.TestSetLayerLocked(layer, false);
                ed.TestToggleSceneLock();
                ed.TestPaste();
                bool sceneRefused = ed.TestAllObjects().Count == 3 && ed.StatusText.Text == L.T("This scene is locked — unlock it (🔒 next to the scene) to change it");
                ed.TestToggleSceneLock();
                ed.TestPaste();
                return layerRefused && sceneRefused && ed.TestAllObjects().Count == 4;
            }
            finally { try { ed.Close(); } catch { } }
        });

        t.Test("copy/paste: objects copied in one scene paste into another scene of the project", () =>
        {
            var (p, layer, ed) = Editor("C");
            try
            {
                var two = new Scene { Name = "Level 2", Layers = { new Layer { Name = "Ground", Kind = LayerKind.Base } } };
                p.Scenes.Add(two);
                ed.TestSelectMany(ed.TestFindObject("Coin")!, ed.TestFindObject("Tree")!); ed.TestCopy();
                ed.TestSwitchScene(two);
                ed.TestSetActiveLayer(two.Layers[0]);
                ed.TestPaste();
                var here = two.Layers[0].Objects;
                return ReferenceEquals(ed.TestScene, two) && here.Count == 2 && here.Any(o => o.Name == "Coin") && here.Any(o => o.Name == "Tree")
                       && here.All(o => o.Id >= 73) && layer.Objects.Count == 3;
            }
            finally { try { ed.Close(); } catch { } }
        });

        t.Test("copy/paste: a text field keeps its own Ctrl+C / Ctrl+V; the scene's right-click menu has Copy, Paste, Duplicate, Delete", () =>
        {
            bool keys = !EasyEngine.MainWindow.ClipboardKeysAreOurs(new TextBox()) && !EasyEngine.MainWindow.ClipboardKeysAreOurs(new RichTextBox()) && !EasyEngine.MainWindow.ClipboardKeysAreOurs(new PasswordBox())
                        && EasyEngine.MainWindow.ClipboardKeysAreOurs(new Button()) && EasyEngine.MainWindow.ClipboardKeysAreOurs(null);
            var (p, layer, ed) = Editor("D");
            try
            {
                ed.TestSelect(null);
                var empty = ed.TestObjectMenu();
                ed.TestSelect(ed.TestFindObject("Tree")); ed.TestCopy();
                var full = ed.TestObjectMenu();
                ed.TestPaste(new Point(600, 300));
                var at = ed.TestFindObject("Tree_2");
                return keys && empty.SequenceEqual(new[] { L.T("Copy") + " (off)", L.T("Paste") + " (off)", L.T("Duplicate") + " (off)", L.T("Delete") + " (off)" })
                       && full.SequenceEqual(new[] { L.T("Copy"), L.T("Paste"), L.T("Duplicate"), L.T("Delete") })
                       && at != null && at.X == 600 && at.Y == 300;
            }
            finally { try { ed.Close(); } catch { } }
        });

        DeleteTempFolders("EE_Full_Fonts *", "EE_Full_Copy_*");
    }
}
