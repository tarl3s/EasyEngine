using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEngine;

// E2E for the "Variable Types" scene of the EasyEngine Tests project: the sign shows a Text variable, N renames the
// player, T switches a Yes/No lamp on and off, C paints a lamp with a Colour variable, Q / X fill and empty a List, Y confirms — the on-screen checklist must reach ALL PASSED.
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
                lines.Add($"   state: sign \"{pw.TLabel("Sign")}\" · playerName \"{pw.TText("playerName")}\" · lampOn {pw.TVar("lampOn")} · lampColor {pw.TText("lampColor")} · quests [{VarScope.ListText(pw.TList("quests"))}] · checks {pw.TVar("check:done")}/{pw.TVar("check:total")}");
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
            Step("The colour lamp starts grey (a Colour variable)", () => pw.TText("lampColor") == "#5A5A66" && pw.TBackground("ColorLamp") == "#FF5A5A66");
            Step("C puts orange in the Colour variable: the lamp is painted orange", () =>
            { Key("C"); return pw.TText("lampColor") == "#FF8800" && pw.TBackground("ColorLamp") == "#FFFF8800" && pw.TLabel("ColorLampText") == "Colour: #FF8800" && pw.TVar("lampTinted") == 1; });
            Step("The quest list starts empty", () => pw.TList("quests").Count == 0 && pw.TLabel("Quests") == "Quests (0): ");
            Step("Q adds a quest: the Label lists it", () => { Key("Q"); return pw.TList("quests").Count == 1 && pw.TLabel("Quests") == "Quests (1): Find the key"; });
            Step("Q again: two quests (count:quests in the checklist)", () =>
            { Key("Q"); return pw.TEval("count:quests") == 2 && pw.TVar("hadTwo") == 1 && pw.TLabel("Quests") == "Quests (2): Find the key, Find the key"; });
            Step("X empties the list", () => { Key("X"); return pw.TList("quests").Count == 0 && pw.TVar("listEmptied") == 1; });
            Step("Y: the on-screen checklist reaches ALL PASSED", () => { Key("Y"); return pw.TVar("check:all") == 1 && pw.TLogHas("ALL PASSED"); });
            Step("R restarts: the name is Hero again, the lamp grey, no quests", () =>
            {
                Key("Q"); Key("R"); pw.TSteps(3);
                return pw.TText("playerName") == "Hero" && pw.TLabel("Sign") == "Hello Hero!" && pw.TText("lampColor") == "#5A5A66" && pw.TList("quests").Count == 0;
            });
        }
        finally { try { pw.Close(); } catch { } }
        return results;
    }
}
