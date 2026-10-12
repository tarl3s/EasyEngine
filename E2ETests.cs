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
            foreach (var (title, run) in new (string, Func<List<(string, bool, List<string>)>>)[] { ("Axe Test", RunAxeE2E), ("Lighting Test", RunLightingE2E) })
            {
                sb.AppendLine($"===== {title} =====");
                foreach (var (step, ok, lines) in run())
                {
                    if (ok) pass++; else fail++;
                    sb.AppendLine((ok ? "PASS" : "FAIL") + "  " + step);
                    foreach (var l in lines) sb.AppendLine("          " + l);
                }
                sb.AppendLine();
            }
        }
        catch (Exception ex) { sb.AppendLine("FATAL: " + ex); fail++; }
        try { foreach (var dir in Directory.GetDirectories(Path.GetTempPath(), "EE_FullE2E_*")) try { Directory.Delete(dir, true); } catch { } } catch { }
        sb.Insert(0, $"e2e — test scenarios — {DateTime.Now}\n\n");
        sb.AppendLine($"\n{pass}/{pass + fail} steps passed" + (fail == 0 ? "  — ALL PASS" : $"  — {fail} FAILED"));
        try { File.WriteAllText(log, sb.ToString()); } catch { }
        Shutdown();
    }

    /// <summary>Plays the "Lighting Test" scene headless: sky colour, daylight, N → night, M → morning, the cycle on its own.</summary>
    internal static List<(string step, bool ok, List<string> lines)> RunLightingE2E()
    {
        var results = new List<(string, bool, List<string>)>();
        var tmp = Path.Combine(Path.GetTempPath(), "EE_FullE2E_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(tmp);
        var project = ProjectIO.Load(TestScenarios.Build(tmp));
        var pw = new PlayWindow(project, "Lighting Test");
        try
        {
            pw.TLoad();
            pw.TSteps(10);
            long seq = 0;
            void Step(string name, Func<bool> check)
            {
                bool ok; string err = "";
                try { ok = check(); } catch (Exception ex) { ok = false; err = ex.GetType().Name + ": " + ex.Message; }
                var lines = pw.TLog.Entries.Where(e => e.Seq > seq).Select(e => $"{e.TimeText,7} {e.Line}{(e.Count > 1 ? "  ×" + e.Count : "")}").ToList();
                lines.Add($"   state: darkness {pw.TDarkness:0.00} · glow {pw.TGlow:0.00} · holes {pw.TLightHoles} · night {pw.TVar("isNight")} · sky {pw.TSkyColor} · dark {pw.TDarkColor}");
                if (err.Length > 0) lines.Add("EXCEPTION " + err);
                seq = pw.TLog.LastSeq;
                results.Add((name, ok, lines));
            }
            void Key(string k) { pw.TKey(k, true); pw.TStep(); pw.TKey(k, false); pw.TSteps(3); pw.TUpdateLighting(); }

            Step("The sky is the scene's blue (no more black sky)", () => pw.TSkyColor == "#FF8EC5F0");

            Step("It starts in FULL daylight: no darkness and no glow on the player", () =>
            {
                pw.TUpdateLighting();
                return pw.TVar("isDay") == 1 && pw.TVar("dayness") >= 0.99 && pw.TDarkness < 0.01 && pw.TGlow < 0.01 && pw.TVisible("Sun") && !pw.TVisible("Moon");
            });

            Step("Each light (torch + lamp) and the sun cut their own hole in the darkness", () => pw.TLightHoles == 3);

            Step("The sun and moon live in the Sky layer, behind every other layer", () => pw.TInSky("Sun") && pw.TInSky("Moon"));

            Step("Just before night falls, the sun has already left the screen completely", () =>
            {
                pw.TSetTimeNow((19.8 / 35).ToString(System.Globalization.CultureInfo.InvariantCulture)); pw.TStep();
                return pw.TVar("isDay") == 1 && (pw.TX("Sun") >= 960 || pw.TX("Sun") + pw.TW("Sun") <= 0);
            });

            Step("When night starts, the moon is still off-screen (it rises from the left)", () =>
            {
                pw.TSetTimeNow((20.1 / 35).ToString(System.Globalization.CultureInfo.InvariantCulture)); pw.TStep();
                return pw.TVar("isNight") == 1 && pw.TX("Moon") + pw.TW("Moon") <= 0;
            });

            Step("N jumps to night: dark-blue darkness, moon up, sun gone, the lights glow", () =>
            {
                Key("N");
                return pw.TVar("isNight") == 1 && pw.TDarkness >= 0.8 && pw.TDarkColor == "#FF0A1030" && pw.TGlow > 0.3
                       && pw.TVisible("Moon") && !pw.TVisible("Sun") && pw.TLogHas("Night falls") && pw.TVar("dn:nights") == 1;
            });

            Step("M brings the morning back: no darkness, no glow, sun up", () =>
            {
                Key("M");
                return pw.TVar("isDay") == 1 && pw.TDarkness < 0.01 && pw.TGlow < 0.01 && pw.TVisible("Sun") && !pw.TVisible("Moon") && pw.TVar("dn:days") == 1;
            });

            Step("Left alone, the cycle reaches night again by itself (~20 s of day)", () =>
            {
                pw.TSteps(1300); pw.TUpdateLighting();
                return pw.TVar("dn:nights") >= 2;
            });

            Step("Pressing Y completes the checklist (ALL PASSED)", () => { Key("Y"); pw.TStep(); return pw.TVar("check:all") == 1 && pw.TLogHas("ALL PASSED"); });
        }
        finally { try { pw.Close(); } catch { } }
        return results;
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

            Step("The sword (key 3) works like any item: click near the dummy → hit, arm swings", () =>
            {
                Key("D3");
                pw.TPlace("Player", 290, 446); pw.TSteps(10);           // dummy is at x 230..262
                pw.TClick(246, 470); pw.TSteps(25);
                return pw.THeld == "sword" && pw.TVar("stat:enemyHits") == 1 && pw.TLogHas("Hit by the sword");
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
