using System;
using System.IO;
using System.Linq;

namespace EasyEngine;

// M5/M6 in real windows: the Advanced Scratch editor in both modes, the Boss Demo playing live (its intro starts, the
// overlays draw, no console errors), and the Boss AI / Cutscene inspector cards.
public partial class App
{
    partial void SelfTestSequences(SelfTestContext c)
    {
        var dir = Path.Combine(Path.GetTempPath(), "EE_SelfTest_boss_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(dir);
        var project = ProjectIO.Load(BossDemo.Build(dir));
        project.ConsoleOnStart = false;
        var scene = project.GetStartScene();
        var boss = scene.Layers.SelectMany(l => l.Objects).First(o => o.Name == "SlimeKing");
        var intro = scene.Layers.SelectMany(l => l.Objects).First(o => o.Name == "Intro");

        // the Advanced Scratch editor, both modes
        var cutEd = new SequenceEditorWindow(project, scene, SeqMode.Cutscene, intro.Behaviors.First(b => b.Kind == BehaviorKind.Cutscene).Text("script"), "Intro") { ShowActivated = false };
        cutEd.Show(); c.CloseLater.Add(cutEd);
        cutEd.UpdateLayout();
        var bossEd = new SequenceEditorWindow(project, scene, SeqMode.Boss, boss.Behaviors.First(b => b.Kind == BehaviorKind.BossAI).Text("script"), "SlimeKing") { ShowActivated = false };
        bossEd.Show(); c.CloseLater.Add(bossEd);
        var pages = new int[3];
        for (int i = 0; i < 3; i++) { bossEd.TestShowTab(i); bossEd.UpdateLayout(); pages[i] = bossEd.TestBlocks; }
        c.Write($"advanced scratch: cutscene {cutEd.TestBlocks} blocks · boss attacks/phases/death {string.Join("/", pages)} blocks");
        if (cutEd.TestBlocks != 10 || pages[0] < 10 || pages[1] < 6 || pages[2] != 8 || bossEd.TestTabs != 3)
            throw new Exception("the Advanced Scratch editor didn't show the demo's steps");

        // the Boss AI / Cutscene inspector cards
        var bossEditor = new MainWindow(project);
        bossEditor.Show(); c.CloseLater.Add(bossEditor);
        var card = bossEditor.TestSequenceCard(bossEditor.TestFindObject("SlimeKing")!);
        var cutCard = bossEditor.TestSequenceCard(bossEditor.TestFindObject("Intro")!);
        c.Write($"boss card: scratch {card.scratchButton} · json {card.jsonBox} · ask {card.askBox} · make {card.makeButton} · \"{card.summary}\" — cutscene card \"{cutCard.summary}\"");
        if (!card.scratchButton || !card.jsonBox || !card.askBox || !card.makeButton || card.summary != "✓ 3 phases · 5 attacks · death: 8 steps" || cutCard.summary != "✓ 10 steps")
            throw new Exception("the Boss AI / Cutscene card is missing its Advanced Scratch button, JSON box or Ask-the-AI box");

        // the demo, live: the intro starts with its cinema bars
        var play = new PlayWindow(project, project.StartScene!) { Owner = c.Editor };
        play.Show(); c.CloseLater.Add(play);
        c.LateChecks.Add(() =>
        {
            c.Write($"boss demo window: intro {(play.TCutsceneRunning("Intro") ? "playing" : "not playing")} · {play.TOverlays} · {play.TLog.Errors} errors");
            var err = play.TLog.Entries.FirstOrDefault(e => e.Level == LogLevel.Error);
            if (err != null) return "the Boss Demo logged an error: " + err.FullText();
            if (!play.TOverlaysExist) return "the cutscene/boss overlays are missing from the play window";
            if (!play.TCutsceneRunning("Intro") || !play.TInputLocked || !play.TBarsDrawn) return "the Boss Demo's intro didn't start (locked controls + cinema bars)";
            return null;
        });
    }
}
