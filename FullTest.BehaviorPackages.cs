using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BE = BuilderEngine.Script;

namespace EasyEngine;

// Behavior packages (.behavior, BehaviorPackages.cs, MainWindow.Packages.cs, MainWindow.AiPackages.cs, the bridge in
// PlayWindow.BuilderBridge.cs): found in the project's behaviors/ (first) and the installed folder, broken files reported,
// offered in ＋ Add behavior and copied into the project, a picker per setting type with "show if", saved like any
// behavior, played through BuilderEngine (Twirl turns, Drift moves an object together with its Platformer, a repeatable
// one twice), exported with the game, and known to the AI (Build and Learn).
public partial class App
{
    partial void FeatureTestsBehaviorPackages(FullTestKit t)
    {
        string Temp(string tag)
        {
            var d = Path.Combine(Path.GetTempPath(), "EE_Full_Pkg_" + tag + "_" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(d);
            return d;
        }
        // a project (in its own temp folder) with these packages in its behaviors/, and these in the "installed" folder
        (Project p, string installed) Make(string tag, (string, string)[] own, (string, string)[]? inst = null)
        {
            var p = ProjectIO.CreateNew("Pkg " + tag, Temp(tag));
            TestPackages.Write(Path.Combine(p.FolderPath!, "behaviors"), own);
            var installed = Temp(tag + "_inst");
            TestPackages.Write(installed, inst ?? Array.Empty<(string, string)>());
            BehaviorPackages.InstalledFolder = installed;
            return (p, installed);
        }
        var keepInstalled = BehaviorPackages.InstalledFolder;
        GameObjectData Obj(Project p, string name) => p.GetStartScene().Layers.SelectMany(l => l.Objects).First(o => o.Name == name);
        GameObjectData Add(Project p, GameObjectData o) { o.Id = 900 + p.GetStartScene().Layers.Sum(l => l.Objects.Count); p.GetStartScene().Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(o); return o; }
        void Test(string name, Func<bool> f) => t.Test(name, () => { try { return f(); } finally { BehaviorPackages.InstalledFolder = keepInstalled; } });

        // ---------------- finding packages
        Test("packages: the project's copy wins over the installed one; broken files are reported (status, F1), never loaded", () =>
        {
            var (p, _) = Make("FIND",
                new[] { ("Drift.behavior", TestPackages.Drift(60, "the project's")), ("Broken.behavior", "{\"format\":\"builderengine.behavior\",\"version\":1,\"name\":\"Broken\",\"script\":\"mechanism Other\\n\"}"),
                        ("Garbage.behavior", "{ this is not json") },
                new[] { ("Drift.behavior", TestPackages.Drift(10, "the installed one")), ("Twirl.behavior", TestPackages.Twirl()) });
            var set = BehaviorPackages.For(p);
            bool found = set.Find("drift")?.Description == "the project's" && set.Find("Twirl") != null && set.Find("Broken") == null
                         && set.All.Count(x => x.Origin != BE.PackageOrigin.Shipped) == 2 && set.Find("Drift")!.Origin == BE.PackageOrigin.Project && set.Find("Twirl")!.Origin == BE.PackageOrigin.Installed
                         && set.Find("Spin")?.Origin == BE.PackageOrigin.Shipped && set.All.Count(x => x.Origin == BE.PackageOrigin.Shipped) >= 13;
            bool problems = set.Problems.Count == 2 && set.Problems.Any(x => x.StartsWith("Broken.behavior")) && set.Problems.Any(x => x.StartsWith("Garbage.behavior"));
            var ed = new MainWindow(p);
            ed.TestSelect(ed.TestFindObject("Player"));
            bool notice = ed.TestStatus.Contains("couldn't be read") && ed.TestStatus.Contains("Garbage.behavior");
            Obj(p, "Player").Behaviors.Add(TestPackages.Use("Drift"));
            var pw = new PlayWindow(p, p.StartScene!);
            try
            {
                pw.TLoad(); pw.TSteps(2);
                bool console = pw.TLogHas("couldn't be read") && pw.TLogHas("Broken.behavior") && pw.TBuilderRunning;
                if (!(found && problems && notice && console)) throw new Exception($"found={found} problems=[{string.Join(" | ", set.Problems)}] notice='{ed.TestStatus}' console={console}");
                return true;
            }
            finally { try { pw.Close(); } catch { } }
        });

        Test("packages: a file changed on disk is read again (the cache follows the files)", () =>
        {
            var (p, _) = Make("CACHE", new[] { ("Drift.behavior", TestPackages.Drift(60, "first")) });
            bool first = BehaviorPackages.For(p).Find("Drift")?.Description == "first";
            var file = Path.Combine(p.FolderPath!, "behaviors", "Drift.behavior");
            File.WriteAllText(file, TestPackages.Drift(60, "second, a little longer"));
            return first && BehaviorPackages.For(p).Find("Drift")?.Description == "second, a little longer";
        });

        // ---------------- the editor
        Test("packages: ＋ Add behavior offers them (📦); adding one copies it into the project; a repeatable one again, the others once", () =>
        {
            var (p, installed) = Make("PICK", Array.Empty<(string, string)>(), new[] { ("Twirl.behavior", TestPackages.Twirl()), ("Bell.behavior", TestPackages.Bell()) });
            var ed = new MainWindow(p);
            ed.TestSelect(ed.TestFindObject("Player"));
            var offered = ed.TestAddablePackages();
            bool added = ed.TestAddPackage("Twirl");
            var player = Obj(p, "Player");
            var twirl = player.Behaviors.FirstOrDefault(b => b.Kind == BehaviorKind.Package);
            bool data = twirl != null && twirl.Text("package") == "Twirl" && twirl.Num("speed") == 90 && twirl.Text("direction") == "clockwise";
            bool copied = File.Exists(Path.Combine(p.FolderPath!, "behaviors", "Twirl.behavior")) && BehaviorPackages.For(p).Find("Twirl")!.SourcePath!.StartsWith(p.FolderPath!);
            ed.TestSelect(ed.TestFindObject("Player"));
            var again = ed.TestAddablePackages();
            ed.TestAddPackage("Bell");
            ed.TestSelect(ed.TestFindObject("Player"));
            ed.TestAddPackage("Bell");
            int bells = player.Behaviors.Count(b => BehaviorPackages.NameOf(b) == "Bell");
            ed.TestSelect(ed.TestFindObject("Player"));
            var cards = ed.TestCardTitles();
            bool numbered = cards.Any(c => c.Contains("Bell 1")) && cards.Any(c => c.Contains("Bell 2")) && cards.Any(c => c.Contains("🌀  Twirl"));
            bool ok = offered.Contains("Twirl") && offered.Contains("Bell") && added && data && copied && !again.Contains("Twirl") && again.Contains("Bell") && bells == 2 && numbered;
            if (!ok) throw new Exception($"offered=[{string.Join(",", offered)}] added={added} data={data} copied={copied} again=[{string.Join(",", again)}] bells={bells} cards=[{string.Join(" | ", cards)}]");
            return ok;
        });

        Test("packages: each setting gets EasyEngine's picker for its type; 'show if' hides one until its condition holds", () =>
        {
            var (p, _) = Make("CARD", new[] { ("Gadget.behavior", TestPackages.Gadget()) });
            var pkg = BehaviorPackages.For(p).Find("Gadget")!;
            var obj = Add(p, new GameObjectData { Name = "Thing", X = 300, Y = 300, Width = 40, Height = 40 });
            var bh = BehaviorPackages.CreateData(pkg);
            obj.Behaviors.Add(bh);
            var ed = new MainWindow(p);
            var rows = ed.TestPackageRows(obj, bh);
            bool Has(string name, params string[] kinds) => rows.TryGetValue(name, out var k) && kinds.All(k.Contains);
            bool pickers = Has("power", "spinner", "TextBox") && Has("delay", "spinner") && Has("loud", "CheckBox") && Has("title", "TextBox")
                           && Has("noise", "Button") && Has("picture", "ComboBox") && Has("tint", "ColorButton") && Has("trigger", "ComboBox", "Button")
                           && Has("target", "ComboBox") && Has("next_scene", "ComboBox") && Has("route", "path") && Has("pose", "ComboBox")
                           && Has("steps", "list") && Has("mode", "ComboBox") && !Has("title", "ComboBox");
            bool hidden = !rows.ContainsKey("wild_level") && !BehaviorPackages.Shows(pkg, pkg.AllParams().First(x => x.Name == "wild_level"), bh);
            bh.Texts["mode"] = "wild";
            bool shown = ed.TestPackageRows(obj, bh).ContainsKey("wild_level");
            // stored in the right bags: numbers and on/off in Nums, the rest in Texts (a key as EasyEngine's picker spells it)
            bool bags = bh.Nums["power"] == 5 && bh.Nums["delay"] == 1.5 && bh.Nums["loud"] == 0 && bh.Texts["tint"] == "#FF8800" && bh.Texts["trigger"] == "Space"
                        && bh.Texts["route"] == "[]" && bh.Texts["steps"] == "[1,2]" && bh.Texts["target"] == "Player";
            // and handed to BuilderEngine converted for its types
            var rt = BehaviorPackages.RuntimeSettings(pkg, bh);
            bool runtime = rt["loud"] is false && rt["trigger"] as string == "space" && rt["steps"] is List<object?> { Count: 2 } && rt["power"] is 5.0;
            bool ok = pickers && hidden && shown && bags && runtime;
            if (!ok) throw new Exception($"pickers={pickers} hidden={hidden} shown={shown} bags={bags} runtime={runtime} rows: {string.Join(" · ", rows.Select(r => r.Key + "=" + string.Join("+", r.Value)))}");
            return ok;
        });

        Test("packages: a number setting keeps to its limits (the wheel steps by its step); keys convert both ways", () =>
        {
            var r = new NumberWheel.WheelRange(0, 10, 0.5);
            bool wheel = NumberWheel.StepIn(r, "9.5", 1) == "10" && NumberWheel.StepIn(r, "10", 1) == "10" && NumberWheel.StepIn(r, "1", -1) == "0.5"
                         && NumberWheel.StepIn(r, "0", -1) == "0" && NumberWheel.StepIn(r, "2", 1, System.Windows.Input.ModifierKeys.Shift) == "7";
            bool keys = BehaviorPackages.EeKey("space") == "Space" && BehaviorPackages.EeKey("e") == "E" && BehaviorPackages.EeKey("shift") == "LeftShift" && BehaviorPackages.EeKey("1") == "D1"
                        && BehaviorPackages.BeKey("Space") == "space" && BehaviorPackages.BeKey("LeftShift") == "shift" && BehaviorPackages.BeKey("D1") == "d1" && BehaviorPackages.BeKey("Enter") == "enter";
            return wheel && keys;
        });

        Test("packages: saving and loading keeps the package's name and every setting", () =>
        {
            var (p, _) = Make("SAVE", new[] { ("Gadget.behavior", TestPackages.Gadget()) });
            var obj = Add(p, new GameObjectData { Name = "Thing", X = 10, Y = 10 });
            obj.Behaviors.Add(TestPackages.Use("Gadget", new() { ["power"] = 7.5, ["loud"] = 1 }, new() { ["mode"] = "wild", ["route"] = "[[0,0],[100,50]]", ["trigger"] = "E" }));
            obj.Behaviors.Add(TestPackages.Use("NotOnThisComputer", new() { ["x"] = 3 }, new() { ["y"] = "kept" }));
            ProjectIO.Save(p);
            var back = ProjectIO.Load(p.FilePath!);
            var b = back.GetStartScene().Layers.SelectMany(l => l.Objects).First(o => o.Name == "Thing").Behaviors;
            return b.Count == 2 && b.All(x => x.Kind == BehaviorKind.Package) && b[0].Text("package") == "Gadget" && b[0].Num("power") == 7.5 && b[0].Num("loud") == 1
                   && b[0].Text("mode") == "wild" && b[0].Text("route") == "[[0,0],[100,50]]" && b[0].Text("trigger") == "E"
                   && b[1].Text("package") == "NotOnThisComputer" && b[1].Num("x") == 3 && b[1].Text("y") == "kept";
        });

        Test("packages: a package that isn't installed keeps its data, its card says so, the game says so once and plays on", () =>
        {
            var (p, _) = Make("MISS", Array.Empty<(string, string)>());
            var player = Obj(p, "Player");
            player.Behaviors.Add(TestPackages.Use("Ghost", new() { ["speed"] = 3 }));
            var ed = new MainWindow(p);
            ed.TestSelect(ed.TestFindObject("Player"));
            bool card = ed.TestCardTitles().Any(c => c.Contains("Ghost"));
            var pw = new PlayWindow(p, p.StartScene!);
            try
            {
                pw.TLoad(); pw.TSteps(5);
                return card && pw.TLogHas("isn't installed") && pw.TLogCount("Packages", "Ghost") == 1 && pw.TLog.Errors == 0 && player.Behaviors.Last().Num("speed") == 3;
            }
            finally { try { pw.Close(); } catch { } }
        });

        // ---------------- playing
        Test("packages bridge: a Spin-like package (Twirl) turns an object in headless play, either way, at its speed", () =>
        {
            var (p, _) = Make("TWIRL", new[] { ("Twirl.behavior", TestPackages.Twirl()) });
            Add(p, new GameObjectData { Name = "Gem", X = 300, Y = 200, Width = 40, Height = 40 }).Behaviors.Add(TestPackages.Use("Twirl", new() { ["speed"] = 180 }));
            Add(p, new GameObjectData { Name = "Saw", X = 500, Y = 200, Width = 40, Height = 40 }).Behaviors.Add(TestPackages.Use("Twirl", new() { ["speed"] = 90 }, new() { ["direction"] = "counter" }));
            var pw = new PlayWindow(p, p.StartScene!);
            try
            {
                pw.TLoad(); pw.TSteps(60); // ≈ 0.96 s
                double gem = pw.TRot("Gem"), saw = pw.TRot("Saw");
                bool ok = pw.TBuilderRunning && pw.TPackageObjects == 2 && Math.Abs(gem - 180 * 0.96) < 12 && Math.Abs(saw + 90 * 0.96) < 8 && pw.TX("Gem") == 300 && pw.TBuilderErrors.Length == 0;
                if (!ok) throw new Exception($"running={pw.TBuilderRunning} objects={pw.TPackageObjects} gem={gem:0.#} saw={saw:0.#} x={pw.TX("Gem")} errors=[{pw.TBuilderErrors}]");
                return ok;
            }
            finally { try { pw.Close(); } catch { } }
        });

        Test("packages bridge: a package moves an object together with its Platformer (both move it; it still stands on the ground)", () =>
        {
            double Run(double drift, bool walk)
            {
                var (p, _) = Make("DRIFT", new[] { ("Drift.behavior", TestPackages.Drift()) });
                if (drift != 0) Obj(p, "Player").Behaviors.Add(TestPackages.Use("Drift", new() { ["speed"] = drift }));
                var pw = new PlayWindow(p, p.StartScene!);
                try
                {
                    pw.TLoad(); pw.TSteps(20); // lands
                    double x0 = pw.TX("Player"), y0 = pw.TY("Player");
                    if (walk) pw.TKey("D", true);
                    pw.TSteps(60);
                    pw.TKey("D", false);
                    if (Math.Abs(pw.TY("Player") - y0) > 2 || !pw.TGrounded("Player")) throw new Exception($"the player left the ground: y {y0:0.#} → {pw.TY("Player"):0.#}");
                    return pw.TX("Player") - x0;
                }
                finally { try { pw.Close(); } catch { } }
            }
            double walkOnly = Run(0, true), walkDrift = Run(100, true), driftOnly = Run(100, false), still = Run(0, false);
            bool ok = Math.Abs(still) < 1 && Math.Abs(driftOnly - 96) < 8 && Math.Abs(walkDrift - walkOnly - 96) < 10 && walkOnly > 100;
            if (!ok) throw new Exception($"walk={walkOnly:0.#} walk+drift={walkDrift:0.#} drift={driftOnly:0.#} still={still:0.#}");
            return ok;
        });

        Test("packages bridge: a repeatable package twice on one object — each runs with its own settings (Bell: E adds 1, Q adds 5)", () =>
        {
            var (p, _) = Make("BELL", new[] { ("Bell.behavior", TestPackages.Bell()) });
            p.GetStartScene().Variables["rings"] = 0;
            var bell = Add(p, new GameObjectData { Name = "Bell", X = 600, Y = 300, Width = 30, Height = 30 });
            bell.Behaviors.Add(TestPackages.Use("Bell", new() { ["amount"] = 1 }, new() { ["press"] = "E" }));
            bell.Behaviors.Add(TestPackages.Use("Bell", new() { ["amount"] = 5 }, new() { ["press"] = "Q" }));
            var pw = new PlayWindow(p, p.StartScene!);
            try
            {
                pw.TLoad(); pw.TSteps(3);
                void Press(string k) { pw.TKey(k, true); pw.TSteps(2); pw.TKey(k, false); pw.TSteps(2); }
                Press("E"); double afterE = pw.TVar("rings");
                Press("Q"); double afterQ = pw.TVar("rings");
                bool ok = afterE == 1 && afterQ == 6 && pw.TPackageVar("Bell", "amount", 1) is 5.0 && pw.TBuilderErrors.Length == 0;
                if (!ok) throw new Exception($"after E {afterE}, after Q {afterQ}, second amount {pw.TPackageVar("Bell", "amount", 1)}, errors [{pw.TBuilderErrors}]");
                return ok;
            }
            finally { try { pw.Close(); } catch { } }
        });

        Test("packages bridge: a package with a script error is said once in the console; the game plays on", () =>
        {
            var bad = TestPackages.Json("Oops", "⚠", "Tests", "Breaks when it runs.", "mechanism Oops\n    when start\n        uses_nothing_here 1 2 3\n", new System.Text.Json.Nodes.JsonArray());
            var (p, _) = Make("ERR", new[] { ("Oops.behavior", bad) });
            Obj(p, "Player").Behaviors.Add(TestPackages.Use("Oops"));
            var pw = new PlayWindow(p, p.StartScene!);
            try { pw.TLoad(); pw.TSteps(30); return pw.TExists("Player") && pw.TLog.Errors <= 1; }
            finally { try { pw.Close(); } catch { } }
        });

        Test("packages: the ones shipped with BuilderEngine are offered too (📦, 'comes with BuilderEngine'); one plays here and is copied into the project; the AI keeps EasyEngine's built-in", () =>
        {
            var (p, _) = Make("SHIP", Array.Empty<(string, string)>());
            var ed = new MainWindow(p);
            ed.TestSelect(ed.TestFindObject("Player"));
            var offered = ed.TestAddablePackages();
            bool listed = offered.Contains("Spin") && offered.Contains("TriggerArea") && offered.Contains("Spawner");
            var gem = Add(p, new GameObjectData { Name = "Gem", X = 300, Y = 200, Width = 40, Height = 40 });
            ed.TestSelect(gem);
            bool added = ed.TestAddPackage("Spin");
            var spin = gem.Behaviors.FirstOrDefault(b => b.Kind == BehaviorKind.Package);
            bool copied = File.Exists(Path.Combine(p.FolderPath!, "behaviors", "Spin.behavior")) && BehaviorPackages.For(p).Find("Spin")!.Origin == BE.PackageOrigin.Project;
            if (spin != null) spin.Nums["speed"] = 360;
            string prompt = ed.TestBuildPrompt("make the coin spin");
            bool aiKeepsBuiltIn = !prompt.Contains("(a behavior package)") && !prompt.Contains("Behavior packages") && prompt.Contains("Spin — ");
            var pw = new PlayWindow(p, p.StartScene!);
            try
            {
                pw.TLoad(); pw.TSteps(60);
                double rot = pw.TRot("Gem");
                bool ok = listed && added && spin?.Text("package") == "Spin" && copied && aiKeepsBuiltIn && Math.Abs(rot - 360 * 0.96) < 20 && pw.TBuilderErrors.Length == 0;
                if (!ok) throw new Exception($"listed={listed} added={added} copied={copied} ai={aiKeepsBuiltIn} rot={rot:0.#} errors=[{pw.TBuilderErrors}] offered=[{string.Join(",", offered)}]");
                return ok;
            }
            finally { try { pw.Close(); } catch { } }
        });

        // ---------------- export
        Test("packages: exporting copies the project's behaviors/ and an installed package it uses", () =>
        {
            var (p, _) = Make("EXP", new[] { ("Twirl.behavior", TestPackages.Twirl()) }, new[] { ("Drift.behavior", TestPackages.Drift()), ("Bell.behavior", TestPackages.Bell()) });
            Obj(p, "Player").Behaviors.Add(TestPackages.Use("Twirl"));
            Obj(p, "Player").Behaviors.Add(TestPackages.Use("Drift"));
            ProjectIO.Save(p);
            var game = Path.Combine(Temp("EXP_out"), "game");
            EasyEngine.MainWindow.CopyGameFiles(p, p.FolderPath!, game);
            var dir = Path.Combine(game, "behaviors");
            return File.Exists(Path.Combine(dir, "Twirl.behavior")) && File.Exists(Path.Combine(dir, "Drift.behavior")) && !File.Exists(Path.Combine(dir, "Bell.behavior"))
                   && BE.BehaviorPackage.Discover(new[] { dir }).Count == 2;
        });

        // ---------------- the AI
        Test("packages AI: a reply adds a package by its name (params cleaned up like a built-in's, limits kept); Package + texts.package works too; remove takes it off", () =>
        {
            var (p, _) = Make("AI", new[] { ("Twirl.behavior", TestPackages.Twirl()) }, new[] { ("Drift.behavior", TestPackages.Drift()) });
            var ed = new MainWindow(p);
            var (ok1, _) = ed.TestApplySpec("{\"edit\":[{\"name\":\"Player\",\"behaviors\":[{\"kind\":\"twirl\",\"Speed\":\"360\",\"direction\":\"Counter\"}]}]}", "make the player twirl backwards fast");
            var player = Obj(p, "Player");
            var tw = player.Behaviors.FirstOrDefault(b => BehaviorPackages.NameOf(b) == "Twirl");
            bool named = ok1 && tw != null && tw.Num("speed") == 360 && tw.Text("direction") == "counter";
            ed.TestApplySpec("{\"create\":[{\"name\":\"Leaf\",\"x\":300,\"y\":200,\"width\":20,\"height\":20,\"behaviors\":[{\"kind\":\"Package\",\"texts\":{\"package\":\"Drift\"},\"nums\":{\"speed\":5000}}]}]}", "a leaf that drifts in the wind");
            var leaf = ed.TestFindObject("Leaf");
            var dr = leaf?.Behaviors.FirstOrDefault(b => BehaviorPackages.NameOf(b) == "Drift");
            bool generic = dr != null && dr.Num("speed") == 600 && File.Exists(Path.Combine(p.FolderPath!, "behaviors", "Drift.behavior"));
            ed.TestApplySpec("{\"edit\":[{\"name\":\"Player\",\"remove\":[\"Twirl\"]}]}", "stop the player twirling");
            bool removed = !player.Behaviors.Any(b => BehaviorPackages.NameOf(b) == "Twirl");
            // a built-in with that name wins: "Spin" stays the built-in
            ed.TestApplySpec("{\"edit\":[{\"name\":\"Player\",\"behaviors\":[{\"kind\":\"Spin\"}]}]}", "spin");
            bool builtIn = player.Behaviors.Any(b => b.Kind == BehaviorKind.Spin);
            bool ok = named && generic && removed && builtIn;
            if (!ok) throw new Exception($"named={named} generic={generic} removed={removed} builtIn={builtIn} twirl=[{(tw == null ? "none" : string.Join(",", tw.Nums.Select(n => n.Key + "=" + n.Value)) + " " + string.Join(",", tw.Texts.Select(x => x.Key + "=" + x.Value)))}]");
            return ok;
        });

        Test("packages AI: the Build prompt lists them with the behaviors and details the one a request is about; Learn explains it", () =>
        {
            var (p, _) = Make("PROMPT", new[] { ("Twirl.behavior", TestPackages.Twirl()), ("Drift.behavior", TestPackages.Drift()) });
            Obj(p, "Player").Behaviors.Add(TestPackages.Use("Drift"));
            var ed = new MainWindow(p);
            string plain = ed.TestBuildPrompt("make the player jump higher"), asked = ed.TestBuildPrompt("make the gem rotate like a saw");
            bool list = plain.Contains("Behavior packages") && plain.Contains("Twirl") && plain.Contains("Drift") && !plain.Contains("Twirl — ");
            bool detail = asked.Contains("Twirl — Turns the object round and round.") && asked.Contains("speed=90") && asked.Contains("(clockwise|counter)") && asked.Contains("Add Twirl to make a gem");
            bool scene = plain.Contains("[Platformer,FollowCamera,Health,Drift]") || plain.Contains(",Drift]");
            string learn = ed.TestTeachPrompt("how do I make something twirl?", true);
            bool teach = learn.Contains("BEHAVIOR PACKAGES") && learn.Contains("🌀 Twirl (📦 behavior package, Movement)") && learn.Contains("Add Twirl to make a gem") && learn.Contains("Direction: clockwise / counter");
            bool ok = list && detail && scene && teach;
            if (!ok) throw new Exception($"list={list} detail={detail} scene={scene} teach={teach}\n{asked}");
            return ok;
        });

        foreach (var d in Directory.GetDirectories(Path.GetTempPath(), "EE_Full_Pkg_*")) try { Directory.Delete(d, true); } catch { }
    }
}
