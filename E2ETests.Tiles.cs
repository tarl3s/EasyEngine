using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;

namespace EasyEngine;

// E2E for the "Tiles Test" scene of the EasyEngine Tests project: the bricks are a drawn picture, the water an animated
// tile (every water cell shows the same frame, and it changes over time), the grass a plain colour; D walks to the
// brick wall, K breaks the brick in front (+1 brick), D walks through the gap, Y confirms — the checklist must reach
// ALL PASSED; R restarts.
public partial class App
{
    partial void AddE2EScenariosTiles(List<(string, Func<List<(string, bool, List<string>)>>)> scenarios)
        => scenarios.Add(("Tiles Test", RunTilesE2E));

    internal static List<(string step, bool ok, List<string> lines)> RunTilesE2E()
    {
        var results = new List<(string, bool, List<string>)>();
        var tmp = Path.Combine(Path.GetTempPath(), "EE_FullE2E_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(tmp);
        var project = ProjectIO.Load(TestScenarios.Build(tmp));
        var pw = new PlayWindow(project, "Tiles Test");
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
                lines.Add($"   state: hero ({pw.TX("Hero"):0.#},{pw.TY("Hero"):0.#}) · tiles {pw.TTiles("Level")} · bricks {pw.TItem("brick")} · water frame [{string.Join(",", pw.TTileAnimFrames())}] · checks {pw.TVar("check:done")}/{pw.TVar("check:total")}");
                if (err.Length > 0) lines.Add("EXCEPTION " + err);
                seq = pw.TLog.LastSeq;
                results.Add((name, ok, lines));
            }
            void Hold(string k, int frames) { pw.TKey(k, true); pw.TSteps(frames); pw.TKey(k, false); pw.TSteps(5); }
            void Key(string k) { pw.TKey(k, true); pw.TStep(); pw.TKey(k, false); pw.TSteps(25); }
            ImageSource?[] Water() => Enumerable.Range(12, 5).Select(c => pw.TTileCellImage("Level", c, 11)).ToArray();

            int tiles0 = 0;
            Step("The bricks show their drawn picture, the grass its plain colour", () =>
            {
                tiles0 = pw.TTiles("Level");
                return pw.TTileCellImage("Level", 10, 12) is System.Windows.Media.Imaging.BitmapSource b && b.PixelWidth == TestScenarios.TilesTS
                       && pw.TTileCellFill("Level", 1, 12) is SolidColorBrush && pw.TLog.Errors == 0;
            });
            Step("The water is animated: every water cell shows the same frame, and the frame changes with time", () =>
            {
                var a = Water(); pw.TSteps(18); var b = Water();
                return a[0] != null && b[0] != null && a.All(x => ReferenceEquals(x, a[0])) && b.All(x => ReferenceEquals(x, b[0])) && !ReferenceEquals(a[0], b[0]);
            });
            Step("D: the hero walks on the brick floor (through the water) and stops at the brick wall", () =>
            {
                Hold("D", 200);
                return Math.Abs(pw.TX("Hero") + 30 - 960) < 1.5 && Math.Abs(pw.TY("Hero") + 36 - 480) < 1.5;
            });
            Step("K: the pictured brick in front breaks and gives a brick", () =>
            {
                Key("K");
                return pw.TTiles("Level") == tiles0 - 1 && Math.Abs(pw.TItem("brick") - 1) < 0.01 && pw.TLogHas("Broke tile");
            });
            Step("D: the hero walks through the gap", () => { Hold("D", 80); return pw.TX("Hero") >= 1010; });
            Step("Y: the on-screen checklist reaches ALL PASSED", () => { Key("Y"); return pw.TVar("check:all") == 1 && pw.TLogHas("ALL PASSED"); });
            Step("R restarts: the wall is whole again and the hero is back at the start", () =>
            {
                Key("R"); pw.TSteps(3);
                return pw.TTiles("Level") == tiles0 && pw.TX("Hero") < 300 && pw.TTileAnimFrames().Length == 1;
            });
        }
        finally { try { pw.Close(); } catch { } }
        return results;
    }
}
