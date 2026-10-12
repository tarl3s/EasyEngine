using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace EasyEngine;

public partial class App : Application
{
    // --selftest feature checks (App.SelfTest.<Feature>.cs). Each may open windows (add them to CloseLater) and
    // queue LateChecks that run after the ~2.5 s live loop (return an error message to fail, null to pass).
    partial void SelfTestContrast(SelfTestContext c);
    partial void SelfTestMinimap(SelfTestContext c);
    partial void SelfTestLearn(SelfTestContext c);
    partial void SelfTestSequences(SelfTestContext c);
    partial void SelfTestTerrain(SelfTestContext c);
    partial void SelfTestReadability(SelfTestContext c);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // the theme comes first (the styles load with it). Tests, demo builders and --play always run dark.
        AiSettings.Load();
        // (--theme light / --lang pt-BR force them in any mode, e.g. to take --selftest pictures of the light theme)
        bool personRun = !e.Args.Any(a => a.StartsWith("--") && a != "--editor");
        string? ArgAfter(string flag) { int i = Array.IndexOf(e.Args, flag); return i >= 0 && i + 1 < e.Args.Length ? e.Args[i + 1] : null; }
        Theme.Init(ArgAfter("--theme") ?? (personRun ? AiSettings.ThemeName : "dark"));
        if (ArgAfter("--lang") is string forcedLang) L.SetLanguage(forcedLang);


        // Every window gets the theme's light text colour, and everything inside inherits it. (It used to be forced on
        // every TextBlock app-wide, which also hit the text WPF puts inside menus, tooltips and buttons → light on light.)
        // (A local value set on Loaded: an OverrideMetadata default would not do — WPF only passes non-default values down.)
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
        {
            if (s is Window w && w.ReadLocalValue(System.Windows.Controls.Control.ForegroundProperty) == DependencyProperty.UnsetValue)
                w.Foreground = Theme.Brush("Text", "#DCDCDC");
            if (s is Window sw) ScaleWindow(sw);
        }));
        L.Install(); // editor texts in the chosen language (English until ApplyUserPreferences)

        // exported game: EasyEngine.exe --play <project.eeproj>
        var playIdx = Array.IndexOf(e.Args, "--play");
        if (playIdx >= 0 && playIdx + 1 < e.Args.Length) { RunStandalone(e.Args[playIdx + 1]); return; }

        // test modes create many throwaway projects — keep them out of the user's Recent list
        string[] testModes = { "--selftest", "--aitest", "--blocktest", "--exprtest", "--behtest", "--aijsontest", "--fulltest", "--e2e" };
        if (e.Args.Any(a => testModes.Contains(a))) ProjectIO.SuppressRecent = true;

        if (e.Args.Contains("--restore-recent")) { RunRestoreRecent(); return; }

        if (e.Args.Contains("--strings")) { RunStrings(e.Args); return; } // dev tool: writes Lang/en.json, the texts to translate

        if (e.Args.Contains("--selftest")) { RunSelfTest(); return; }

        if (e.Args.Contains("--aitest")) { RunAiTest(e.Args); return; }

        if (e.Args.Contains("--makegame")) { RunDemoBuild("makegame", "Platformer Demo", DemoGame.Build); return; }

        if (e.Args.Contains("--spritetest")) { RunSpriteTest(); return; }

        if (e.Args.Contains("--blocktest")) { RunBlockTest(); return; }

        if (e.Args.Contains("--exprtest")) { RunExprTest(); return; }

        if (e.Args.Contains("--behtest")) { RunBehTest(); return; }

        if (e.Args.Contains("--aijsontest")) { RunAiJsonTest(); return; }

        if (e.Args.Contains("--fulltest")) { RunFullTest(); return; }

        if (e.Args.Contains("--mining")) { RunDemoBuild("mining", "Mining Demo", MiningGame.Build); return; }

        if (e.Args.Contains("--topdown")) { RunDemoBuild("topdown", TopDownGame.ProjectName, TopDownGame.Build); return; }

        if (e.Args.Contains("--bossdemo")) { RunDemoBuild("bossdemo", BossDemo.ProjectName, BossDemo.Build); return; }

        if (e.Args.Contains("--infinite")) { RunDemoBuild("infinite", InfiniteWorld.ProjectName, InfiniteWorld.Build); return; }

        if (e.Args.Contains("--e2e")) { RunE2E(); return; }

        if (e.Args.Contains("--scenarios")) { RunScenarios(); return; }

        if (e.Args.Contains("--review")) { ProjectIO.SuppressRecent = true; RunReview(); return; }

        if (e.Args.Contains("--update-now")) { RunUpdateNow(); return; } // install the latest GitHub release without asking

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

        // normal: show the launcher, in the person's language, theme and size
        ApplyUserPreferences();
        var launcher = new LauncherWindow();
        MainWindow = launcher;
        launcher.Show();
    }

    /// <summary>Language, theme and interface size from settings.json (only for the editor — tests and exported games
    /// always run in English, dark, 100%).</summary>
    public static void ApplyUserPreferences()
    {
        AiSettings.Load();
        L.SetLanguage(AiSettings.Language);
        _uiScale = AiSettings.UiScale;
        ApplyUiScale();
    }

    /// <summary>Closes every window (the editor asks to save first — Cancel stops the restart) and starts EasyEngine
    /// again at the launcher, e.g. so a new theme applies.</summary>
    public static void Restart()
    {
        var app = Current;
        var before = app.ShutdownMode;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        foreach (var w in app.Windows.Cast<Window>().Reverse().ToList())
        {
            w.Close();
            if (w.IsVisible) { app.ShutdownMode = before; return; } // the person cancelled (unsaved changes)
        }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--editor") { UseShellExecute = false }); }
        catch { }
        app.Shutdown();
    }

    private static double _uiScale = 1.0;

    /// <summary>Re-applies the interface size (settings) to every open window.</summary>
    public static void ApplyUiScale()
    {
        _uiScale = AiSettings.UiScale is >= 0.8 and <= 1.5 ? AiSettings.UiScale : 1.0;
        if (Current != null) foreach (Window w in Current.Windows) ScaleWindow(w);
    }

    // the whole content of an editor window is drawn bigger/smaller; games are never scaled
    private static void ScaleWindow(Window w)
    {
        if (w is PlayWindow || w.Content is not FrameworkElement fe) return;
        if (Math.Abs(_uiScale - 1.0) < 0.001) { if (fe.LayoutTransform is ScaleTransform) fe.LayoutTransform = Transform.Identity; }
        else fe.LayoutTransform = new ScaleTransform(_uiScale, _uiScale);
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
                Title = project.Name,
                DevTools = false // exported game: no F1 console, no play log
            };
            MainWindow = play;
            play.Closed += (s, ev) => Shutdown();
            play.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(L.T("Could not start the game:") + "\n" + ex.Message, "EasyEngine");
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
            var (project, sceneName) = BuildBehaviorScene();
            var play = new PlayWindow(project, sceneName); // not shown; headless
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

            // 5) AI-drawn pixel-art sprite → renders a PNG and sets the object's sprite
            editor.TestApplySpec("{\"create\":[{\"name\":\"Coin\",\"width\":16,\"height\":16,\"pixelArt\":{\"palette\":{\"Y\":\"#ffd700\",\"o\":\"#b8860b\"},\"rows\":[\".YYY.\",\"YoYoY\",\"YoYoY\",\"YoYoY\",\".YYY.\"]}}]}");
            var coin = editor.TestFindObject("Coin");
            bool coinPng = coin?.SpritePath != null && File.Exists(Path.Combine(project.FolderPath!, coin.SpritePath));
            Check("AI pixelArt rendered a sprite PNG", coinPng);

            // 6) AI-drawn animation frames
            editor.TestApplySpec("{\"create\":[{\"name\":\"Blinker\",\"animations\":[{\"name\":\"idle\",\"fps\":4,\"frames\":[{\"palette\":{\"R\":\"#ff0000\"},\"rows\":[\"RR\",\"RR\"]},{\"palette\":{\"B\":\"#0000ff\"},\"rows\":[\"BB\",\"BB\"]}]}]}]}");
            var blinker = editor.TestFindObject("Blinker");
            Check("AI drew a 2-frame animation", blinker != null && blinker.Animations.Count == 1 && blinker.Animations[0].Frames.Count == 2);

            // 7) Boss AI / Cutscene "✨ Ask the AI" replies: normalised, then read with the same parser as the game
            var seqNotes = new List<string>();
            var bossJson = SequenceAi.Normalize(SeqMode.Boss, "Here is your boss:\n```json\n{\"boss\":{\"attacks\":{\"Stomp\":[{\"Type\":\"Slam\",\"Height\":\"300\"},{\"action\":\"fire\",\"pattern\":\"aimed\",\"count\":\"3\"}]}," +
                                                "\"phases\":[{\"name\":\"Rage\",\"when\":{\"type\":\"health\",\"value\":\"50\"},\"attacks\":[\"stomp\"]}]}}\n```", seqNotes, out var bossErr);
            var bossParsed = bossJson != null ? SequenceJson.ParseBoss(bossJson, out _) : null;
            Check("Boss reply: fenced {\"boss\":…} with step synonyms + quoted numbers normalised", bossErr == null
                  && bossParsed?.Attack("Stomp")?.Steps.Select(s => s.Do).SequenceEqual(new[] { "jump", "shoot" }) == true && bossParsed.Attack("Stomp")!.Steps[0].Num("height") == 300);
            Check("Boss reply: attack names match whatever their case; a health phase keeps its %", bossParsed?.Phases.FirstOrDefault()?.Attacks.SequenceEqual(new[] { "Stomp" }) == true
                  && bossParsed.Phases[0].When == "health" && bossParsed.Phases[0].Value == 50);
            Check("Boss reply without attacks is rejected (so it can be retried)", SequenceAi.Normalize(SeqMode.Boss, "{\"phases\":[{\"name\":\"P\"}]}", new List<string>(), out var noAtk) == null && noAtk != null);
            var cutJson = SequenceAi.Normalize(SeqMode.Cutscene, "{\"steps\":[{\"do\":\"Talk\",\"line\":\"Hi\",\"who\":\"Bob\"},{\"do\":\"teleport\"}]}", seqNotes, out var cutErr);
            Check("Cutscene reply: 'Talk' → say (text + speaker); unknown 'teleport' dropped with a note", cutErr == null && cutJson != null
                  && SequenceJson.ParseCutscene(cutJson, out _).Steps.Single().Str("speaker") == "Bob" && seqNotes.Any(n => n.Contains("teleport")));

            // 8) decimal commas are decimals (not thousands), and "%" is only a percentage where one is meant
            editor.TestApplySpec("{\"create\":[{\"name\":\"Sky\",\"behaviors\":[{\"kind\":\"daynight\",\"nums\":{\"dayLength\":\"1,5\",\"nightLight\":\"25%\"}}]}]}");
            var sky = editor.TestFindObject("Sky")?.Behaviors.FirstOrDefault(b => b.Kind == BehaviorKind.DayNight);
            Check("Quoted \"1,5\" is 1.5 (was 15: ',' read as a thousands separator)", sky != null && Math.Abs(sky.Num("dayLength") - 1.5) < 0.001);
            Check("\"25%\" for a 0-1 brightness is rejected (kept the default, was 25)", sky != null && Math.Abs(sky.Num("nightLight") - 0.25) < 0.001);
            var pctJson = SequenceAi.Normalize(SeqMode.Boss, "{\"attacks\":{\"Glide\":[{\"do\":\"moveto\",\"to\":\"\",\"x\":600,\"y\":120,\"time\":\"0,5\"}]}," +
                                               "\"phases\":[{\"name\":\"Rage\",\"when\":{\"type\":\"health\",\"value\":\"40%\"}}]}", new List<string>(), out var pctErr);
            var pct = pctJson != null ? SequenceJson.ParseBoss(pctJson, out _) : null;
            var glide = pct?.Attack("Glide")?.Steps.FirstOrDefault();
            Check("Boss reply: a phase's \"40%\" health is 40; a step's \"0,5\" seconds is 0.5", pctErr == null && pct?.Phases.FirstOrDefault()?.Value == 40 && glide?.Num("time") == 0.5);
            Check("Boss reply: \"to\":\"\" (glide to a point) survives Normalize → Serialize (was replaced by \"player\")", glide?.Str("to") == "" && glide.Num("x") == 600);
        }
        catch (Exception ex) { sb.AppendLine("FATAL: " + ex); fail++; }
        sb.Insert(0, "aijsontest " + DateTime.Now + "\n");
        sb.AppendLine($"\n{pass}/{pass + fail} passed" + (fail == 0 ? "  — ALL PASS" : $"  — {fail} FAILED"));
        try { File.WriteAllText(log, sb.ToString()); } catch { }
        Shutdown();
    }

    // One-off repair: put every project found one level under Desktop\Jogos back into the Recent list
    // (older test runs had pushed the user's real projects out of it). Oldest first, so the newest ends on top.
    private void RunRestoreRecent()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var log = Path.Combine(Path.GetTempPath(), "easyengine_restore.log");
        var sb = new System.Text.StringBuilder();
        try
        {
            var parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Jogos");
            var files = Directory.Exists(parent)
                ? Directory.GetDirectories(parent).SelectMany(d => Directory.GetFiles(d, "*.eeproj")).OrderBy(File.GetLastWriteTime).ToList()
                : new List<string>();
            foreach (var f in files)
            {
                try { ProjectIO.Load(f); sb.AppendLine("restored: " + f); }
                catch (Exception ex) { sb.AppendLine("skipped: " + f + " (" + ex.Message + ")"); }
            }
        }
        catch (Exception ex) { sb.AppendLine("ERROR: " + ex); }
        try { File.WriteAllText(log, sb.ToString()); } catch { }
        Shutdown();
    }

    // Builds the "EasyEngine Tests" project (isolated test scenarios) into Desktop\Jogos.
    private void RunScenarios()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var log = Path.Combine(Path.GetTempPath(), "easyengine_scenarios.log");
        try
        {
            var parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Jogos");
            Directory.CreateDirectory(parent);
            File.WriteAllText(log, "SCENARIOS BUILT: " + TestScenarios.Build(parent) + Environment.NewLine);
        }
        catch (Exception ex) { try { File.WriteAllText(log, "ERROR: " + ex + Environment.NewLine); } catch { } }
        Shutdown();
    }

    // Builds a demo project into DesktopJogos<folder> (replacing the old copy) and logs where it went.
    // --mining, --topdown, --makegame, --bossdemo, --infinite all go through here.
    // --update-now: what the launcher's "Install it now?" does, without asking (and logged) — for testing the updater.
    private async void RunUpdateNow()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var log = Path.Combine(Path.GetTempPath(), "easyengine_update_now.log");
        try
        {
            File.WriteAllText(log, $"current v{Updater.Current.ToString(3)} in {Updater.InstallDir} (dev build: {Updater.IsDevBuild})\n");
            var r = await Updater.CheckAsync();
            if (r == null) { File.AppendAllText(log, "no newer release\n"); Shutdown(); return; }
            File.AppendAllText(log, $"found {r.Tag}, downloading {r.ZipUrl}\n");
            var dir = await Updater.DownloadAsync(r);
            File.AppendAllText(log, $"unpacked to {dir}, swapping\n");
            Updater.StartSwap(dir, Updater.InstallDir, Environment.ProcessId);
        }
        catch (Exception ex) { try { File.AppendAllText(log, "ERROR: " + ex + "\n"); } catch { } }
        Shutdown();
    }

    private void RunDemoBuild(string mode, string folder, Func<string, string> build)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var log = Path.Combine(Path.GetTempPath(), $"easyengine_{mode}.log");
        try
        {
            var parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Jogos");
            Directory.CreateDirectory(parent);
            var existing = Path.Combine(parent, folder);
            if (Directory.Exists(existing)) TestScenarios.TryDeleteDir(existing);
            var file = build(parent);
            File.WriteAllText(log, "BUILT: " + file + Environment.NewLine);
        }
        catch (Exception ex) { try { File.WriteAllText(log, "ERROR: " + ex + Environment.NewLine); } catch { } }
        Shutdown();
    }

    private void RunBlockTest()
    {
        var log = Path.Combine(Path.GetTempPath(), "easyengine_blocktest.log");
        var sb = new System.Text.StringBuilder(); int pass = 0, fail = 0;
        void Check(string n, bool ok) { if (ok) pass++; else fail++; sb.AppendLine((ok ? "PASS" : "FAIL") + "  " + n); }
        try
        {
            var temp = Path.Combine(Path.GetTempPath(), "EE_Block_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(temp);
            var project = ProjectIO.CreateNew("Block", temp);
            var scene = project.GetStartScene();

            string RT(string js) => new BlockEditorWindow(project, scene, js).SerializeTest();
            bool Has(string json, Func<System.Text.Json.JsonElement, bool> pred)
            { using var d = System.Text.Json.JsonDocument.Parse(json); return d.RootElement.EnumerateArray().Any(pred); }

            var coin = RT("[{\"when\":\"touch\",\"name\":\"Player\",\"do\":\"addvar\",\"var\":\"coins\",\"amount\":1},{\"when\":\"touch\",\"name\":\"Player\",\"do\":\"destroy\"}]");
            using (var d = System.Text.Json.JsonDocument.Parse(coin)) Check("Coin keeps 2 rules", d.RootElement.GetArrayLength() == 2);
            Check("Coin addvar coins survives", Has(coin, r => r.TryGetProperty("do", out var dv) && dv.GetString() == "addvar" && r.TryGetProperty("var", out var vv) && vv.GetString() == "coins"));

            var legacy = RT("[{\"when\":\"keypress\",\"key\":\"B\",\"cooldown\":1,\"if\":\"coins\",\"ifop\":\">=\",\"ifval\":15,\"do\":\"boost\",\"factor\":1.4}]");
            Check("Legacy if→cond", Has(legacy, r => r.TryGetProperty("cond", out var cv) && (cv.GetString() ?? "").Contains("coins") && (cv.GetString() ?? "").Contains("15")));
            Check("Legacy drops ifval key", !Has(legacy, r => r.TryGetProperty("ifval", out _)));
            Check("Legacy keeps boost factor", Has(legacy, r => r.TryGetProperty("factor", out var fv) && Math.Abs(fv.GetDouble() - 1.4) < 0.001));

            var expr = RT("[{\"when\":\"click\",\"do\":\"setvar\",\"var\":\"coins\",\"expr\":\"coins - 15\"}]");
            Check("Expr value round-trips", Has(expr, r => r.TryGetProperty("expr", out var ev) && (ev.GetString() ?? "").Replace(" ", "").Contains("coins-15")));

            Check("Round-trip is stable (idempotent)", RT(coin) == coin);
        }
        catch (Exception ex) { sb.AppendLine("FATAL: " + ex); fail++; }
        sb.Insert(0, "blocktest " + DateTime.Now + "\n");
        sb.AppendLine($"\n{pass}/{pass + fail} passed" + (fail == 0 ? "  — ALL PASS" : $"  — {fail} FAILED"));
        try { File.WriteAllText(log, sb.ToString()); } catch { }
        Shutdown();
    }

    private void RunSpriteTest()
    {
        var log = Path.Combine(Path.GetTempPath(), "easyengine_spritetest.log");
        try
        {
            var parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Jogos");
            Directory.CreateDirectory(parent);
            var existing = Path.Combine(parent, "Sprite Test");
            if (Directory.Exists(existing)) Directory.Delete(existing, true);
            var file = SpriteTestGame.Build(parent);
            File.WriteAllText(log, "SPRITE TEST BUILT: " + file + Environment.NewLine);
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
            play.TShowConsole(true);
            if (!play.TConsoleVisible) throw new Exception("debug console did not open");
            Write("debug console opened OK");
            // real playback of the generated effects (a MediaFailed would show up as a console Error below)
            foreach (var (wav, name) in new[] { (SoundSynth.Whoosh(), "whoosh"), (SoundSynth.Chop(), "chop"), (SoundSynth.TreeFall(), "fall"), (SoundSynth.Denied(), "denied") })
                play.TPlaySound(ProjectIO.SaveSound(project, wav, name));
            Write("sound effects played");

            // the lighting scenario in a real, rendering window (sky colour, light holes, night tint)
            var scenDir = Path.Combine(Path.GetTempPath(), "EE_SelfTest_scen_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(scenDir);
            var scen = ProjectIO.Load(TestScenarios.Build(scenDir));
            scen.ConsoleOnStart = false;
            var lightPlay = new PlayWindow(scen, "Lighting Test") { Owner = editor };
            lightPlay.Show();
            lightPlay.TSetTime("night");
            Write("lighting scenario shown at night OK");

            // exercise the animation window (UI-only path)
            var anyObj = project.GetStartScene().Layers.SelectMany(l => l.Objects).FirstOrDefault();
            AnimationWindow? anim = null;
            if (anyObj != null) { anim = new AnimationWindow(project, anyObj) { Owner = editor }; anim.Show(); Write("animation window shown OK"); }

            var vars = new VariablesWindow(project, project.GetStartScene(), anyObj) { Owner = editor };
            vars.Show(); Write("variables window shown OK");

            var newItem = new NewItemDialog(project) { Owner = editor };
            newItem.Show(); newItem.UpdateLayout(); Write("new item dialog shown OK"); newItem.Close();

            // the inspector picks everything: the Top-Down Demo's player (Hotbar + Use Item) has design pickers, no "a=1"/"a:#b" boxes
            var tdDir = Path.Combine(Path.GetTempPath(), "EE_SelfTest_td_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(tdDir);
            var td = ProjectIO.Load(TopDownGame.Build(tdDir));
            var tdEditor = new MainWindow(td);
            tdEditor.Show();
            var insp = tdEditor.TestInspect(tdEditor.TestFindObject("Player")!);
            Write($"inspector (Player): {insp.combos} dropdowns, {insp.lookPickers} design pickers, text boxes: [{string.Join(" | ", insp.textBoxes)}]");
            if (insp.lookPickers < 4 || insp.textBoxes.Any(t => t.Contains('=') || t.Contains(':')))
                throw new Exception("the Player's behavior cards still have raw text formats, or no design pickers");
            // every behavior card builds without an error (one object carrying all of them)
            var all = new GameObjectData { Name = "AllBehaviors" };
            foreach (var info in BehaviorCatalog.All.Where(i => !i.Hidden && i.Kind != BehaviorKind.Tilemap)) all.Behaviors.Add(info.CreateData());
            td.GetStartScene().Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(all);
            var inspAll = tdEditor.TestInspect(all);
            Write($"inspector (all {all.Behaviors.Count} behaviors): {inspAll.combos} dropdowns, {inspAll.textBoxes.Count} text boxes");
            tdEditor.Close();

            // feature checks in real windows, each in its own App.SelfTest.<Feature>.cs
            var ctx = new SelfTestContext { Editor = editor, Write = Write };
            SelfTestContrast(ctx);
            SelfTestMinimap(ctx);
            SelfTestLearn(ctx);
            SelfTestSequences(ctx);
            SelfTestTerrain(ctx);
            SelfTestReadability(ctx); // last: it measures the text of every window the others opened

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            timer.Tick += (s, ev) =>
            {
                timer.Stop();
                void CloseAll()
                {
                    foreach (var w in ctx.CloseLater) try { w.Close(); } catch { }
                    try { lightPlay.Close(); vars.Close(); anim?.Close(); play.Close(); editor.Close(); } catch { }
                    // the throwaway projects this run made
                    try { foreach (var dir in Directory.GetDirectories(Path.GetTempPath(), "EE_SelfTest_*")) try { Directory.Delete(dir, true); } catch { } } catch { }
                }
                int errors = play.TLog.Errors + lightPlay.TLog.Errors;
                string firstError = play.TLog.Entries.Concat(lightPlay.TLog.Entries).FirstOrDefault(x => x.Level == LogLevel.Error)?.FullText() ?? "";
                Write($"lighting window: {lightPlay.TLightHoles} light holes, darkness {lightPlay.TDarkness:0.00}, sky {lightPlay.TSkyColor}");
                if (lightPlay.TLightHoles != 3 || lightPlay.TDarkness < 0.5 || !lightPlay.TInSky("Moon")) { Write("SELFTEST FAIL: lighting did not render as expected"); CloseAll(); Shutdown(); return; }
                foreach (var check in ctx.LateChecks)
                {
                    string? err;
                    try { err = check(); } catch (Exception ex) { err = ex.ToString(); }
                    if (err != null) { Write("SELFTEST FAIL: " + err); CloseAll(); Shutdown(); return; }
                }
                CloseAll();
                Write("ran loop ~2.5s, closed OK");
                if (errors > 0) { Write($"SELFTEST FAIL: {errors} error(s) in the play console:\n{firstError}"); Shutdown(); return; }
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
