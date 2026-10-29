using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEngine;

// M5 — the step runner and the Cutscene behavior, played headless: timing on the logic clock, step order, steps
// delegated to the Custom actions, dialogue that waits for Enter, the input lock, the camera pan + return, fade /
// bars / title, moving/showing/hiding objects, the four triggers, "only once", the skip key, the JSON parser's
// tolerance and the Advanced Scratch round-trip.
public partial class App
{
    private static string J(string s) => s.Replace('\'', '"');

    // A floor, a Platformer player (camera follows it), a Box, a far-away object, and "Cut" with a Cutscene.
    private static Action<Project, Scene> CutArena(string steps, Action<BehaviorData>? cfg = null, Action<Project, Scene, Layer>? more = null) => (p, s) =>
    {
        var l = s.Layers.First(z => z.Kind == LayerKind.Base);
        l.Objects.Add(new GameObjectData { Id = 10, Name = "Floor", X = 0, Y = 600, Width = 4000, Height = 40, Physics = new PhysicsData { Mode = PhysicsMode.Static }, Behaviors = { new BehaviorData { Kind = BehaviorKind.Solid } } });
        var player = new GameObjectData { Id = 11, Name = "Player", X = 100, Y = 552, Width = 32, Height = 48, Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
        player.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Platformer).CreateData());
        player.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.FollowCamera).CreateData());
        l.Objects.Add(player);
        l.Objects.Add(new GameObjectData { Id = 12, Name = "Box", X = 500, Y = 300, Width = 40, Height = 40 });
        l.Objects.Add(new GameObjectData { Id = 13, Name = "Far", X = 3000, Y = 300, Width = 40, Height = 40 });
        var cut = BehaviorCatalog.Info(BehaviorKind.Cutscene).CreateData();
        cut.Texts["script"] = J(steps);
        cfg?.Invoke(cut);
        l.Objects.Add(new GameObjectData { Id = 14, Name = "Cut", X = 1000, Y = 500, Width = 100, Height = 100, Color = "#00000000", Behaviors = { cut } });
        more?.Invoke(p, s, l);
    };

    partial void FeatureTestsSequences(FullTestKit t)
    {
        PlayWindow Cut(string steps, Action<BehaviorData>? cfg = null, Action<Project, Scene, Layer>? more = null) => t.Sim("[]", PhysicsMode.None, CutArena(steps, cfg, more));

        // ---------------- the runner
        t.Test("cutscene: wait uses the game clock (the next step runs only after the wait)", () =>
        {
            var p = Cut("{'steps':[{'do':'setvar','var':'a','to':1},{'do':'wait','time':0.5},{'do':'setvar','var':'b','to':1}]}");
            p.TSteps(2);
            bool started = p.TVar("a") == 1 && p.TVar("b") == 0 && p.TCutsceneRunning("Cut");
            p.TSteps(25); bool still = p.TVar("b") == 0;
            p.TSteps(15);
            return started && still && p.TVar("b") == 1 && !p.TCutsceneRunning("Cut");
        });
        t.Test("cutscene: steps run in order; instant ones go through the Custom actions (setvar/addvar/spawn/emit/playsound)", () =>
        {
            var p = Cut("{'steps':[{'do':'setvar','var':'n','to':1},{'do':'addvar','var':'n','amount':2},{'do':'setvar','var':'m','to':'n * 10'}," +
                        "{'do':'spawn','object':'Orb','at':'Box'},{'do':'emit','at':'Box','count':20},{'do':'playsound','sound':'Assets/sounds/beep.wav'}]}",
                        more: (pr, sc, l) =>
                        {
                            ProjectIO.SaveSound(pr, SoundSynth.Chop(), "beep");
                            pr.Prefabs.Add(new PrefabDef { Name = "Orb", Template = new GameObjectData { Name = "Orb", Width = 10, Height = 10 } });
                        });
            p.TSteps(3);
            return p.TVar("n") == 3 && p.TVar("m") == 30 && p.TAlive("Orb") == 1 && Math.Abs(p.TX("Orb") - 515) < 1
                   && p.TParticles >= 20 && p.TSounds.Contains("beep.wav") && p.TLogHas("step 6/6");
        });
        t.Test("cutscene: 'say' shows the line and waits until Enter closes it", () =>
        {
            var p = Cut("{'steps':[{'do':'say','text':'Hello there','speaker':'Guide'},{'do':'setvar','var':'done','to':1}]}");
            p.TSteps(10);
            bool waiting = p.TDialog && p.TVar("done") == 0;
            p.TAdvanceDialog(); p.TStep(); bool stillOpen = p.TDialog && p.TDialogText == "Hello there"; // 1st Enter finishes the typewriter
            p.TAdvanceDialog(); p.TSteps(2);
            return waiting && stillOpen && !p.TDialog && p.TVar("done") == 1;
        });
        t.Test("cutscene: lock — the player can't move until it ends (and moves after)", () =>
        {
            var p = Cut("{'steps':[{'do':'wait','time':0.6}]}");
            p.TSteps(10); double x0 = p.TX("Player");
            p.TKey("D", true); p.TSteps(15);
            bool frozen = Math.Abs(p.TX("Player") - x0) < 1 && p.TInputLocked;
            p.TSteps(40);
            return frozen && !p.TInputLocked && p.TX("Player") > x0 + 40;
        });
        t.Test("cutscene: 'Lock the player' off leaves the controls alone", () =>
        {
            var p = Cut("{'steps':[{'do':'wait','time':1}]}", c => c.Nums["lock"] = 0);
            p.TSteps(3); p.TKey("D", true); double x0 = p.TX("Player"); p.TSteps(10);
            return !p.TInputLocked && p.TX("Player") > x0 + 20;
        });
        t.Test("cutscene: unlock step gives the controls back early", () =>
        {
            var p = Cut("{'steps':[{'do':'wait','time':0.2},{'do':'unlock'},{'do':'wait','time':5}]}");
            p.TSteps(5); bool locked = p.TInputLocked;
            p.TSteps(20);
            return locked && !p.TInputLocked && p.TCutsceneRunning("Cut");
        });
        t.Test("cutscene: camera pans to an object, then 'camera back' returns to the player", () =>
        {
            var p = Cut("{'steps':[{'do':'camera','object':'Far','time':0.5},{'do':'wait','time':0.3},{'do':'cameraback','time':0.5},{'do':'setvar','var':'done','to':1}]}");
            for (int i = 0; i < 40; i++) { p.TStep(); p.TUpdateCamera(); }
            bool atFar = Math.Abs(p.TCamX + 3020) < 5 && p.TCameraOverride;
            for (int i = 0; i < 60; i++) { p.TStep(); p.TUpdateCamera(); }
            p.TUpdateCamera(60);
            double playerCX = p.TX("Player") + 16;
            return atFar && p.TVar("done") == 1 && !p.TCameraOverride && Math.Abs(p.TCamX + playerCX) < 20;
        });
        t.Test("cutscene: 'the camera stops where it is' holds the view while the player moves; 'keeps following' follows", () =>
        {
            double Moved(string camera)
            {
                var p = Cut("{'steps':[{'do':'wait','time':3}]}", c => { c.Nums["lock"] = 0; c.Texts["camera"] = camera; });
                for (int i = 0; i < 5; i++) { p.TStep(); p.TUpdateCamera(); }
                double c0 = p.TCamX;
                p.TKey("D", true);
                for (int i = 0; i < 60; i++) { p.TStep(); p.TUpdateCamera(); }
                return Math.Abs(p.TCamX - c0);
            }
            return Moved("stop") < 0.5 && Moved("follow") > 20;
        });
        t.Test("cutscene: camera to a point (x, y)", () =>
        {
            var p = Cut("{'steps':[{'do':'camera','object':'','x':1500,'y':300,'time':0.2},{'do':'wait','time':5}]}");
            for (int i = 0; i < 20; i++) { p.TStep(); p.TUpdateCamera(); }
            return Math.Abs(p.TCamX + 1500) < 5;
        });
        t.Test("cutscene: shake is on for its time", () =>
        {
            var p = Cut("{'steps':[{'do':'shake','strength':10,'time':0.3},{'do':'wait','time':2}]}");
            p.TSteps(3); bool on = p.TShaking; p.TSteps(30);
            return on && !p.TShaking;
        });
        t.Test("cutscene: fade out / title / fade in, with cinema bars while it plays", () =>
        {
            var p = Cut("{'steps':[{'do':'fadeout','color':'#000000','time':0.3},{'do':'title','text':'HELLO','time':1,'wait':0},{'do':'wait','time':0.5},{'do':'fadein','time':0.3}]}");
            p.TSteps(30);
            bool mid = p.TBarsShown && p.TFadeOpacity > 0.99 && p.TTitleText == "HELLO";
            p.TSteps(60);
            bool after = p.TFadeOpacity < 0.01 && p.TTitleText == "" && !p.TCutsceneRunning("Cut");
            p.TSteps(30);
            return mid && after && p.TBarsT == 0;
        });
        t.Test("cutscene: a fade left on when it ends clears by itself", () =>
        {
            var p = Cut("{'steps':[{'do':'fadeout','color':'#FFFFFF','time':0.1}]}");
            p.TSteps(10); bool white = p.TFadeOpacity > 0.5;
            p.TSteps(50);
            return white && p.TFadeOpacity < 0.01;
        });
        t.Test("cutscene: move slides an object to a point over time (and waits for it)", () =>
        {
            var p = Cut("{'steps':[{'do':'move','object':'Box','to':'','x':800,'y':300,'time':1},{'do':'setvar','var':'done','to':1}]}");
            p.TSteps(2); p.TSteps(30);
            bool halfway = p.TX("Box") > 560 && p.TX("Box") < 740 && p.TVar("done") == 0;
            p.TSteps(40);
            return halfway && Math.Abs(p.TX("Box") - 800) < 0.5 && Math.Abs(p.TY("Box") - 300) < 0.5 && p.TVar("done") == 1;
        });
        t.Test("cutscene: move to an object centres on it; a Dynamic player doesn't fall while moved", () =>
        {
            var p = Cut("{'steps':[{'do':'move','object':'player','to':'Box','time':0.5},{'do':'setvar','var':'moved','to':1},{'do':'wait','time':0.3}]}");
            for (int i = 0; i < 80 && p.TVar("moved") == 0; i++) p.TStep();
            return p.TVar("moved") == 1 && Math.Abs(p.TX("Player") + 16 - 520) < 1 && Math.Abs(p.TY("Player") + 24 - 320) < 1;
        });
        t.Test("cutscene: hide and show an object; face turns it", () =>
        {
            var p = Cut("{'steps':[{'do':'hide','object':'Box'},{'do':'face','object':'Box','toward':'left'},{'do':'wait','time':0.3},{'do':'show','object':'Box'},{'do':'face','object':'Box','toward':'Far'}]}");
            p.TSteps(3); bool hidden = !p.TVisible("Box") && p.TFacing("Box") == -1;
            p.TSteps(30);
            return hidden && p.TVisible("Box") && p.TFacing("Box") == 1;
        });
        t.Test("cutscene: destroy removes the object; goscene switches the scene", () =>
        {
            var p = Cut("{'steps':[{'do':'destroy','object':'Box'},{'do':'wait','time':0.1},{'do':'goscene','scene':'S2'}]}",
                        more: (pr, sc, l) => { var s2 = new Scene { Name = "S2" }; s2.Layers.Add(new Layer { Name = "Base", Kind = LayerKind.Base, Objects = { new GameObjectData { Name = "Won" } } }); pr.Scenes.Add(s2); });
            p.TSteps(3); bool gone = p.TDead("Box");
            p.TSteps(10);
            return gone && p.TScene == "S2" && p.TExists("Won") && !p.TInputLocked && p.TRunners == 0;
        });

        // ---------------- triggers, only once, skip
        t.Test("cutscene trigger: the player enters its area", () =>
        {
            var p = Cut("{'steps':[{'do':'wait','time':2}]}", c => c.Texts["trigger"] = "enter");
            p.TSteps(5); bool before = p.TCutsceneRunning("Cut");
            p.TPlace("Player", 1020, 520); p.TStep();
            return !before && p.TCutsceneRunning("Cut");
        });
        t.Test("cutscene trigger: a variable turns on", () =>
        {
            var p = Cut("{'steps':[{'do':'wait','time':2}]}", c => { c.Texts["trigger"] = "var"; c.Texts["var"] = "go"; });
            p.TSteps(5); bool before = p.TCutsceneRunning("Cut");
            p.TSetVar("go", 1); p.TStep();
            return !before && p.TCutsceneRunning("Cut");
        });
        t.Test("cutscene trigger: a key is pressed", () =>
        {
            var p = Cut("{'steps':[{'do':'wait','time':2}]}", c => { c.Texts["trigger"] = "key"; c.Texts["key"] = "K"; });
            p.TSteps(5); bool before = p.TCutsceneRunning("Cut");
            p.TKey("K", true); p.TStep();
            return !before && p.TCutsceneRunning("Cut");
        });
        bool Replays(double once)
        {
            var p = Cut("{'steps':[{'do':'wait','time':0.1}]}", c => { c.Texts["trigger"] = "enter"; c.Nums["once"] = once; });
            p.TPlace("Player", 1020, 520); p.TStep(); bool first = p.TCutsceneRunning("Cut");
            p.TSteps(20); p.TPlace("Player", 100, 552); p.TStep();
            p.TPlace("Player", 1020, 520); p.TStep();
            return first && p.TCutsceneRunning("Cut");
        }
        t.Test("cutscene: 'Only once' — it doesn't play a second time (remembered as cut:<name>)", () => !Replays(1));
        t.Test("cutscene: with 'Only once' off it plays every time", () => Replays(0));
        t.Test("cutscene: the skip key jumps to the end — later setvar/addvar/hide/move applied, controls, bars and camera given back", () =>
        {
            var p = Cut("{'steps':[{'do':'setvar','var':'a','to':1},{'do':'camera','object':'Far','time':1},{'do':'wait','time':5},{'do':'setvar','var':'b','to':1}," +
                        "{'do':'hide','object':'Box'},{'do':'move','object':'Box','to':'','x':900,'y':300,'time':3},{'do':'addvar','var':'c','amount':5},{'do':'say','text':'never shown'}]}");
            p.TSteps(5);
            bool takesEsc = p.TCutsceneTakesEscape && p.TInputLocked && p.TCameraOverride;
            p.TKey("Escape", true); p.TStep();
            bool applied = p.TVar("a") == 1 && p.TVar("b") == 1 && p.TVar("c") == 5 && !p.TVisible("Box") && Math.Abs(p.TX("Box") - 900) < 0.5 && !p.TDialog;
            p.TKey("Escape", false); p.TSteps(30);
            return takesEsc && applied && !p.TCutsceneRunning("Cut") && !p.TInputLocked && !p.TCameraOverride && p.TBarsT == 0 && p.TLogHas("Skipped");
        });
        t.Test("cutscene: with no skip key Esc isn't taken (it still closes the game)", () =>
        {
            var p = Cut("{'steps':[{'do':'wait','time':5}]}", c => c.Texts["skipKey"] = "");
            p.TSteps(3);
            return p.TCutsceneRunning("Cut") && !p.TCutsceneTakesEscape;
        });
        t.Test("cutscene: the Esc that skipped it doesn't also close the game (held, auto-repeat, a quick second tap)", () =>
        {
            var p = Cut("{'steps':[{'do':'wait','time':5}]}");
            p.TSteps(3);
            p.TKey("Escape", true); p.TStep();
            bool skipped = !p.TCutsceneRunning("Cut"), held = !p.TEscapeCloses(repeat: true) && !p.TEscapeCloses();
            p.TKey("Escape", false); p.TSteps(5); bool quickTap = !p.TEscapeCloses();
            p.TSteps(40);
            return skipped && held && quickTap && p.TEscapeCloses() && !p.TEscapeCloses(repeat: true);
        });
        t.Test("cutscene: with Enter as its skip key, the Enter that advances the dialogue doesn't skip it (one with no line open does)", () =>
        {
            var p = Cut("{'steps':[{'do':'say','text':'one'},{'do':'say','text':'two'},{'do':'wait','time':5}]}", c => c.Texts["skipKey"] = "Enter");
            void Enter() { p.TKey("Enter", true); if (p.TDialog) p.TAdvanceDialog(); p.TSteps(2); p.TKey("Enter", false); p.TSteps(2); } // like the key handler
            p.TSteps(5);
            Enter(); Enter(); Enter(); // line one typed out, closed; line two typed out
            bool onTwo = p.TCutsceneRunning("Cut") && p.TDialog && p.TDialogText == "two";
            Enter(); bool waiting = p.TCutsceneRunning("Cut") && !p.TDialog;
            Enter();
            return onTwo && waiting && !p.TCutsceneRunning("Cut") && p.TLogHas("Skipped");
        });
        t.Test("cutscene: a 'move' whose object is destroyed, or taken over by another move, doesn't wait forever", () =>
        {
            const string steps = "{'steps':[{'do':'move','object':'Box','to':'','x':900,'y':300,'time':3},{'do':'setvar','var':'a','to':1}]}";
            var p = Cut(steps);
            p.TSteps(10); p.TDestroyAll("Box"); p.TCleanup(); p.TSteps(3); // removed like a live frame does
            bool destroyed = p.TVar("a") == 1;
            var q = Cut(steps, more: (pr, sc, l) =>
            {
                var c2 = BehaviorCatalog.Info(BehaviorKind.Cutscene).CreateData();
                c2.Texts["script"] = J("{'steps':[{'do':'wait','time':0.2},{'do':'move','object':'Box','to':'','x':100,'y':300,'time':0.2}]}");
                l.Objects.Add(new GameObjectData { Id = 15, Name = "Cut2", Behaviors = { c2 } });
            });
            q.TSteps(40);
            return destroyed && q.TVar("a") == 1 && !q.TCutsceneRunning("Cut") && Math.Abs(q.TX("Box") - 100) < 0.5;
        });
        t.Test("cutscene: without a follow camera, 'camera back' returns the view to where it was", () =>
        {
            var p = Cut("{'steps':[{'do':'camera','object':'Far','time':0.3},{'do':'wait','time':0.2},{'do':'cameraback','time':0.3},{'do':'setvar','var':'done','to':1}]}",
                        more: (pr, sc, l) => l.Objects.First(o => o.Name == "Player").Behaviors.RemoveAll(b => b.Kind == BehaviorKind.FollowCamera));
            double x0 = p.TCamX; bool away = false;
            for (int i = 0; i < 80; i++) { p.TStep(); p.TUpdateCamera(); away |= p.TCamX < -2000; }            return away && p.TVar("done") == 1 && !p.TCameraOverride && Math.Abs(p.TCamX - x0) < 1;
        });
        t.Test("cutscene: the camera stays where its target died; an unknown name warns and leaves the camera alone", () =>
        {
            var p = Cut("{'steps':[{'do':'camera','object':'Box','time':0.3},{'do':'destroy','object':'Box'},{'do':'wait','time':1}]}");
            for (int i = 0; i < 50; i++) { p.TStep(); p.TUpdateCamera(); }
            bool stays = p.TDead("Box") && p.TCameraOverride && Math.Abs(p.TCamX + 520) < 2 && Math.Abs(p.TCamY + 320) < 2;
            var q = Cut("{'steps':[{'do':'camera','object':'Nobody','time':0.3},{'do':'wait','time':1}]}");
            for (int i = 0; i < 10; i++) { q.TStep(); q.TUpdateCamera(); }
            return stays && !q.TCameraOverride && q.TLogHas("no object 'Nobody'");
        });
        t.Test("cutscene: a scene named like a number (\"2\") is still found by goscene", () =>
        {
            var p = Cut("{'steps':[{'do':'goscene','scene':'2'}]}",
                        more: (pr, sc, l) => { var s2 = new Scene { Name = "2" }; s2.Layers.Add(new Layer { Name = "Base", Kind = LayerKind.Base, Objects = { new GameObjectData { Name = "Won" } } }); pr.Scenes.Add(s2); });
            p.TSteps(3);
            return p.TScene == "2" && p.TExists("Won");
        });
        t.Test("cutscene: the lock also holds back a held mouse button ('mouse held' rules wait until it ends)", () =>
        {
            var p = Cut("{'steps':[{'do':'wait','time':0.5}]}", more: (pr, sc, l) => l.Objects.Add(new GameObjectData
            {
                Id = 16, Name = "Btn", X = 200, Y = 200, Width = 64, Height = 64,
                Behaviors = { new BehaviorData { Kind = BehaviorKind.Custom, Texts = { ["script"] = "[{\"when\":\"mousedown\",\"do\":\"addvar\",\"var\":\"n\",\"amount\":1}]" } } }
            }));
            p.TSteps(2);
            p.TMouseAt(232, 232); p.TMouseDown(true);
            p.TSteps(20); bool held = p.TVar("n") == 0 && p.TInputLocked;
            p.TSteps(30);
            return held && !p.TInputLocked && p.TVar("n") > 5;
        });
        t.Test("cutscene: bad JSON warns once in the console and plays nothing", () =>
        {
            var p = Cut("{'steps':[{'do':'wait'");
            p.TSteps(5);
            return !p.TCutsceneRunning("Cut") && p.TLogCount("Cutscene", "can't be read") == 1;
        });

        // ---------------- the JSON (same parser everywhere)
        t.Test("sequence json: invalid JSON is an error with a line number; a non-object/array root too", () =>
        {
            SequenceJson.ParseCutscene("{\"steps\":[", out var e1);
            SequenceJson.ParseCutscene("42", out var e2);
            SequenceJson.ParseBoss("[1,2]", out var e3);
            return e1 != null && e1.Contains("line") && e2 != null && e3 != null;
        });
        t.Test("sequence json: synonyms (action/type, Dialog, pause, duration), numbers as text, defaults filled, unknown steps dropped with a note", () =>
        {
            var notes = new List<string>();
            var c = SequenceJson.ParseCutscene(J("{'steps':[{'action':'Dialog','message':'hi'},{'type':'pause','duration':'2'},{'do':'Camera_Back'},{'do':'explode'},{'do':'zzz'},{'do':'bars','on':false}]}"), out var err, notes);
            var s = c.Steps;
            return err == null && s.Count == 5 && s[0].Do == "say" && s[0].Str("text") == "hi" && s[1].Do == "wait" && s[1].Num("time") == 2
                   && s[2].Do == "cameraback" && s[2].Num("time") == 1 && s[3].Do == "emit" && s[3].Str("color") == "#FFD24D"
                   && s[4].Do == "bars" && s[4].Str("on") == "0" && notes.Count == 1 && notes[0].Contains("zzz");
        });
        t.Test("sequence json: a bare array and {\"cutscene\":{…}} are accepted too", () =>
            SequenceJson.ParseCutscene("[{\"do\":\"wait\"}]", out var e1).Steps.Count == 1 && e1 == null
            && SequenceJson.ParseCutscene("{\"cutscene\":{\"steps\":[{\"do\":\"wait\"},{\"do\":\"lock\"}]}}", out var e2).Steps.Count == 2 && e2 == null);
        t.Test("sequence json: boss steps aren't cutscene steps (dropped with a note)", () =>
        {
            var notes = new List<string>();
            var c = SequenceJson.ParseCutscene("{\"steps\":[{\"do\":\"dash\"},{\"do\":\"wait\"}]}", out _, notes);
            return c.Steps.Count == 1 && notes.Any(n => n.Contains("dash"));
        });
        t.Test("sequence json: serialize → parse → serialize is stable", () =>
        {
            var j1 = SequenceJson.Serialize(SequenceJson.ParseCutscene(J("{'steps':[{'do':'say','text':'a \\'quote\\' and a|pipe','speaker':'Me'},{'do':'move','object':'Box','to':'','x':'12.5','y':3,'extra':'kept'}]}"), out _));
            var j2 = SequenceJson.Serialize(SequenceJson.ParseCutscene(j1, out _));
            return j1 == j2 && j1.Contains("\"extra\":\"kept\"") && j1.Contains("\"x\":12.5");
        });

        // ---------------- Advanced Scratch: JSON → editor → JSON is stable
        var edDir = Path.Combine(Path.GetTempPath(), "EE_Full_SeqEd_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        var edProject = ProjectIO.CreateNew("SeqEd", edDir);
        string EdRT(SeqMode mode, string json) { var w = new SequenceEditorWindow(edProject, edProject.GetStartScene(), mode, json); var r = w.SerializeTest(); w.Close(); return r; }
        t.Test("advanced scratch: a cutscene round-trips through the editor unchanged (and stays stable)", () =>
        {
            var json = J("{'steps':[{'do':'bars','on':1},{'do':'camera','object':'Boss','time':1.2},{'do':'say','text':'Hi','speaker':'K'},{'do':'cameraback'},{'do':'setvar','var':'awake','to':1}]}");
            var canon = SequenceJson.Serialize(SequenceJson.ParseCutscene(json, out _));
            var once = EdRT(SeqMode.Cutscene, json);
            return once == canon && EdRT(SeqMode.Cutscene, once) == once;
        });
        t.Test("advanced scratch: the Boss Demo's boss round-trips through the editor unchanged", () =>
        {
            var boss = BossDemo.BossScriptJson();
            return EdRT(SeqMode.Boss, boss) == boss;
        });
        t.Test("advanced scratch: one palette per mode — every step has a category colour; boss mode adds the boss steps", () =>
            StepCatalog.Boss.All(d => StepCatalog.Categories.Any(c => c.name == d.Category))
            && StepCatalog.Cutscene.All(d => StepCatalog.Boss.Contains(d)) && StepCatalog.Find(SeqMode.Cutscene, "dash") == null
            && new StepListControl(new List<SeqStep>(), SeqMode.Boss, new StepContext { Project = edProject, Scene = edProject.GetStartScene() }).AddMenu().Items.OfType<System.Windows.Controls.MenuItem>().Count(m => !m.IsEnabled) == 7);
    }
}
