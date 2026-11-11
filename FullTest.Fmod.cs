using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BE = BuilderEngine.Script;

namespace EasyEngine;

// Optional FMOD audio (GameAudio.cs, PlayWindow.Fmod.cs): the setting and its fallback to the classic sound, the Sound
// behavior playing an FMOD Studio event, the "fmodevent" / "fmodparam" rule actions, the cutscene step, BuilderEngine's
// requests and the export helpers — all headless (requests are recorded, nothing is heard). When FMOD is installed on this
// computer, a silent smoke test (FMOD's NOSOUND output) plays a generated WAV and the FMOD Engine's example banks; otherwise
// it prints a skip line. These pass with or without FMOD.
public partial class App
{
    partial void FeatureTestsFmod(FullTestKit t)
    {
        static string Jq(string s) => s.Replace('\'', '"');

        // ---------------- the setting ----------------
        t.Test("fmod setting: auto / fmod / classic are read (a typo or nothing = auto) and written back the same", () =>
            BE.FmodLibrary.ParseBackend("fmod") == BE.AudioBackend.Fmod && BE.FmodLibrary.ParseBackend(" Classic ") == BE.AudioBackend.Classic
            && BE.FmodLibrary.ParseBackend("") == BE.AudioBackend.Auto && BE.FmodLibrary.ParseBackend("fmdo") == BE.AudioBackend.Auto && BE.FmodLibrary.ParseBackend(null) == BE.AudioBackend.Auto
            && new[] { BE.AudioBackend.Auto, BE.AudioBackend.Fmod, BE.AudioBackend.Classic }.All(b => BE.FmodLibrary.ParseBackend(BE.FmodLibrary.BackendKey(b)) == b));

        t.Test("fmod setting: Classic in Settings = no FMOD for sound files (GameAudio.Core is null, the status says classic)", () =>
        {
            var was = AiSettings.AudioBackend;
            try
            {
                AiSettings.AudioBackend = "classic"; // never saved: settings.json isn't touched
                return GameAudio.Backend == BE.AudioBackend.Classic && !GameAudio.WantsFmod && GameAudio.Core == null
                       && GameAudio.StatusText().StartsWith(L.T("Classic sound (chosen above)"));
            }
            finally { AiSettings.AudioBackend = was; }
        });

        t.Test("fmod fallback: FMOD missing (FMOD_DLL=none / ForceMissing) = classic sound, a clear status, no DLL to export, events listed as not installed", () =>
        {
            bool was = BE.FmodLibrary.ForceMissing;
            try
            {
                BE.FmodLibrary.ForceMissing = true;
                var (_, _, banksText) = GameAudio.ProjectBanks(Path.Combine(Path.GetTempPath(), "EE_Full_FmodNone"));
                return !GameAudio.WantsFmod && GameAudio.Core == null && GameAudio.InstalledVersion() == null
                       && GameAudio.StatusText() == L.T("Classic sound (FMOD not found)") && GameAudio.ExportableDlls().Count == 0
                       && banksText == L.T("FMOD Studio isn't installed on this computer.")
                       && GameAudio.EventOptions(Path.Combine(Path.GetTempPath(), "EE_Full_FmodNone"), out var why).Count == 0 && why.Length > 0;
            }
            finally { BE.FmodLibrary.ForceMissing = was; }
        });

        // ---------------- helpers ----------------
        t.Test("fmod params: name=value pairs read tolerantly (spaces, 0,5, junk skipped, last one wins) and written back", () =>
        {
            var ps = GameAudio.ParseParams(" RPM = 1500 ; Load=0,5;junk; x= ;RPM=2000");
            var back = GameAudio.ParseParams(GameAudio.FormatParams(ps));
            return ps.Count == 2 && ps.Any(p => p.name == "RPM" && p.value == 2000) && ps.Any(p => p.name == "Load" && p.value == 0.5)
                   && back.SequenceEqual(ps) && GameAudio.ParseParams("").Count == 0 && GameAudio.FormatParams(new List<(string, double)>()) == "";
        });
        t.Test("fmod paths: Music/Forest, /Music/Forest and event:/Music/Forest are the same event; empty never matches", () =>
            GameAudio.SameEvent("Music/Forest", "event:/Music/Forest") && GameAudio.SameEvent("/music/forest", "event:/Music/Forest")
            && !GameAudio.SameEvent("Music/Forest", "Music/Rain") && !GameAudio.SameEvent("", "") && !GameAudio.SameEvent(null, "x")
            && BE.FmodStudio.NormalizePath("Music/Forest") == "event:/Music/Forest");

        // ---------------- the Sound behavior ----------------
        t.Test("fmod catalog: Sound plays a file (default — every older project) or an FMOD event; event, its parameters and loop show only where they apply", () =>
        {
            var info = BehaviorCatalog.Info(BehaviorKind.SoundEmitter);
            var src = info.Texts.FirstOrDefault(x => x.Key == "source");
            var ev = info.Texts.FirstOrDefault(x => x.Key == "event");
            var ps = info.Texts.FirstOrDefault(x => x.Key == "fmodParams");
            var asset = info.Texts.First(x => x.Key == "asset");
            var loop = info.Nums.First(x => x.Key == "loop");
            var scene = new Scene();
            bool Shown(Func<ParamCtx, bool>? f, BehaviorData b) => f == null || f(new ParamCtx(b, new GameObjectData(), scene));
            var file = info.CreateData();
            var fmod = info.CreateData(); fmod.Texts["source"] = "fmod";
            var old = new BehaviorData { Kind = BehaviorKind.SoundEmitter }; // an older project: no "source" key
            return src != null && src.Pick == PickKind.Choice && src.Default == "file" && src.Options!.Select(o => o.Split('|')[0]).SequenceEqual(new[] { "file", "fmod" })
                   && ev?.Pick == PickKind.FmodEvent && ps?.Pick == PickKind.FmodParams && file.Text("source", "?") == "file"
                   && Shown(asset.ShowIf, file) && !Shown(asset.ShowIf, fmod) && Shown(asset.ShowIf, old)
                   && !Shown(ev.ShowIf, file) && Shown(ev.ShowIf, fmod) && !Shown(ev.ShowIf, old) && Shown(ps.ShowIf, fmod)
                   && Shown(loop.ShowIf, file) && !Shown(loop.ShowIf, fmod);
        });

        PlayWindow SoundWorld(Action<Project, BehaviorData> setup, bool fullCatalog = true) => t.Sim("[]", PhysicsMode.None, (p, s) =>
        {
            var layer = s.Layers.First(l => l.Kind == LayerKind.Base);
            var snd = fullCatalog ? BehaviorCatalog.Info(BehaviorKind.SoundEmitter).CreateData() : new BehaviorData { Kind = BehaviorKind.SoundEmitter };
            snd.Texts["asset"] = ProjectIO.SaveSound(p, SoundSynth.Chop(), "fmod_chop");
            setup(p, snd);
            layer.Objects.Add(new GameObjectData { Id = 40, Name = "Radio", X = 300, Y = 200, Width = 40, Height = 40, Behaviors = { snd } });
        });

        t.Test("fmod sound: an older project's Sound (no 'source') still plays its file the classic way — no FMOD request", () =>
        {
            var p = SoundWorld((pr, b) => { }, fullCatalog: false);
            p.TSteps(3);
            return p.TSounds.Contains("fmod_chop.wav") && p.TFmod.Count == 0 && !p.TFmodCore;
        });
        t.Test("fmod sound: a Sound set to an FMOD event starts it on start with its parameters (recorded headless, nothing heard, no file played)", () =>
        {
            var p = SoundWorld((pr, b) => { b.Texts["source"] = "fmod"; b.Texts["event"] = "Music/Forest"; b.Texts["fmodParams"] = "Intensity=0.5;Rain=1"; });
            p.TSteps(3);
            return p.TFmod.Contains("▶ event:/Music/Forest") && p.TFmod.Contains("◆ event:/Music/Forest Intensity=0.5") && p.TFmod.Contains("◆ event:/Music/Forest Rain=1")
                   && p.TSoundPlaying("Radio") && !p.TSounds.Contains("fmod_chop.wav") && p.TLogHas("(muted in test)");
        });
        t.Test("fmod sound: with 'Only while', the event starts when the formula turns true and stops when it turns false", () =>
        {
            var p = SoundWorld((pr, b) => { b.Texts["source"] = "fmod"; b.Texts["event"] = "event:/Ambience/Rain"; b.Texts["cond"] = "raining >= 1"; });
            p.TSteps(3);
            bool silent = p.TFmod.Count == 0 && !p.TSoundPlaying("Radio");
            p.TSetVar("raining", 1); p.TSteps(2);
            bool on = p.TFmod.Count(x => x == "▶ event:/Ambience/Rain") == 1 && p.TSoundPlaying("Radio");
            p.TSetVar("raining", 0); p.TSteps(2);
            return silent && on && p.TFmod.Contains("■ event:/Ambience/Rain") && !p.TSoundPlaying("Radio");
        });
        t.Test("fmod sound: an FMOD event with none picked warns once in the console and plays nothing", () =>
        {
            var p = SoundWorld((pr, b) => { b.Texts["source"] = "fmod"; });
            p.TSteps(3);
            return p.TFmod.Count == 0 && p.TLogHas("none is picked") && !p.TSounds.Contains("fmod_chop.wav");
        });

        // ---------------- rule actions ----------------
        t.Test("fmod rules: fmodevent plays an event (named or not) and stops it; stop with nothing picked stops every event", () =>
        {
            var p = t.Sim(Jq("[{'when':'start','do':'fmodevent','event':'Music/Forest','name':'music'},{'when':'after','seconds':0.05,'do':'fmodevent','event':'Music/Forest','action':'stop','name':'music'}," +
                             "{'when':'after','seconds':0.1,'do':'fmodevent','action':'stop'},{'when':'start','do':'fmodevent','event':'event:/SFX/Explosion','volume':0.5}]"), PhysicsMode.None, null);
            p.TSteps(12);
            var l = p.TFmod.ToList();
            return l.Contains("▶ event:/Music/Forest as music") && l.Contains("▶ event:/SFX/Explosion") && l.Contains("■ music") && l.Contains("■ every event")
                   && l.IndexOf("▶ event:/Music/Forest as music") < l.IndexOf("■ music");
        });
        t.Test("fmod rules: fmodparam sets a global parameter (no event) or an event's, from a formula — sent again only when the value changes", () =>
        {
            var p = t.Sim(Jq("[{'when':'start','do':'setvar','var':'speed','to':3},{'when':'always','do':'fmodparam','param':'Rain','value':'1'}," +
                             "{'when':'always','do':'fmodparam','event':'Vehicles/Car Engine','param':'RPM','value':'speed * 1000'}]"), PhysicsMode.None, null);
            p.TSteps(5);
            bool once = p.TFmod.Count(x => x == "◆ global Rain=1") == 1 && p.TFmod.Count(x => x == "◆ Vehicles/Car Engine RPM=3000") == 1;
            p.TSetVar("speed", 4); p.TSteps(2);
            return once && p.TFmod.Contains("◆ Vehicles/Car Engine RPM=4000");
        });
        t.Test("fmod rules: the blocks have 'Play an FMOD event' and 'Set an FMOD parameter' with event / parameter pickers, the AI's rule docs list them", () =>
        {
            var acts = BlockEditorWindow.MakeActions();
            var ev = acts.FirstOrDefault(a => a.Id == "fmodevent");
            var pa = acts.FirstOrDefault(a => a.Id == "fmodparam");
            var docs = BlockEditorWindow.RuleDocs();
            return ev != null && pa != null && ev.Fields.Any(f => f.Key == "event" && f.Type == "fmodevent") && ev.Fields.Any(f => f.Key == "action" && f.Options!.SequenceEqual(new[] { "play", "stop" }))
                   && pa.Fields.Any(f => f.Key == "param" && f.Type == "fmodparam") && pa.Fields.Any(f => f.Key == "value" && f.Type == "val")
                   && AiSpecFix.Actions.Contains("fmodevent") && AiSpecFix.Actions.Contains("fmodparam")
                   && docs.Any(d => d.id == "fmodevent" && d.doc.Contains("action=play|stop")) && docs.Any(d => d.id == "fmodparam" && d.doc.Contains("param"));
        });
        t.Test("fmod rules: a rule with fmodevent survives the block editor round trip", () =>
        {
            var rt = t.RT(Jq("[{'when':'keypress','key':'E','do':'fmodevent','event':'event:/SFX/Door','action':'play','name':'door','volume':'0.7'}]"));
            return rt.Contains("\"fmodevent\"") && rt.Contains("event:/SFX/Door") && rt.Contains("\"door\"");
        });

        // ---------------- cutscene step ----------------
        t.Test("fmod cutscene: the step 'Play an FMOD event' plays and stops an event (also written as 'fmod' / 'playevent')", () =>
        {
            var def = StepCatalog.Find(SeqMode.Cutscene, "fmodevent");
            var bossDef = StepCatalog.Find(SeqMode.Boss, "fmodevent");
            var parsed = SequenceJson.ParseCutscene(Jq("{'steps':[{'do':'fmod','event':'Music/Boss'},{'do':'playevent','event':'Music/Calm'}]}"), out var err);
            var p = t.Sim("[]", PhysicsMode.None, CutArena("{'steps':[{'do':'fmodevent','event':'Music/Boss'},{'do':'wait','time':0.05},{'do':'fmodevent','event':'Music/Boss','action':'stop'}]}"));
            p.TSteps(10);
            return def != null && bossDef != null && def.Category == "Sound" && def.Fields.Any(f => f.Key == "event" && f.Type == "fmodevent")
                   && err == null && parsed.Steps.Count == 2 && parsed.Steps.All(s => s.Do == "fmodevent")
                   && p.TFmod.Contains("▶ event:/Music/Boss") && p.TFmod.Contains("■ event:/Music/Boss");
        });

        // ---------------- BuilderEngine ----------------
        t.Test("fmod BuilderEngine: a BuilderEngine object's FMOD request (World.FmodControl) goes to the same place (recorded headless)", () =>
        {
            var p = t.Sim("[]", PhysicsMode.None, null);
            p.TSteps(1);
            p.TBuilderFmod(new BE.FmodRequest(BE.FmodAction.Play, "Music/Cave", "cave"));
            p.TBuilderFmod(new BE.FmodRequest(BE.FmodAction.SetGlobal, Parameter: "Danger", Value: 2));
            p.TBuilderFmod(new BE.FmodRequest(BE.FmodAction.Stop, Name: "cave"));
            return p.TFmod.SequenceEqual(new[] { "▶ event:/Music/Cave as cave", "◆ global Danger=2", "■ cave" });
        });

        // ---------------- export ----------------
        t.Test("fmod export: opted in, fmod.dll (+ fmodstudio.dll) go next to the game; not opted in, any FMOD DLL is taken out", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "EE_Full_FmodExport_" + Guid.NewGuid().ToString("N")[..6]);
            var src = Path.Combine(dir, "src"); var game = Path.Combine(dir, "game");
            Directory.CreateDirectory(src); Directory.CreateDirectory(game);
            File.WriteAllText(Path.Combine(src, "fmod.dll"), "x"); File.WriteAllText(Path.Combine(src, "fmodstudio.dll"), "y");
            var copied = GameAudio.CopyFmodInto(game, new[] { Path.Combine(src, "fmod.dll"), Path.Combine(src, "fmodstudio.dll") });
            bool inGame = copied.Count == 2 && File.Exists(Path.Combine(game, "fmod.dll")) && File.Exists(Path.Combine(game, "fmodstudio.dll"));
            File.WriteAllText(Path.Combine(game, "Other.dll"), "z");
            GameAudio.ExportFmod(game, include: false);
            bool removed = !File.Exists(Path.Combine(game, "fmod.dll")) && !File.Exists(Path.Combine(game, "fmodstudio.dll")) && File.Exists(Path.Combine(game, "Other.dll"));
            return inGame && removed;
        });
        t.Test("fmod export: a project with FMOD events (or banks) is seen as using FMOD; one without isn't", () =>
        {
            var p = new Project { Name = "X" };
            var sc = new Scene { Name = "S" }; var l = new Layer(); sc.Layers.Add(l); p.Scenes.Add(sc);
            bool none = !GameAudio.ProjectUsesFmod(p);
            var snd = BehaviorCatalog.Info(BehaviorKind.SoundEmitter).CreateData(); snd.Texts["source"] = "fmod";
            l.Objects.Add(new GameObjectData { Name = "R", Behaviors = { snd } });
            bool beh = GameAudio.ProjectUsesFmod(p);
            snd.Texts["source"] = "file";
            var c = new BehaviorData { Kind = BehaviorKind.Custom }; c.Texts["script"] = Jq("[{'when':'start','do':'fmodevent','event':'Music/A'}]");
            l.Objects[0].Behaviors.Add(c);
            return none && beh && GameAudio.ProjectUsesFmod(p);
        });

        // ---------------- FMOD itself (only when installed here): silent ----------------
        bool installed = !BE.FmodLibrary.Disabled && BE.FmodLibrary.FindCore() != null;
        if (!installed)
        {
            t.Test("fmod smoke: SKIPPED — FMOD isn't installed on this computer (the checks above cover the classic fallback)", () => true);
            return;
        }
        t.Test("fmod smoke: FMOD Core (silent output) plays a generated WAV from a file and from memory, sets volume / pan, stops", () =>
        {
            var core = BE.FmodCore.TryCreate(out var why, silent: true);
            if (core == null) throw new Exception("FMOD Core didn't start: " + why);
            var dir = Path.Combine(Path.GetTempPath(), "EE_Full_FmodCore_" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(dir);
            var wav = Path.Combine(dir, "tune.wav");
            File.WriteAllBytes(wav, SoundSynth.Tune());
            try
            {
                var ch = core.Play(wav, 0.5, -0.5, loop: true);
                bool playing = !ch.IsNone && core.IsPlaying(ch) && core.SetVolume(ch, 0.2) && core.SetPan(ch, 0.7) && core.Update();
                core.Stop(ch); core.Update();
                bool stopped = !core.IsPlaying(ch) && !core.SetVolume(ch, 1); // a stopped channel just says no
                var mem = core.PlayMemory(SoundSynth.Chop(), 0.3);
                bool memOk = !mem.IsNone && core.IsPlaying(mem);
                core.StopAll(); core.Update();
                return playing && stopped && memOk && !core.IsPlaying(mem) && core.Silent && core.VersionText.StartsWith("2.");
            }
            finally { core.Dispose(); }
        });
        t.Test("fmod smoke: GameAudio's status shows the installed FMOD version", () =>
            GameAudio.InstalledVersion() is { } v && v.StartsWith("2.") && (GameAudio.Backend == BE.AudioBackend.Classic || GameAudio.StatusText() == L.F("FMOD {0} ✓", v)));

        var examples = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), BE.FmodLibrary.InstallFolder, "api", "studio", "examples", "media");
        bool studio = BE.FmodLibrary.LoadStudio() && File.Exists(Path.Combine(examples, "Vehicles.bank"));
        if (!studio)
        {
            t.Test("fmod smoke: FMOD Studio SKIPPED — fmodstudio.dll or the FMOD Engine's example banks aren't here", () => true);
            return;
        }
        t.Test("fmod smoke: a project's banks (fmod\\Desktop) are listed for the pickers, and an event plays, takes a parameter and stops (silent)", () =>
        {
            var proj = Path.Combine(Path.GetTempPath(), "EE_Full_FmodBanks_" + Guid.NewGuid().ToString("N")[..6]);
            var desk = Path.Combine(proj, BE.FmodStudio.FolderName, "Desktop");
            Directory.CreateDirectory(desk);
            foreach (var b in new[] { "Master.bank", "Master.strings.bank", "Vehicles.bank" }) File.Copy(Path.Combine(examples, b), Path.Combine(desk, b));
            BE.FmodStudio.ClearCache();
            var options = GameAudio.EventOptions(proj, out var why);
            var (banks, events, text) = GameAudio.ProjectBanks(proj);
            var withParam = BE.FmodStudio.ListEvents(proj, out _).FirstOrDefault(e => e.Parameters.Count > 0 && !e.Parameters[0].Global);
            if (withParam == null) throw new Exception("no event with a parameter in the example banks");
            var host = new BE.FmodEventHost(proj, silent: true);
            try
            {
                bool open = host.Studio != null;
                host.Handle(new BE.FmodRequest(BE.FmodAction.Play, withParam.Path, "engine", X: 100, Y: 50));
                host.SetListener(0, 0);
                host.Update();
                bool playing = host.IsPlaying("engine");
                host.Handle(new BE.FmodRequest(BE.FmodAction.SetParameter, Name: "engine", Parameter: withParam.Parameters[0].Name, Value: withParam.Parameters[0].Max));
                host.Handle(new BE.FmodRequest(BE.FmodAction.Stop, Name: "engine", Fade: false));
                host.Update();
                return open && options.Count > 0 && options.Any(o => o.value == withParam.Path) && banks == 3 && events == options.Count
                       && text == L.F("{0} bank(s), {1} event(s)", 3, events) && GameAudio.FindEvent(proj, withParam.Short) != null
                       && GameAudio.ParamOptions(proj).Any(o => o.value == withParam.Parameters[0].Name) && playing && !host.IsPlaying("engine");
            }
            finally { host.Dispose(); BE.FmodStudio.ClearCache(); }
        });
    }
}
