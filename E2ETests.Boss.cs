using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEngine;

// Plays the Boss Demo headless like a person: the intro cutscene (locked controls, camera to the boss and back, the
// lines advanced with Enter), the fight with the sword through the three phases, the death sequence, the victory
// cutscene that opens the gate, and the walk to the crown.
public partial class App
{
    partial void AddE2EScenariosBoss(List<(string, Func<List<(string, bool, List<string>)>>)> scenarios) => scenarios.Add(("Boss Demo", RunBossE2E));

    internal static List<(string step, bool ok, List<string> lines)> RunBossE2E()
    {
        var results = new List<(string, bool, List<string>)>();
        var tmp = Path.Combine(Path.GetTempPath(), "EE_FullE2E_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(tmp);
        var project = ProjectIO.Load(BossDemo.Build(tmp));
        var pw = new PlayWindow(project, project.StartScene!);
        const string Boss = "SlimeKing";
        try
        {
            pw.TLoad();
            long seq = 0;
            void Step(string name, Func<bool> check)
            {
                bool ok; string err = "";
                try { ok = check(); } catch (Exception ex) { ok = false; err = ex.GetType().Name + ": " + ex.Message; }
                var lines = pw.TLog.Entries.Where(e => e.Seq > seq).Select(e => $"{e.TimeText,7} {e.Line}{(e.Count > 1 ? "  ×" + e.Count : "")}").ToList();
                lines.Add($"   state: boss hp {pw.THp(Boss):0.#} · phase '{pw.TBossPhase(Boss)}' · attack '{pw.TBossAttack(Boss)}' · player hp {pw.THp("Player"):0.#} · locked {pw.TInputLocked} · cam {pw.TCamX:0}");
                if (err.Length > 0) lines.Add("EXCEPTION " + err);
                seq = pw.TLog.LastSeq;
                results.Add((name, ok, lines));
            }
            // a frame of play with the camera too (live it runs every frame)
            void Frames(int n) { for (int i = 0; i < n; i++) { pw.TStep(); pw.TUpdateCamera(); } }
            bool Until(Func<bool> cond, int max) { for (int i = 0; i < max; i++) { if (cond()) return true; Frames(1); } return cond(); }
            // Enter on the dialogue box: the first press shows the whole line, the next one goes on
            void Enter() { pw.TAdvanceDialog(); Frames(2); pw.TAdvanceDialog(); Frames(2); }
            double PlayerCX() => pw.TX("Player") + 17;
            // stand next to the boss and click on it (the click is in screen space: world + camera)
            void Swing()
            {
                double bx = pw.TX(Boss), by = pw.TY(Boss);
                bool left = bx + 60 > 300;
                pw.TPlace("Player", left ? bx - 40 : bx + 126, by + 96 - 50);
                pw.TClick(bx + 60 + pw.TCamX, by + 48 + pw.TCamY);
                Frames(22);
            }

            Step("The intro cutscene starts: the controls are locked and the cinema bars come in", () =>
            {
                Frames(40);
                return pw.TCutsceneRunning("Intro") && pw.TInputLocked && pw.TBarsShown && pw.TVar("bossAwake") == 0;
            });

            Step("Pressing the arrows does nothing while it plays", () =>
            {
                double x0 = pw.TX("Player");
                pw.TKey("D", true); Frames(20); pw.TKey("D", false);
                return Math.Abs(pw.TX("Player") - x0) < 1;
            });

            Step("The camera travels to the Slime King and its title appears", () =>
                Until(() => pw.TTitleText == "THE SLIME KING", 200) && pw.TCamX < -1000 && pw.TCameraOverride && !pw.TBossStarted(Boss));

            Step("The boss speaks; Enter shows the next line", () =>
            {
                bool first = Until(() => pw.TDialog, 300);
                pw.TAdvanceDialog(); Frames(1);
                bool line1 = pw.TDialogText.Contains("Who dares");
                pw.TAdvanceDialog(); Frames(2); pw.TAdvanceDialog(); Frames(1);
                bool line2 = pw.TDialogText.Contains("squish");
                pw.TAdvanceDialog(); Frames(2);
                return first && line1 && line2;
            });

            Step("The camera comes back to the knight", () =>
                Until(() => !pw.TCameraOverride, 120) && Until(() => Math.Abs(pw.TCamX + PlayerCX()) < 30, 60));

            Step("After the knight's line the controls come back and the boss wakes up (health bar shown)", () =>
            {
                bool line = Until(() => pw.TDialog, 120) && Until(() => { pw.TAdvanceDialog(); return pw.TDialogText.Contains("Sliminess"); }, 3);
                pw.TAdvanceDialog(); Frames(5);
                return line && !pw.TCutsceneRunning("Intro") && !pw.TInputLocked && pw.TVar("bossAwake") == 1 && pw.TBossStarted(Boss)
                       && pw.TBossPhase(Boss) == "Phase 1" && pw.TBossBarShown && pw.TBossBarTitle == "THE SLIME KING" && pw.TBarsT < 1;
            });

            Step("Phase 1: it attacks on its own (charge / ring of bullets)", () =>
                Until(() => pw.TLogHas("Attack: Charge") || pw.TLogHas("Attack: Ring"), 300));

            Step("Sword hits hurt it; at 60% health Phase 2 starts", () =>
            {
                double hp0 = pw.THp(Boss);
                for (int i = 0; i < 80 && pw.TBossPhase(Boss) != "Phase 2"; i++) Swing();
                return pw.THp(Boss) < hp0 && pw.TBossPhase(Boss) == "Phase 2" && pw.THp(Boss) <= 18 && pw.TVar("stat:enemyHits") >= 6;
            });

            Step("Phase 2 brings jump slams and aimed fireballs", () =>
            {
                for (int i = 0; i < 60 && !(pw.TLogHas("Attack: Jump slam") || pw.TLogHas("Attack: Fireballs")); i++)
                    if (pw.THp(Boss) > 10) Swing(); else Frames(22); // keep it above 25% for the next step
                return pw.TLogHas("Attack: Jump slam") || pw.TLogHas("Attack: Fireballs");
            });

            Step("At 25% Phase 3: it turns invulnerable for a moment and summons 2 minions", () =>
            {
                for (int i = 0; i < 80 && pw.TBossPhase(Boss) != "Phase 3"; i++) Swing();
                Frames(3);
                bool p3 = pw.TBossPhase(Boss) == "Phase 3" && pw.TInv(Boss) > 0.5;
                double hp = pw.THp(Boss);
                Swing();
                bool shielded = pw.THp(Boss) == hp;
                return p3 && shielded && Until(() => pw.TAlive("Minion") == 2, 120);
            });

            Step("Defeated: the death sequence plays (it shakes and stops hurting) and it says its last words", () =>
            {
                for (int i = 0; i < 80 && !pw.TBossDying(Boss); i++) Swing();
                bool dying = pw.TBossDying(Boss) && !pw.TDead(Boss) && pw.TShaking;
                bool words = Until(() => pw.TDialog, 200);
                pw.TAdvanceDialog(); Frames(1);
                bool last = pw.TDialogText.Contains("swamp");
                pw.TAdvanceDialog(); Frames(5);
                return dying && words && last && pw.TDead(Boss) && pw.TVar("bossDefeated") == 1 && !pw.TBossBarShown;
            });

            Step("The victory cutscene: VICTORY! and the gate opens", () =>
            {
                bool started = Until(() => pw.TCutsceneRunning("Victory"), 30) && pw.TInputLocked;
                bool title = Until(() => pw.TTitleText == "VICTORY!", 60);
                bool gate = Until(() => pw.TVar("gateOpen") == 1, 400);
                bool line = Until(() => pw.TDialog, 300);
                Enter();
                return started && title && gate && line && Until(() => !pw.TCutsceneRunning("Victory"), 30) && !pw.TInputLocked;
            });

            Step("The knight walks through the open gate and takes the crown", () =>
            {
                pw.TPlace("Player", 1450, 590); Frames(10);
                pw.TKey("D", true); Frames(150); pw.TKey("D", false);
                return pw.TX("Player") > 1620 && pw.TDead("Crown") && pw.TDialog;
            });
        }
        finally { try { pw.Close(); } catch { } }
        return results;
    }
}
