using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEngine;

// E2E for the "Variable Types" scene of the EasyEngine Tests project: the sign shows a Text variable, N renames the
// player, T switches a Yes/No lamp on and off, Y confirms — the on-screen checklist must reach ALL PASSED.
public partial class App
{
    partial void AddE2EScenariosVarTypes(List<(string, Func<List<(string, bool, List<string>)>>)> scenarios)
        => scenarios.Add(("Variable Types", RunVarTypesE2E));

    internal static List<(string step, bool ok, List<string> lines)> RunVarTypesE2E()
    {
        var results = new List<(string, bool, List<string>)>();
        var tmp = Path.Combine(Path.GetTempPath(), "EE_FullE2E_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(tmp);
        var project = ProjectIO.Load(TestScenarios.Build(tmp));
        var pw = new PlayWindow(project, "Variable Types");
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
                lines.Add($"   state: sign \"{pw.TLabel("Sign")}\" · playerName \"{pw.TText("playerName")}\" · lampOn {pw.TVar("lampOn")} · checks {pw.TVar("check:done")}/{pw.TVar("check:total")}");
                if (err.Length > 0) lines.Add("EXCEPTION " + err);
                seq = pw.TLog.LastSeq;
                results.Add((name, ok, lines));
            }
            void Key(string k) { pw.TKey(k, true); pw.TStep(); pw.TKey(k, false); pw.TSteps(25); }

            Step("The sign shows the Text variable: Hello Hero!", () => pw.TLabel("Sign") == "Hello Hero!" && pw.TVar("sawHero") == 1);
            Step("The lamp (a Yes/No) starts off", () => pw.TVar("lampOn") == 0 && pw.TLabel("Lamp") == "Lamp on: 0");
            Step("N renames the player: the sign says Hello Ana!", () => { Key("N"); return pw.TText("playerName") == "Ana" && pw.TLabel("Sign") == "Hello Ana!" && pw.TVar("renamed") == 1; });
            Step("T switches the lamp on", () => { Key("T"); return pw.TVar("lampOn") == 1 && pw.TLabel("Lamp") == "Lamp on: 1"; });
            Step("T again switches it off", () => { Key("T"); return pw.TVar("lampOn") == 0 && pw.TVar("lampOffAgain") == 1; });
            Step("Y: the on-screen checklist reaches ALL PASSED", () => { Key("Y"); return pw.TVar("check:all") == 1 && pw.TLogHas("ALL PASSED"); });
            Step("R restarts: the name is Hero again", () => { Key("R"); pw.TSteps(3); return pw.TText("playerName") == "Hero" && pw.TLabel("Sign") == "Hello Hero!"; });
        }
        finally { try { pw.Close(); } catch { } }
        return results;
    }
}
