using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace EasyEngine;

// M2 in real windows: the inspector's "Also show" marker rows (who ▾ + colour + ✕, "＋ Add"), and a playing minimap
// whose big map opens with its key and is really drawn in the middle of the window.
public partial class App
{
    partial void SelfTestMinimap(SelfTestContext c)
    {
        var dir = Path.Combine(Path.GetTempPath(), "EE_SelfTest_mini_" + System.Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(dir);
        var project = ProjectIO.CreateNew("MinimapTest", dir);
        var scene = project.GetStartScene();
        var layer = scene.Layers.First(l => l.Kind == LayerKind.Base);
        int id = 900;
        void Obj(string name, double x, double y, string group = "") =>
            layer.Objects.Add(new GameObjectData { Id = id++, Name = name, Group = group, Type = "Object", X = x, Y = y, Width = 24, Height = 24, Color = "#888888", Physics = new PhysicsData { Mode = PhysicsMode.None } });
        Obj("Hero", 300, 300); Obj("Slime", 500, 300); Obj("Coin", 700, 200); Obj("Bat1", 900, 250, "Bats");
        var mm = BehaviorCatalog.Info(BehaviorKind.Minimap).CreateData();
        mm.Texts["player"] = "Hero";
        mm.Texts["markers"] = "Slime:#9B59F0,Bats:#5BC77A,Coin";
        var map = new GameObjectData { Id = id++, Name = "Map", Type = "Object", Width = 40, Height = 40, Physics = new PhysicsData { Mode = PhysicsMode.None }, Behaviors = { mm } };
        layer.Objects.Add(map);

        // the inspector: one row per marker (a dropdown each) and the "＋ Add" button
        var editor = new MainWindow(project);
        editor.Show();
        c.CloseLater.Add(editor);
        editor.TestInspect(map);
        var nodes = new List<object>();
        void Walk(object n) { nodes.Add(n); if (n is DependencyObject d) foreach (var ch in LogicalTreeHelper.GetChildren(d)) Walk(ch); }
        Walk(editor.InspectorRoot);
        var whoBoxes = nodes.OfType<ComboBox>().Where(cb => cb.ToolTip as string == L.T("What to show on the map (an object, every copy of a prefab, or a whole group)")).ToList();
        var picked = whoBoxes.Select(cb => (cb.SelectedItem as ComboBoxItem)?.Tag as string).ToList();
        bool addBtn = nodes.OfType<Button>().Any(b => b.Content as string == L.T("＋ Add"));
        var swatches = nodes.OfType<Border>().Count(b => b.ToolTip as string == L.T("Colour of its dot on the map"));
        c.Write($"minimap inspector: {whoBoxes.Count} marker rows [{string.Join(", ", picked)}], {swatches} colour swatches, add button {addBtn}");
        if (whoBoxes.Count != 3 || !picked.SequenceEqual(new[] { "Slime", "Bats", "Coin" }) || swatches != 3 || !addBtn)
            c.LateChecks.Add(() => "the minimap's 'Also show' rows did not render as expected");

        // playing: press M → the big map opens (and stays drawn while the game runs)
        var play = new PlayWindow(project, scene.Name) { Owner = c.Editor };
        play.Show();
        c.CloseLater.Add(play);
        play.TKey("M", true); play.TStep(); play.TKey("M", false); play.TStep();
        c.LateChecks.Add(() =>
        {
            var size = play.TMinimapShownSize();
            c.Snapshot(play, "big_map");
            c.Write($"minimap big map: open {play.TMinimapMax()}, drawn {size.Width:0}x{size.Height:0}, dots purple {play.TMinimapDots("#9B59F0")} green {play.TMinimapDots("#5BC77A")} gray {play.TMinimapDots("#B4B4BE")} cyan {play.TMinimapDots("#00E5FF")}");
            if (!play.TMinimapMax() || !play.TMinimapTitleVisible() || !play.TUiHas("the map")) return "the big map did not open with its key";
            if (size.Width < 300 || size.Height < 200) return $"the big map is not drawn big ({size.Width:0}x{size.Height:0})";
            if (play.TMinimapDots("#9B59F0") != 1 || play.TMinimapDots("#5BC77A") != 1 || play.TMinimapDots("#B4B4BE") != 1 || play.TMinimapDots("#00E5FF") != 1)
                return "the big map's dots don't have their marker colours";
            return null;
        });
    }
}
