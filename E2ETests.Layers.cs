using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEngine;

// E2E for the "Layers Test" scene of the EasyEngine Tests project: the Bridge (an object that is off) and the Secret
// layer (off) aren't there at the start; B / N turn the bridge on / off, L / K the whole Secret layer; the Mist layer is
// drawn at 50%; Y confirms — the on-screen checklist must reach ALL PASSED; R restarts.
public partial class App
{
    partial void AddE2EScenariosLayers(List<(string, Func<List<(string, bool, List<string>)>>)> scenarios)
        => scenarios.Add(("Layers Test", RunLayersE2E));

    internal static List<(string step, bool ok, List<string> lines)> RunLayersE2E()
    {
        var results = new List<(string, bool, List<string>)>();
        var tmp = Path.Combine(Path.GetTempPath(), "EE_FullE2E_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(tmp);
        var project = ProjectIO.Load(TestScenarios.Build(tmp));
        var pw = new PlayWindow(project, "Layers Test");
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
                lines.Add($"   state: bridge {pw.TAlive("Bridge")} · gems {pw.TAlive("Gem1")}+{pw.TAlive("Gem2")} · ledge {pw.TAlive("Ledge")} · fog alpha {pw.TLayerAlpha("Fog"):0.##} · checks {pw.TVar("check:done")}/{pw.TVar("check:total")}");
                if (err.Length > 0) lines.Add("EXCEPTION " + err);
                seq = pw.TLog.LastSeq;
                results.Add((name, ok, lines));
            }
            void Key(string k) { pw.TKey(k, true); pw.TStep(); pw.TKey(k, false); pw.TSteps(10); pw.TCleanup(); }

            Step("At the start the bridge and the Secret layer are not in the game; the Mist layer is at 50%", () =>
                !pw.TExists("Bridge") && !pw.TExists("Gem1") && !pw.TExists("Ledge") && pw.TExists("Fog") && Math.Abs(pw.TLayerAlpha("Fog") - 0.5) < 0.01
                && pw.TVar("startOk") == 1 && !pw.TSolids().Contains("(520,500,180x24)"));
            Step("B: the bridge comes in (and is solid)", () => { Key("B"); return pw.TAlive("Bridge") == 1 && pw.TVar("bridgeSeen") == 1 && pw.TSolids().Contains("(520,500,180x24)"); });
            Step("N: the bridge goes away", () => { Key("N"); return !pw.TExists("Bridge") && pw.TVar("bridgeGone") == 1 && !pw.TSolids().Contains("(520,500,180x24)"); });
            Step("L: the whole Secret layer comes in (gems and ledge)", () => { Key("L"); return pw.TAlive("Gem1") == 1 && pw.TAlive("Gem2") == 1 && pw.TAlive("Ledge") == 1 && pw.TVar("secretSeen") == 1 && pw.TLogHas("layer \"Secret\" turned on"); });
            Step("K: the whole Secret layer goes away", () => { Key("K"); return !pw.TExists("Gem1") && !pw.TExists("Gem2") && !pw.TExists("Ledge") && pw.TVar("secretGone") == 1 && pw.TExists("Hero"); });
            Step("Y: the on-screen checklist reaches ALL PASSED", () => { Key("Y"); pw.TSteps(10); return pw.TVar("check:all") == 1 && pw.TLogHas("ALL PASSED"); });
            Step("R restarts: the bridge and the Secret layer are off again", () =>
            {
                Key("R"); pw.TSteps(3);
                return !pw.TExists("Bridge") && !pw.TExists("Gem1") && pw.TVar("bridgeSeen") == 0 && pw.TVar("startOk") == 1;
            });
        }
        finally { try { pw.Close(); } catch { } }
        return results;
    }
}
