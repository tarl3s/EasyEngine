using System;
using System.IO;
using System.Linq;

namespace EasyEngine;

// A behavior package's card in a real editor window (MainWindow.Packages.cs): an object carrying the test "Gadget" package
// (one setting of every type: spinner, on/off, text, sound, picture, colour, key, object, scene, path, animation, list,
// choice, and a "show if" one shown) next to Twirl — rendered to a snapshot and, through SelfTestReadability (it measures
// every editor window), checked for contrast in this run's theme.
public partial class App
{
    partial void SelfTestPackages(SelfTestContext c)
    {
        var dir = Path.Combine(Path.GetTempPath(), "EE_SelfTest_pkg_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(dir);
        var proj = ProjectIO.CreateNew("Packages", dir);
        TestPackages.Write(Path.Combine(proj.FolderPath!, "behaviors"), ("Gadget.behavior", TestPackages.Gadget()), ("Twirl.behavior", TestPackages.Twirl()));
        var scene = proj.GetStartScene();
        var thing = new GameObjectData { Id = 500, Name = "Thing", X = 300, Y = 300, Width = 48, Height = 48, Color = "#4DC8E5" };
        thing.Behaviors.Add(TestPackages.Use("Twirl"));
        var gadget = BehaviorPackages.CreateData(BehaviorPackages.For(proj).Find("Gadget")!);
        gadget.Texts["mode"] = "wild"; // the "show if" setting shows
        gadget.Texts["route"] = "[[300,300],[400,300]]"; gadget.Texts["steps"] = "[1,2,3]";
        thing.Behaviors.Add(gadget);
        scene.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(thing);
        ProjectIO.Save(proj);

        var ed = new MainWindow(proj) { ShowActivated = false };
        ed.Show(); c.CloseLater.Add(ed);
        ed.TestSelect(ed.TestFindObject("Thing"));
        ed.TabInspector.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var rows = ed.TestPackageRows(thing, gadget);
        c.Write($"package card: {rows.Count} settings shown [{string.Join(", ", rows.Keys)}]");
        if (rows.Count != 15) c.LateChecks.Add(() => $"packages: the Gadget card shows {rows.Count} settings, expected 15");
        c.LateChecks.Add(() =>
        {
            ed.InspectorScroll.ScrollToEnd(); ed.UpdateLayout();
            c.Snapshot(ed, "packages_inspector");
            return ed.TestCardTitles().Any(t => t.Contains("Gadget")) && ed.TestCardTitles().Any(t => t.Contains("Twirl")) ? null : "packages: the package cards aren't in the inspector";
        });
    }
}
