using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media;

namespace EasyEngine;

// M7 in a real window: the "Infinite World" project plays live (rendering, streaming while the player walks right):
// no console errors, chunks loaded, the terrain drawn behind the trees and the player, a reasonable frame time.
public partial class App
{
    partial void SelfTestTerrain(SelfTestContext c)
    {
        var dir = Path.Combine(Path.GetTempPath(), "EE_SelfTest_inf_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(dir);
        var proj = ProjectIO.Load(InfiniteWorld.Build(dir));
        proj.ConsoleOnStart = false;
        var play = new PlayWindow(proj, proj.StartScene!) { Owner = c.Editor };
        play.Show();
        c.CloseLater.Add(play);
        play.TKey("D", true); // walk right while the loop runs: chunks stream in live

        int frames = 0;
        var sw = Stopwatch.StartNew();
        void OnFrame(object? s, EventArgs e) => frames++;
        CompositionTarget.Rendering += OnFrame;

        c.LateChecks.Add(() =>
        {
            CompositionTarget.Rendering -= OnFrame;
            double appMs = sw.Elapsed.TotalMilliseconds / Math.Max(1, frames);
            double frameMs = play.TMeasureFrames(120); // this window's own frames (logic + render + layout), still walking right
            play.TKey("D", false);
            c.Snapshot(play, "infinite_world");
            int layer = play.TTerrainLayerIndex, player = play.TWorldIndexOf("Player"), tree = play.TWorldIndexOfTree();
            c.Write($"infinite world (real window): {play.TTerrainChunks} chunks, {play.TTerrainVisuals} blocks drawn · World index: terrain {layer}, tree {tree}, player {player} · " +
                    $"walked {play.TX("Player") - 400:0} px · {frameMs:0.00} ms per frame (CPU) · streaming {play.TTerrainMs:0.00} ms avg, {play.TTerrainMsMax:0.0} ms worst · " +
                    $"app loop {appMs:0} ms/frame with every selftest window open");
            if (play.TLog.Errors > 0) return "infinite world: console error — " + play.TLog.Entries.First(e => e.Level == LogLevel.Error).FullText();
            if (play.TTerrainChunks < 6 || play.TTerrainVisuals < 500) return "infinite world: the terrain didn't load around the player";
            if (layer != 0 || player <= 0 || (tree != -1 && (tree <= layer || tree >= player))) return "infinite world: the terrain isn't drawn behind the trees and the player";
            if (frameMs > 16 || play.TTerrainMsMax > 50) return $"infinite world: too slow ({frameMs:0.0} ms per frame, streaming worst {play.TTerrainMsMax:0} ms)";
            return null;
        });
    }
}
