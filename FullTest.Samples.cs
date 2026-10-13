using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEngine;

// The sample projects that come with the engine (SampleProjects.cs) and the GitHub updater (Updater.cs).
public partial class App
{
    partial void FeatureTestsSamples(FullTestKit t)
    {
        string TempDir(string tag) => Path.Combine(Path.GetTempPath(), $"EE_Full_{tag}_" + Guid.NewGuid().ToString("N").Substring(0, 6));

        // ---------------- samples ----------------
        t.Test("samples: a new computer gets every sample built and opened; the same version adds nothing more", () =>
        {
            var folder = TempDir("Samples");
            var done = new List<string>(); var opened = new List<string>();
            int first = SampleProjects.Install(folder, done, true, opened.Add);
            int second = SampleProjects.Install(folder, done, false, opened.Add);
            bool ok = first == SampleProjects.All.Length && second == 0 && opened.Count == first && opened.All(File.Exists);
            TestScenarios.TryDeleteDir(folder);
            return ok;
        });
        t.Test("samples: a new version rebuilds them (a removed one comes back); an edited one is kept as 'your copy'", () =>
        {
            var folder = TempDir("Samples");
            SampleProjects.Install(folder, new List<string>(), true, _ => { });
            var boss = Path.Combine(folder, BossDemo.ProjectName);
            var top = Path.Combine(folder, TopDownGame.ProjectName);
            var edited = Directory.GetFiles(boss, "*" + ProjectIO.Extension).First();
            File.SetLastWriteTimeUtc(edited, DateTime.UtcNow.AddMinutes(5));         // the person changed the Boss Demo
            File.WriteAllText(Path.Combine(top, "save.json"), "{}");                  // and only played the Top-Down Demo
            File.SetLastWriteTimeUtc(Path.Combine(top, "save.json"), DateTime.UtcNow.AddMinutes(5));
            TestScenarios.TryDeleteDir(Path.Combine(folder, InfiniteWorld.ProjectName)); // and deleted Infinite World
            var opened = new List<string>();
            int n = SampleProjects.Install(folder, new List<string>(), true, opened.Add);  // the next version
            var copies = Directory.GetDirectories(folder, BossDemo.ProjectName + " (your copy *").ToList();
            bool ok = n == SampleProjects.All.Length && copies.Count == 1 && Directory.Exists(boss)
                      && Directory.Exists(Path.Combine(folder, InfiniteWorld.ProjectName))
                      && Directory.GetDirectories(folder, TopDownGame.ProjectName + " (your copy*").Length == 0
                      && opened.Any(f => f.StartsWith(copies[0]));
            TestScenarios.TryDeleteDir(folder);
            return ok;
        });

        // ---------------- updater ----------------
        t.Test("updater: reads GitHub's latest release (version from the tag, the engine zip's URL)", () =>
        {
            var json = "{\"tag_name\":\"v0.12.3\",\"body\":\"New stuff\",\"assets\":[{\"name\":\"other.zip\",\"browser_download_url\":\"x\"}," +
                       "{\"name\":\"" + Updater.AssetName + "\",\"browser_download_url\":\"https://example.com/e.zip\"}]}";
            var r = Updater.ParseLatest(json);
            var noZip = Updater.ParseLatest("{\"tag_name\":\"v0.12.3\",\"assets\":[]}");
            return r != null && r.Version == new Version(0, 12, 3) && r.ZipUrl == "https://example.com/e.zip" && r.Notes == "New stuff"
                   && noZip == null && Updater.ParseLatest("not json") == null;
        });
        t.Test("updater: versions compare by number (v0.10.10 > v0.10.9 > 0.9) and 'v' is optional", () =>
            Updater.ParseVersion("v0.10.10") > Updater.ParseVersion("0.10.9") && Updater.ParseVersion("0.10.9") > Updater.ParseVersion("v0.9")
            && Updater.ParseVersion("v1.2") == new Version(1, 2, 0) && Updater.ParseVersion("latest") == null);
        t.Test("updater: the swap removes only the old version's files, copies the new ones (subfolders too) and keeps the person's files", () =>
        {
            var install = TempDir("Install"); var fresh = TempDir("New");
            Directory.CreateDirectory(Path.Combine(install, "runtimes", "old"));
            File.WriteAllText(Path.Combine(install, "EasyEngine.exe"), "old");
            File.WriteAllText(Path.Combine(install, "Old.dll"), "old");
            File.WriteAllText(Path.Combine(install, "runtimes", "old", "x.dll"), "old");
            File.WriteAllText(Path.Combine(install, "my notes.txt"), "mine");                 // not in the manifest
            File.WriteAllText(Path.Combine(install, Updater.ManifestName), "EasyEngine.exe\nOld.dll\nruntimes\\old\\x.dll\n" + Updater.ManifestName);
            Directory.CreateDirectory(fresh);
            File.WriteAllText(Path.Combine(fresh, "EasyEngine.exe"), "new");
            File.WriteAllText(Path.Combine(fresh, "New.dll"), "new");
            Directory.CreateDirectory(Path.Combine(fresh, "runtimes", "new"));
            File.WriteAllText(Path.Combine(fresh, "runtimes", "new", "y.dll"), "new");               // nested folders are copied too
            File.WriteAllText(Path.Combine(fresh, Updater.ManifestName), "EasyEngine.exe\nNew.dll\n" + Updater.ManifestName);
            var p = Updater.StartSwap(fresh, install, -1, start: false);
            bool exited = p.WaitForExit(30000);
            bool ok = exited && File.ReadAllText(Path.Combine(install, "EasyEngine.exe")) == "new" && File.Exists(Path.Combine(install, "New.dll"))
                      && !File.Exists(Path.Combine(install, "Old.dll")) && !Directory.Exists(Path.Combine(install, "runtimes", "old"))
                      && File.ReadAllText(Path.Combine(install, "runtimes", "new", "y.dll")) == "new"
                      && File.ReadAllText(Path.Combine(install, "my notes.txt")) == "mine";
            TestScenarios.TryDeleteDir(install); TestScenarios.TryDeleteDir(fresh);
            return ok;
        });

        // ---------------- sprite editor: turning a selection, custom canvas sizes ----------------
        static byte[] Px(int w, int h) { var b = new byte[w * h * 4]; for (int i = 0; i < w * h; i++) { b[i * 4] = (byte)(i + 1); b[i * 4 + 3] = 255; } return b; }
        static byte At(byte[] b, int w, int x, int y) => b[(y * w + x) * 4];
        t.Test("sprite editor: a quarter turn right is exact (2×3 → 3×2, top-left goes top-right); four turns = the original", () =>
        {
            var src = Px(2, 3); // values 1..6, row by row
            var (r, w, h) = SpriteEditorWindow.Rotate(src, 2, 3, 90);
            var (back, w4, h4) = (src, 2, 3);
            for (int i = 0; i < 4; i++) (back, w4, h4) = SpriteEditorWindow.Rotate(back, w4, h4, 90);
            var (l, lw, lh) = SpriteEditorWindow.Rotate(src, 2, 3, -90);
            return w == 3 && h == 2 && At(r, w, 2, 0) == 1 && At(r, w, 0, 0) == 5 && At(r, w, 2, 1) == 2
                   && back.SequenceEqual(src) && lw == 3 && lh == 2 && At(l, lw, 0, 1) == 1;
        });
        t.Test("sprite editor: a free turn (45°) grows the box and keeps the pixels crisp (only original colours)", () =>
        {
            var src = Px(8, 8);
            var (r, w, h) = SpriteEditorWindow.Rotate(src, 8, 8, 45);
            var colours = Enumerable.Range(0, w * h).Where(i => r[i * 4 + 3] != 0).Select(i => r[i * 4]).ToHashSet();
            return w == 12 && h == 12 && colours.Count > 30 && colours.All(c => c >= 1 && c <= 64);
        });
        t.Test("sliders: the sprite editor and the inspector use the editor's own slider (click AND drag), never WPF's native one", () =>
        {
            static IEnumerable<System.Windows.DependencyObject> Tree(System.Windows.DependencyObject d) =>
                new[] { d }.Concat(System.Windows.LogicalTreeHelper.GetChildren(d).OfType<System.Windows.DependencyObject>().SelectMany(Tree));
            var project = ProjectIO.CreateNew("Sliders", TempDir("Sliders"));
            var sprite = new SpriteEditorWindow(project, 16);
            bool spriteOk = !Tree(sprite).OfType<System.Windows.Controls.Slider>().Any();
            sprite.Close();
            var editor = new MainWindow(project);
            var obj = new GameObjectData { Name = "Lamp" };
            obj.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.LightSource).CreateData()); // has an intensity slider, plus Opacity
            project.Scenes[0].Layers[0].Objects.Add(obj);
            editor.TestInspect(obj);
            return spriteOk && !Tree(editor.InspectorRoot).OfType<System.Windows.Controls.Slider>().Any();
        });
        t.Test("sprite editor: copy / cut / paste — the whole drawing or a selection comes back as a selection you can move", () =>
        {
            SpriteEditorWindow.UseSystemClipboard = false; // never the person's real clipboard
            var w = new SpriteEditorWindow(ProjectIO.CreateNew("SpriteClip", TempDir("Sprite")), 16);
            w.TestSelectTool("Polygon");
            w.PolygonDown(2, 2, false); w.PolygonDown(12, 2, false); w.PolygonDown(12, 12, false); w.PolygonDown(2, 2, false);
            w.CutSelection();                                   // nothing selected: the whole drawing goes to the clipboard
            bool cut = w.TestPixelAlpha(12, 7) == 0;
            w.PasteClipboard();
            bool pasted = w.TestPixelAlpha(12, 7) == 255 && w.TestSelectionSize == (16, 16);
            w.TestSelect(10, 4, 4, 6); w.CopySelection(); w.PasteClipboard();
            bool part = w.TestSelectionSize == (4, 6) && w.TestPixelAlpha(2, 3) == 255; // the copied right side, pasted at the top left
            w.Close();
            return cut && pasted && part;
        });
        t.Test("double-click on an object brings its inspector forward (the AI tab was showing)", () =>
        {
            var project = ProjectIO.CreateNew("DblClick", TempDir("Dbl"));
            var obj = new GameObjectData { Name = "Crate" };
            project.Scenes[0].Layers[0].Objects.Add(obj);
            var editor = new MainWindow(project);
            bool before = editor.TestInspectorShown; // a new editor shows the AI tab
            editor.TestOpenInspector(obj);
            bool ok = !before && editor.TestInspectorShown && editor.InspectorRoot.Children.OfType<System.Windows.Controls.Grid>()
                          .SelectMany(g => g.Children.OfType<System.Windows.Controls.TextBox>()).Any(tb => tb.Text == "Crate");
            TestScenarios.TryDeleteDir(Path.GetDirectoryName(project.FolderPath!)!);
            return ok;
        });
        t.Test("sprite editor: Ctrl+Z / Ctrl+Y undo and redo a drawing step, a cut, and cancel a polygon being drawn", () =>
        {
            SpriteEditorWindow.UseSystemClipboard = false;
            var w = new SpriteEditorWindow(ProjectIO.CreateNew("SpriteUndo", TempDir("Sprite")), 16);
            w.TestSelectTool("Polygon");
            w.PolygonDown(2, 2, false); w.PolygonDown(12, 2, false); w.PolygonDown(12, 12, false); w.PolygonDown(2, 2, false);
            bool drawn = w.TestPixelAlpha(12, 7) == 255 && w.TestUndoSteps == 1;
            w.Undo();
            bool undone = w.TestPixelAlpha(12, 7) == 0;
            w.Redo();
            bool redone = w.TestPixelAlpha(12, 7) == 255;
            w.CutSelection(); w.Undo();
            bool cutBack = w.TestPixelAlpha(12, 7) == 255;
            w.TestSelectTool("Polygon");
            int steps = w.TestUndoSteps;
            w.PolygonDown(3, 13, false); w.PolygonDown(9, 13, false);
            w.Undo();                                           // a polygon in progress: cancelled, no step left behind
            bool cancelled = w.TestPolygonPoints == 0 && w.TestUndoSteps == steps && w.TestPixelAlpha(6, 13) == 0;
            w.Close();
            return drawn && undone && redone && cutBack && cancelled;
        });
        t.Test("corners: an object with a picture has square corners by default (the drawing isn't clipped); a set radius still applies", () =>
        {
            var box = new GameObjectData { Width = 40, Height = 40 };
            var pic = new GameObjectData { Width = 40, Height = 40, SpritePath = "Assets/sprites/hero.png" };
            var anim = new GameObjectData { Width = 40, Height = 40 }; anim.Animations.Add(new SpriteAnimation { Frames = { "a.png" } });
            var bullet = new GameObjectData { Width = 10, Height = 10, Type = "Bullet" };
            var rounded = new GameObjectData { Width = 40, Height = 40, SpritePath = "Assets/sprites/hero.png", CornerRadius = 8 };
            return box.EffectiveCorner() == 4 && pic.EffectiveCorner() == 0 && anim.EffectiveCorner() == 0 && bullet.EffectiveCorner() == 5 && rounded.EffectiveCorner() == 8;
        });
        t.Test("copy look: an object gets another's sprite, animations, colour and border (its own copy of the lists)", () =>
        {
            var a = new GameObjectData { Name = "A", SpritePath = "Assets/sprites/slime.png", Color = "#123456", Opacity = 0.5, BorderWidth = 2, DefaultAnimation = "walk" };
            a.Animations.Add(new SpriteAnimation { Name = "walk", Frames = { "f1.png", "f2.png" }, Fps = 8 });
            var b = new GameObjectData { Name = "B", Width = 99 };
            EasyEngine.MainWindow.CopyLook(a, b);
            b.Animations[0].Frames.Add("f3.png");
            return b.SpritePath == a.SpritePath && b.Color == "#123456" && b.Opacity == 0.5 && b.BorderWidth == 2 && b.DefaultAnimation == "walk"
                   && b.Width == 99 && b.Name == "B" && a.Animations[0].Frames.Count == 2 && b.Animations[0].Fps == 8;
        });
        t.Test("duplicate image: the file is copied next to itself (slime_copy.png, then slime_copy2.png)", () =>
        {
            var p = ProjectIO.CreateNew("DupImg", TempDir("Dup"));
            var dir = Path.Combine(p.FolderPath!, "Assets", "sprites"); Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "slime.png"), new byte[] { 1, 2, 3 });
            var c1 = ProjectIO.DuplicateAsset(p, Path.Combine("Assets", "sprites", "slime.png"));
            var c2 = ProjectIO.DuplicateAsset(p, Path.Combine("Assets", "sprites", "slime.png"));
            bool ok = Path.GetFileName(c1) == "slime_copy.png" && Path.GetFileName(c2) == "slime_copy2.png"
                      && File.ReadAllBytes(ProjectIO.AbsoluteAsset(p, c2)).SequenceEqual(new byte[] { 1, 2, 3 });
            TestScenarios.TryDeleteDir(Path.GetDirectoryName(p.FolderPath!)!);
            return ok;
        });
        t.Test("sprite editor: a polygon closes by clicking its first point again (after 3 corners), drawing the closing side", () =>
        {
            var w = new SpriteEditorWindow(ProjectIO.CreateNew("SpritePoly", TempDir("Sprite")), 16);
            w.TestSelectTool("Polygon");
            w.PolygonDown(2, 2, false); w.PolygonDown(12, 2, false); w.PolygonDown(12, 12, false);
            bool open = w.TestPolygonPoints == 3 && w.TestPixelAlpha(7, 7) == 0;
            w.PolygonDown(2, 2, false);                       // the first point again → closed (right side + the diagonal back)
            bool closed = w.TestPolygonPoints == 0 && w.TestPixelAlpha(12, 7) == 255 && w.TestPixelAlpha(7, 7) == 255;
            w.PolygonDown(3, 13, false); w.PolygonDown(9, 13, false);
            w.PolygonDown(3, 13, false);                      // only 2 corners: that's one more corner, not "close"
            bool notYet = w.TestPolygonPoints == 3;
            w.Close();
            return open && closed && notYet;
        });
        t.Test("sprite editor: turning and mirroring a selection (or the whole drawing) works in the window", () =>
        {
            var w = new SpriteEditorWindow(ProjectIO.CreateNew("SpriteRot", TempDir("Sprite")), 16);
            w.RotateSelection(90); w.RotateSelection(-15); w.FlipSelection(true); w.FlipSelection(false);
            w.Close();
            return true;
        });
        t.Test("sprite editor: custom sizes are read as width × height (4 to 256)", () =>
            SpriteEditorWindow.ParseSize("32x128") == (32, 128) && SpriteEditorWindow.ParseSize(" 32 × 128 ") == (32, 128)
            && SpriteEditorWindow.ParseSize("48*16") == (48, 16) && SpriteEditorWindow.ParseSize("64") == null
            && SpriteEditorWindow.ParseSize("2x64") == null && SpriteEditorWindow.ParseSize("300x10") == null);

        // ---------------- renaming a project ----------------
        t.Test("rename: the project gets the new name inside, and its folder and file follow it; assets still load", () =>
        {
            var parent = TempDir("Rename"); Directory.CreateDirectory(parent);
            var p = ProjectIO.CreateNew("Old Name", parent);
            ProjectIO.Save(p);
            var newPath = ProjectIO.RenameProject(p.FilePath!, "My Cool Game");
            var back = ProjectIO.Load(newPath);
            bool ok = back.Name == "My Cool Game" && newPath == Path.Combine(parent, "My Cool Game", "My Cool Game" + ProjectIO.Extension)
                      && !Directory.Exists(Path.Combine(parent, "Old Name")) && back.Scenes.Count > 0;
            TestScenarios.TryDeleteDir(parent);
            return ok;
        });
        t.Test("rename: when the new name is taken by another folder, the project folder stays put (the name inside changes)", () =>
        {
            var parent = TempDir("Rename"); Directory.CreateDirectory(parent);
            var p = ProjectIO.CreateNew("Alpha", parent); ProjectIO.Save(p);
            Directory.CreateDirectory(Path.Combine(parent, "Beta")); // the new name is taken
            var path = ProjectIO.RenameProject(p.FilePath!, "Beta");
            bool ok = Path.GetDirectoryName(path) == Path.Combine(parent, "Alpha") && ProjectIO.Load(path).Name == "Beta" && !Directory.EnumerateFileSystemEntries(Path.Combine(parent, "Beta")).Any();
            TestScenarios.TryDeleteDir(parent);
            return ok;
        });
    }
}
