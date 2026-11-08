using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using BE = BuilderEngine.Script;

namespace EasyEngine;

// The EasyEngine ⇄ BuilderEngine link (BuilderLink.cs, MainWindow.BuilderLink.cs, MainWindow.LiveLink.cs,
// PlayWindow.BuilderBridge.cs): BuilderEngine's data survives EasyEngine, BuilderEngine folders open and save back, an object
// built on BuilderEngine has no EasyEngine behaviors to edit (and the AI leaves it alone), the live link reloads keeping
// the editor's state, and its mechanisms run when the game plays.
public partial class App
{
    partial void FeatureTestsBuilderLink(FullTestKit t)
    {
        string Temp(string tag)
        {
            var d = Path.Combine(Path.GetTempPath(), "EE_Full_Link_" + tag + "_" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(d);
            return d;
        }
        static JsonObject Block(params (string key, JsonNode? value)[] parts)
        {
            var o = new JsonObject();
            foreach (var (k, v) in parts) o[k] = v;
            return o;
        }
        static JsonArray Mechs(string name, double value) =>
            new(new JsonObject { ["name"] = name, ["params"] = new JsonObject { ["value"] = value, ["who"] = "player", ["on"] = true } });

        // ---------------- unknown data survives
        t.Test("builder link: an .eeproj with Builder blocks (project, scene, layer, object, prefab) round-trips through EasyEngine Load/Save unchanged", () =>
        {
            var p = ProjectIO.CreateNew("Linked", Temp("RT"));
            var root = JsonNode.Parse(File.ReadAllText(p.FilePath!))!.AsObject();
            root["Builder"] = Block(("linkVersion", 1), ("mechanisms", new JsonObject { ["Sign"] = "mechanism Sign\n    when start\n        print \"hi\"\n" }), ("extra", new JsonObject { ["future"] = new JsonArray(1, 2) }));
            var scene = root["Scenes"]![0]!.AsObject();
            scene["Builder"] = Block(("extra", new JsonObject { ["tileLayers"] = new JsonArray() }));
            var layer = scene["Layers"]![1]!.AsObject();
            layer["Builder"] = Block(("note", "a layer key BuilderEngine might add one day"));
            var ground = layer["Objects"]![0]!.AsObject();
            ground["Builder"] = Block(("mechanisms", Mechs("Coin", 5)), ("tags", new JsonArray("gold")), ("extra", new JsonObject { ["z"] = 2 }));
            root["Prefabs"]![0]!.AsObject()["Builder"] = Block(("note", "prefab data"));
            string text = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(p.FilePath!, text);
            var loaded = ProjectIO.Load(p.FilePath!);
            ProjectIO.Save(loaded);
            string back = File.ReadAllText(p.FilePath!);
            var obj = loaded.Scenes[0].Layers[1].Objects[0];
            return back == text && BuilderLink.IsBuilt(obj) && BuilderLink.MechanismNames(obj).SequenceEqual(new[] { "Coin" })
                   && BuilderLink.Tags(obj).SequenceEqual(new[] { "gold" }) && BuilderLink.ProjectMechanisms(loaded).ContainsKey("Sign") && BuilderLink.IsLinked(loaded);
        });

        t.Test("builder link: copies keep the Builder data (paste, undo snapshot, prefab apply); a plain object is not built", () =>
        {
            var p = ProjectIO.CreateNew("Copies", Temp("CP"));
            var layer = p.GetStartScene().Layers.First(l => l.Kind == LayerKind.Base);
            var coin = TestScenarios.Built(new GameObjectData { Id = 50, Name = "Coin", X = 300, Y = 300 }, "Coin", new { value = 2 });
            layer.Objects.Add(coin);
            var ed = new MainWindow(p);
            ed.TestSelect(ed.TestFindObject("Coin"));
            ed.TestCopy(); ed.TestPaste();
            var pasted = ed.TestScene.Layers.SelectMany(l => l.Objects).Where(o => o.Name.StartsWith("Coin")).ToList();
            var clone = JsonSerializer.Deserialize<GameObjectData>(JsonSerializer.Serialize(coin))!;
            EasyEngine.MainWindow.TestClearClipboard();
            return pasted.Count == 2 && pasted.All(BuilderLink.IsBuilt) && BuilderLink.IsBuilt(clone)
                   && !BuilderLink.IsBuilt(layer.Objects.First(o => o.Name == "Ground")) && !BuilderLink.IsBuilt(null);
        });

        // ---------------- BuilderEngine folders
        t.Test("builder link: a BuilderEngine folder opens (project.json), saves back, and BuilderEngine reads it with its mechanisms intact", () =>
        {
            var dir = Temp("BE");
            var bp = new BE.Project { Name = "Made in BuilderEngine" };
            var s = new BE.Scene { Name = "Level 1", Width = 800, Height = 450 };
            var coin = new BE.SceneObject { Id = 1, Name = "Coin", X = 100, Y = 200, Width = 24, Height = 24, Color = "#F4C03B", Tags = ["gold"] };
            coin.Mechanisms.Add(new BE.SceneMechanism { Name = "Coin", Params = { ["value"] = 3.0 } });
            s.Objects.Add(coin);
            s.Objects.Add(new BE.SceneObject { Id = 2, Name = "Floor", X = 0, Y = 400, Width = 800, Height = 50 });
            bp.Scenes.Clear(); bp.Scenes.Add(s);
            bp.Mechanisms["Spinner"] = "mechanism Spinner\n    when tick\n        self.x += 1\n";
            bp.Save(dir);
            string pj = Path.Combine(dir, "project.json");
            bool openable = BuilderLink.OpenablePath(dir) == pj && BuilderLink.OpenablePath(pj) == pj;
            var p = ProjectIO.Load(dir);
            var c = p.Scenes[0].Layers.SelectMany(l => l.Objects).First(o => o.Name == "Coin");
            bool loaded = p.IsBuilderEngine && p.FilePath == pj && BuilderLink.IsBuilt(c) && p.Prefabs.Count == 0 && BuilderLink.ProjectMechanisms(p).ContainsKey("Spinner");
            c.X = 150;
            p.Scenes[0].Layers.SelectMany(l => l.Objects).First(o => o.Name == "Floor").Color = "#123456";
            ProjectIO.Save(p);
            var again = BE.Project.Load(dir);
            var c2 = again.Scenes[0].Objects.First(o => o.Name == "Coin");
            bool savedBack = c2.X == 150 && c2.Mechanisms.Count == 1 && c2.Mechanisms[0].Name == "Coin" && Equals(c2.Mechanisms[0].Params["value"], 3.0)
                             && c2.Tags.Contains("gold") && again.Scenes[0].Objects.First(o => o.Name == "Floor").Color == "#123456"
                             && again.Mechanisms.TryGetValue("Spinner", out var src) && src.Contains("self.x += 1") && !File.Exists(Path.Combine(dir, "Made in BuilderEngine.eeproj"));
            var recent = new RecentProject { Path = pj };
            ProjectIO.RenameProject(pj, "Renamed");
            return openable && loaded && savedBack && recent.IsBuilderEngine && !new RecentProject { Path = "x.eeproj" }.IsBuilderEngine
                   && BE.Project.Load(dir).Name == "Renamed" && BuilderLink.OpenablePath(Path.Combine(dir, "nothing.txt")) == null;
        });

        t.Test("builder link: a BuilderEngine folder whose project.json broke meanwhile is never saved over (its mechanisms stay)", () =>
        {
            var dir = Temp("BRK");
            var bp = new BE.Project { Name = "Fragile" };
            bp.Mechanisms["Spinner"] = "mechanism Spinner\n    when tick\n        self.x += 1\n";
            bp.Save(dir);
            var p = ProjectIO.Load(dir);
            string pj = Path.Combine(dir, "project.json"), bs = Path.Combine(dir, "mechanisms", "Spinner.bs");
            File.WriteAllText(pj, "{ \"name\": \"Fragile\", \"scenes\": [");
            bool threw = false;
            try { ProjectIO.Save(p); } catch (IOException) { threw = true; }
            return threw && File.Exists(bs) && File.ReadAllText(pj).EndsWith("[");
        });

        // ---------------- Built on BuilderEngine in the editor
        t.Test("builder link: a Built-on-BuilderEngine object shows one read-only card, no behavior cards and no ＋ behavior; the AI leaves it alone", () =>
        {
            var p = ProjectIO.CreateNew("Card", Temp("CARD"));
            var layer = p.GetStartScene().Layers.First(l => l.Kind == LayerKind.Base);
            layer.Objects.Add(TestScenarios.Built(new GameObjectData { Id = 60, Name = "Coin", X = 300, Y = 300 }, "Coin", new { value = 1 }));
            var ed = new MainWindow(p);
            ed.TestSelect(ed.TestFindObject("Coin"));
            bool card = ed.TestHasBuilderCard() && !ed.TestHasAddBehavior() && ed.TestBehaviorCards() == 0;
            ed.TestSelect(ed.TestFindObject("Player"));
            bool plain = !ed.TestHasBuilderCard() && ed.TestHasAddBehavior();
            int playerBehaviors = ed.TestFindObject("Player")!.Behaviors.Count;
            ed.TestSelect(ed.TestFindObject("Coin")); // the person asks with the coin selected
            var (_, summary) = ed.TestApplySpec("{\"edit\":[{\"name\":\"Coin\",\"behaviors\":[{\"kind\":\"Spin\"}],\"custom\":[{\"when\":\"start\",\"do\":\"jump\"}]}]}", "make the coin spin");
            var coin = ed.TestFindObject("Coin")!;
            bool ok = card && plain && coin.Behaviors.Count == 0 && summary.Contains("BuilderEngine") && ed.TestFindObject("Player")!.Behaviors.Count == playerBehaviors;
            if (!ok) throw new Exception($"card={card} plain={plain} behaviors={coin.Behaviors.Count} summary={summary}");
            return ok;
        });

        // ---------------- live link
        t.Test("builder link: the live link reloads an outside change keeping the scene, the selection and the zoom; Ctrl+Z brings back the old scene", () =>
        {
            var p = ProjectIO.CreateNew("Live", Temp("LIVE"));
            var ed = new MainWindow(p);
            ed.TestSelect(ed.TestFindObject("Player"));
            ed.TestZoom = 1.7;
            double x0 = ed.TestFindObject("Player")!.X;
            // BuilderEngine moves the player and saves (through the one converter, merging into the file)
            string before = File.ReadAllText(p.FilePath!);
            var bp = BE.EasyLink.FromEeproj(before, p.FolderPath!);
            bp.Scenes[0].Objects.First(o => o.Name == "Player").X = 444;
            BE.EasyLink.WriteAtomic(p.FilePath!, BE.EasyLink.ToEeproj(bp, before, p.FolderPath));
            bool reloaded = ed.TestReload();
            var sel = ed.TestSelected;
            bool kept = sel != null && sel.Name == "Player" && sel.X == 444 && Math.Abs(ed.TestZoom - 1.7) < 1e-9 && ed.TestScene == p.Scenes[0] && !ed.TestIsDirty;
            ed.TestUndo();
            bool undone = ed.TestFindObject("Player")!.X == x0;
            return reloaded && kept && undone;
        });

        // ---------------- the bridge (more in the "BuilderEngine Link" E2E)
        t.Test("builder link bridge: a BuilderEngine Coin is collected by an EasyEngine Platformer player and a Patrol moves, headless", () =>
        {
            var p = ProjectIO.CreateNew("Bridge", Temp("BRIDGE"));
            var layer = p.GetStartScene().Layers.First(l => l.Kind == LayerKind.Base);
            layer.Objects.Add(TestScenarios.Built(new GameObjectData { Id = 70, Name = "Coin", X = 300, Y = 380, Width = 24, Height = 24 }, "Coin", new { value = 2 }));
            layer.Objects.Add(TestScenarios.Built(new GameObjectData { Id = 71, Name = "Slime", X = 600, Y = 420, Width = 40, Height = 40 }, "Patrol", new { speed = 120, distance = 200 }));
            var pw = new PlayWindow(p, p.StartScene!);
            try
            {
                pw.TLoad(); pw.TSteps(3);
                double slime0 = pw.TX("Slime");
                bool running = pw.TBuilderRunning && pw.TBuilderOwned == 2;
                pw.TPlace("Player", 296, 360); pw.TSteps(30);
                bool collected = pw.TVar("score") == 2 && pw.TDead("Coin");
                return running && collected && pw.TX("Slime") > slime0 + 20;
            }
            finally { try { pw.Close(); } catch { } }
        });

        t.Test("builder link bridge: a scene without BuilderEngine objects starts no world", () =>
        {
            var p = ProjectIO.CreateNew("NoBridge", Temp("NOB"));
            var pw = new PlayWindow(p, p.StartScene!);
            try { pw.TLoad(); pw.TSteps(3); return !pw.TBuilderRunning; }
            finally { try { pw.Close(); } catch { } }
        });

        foreach (var d in Directory.GetDirectories(Path.GetTempPath(), "EE_Full_Link_*")) try { Directory.Delete(d, true); } catch { }
    }
}
