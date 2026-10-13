using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEngine;

// E2E for the "Radio Toggle" scene of the EasyEngine Tests project: a radio with two Interacts (on / off, each "only
// while") and a looping Sound "only while radioOn >= 1" — one press of E changes it exactly once, the music plays only
// while it is on, and the on-screen checklist must reach ALL PASSED.
public partial class App
{
    partial void AddE2EScenariosMulti(List<(string, Func<List<(string, bool, List<string>)>>)> scenarios)
        => scenarios.Add(("Radio Toggle", RunRadioToggleE2E));

    internal static List<(string step, bool ok, List<string> lines)> RunRadioToggleE2E()
    {
        var results = new List<(string, bool, List<string>)>();
        var tmp = Path.Combine(Path.GetTempPath(), "EE_FullE2E_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(tmp);
        var project = ProjectIO.Load(TestScenarios.Build(tmp));
        var pw = new PlayWindow(project, "Radio Toggle");
        try
        {
            pw.TLoad();
            pw.TSteps(5);
            long seq = 0;
            void Step(string name, Func<bool> check)
            {
                bool ok; string err = "";
                try { ok = check(); } catch (Exception ex) { ok = false; err = ex.GetType().Name + ": " + ex.Message; }
                var lines = pw.TLog.Entries.Where(e => e.Seq > seq).Select(e => $"{e.TimeText,7} {e.Line}{(e.Count > 1 ? "  ×" + e.Count : "")}").ToList();
                lines.Add($"   state: radioOn {pw.TVar("radioOn")} · presses {pw.TVar("uses")} · music {(pw.TSoundPlaying("Radio") ? "playing" : "silent")} · checks {pw.TVar("check:done")}/{pw.TVar("check:total")}");
                if (err.Length > 0) lines.Add("EXCEPTION " + err);
                seq = pw.TLog.LastSeq;
                results.Add((name, ok, lines));
            }
            void Key(string k) { pw.TKey(k, true); pw.TStep(); pw.TKey(k, false); pw.TSteps(25); }
            void NextToRadio() { pw.TPlace("Player", pw.TX("Radio") - 45, pw.TY("Player")); pw.TSteps(3); }

            Step("The radio starts off and silent", () => pw.TVar("radioOn") == 0 && !pw.TSoundPlaying("Radio"));
            Step("Far from the radio, E does nothing", () => { Key("E"); return pw.TVar("radioOn") == 0 && pw.TVar("uses") == 0; });
            Step("Next to it, E turns the radio on and the music starts", () =>
            { NextToRadio(); Key("E"); return pw.TVar("radioOn") == 1 && pw.TSoundPlaying("Radio") && pw.TVar("uses") == 1 && pw.TSounds.Contains("radio_tune.wav"); });
            Step("E again turns it off (only the second Interact ran) and the music stops", () =>
            { Key("E"); return pw.TVar("radioOn") == 0 && !pw.TSoundPlaying("Radio") && pw.TVar("uses") == 2 && pw.TLogHas("stopped"); });
            Step("Holding E down turns it on once, not on-off-on…", () =>
            {
                pw.TKey("E", true); pw.TSteps(40); pw.TKey("E", false); pw.TSteps(5);
                return pw.TVar("radioOn") == 1 && pw.TVar("uses") == 3 && pw.TSoundPlaying("Radio");
            });
            Step("Y: the on-screen checklist reaches ALL PASSED", () => { Key("Y"); return pw.TVar("check:all") == 1 && pw.TLogHas("ALL PASSED"); });
            Step("R restarts: the radio is off and silent again", () =>
            { Key("R"); pw.TSteps(3); return pw.TVar("radioOn") == 0 && pw.TVar("uses") == 0 && !pw.TSoundPlaying("Radio"); });
        }
        finally { try { pw.Close(); } catch { } }
        return results;
    }
}
