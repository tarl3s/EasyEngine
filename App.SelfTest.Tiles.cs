using System;
using System.IO;
using System.Linq;
using System.Windows.Media;

namespace EasyEngine;

// Tilemap tiles with pictures in real windows: the editor with the "Tiles Test" level selected (its inspector: the
// APPEARANCE without sprite rows, the palette swatches showing the brick picture and the water's first frame, the
// selected water tile's Draw / Choose image / Animate / ✕ buttons) and the level playing (bricks, moving water).
public partial class App
{
    partial void SelfTestTiles(SelfTestContext c)
    {
        var dir = Path.Combine(Path.GetTempPath(), "EE_SelfTest_tiles_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(dir);
        var proj = ProjectIO.Load(TestScenarios.Build(dir));
        proj.StartScene = "Tiles Test"; proj.ConsoleOnStart = false;

        var ed = new MainWindow(proj) { ShowActivated = false };
        ed.Show(); c.CloseLater.Add(ed);
        var map = ed.TestFindObject("Level")!;
        ed.TestActiveTile = 2; // the animated water tile
        ed.TestSelect(map);
        ed.TestRedraw(); // draw the map as the one being edited (grid + cells) — nothing changes, so closing asks nothing
        ed.TabInspector.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        bool swatches = ed.TestTileSwatchBackground(1) is ImageBrush && ed.TestTileSwatchBackground(2) is ImageBrush && ed.TestTileSwatchBackground(3) is SolidColorBrush;
        var labels = ed.TestAppearanceLabels().ToList();
        bool noSprite = !labels.Contains(L.T("Animations")) && !labels.Contains(L.T("Static image")) && labels.Contains(L.T("Opacity"));
        c.Write($"tiles inspector: picture swatches {swatches}, appearance rows [{string.Join(", ", labels)}]");
        if (!swatches || !noSprite) c.LateChecks.Add(() => "tiles: the tilemap inspector doesn't show the tile pictures / still shows sprite rows");

        var play = new PlayWindow(proj, "Tiles Test") { Owner = ed };
        play.Show(); c.CloseLater.Add(play);
        c.LateChecks.Add(() =>
        {
            ed.InspectorScroll.ScrollToEnd(); ed.UpdateLayout();
            c.Snapshot(ed, "tiles_inspector");
            play.TMeasureFrames(20);
            c.Snapshot(play, "tiles_play");
            c.Write($"tiles (real window): water frame [{string.Join(",", play.TTileAnimFrames())}], {play.TTiles("Level")} tiles");
            if (play.TLog.Errors > 0) return "tiles: console error — " + play.TLog.Entries.First(e => e.Level == LogLevel.Error).FullText();
            return play.TTileCellImage("Level", 10, 12) == null ? "tiles: the brick picture isn't drawn in play" : null;
        });
    }
}
