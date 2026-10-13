using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace EasyEngine
{
    // --aitest: a GRADED evaluation of the AI's Build mode. Every case is a request a person would really type
    // (simple moves, timers, enemies, bosses, HUD, whole levels, edits, Portuguese…). The prompt goes through the
    // same pipeline as the AI panel (AiBuildAsync), then a check looks at the result — the project it produced
    // and, whenever it can, a headless play of it (did the box fade? did enemies spawn? did the scene change?).
    //   --aitest                         local qwen2.5-coder:1.5b (the owner's worst case)
    //   --aitest --model qwen2.5-coder:3b
    //   --aitest --cloud [auto|groq|gemini|openrouter[|model]]
    //   --only fade,boss                 run the cases whose id contains any of these
    //   --showprompt                     the log gets each Build request's prompt
    //   --delay 15000                    milliseconds between cloud requests (Gemini free: a few a minute)
    // Report: %TEMP%\easyengine_aitest.log (pass/fail per case, why, the AI's reply when it failed).
    public partial class App
    {
        private void RunAiTest(string[] args)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            string Arg(string flag) { int i = Array.IndexOf(args, flag); return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : ""; }
            string choice = args.Contains("--cloud") ? "cloud:" + (Arg("--cloud") is { Length: > 0 } c ? c : "auto")
                          : "local:" + (Arg("--model") is { Length: > 0 } m ? m : "qwen2.5-coder:1.5b");
            var only = Arg("--only").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var log = Path.Combine(Path.GetTempPath(), "easyengine_aitest.log");
            try { File.WriteAllText(log, $"AI EVAL — {choice} — {DateTime.Now}\n"); } catch { }
            string? temp = null;
            try
            {
                temp = Path.Combine(Path.GetTempPath(), "EE_AiTest_" + Guid.NewGuid().ToString("N")[..6]);
                Directory.CreateDirectory(temp);
                var editor = new MainWindow(ProjectIO.CreateNew("AiTest", temp)); // constructed, not shown
                editor.EvalUseAi(choice);
                var cases = AiEvalCases().Where(k => only.Length == 0 || only.Any(o => k.Id.Contains(o, StringComparison.OrdinalIgnoreCase))).ToList();
                int gap = int.TryParse(Arg("--delay"), out var d) ? d : 6000;
                _showPrompt = args.Contains("--showprompt");
                _ = RunAiEvalAsync(editor, cases, log, choice.StartsWith("cloud:"), gap).ContinueWith(t => Dispatcher.Invoke(() =>
                {
                    if (t.Exception != null) try { File.AppendAllText(log, "\nEVAL EXCEPTION: " + t.Exception); } catch { }
                    try { Directory.Delete(temp, true); } catch { }
                    Shutdown();
                }));
            }
            catch (Exception ex) { try { File.AppendAllText(log, "FATAL: " + ex); } catch { } if (temp != null) try { Directory.Delete(temp, true); } catch { } Shutdown(); }
        }

        private static bool _showPrompt; // --showprompt: the log gets each Build request's prompt
        private static async Task RunAiEvalAsync(MainWindow ed, List<AiCase> cases, string log, bool cloud, int gap)
        {
            if (!cloud) await OllamaManager.EnsureRunningAsync();
            ed.EvalReset();
            try { File.AppendAllText(log, "prompt sizes: " + string.Join(", ", new[] { "make the player run faster", "build a level with 3 platforms and 5 coins", "the Enemy chases the player only when near" }.Select(q => $"{ed.TestBuildPrompt(q).Length}")) + " chars\n\n"); } catch { }
            int pass = 0, n = 0, skipped = 0;
            var fails = new List<string>();
            foreach (var c in cases)
            {
                n++;
                ed.EvalReset();
                var kit = new AiEvalKit(ed);
                string verdict, detail = "", shownPrompt = "";
                var sw = Stopwatch.StartNew();
                try
                {
                    c.Setup?.Invoke(kit);
                    kit.Baseline();
                    if (c.Select != null) ed.EvalSelect(c.Select);
                    if (_showPrompt && c.Blocks == null && !c.Learn && c.Seq == null) shownPrompt = ed.TestBuildPrompt(c.Prompt);
                    var before = c.Learn ? JsonSerializer.Serialize(kit.P) : "";
                    var (ok, summary, reply) = c.Learn ? (true, "", await ed.EvalLearn(c.Prompt))
                                             : c.Seq is SeqMode mode ? await ed.EvalSequence(mode, c.SeqObject, c.Prompt)
                                             : c.Blocks != null ? await ed.EvalBlocks(c.Blocks, c.Prompt)
                                             : await ed.EvalBuild(c.Prompt);
                    foreach (var more in c.Then ?? Array.Empty<string>()) // a conversation: each request builds on the last
                    {
                        if (cloud) await Task.Delay(gap);
                        var (ok2, summary2, reply2) = await ed.EvalBuild(more);
                        ok &= ok2; summary += "  ⟶  " + summary2; reply += "\n--- then: " + more + " ---\n" + reply2;
                    }
                    if (c.Learn) { kit.Answer = reply; kit.Changed = JsonSerializer.Serialize(kit.P) != before; }
                    string? why;
                    try { why = ok ? c.Check(kit) : "nothing was applied"; }
                    catch (Exception ex) { why = "check crashed: " + ex.GetType().Name + " " + ex.Message; }
                    verdict = why == null ? "PASS" : "FAIL";
                    if (why == null) pass++; else fails.Add(c.Id);
                    detail = (why != null ? "  WHY: " + why + "\n" : "") + "  APPLIED: " + summary.Replace("\n", " | ") + "\n  STATE: " + ed.EvalDump() +
                             (why != null ? "\n  REPLY: " + OneLine(reply, 2500) : "");
                }
                catch (CloudException ex) when (ex.RateLimited || ex.Message.Contains("quota")) { verdict = "SKIP"; skipped++; detail = "  (no AI answer: " + ex.Message + ")"; } // free tier used up — not the AI's fault
                catch (Exception ex) { verdict = "ERROR"; fails.Add(c.Id); detail = "  " + ex.GetType().Name + ": " + ex.Message; }
                finally { kit.Dispose(); }
                var line = $"{verdict}  [{c.Id}] {c.Prompt}  ({sw.Elapsed.TotalSeconds:0.0}s)\n{detail}\n";
                if (shownPrompt.Length > 0) line += "  PROMPT:\n" + shownPrompt + "\n";
                try { File.AppendAllText(log, line); } catch { }
                if (cloud) await Task.Delay(gap); // --delay ms; be gentle with the free tiers' per-minute limits (Groq: ~7k input tokens a minute)
            }
            var total = $"\n{pass}/{n - skipped} passed" + (skipped > 0 ? $" ({skipped} skipped: no AI answer)" : "") + (fails.Count > 0 ? "  — failed: " + string.Join(", ", fails) : "  — ALL PASS") + "\n";
            try { File.AppendAllText(log, total); } catch { }
        }

        private static string OneLine(string s, int max)
        {
            s = System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim();
            return s.Length > max ? s[..max] + "…" : s;
        }

        public sealed class AiCase
        {
            public string Id = "", Prompt = "";
            public Action<AiEvalKit>? Setup;
            public string[]? Select;
            public SeqMode? Seq; public string SeqObject = ""; // a Boss AI / Cutscene card's "✨ Ask the AI" instead of the panel
            public bool Learn;           // 🎓 Learn mode: a question; the check reads the answer (and the project must not change)
            public string? Blocks;       // the block editor's "✨ Ask the AI" on this object instead of the panel
            public string[]? Then;       // more requests after the first, in the same scene (a conversation)
            public Func<AiEvalKit, string?> Check = _ => null; // null = pass, else why it failed
        }

        /// <summary>What a check can look at: the edited project, what the AI added, and a headless play of it.</summary>
        public sealed class AiEvalKit : IDisposable
        {
            public readonly MainWindow Ed;
            public string Answer = ""; public bool Changed; // the Learn mode's reply; whether it changed the project (it never may)
            private HashSet<int> _before = new();
            private readonly List<PlayWindow> _plays = new();
            public AiEvalKit(MainWindow ed) { Ed = ed; }

            public Project P => Ed.EvalProject;
            public Scene S => Ed.EvalScene;
            public IEnumerable<GameObjectData> Objs => S.Layers.SelectMany(l => l.Objects);
            public GameObjectData? Obj(string name) => Objs.FirstOrDefault(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            public GameObjectData Player => Obj("Player")!;
            public void Baseline() => _before = Objs.Select(o => o.Id).ToHashSet();
            /// <summary>Objects the AI put in the scene (not prefabs waiting to spawn).</summary>
            public List<GameObjectData> New => Objs.Where(o => !_before.Contains(o.Id)).ToList();
            public IEnumerable<GameObjectData> NewWith(BehaviorKind k) => New.Where(o => Has(o, k));
            public static bool Has(GameObjectData o, BehaviorKind k) => o.Behaviors.Any(b => b.Kind == k);
            public static BehaviorData? Beh(GameObjectData? o, BehaviorKind k) => o?.Behaviors.FirstOrDefault(b => b.Kind == k);
            public static double Default(BehaviorKind k, string key) => BehaviorCatalog.Info(k).Nums.First(n => n.Key == key).Default;
            public Layer LayerOf(GameObjectData o) => S.Layers.First(l => l.Objects.Contains(o));
            public bool OnScreenLayer(GameObjectData o) { var l = LayerOf(o); return l.ScreenSpace || l.Kind == LayerKind.UI; }

            /// <summary>The object's Custom rules as lower-case key → text (numbers as text).</summary>
            public static List<Dictionary<string, string>> Rules(GameObjectData? o) => RulesOf(Beh(o, BehaviorKind.Custom)?.Text("script", "[]") ?? "[]");
            public static List<Dictionary<string, string>> RulesOf(string s)
            {
                var list = new List<Dictionary<string, string>>();
                try
                {
                    using var doc = JsonDocument.Parse(s, LooseJson.Options);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        foreach (var r in doc.RootElement.EnumerateArray())
                            if (r.ValueKind == JsonValueKind.Object)
                                list.Add(r.EnumerateObject().ToDictionary(p => p.Name.ToLowerInvariant(), p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.GetRawText(), StringComparer.OrdinalIgnoreCase));
                }
                catch { }
                return list;
            }
            public IEnumerable<Dictionary<string, string>> AllRules => Objs.Concat(P.Prefabs.Select(p => p.Template)).SelectMany(Rules);
            public static bool Is(Dictionary<string, string> r, string key, string value) => r.TryGetValue(key, out var v) && v.Equals(value, StringComparison.OrdinalIgnoreCase);

            public static (byte r, byte g, byte b) Rgb(string? hex)
            {
                try { var c = (Color)ColorConverter.ConvertFromString(hex ?? "#000"); return (c.R, c.G, c.B); } catch { return (0, 0, 0); }
            }
            public static bool Reddish(string? hex) { var (r, g, b) = Rgb(hex); return r > 150 && g < 110 && b < 110; }
            public static bool Bluish(string? hex) { var (r, g, b) = Rgb(hex); return b > 150 && r < 120 && b > g; }

            public void AddScene(string name)
            {
                if (P.FindScene(name) != null) return;
                var sc = new Scene { Name = name };
                sc.Layers.Add(new Layer { Name = "Base", Kind = LayerKind.Base });
                P.Scenes.Add(sc);
            }

            public GameObjectData Add(string name, double x, double y, double w = 64, double h = 64, string color = "#E0A030",
                                      PhysicsMode phys = PhysicsMode.None, params BehaviorKind[] kinds)
            {
                var o = new GameObjectData { Id = Objs.Select(z => z.Id).DefaultIfEmpty(0).Max() + 1, Name = name, Type = "Object", X = x, Y = y, Width = w, Height = h, Color = color, Physics = new PhysicsData { Mode = phys } };
                foreach (var k in kinds) o.Behaviors.Add(BehaviorCatalog.Info(k).CreateData());
                S.Layers.First(l => l.Kind == LayerKind.Base).Objects.Add(o);
                return o;
            }
            /// <summary>A boss "Golem" that fights from the start (its script is what the AI writes).</summary>
            public GameObjectData AddBoss(string moves)
            {
                var b = AddEnemy("Golem", 640);
                b.Width = b.Height = 96; b.Y = 460 - 96;
                Beh(b, BehaviorKind.Health)!.Nums["max"] = 20;
                var ai = BehaviorCatalog.Info(BehaviorKind.BossAI).CreateData();
                ai.Texts["moves"] = moves; ai.Texts["starts"] = "start";
                b.Behaviors.Add(ai);
                b.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Damager).CreateData());
                if (moves == "fly") { b.Physics.Mode = PhysicsMode.None; b.Y = 120; }
                return b;
            }

            public GameObjectData AddEnemy(string name = "Enemy", double x = 600)
            {
                var e = Add(name, x, 396, 48, 64, "#E5484B", PhysicsMode.Dynamic, BehaviorKind.Health);
                Beh(e, BehaviorKind.Health)!.Nums["respawn"] = 0;
                return e;
            }

            /// <summary>Plays the current scene headless (seeded), ready to step.</summary>
            public PlayWindow Play()
            {
                var pw = new PlayWindow(P, S.Name);
                pw.TSeed(1); pw.TLoad();
                _plays.Add(pw);
                return pw;
            }
            public static void Run(PlayWindow pw, double seconds, Action<PlayWindow>? eachFrame = null)
            {
                int n = (int)Math.Round(seconds * 60);
                for (int i = 0; i < n; i++) { pw.TStep(1.0 / 60); eachFrame?.Invoke(pw); }
            }
            /// <summary>How many objects appear during <paramref name="seconds"/> of play (spawns, bullets, drops).</summary>
            public static int Spawned(PlayWindow pw, double seconds)
            {
                var seen = new HashSet<object>(); pw.TCollect(seen); int start = seen.Count;
                Run(pw, seconds, p => p.TCollect(seen));
                return seen.Count - start;
            }
            public static double Dist(PlayWindow pw, string a, string b)
            {
                double dx = pw.TX(a) - pw.TX(b), dy = pw.TY(a) - pw.TY(b);
                return Math.Sqrt(dx * dx + dy * dy);
            }
            /// <summary>Puts the player on top of an object (overlapping it) for touch tests.</summary>
            public void PutPlayerOn(PlayWindow pw, GameObjectData o) => pw.TPlace("Player", o.X + o.Width / 2 - 24, o.Y + o.Height / 2 - 32);

            public void Dispose() { foreach (var p in _plays) try { p.Close(); } catch { } }
        }

        private static string? Need(bool ok, string why) => ok ? null : why;

        private static List<AiCase> AiEvalCases()
        {
            var cases = new List<AiCase>();
            void C(string id, string prompt, Func<AiEvalKit, string?> check, Action<AiEvalKit>? setup = null, string[]? select = null)
                => cases.Add(new AiCase { Id = id, Prompt = prompt, Check = check, Setup = setup, Select = select });
            void Boss(string id, string prompt, Func<AiEvalKit, string?> check, string moves = "ground")
                => cases.Add(new AiCase { Id = id, Prompt = prompt, Check = check, Seq = SeqMode.Boss, SeqObject = "Golem", Setup = k => k.AddBoss(moves) });
            void Cut(string id, string prompt, Func<AiEvalKit, string?> check)
                => cases.Add(new AiCase { Id = id, Prompt = prompt, Check = check, Seq = SeqMode.Cutscene, SeqObject = "Intro", Setup = k =>
                {
                    k.AddBoss("ground");
                    var o = k.Add("Intro", 0, 0, 32, 32, "#00000000", PhysicsMode.None, BehaviorKind.Cutscene);
                    AiEvalKit.Beh(o, BehaviorKind.Cutscene)!.Texts["trigger"] = "start";
                } });
            static BossScript? Script(AiEvalKit k) { var j = AiEvalKit.Beh(k.Obj("Golem"), BehaviorKind.BossAI)?.Text("script", ""); return string.IsNullOrEmpty(j) ? null : SequenceJson.ParseBoss(j, out _); }
            static bool Uses(BossScript b, params string[] steps) => b.Attacks.SelectMany(a => a.Steps).Any(st => steps.Contains(st.Do));
            static GameObjectData? First(IEnumerable<GameObjectData> e) => e.FirstOrDefault();
            Action<AiEvalKit> box = k => k.Add("Box", 400, 200);
            Action<AiEvalKit> level2 = k => k.AddScene("Level2");

            // ---------- the player: stats and abilities ----------
            C("player-faster", "make the player run faster", k =>
                Need(AiEvalKit.Beh(k.Player, BehaviorKind.Platformer)?.Num("speed") > AiEvalKit.Default(BehaviorKind.Platformer, "speed"), "Platformer speed not raised"));
            C("pt-jump-higher", "faça o jogador pular mais alto", k =>
                Need(AiEvalKit.Beh(k.Player, BehaviorKind.Platformer)?.Num("jump") > AiEvalKit.Default(BehaviorKind.Platformer, "jump"), "Platformer jump not raised"));
            C("player-hearts", "give the player 5 hearts", k => Need(AiEvalKit.Beh(k.Player, BehaviorKind.Health)?.Num("max") == 5, "Health max is not 5"));
            C("player-doublejump-dash", "let the player double jump and dash", k =>
            {
                var p = AiEvalKit.Beh(k.Player, BehaviorKind.Platformer);
                return Need(p?.Num("doubleJump") >= 0.5 && p.Num("dash") >= 0.5, "doubleJump/dash not on");
            });
            C("player-sword", "give the player a sword attack", k => Need(AiEvalKit.Has(k.Player, BehaviorKind.ToolUse) || AiEvalKit.Has(k.Player, BehaviorKind.Hotbar), "no Tool Use / Hotbar on the player"));
            C("player-blue", "make the player blue", k => Need(AiEvalKit.Bluish(k.Player.Color), "player colour is " + k.Player.Color));
            C("player-bigger", "make the player twice as big", k => Need(k.Player.Width >= 90 && k.Player.Height >= 120, $"size is {k.Player.Width}x{k.Player.Height}"));
            C("player-no-health", "remove the health from the player", k => Need(!AiEvalKit.Has(k.Player, BehaviorKind.Health), "player still has Health"));
            C("player-stay-on-screen", "don't let the player walk out of the screen", k =>
            {
                var pw = k.Play(); pw.TKey("Left", true); AiEvalKit.Run(pw, 2);
                return Need(pw.TX("Player") >= -2, $"player walked out to x={pw.TX("Player"):0}");
            });
            C("teleport-key", "when I press T the player teleports to x 600", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 0.3); pw.TKey("T", true); AiEvalKit.Run(pw, 0.2); pw.TKey("T", false); AiEvalKit.Run(pw, 0.1);
                return Need(Math.Abs(pw.TX("Player") - 600) < 80, $"player x={pw.TX("Player"):0}");
            });

            // ---------- simple objects and moves ----------
            C("red-box", "create a red box in the middle of the screen", k =>
            {
                var o = k.New.FirstOrDefault(z => AiEvalKit.Reddish(z.Color));
                return o == null ? "no new red object" : Need(o.X + o.Width / 2 > 250 && o.X + o.Width / 2 < 710, $"not in the middle (x={o.X})");
            });
            C("box-moves-right", "create a box that keeps moving to the right", k =>
            {
                var o = First(k.New); if (o == null) return "nothing created";
                var pw = k.Play(); double x0 = pw.TX(o.Name); AiEvalKit.Run(pw, 2);
                return Need(pw.TX(o.Name) - x0 > 60, $"moved {pw.TX(o.Name) - x0:0}px");
            });
            C("follow-mouse", "make the Box follow the mouse", k =>
            {
                var pw = k.Play(); pw.TMouseAt(700, 150); AiEvalKit.Run(pw, 1.5);
                double cx = pw.TX("Box") + 32, cy = pw.TY("Box") + 32;
                return Need(Math.Abs(cx - 700) < 90 && Math.Abs(cy - 150) < 90, $"box at ({cx:0},{cy:0}), mouse at (700,150)");
            }, box);
            C("rotate-hold-key", "make the Box rotate while I hold R", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 0.5); double r0 = pw.TRot("Box");
                pw.TKey("R", true); AiEvalKit.Run(pw, 1); pw.TKey("R", false); double r1 = pw.TRot("Box"); AiEvalKit.Run(pw, 1);
                return Need(Math.Abs(r1 - r0) > 20, "didn't rotate while R held") ?? Need(Math.Abs(pw.TRot("Box") - r1) < 5, "kept rotating after R was released");
            }, box);
            C("circle", "make the Box move in a circle", k =>
            {
                var pw = k.Play(); double minX = 1e9, maxX = -1e9, minY = 1e9, maxY = -1e9;
                AiEvalKit.Run(pw, 4, p => { minX = Math.Min(minX, p.TX("Box")); maxX = Math.Max(maxX, p.TX("Box")); minY = Math.Min(minY, p.TY("Box")); maxY = Math.Max(maxY, p.TY("Box")); });
                return Need(maxX - minX > 40 && maxY - minY > 40, $"x range {maxX - minX:0}, y range {maxY - minY:0}");
            }, box);
            C("invisible", "make the Box invisible", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 0.3);
                return Need(pw.TOpacity("Box") < 0.05 || pw.TDead("Box") == false && !pw.TVisible("Box"), $"Box opacity {pw.TOpacity("Box"):0.00}");
            }, box);
            C("ball-bounces", "make a ball that bounces", k =>
                Need(k.New.Any(o => o.Physics.Mode == PhysicsMode.Dynamic && o.Physics.Bounciness >= 0.4), "no new dynamic object with bounciness"));
            C("pt-wall-right", "coloque uma parede no lado direito da tela", k =>
                Need(k.NewWith(BehaviorKind.Solid).Any(o => o.X > 600 && o.Height > o.Width), "no tall solid wall on the right"));

            // ---------- timers ----------
            C("fade-after", "after 5 seconds, slowly fade the Box out until it disappears", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 4); double o4 = pw.TOpacity("Box"); AiEvalKit.Run(pw, 7);
                return Need(o4 > 0.85, $"already fading at 4 s (opacity {o4:0.00})") ?? Need(pw.TDead("Box") || pw.TOpacity("Box") < 0.1, $"opacity at 11 s is {pw.TOpacity("Box"):0.00}");
            }, box);
            C("pt-fade", "depois de 3 segundos a Box vai sumindo devagar", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 10);
                return Need(pw.TDead("Box") || pw.TOpacity("Box") < 0.1, $"opacity at 10 s is {pw.TOpacity("Box"):0.00}");
            }, box);
            C("timer-jump", "every 2 seconds the Box jumps", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 1); double y0 = pw.TY("Box"), minY = y0;
                AiEvalKit.Run(pw, 5, p => minY = Math.Min(minY, p.TY("Box")));
                return Need(y0 - minY > 30, $"rose only {y0 - minY:0}px");
            }, k => k.Add("Box", 400, 396, 64, 64, "#E0A030", PhysicsMode.Dynamic));
            C("countdown", "make a 60 second countdown timer shown on the screen", k =>
            {
                var label = k.Objs.FirstOrDefault(o => AiEvalKit.Beh(o, BehaviorKind.Label)?.Text("text", "").Contains('{') == true);
                if (label == null) return "no Label showing a {variable}";
                var name = System.Text.RegularExpressions.Regex.Match(AiEvalKit.Beh(label, BehaviorKind.Label)!.Text("text", ""), @"\{([^}]+)\}").Groups[1].Value;
                var pw = k.Play(); AiEvalKit.Run(pw, 0.2); double v0 = pw.TVar(name); AiEvalKit.Run(pw, 3);
                return Need(v0 >= 55 && v0 <= 60, $"'{name}' starts at {v0}") ?? Need(pw.TVar(name) <= v0 - 2 && pw.TVar(name) >= v0 - 4, $"'{name}' went {v0} → {pw.TVar(name)} in 3 s");
            });
            C("after-goscene", "after 3 seconds go to Level2", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 2); var s2 = pw.TScene; AiEvalKit.Run(pw, 2);
                return Need(s2 != "Level2", "changed scene too early") ?? Need(pw.TScene == "Level2", "still in " + pw.TScene);
            }, level2);
            C("spawn-every-2s", "spawn an enemy every 2 seconds", k =>
            {
                var pw = k.Play(); int n = AiEvalKit.Spawned(pw, 7);
                return Need(n >= 2 && n <= 6, $"{n} objects appeared in 7 s");
            });
            C("rain-rocks", "make rocks fall from the sky every second", k =>
            {
                var pw = k.Play(); int n = AiEvalKit.Spawned(pw, 4.5);
                return Need(n >= 3, $"{n} objects appeared in 4.5 s");
            });
            C("boss-appears-later", "after 10 seconds a boss appears", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 8); int before = pw.TLiveCount; int n = AiEvalKit.Spawned(pw, 4);
                return Need(n >= 1, $"nothing appeared between 8 and 12 s (alive before: {before})");
            });
            C("blink", "make the Box blink every second", k =>
            {
                var pw = k.Play(); double lo = 1, hi = 0;
                AiEvalKit.Run(pw, 3, p => { double o = p.TVisible("Box") ? p.TOpacity("Box") : 0; lo = Math.Min(lo, o); hi = Math.Max(hi, o); });
                return Need(lo < 0.3 && hi > 0.7, $"opacity stayed between {lo:0.00} and {hi:0.00}");
            }, box);
            C("grow-on-start", "when the game starts, the Box grows to double its size over 2 seconds", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 2.6);
                return Need(pw.TScale("Box") >= 1.8 || pw.TW("Box") >= 115, $"scale {pw.TScale("Box"):0.00}, width {pw.TW("Box"):0}");
            }, box);

            // ---------- enemies and combat ----------
            C("spike-hurts", "add a spike on the ground that hurts the player", k =>
            {
                var s = First(k.NewWith(BehaviorKind.Damager)); if (s == null) return "no new object with Damager";
                var pw = k.Play(); double hp0 = pw.THp("Player"); k.PutPlayerOn(pw, s); AiEvalKit.Run(pw, 0.3);
                return Need(pw.THp("Player") < hp0, "touching it did no damage");
            });
            C("enemy-patrol", "create an enemy that walks back and forth", k =>
            {
                var e = First(k.New); if (e == null) return "nothing created";
                var pw = k.Play(); double minX = 1e9, maxX = -1e9; bool right = false, left = false; double last = pw.TX(e.Name);
                AiEvalKit.Run(pw, 8, p => { double x = p.TX(e.Name); if (x > last + 0.5) right = true; if (x < last - 0.5) left = true; last = x; minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); });
                return Need(right && left && maxX - minX > 40, $"right={right} left={left} range={maxX - minX:0}");
            });
            C("enemy-chase", "create an enemy that chases the player", k =>
            {
                var e = First(k.New); if (e == null) return "nothing created";
                var pw = k.Play(); AiEvalKit.Run(pw, 0.5); double d0 = AiEvalKit.Dist(pw, e.Name, "Player"); AiEvalKit.Run(pw, 2);
                return Need(AiEvalKit.Dist(pw, e.Name, "Player") < d0 - 40, $"distance {d0:0} → {AiEvalKit.Dist(pw, e.Name, "Player"):0}");
            });
            C("pt-enemy-chase", "crie um inimigo que persegue o jogador", k =>
            {
                var e = First(k.New); if (e == null) return "nothing created";
                var pw = k.Play(); AiEvalKit.Run(pw, 0.5); double d0 = AiEvalKit.Dist(pw, e.Name, "Player"); AiEvalKit.Run(pw, 2);
                return Need(AiEvalKit.Dist(pw, e.Name, "Player") < d0 - 40, $"distance {d0:0} → {AiEvalKit.Dist(pw, e.Name, "Player"):0}");
            });
            C("enemy-flee", "make the Enemy run away from the player", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 0.5); double d0 = AiEvalKit.Dist(pw, "Enemy", "Player"); AiEvalKit.Run(pw, 2);
                return Need(AiEvalKit.Dist(pw, "Enemy", "Player") > d0 + 40, $"distance {d0:0} → {AiEvalKit.Dist(pw, "Enemy", "Player"):0}");
            }, k => k.AddEnemy());
            C("enemy-shoots", "make an enemy that shoots at the player every 2 seconds", k =>
            {
                var pw = k.Play(); int n = AiEvalKit.Spawned(pw, 5);
                return Need(n >= 2, $"{n} bullets/objects appeared in 5 s");
            });
            C("enemy-explodes", "make the Enemy explode into particles when it dies", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 0.2); pw.TSetHp("Enemy", 0); AiEvalKit.Run(pw, 0.1);
                return Need(pw.TDead("Enemy"), "enemy didn't die at 0 health") ?? Need(pw.TParticles > 0, "no particles");
            }, k => k.AddEnemy());
            C("enemy-turns-red", "when the player touches the Enemy, the Enemy turns red", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 0.2); k.PutPlayerOn(pw, k.Obj("Enemy")!); AiEvalKit.Run(pw, 0.3);
                return Need(pw.TLogHas("tint") || AiEvalKit.Rules(k.Obj("Enemy")).Concat(AiEvalKit.Rules(k.Player)).Any(r => AiEvalKit.Is(r, "do", "tint")), "no tint rule fired");
            }, k => k.AddEnemy("Enemy", 600).Color = "#3B82F6");
            C("keep-old-rules", "also make the Enemy spin when I press R", k =>
            {
                var rules = AiEvalKit.Rules(k.Obj("Enemy"));
                return Need(rules.Any(r => AiEvalKit.Is(r, "when", "timer") && AiEvalKit.Is(r, "do", "jump")), "the Enemy's old 'every 2 s jump' rule was lost")
                    ?? Need(rules.Count >= 2 || AiEvalKit.Has(k.Obj("Enemy")!, BehaviorKind.Spin), "no new rule");
            }, k => { var e = k.AddEnemy(); e.Behaviors.Add(new BehaviorData { Kind = BehaviorKind.Custom, Texts = { ["script"] = "[{\"when\":\"timer\",\"every\":2,\"do\":\"jump\",\"force\":500}]" } }); });
            C("delete-enemy", "delete the Enemy", k => Need(k.Obj("Enemy") == null, "Enemy is still there"), k => k.AddEnemy());

            // ---------- bosses ----------
            C("boss-basic", "create a boss with 20 health that chases the player and hurts it on touch", k =>
            {
                var b = k.New.FirstOrDefault(o => AiEvalKit.Has(o, BehaviorKind.Health)); if (b == null) return "no new object with Health";
                return Need(AiEvalKit.Beh(b, BehaviorKind.Health)!.Num("max") == 20, "max health isn't 20")
                    ?? Need(AiEvalKit.Has(b, BehaviorKind.Damager), "no Damager")
                    ?? Need(AiEvalKit.Has(b, BehaviorKind.Chase) || AiEvalKit.Has(b, BehaviorKind.BossAI) || AiEvalKit.Has(b, BehaviorKind.PathChase), "doesn't chase");
            });
            C("boss-flying", "make a flying boss that shoots at the player", k =>
            {
                var b = k.New.FirstOrDefault(o => AiEvalKit.Has(o, BehaviorKind.BossAI)); if (b == null) return "no BossAI";
                return Need(AiEvalKit.Beh(b, BehaviorKind.BossAI)!.Text("moves", "ground") == "fly", "boss doesn't fly");
            });
            C("boss-dies-goscene", "when the Boss dies, go to Level2", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 0.2); pw.TSetHp("Boss", 0); AiEvalKit.Run(pw, 0.5);
                return Need(pw.TScene == "Level2", "still in " + pw.TScene);
            }, k => { k.AddScene("Level2"); var b = k.AddEnemy("Boss", 650); AiEvalKit.Beh(b, BehaviorKind.Health)!.Nums["max"] = 20; });
            C("player-dies-gameover", "when the player dies, go to the GameOver scene", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 0.2); pw.TSetHp("Player", 0); AiEvalKit.Run(pw, 0.5);
                return Need(pw.TScene == "GameOver", "still in " + pw.TScene);
            }, k => k.AddScene("GameOver"));

            // ---------- variables, HUD, UI ----------
            C("global-vars", "create a global variable score starting at 0 and lives at 3", k =>
                Need(VarScope.Of(k.P).Count >= 2 && k.P.Variables.TryGetValue("lives", out var l) && l == 3 && k.P.Variables.ContainsKey("score"), "score/lives not created"));
            C("score-hud", "show the score at the top of the screen", k =>
            {
                var l = k.Objs.FirstOrDefault(o => AiEvalKit.Beh(o, BehaviorKind.Label)?.Text("text", "").Contains("{score}", StringComparison.OrdinalIgnoreCase) == true);
                return l == null ? "no Label with {score}" : Need(k.OnScreenLayer(l), "label isn't on a fixed (UI) layer") ?? Need(l.Y < 150, $"label at y={l.Y}");
            });
            C("score-goal", "when the score reaches 10 go to Level2", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 0.3); var s0 = pw.TScene; pw.TSetVar("score", 10); AiEvalKit.Run(pw, 0.3);
                return Need(s0 != "Level2", "left before the score was 10") ?? Need(pw.TScene == "Level2", "still in " + pw.TScene);
            }, level2);
            C("coin-points", "make a coin that gives 10 points when collected, and show the score", k =>
            {
                var c = First(k.NewWith(BehaviorKind.Collectible)) ?? First(k.New); if (c == null) return "no coin";
                var pw = k.Play(); AiEvalKit.Run(pw, 0.2); double s0 = pw.TVar("score"); k.PutPlayerOn(pw, c); AiEvalKit.Run(pw, 0.3);
                return Need(pw.TVar("score") - s0 == 10, $"score went {s0} → {pw.TVar("score")}") ?? Need(pw.TDead(c.Name), "coin is still there")
                    ?? Need(k.Objs.Any(o => AiEvalKit.Beh(o, BehaviorKind.Label)?.Text("text", "").Contains("{score}", StringComparison.OrdinalIgnoreCase) == true), "no score label");
            });
            C("click-score", "when I click the Box, add 1 to the score", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 0.2); pw.TClick(432, 232); AiEvalKit.Run(pw, 0.1);
                return Need(pw.TVar("score") == 1, $"score is {pw.TVar("score")}");
            }, box);
            C("health-bar", "add a health bar for the player at the top left of the screen", k =>
            {
                var b = First(k.NewWith(BehaviorKind.Bar)); if (b == null) return "no Bar";
                return Need(AiEvalKit.Beh(b, BehaviorKind.Bar)!.Text("var", "") == "health", "bar doesn't show 'health'") ?? Need(k.OnScreenLayer(b), "bar isn't on the UI layer");
            });
            C("start-button", "make a Start button that goes to Level2 when clicked", k =>
            {
                var b = k.New.FirstOrDefault(o => AiEvalKit.Has(o, BehaviorKind.UIButton) || AiEvalKit.Has(o, BehaviorKind.GoToScene) || AiEvalKit.Rules(o).Count > 0);
                if (b == null) return "no button";
                var pw = k.Play(); AiEvalKit.Run(pw, 0.2); pw.TClick(b.X + b.Width / 2, b.Y + b.Height / 2); AiEvalKit.Run(pw, 0.3);
                return Need(pw.TScene == "Level2", "clicking it didn't go to Level2");
            }, level2);
            C("say-on-start", "when the level starts, the player says 'Where am I?'", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 1);
                return Need(pw.TDialog || pw.TDialogQueued > 0, "no dialogue shown"); // (the typewriter only types in a live window)
            });
            C("sign-touch-says", "when the player touches the Sign, show the message 'Welcome!'", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 0.2); k.PutPlayerOn(pw, k.Obj("Sign")!); AiEvalKit.Run(pw, 0.5);
                return Need(pw.TDialog || pw.TLogHas("Welcome"), "no message");
            }, k => k.Add("Sign", 300, 396, 40, 64, "#8B5A2B"));
            C("npc-talk", "create an NPC that says hello when I press E near it", k =>
                Need(k.New.Any(o => AiEvalKit.Has(o, BehaviorKind.Interact) || AiEvalKit.Rules(o).Any(r => AiEvalKit.Is(r, "do", "say"))), "no NPC with Interact/say"));

            // ---------- level pieces ----------
            C("door-level2", "create a door that goes to Level2", k =>
            {
                var d = First(k.NewWith(BehaviorKind.GoToScene)); if (d == null) return "no GoToScene door";
                var pw = k.Play(); AiEvalKit.Run(pw, 0.2); k.PutPlayerOn(pw, d); AiEvalKit.Run(pw, 0.3);
                return Need(pw.TScene == "Level2", "touching it didn't go to Level2");
            }, level2);
            C("moving-platform", "make a platform that moves up and down", k =>
            {
                var p = First(k.NewWith(BehaviorKind.Solid)); if (p == null) return "no new solid";
                var pw = k.Play(); double lo = 1e9, hi = -1e9; AiEvalKit.Run(pw, 6, q => { lo = Math.Min(lo, q.TY(p.Name)); hi = Math.Max(hi, q.TY(p.Name)); });
                return Need(hi - lo > 30, $"moved {hi - lo:0}px vertically");
            });
            C("trampoline", "add a trampoline", k => Need(k.NewWith(BehaviorKind.Spring).Any(), "no Spring"));
            C("crate", "add a crate the player can push", k => Need(k.NewWith(BehaviorKind.Pushable).Any(), "no Pushable"));
            C("checkpoint", "add a checkpoint flag in the middle of the level", k => Need(k.NewWith(BehaviorKind.Checkpoint).Any(), "no Checkpoint"));
            C("key-door", "make a locked door that opens when the player picks up a key", k =>
            {
                var g = First(k.NewWith(BehaviorKind.Gate)); var key = First(k.NewWith(BehaviorKind.AbilityPickup));
                if (g == null || key == null) return $"gate={g != null} key={key != null}";
                return Need(AiEvalKit.Beh(g, BehaviorKind.Gate)!.Text("var", "") == AiEvalKit.Beh(key, BehaviorKind.AbilityPickup)!.Text("var", ""), "gate and key use different variables");
            });
            C("day-night", "add a day and night cycle", k => Need(k.Objs.Any(o => AiEvalKit.Has(o, BehaviorKind.DayNight)), "no Day/Night"));
            C("pt-coin", "crie uma moeda que some quando o jogador encosta nela", k =>
            {
                var c = First(k.NewWith(BehaviorKind.Collectible)); if (c == null) return "no Collectible";
                var pw = k.Play(); AiEvalKit.Run(pw, 0.2); k.PutPlayerOn(pw, c); AiEvalKit.Run(pw, 0.3);
                return Need(pw.TDead(c.Name), "the coin didn't disappear");
            });

            // ---------- whole levels ----------
            C("level-platformer", "build a simple platformer level with 3 floating platforms, 5 coins, 2 enemies that patrol and a door to Level2 at the end", k =>
            {
                int plats = k.NewWith(BehaviorKind.Solid).Count(), coins = k.NewWith(BehaviorKind.Collectible).Count(),
                    enemies = k.New.Count(o => AiEvalKit.Has(o, BehaviorKind.Patrol) || AiEvalKit.Has(o, BehaviorKind.AdvancedPatrol)),
                    doors = k.New.Count(o => AiEvalKit.Beh(o, BehaviorKind.GoToScene)?.Text("scene", "") == "Level2");
                return Need(plats >= 3 && coins >= 5 && enemies >= 2 && doors >= 1, $"platforms {plats}, coins {coins}, patrolling enemies {enemies}, doors {doors}");
            }, level2);
            C("room-walls", "make a top-down room surrounded by walls", k => Need(k.NewWith(BehaviorKind.Solid).Count() >= 4, $"{k.NewWith(BehaviorKind.Solid).Count()} walls"));
            C("stairs", "make a staircase of 5 platforms going up to the right", k =>
            {
                var s = k.NewWith(BehaviorKind.Solid).OrderBy(o => o.X).ToList();
                if (s.Count < 5) return $"{s.Count} platforms";
                for (int i = 1; i < s.Count; i++) if (s[i].Y >= s[i - 1].Y) return "not going up to the right";
                return null;
            });
            C("coins-row", "put 10 coins in a row above the ground", k => Need(k.NewWith(BehaviorKind.Collectible).Count() >= 10, $"{k.NewWith(BehaviorKind.Collectible).Count()} coins"));
            C("pt-level", "monte uma fase com chão, 4 plataformas, 3 moedas e um inimigo que patrulha", k =>
            {
                int plats = k.NewWith(BehaviorKind.Solid).Count(), coins = k.NewWith(BehaviorKind.Collectible).Count(),
                    enemies = k.New.Count(o => AiEvalKit.Has(o, BehaviorKind.Patrol) || AiEvalKit.Has(o, BehaviorKind.AdvancedPatrol));
                return Need(plats >= 4 && coins >= 3 && enemies >= 1, $"platforms {plats}, coins {coins}, patrolling enemies {enemies}");
            });

            // ---------- the Boss AI card's ✨ Ask the AI ----------
            Boss("boss-ai-jump-ring", "it jumps toward the player and slams the ground, then shoots a ring of 8 bullets", k =>
            {
                var b = Script(k); if (b == null) return "no script";
                if (!Uses(b, "jump") || !Uses(b, "shoot")) return "no jump or no shoot step";
                var pw = k.Play(); double y0 = pw.TY("Golem"), minY = y0; int shots = 0;
                var seen = new HashSet<object>(); pw.TCollect(seen); int n0 = seen.Count;
                AiEvalKit.Run(pw, 14, p => { minY = Math.Min(minY, p.TY("Golem")); p.TCollect(seen); });
                shots = seen.Count - n0;
                return Need(y0 - minY > 30, "the boss never jumped") ?? Need(shots >= 4, $"{shots} bullets in 14 s");
            });
            Boss("boss-ai-phases", "phase 1: it charges at the player. Below half health it gets angry: it shoots spirals and rests less", k =>
            {
                var b = Script(k); if (b == null) return "no script";
                if (b.Phases.Count < 2) return $"{b.Phases.Count} phase(s)";
                var pw = k.Play(); AiEvalKit.Run(pw, 1); var p1 = pw.TBossPhase("Golem");
                pw.TSetHp("Golem", 8); AiEvalKit.Run(pw, 1.5);
                return Need(pw.TBossPhase("Golem") != p1, $"still in phase '{p1}' at 40% health");
            });
            Boss("boss-ai-fly", "it flies above the player, swoops down at them and flies back up, then drops bombs", k =>
            {
                var b = Script(k); if (b == null) return "no script";
                var pw = k.Play(); double lo = 1e9, hi = -1e9; AiEvalKit.Run(pw, 12, p => { lo = Math.Min(lo, p.TY("Golem")); hi = Math.Max(hi, p.TY("Golem")); });
                return Need(b.Attacks.Count >= 2, $"{b.Attacks.Count} attack(s)") ?? Need(hi - lo > 60, $"moved only {hi - lo:0}px up and down");
            }, "fly");
            Boss("boss-ai-death", "when it dies the screen shakes, it explodes into particles and the variable doorOpen becomes 1", k =>
            {
                var b = Script(k); if (b == null) return "no script";
                if (b.Death.Count == 0) return "no death steps";
                var pw = k.Play(); AiEvalKit.Run(pw, 1); pw.TSetHp("Golem", 0); AiEvalKit.Run(pw, 6);
                return Need(pw.TVar("doorOpen") == 1, $"doorOpen = {pw.TVar("doorOpen")}");
            });
            Boss("pt-boss-ai", "o chefe dá um dash na direção do jogador e depois pula; com 30% de vida ele fica mais rápido e atira", k =>
            {
                var b = Script(k); if (b == null) return "no script";
                return Need(b.Attacks.Count >= 2, $"{b.Attacks.Count} attack(s)") ?? Need(b.Phases.Count >= 2, $"{b.Phases.Count} phase(s)")
                    ?? Need(Uses(b, "dash") && Uses(b, "jump"), "no dash or no jump");
            });

            // ---------- the Cutscene card's ✨ Ask the AI ----------
            Cut("cutscene-intro", "the camera goes to the Golem, the Golem says 'You shall not pass!', then the camera goes back to the player", k =>
            {
                var pw = k.Play(); bool said = false;
                AiEvalKit.Run(pw, 8, p => { if (p.TDialog && p.TDialogText.Contains("pass", StringComparison.OrdinalIgnoreCase)) said = true; if (p.TDialog) p.TAdvanceDialog(); });
                return Need(said, "the line was never shown");
            });
            Cut("pt-cutscene", "o Player diz 'Onde estou?' e depois aparece o título 'Capítulo 1'", k =>
            {
                var pw = k.Play(); bool said = false;
                AiEvalKit.Run(pw, 8, p => { if (p.TDialog && p.TDialogText.Contains("Onde", StringComparison.OrdinalIgnoreCase)) said = true; if (p.TDialog) p.TAdvanceDialog(); });
                var j = AiEvalKit.Beh(k.Obj("Intro"), BehaviorKind.Cutscene)?.Text("script", "") ?? "";
                return Need(said, "the line was never shown") ?? Need(j.Contains("Cap", StringComparison.OrdinalIgnoreCase), "no title 'Capítulo 1'");
            });

            // ---------- 🎓 Learn mode: questions people ask ----------
            void Ask(string id, string question, params string[] anyOf)
                => cases.Add(new AiCase { Id = id, Prompt = question, Learn = true, Check = k =>
                       Need(!k.Changed, "Learn mode changed the project") ?? Need(k.Answer.Length > 40, "no real answer") ?? Need(anyOf.Any(w => k.Answer.Contains(w, StringComparison.OrdinalIgnoreCase)), "doesn't mention " + string.Join(" / ", anyOf)) });
            Ask("learn-double-jump", "how do I make the player double jump?", "double jump", "doubleJump");
            Ask("learn-patrol", "how do I make an enemy that walks back and forth?", "Patrol");
            Ask("learn-coin", "how do I make a coin that gives points?", "Collectible");
            Ask("learn-door", "how do I make a door that goes to the next level?", "Go To Scene", "GoToScene");
            Ask("learn-score", "how do I show the score on the screen?", "Label");
            Ask("learn-fade", "how do I make something fade away after a few seconds?", "Glide", "tween", "After some seconds", "opacity");
            Ask("learn-boss", "how do I make a boss with phases?", "Boss AI", "BossAI", "phase");
            Ask("learn-blocks", "what is the block editor for?", "when", "rule");
            Ask("pt-learn-spawn", "como faço inimigos aparecerem a cada 3 segundos?", "Spawner", "spawn", "timer");

            // ---------- harder: several mechanics at once ----------
            void Touch(AiEvalKit k, PlayWindow pw, string name, double secs = 0.4) { k.PutPlayerOn(pw, k.Obj(name)!); AiEvalKit.Run(pw, secs); }
            void Press(PlayWindow pw, string key) { pw.TKey(key, true); AiEvalKit.Run(pw, 0.1); pw.TKey(key, false); AiEvalKit.Run(pw, 0.2); }
            C("near-chase", "the Enemy chases the player only when the player is closer than 250 pixels; otherwise it stands still", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 1); double x0 = pw.TX("Enemy"); AiEvalKit.Run(pw, 1);
                if (Math.Abs(pw.TX("Enemy") - x0) > 15) return $"moved {pw.TX("Enemy") - x0:0}px with the player far away";
                pw.TPlace("Player", pw.TX("Enemy") - 180, 396); AiEvalKit.Run(pw, 0.2); double d0 = AiEvalKit.Dist(pw, "Enemy", "Player"); AiEvalKit.Run(pw, 1.5);
                return Need(AiEvalKit.Dist(pw, "Enemy", "Player") < d0 - 40, $"didn't chase when near ({d0:0} → {AiEvalKit.Dist(pw, "Enemy", "Player"):0})");
            }, k => k.AddEnemy("Enemy", 780));
            C("lava-lives", "the player has 3 lives shown on the screen. Touching the Lava takes away a life and sends the player back to the start. When the lives run out, go to the GameOver scene", k =>
            {
                if (!k.Objs.Any(o => AiEvalKit.Beh(o, BehaviorKind.Label)?.Text("text", "").Contains('{') == true)) return "the lives aren't shown";
                var pw = k.Play(); AiEvalKit.Run(pw, 0.5);
                for (int i = 0; i < 4 && pw.TScene != "GameOver"; i++) { pw.TPlace("Player", 430, 386); AiEvalKit.Run(pw, 1.6); }
                return Need(pw.TScene == "GameOver", "4 touches of the Lava didn't end the game");
            }, k => { k.AddScene("GameOver"); k.Add("Lava", 400, 440, 120, 20, "#FF5500"); });
            C("all-coins", "when the player has collected all 3 coins, go to Level2", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 0.3); Touch(k, pw, "Coin"); Touch(k, pw, "Coin2");
                if (pw.TScene == "Level2") return "left after 2 coins";
                Touch(k, pw, "Coin3", 0.6);
                return Need(pw.TScene == "Level2", "still here after the 3rd coin");
            }, k => { k.AddScene("Level2"); foreach (var (n, x) in new[] { ("Coin", 300), ("Coin2", 450), ("Coin3", 600) }) k.Add(n, x, 400, 24, 24, "#FFD700", PhysicsMode.None, BehaviorKind.Collectible); });
            C("turret-front", "the Turret shoots at the player every second, but only while the player is to its left", k =>
            {
                var pw = k.Play(); int left = AiEvalKit.Spawned(pw, 3.2);
                pw.TPlace("Player", 900, 396); AiEvalKit.Run(pw, 0.5); int right = AiEvalKit.Spawned(pw, 3);
                return Need(left >= 2, $"{left} shots with the player on its left") ?? Need(right == 0, $"{right} shots with the player on its right");
            }, k => k.Add("Turret", 700, 380, 40, 40, "#777788"));
            C("star-doubles", "every time the player touches a Star, the score doubles and that Star disappears", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 0.2); Touch(k, pw, "Star");
                double s1 = pw.TVar("score"); Touch(k, pw, "Star2");
                return Need(s1 == 2 && pw.TVar("score") == 4, $"score 1 → {s1} → {pw.TVar("score")}") ?? Need(pw.TDead("Star") && pw.TDead("Star2"), "a Star is still there");
            }, k => { k.P.Variables["score"] = 1; k.Add("Star", 300, 380, 32, 32, "#FFE066"); k.Add("Star2", 520, 380, 32, 32, "#FFE066"); });
            C("platform-after-step", "the Platform starts moving up and down only after the player steps on it", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 1); double y0 = pw.TY("Platform"); AiEvalKit.Run(pw, 1);
                if (Math.Abs(pw.TY("Platform") - y0) > 2) return "moved before the player stepped on it";
                pw.TPlace("Player", 330, 316); double lo = 1e9, hi = -1e9; AiEvalKit.Run(pw, 3, p => { lo = Math.Min(lo, p.TY("Platform")); hi = Math.Max(hi, p.TY("Platform")); });
                return Need(hi - lo > 20, $"moved {hi - lo:0}px after the player stood on it");
            }, k => k.Add("Platform", 300, 380, 140, 20, "#6B5B45", PhysicsMode.Static, BehaviorKind.Solid));
            C("night-spawn", "spawn an Enemy every 2 seconds, but only at night", k =>
            {
                var pw = k.Play(); pw.TSetTimeNow("noon"); AiEvalKit.Run(pw, 0.3); int day = AiEvalKit.Spawned(pw, 4.5);
                pw.TSetTimeNow("midnight"); AiEvalKit.Run(pw, 0.3); int night = AiEvalKit.Spawned(pw, 4.5);
                return Need(day == 0, $"{day} spawned by day") ?? Need(night >= 1, "nothing spawned at night");
            }, k => { k.Add("Sky", 0, 0, 10, 10, "#00000000", PhysicsMode.None, BehaviorKind.DayNight); k.AddEnemy("Enemy", 800); });
            C("shop", "when I press E near the Shopkeeper and have at least 10 coins, I pay 10 coins and get a Sword", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 0.3); pw.TPlace("Player", 900, 396); AiEvalKit.Run(pw, 0.2); Press(pw, "E");
                if (pw.TVar("coins") != 15) return "paid while far from the Shopkeeper";
                pw.TPlace("Player", 330, 396); AiEvalKit.Run(pw, 0.2); Press(pw, "E");
                if (pw.TVar("coins") != 5 || pw.TItem("Sword") < 1) return $"near: coins {pw.TVar("coins")}, swords {pw.TItem("Sword")}";
                Press(pw, "E");
                return Need(pw.TVar("coins") == 5, "paid again with only 5 coins");
            }, k => { k.P.Variables["coins"] = 15; k.Add("Shopkeeper", 300, 396, 40, 64, "#8E6CFF"); });
            C("combo", "every time I press J, add 1 to combo. If I don't press J for 1 second, combo goes back to 0. When combo reaches 3, show the message 'Combo!' and set combo to 0", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 0.3); Press(pw, "J"); Press(pw, "J");
                if (pw.TVar("combo") != 2) return $"combo is {pw.TVar("combo")} after 2 presses";
                AiEvalKit.Run(pw, 1.5);
                if (pw.TVar("combo") != 0) return $"combo didn't go back to 0 ({pw.TVar("combo")})";
                Press(pw, "J"); Press(pw, "J"); Press(pw, "J"); AiEvalKit.Run(pw, 0.2);
                return Need(pw.TDialog || pw.TDialogQueued > 0, "no 'Combo!' after 3 quick presses");
            });
            C("health-pickup", "when the player touches the Heart, the player gets 1 health back (never more than the maximum) and the Heart disappears", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 0.3); pw.TSetHp("Player", 1); AiEvalKit.Run(pw, 0.1); Touch(k, pw, "Heart");
                return Need(pw.THp("Player") == 2, $"health is {pw.THp("Player")}") ?? Need(pw.TDead("Heart") || pw.TOpacity("Heart") < 0.05, "the Heart is still there");
            }, k => k.Add("Heart", 300, 400, 28, 28, "#FF4D6D"));
            C("pt-door-key", "quando o jogador apertar F perto da Porta e tiver a chave (hasKey igual a 1), a Porta some", k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 0.3); pw.TPlace("Player", 450, 396); AiEvalKit.Run(pw, 0.2); Press(pw, "F");
                if (pw.TDead("Porta") || pw.TOpacity("Porta") < 0.05) return "opened without the key";
                pw.TSetVar("hasKey", 1); Press(pw, "F"); AiEvalKit.Run(pw, 0.5);
                return Need(pw.TDead("Porta") || pw.TOpacity("Porta") < 0.05, "didn't open with the key");
            }, k => { k.P.Variables["hasKey"] = 0; k.Add("Porta", 500, 364, 40, 96, "#8B5A2B"); });
            C("level-boss", "build a level with 2 platforms and 3 coins, and at the end put a boss with 10 health that chases the player", k =>
            {
                int plats = k.NewWith(BehaviorKind.Solid).Count(), coins = k.NewWith(BehaviorKind.Collectible).Count();
                var boss = k.New.FirstOrDefault(o => AiEvalKit.Beh(o, BehaviorKind.Health)?.Num("max") == 10);
                return Need(plats >= 2 && coins >= 3, $"platforms {plats}, coins {coins}") ?? Need(boss != null, "no boss with 10 health")
                    ?? Need(AiEvalKit.Has(boss!, BehaviorKind.Chase) || AiEvalKit.Has(boss!, BehaviorKind.BossAI) || AiEvalKit.Has(boss!, BehaviorKind.PathChase), "the boss doesn't chase");
            });

            // ---------- conversations: each request builds on the last ----------
            cases.Add(new AiCase { Id = "talk-slime", Prompt = "create an enemy called Slime that walks back and forth", Then = new[] { "make the Slime faster", "when the Slime dies it explodes into particles" }, Check = k =>
            {
                var s = k.Obj("Slime"); if (s == null) return "no Slime";
                var pw = k.Play(); AiEvalKit.Run(pw, 0.3); pw.TSetHp("Slime", 0); AiEvalKit.Run(pw, 0.2);
                return Need(AiEvalKit.Beh(s, BehaviorKind.Patrol)?.Num("speed") > AiEvalKit.Default(BehaviorKind.Patrol, "speed") || AiEvalKit.Rules(s).Count > 0, "the Slime isn't faster")
                    ?? Need(pw.TParticles > 0, "no explosion when it dies");
            } });
            cases.Add(new AiCase { Id = "talk-player", Prompt = "give the player a double jump", Then = new[] { "also let the player dash", "and make the player red" }, Check = k =>
            {
                var p = AiEvalKit.Beh(k.Player, BehaviorKind.Platformer);
                return Need(p?.Num("doubleJump") >= 0.5, "lost the double jump") ?? Need(p?.Num("dash") >= 0.5, "no dash") ?? Need(AiEvalKit.Reddish(k.Player.Color), "not red: " + k.Player.Color);
            } });
            cases.Add(new AiCase { Id = "talk-stop-spin", Prompt = "make the Box spin", Then = new[] { "stop the Box from spinning" }, Setup = box, Check = k =>
            {
                var pw = k.Play(); AiEvalKit.Run(pw, 0.5); double r0 = pw.TRot("Box"); AiEvalKit.Run(pw, 1);
                return Need(Math.Abs(pw.TRot("Box") - r0) < 2, "still spinning");
            } });

            // ---------- the same logic through the block editor's ✨ Ask the AI (Scratch must do what the AI does) ----------
            foreach (var (id, target) in new[] { ("fade-after", "Box"), ("pt-fade", "Box"), ("timer-jump", "Box"), ("follow-mouse", "Box"), ("rotate-hold-key", "Box"), ("circle", "Box"),
                         ("blink", "Box"), ("grow-on-start", "Box"), ("invisible", "Box"), ("click-score", "Box"), ("enemy-flee", "Enemy"), ("enemy-explodes", "Enemy"),
                         ("sign-touch-says", "Sign"), ("keep-old-rules", "Enemy"), ("teleport-key", "Player"), ("score-goal", "Player"), ("after-goscene", "Player"),
                         ("player-dies-gameover", "Player"), ("boss-dies-goscene", "Boss"), ("near-chase", "Enemy"), ("turret-front", "Turret"), ("star-doubles", "Player"),
                         ("shop", "Shopkeeper"), ("combo", "Player"), ("health-pickup", "Heart"), ("pt-door-key", "Porta") })
            {
                var src = cases.First(c => c.Id == id);
                cases.Add(new AiCase { Id = "blocks-" + id, Prompt = src.Prompt, Setup = src.Setup, Check = src.Check, Blocks = target });
            }

            // ---------- selection ----------
            C("selected-spin", "make it spin", k => Need(AiEvalKit.Has(k.Obj("Box")!, BehaviorKind.Spin) || AiEvalKit.Rules(k.Obj("Box")).Any(r => AiEvalKit.Is(r, "do", "rotate")), "the selected Box doesn't spin")
                                                   ?? Need(!AiEvalKit.Has(k.Player, BehaviorKind.Spin), "the player got Spin"), box, new[] { "Box" });
            return cases;
        }
    }
}
