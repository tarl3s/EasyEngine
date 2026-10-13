using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEngine;

// E2E for the "Lives & Blocks Test" scene of the EasyEngine Tests project: 3 lives as hearts; the coins and "When none of
// an object are left"; the Shopkeeper's "When a key is pressed near an object"; the flag + the lava (a life lost, back at
// the flag); the spike and the healing Heart; the J combo ("When a key hasn't been pressed for a while", "When a variable
// becomes…"); the checklist reaching ALL PASSED; every life lost starts the scene again; R restarts.
public partial class App
{
    partial void AddE2EScenariosScratch(List<(string, Func<List<(string, bool, List<string>)>>)> scenarios)
        => scenarios.Add((TestScenarios.LivesBlocksScene, RunLivesBlocksE2E));

    internal static List<(string step, bool ok, List<string> lines)> RunLivesBlocksE2E()
    {
        var results = new List<(string, bool, List<string>)>();
        var tmp = Path.Combine(Path.GetTempPath(), "EE_FullE2E_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(tmp);
        var project = ProjectIO.Load(TestScenarios.Build(tmp));
        var pw = new PlayWindow(project, TestScenarios.LivesBlocksScene);
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
                lines.Add($"   state: lives {pw.TVar("lives")} ({pw.TLivesHud("Hero")}) · hero {pw.TX("Hero"):0},{pw.TY("Hero"):0} hp {pw.THp("Hero")} · coins {pw.TVar("coins")} sword {pw.TItem("Sword")}"
                          + $" · combo {pw.TVar("combo")} · msg '{pw.TText("msg")}' · checks {pw.TVar("check:done")}/{pw.TVar("check:total")} · scene {pw.TScene}");
                if (err.Length > 0) lines.Add("EXCEPTION " + err);
                seq = pw.TLog.LastSeq;
                results.Add((name, ok, lines));
            }
            void Key(string k, int held = 4) { pw.TKey(k, true); pw.TSteps(held); pw.TKey(k, false); pw.TSteps(8); }
            void At(double x, int steps = 4) { pw.TPlace("Hero", x, 436); pw.TSteps(steps); }

            Step("At the start: 3 hearts at the top right, 3 coins, no sword", () =>
                pw.TVar("lives") == 3 && pw.TLivesHud("Hero") == "♥ ♥ ♥" && pw.TExists("Coin") && pw.TExists("Coin_2") && pw.TExists("Coin_3") && pw.TItem("Sword") == 0);
            Step("Collect the 3 coins: \"When none of an object are left\" fires once (Coin, Coin_2, Coin_3)", () =>
            {
                At(252); bool notYet = pw.TVar("allCoins") == 0;
                At(322); At(392);
                return notYet && pw.TVar("coins") == 3 && pw.TVar("allCoins") == 1 && pw.TText("msg") == "All the coins!";
            });
            Step("E next to the Shopkeeper (3 coins): pay 3, get a Sword — far away nothing happens", () =>
            {
                At(600); Key("E"); bool far = pw.TItem("Sword") == 0;
                At(900); Key("E", 20); // held: once
                bool bought = pw.TItem("Sword") == 1 && pw.TVar("coins") == 0;
                Key("E"); // no coins left
                return far && bought && pw.TItem("Sword") == 1 && pw.TText("msg") == "Here is your Sword!";
            });
            Step("Touch the flag, then the lava: a heart is lost and the hero is back at the flag", () =>
            {
                At(550); At(740, 3);
                return pw.TVar("lives") == 2 && pw.TLivesHud("Hero") == "♥ ♥" && Math.Abs(pw.TX("Hero") - 560) < 30 && pw.TVar("lostOne") == 1;
            });
            Step("The spike hurts (after the safe time), the Heart heals 1 and disappears", () =>
            {
                pw.TSteps(110);
                At(1110); bool hurt = pw.THp("Hero") == 2;
                At(1250);
                return hurt && pw.THp("Hero") == 3 && pw.TVar("healed") == 1 && !pw.TExists("Heart");
            });
            Step("J J J quickly: Combo! (and combo back to 0); one J then a pause: back to 0", () =>
            {
                Key("J"); Key("J"); Key("J");
                bool combo = pw.TVar("comboDone") == 1 && pw.TVar("combo") == 0 && pw.TText("msg") == "Combo!";
                Key("J"); bool one = pw.TVar("combo") == 1;
                pw.TSteps(75);
                return combo && one && pw.TVar("combo") == 0;
            });
            Step("The on-screen checklist reaches ALL PASSED", () => { pw.TSteps(5); return pw.TVar("check:all") == 1 && pw.TLogHas("ALL PASSED"); });
            Step("Every life lost: the scene starts again with 3 hearts and the coins back", () =>
            {
                pw.TSteps(110); At(740, 3); bool two = pw.TVar("lives") == 1;
                pw.TSteps(110); At(740, 3); pw.TSteps(3);
                return two && pw.TVar("lives") == 3 && pw.TLivesHud("Hero") == "♥ ♥ ♥" && pw.TExists("Coin") && pw.TVar("coins") == 0 && pw.TLogHas("the scene starts again");
            });
            Step("R restarts: a coin taken comes back", () =>
            {
                At(252); bool took = pw.TVar("coins") == 1;
                Key("R"); pw.TSteps(3);
                return took && pw.TVar("coins") == 0 && pw.TExists("Coin") && pw.TVar("lives") == 3;
            });
        }
        finally { try { pw.Close(); } catch { } }
        return results;
    }
}
