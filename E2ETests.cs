using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace EasyEngine;

// End-to-end tests: build the real test-scenario project and play it headless, step by step, the way a
// person would (keys, clicks, walking up to things). Each step records the console lines it produced.
// Run with:  EasyEngine.exe --e2e   → writes easyengine_e2e.log   (also part of --fulltest)
public partial class App
{
    private void RunE2E()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var log = Path.Combine(Path.GetTempPath(), "easyengine_e2e.log");
        var sb = new StringBuilder();
        int pass = 0, fail = 0;
        try
        {
            foreach (var (step, ok, lines) in RunAxeE2E())
            {
                if (ok) pass++; else fail++;
                sb.AppendLine((ok ? "PASS" : "FAIL") + "  " + step);
                foreach (var l in lines) sb.AppendLine("          " + l);
            }
        }
        catch (Exception ex) { sb.AppendLine("FATAL: " + ex); fail++; }
        try { foreach (var dir in Directory.GetDirectories(Path.GetTempPath(), "EE_FullE2E_*")) try { Directory.Delete(dir, true); } catch { } } catch { }
        sb.Insert(0, $"e2e — Axe Test scenario — {DateTime.Now}\n\n");
        sb.AppendLine($"\n{pass}/{pass + fail} steps passed" + (fail == 0 ? "  — ALL PASS" : $"  — {fail} FAILED"));
        try { File.WriteAllText(log, sb.ToString()); } catch { }
        Shutdown();
    }

    /// <summary>Plays the "Axe Test" scene headless. Returns one (step, ok, console lines) per step.</summary>
    internal static List<(string step, bool ok, List<string> lines)> RunAxeE2E()
    {
        var results = new List<(string, bool, List<string>)>();
        var tmp = Path.Combine(Path.GetTempPath(), "EE_FullE2E_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(tmp);
        var project = ProjectIO.Load(TestScenarios.Build(tmp));
        var pw = new PlayWindow(project, "Axe Test");
        try
        {
            pw.TLoad();
            pw.TSteps(30); // settle: the player lands, the hotbar puts the pickaxe in hand
            long seq = 0;
            void Step(string name, Func<bool> check)
            {
                bool ok;
                string err = "";
                try { ok = check(); } catch (Exception ex) { ok = false; err = ex.GetType().Name + ": " + ex.Message; }
                var lines = pw.TLog.Entries.Where(e => e.Seq > seq).Select(e => $"{e.TimeText,7} {e.Line}{(e.Count > 1 ? "  ×" + e.Count : "")}").ToList();
                if (err.Length > 0) lines.Add("EXCEPTION " + err);
                seq = pw.TLog.LastSeq;
                results.Add((name, ok, lines));
            }
            void Key(string k) { pw.TKey(k, true); pw.TStep(); pw.TKey(k, false); pw.TStep(); }
            int Count(string sound) => pw.TSounds.Count(s => s == sound);

            Step("Starts with the pickaxe in hand", () => pw.THeld == "pickaxe");

            Step("Key 2 picks the axe (logged: In hand: Axe)", () => { Key("D2"); return pw.THeld == "axe" && pw.TLogHas("In hand: Axe"); });

            Step("A click far from the trees swings (whoosh) but misses (no chop)", () =>
            {
                pw.TClick(260, 250); pw.TSteps(30);
                return pw.TVar("stat:swings") == 1 && pw.TVar("stat:missed") == 1 && pw.TVar("stat:hits") == 0
                       && Count("swing.wav") == 1 && Count("chop.wav") == 0 && pw.TLogHas("Nothing in reach");
            });

            Step("Clicking a hotbar slot selects it and does NOT swing", () =>
            {
                var c = pw.THotbarSlotCenter(0);
                pw.TClick(c.X, c.Y); pw.TSteps(25);
                bool ok = pw.THeld == "pickaxe" && pw.TVar("stat:swings") == 1;
                Key("D2"); // back to the axe
                return ok && pw.THeld == "axe";
            });

            Step("Next to Tree1, 4 clicks fell it: 4 hits, +5 wood, 4 chops then the fall sound", () =>
            {
                pw.TPlace("Player", 366, 446); pw.TSteps(10);
                for (int k = 0; k < 4; k++) { pw.TClick(468, 400); pw.TSteps(25); }
                var s = pw.TSounds.ToList();
                return pw.TVar("stat:hits") == 4 && pw.TFelled("Tree1") && Math.Abs(pw.TItem("wood") - 5) < 0.01
                       && Count("chop.wav") == 4 && s.LastIndexOf("fall.wav") > s.LastIndexOf("chop.wav") && pw.TLogHas("Tree1: FELLED");
            });

            Step("The felled tree is gone and can't be hit again", () =>
            {
                pw.TSteps(45);
                pw.TClick(468, 400); pw.TSteps(25);
                return pw.TDead("Tree1") && pw.TVar("stat:hits") == 4;
            });

            Step("The rock refuses the axe (warning + refused sound)", () =>
            {
                pw.TPlace("Player", 1326, 446); pw.TSteps(10);
                pw.TClick(1412, 476); pw.TSteps(25);
                return pw.TVar("stat:denied") == 1 && pw.TLogHas("needs a pickaxe") && Count("denied.wav") >= 1 && pw.TItem("stone") == 0;
            });

            Step("The pickaxe breaks the rock (3 hits → +3 stone)", () =>
            {
                Key("D1");
                for (int k = 0; k < 3; k++) { pw.TClick(1412, 476); pw.TSteps(25); }
                return pw.THeld == "pickaxe" && pw.TFelled("Rock") && Math.Abs(pw.TItem("stone") - 3) < 0.01;
            });

            Step("The on-screen checklist reaches ALL PASSED", () => { pw.TStep(); return pw.TVar("check:all") == 1 && pw.TLogHas("ALL PASSED"); });

            Step("R restarts the scene: counters back to zero, pickaxe in hand", () =>
            {
                Key("R"); pw.TSteps(3);
                return pw.TVar("stat:swings") == 0 && pw.TItem("wood") == 0 && pw.THeld == "pickaxe" && pw.TExists("Tree1");
            });
        }
        finally { try { pw.Close(); } catch { } }
        return results;
    }
}
