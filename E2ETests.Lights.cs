using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEngine;

// E2E for the "Light Shapes Test" scene of the EasyEngine Tests project: a dark room, the player's flashlight (beam that
// follows where it faces), a lit window (area), a pulsing neon (bar), a wall lamp (half circle), a flickering torch and a
// blinking alarm. A walks left (the beam turns), D walks back right to the window, Y confirms — ALL PASSED; R restarts.
public partial class App
{
    partial void AddE2EScenariosLights(List<(string, Func<List<(string, bool, List<string>)>>)> scenarios)
        => scenarios.Add((TestScenarios.LightShapesScene, RunLightShapesE2E));

    internal static List<(string step, bool ok, List<string> lines)> RunLightShapesE2E()
    {
        var results = new List<(string, bool, List<string>)>();
        var tmp = Path.Combine(Path.GetTempPath(), "EE_FullE2E_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(tmp);
        var project = ProjectIO.Load(TestScenarios.Build(tmp));
        var pw = new PlayWindow(project, TestScenarios.LightShapesScene);
        try
        {
            pw.TLoad();
            pw.TSteps(20); pw.TUpdateLighting();
            long seq = 0;
            double PX() => pw.TX("Player") + 17;
            double PY() => pw.TY("Player") + 26;
            void Step(string name, Func<bool> check)
            {
                bool ok; string err = "";
                try { ok = check(); } catch (Exception ex) { ok = false; err = ex.GetType().Name + ": " + ex.Message; }
                var lines = pw.TLog.Entries.Where(e => e.Seq > seq).Select(e => $"{e.TimeText,7} {e.Line}{(e.Count > 1 ? "  ×" + e.Count : "")}").ToList();
                lines.Add($"   state: player ({PX():0},{PY():0}) · aim {pw.TLightAim("Player"):0} · holes {pw.TLightHoles} (shaped {pw.TShapedLights}) · darkness {pw.TDarkness:0.00} · checks {pw.TVar("check:done")}/{pw.TVar("check:total")}");
                if (err.Length > 0) lines.Add("EXCEPTION " + err);
                seq = pw.TLog.LastSeq;
                results.Add((name, ok, lines));
            }
            void Hold(string k, int frames) { pw.TKey(k, true); pw.TSteps(frames); pw.TKey(k, false); pw.TSteps(3); pw.TUpdateLighting(); }

            Step("The room is dark and each of the 6 lights cuts its own shaped hole (flashlight, window, neon, wall lamp, torch, alarm)", () =>
                pw.TDarkness >= 0.9 && pw.TLightHoles == 6 && pw.TShapedLights == 6
                && pw.TLightShape("Player") == "beam" && pw.TLightShape("Window") == "area" && pw.TLightShape("Neon") == "bar" && pw.TLightShape("WallLamp") == "half");

            Step("The flashlight lights in front of the player (right), not behind", () =>
                Math.Abs(pw.TLightAim("Player")) < 1e-6 && pw.TShapedLightAt(PX() + 120, PY()) > 0.8 && pw.TShapedLightAt(PX() - 120, PY()) < 0.05);

            Step("The window lights its whole rectangle; the wall lamp lights below it, not above", () =>
                pw.TShapedLightAt(645, 155) > 0.7 && pw.TShapedLightAt(765, 245) > 0.7 && pw.TShapedLightAt(1322, 360) > 0.7 && pw.TShapedLightAt(1322, 150) < 0.05);

            Step("A walks left: the flashlight turns with the player (checklist ticks)", () =>
            {
                Hold("A", 20);
                return Math.Abs(pw.TLightAim("Player") - 180) < 1e-6 && pw.TShapedLightAt(PX() - 120, PY()) > 0.8 && pw.TVar("wentLeft") == 1;
            });

            Step("D walks back right: the flashlight turns back, and the player reaches the lit window", () =>
            {
                Hold("D", 160);
                return Math.Abs(pw.TLightAim("Player")) < 1e-6 && pw.TVar("backRight") == 1 && pw.TVar("sawWindow") == 1;
            });

            Step("The neon pulses and the torch flickers (their size changes), the alarm blinks on and off", () =>
            {
                double nMin = 9, nMax = 0, tMin = 9, tMax = 0; bool on = false, off = false;
                for (int i = 0; i < 150; i++)
                {
                    pw.TStep(); pw.TUpdateLighting();
                    nMin = Math.Min(nMin, pw.TLightScale("Neon")); nMax = Math.Max(nMax, pw.TLightScale("Neon"));
                    tMin = Math.Min(tMin, pw.TLightScale("Torch")); tMax = Math.Max(tMax, pw.TLightScale("Torch"));
                    if (pw.TLightLevel("Alarm") == 1) on = true; else if (pw.TLightLevel("Alarm") == 0) off = true;
                }
                return nMax - nMin > 0.1 && tMax - tMin > 0.02 && on && off;
            });

            Step("Pressing Y completes the checklist (ALL PASSED)", () =>
            {
                pw.TKey("Y", true); pw.TStep(); pw.TKey("Y", false); pw.TSteps(3);
                return pw.TVar("check:all") == 1 && pw.TLogHas("ALL PASSED");
            });
        }
        finally { try { pw.Close(); } catch { } }
        return results;
    }
}
