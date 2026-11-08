using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEngine;

// E2E for the "BuilderEngine Link" scene of the EasyEngine Tests project, played headless through the bridge
// (PlayWindow.BuilderBridge.cs): the BuilderEngine objects get their world, the BuilderEngine Patrol moves the slime, the
// EasyEngine Platformer hero walks over three BuilderEngine coins (BuilderEngine's score → EasyEngine's variable), the
// project's own Sign mechanism speaks in EasyEngine's dialogue box (Enter closes it on both sides), the starter Label is a
// HUD text, "camera shake" is logged once as not available; Y ticks the checklist; R restarts.
public partial class App
{
    partial void AddE2EScenariosBuilderLink(List<(string, Func<List<(string, bool, List<string>)>>)> scenarios)
        => scenarios.Add(("BuilderEngine Link", RunBuilderLinkE2E));

    internal static List<(string step, bool ok, List<string> lines)> RunBuilderLinkE2E()
    {
        var results = new List<(string, bool, List<string>)>();
        var tmp = Path.Combine(Path.GetTempPath(), "EE_FullE2E_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(tmp);
        var project = ProjectIO.Load(TestScenarios.Build(tmp));
        var pw = new PlayWindow(project, TestScenarios.BuilderLinkScene);
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
                lines.Add($"   state: hero ({pw.TX("Hero"):0.#},{pw.TY("Hero"):0.#}) · slime x {pw.TX("Slime"):0.#} · score {pw.TVar("score")} (BuilderEngine {pw.TBuilderGlobal("score")}) · coins left {pw.TAlive("Coin1") + pw.TAlive("Coin2") + pw.TAlive("Coin3")} · dialog {(pw.TDialog ? "'" + pw.TDialogText + "'" : "closed")} · BuilderEngine errors [{pw.TBuilderErrors}] · checks {pw.TVar("check:done")}/{pw.TVar("check:total")}");
                if (err.Length > 0) lines.Add("EXCEPTION " + err);
                seq = pw.TLog.LastSeq;
                results.Add((name, ok, lines));
            }
            void Key(string k) { pw.TKey(k, true); pw.TStep(); pw.TKey(k, false); pw.TSteps(10); }

            Step("The scene's BuilderEngine objects run in a BuilderEngine world (coins, slime, sign, label); the others are mirrors", () =>
                pw.TBuilderRunning && pw.TBuilderOwned == 6 && pw.TBuilderMirrors >= 3 && pw.TBuilderErrors.Length == 0 && pw.TLogHas("Built on BuilderEngine"));
            double slime0 = 0;
            Step("The BuilderEngine Patrol moves the slime", () =>
            {
                slime0 = pw.TX("Slime");
                pw.TSteps(60);
                return pw.TX("Slime") > slime0 + 20;
            });
            Step("The starter Label is a HUD text showing BuilderEngine's score; its object isn't drawn", () =>
                pw.TBuilderHudTexts().Any(t => t == "BuilderEngine coins: 0") && !pw.TVisible("CoinText"));
            Step("D: the EasyEngine hero walks over the three BuilderEngine coins — they disappear and the score is 3 on both sides", () =>
            {
                pw.TKey("D", true);
                for (int i = 0; i < 240 && pw.TVar("score") < 3; i++) pw.TStep();
                return pw.TVar("score") == 3 && pw.TBuilderGlobal("score") == 3 && pw.TDead("Coin1") && pw.TDead("Coin2") && pw.TDead("Coin3")
                       && pw.TSounds.Any(s => s.Contains("coin", StringComparison.OrdinalIgnoreCase));
            });
            Step("The HUD text follows the score", () => { pw.TSteps(2); return pw.TBuilderHudTexts().Any(t => t == "BuilderEngine coins: 3"); });
            Step("Touching the sign: the project's own Sign mechanism speaks in EasyEngine's dialogue box", () =>
            {
                for (int i = 0; i < 200 && !pw.TDialog; i++) pw.TStep();
                pw.TKey("D", false); pw.TSteps(5);
                pw.TAdvanceDialog(); // = Enter while it types: the whole line shows (headless runs don't render the typewriter)
                return pw.TDialog && pw.TBuilderDialogOpen && pw.TDialogText == "Hello from BuilderEngine!" && pw.TBuilderWarned("camera shake");
            });
            Step("Enter closes the dialogue on both sides (BuilderEngine's 'say' goes on)", () =>
            {
                pw.TAdvanceDialog(); pw.TSteps(3); // = Enter again: EasyEngine's box closes, and so does BuilderEngine's line
                return !pw.TDialog && !pw.TBuilderDialogOpen && pw.TBuilderErrors.Length == 0;
            });
            Step("Y: the on-screen checklist reaches ALL PASSED", () => { pw.TSteps(60); Key("Y"); return pw.TVar("check:all") == 1 && pw.TLogHas("ALL PASSED"); });
            Step("R restarts: the coins are back, the score is 0 again on both sides", () =>
            {
                Key("R"); pw.TSteps(5);
                return pw.TAlive("Coin1") == 1 && pw.TAlive("Coin3") == 1 && pw.TVar("score") == 0 && pw.TBuilderGlobal("score") == 0 && pw.TBuilderRunning && !pw.TDialog;
            });
        }
        finally { try { pw.Close(); } catch { } }
        return results;
    }
}
