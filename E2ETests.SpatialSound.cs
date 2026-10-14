using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEngine;

// E2E for the "Spatial Sound" scene of the EasyEngine Tests project: two looping radios, each "Only heard nearby"
// (600 px) and "Left / right in the headphones". The player walks with the arrow keys from the silent middle to each
// radio and back; the volume and balance each radio plays at are read through test hooks (nothing is heard in tests),
// and the on-screen checklist must reach ALL PASSED.
public partial class App
{
    partial void AddE2EScenariosSpatialSound(List<(string, Func<List<(string, bool, List<string>)>>)> scenarios)
        => scenarios.Add(("Spatial Sound", RunSpatialSoundE2E));

    internal static List<(string step, bool ok, List<string> lines)> RunSpatialSoundE2E()
    {
        var results = new List<(string, bool, List<string>)>();
        var tmp = Path.Combine(Path.GetTempPath(), "EE_FullE2E_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(tmp);
        var project = ProjectIO.Load(TestScenarios.Build(tmp));
        var pw = new PlayWindow(project, TestScenarios.SpatialSoundScene);
        try
        {
            pw.TLoad();
            pw.TSteps(5);
            long seq = 0;
            double Cx() => pw.TX("Player") + 17;
            void Step(string name, Func<bool> check)
            {
                bool ok; string err = "";
                try { ok = check(); } catch (Exception ex) { ok = false; err = ex.GetType().Name + ": " + ex.Message; }
                var lines = pw.TLog.Entries.Where(e => e.Seq > seq).Select(e => $"{e.TimeText,7} {e.Line}{(e.Count > 1 ? "  ×" + e.Count : "")}").ToList();
                var (lv, lb) = pw.TSoundMix("RadioLeft"); var (rv, rb) = pw.TSoundMix("RadioRight");
                lines.Add($"   state: player x {Cx():0} · left radio vol {lv:0.###} bal {lb:0.##} · right radio vol {rv:0.###} bal {rb:0.##} · checks {pw.TVar("check:done")}/{pw.TVar("check:total")}");
                if (err.Length > 0) lines.Add("EXCEPTION " + err);
                seq = pw.TLog.LastSeq;
                results.Add((name, ok, lines));
            }
            void Key(string k) { pw.TKey(k, true); pw.TStep(); pw.TKey(k, false); pw.TSteps(25); }
            // hold an arrow until the player's centre passes x, then let go and let the sound settle
            void WalkTo(string key, double x)
            {
                pw.TKey(key, true);
                for (int i = 0; i < 900 && (key == "Left" ? Cx() > x : Cx() < x); i++) pw.TStep();
                pw.TKey(key, false);
                pw.TSteps(40);
            }

            Step("In the middle both radios play (looping) but neither is heard", () =>
                pw.TSoundPlaying("RadioLeft") && pw.TSoundPlaying("RadioRight") && pw.TSoundMix("RadioLeft").volume < 0.005 && pw.TSoundMix("RadioRight").volume < 0.005
                && pw.TSounds.Contains("radio_left.wav") && pw.TSounds.Contains("radio_right.wav") && pw.TLogHas("left/right"));
            Step("Walking left, the left radio fades in — in the left ear — and the right one stays silent", () =>
            {
                WalkTo("Left", 650);
                var (v, b) = pw.TSoundMix("RadioLeft");
                return v > 0.1 && v < 0.65 && b < -0.3 && pw.TSoundMix("RadioRight").volume < 0.005;
            });
            Step("Next to the left radio it is at full volume, nearly centred", () =>
            {
                WalkTo("Left", 330);
                var (v, b) = pw.TSoundMix("RadioLeft");
                return Math.Abs(v - 0.7) < 0.02 && Math.Abs(b) < 0.15 && pw.TVar("nearLeft") == 1;
            });
            Step("Walking right to the other radio: the left one fades out, the right one comes in the right ear", () =>
            {
                WalkTo("Right", 1750);
                var (v, b) = pw.TSoundMix("RadioRight");
                return v > 0.1 && v < 0.65 && b > 0.3 && pw.TSoundMix("RadioLeft").volume < 0.005 && pw.TSoundPlaying("RadioLeft");
            });
            Step("Next to the right radio it is at full volume", () =>
            {
                WalkTo("Right", 2070);
                return Math.Abs(pw.TSoundMix("RadioRight").volume - 0.7) < 0.02 && pw.TVar("nearRight") == 1;
            });
            Step("Back in the middle: both silent again, still playing (walking back brings them in)", () =>
            {
                WalkTo("Left", 1200);
                return pw.TSoundMix("RadioLeft").volume < 0.005 && pw.TSoundMix("RadioRight").volume < 0.005
                       && pw.TSoundPlaying("RadioLeft") && pw.TSoundPlaying("RadioRight") && pw.TVar("farBoth") == 1;
            });
            Step("Y: the on-screen checklist reaches ALL PASSED", () => { Key("Y"); return pw.TVar("check:all") == 1 && pw.TLogHas("ALL PASSED"); });
            Step("R restarts: back in the silent middle, nothing ticked", () =>
            {
                Key("R"); pw.TSteps(3);
                return pw.TVar("nearLeft") == 0 && pw.TVar("farBoth") == 0 && pw.TSoundMix("RadioLeft").volume < 0.005 && pw.TSoundPlaying("RadioRight");
            });
        }
        finally { try { pw.Close(); } catch { } try { Directory.Delete(tmp, true); } catch { } }
        return results;
    }
}
