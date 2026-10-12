using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace EasyEngine;

// --review: a visual tour for a person (or Claude) checking a release. Plays the Boss Demo and the Infinite World in
// real windows, steps them frame by frame through their key moments and saves a picture of each moment
// (%TEMP%\easyengine_snap_review_*.png), plus a log of what was on screen (%TEMP%\easyengine_review.log).
// Not part of the test suites — it only looks; the checks live in --fulltest / --e2e / --selftest.
public partial class App
{
    private void RunReview()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var log = Path.Combine(Path.GetTempPath(), "easyengine_review.log");
        var lines = new List<string> { $"review {DateTime.Now}" };
        var shots = new SelfTestContext { Write = s => lines.Add(s) };
        var windows = new List<Window>();
        var dirs = new List<string>();

        PlayWindow Open(Func<string, string> build, string tag)
        {
            var dir = Path.Combine(Path.GetTempPath(), $"EE_Review_{tag}_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(dir); dirs.Add(dir);
            var proj = ProjectIO.Load(build(dir));
            proj.ConsoleOnStart = false;
            var w = new PlayWindow(proj, proj.StartScene!) { Width = 1100, Height = 680 };
            w.Show(); windows.Add(w);
            w.UpdateLayout();
            return w;
        }
        void Shot(PlayWindow w, string name, int frames)
        {
            if (frames > 0) w.TMeasureFrames(frames);
            shots.Snapshot(w, "review_" + name);
            lines.Add($"  {name}: {w.TOverlays} · errors {w.TLog.Errors}");
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                // ---------------- Boss Demo ----------------
                var b = Open(BossDemo.Build, "boss");
                Shot(b, "boss_1_camera_to_boss", 90);
                Shot(b, "boss_2_title", 90);
                for (int i = 0; i < 40 && b.TCutsceneRunning("Intro"); i++) { b.TMeasureFrames(30); b.TAdvanceDialog(); b.TAdvanceDialog(); }
                Shot(b, "boss_3_fight", 90);
                lines.Add($"  phase now: {b.TBossPhase("SlimeKing")} · hp {b.THp("SlimeKing")}");
                b.TSetHp("SlimeKing", 20); // 20/36 ≈ 56 % → phase 2
                Shot(b, "boss_4_phase2", 150);
                lines.Add($"  phase now: {b.TBossPhase("SlimeKing")} · attack {b.TBossAttack("SlimeKing")}");
                b.TSetHp("SlimeKing", 8);  // ≈ 22 % → phase 3 (invulnerable, red, minions)
                Shot(b, "boss_5_phase3", 70);
                lines.Add($"  phase now: {b.TBossPhase("SlimeKing")} · minions {b.TAlive("Minion")}");
                b.TSetHp("SlimeKing", 0);
                Shot(b, "boss_6_dying", 60);
                for (int i = 0; i < 40 && b.TVar("bossDefeated") < 1; i++) { b.TMeasureFrames(20); b.TAdvanceDialog(); }
                Shot(b, "boss_7_victory", 100);
                lines.Add($"  bossDefeated {b.TVar("bossDefeated")} · gate open {b.TVar("gateOpen")}");

                // ---------------- Infinite World ----------------
                var w = Open(InfiniteWorld.Build, "inf");
                Shot(w, "inf_1_day", 30);
                w.TKey("D", true); w.TMeasureFrames(240); w.TKey("D", false);
                Shot(w, "inf_2_walked_right", 20);
                w.TSetTimeNow("night");
                Shot(w, "inf_3_night", 60);
                // underground: the first cave below the player's column, in daylight (the darkness comes from the cover)
                w.TSetTimeNow("noon");
                int col = w.TTerrainColumn(w.TX("Player")), surf = w.TTerrainSurfaceRow(col);
                for (int c = col; c < col + 400; c++)
                {
                    int s = w.TTerrainSurfaceRow(c), r = Enumerable.Range(s + 8, 30).FirstOrDefault(rr => w.TTerrainBlock(c, rr) == 0 && w.TTerrainBlock(c, rr - 1) == 0 && w.TTerrainBlock(c, rr + 1) != 0);
                    if (r <= 0) continue;
                    var p = w.TTerrainCellCenter(c, r);
                    w.TPlace("Player", p.X - 14, p.Y - 30);
                    lines.Add($"  cave at column {c}, row {r} ({r - s} blocks under the surface)");
                    break;
                }
                Shot(w, "inf_4_underground_noon", 120);
                lines.Add($"  cover {w.TVar("cover"):0.00} · underground {w.TVar("underground")}");
                w.TKey("M", true); w.TMeasureFrames(2); w.TKey("M", false);
                Shot(w, "inf_5_big_map", 10);
            }
            catch (Exception ex) { lines.Add("REVIEW ERROR: " + ex); }
            foreach (var win in windows) try { win.Close(); } catch { }
            foreach (var d in dirs) TestScenarios.TryDeleteDir(d);
            lines.Add("review done");
            try { File.WriteAllLines(log, lines); } catch { }
            Shutdown();
        }), DispatcherPriority.ApplicationIdle);
    }
}
