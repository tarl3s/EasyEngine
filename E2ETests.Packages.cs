using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEngine;

// E2E for the "Behavior Packages" scene of the EasyEngine Tests project, played headless: the packages in the project's
// behaviors/ folder run through the BuilderEngine bridge — Twirl turns the gem, Drift slides the cloud and pushes the
// Platformer hero (a breeze that adds to its walking), Bell turns E presses into the EasyEngine variable rings — the
// checklist ticks itself, Y confirms, R restarts.
public partial class App
{
    partial void AddE2EScenariosPackages(List<(string, Func<List<(string, bool, List<string>)>>)> scenarios)
        => scenarios.Add(("Behavior Packages", RunPackagesE2E));

    internal static List<(string step, bool ok, List<string> lines)> RunPackagesE2E()
    {
        var results = new List<(string, bool, List<string>)>();
        var tmp = Path.Combine(Path.GetTempPath(), "EE_FullE2E_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(tmp);
        var project = ProjectIO.Load(TestScenarios.Build(tmp));
        var pw = new PlayWindow(project, TestScenarios.PackagesScene);
        try
        {
            pw.TLoad();
            pw.TSteps(3);
            long seq = 0;
            void Step(string name, Func<bool> check)
            {
                bool ok; string err = "";
                try { ok = check(); } catch (Exception ex) { ok = false; err = ex.GetType().Name + ": " + ex.Message; }
                var lines = pw.TLog.Entries.Where(e => e.Seq > seq).Select(e => $"{e.TimeText,7} {e.Line}{(e.Count > 1 ? "  ×" + e.Count : "")}").ToList();
                lines.Add($"   state: hero ({pw.TX("Hero"):0.#},{pw.TY("Hero"):0.#}) · gem rot {pw.TRot("Gem"):0.#} · cloud x {pw.TX("Cloud"):0.#} · rings {pw.TVar("rings")} · package objects {pw.TPackageObjects} · BuilderEngine errors [{pw.TBuilderErrors}] · checks {pw.TVar("check:done")}/{pw.TVar("check:total")}");
                if (err.Length > 0) lines.Add("EXCEPTION " + err);
                seq = pw.TLog.LastSeq;
                results.Add((name, ok, lines));
            }
            void Key(string k) { pw.TKey(k, true); pw.TStep(); pw.TKey(k, false); pw.TSteps(10); }

            Step("The project's behaviors/ packages are found and their objects run them in a BuilderEngine world", () =>
                File.Exists(Path.Combine(project.FolderPath!, "behaviors", "Twirl.behavior")) && pw.TBuilderRunning && pw.TPackageObjects == 4
                && pw.TBuilderErrors.Length == 0 && pw.TLogHas("Behavior packages: 4 object(s)"));
            double cloud0 = 0, hero0 = 0;
            Step("Twirl turns the gem (180° a second)", () =>
            {
                cloud0 = pw.TX("Cloud"); hero0 = pw.TX("Hero");
                double r0 = pw.TRot("Gem");
                pw.TSteps(60);
                return pw.TRot("Gem") - r0 > 150 && pw.TRot("Gem") - r0 < 190;
            });
            Step("Drift slides the cloud right on its own", () => pw.TX("Cloud") - cloud0 > 30);
            Step("The breeze (Drift on the Platformer hero) pushes the standing hero right; it stays on the ground", () =>
                pw.TX("Hero") - hero0 > 20 && pw.TGrounded("Hero"));
            Step("D: walking adds to the breeze (both move the hero)", () =>
            {
                double x0 = pw.TX("Hero");
                pw.TKey("D", true); pw.TSteps(30); pw.TKey("D", false);
                return pw.TX("Hero") - x0 > 100;
            });
            Step("E: the Bell package rings — the EasyEngine variable rings counts each press", () =>
            {
                Key("E"); Key("E");
                return pw.TVar("rings") == 2;
            });
            Step("Y: the on-screen checklist reaches ALL PASSED", () => { pw.TSteps(30); Key("Y"); return pw.TVar("check:all") == 1 && pw.TLogHas("ALL PASSED"); });
            Step("R restarts: rings back to 0, the gem and the cloud start over", () =>
            {
                Key("R"); pw.TSteps(3);
                return pw.TVar("rings") == 0 && pw.TX("Cloud") < cloud0 + 20 && pw.TBuilderRunning && pw.TPackageObjects == 4;
            });
        }
        finally { try { pw.Close(); } catch { } }
        return results;
    }
}
