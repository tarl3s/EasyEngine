using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace EasyEngine;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // exported game: EasyEngine.exe --play <project.eeproj>
        var playIdx = Array.IndexOf(e.Args, "--play");
        if (playIdx >= 0 && playIdx + 1 < e.Args.Length) { RunStandalone(e.Args[playIdx + 1]); return; }

        if (e.Args.Contains("--selftest")) { RunSelfTest(); return; }

        if (e.Args.Contains("--aitest")) { RunAiTest(e.Args); return; }

        if (e.Args.Contains("--makegame")) { RunMakeGame(); return; }

        if (e.Args.Contains("--exprtest")) { RunExprTest(); return; }

        if (e.Args.Contains("--behtest")) { RunBehTest(); return; }

        if (e.Args.Contains("--aijsontest")) { RunAiJsonTest(); return; }

        // exported standalone game: a 'game' folder with a .eeproj sits next to the exe → play it directly
        if (!e.Args.Contains("--editor"))
        {
            try
            {
                var gameDir = Path.Combine(AppContext.BaseDirectory, "game");
                if (Directory.Exists(gameDir))
                {
                    var proj = Directory.GetFiles(gameDir, "*.eeproj").FirstOrDefault();
                    if (proj != null) { RunStandalone(proj); return; }
                }
            }
            catch { }
        }

        // normal: show the launcher
        var launcher = new LauncherWindow();
        MainWindow = launcher;
        launcher.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        OllamaManager.Stop();
        base.OnExit(e);
    }

    // Standalone play mode for exported games.
    private void RunStandalone(string projectPath)
    {
        try
        {
            var project = ProjectIO.Load(projectPath);
            var play = new PlayWindow(project, project.StartScene ?? project.Scenes[0].Name)
            {
                WindowState = WindowState.Maximized,
                Title = project.Name
            };
            MainWindow = play;
            play.Closed += (s, ev) => Shutdown();
            play.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not start the game:\n" + ex.Message, "EasyEngine");
            Shutdown();
        }
    }

    // Batch-test MANY AI prompts through the real pipeline; writes a report to %TEMP%\easyengine_aitest.log
    private void RunAiTest(string[] args)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        string model = "qwen2.5-coder:1.5b";
        var mi = Array.IndexOf(args, "--model");
        if (mi >= 0 && mi + 1 < args.Length) model = args[mi + 1];
        var log = Path.Combine(Path.GetTempPath(), "easyengine_aitest.log");
        try { File.WriteAllText(log, "starting " + DateTime.Now + "\n"); } catch { }
        try
        {
            var temp = Path.Combine(Path.GetTempPath(), "EE_AiTest_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(temp);
            var project = ProjectIO.CreateNew("AiTest", temp);
            var editor = new MainWindow(project); // constructed, not shown
            _ = editor.RunAiBatchAsync(AiTestPrompts(), log, model).ContinueWith(t =>
                Dispatcher.Invoke(() => { if (t.Exception != null) { try { File.AppendAllText(log, "\nBATCH EXCEPTION: " + t.Exception); } catch { } } Shutdown(); }));
        }
        catch (Exception ex) { try { File.AppendAllText(log, "FATAL: " + ex); } catch { } Shutdown(); }
    }

    private static List<string> AiTestPrompts() => new()
    {
        "make a player that walks and jumps",
        "give the player a dash and a double jump",
        "give the player 5 hearts of health",
        "make the player able to wall jump",
        "create an enemy that patrols back and forth",
        "create an enemy that chases the player",
        "make an enemy that jumps and every 3 jumps dashes forward",
        "add a melee sword attack on the J key",
        "the player shoots bullets in all directions when pressing space",
        "when the player presses Y, spawn rocks that fall from the sky",
        "spawn an enemy every 2 seconds",
        "when the player touches a spike it loses one life",
        "make a coin that gives 1 point when the player collects it",
        "show the score as a HUD label at the top",
        "create a global variable score starting at 0 and lives at 3",
        "make the UI layer stay fixed on the screen",
        "when the player touches the crystal, it moves twice as fast",
        "create a door that goes to the next scene when touched",
        "make a moving platform that goes left and right",
        "make a one-way platform you can jump through from below",
        "create a boss with 20 health that chases the player and hurts it on touch",
        "give the player 3 lives and make it respawn at a checkpoint",
        "make the background scroll slower than the camera for parallax",
        "create an NPC that says hello when you press E",
        "make an enemy that shoots at the player every 2 seconds",
        "make an object that spins forever",
        "make an object that disappears when it leaves the screen",
        "when the score reaches 10 go to the next scene",
        "add a light that glows around the player",
        "make a platform the player can stand on",
        "double the player's speed",
        "make the enemy explode into particles when it dies",
    };

    // Verify the expression evaluator (Phase 1 foundation) — writes pass/fail to %TEMP%\easyengine_exprtest.log
    private void RunExprTest()
    {
        var log = Path.Combine(Path.GetTempPath(), "easyengine_exprtest.log");
        var vars = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["score"] = 10, ["lives"] = 3, ["coins"] = 7, ["self.x"] = 100, ["Player.x"] = 250, ["item:key"] = 1
        };
        double Resolve(string n) => vars.TryGetValue(n, out var v) ? v : 0;
        double Func(string n, double[] a)
        {
            double A(int i) => i < a.Length ? a[i] : 0;
            return n.ToLowerInvariant() switch
            {
                "abs" => Math.Abs(A(0)), "min" => Math.Min(A(0), A(1)), "max" => Math.Max(A(0), A(1)),
                "floor" => Math.Floor(A(0)), "clamp" => Math.Clamp(A(0), A(1), A(2)),
                "dist" => Math.Sqrt((A(2) - A(0)) * (A(2) - A(0)) + (A(3) - A(1)) * (A(3) - A(1))),
                _ => 0
            };
        }

        var cases = new (string expr, double want)[]
        {
            ("1 + 2 * 3", 7),
            ("(1 + 2) * 3", 9),
            ("10 - 2 - 3", 5),
            ("2 * 3 + 4 * 5", 26),
            ("12 % 5", 2),
            ("10 / 4", 2.5),
            ("-5 + 3", -2),
            ("score + coins", 17),
            ("score > 5", 1),
            ("score < 5", 0),
            ("score == 10", 1),
            ("lives != 3", 0),
            ("score > 5 && lives > 0", 1),
            ("score > 50 || coins > 5", 1),
            ("!(score > 50)", 1),
            ("score >= 10 && coins <= 7 && lives == 3", 1),
            ("Player.x - self.x", 150),
            ("abs(self.x - Player.x)", 150),
            ("min(score, lives)", 3),
            ("max(score, coins)", 10),
            ("clamp(score, 0, 5)", 5),
            ("dist(0, 0, 3, 4)", 5),
            ("item:key >= 1", 1),
            ("floor(10 / 4)", 2),
            ("score * 2 + coins - lives", 24),
            ("2 + 2 == 4 && 3 * 3 == 9", 1),
        };

        int pass = 0, fail = 0;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("exprtest " + DateTime.Now);
        foreach (var (expr, want) in cases)
        {
            double got = Expr.Eval(expr, Resolve, Func);
            bool ok = Math.Abs(got - want) < 1e-6;
            if (ok) pass++; else fail++;
            sb.AppendLine($"{(ok ? "PASS" : "FAIL")}  {expr}  => {got}  (want {want})");
        }
        sb.AppendLine($"\n{pass}/{pass + fail} passed" + (fail == 0 ? "  — ALL PASS" : $"  — {fail} FAILED"));
        try { File.WriteAllText(log, sb.ToString()); } catch { }
        Shutdown();
    }

    // Headless functional test of the Phase-2 behaviors (Pushable/Switch/Gate/Interact/AbilityPickup/Area).
    private void RunBehTest()
    {
        var log = Path.Combine(Path.GetTempPath(), "easyengine_behtest.log");
        try
        {
            var temp = Path.Combine(Path.GetTempPath(), "EE_BehTest_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(temp);
            var project = ProjectIO.CreateNew("BehTest", temp);
            var scene = project.GetStartScene();
            scene.Width = 9000; scene.Height = 3000; // room for all the test objects + pathfinding grid
            var layer = scene.Layers.FirstOrDefault(l => l.Kind == LayerKind.Base) ?? scene.Layers[0];

            BehaviorData B(BehaviorKind k, (string, double)[]? nums = null, (string, string)[]? texts = null)
            {
                var d = new BehaviorData { Kind = k };
                if (nums != null) foreach (var (kk, v) in nums) d.Nums[kk] = v;
                if (texts != null) foreach (var (kk, v) in texts) d.Texts[kk] = v;
                return d;
            }
            GameObjectData O(int id, string name, double x, double y, double w, double h, PhysicsMode phys, params BehaviorData[] bs)
            {
                var o = new GameObjectData { Id = id, Name = name, Type = "Object", X = x, Y = y, Width = w, Height = h, Color = "#AAAAAA", Physics = new PhysicsData { Mode = phys } };
                foreach (var b in bs) o.Behaviors.Add(b);
                return o;
            }

            layer.Objects.Add(O(1, "Player", 0, 0, 48, 48, PhysicsMode.Dynamic, B(BehaviorKind.Platformer)));
            layer.Objects.Add(O(2, "Gate1", 500, 0, 40, 120, PhysicsMode.None, B(BehaviorKind.Gate, new[] { ("value", 1.0) }, new[] { ("var", "hasKey") })));
            layer.Objects.Add(O(3, "Switch1", 1000, 0, 40, 40, PhysicsMode.None, B(BehaviorKind.Switch, null, new[] { ("var", "sw"), ("mode", "touch"), ("trigger", "Player") })));
            layer.Objects.Add(O(4, "Pickup1", 2000, 0, 32, 32, PhysicsMode.None, B(BehaviorKind.AbilityPickup, null, new[] { ("var", "hasDash"), ("collector", "Player"), ("grant", "dash") })));
            layer.Objects.Add(O(5, "Area1", 3000, 0, 100, 100, PhysicsMode.None, B(BehaviorKind.Area, new[] { ("to", 1.0), ("once", 1.0) }, new[] { ("action", "setvar"), ("var", "entered"), ("target", "Player") })));
            layer.Objects.Add(O(6, "Pusher", 4000, 0, 40, 40, PhysicsMode.None));
            layer.Objects.Add(O(7, "Block1", 4040, 0, 40, 40, PhysicsMode.None, B(BehaviorKind.Pushable, null, new[] { ("pusher", "Pusher") })));
            layer.Objects.Add(O(8, "NPC1", 5000, 0, 40, 60, PhysicsMode.None, B(BehaviorKind.Interact, new[] { ("radius", 60.0), ("to", 1.0) }, new[] { ("key", "E"), ("action", "setvar"), ("var", "talked"), ("trigger", "Player") })));
            layer.Objects.Add(O(9, "Gem1", 6000, 0, 24, 24, PhysicsMode.None, B(BehaviorKind.Collectible, null, new[] { ("collector", "Player"), ("id", "gem1") })));
            layer.Objects.Add(O(10, "World", 7000, 0, 600, 400, PhysicsMode.None, B(BehaviorKind.Generator, new[] { ("count", 12.0) }, new[] { ("prefab", "Tile"), ("mode", "once"), ("layout", "scatter"), ("id", "genA") })));
            project.Prefabs.Add(new PrefabDef { Name = "Tile", Template = new GameObjectData { Id = 999, Name = "Tile", Type = "Object", Width = 20, Height = 20, Color = "#88AAFF" } });

            // Phase 4 (aesthetics): a styled box + a label with a text effect
            var styled = O(11, "StyledBox", 8000, 0, 50, 50, PhysicsMode.None);
            styled.Opacity = 0.5; styled.CornerRadius = 16; styled.BorderColor = "#00E5FF"; styled.BorderWidth = 3;
            layer.Objects.Add(styled);
            layer.Objects.Add(O(12, "Title", 8100, 0, 200, 50, PhysicsMode.None,
                B(BehaviorKind.Label, new[] { ("effectSpeed", 30.0) }, new[] { ("text", "HELLO WORLD"), ("effect", "typewriter") })));
            project.Theme.DialogCorner = 14; project.Theme.DialogEffect = "typewriter"; project.Theme.DialogBg = "#223344";

            // Phase 5: pathfinding, spring, bounce, minimap
            layer.Objects.Add(O(13, "Chaser", 200, 1500, 24, 24, PhysicsMode.None, B(BehaviorKind.PathChase, new[] { ("speed", 120.0), ("cell", 20.0) }, new[] { ("target", "Player") })));
            layer.Objects.Add(O(14, "PWall", 380, 1400, 40, 400, PhysicsMode.Static, B(BehaviorKind.Solid)));
            layer.Objects.Add(O(15, "SpringPad", 500, 1500, 40, 20, PhysicsMode.None, B(BehaviorKind.Spring, new[] { ("force", 820.0) }, new[] { ("dir", "up") })));
            layer.Objects.Add(O(16, "Faller", 500, 1485, 24, 24, PhysicsMode.Dynamic));
            var ball = O(17, "Ball", 700, 1500, 20, 20, PhysicsMode.Dynamic);
            ball.Physics.Bounciness = 0.7; layer.Objects.Add(ball);
            layer.Objects.Add(O(18, "BFloor", 680, 1560, 80, 20, PhysicsMode.Static, B(BehaviorKind.Solid)));
            layer.Objects.Add(O(19, "Map", 50, 50, 10, 10, PhysicsMode.None, B(BehaviorKind.Minimap, new[] { ("size", 160.0) }, new[] { ("corner", "tr"), ("player", "Player") })));

            // "the works": particles, tween, UI button, bar
            layer.Objects.Add(O(20, "Boom", 1000, 2500, 20, 20, PhysicsMode.None, B(BehaviorKind.Particles, new[] { ("burst", 10.0), ("life", 0.8) }, new[] { ("on", "start") })));
            layer.Objects.Add(O(21, "Tweeny", 1200, 2500, 30, 30, PhysicsMode.None));
            layer.Objects.Add(O(22, "MenuBtn", 1300, 2500, 120, 40, PhysicsMode.None, B(BehaviorKind.UIButton, new[] { ("to", 1.0) }, new[] { ("action", "setvar"), ("var", "clicked") })));
            layer.Objects.Add(O(23, "HealthBar", 1500, 2500, 100, 16, PhysicsMode.None, B(BehaviorKind.Bar, new[] { ("max", 4.0) }, new[] { ("var", "hp") })));

            // Patrol + Platformer on the same enemy (controls off, like the non-player safeguard): patrol must still drive it.
            layer.Objects.Add(O(24, "Walker", 2000, 2700, 40, 40, PhysicsMode.Dynamic,
                B(BehaviorKind.Patrol, new[] { ("speed", 80.0), ("distance", 200.0) }),
                B(BehaviorKind.Platformer, new[] { ("controls", 0.0) })));

            var play = new PlayWindow(project, scene.Name); // not shown; headless
            var results = play.RunBehaviorChecks();
            int pass = results.Count(r => r.StartsWith("PASS")), fail = results.Count - pass;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("behtest " + DateTime.Now);
            foreach (var r in results) sb.AppendLine(r);
            sb.AppendLine($"\n{pass}/{results.Count} passed" + (fail == 0 ? "  — ALL PASS" : $"  — {fail} FAILED"));
            File.WriteAllText(log, sb.ToString());
            try { play.Close(); } catch { }
        }
        catch (Exception ex) { try { File.WriteAllText(log, "FATAL: " + ex); } catch { } }
        Shutdown();
    }

    // Tests the AI JSON parsing robustness (messy JSON, quoted numbers, synonyms) WITHOUT needing the model.
    private void RunAiJsonTest()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var log = Path.Combine(Path.GetTempPath(), "easyengine_aijsontest.log");
        var sb = new System.Text.StringBuilder();
        int pass = 0, fail = 0;
        void Check(string name, bool ok) { if (ok) pass++; else fail++; sb.AppendLine((ok ? "PASS" : "FAIL") + "  " + name); }
        try
        {
            var temp = Path.Combine(Path.GetTempPath(), "EE_AiJson_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(temp);
            var project = ProjectIO.CreateNew("AiJson", temp);
            var editor = new MainWindow(project); // headless (not shown)

            // 1) fences + prose + quoted numbers
            editor.TestApplySpec("Sure! Here you go:\n```json\n{\"create\":[{\"name\":\"Ball\",\"x\":\"100\",\"y\":\"50\",\"width\":\"40\",\"height\":\"40\",\"color\":\"#fff\",\"physics\":\"Dynamic\",\"behaviors\":[{\"kind\":\"platformer\",\"nums\":{\"speed\":\"300\",\"jump\":\"600\"}}]}]}\n```\nEnjoy!");
            var ball = editor.TestFindObject("Ball");
            Check("Created object from fenced/prose JSON", ball != null);
            Check("Quoted number x=\"100\" coerced", ball != null && Math.Abs(ball.X - 100) < 0.01);
            var plat = ball?.Behaviors.FirstOrDefault(b => b.Kind == BehaviorKind.Platformer);
            Check("Quoted behavior num speed=\"300\" coerced", plat != null && Math.Abs(plat.Num("speed") - 300) < 0.01);

            // 2) appearance with quoted values + synonym kind + bool string
            editor.TestApplySpec("{\"edit\":[{\"name\":\"Ball\",\"opacity\":\"0.5\",\"cornerRadius\":\"12\",\"bold\":\"true\",\"behaviors\":[{\"kind\":\"pushblock\"}]}]}");
            ball = editor.TestFindObject("Ball");
            Check("Quoted opacity coerced", ball != null && Math.Abs(ball.Opacity - 0.5) < 0.01);
            Check("Quoted cornerRadius coerced", ball != null && Math.Abs(ball.CornerRadius - 12) < 0.01);
            Check("Bool string \"true\" coerced", ball != null && ball.Bold);
            Check("Synonym kind 'pushblock' → Pushable", ball != null && ball.Behaviors.Any(b => b.Kind == BehaviorKind.Pushable));

            // 3) theme with quoted numbers
            editor.TestApplySpec("{\"theme\":{\"font\":\"Consolas\",\"dialogCorner\":\"18\",\"dialogSpeed\":\"60\"}}");
            Check("Theme font applied", editor.TestTheme.FontFamily == "Consolas");
            Check("Theme quoted dialogCorner coerced", Math.Abs(editor.TestTheme.DialogCorner - 18) < 0.01);

            // 4) gate synonym + metroidvania pickup
            editor.TestApplySpec("{\"create\":[{\"name\":\"RedGate\",\"behaviors\":[{\"kind\":\"lockeddoor\",\"texts\":{\"var\":\"hasRedKey\"}}]}]}");
            var gate = editor.TestFindObject("RedGate");
            Check("Synonym 'lockeddoor' → Gate", gate != null && gate.Behaviors.Any(b => b.Kind == BehaviorKind.Gate));
        }
        catch (Exception ex) { sb.AppendLine("FATAL: " + ex); fail++; }
        sb.Insert(0, "aijsontest " + DateTime.Now + "\n");
        sb.AppendLine($"\n{pass}/{pass + fail} passed" + (fail == 0 ? "  — ALL PASS" : $"  — {fail} FAILED"));
        try { File.WriteAllText(log, sb.ToString()); } catch { }
        Shutdown();
    }

    private void RunMakeGame()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var log = Path.Combine(Path.GetTempPath(), "easyengine_makegame.log");
        try
        {
            var parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Jogos");
            Directory.CreateDirectory(parent);
            var existing = Path.Combine(parent, "Platformer Demo");
            if (Directory.Exists(existing)) Directory.Delete(existing, true);
            var file = DemoGame.Build(parent);
            File.WriteAllText(log, "GAME BUILT: " + file + Environment.NewLine);
        }
        catch (Exception ex) { try { File.WriteAllText(log, "ERROR: " + ex + Environment.NewLine); } catch { } }
        Shutdown();
    }

    // Automated smoke test.
    private void RunSelfTest()
    {
        var log = Path.Combine(Path.GetTempPath(), "easyengine_selftest.log");
        void Write(string m) => File.AppendAllText(log, m + Environment.NewLine);
        try { File.WriteAllText(log, "selftest start " + DateTime.Now + Environment.NewLine); } catch { }

        try
        {
            var temp = Path.Combine(Path.GetTempPath(), "EE_SelfTest_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(temp);
            var project = ProjectIO.CreateNew("SelfTest", temp);

            // add a second scene + a door that links to it, to exercise scene switching
            var level2 = new Scene { Name = "Level2" };
            level2.Layers.Add(new Layer { Name = "Base", Kind = LayerKind.Base });
            project.Scenes.Add(level2);
            Write("project created: " + project.FilePath);

            var editor = new MainWindow(project);
            editor.Show();
            Write("editor constructed + shown OK");

            var play = new PlayWindow(project, project.StartScene ?? project.Scenes[0].Name) { Owner = editor };
            play.Show();
            Write("play window shown OK");

            // exercise the animation window (UI-only path)
            var anyObj = project.GetStartScene().Layers.SelectMany(l => l.Objects).FirstOrDefault();
            AnimationWindow? anim = null;
            if (anyObj != null) { anim = new AnimationWindow(project, anyObj) { Owner = editor }; anim.Show(); Write("animation window shown OK"); }

            var vars = new VariablesWindow(project, project.GetStartScene(), anyObj) { Owner = editor };
            vars.Show(); Write("variables window shown OK");

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            timer.Tick += (s, ev) =>
            {
                timer.Stop();
                try { vars.Close(); anim?.Close(); play.Close(); editor.Close(); } catch { }
                Write("ran loop ~2.5s, closed OK");
                Write("SELFTEST PASS");
                Shutdown();
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            Write("SELFTEST FAIL: " + ex);
            Shutdown();
        }
    }
}
