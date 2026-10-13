using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace EasyEngine;

// Every editor window in a real, rendered state — in whichever theme this run uses (--theme light): opens the ones no
// other check opens (launcher, settings, Custom-rule blocks, sprite editor, new item, input dialog, an inspector full of
// behavior cards), takes a picture of each, then measures every visible text of every editor window against the colour
// really behind it (ContrastAudit). Faint text fails the selftest and is listed in the log. The game window (PlayWindow)
// is the game's own look and the scene shows the game's colours, so both are left out.
public partial class App
{
    partial void SelfTestReadability(SelfTestContext c)
    {
        var dir = Path.Combine(Path.GetTempPath(), "EE_SelfTest_read_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(dir);
        var project = ProjectIO.Load(TopDownGame.Build(dir));
        var scene = project.GetStartScene();

        T Open<T>(T w) where T : Window
        {
            w.ShowActivated = false;
            w.Show(); w.UpdateLayout();
            c.CloseLater.Add(w);
            return w;
        }

        // an inspector with behavior cards (the Top-Down player: Hotbar, Use Item, Health…) — drawn with wide empty margins
        // and "Hitbox: fit to the drawing" on, so the PHYSICS rows and the scene's dashed hitbox outline are checked too
        var hero = scene.Layers.SelectMany(l => l.Objects).First(o => o.Name == "Player");
        hero.SpritePath = TestScenarios.HitboxHeroSprite(project); hero.Width = hero.Height = 64; hero.Physics.FitHitbox = true;
        var editor = Open(new MainWindow(project));
        editor.TestInspect(editor.TestFindObject("Player")!);
        // the editors other checks opened inspect something too: show their Inspector tab (the first editor keeps the AI tab)
        foreach (var w in Windows.OfType<MainWindow>().Where(w => w != c.Editor))
            w.TabInspector.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var launcher = Open(new LauncherWindow());
        var settings = Open(new SettingsWindow { Owner = editor });
        var blocks = Open(new BlockEditorWindow(project, scene,
            "[{\"when\":\"touch\",\"name\":\"Player\",\"do\":\"addvar\",\"var\":\"coins\",\"amount\":1},{\"when\":\"keypress\",\"key\":\"B\",\"cond\":\"coins >= 15\",\"do\":\"say\",\"text\":\"Boost!\"},{\"when\":\"iftext\",\"var\":\"playerName\",\"op\":\"is\",\"value\":\"Ana\",\"do\":\"settext\",\"var\":\"greet\",\"to\":\"Hi {playerName}!\",\"local\":true},{\"when\":\"start\",\"do\":\"setcolor\",\"var\":\"teamColor\",\"to\":\"#FF8800\"},{\"when\":\"keypress\",\"key\":\"Q\",\"do\":\"listadd\",\"var\":\"quests\",\"value\":\"Find the key\"},{\"when\":\"iflist\",\"var\":\"quests\",\"op\":\"countatleast\",\"value\":\"2\",\"do\":\"tint\",\"target\":\"self\",\"color\":\"{teamColor}\"}]", hero,
            (user, system) => System.Threading.Tasks.Task.FromResult("{\"rules\":[{\"when\":\"wait\",\"seconds\":5,\"do\":\"fade\"},{\"when\":\"party\",\"do\":\"confetti\"}]}")) { Owner = editor });
        // "✨ Ask the AI" with a canned answer: the status line and the added block are in the pictures and the audit
        _ = blocks.TestAskAi("fade out after 5 seconds");
        blocks.UpdateLayout();
        var sprite = Open(new SpriteEditorWindow(project, 16) { Owner = editor });
        var newItem = Open(new NewItemDialog(project) { Owner = editor });
        var input = Open(new InputDialog("Rename project", "New name:", "My Game") { Owner = editor });

        c.LateChecks.Add(() =>
        {
            foreach (var sv in Descendants(blocks).OfType<System.Windows.Controls.ScrollViewer>().Take(1)) { sv.ScrollToHome(); blocks.UpdateLayout(); }
            var shots = new List<(Window w, string name)>
            {
                (c.Editor, "editor"), (editor, "editor_inspector"), (launcher, "launcher"), (settings, "settings"),
                (blocks, "block_editor"), (sprite, "sprite_editor"), (newItem, "new_item"), (input, "input_dialog"),
            };
            foreach (var w in Windows.OfType<AnimationWindow>().Take(1)) shots.Add((w, "animation"));
            foreach (var w in Windows.OfType<VariablesWindow>().Take(1)) shots.Add((w, "variables"));
            foreach (var w in Windows.OfType<SequenceEditorWindow>().Take(1)) shots.Add((w, "advanced_scratch_cutscene"));
            foreach (var (w, name) in shots) c.Snapshot(w, name);
            // the inspector scrolled to PHYSICS ("Hitbox: fit to the drawing", "✂ Trim box to drawing")
            var physics = editor.InspectorRoot.Children.OfType<System.Windows.Controls.TextBlock>().FirstOrDefault(t => t.Text == L.T("PHYSICS"));
            if (physics != null) { editor.InspectorScroll.ScrollToVerticalOffset(physics.TransformToAncestor(editor.InspectorRoot).Transform(new Point()).Y); editor.UpdateLayout(); }
            c.Snapshot(editor, "editor_physics");
            // and the inspector scrolled down to its behavior cards
            var cards = editor.InspectorRoot.Children.OfType<System.Windows.Controls.TextBlock>().FirstOrDefault(t => t.Text == L.T("BEHAVIORS"));
            editor.InspectorScroll.ScrollToVerticalOffset(cards?.TransformToAncestor(editor.InspectorRoot).Transform(new Point()).Y ?? editor.InspectorScroll.ScrollableHeight / 2);
            editor.UpdateLayout();
            c.Snapshot(editor, "editor_behaviors");
            // and the blocks scrolled to the end (colour and list blocks); the readability check below then measures them
            foreach (var sv in Descendants(blocks).OfType<System.Windows.Controls.ScrollViewer>().Take(1)) { sv.ScrollToEnd(); blocks.UpdateLayout(); }
            c.Snapshot(blocks, "block_editor_end");
            // the player locked: the 🔒 button and its note stay readable, the other rows are read-only (dimmed), the
            // Objects list shows the 🔒 (MainWindow.LockZ.cs); the readability check below measures it too
            editor.TestClickLock();
            editor.InspectorScroll.ScrollToTop(); editor.UpdateLayout();
            c.Snapshot(editor, "editor_locked");
            editor.TestMarkClean(); // a throwaway project: closing it mustn't ask to save

            // every editor window open right now (the game windows are the game's own look)
            int texts = 0, faint = 0;
            foreach (var w in Windows.OfType<Window>().Where(w => w.IsVisible && w is not PlayWindow))
            {
                var found = ContrastAudit.Audit(w, d => d is FrameworkElement { Name: "SceneCanvas" }, out int measured);
                texts += measured; faint += found.Count;
                c.Write($"readability ({Theme.Current}) \"{w.Title}\": {measured} texts" + (found.Count > 0 ? $", {found.Count} too faint:" : " OK"));
                foreach (var f in found.Take(40)) c.Write("    " + f);
            }
            c.Write($"readability ({Theme.Current}): {texts} texts measured, {faint} too faint");
            return faint > 0 ? $"{faint} text(s) too faint to read in the {Theme.Current} theme (listed above)" : null;
        });
    }
}
