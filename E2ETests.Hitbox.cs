using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEngine;

// E2E for the "Hitbox Test" scene of the EasyEngine Tests project: the hero's picture has wide transparent margins and
// "Hitbox: fit to the drawing" on — D walks the drawing (not the box) into the right wall, A turns (the margins mirror)
// and walks the drawing into the left wall, Y confirms — the on-screen checklist must reach ALL PASSED; R restarts.
public partial class App
{
    partial void AddE2EScenariosHitbox(List<(string, Func<List<(string, bool, List<string>)>>)> scenarios)
        => scenarios.Add(("Hitbox Test", RunHitboxE2E));

    internal static List<(string step, bool ok, List<string> lines)> RunHitboxE2E()
    {
        var results = new List<(string, bool, List<string>)>();
        var tmp = Path.Combine(Path.GetTempPath(), "EE_FullE2E_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(tmp);
        var project = ProjectIO.Load(TestScenarios.Build(tmp));
        var pw = new PlayWindow(project, "Hitbox Test");
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
                var hb = pw.THitbox("Hero"); var pic = pw.TPictureAt("Hero");
                lines.Add($"   state: hitbox ({hb.X:0.#},{hb.Y:0.#} {hb.Width:0.#}×{hb.Height:0.#}) · picture at ({pic.X:0.#},{pic.Y:0.#}) · facing {pw.TFacing("Hero")} · touchR {pw.TVar("touchR")} touchL {pw.TVar("touchL")} · checks {pw.TVar("check:done")}/{pw.TVar("check:total")}");
                if (err.Length > 0) lines.Add("EXCEPTION " + err);
                seq = pw.TLog.LastSeq;
                results.Add((name, ok, lines));
            }
            void Hold(string k, int frames) { pw.TKey(k, true); pw.TSteps(frames); pw.TKey(k, false); pw.TSteps(5); }
            void Key(string k) { pw.TKey(k, true); pw.TStep(); pw.TKey(k, false); pw.TSteps(25); }

            double marginL = 0, marginR = 0;
            Step("The hero's hitbox fits the drawing: narrower than its 96-px picture, standing on the ground", () =>
            {
                var hb = pw.THitbox("Hero"); var pic = pw.TPictureAt("Hero");
                marginL = hb.X - pic.X; marginR = pic.X + 96 - hb.Right;
                return pw.TFitted("Hero") && hb.Width < 60 && marginL > 10 && marginR > marginL && Math.Abs(hb.Bottom - 500) < 1 && pw.TGrounded("Hero")
                       && pw.TLogHas("Fitted to the drawing");
            });
            Step("D: the drawing walks into the right wall — the empty margin overlaps the wall", () =>
            {
                Hold("D", 150);
                var hb = pw.THitbox("Hero"); var pic = pw.TPictureAt("Hero");
                return Math.Abs(hb.Right - 900) < 1.5 && pic.X + 96 > 910 && pw.TVar("touchR") == 1 && pw.TFacing("Hero") == 1;
            });
            Step("A: the hero turns — the margins mirror, so the picture shifts and the drawing stays over the hitbox", () =>
            {
                Hold("A", 3);
                var hb = pw.THitbox("Hero"); var pic = pw.TPictureAt("Hero");
                return pw.TFacing("Hero") == -1 && Math.Abs(hb.X - pic.X - marginR) < 0.01;
            });
            Step("A: the drawing walks into the left wall", () =>
            {
                Hold("A", 260);
                var hb = pw.THitbox("Hero");
                return Math.Abs(hb.X - 100) < 1.5 && pw.TVar("touchL") == 1 && pw.TPictureAt("Hero").X < 90;
            });
            Step("Y: the on-screen checklist reaches ALL PASSED", () => { Key("Y"); return pw.TVar("check:all") == 1 && pw.TLogHas("ALL PASSED"); });
            Step("R restarts: the hero is back where it started, still fitted", () =>
            {
                Key("R"); pw.TSteps(3);
                var hb = pw.THitbox("Hero");
                return pw.TFitted("Hero") && Math.Abs(hb.X - (400 + marginL)) < 1 && pw.TVar("touchR") == 0;
            });
        }
        finally { try { pw.Close(); } catch { } }
        return results;
    }
}
