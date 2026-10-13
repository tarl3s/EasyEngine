using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEngine;

// E2E for the "Infinite World" project (M7): build it, play it headless like a person — start on the grass, walk a long
// way right, dig down with the pickaxe, chop a tree with the axe, wait for the night and fight the slimes.
public partial class App
{
    partial void AddE2EScenariosTerrain(List<(string, Func<List<(string, bool, List<string>)>>)> scenarios)
        => scenarios.Add(("Infinite World", RunInfiniteWorldE2E));

    internal static List<(string step, bool ok, List<string> lines)> RunInfiniteWorldE2E()
    {
        var results = new List<(string, bool, List<string>)>();
        var tmp = Path.Combine(Path.GetTempPath(), "EE_FullE2E_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(tmp);
        var project = ProjectIO.Load(InfiniteWorld.Build(tmp));
        // the saved project makes a new world every game; the test walks the same hills every run, with a tougher player
        // (the night slimes must not send it back to the start in the middle of a step)
        var objects = project.Scenes[0].Layers.SelectMany(l => l.Objects).ToList();
        objects.First(o => o.Name == "Land").Behaviors.First(b => b.Kind == BehaviorKind.Terrain).Nums["seed"] = 4242;
        objects.First(o => o.Name == "Player").Behaviors.First(b => b.Kind == BehaviorKind.Health).Nums["max"] = 100;
        var pw = new PlayWindow(project, "World");
        try
        {
            pw.TSeed(5); // the night slimes come out on a random side: seeded, so a slime that falls into the dug shaft can't make the run flaky
            pw.TLoad();
            pw.TSteps(30);
            long seq = 0;
            void Step(string name, Func<bool> check)
            {
                bool ok; string err = "";
                try { ok = check(); } catch (Exception ex) { ok = false; err = ex.GetType().Name + ": " + ex.Message; }
                var lines = pw.TLog.Entries.Where(e => e.Seq > seq).Select(e => $"{e.TimeText,7} {e.Line}{(e.Count > 1 ? "  ×" + e.Count : "")}").ToList();
                if (lines.Count > 14) lines = lines.Take(6).Append($"… {lines.Count - 12} more …").Concat(lines.Skip(lines.Count - 6)).ToList();
                var b = pw.TTerrainBox("Player");
                lines.Add($"   state: player ({b.X:0}, {b.Y:0}) · {pw.TTerrainChunks} chunks · {pw.TTerrainVisuals} blocks drawn · dirt {pw.TItem("dirt")} stone {pw.TItem("stone")} wood {pw.TItem("wood")} coins {pw.TVar("coins")} · slimes {pw.TAlive("Slime")}");
                if (err.Length > 0) lines.Add("EXCEPTION " + err);
                seq = pw.TLog.LastSeq;
                results.Add((name, ok, lines));
            }
            void Key(string k) { pw.TKey(k, true); pw.TStep(); pw.TKey(k, false); pw.TStep(); }
            void ClickWorld(double x, double y) { pw.TClick(x + pw.TCamX, y + pw.TCamY); pw.TSteps(25); } // the click is in screen coords
            double ts = pw.TTerrainTs;
            double Ground(System.Windows.Rect b) => Enumerable.Range(pw.TTerrainColumn(b.X), pw.TTerrainColumn(b.Right - 0.001) - pw.TTerrainColumn(b.X) + 1).Min(pw.TTerrainSurfaceRow) * ts;

            Step("The player starts standing on the grass, pickaxe in hand", () =>
            {
                var b = pw.TTerrainBox("Player");
                return pw.TGrounded("Player") && Math.Abs(b.Bottom - Ground(b)) < 0.5 && pw.THeld == "pickaxe" && pw.TTerrainChunks >= 6 && pw.TLogHas("World seed 4242");
            });

            double startX = pw.TX("Player");
            Step("Walking right a long way (hopping over the hills) builds the world ahead and drops it behind", () =>
            {
                int maxVis = 0, maxChunks = 0;
                pw.TKey("D", true);
                for (int i = 0; i < 1500; i++)
                {
                    if (i % 36 == 0) pw.TKey("Space", true); else if (i % 36 == 12) pw.TKey("Space", false);
                    pw.TStep();
                    maxVis = Math.Max(maxVis, pw.TTerrainVisuals); maxChunks = Math.Max(maxChunks, pw.TTerrainChunks);
                }
                pw.TClearKeys(); pw.TSteps(40);
                var b = pw.TTerrainBox("Player");
                return b.X - startX > 4000 && pw.TGrounded("Player") && maxVis < 4000 && maxChunks <= 35
                       && !pw.TTerrainChunkLoaded(0, 1) && pw.TLogHas("Streaming:");
            });

            Step("Digging straight down with the pickaxe: grass and dirt first (+dirt), then stone (+stone)", () =>
            {
                double y0 = pw.TY("Player");
                for (int i = 0; i < 80 && pw.TItem("stone") < 1; i++)
                {
                    var b = pw.TTerrainBox("Player");
                    int r = (int)Math.Floor((b.Bottom + 1) / ts), c0 = pw.TTerrainColumn(b.X), c1 = pw.TTerrainColumn(b.Right - 0.001);
                    int c = Enumerable.Range(c0, c1 - c0 + 1).FirstOrDefault(x => pw.TTerrainLive(x, r), int.MinValue);
                    if (c == int.MinValue) { pw.TSteps(20); continue; } // falling into the hole
                    var p = pw.TTerrainCellCenter(c, r);
                    ClickWorld(p.X, p.Y);
                }
                return pw.TItem("dirt") >= 3 && pw.TItem("stone") >= 1 && pw.TY("Player") - y0 >= 4 * ts && pw.TLogHas("Hit tile");
            });

            Step("Back on the grass, the axe (key 2) fells the nearest tree: +5 wood", () =>
            {
                Key("D2");
                var tree = pw.TTerrainTreeNear(pw.TX("Player"));
                double x = tree.X - 40;
                pw.TPlace("Player", x, Ground(new System.Windows.Rect(x, 0, 28, 46)) - 46); pw.TSteps(20);
                for (int k = 0; k < 4; k++) ClickWorld(tree.X + tree.Width / 2, tree.Bottom - 30);
                pw.TSteps(45);
                return pw.THeld == "axe" && pw.TItem("wood") >= 5 && pw.TLogHas("FELLED");
            });

            Step("Night falls: slimes come out near the player (never more than 4)", () =>
            {
                pw.TSetTimeNow("night");
                int most = 0; bool allNear = true;
                for (int i = 0; i < 20; i++)
                {
                    pw.TTerrainSnapCamera(); // Destroy Off-screen reads the camera, which headless runs never move
                    pw.TSteps(60);
                    most = Math.Max(most, pw.TAlive("Slime"));
                    allNear &= pw.TTerrainBoxes("Slime").All(s => Math.Abs(s.X - pw.TX("Player")) < 900);
                }
                return pw.TVar("isNight") == 1 && most >= 2 && most <= 4 && allNear;
            });

            Step("The sword (key 3) kills a slime, which drops a coin", () =>
            {
                Key("D3");
                for (int i = 0; i < 300 && !pw.TExists("Coin") && pw.TVar("coins") < 1; i++)
                {
                    pw.TTerrainSnapCamera();
                    var me = pw.TTerrainBox("Player");
                    var near = pw.TTerrainBoxes("Slime").OrderBy(s => Math.Abs(s.X - me.X)).FirstOrDefault();
                    if (!near.IsEmpty && Math.Abs(near.X + near.Width / 2 - (me.X + me.Width / 2)) < 90) ClickWorld(near.X + near.Width / 2, near.Y + near.Height / 2);
                    else pw.TSteps(5);
                }
                return pw.THeld == "sword" && (pw.TExists("Coin") || pw.TVar("coins") >= 1) && pw.TLogHas("Hit by the sword");
            });

            Step("No errors in the console", () => pw.TLog.Errors == 0);
        }
        finally { try { pw.Close(); } catch { } }
        return results;
    }
}
