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
                _ = RunAiEvalAsync(editor, cases, log, choice.StartsWith("cloud:")).ContinueWith(t => Dispatcher.Invoke(() =>
                {
                    if (t.Exception != null) try { File.AppendAllText(log, "\nEVAL EXCEPTION: " + t.Exception); } catch { }
                    try { Directory.Delete(temp, true); } catch { }
                    Shutdown();
                }));
            }
            catch (Exception ex) { try { File.AppendAllText(log, "FATAL: " + ex); } catch { } if (temp != null) try { Directory.Delete(temp, true); } catch { } Shutdown(); }
        }

        private static async Task RunAiEvalAsync(MainWindow ed, List<AiCase> cases, string log, bool cloud)
        {
            if (!cloud) await OllamaManager.EnsureRunningAsync();
            ed.EvalReset();
            try { File.AppendAllText(log, $"system prompt: {ed.TestBuildPrompt(cloud).Length} chars\n\n"); } catch { }
            int pass = 0, n = 0;
            var fails = new List<string>();
            foreach (var c in cases)
            {
                n++;
                ed.EvalReset();
                var kit = new AiEvalKit(ed);
                string verdict, detail = "";
                var sw = Stopwatch.StartNew();
                try
                {
                    c.Setup?.Invoke(kit);
                    kit.Baseline();
                    if (c.Select != null) ed.EvalSelect(c.Select);
                    var (ok, summary, reply) = await ed.EvalBuild(c.Prompt);
                    string? why;
                    try { why = ok ? c.Check(kit) : "nothing was applied"; }
                    catch (Exception ex) { why = "check crashed: " + ex.GetType().Name + " " + ex.Message; }
                    verdict = why == null ? "PASS" : "FAIL";
                    if (why == null) pass++; else fails.Add(c.Id);
                    detail = (why != null ? "  WHY: " + why + "\n" : "") + "  APPLIED: " + summary.Replace("\n", " | ") + "\n  STATE: " + ed.EvalDump() +
                             (why != null ? "\n  REPLY: " + OneLine(reply, 2500) : "");
                }
                catch (Exception ex) { verdict = "ERROR"; fails.Add(c.Id); detail = "  " + ex.GetType().Name + ": " + ex.Message; }
                finally { kit.Dispose(); }
                var line = $"{verdict}  [{c.Id}] {c.Prompt}  ({sw.Elapsed.TotalSeconds:0.0}s)\n{detail}\n";
                try { File.AppendAllText(log, line); } catch { }
                if (cloud) await Task.Delay(1500); // be gentle with the free tiers' per-minute limits
            }
            var total = $"\n{pass}/{n} passed" + (fails.Count > 0 ? "  — failed: " + string.Join(", ", fails) : "  — ALL PASS") + "\n";
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
            public Func<AiEvalKit, string?> Check = _ => null; // null = pass, else why it failed
        }

        /// <summary>What a check can look at: the edited project, what the AI added, and a headless play of it.</summary>
        public sealed class AiEvalKit : IDisposable
        {
            public readonly MainWindow Ed;
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
            C("invisible", "make the Box invisible", k => Need(k.Obj("Box") is { } b && b.Opacity < 0.05, "Box still visible"), box);
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
                return Need(pw.TDialog && pw.TDialogText.Length > 0 || pw.TDialogQueued > 0, "no dialogue shown");
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

            // ---------- selection ----------
            C("selected-spin", "make it spin", k => Need(AiEvalKit.Has(k.Obj("Box")!, BehaviorKind.Spin) || AiEvalKit.Rules(k.Obj("Box")).Any(r => AiEvalKit.Is(r, "do", "rotate")), "the selected Box doesn't spin")
                                                   ?? Need(!AiEvalKit.Has(k.Player, BehaviorKind.Spin), "the player got Spin"), box, new[] { "Box" });
            return cases;
        }
    }
}
