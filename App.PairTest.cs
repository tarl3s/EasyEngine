using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace EasyEngine;

// --pairtest (dev tool): every visible behavior alone, every pair (both orders when both drive the speed) and a few
// suspicious triples, each on a small object in a headless scene (ground, a Platformer + Health player, a touch target),
// played ~5 s with the real StepLogic. It flags what looks wrong: exceptions, NaN/∞, absurd speeds, falling through the
// ground, an object that should move but is frozen, jitter (two behaviors fighting over its speed), drifting away after
// a hit, health below 0 or above max, console warnings/errors, and the player no longer answering the keys afterwards.
// Writes %TEMP%\easyengine_pairtest.log: one line per suspicious combination, then a summary line.
public partial class App
{
    private static readonly BehaviorKind[] PairMovers =
    {
        BehaviorKind.Platformer, BehaviorKind.TopDown, BehaviorKind.Patrol, BehaviorKind.AdvancedPatrol, BehaviorKind.Chase,
        BehaviorKind.PathChase, BehaviorKind.Projectile, BehaviorKind.Pushable, BehaviorKind.StayInside, BehaviorKind.BossAI,
        BehaviorKind.Spring,
    };
    // these move the object by themselves: frozen for 5 s = something stops them
    private static readonly BehaviorKind[] ShouldMove =
        { BehaviorKind.Patrol, BehaviorKind.AdvancedPatrol, BehaviorKind.Chase, BehaviorKind.PathChase, BehaviorKind.Projectile };

    private const double PairGroundY = 700;
    private static StringBuilder? Trace; // --trace: the first 120 ticks of each played combination (use with --only)

    private void RunPairTest(string[] args)
    {
        var log = Path.Combine(Path.GetTempPath(), "easyengine_pairtest.log");
        var sb = new StringBuilder();
        var temp = Path.Combine(Path.GetTempPath(), "EE_PairTest_" + Guid.NewGuid().ToString("N")[..6]);
        int runs = 0, flagged = 0, cPass = 0, cFail = 0;
        var bySymptom = new Dictionary<string, int>();
        var started = DateTime.Now;
        var expected = new StringBuilder(); // flagged, but that is what those behaviors mean together
        try
        {
            Directory.CreateDirectory(temp);
            var project = ProjectIO.CreateNew("Pairs", temp);
            var kinds = BehaviorCatalog.All.Where(b => !b.Hidden && b.Kind != BehaviorKind.Terrain).Select(b => b.Kind).ToList();
            var cases = new List<(BehaviorKind[] kinds, PhysicsMode mode, bool onPlayer, bool restFirst)>();
            foreach (var k in kinds)
                foreach (var m in new[] { PhysicsMode.None, PhysicsMode.Static, PhysicsMode.Dynamic }) cases.Add((new[] { k }, m, false, false));
            for (int i = 0; i < kinds.Count; i++)
                for (int j = i + 1; j < kinds.Count; j++)
                {
                    var a = kinds[i]; var b = kinds[j];
                    foreach (var m in new[] { PhysicsMode.None, PhysicsMode.Dynamic })
                    {
                        cases.Add((new[] { a, b }, m, false, false));
                        if (PairMovers.Contains(a) && PairMovers.Contains(b)) cases.Add((new[] { b, a }, m, false, false)); // the order matters for movers
                    }
                }
            foreach (var k in kinds) cases.Add((new[] { k }, PhysicsMode.Dynamic, true, false)); // the player itself gets it too
            foreach (var k in kinds.Where(k => BehaviorCatalog.Info(k).Repeatable)) cases.Add((new[] { k, k }, PhysicsMode.None, false, false)); // two of one kind (BehaviorInfo.Repeatable)
            // "Only while" false on the first mover: the second one must still move it (a resting mover used to stop the others)
            foreach (var a in new[] { BehaviorKind.Patrol, BehaviorKind.Chase, BehaviorKind.PathChase, BehaviorKind.AdvancedPatrol, BehaviorKind.Platformer, BehaviorKind.TopDown })
                foreach (var b in ShouldMove)
                    if (a != b && b != BehaviorKind.Projectile) foreach (var m in new[] { PhysicsMode.None, PhysicsMode.Dynamic }) cases.Add((new[] { a, b }, m, false, true));
            foreach (var t in SuspectTriples) foreach (var m in new[] { PhysicsMode.None, PhysicsMode.Dynamic }) cases.Add((t, m, false, false));
            cases.Add((new[] { BehaviorKind.Terrain }, PhysicsMode.None, false, false));
            cases.Add((new[] { BehaviorKind.Checkpoint, BehaviorKind.FollowCamera }, PhysicsMode.Dynamic, true, false));

            if (args.Contains("--trace")) Trace = sb;
            string? only = args.SkipWhile(a => a != "--only").Skip(1).FirstOrDefault(); // --only Patrol,Chase
            // the behavior-combination battery of --fulltest first (FullTest.BehaviorCombos.cs): quick to rerun on its own
            var sims = new List<PlayWindow>();
            var kit = new FullTestKit((name, f) =>
            {
                bool ok; string ex = "";
                try { ok = f(); } catch (Exception e) { ok = false; ex = $"  [EX: {e.GetType().Name}: {e.Message}]"; }
                if (ok) cPass++; else { cFail++; sb.AppendLine("FAIL  " + name + ex); }
            }, (script, phys, extra) => { var p = PlayWindow.NewScriptSim(script, out _, phys: phys, extra: extra); sims.Add(p); return p; }, s => s, sims);
            FeatureTestsBehaviorCombos(kit);
            foreach (var s in sims) { try { s.Close(); } catch { } }
            DeleteTempFolders("EE_Full_Combo_*");
            sb.AppendLine($"combination tests: {cPass}/{cPass + cFail} passed\n");
            if (args.Contains("--combos")) cases.Clear(); // --pairtest --combos: just the battery
            foreach (var c in cases)
            {
                if (only != null && !only.Split(',').All(o => c.kinds.Any(k => k.ToString().Equals(o.Trim(), StringComparison.OrdinalIgnoreCase)))) continue;
                runs++;
                Trace?.AppendLine($"{string.Join(" + ", c.kinds)} [{c.mode}]{(c.restFirst ? " (first one resting)" : "")}{(c.onPlayer ? " (on the player)" : "")}");
                var problems = PairRun(project, c.kinds, c.mode, c.onPlayer, c.restFirst);
                if (problems.Count == 0) continue;
                var why = ByDesign(c.kinds, c.mode, c.onPlayer, problems);
                if (why != null) { expected.AppendLine($"{string.Join(" + ", c.kinds)}{(c.onPlayer ? " (on the player)" : "")} [{c.mode}]  →  {why}"); continue; }
                flagged++;
                foreach (var p in problems) { var key = p.Split(':')[0]; bySymptom[key] = bySymptom.GetValueOrDefault(key) + 1; }
                sb.AppendLine($"{string.Join(" + ", c.kinds)}{(c.onPlayer ? " (on the player)" : "")}{(c.restFirst ? " (first one resting)" : "")} [{c.mode}]  →  {string.Join(" · ", problems)}");
            }
        }
        catch (Exception ex) { sb.AppendLine("FATAL: " + ex); flagged++; }
        DeleteTempFolders("EE_PairTest_*");

        sb.Insert(0, $"pairtest {DateTime.Now}\n\n");
        sb.AppendLine();
        foreach (var kv in bySymptom.OrderByDescending(k => k.Value)) sb.AppendLine($"  {kv.Key}: {kv.Value}");
        if (expected.Length > 0) sb.AppendLine("\nExpected (by design, not counted):").Append(expected);
        sb.AppendLine($"\npairtest: combination tests {cPass}/{cPass + cFail}, {runs} combinations played, {flagged} suspicious ({(DateTime.Now - started).TotalSeconds:0}s)"
                      + (flagged == 0 && cFail == 0 ? "  — ALL CLEAN" : ""));
        try { File.WriteAllText(log, sb.ToString()); } catch { }
        Shutdown();
    }

    private static readonly BehaviorKind[][] SuspectTriples =
    {
        new[] { BehaviorKind.Patrol, BehaviorKind.Chase, BehaviorKind.Health },
        new[] { BehaviorKind.Platformer, BehaviorKind.Patrol, BehaviorKind.Damager },
        new[] { BehaviorKind.Platformer, BehaviorKind.Chase, BehaviorKind.Health },
        new[] { BehaviorKind.Pushable, BehaviorKind.Patrol, BehaviorKind.Solid },
        new[] { BehaviorKind.Solid, BehaviorKind.Patrol, BehaviorKind.Damager },
        new[] { BehaviorKind.Chase, BehaviorKind.Health, BehaviorKind.Damager },
        new[] { BehaviorKind.Patrol, BehaviorKind.StayInside, BehaviorKind.Health },
        new[] { BehaviorKind.Projectile, BehaviorKind.Damager, BehaviorKind.Health },
        new[] { BehaviorKind.BossAI, BehaviorKind.Health, BehaviorKind.Damager },
        new[] { BehaviorKind.Spawner, BehaviorKind.Patrol, BehaviorKind.Health },
    };

    // One combination on the object "S": returns the problems seen ("symptom: details"), empty when it played cleanly.
    private static List<string> PairRun(Project project, BehaviorKind[] kinds, PhysicsMode mode, bool onPlayer, bool restFirst = false)
    {
        var problems = new List<string>();
        void Flag(string s) { if (!problems.Contains(s)) problems.Add(s); }

        var scene = project.GetStartScene();
        scene.Width = 1600; scene.Height = 900;
        foreach (var l in scene.Layers) l.Objects.Clear();
        var layer = scene.Layers.First(l => l.Kind == LayerKind.Base);
        layer.Objects.Add(new GameObjectData { Id = 1, Name = "Ground", X = 0, Y = PairGroundY, Width = 1600, Height = 60, Color = "#555555",
            Physics = new PhysicsData { Mode = PhysicsMode.Static }, Behaviors = { new BehaviorData { Kind = BehaviorKind.Solid } } });
        var player = new GameObjectData { Id = 2, Name = "Player", Type = "Player", X = 100, Y = PairGroundY - 48, Width = 32, Height = 48, Color = "#3BC97A",
            Physics = new PhysicsData { Mode = PhysicsMode.Dynamic },
            Behaviors = { BehaviorCatalog.Info(BehaviorKind.Platformer).CreateData(), BehaviorCatalog.Info(BehaviorKind.Health).CreateData() } };
        layer.Objects.Add(player);
        layer.Objects.Add(new GameObjectData { Id = 3, Name = "Target", X = 900, Y = PairGroundY - 40, Width = 40, Height = 40, Color = "#88AAFF" });
        GameObjectData subject;
        if (onPlayer) subject = player;
        else
        {
            subject = new GameObjectData { Id = 4, Name = "S", Type = "Enemy", X = 600, Y = PairGroundY - 140, Width = 40, Height = 40, Color = "#E5484B",
                Physics = new PhysicsData { Mode = mode } };
            layer.Objects.Add(subject);
        }
        foreach (var k in kinds)
        {
            if (subject.Behaviors.Any(b => b.Kind == k)) continue;
            var bh = BehaviorCatalog.Info(k).CreateData();
            PairTune(bh, onPlayer);
            if (restFirst && k == kinds[0]) bh.Texts["cond"] = "0"; // "only while 0": always resting
            subject.Behaviors.Add(bh);
        }
        string sName = subject.Name;

        PlayWindow? pw = null;
        try
        {
            pw = new PlayWindow(project, scene.Name);
            pw.TSeed(7);
            pw.TLoad();
            var s0 = pw.TProbe(sName);
            if (s0 == null) { Flag("missing: the object never appeared"); return problems; }
            double minX = s0.Value.X, maxX = s0.Value.X, minY = s0.Value.Y, maxY = s0.Value.Y;
            double prevX = s0.Value.X, prevY = s0.Value.Y, prevDx = 0, prevDy = 0;
            int flipsX = 0, flipsY = 0;
            bool everDynamic = false;
            for (int i = 0; i < 300; i++)
            {
                if ((i == 60 || i == 200) && kinds.Contains(BehaviorKind.Health) && !onPlayer) pw.THit(sName, 0.5, 300, 560); // a sword hit from its left
                try { pw.TStep(); }
                catch (Exception ex) { Flag($"exception: {ex.GetType().Name}: {ex.Message}"); break; }
                foreach (var (n, x, y, vx, vy) in pw.TAll())
                {
                    if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(vx) || !double.IsFinite(vy)) Flag($"nan: {n}");
                    else if (Math.Abs(vx) > 3000 || Math.Abs(vy) > 3000) Flag($"absurd speed: {n} ({vx:0},{vy:0})");
                }
                foreach (var who in new[] { sName, "Player" })
                {
                    var st = pw.TProbe(who);
                    if (st is not { } p || p.Dead) continue;
                    if (p.MaxHp > 0 && (p.Hp < -0.001 || p.Hp > p.MaxHp + 0.001)) Flag($"health out of range: {who} {p.Hp:0.##}/{p.MaxHp:0.##}");
                    if (p.Dynamic && p.Y > PairGroundY + 2 && p.Y < PairGroundY + 300 && p.X > 0 && p.X < 1560) Flag($"fell through the ground: {who}"); // over the ground, not off its end
                }
                var s = pw.TProbe(sName);
                if (s is not { } q || q.Dead) break;
                if (Trace != null && i < 120) Trace.AppendLine($"  {i}: x={q.X:0.0} y={q.Y:0.0} vx={q.VX:0.0} vy={q.VY:0.0} grounded={q.Grounded}");
                everDynamic |= q.Dynamic;
                double dx = q.X - prevX, dy = q.Y - prevY;
                if (Math.Abs(dx) > 0.3 && Math.Abs(prevDx) > 0.3 && Math.Sign(dx) != Math.Sign(prevDx)) flipsX++;
                if (Math.Abs(dy) > 0.3 && Math.Abs(prevDy) > 0.3 && Math.Sign(dy) != Math.Sign(prevDy)) flipsY++;
                if (Math.Abs(dx) > 0.3) prevDx = dx;
                if (Math.Abs(dy) > 0.3) prevDy = dy;
                prevX = q.X; prevY = q.Y;
                minX = Math.Min(minX, q.X); maxX = Math.Max(maxX, q.X); minY = Math.Min(minY, q.Y); maxY = Math.Max(maxY, q.Y);
            }
            var end = pw.TProbe(sName);
            bool alive = end is { Dead: false };
            if (alive && !onPlayer)
            {
                if (flipsX > 25) Flag($"jitter: {flipsX} left/right flips");
                if (flipsY > 25) Flag($"jitter: {flipsY} up/down flips");
                bool mover = kinds.Any(k => ShouldMove.Contains(k)) && !kinds.Contains(BehaviorKind.BossAI) && mode != PhysicsMode.Static
                             && !kinds.Contains(BehaviorKind.Pushable);
                if (mover && maxX - minX < 3 && maxY - minY < 3) Flag("frozen: should move but stayed put");
                bool stillOne = !kinds.Any(k => ShouldMove.Contains(k) || PairMovers.Contains(k) || k is BehaviorKind.Platformer or BehaviorKind.Custom or BehaviorKind.Spawner);
                if (stillOne && !everDynamic && (maxX - minX > 300 || maxY - minY > 300)) Flag($"drifts away: moved {Math.Max(maxX - minX, maxY - minY):0}px");
            }

            // the player still answers the keys afterwards (unless the case made it something else)
            var pl = pw.TProbe("Player");
            if (pl is { Dead: false } && !(onPlayer && kinds.Any(k => k is BehaviorKind.TopDown or BehaviorKind.Pushable or BehaviorKind.Projectile)))
            {
                pw.TClearKeys();
                pw.TSteps(5);
                double x0 = pw.TProbe("Player")!.Value.X;
                pw.TKey("D", true); pw.TSteps(40); pw.TKey("D", false);
                double x1 = pw.TProbe("Player")!.Value.X;
                var p2 = pw.TProbe("Player")!.Value;
                if (!p2.Dead && x1 - x0 < 20 && !(p2.X + 32 >= 1590)) Flag($"player lost control: moved {x1 - x0:0}px holding →");
            }

            foreach (var line in pw.TProblems().Where(l => !l.Contains("NOT owned")).Distinct().Take(3)) Flag("console: " + line); // a hotbar slot not owned yet is the person's setup
        }
        catch (Exception ex) { Flag($"exception: {ex.GetType().Name}: {ex.Message}"); }
        finally { try { pw?.Close(); } catch { } }
        return problems;
    }

    // Flags that are what the person built, not a bug: a solid object that chases the player pins it against itself; a
    // generator on the player fills its own box with copies; a patroller whose generated platforms box it in.
    private static string? ByDesign(BehaviorKind[] kinds, PhysicsMode mode, bool onPlayer, List<string> problems)
    {
        bool solid = mode == PhysicsMode.Static || kinds.Any(k => k is BehaviorKind.Solid or BehaviorKind.Gate or BehaviorKind.Spring or BehaviorKind.Pushable);
        bool chases = kinds.Any(k => k is BehaviorKind.Chase or BehaviorKind.PathChase);
        bool onlyControl = problems.All(p => p.StartsWith("player lost control"));
        if (solid && chases && onlyControl) return "a solid object chasing the player pins it";
        if (kinds.Contains(BehaviorKind.Generator) && (onPlayer || kinds.Contains(BehaviorKind.Patrol))) return "the generated platforms appear inside its own box";
        return null;
    }

    // Small changes so each behavior does something in the test scene (and never reads the player's keys unless on the player).
    private static void PairTune(BehaviorData bh, bool onPlayer)
    {
        switch (bh.Kind)
        {
            case BehaviorKind.Platformer or BehaviorKind.TopDown: if (!onPlayer) bh.Nums["controls"] = 0; break;
            case BehaviorKind.AdvancedPatrol: bh.Texts["path"] = "[{\"x\":520,\"y\":600},{\"x\":780,\"y\":600}]"; break;
            case BehaviorKind.Shooter: bh.Texts["key"] = ""; bh.Nums["cooldown"] = 0.5; bh.Nums["directions"] = 1; break;
            case BehaviorKind.Spawner: bh.Nums["max"] = 3; bh.Nums["interval"] = 1; break;
            case BehaviorKind.Health: if (!onPlayer) bh.Nums["respawn"] = 0; if (!onPlayer) bh.Texts["var"] = "enemyHP"; break;
            case BehaviorKind.Area: bh.Texts["action"] = "addvar"; bh.Texts["var"] = "entered"; break;
            case BehaviorKind.Interact: bh.Texts["action"] = "addvar"; bh.Texts["var"] = "talked"; break;
            case BehaviorKind.UIButton: bh.Texts["action"] = "addvar"; bh.Texts["var"] = "clicked"; break;
            case BehaviorKind.Generator: bh.Nums["count"] = 3; break;
            case BehaviorKind.Terrain: bh.Nums["trees"] = 0; break; // the test scene has no tree to plant
            case BehaviorKind.Damager: if (onPlayer) bh.Texts["target"] = "Target"; break;
            case BehaviorKind.Checkpoint or BehaviorKind.Collectible or BehaviorKind.AbilityPickup or BehaviorKind.Chase or BehaviorKind.PathChase:
                break; // their default target is the Player
        }
    }
}
