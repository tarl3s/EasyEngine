using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEngine;

// E2E for the "Slope Test" scene of the EasyEngine Tests project: D walks the hero up the hill (a triangle shape), over
// its top and down the other side, then into the rock whose steep side stops it; Y confirms — the on-screen checklist
// must reach ALL PASSED; R restarts.
public partial class App
{
    partial void AddE2EScenariosPolyHitbox(List<(string, Func<List<(string, bool, List<string>)>>)> scenarios)
        => scenarios.Add(("Slope Test", RunSlopeE2E));

    internal static List<(string step, bool ok, List<string> lines)> RunSlopeE2E()
    {
        var results = new List<(string, bool, List<string>)>();
        var tmp = Path.Combine(Path.GetTempPath(), "EE_FullE2E_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(tmp);
        var project = ProjectIO.Load(TestScenarios.Build(tmp));
        var pw = new PlayWindow(project, "Slope Test");
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
                lines.Add($"   state: hero ({pw.TX("Hero"):0.#},{pw.TY("Hero"):0.#}) grounded {pw.TGrounded("Hero")} · onHill {pw.TVar("onHill")} top {pw.TVar("topReached")} rock {pw.TVar("atRock")} · checks {pw.TVar("check:done")}/{pw.TVar("check:total")}");
                if (err.Length > 0) lines.Add("EXCEPTION " + err);
                seq = pw.TLog.LastSeq;
                results.Add((name, ok, lines));
            }
            void Key(string k) { pw.TKey(k, true); pw.TStep(); pw.TKey(k, false); pw.TSteps(25); }

            Step("The hill and the rock have their drawn shapes; the hero stands on the ground", () =>
                pw.THasShape("Hill") && pw.THasShape("Rock") && !pw.THasShape("Hero") && Math.Abs(pw.TY("Hero") + 56 - 500) < 1 && pw.TGrounded("Hero"));
            Step("D: up the hill — standing on its slope, then on its top", () =>
            {
                pw.TKey("D", true);
                double best = 1e9;
                for (int i = 0; i < 90; i++) { pw.TStep(); best = Math.Min(best, pw.TY("Hero") + 56); }
                return pw.TVar("onHill") == 1 && pw.TVar("topReached") == 1 && best > 345;
            });
            Step("Down the other side and into the rock: its steep side stops the hero on the ground", () =>
            {
                pw.TSteps(140); pw.TKey("D", false); pw.TSteps(10);
                double right = pw.TX("Hero") + 40;
                return pw.TVar("atRock") == 1 && right < 960 && Math.Abs(pw.TY("Hero") + 56 - 500) < 1;
            });
            Step("Y: the on-screen checklist reaches ALL PASSED", () => { Key("Y"); return pw.TVar("check:all") == 1 && pw.TLogHas("ALL PASSED"); });
            Step("R restarts: the hero is back at the start", () => { Key("R"); pw.TSteps(3); return Math.Abs(pw.TX("Hero") - 200) < 1 && pw.TVar("topReached") == 0; });
        }
        finally { try { pw.Close(); } catch { } }
        return results;
    }
}
