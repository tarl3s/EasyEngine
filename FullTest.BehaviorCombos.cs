using System;
using System.IO;
using System.Linq;

namespace EasyEngine;

// Two (or more) behaviors on one object, and behaviors meeting each other (PlayWindow.Movers.cs): one mover steers at a
// time (Chase beats Patrol while its target is near, Top-Down without keys leaves the speed to a Patrol, a resting mover no
// longer stops the others), chasers settle on their target and walk when they have gravity, a hit's knockback is felt and
// then stops, patrols turn at walls and edges, a pushable block listed before the player, solid hazards / plates / doors
// that count a touch when you stand on or walk into them, checkpoints, health never below 0, an enemy falling off the
// world, bounce pads, "set speed" on patrols and chasers, and the inspector's warnings (BehaviorCombos.cs).
public partial class App
{
    partial void FeatureTestsBehaviorCombos(FullTestKit t)
    {
        const double GroundY = 700;

        static BehaviorData B(BehaviorKind k, params (string key, object v)[] p)
        {
            var b = BehaviorCatalog.Info(k).CreateData();
            foreach (var (key, v) in p)
                if (v is string s) b.Texts[key] = s; else b.Nums[key] = Convert.ToDouble(v);
            return b;
        }
        static GameObjectData O(int id, string name, double x, double y, double w, double h, PhysicsMode m, params BehaviorData[] bs)
        {
            var o = new GameObjectData { Id = id, Name = name, X = x, Y = y, Width = w, Height = h, Color = "#AA4444", Physics = new PhysicsData { Mode = m } };
            o.Behaviors.AddRange(bs);
            return o;
        }
        static GameObjectData Player(int id, double x, double y = GroundY - 48, params BehaviorData[] extra)
        {
            var p = O(id, "Player", x, y, 32, 48, PhysicsMode.Dynamic, B(BehaviorKind.Platformer), B(BehaviorKind.Health, ("max", 3)));
            p.Type = "Player";
            p.Behaviors.AddRange(extra);
            return p;
        }
        static GameObjectData Ground(int id = 1) =>
            O(id, "Ground", 0, GroundY, 1600, 60, PhysicsMode.Static, B(BehaviorKind.Solid));

        // a one-scene project (1600×900) with these objects, in this order, loaded and seeded
        PlayWindow Play(string tag, params GameObjectData[] objects)
        {
            var dir = Path.Combine(Path.GetTempPath(), "EE_Full_Combo_" + tag + "_" + Guid.NewGuid().ToString("N")[..6]);
            var p = ProjectIO.CreateNew("Combo", dir);
            var s = p.GetStartScene();
            s.Width = 1600; s.Height = 900;
            foreach (var l in s.Layers) l.Objects.Clear();
            s.Layers.First(l => l.Kind == LayerKind.Base).Objects.AddRange(objects);
            p.Scenes.Add(new Scene { Name = "S2", Layers = { new Layer { Name = "Base", Kind = LayerKind.Base } } });
            var pw = new PlayWindow(p, s.Name); t.Sims.Add(pw);
            pw.TSeed(1); pw.TLoad();
            return pw;
        }
        // how far an object moves over n ticks (max − min of X) and where it ends
        static (double spread, double endX) Watch(PlayWindow pw, string name, int n)
        {
            double lo = double.MaxValue, hi = double.MinValue;
            for (int i = 0; i < n; i++) { pw.TStep(); double x = pw.TX(name); lo = Math.Min(lo, x); hi = Math.Max(hi, x); }
            return (hi - lo, pw.TX(name));
        }

        // ---------------- chasers
        t.Test("combos: Chase settles on its target instead of shaking around it", () =>
        {
            var pw = Play("ChaseSettle", O(2, "T", 400, 300, 40, 40, PhysicsMode.None),
                          O(3, "C", 100, 300, 40, 40, PhysicsMode.None, B(BehaviorKind.Chase, ("target", "T"), ("speed", 300))));
            pw.TSteps(120);
            var (spread, _) = Watch(pw, "C", 30);
            return spread < 0.5 && Math.Abs(pw.TX("C") - 400) < 1.5 && Math.Abs(pw.TY("C") - 300) < 1.5;
        });
        t.Test("combos: Smart Chase settles on its target too (it used to shake from cell to cell)", () =>
        {
            var pw = Play("PathSettle", O(2, "T", 400, 300, 40, 40, PhysicsMode.None),
                          O(3, "C", 100, 300, 40, 40, PhysicsMode.None, B(BehaviorKind.PathChase, ("target", "T"), ("speed", 200))));
            pw.TSteps(150);
            var (spread, _) = Watch(pw, "C", 30);
            return spread < 0.5 && Math.Abs(pw.TX("C") - 400) < 2;
        });
        t.Test("combos: a Chase with gravity runs after its target on the ground (it used to fly up to it)", () =>
        {
            var pw = Play("ChaseGround", Ground(), O(2, "T", 800, 300, 40, 40, PhysicsMode.None),
                          O(3, "C", 200, GroundY - 40, 40, 40, PhysicsMode.Dynamic, B(BehaviorKind.Chase, ("target", "T"))));
            pw.TSteps(60);
            return pw.TGrounded("C") && Math.Abs(pw.TY("C") - (GroundY - 40)) < 0.5 && pw.TX("C") > 250;
        });
        t.Test("combos: Patrol + Chase 'Only when closer than 300' patrols while the target is far, chases it when near, then patrols again", () =>
        {
            var pw = Play("PatrolChase", O(2, "P", 1500, 500, 40, 40, PhysicsMode.None),
                          O(3, "E", 600, 500, 40, 40, PhysicsMode.None, B(BehaviorKind.Patrol, ("distance", 200), ("speed", 90)),
                            B(BehaviorKind.Chase, ("target", "P"), ("speed", 150), ("range", 300))));
            var (spread, _) = Watch(pw, "E", 150);
            bool patrolled = spread > 100 && pw.TX("E") >= 598 && pw.TX("E") <= 802;
            double x0 = pw.TX("E");
            pw.TPlace("P", x0 - 250, 500);           // the player walks up
            pw.TSteps(30);
            bool chased = pw.TX("E") < x0 - 40 && pw.TVX("E") < -100;
            pw.TPlace("P", 1550, 100);                // and runs away
            pw.TSteps(30);
            return patrolled && chased && Math.Abs(Math.Abs(pw.TVX("E")) - 90) < 0.5;
        });
        t.Test("combos: Chase (no distance limit) beats Patrol whatever the order in the list", () =>
        {
            var pw = Play("ChaseFirst", O(2, "P", 100, 500, 40, 40, PhysicsMode.None),
                          O(3, "E", 600, 500, 40, 40, PhysicsMode.None, B(BehaviorKind.Chase, ("target", "P")), B(BehaviorKind.Patrol)));
            pw.TSteps(60);
            return pw.TX("E") < 600 - 100;
        });

        t.Test("combos: a chaser whose target is gone stands still, but a homing shot (Projectile + Chase) flies on", () =>
        {
            var pw = Play("ChaseGone", O(2, "T", 1200, 300, 40, 40, PhysicsMode.None),
                          O(3, "C", 100, 300, 40, 40, PhysicsMode.None, B(BehaviorKind.Chase, ("target", "T"))),
                          O(4, "Shot", 100, 500, 20, 20, PhysicsMode.None, B(BehaviorKind.Projectile, ("speed", 200), ("life", 0)), B(BehaviorKind.Chase, ("target", "T"), ("speed", 200))));
            pw.TSteps(10);
            pw.TDestroyAll("T");
            pw.TSteps(10);
            double c = pw.TX("C"), s = pw.TX("Shot");
            pw.TSteps(10);
            return Math.Abs(pw.TX("C") - c) < 0.01 && pw.TX("Shot") - s > 20;
        });

        // ---------------- who steers
        t.Test("combos: Top-Down without keys + Patrol: the patrol moves it (Top-Down used to stop it every tick)", () =>
        {
            var pw = Play("TopDownPatrol", O(3, "E", 600, 500, 40, 40, PhysicsMode.None, B(BehaviorKind.Patrol), B(BehaviorKind.TopDown, ("controls", 0))));
            pw.TSteps(60);
            return pw.TX("E") > 630;
        });
        t.Test("combos: a mover resting ('Only while' false) no longer stops the one that is on", () =>
        {
            var chase = B(BehaviorKind.Chase, ("target", "P")); chase.Texts["cond"] = "0";
            var pw = Play("Resting", O(2, "P", 100, 500, 40, 40, PhysicsMode.None),
                          O(3, "E", 600, 500, 40, 40, PhysicsMode.None, B(BehaviorKind.Patrol), chase));
            pw.TSteps(60);
            return pw.TX("E") > 630;
        });
        t.Test("combos: a resting Platformer without Dynamic physics still falls at full speed (it used to float down)", () =>
        {
            var plat = B(BehaviorKind.Platformer, ("controls", 0)); plat.Texts["cond"] = "0";
            var pw = Play("RestFall", Ground(), O(3, "E", 300, 100, 40, 40, PhysicsMode.None, plat));
            pw.TSteps(20);
            return pw.TY("E") > 160;
        });
        t.Test("combos: the player keeps its keys when a Chase on it has nothing to chase", () =>
        {
            var pw = Play("PlayerChase", Ground(), Player(2, 300, GroundY - 48, B(BehaviorKind.Chase, ("target", "Nobody"))));
            pw.TSteps(5);
            pw.TKey("D", true); pw.TSteps(30); pw.TKey("D", false);
            return pw.TX("Player") > 350;
        });

        // ---------------- knockback
        t.Test("combos: a hit pushes a patrolling enemy back, then it stops and patrols again (no floating off without gravity)", () =>
        {
            var pw = Play("KnockPatrol", O(3, "E", 600, 500, 40, 40, PhysicsMode.None, B(BehaviorKind.Patrol, ("speed", 90)), B(BehaviorKind.Health, ("max", 5), ("respawn", 0))));
            pw.TSteps(5);
            double x0 = pw.TX("E");
            pw.THit("E", 1, 300, x0 - 50);           // hit from its left → pushed right
            pw.TSteps(6);
            bool pushed = pw.TX("E") - x0 > 20 && Math.Abs(pw.TY("E") - 500) < 0.5;
            pw.TSteps(60);
            return pushed && Math.Abs(Math.Abs(pw.TVX("E")) - 90) < 0.5 && Math.Abs(pw.TY("E") - 500) < 0.5 && pw.TX("E") < x0 + 200;
        });
        t.Test("combos: a Dynamic enemy with no mover slides back when hit, then stops (it used to slide forever)", () =>
        {
            var pw = Play("KnockSlide", Ground(), O(3, "E", 600, GroundY - 40, 40, 40, PhysicsMode.Dynamic, B(BehaviorKind.Health, ("max", 5), ("respawn", 0))));
            pw.TSteps(10);
            double x0 = pw.TX("E");
            pw.THit("E", 1, 300, x0 - 50);
            pw.TSteps(60);
            double x1 = pw.TX("E");
            pw.TSteps(60);
            return x1 - x0 > 20 && x1 - x0 < 150 && Math.Abs(pw.TX("E") - x1) < 0.5;
        });
        t.Test("combos: the player is really knocked back by a hit (its controls used to cancel the push the next tick)", () =>
        {
            var pw = Play("KnockPlayer", Ground(), Player(2, 600));
            pw.TSteps(10);
            double x0 = pw.TX("Player");
            pw.THit("Player", 1, 320, x0 + 60);       // hit from its right → pushed left
            pw.TSteps(12);
            return x0 - pw.TX("Player") > 25;
        });

        // ---------------- patrols at walls and edges
        t.Test("combos: a patrolling enemy turns around at a wall instead of pushing against it forever", () =>
        {
            var pw = Play("PatrolWall", Ground(), O(2, "Wall", 420, GroundY - 140, 40, 140, PhysicsMode.Static, B(BehaviorKind.Solid)),
                          O(3, "E", 300, GroundY - 40, 40, 40, PhysicsMode.Dynamic, B(BehaviorKind.Patrol, ("distance", 400), ("speed", 120))));
            pw.TSteps(150);
            return pw.TX("E") < 340 && pw.TVX("E") < 0;
        });
        t.Test("combos: Patrol + Stay Inside turns around at the scene's edge (it used to stay stuck there)", () =>
        {
            var pw = Play("PatrolEdge", O(3, "E", 1400, 300, 40, 40, PhysicsMode.None, B(BehaviorKind.Patrol, ("distance", 400), ("speed", 90)), B(BehaviorKind.StayInside)));
            double far = 0;
            for (int i = 0; i < 150; i++) { pw.TStep(); far = Math.Max(far, pw.TX("E")); }
            return far > 1559 && far < 1560.5 && pw.TX("E") < far - 20 && pw.TVX("E") < 0; // reached the edge (1600 − 40), turned back
        });

        // ---------------- touching solid things
        t.Test("combos: a pushable block listed before the player is still pushed", () =>
        {
            var pw = Play("PushFirst", Ground(), O(2, "Box", 400, GroundY - 40, 40, 40, PhysicsMode.Dynamic, B(BehaviorKind.Pushable)), Player(3, 300));
            pw.TKey("D", true); pw.TSteps(90); pw.TKey("D", false);
            return pw.TX("Box") > 450;
        });
        t.Test("combos: solid spikes (Solid + Damager) hurt the player standing on them", () =>
        {
            var pw = Play("SolidSpikes", Ground(), O(2, "Spikes", 250, GroundY - 20, 200, 20, PhysicsMode.Static, B(BehaviorKind.Solid), B(BehaviorKind.Damager)),
                          Player(3, 300, 560));
            pw.TSteps(40);
            return pw.THp("Player") < 3;
        });
        t.Test("combos: a solid pressure plate (Switch, touch) flips when the player stands on it", () =>
        {
            var pw = Play("Plate", Ground(), O(2, "Plate", 250, GroundY - 10, 100, 10, PhysicsMode.Static, B(BehaviorKind.Switch, ("mode", "touch"), ("var", "plate"))),
                          Player(3, 280, 560));
            pw.TSteps(40);
            return pw.TVar("plate") >= 1;
        });
        t.Test("combos: a solid door (Go To Scene + Static) takes the player there when it walks into it", () =>
        {
            var pw = Play("Door", Ground(), O(2, "Door", 400, GroundY - 80, 40, 80, PhysicsMode.Static, B(BehaviorKind.GoToScene, ("scene", "S2"))), Player(3, 300));
            pw.TKey("D", true); pw.TSteps(40);
            return pw.TScene == "S2";
        });
        t.Test("combos: a checkpoint brings the player back standing where it stands, and health never shows below 0", () =>
        {
            var pw = Play("Checkpoint", Ground(), O(2, "Flag", 500, GroundY - 80, 20, 80, PhysicsMode.None, B(BehaviorKind.Checkpoint)), Player(3, 100));
            pw.TSteps(5);
            pw.TPlace("Player", 495, GroundY - 48);
            pw.TSteps(2);                              // touches the flag
            pw.TPlace("Player", 100, GroundY - 48);
            pw.TSteps(2);
            pw.THit("Player", 5, 0, 0);               // a big hit: 3 → -2
            double low = double.MaxValue;
            for (int i = 0; i < 3; i++) { pw.TStep(); low = Math.Min(low, pw.TVar("health")); }
            bool back = Math.Abs(pw.TY("Player") - (GroundY - 48)) < 1 && Math.Abs(pw.TX("Player") - (510 - 16)) < 1;
            pw.TSteps(10);
            return back && low >= 0 && pw.TGrounded("Player") && Math.Abs(pw.TY("Player") - (GroundY - 48)) < 1;
        });
        t.Test("combos: an enemy that falls off the world dies (its death rules run) instead of coming back to its start", () =>
        {
            var e = O(3, "E", 100, 800, 40, 40, PhysicsMode.Dynamic, B(BehaviorKind.Health, ("max", 3), ("respawn", 0)),
                      B(BehaviorKind.Custom, ("script", "[{\"when\":\"death\",\"do\":\"setvar\",\"var\":\"died\",\"to\":1}]")));
            var pw = Play("FallOff", e);
            pw.TSteps(60);
            return pw.TDead("E") && pw.TVar("died") >= 1;
        });
        t.Test("combos: a bounce pad launches what lands on it, not what walks into its side", () =>
        {
            var pw = Play("Spring", Ground(), O(2, "Pad", 500, GroundY - 30, 60, 30, PhysicsMode.None, B(BehaviorKind.Spring)), Player(3, 400));
            pw.TKey("D", true);
            double minVy = 0;
            for (int i = 0; i < 40; i++) { pw.TStep(); minVy = Math.Min(minVy, pw.TVY("Player")); }
            pw.TKey("D", false);
            bool side = minVy > -400 && pw.TX("Player") < 470;
            pw.TPlace("Player", 510, GroundY - 30 - 48);
            pw.TSteps(2);
            return side && pw.TVY("Player") < -500;
        });

        // ---------------- "set speed" reaches every mover
        t.Test("combos: 'set speed' (boost / setspeed) also slows Patrol and Chase", () =>
        {
            const string half = "[{\"when\":\"start\",\"do\":\"setspeed\",\"factor\":0.5}]";
            var pw = Play("SpeedScale", O(2, "P", 1500, 500, 40, 40, PhysicsMode.None),
                          O(3, "Pa", 300, 300, 40, 40, PhysicsMode.None, B(BehaviorKind.Patrol, ("speed", 100)), B(BehaviorKind.Custom, ("script", half))),
                          O(4, "Ch", 300, 500, 40, 40, PhysicsMode.None, B(BehaviorKind.Chase, ("target", "P"), ("speed", 100)), B(BehaviorKind.Custom, ("script", half))));
            pw.TSteps(5);
            return Math.Abs(Math.Abs(pw.TVX("Pa")) - 50) < 0.5 && Math.Abs(pw.TVX("Ch") - 50) < 0.5;
        });

        // ---------------- the inspector's warnings
        t.Test("combos: the behavior card warns only where two behaviors can't work together", () =>
        {
            string? W(GameObjectData o, BehaviorKind k) => BehaviorCombos.Warning(o, o.Behaviors.First(b => b.Kind == k));
            var patrolChase = O(1, "E", 0, 0, 40, 40, PhysicsMode.None, B(BehaviorKind.Patrol), B(BehaviorKind.Chase));
            var ranged = O(2, "E", 0, 0, 40, 40, PhysicsMode.None, B(BehaviorKind.Patrol), B(BehaviorKind.Chase, ("range", 300)));
            var boss = O(3, "Boss", 0, 0, 40, 40, PhysicsMode.None, B(BehaviorKind.BossAI), B(BehaviorKind.Chase));
            var both = O(4, "Hero", 0, 0, 40, 40, PhysicsMode.None, B(BehaviorKind.Platformer), B(BehaviorKind.TopDown));
            var self = O(5, "Bat", 0, 0, 40, 40, PhysicsMode.None, B(BehaviorKind.Chase, ("target", "Bat")));
            var push = O(6, "Box", 0, 0, 40, 40, PhysicsMode.None, B(BehaviorKind.Pushable), B(BehaviorKind.Patrol));
            var plain = O(7, "E", 0, 0, 40, 40, PhysicsMode.Dynamic, B(BehaviorKind.Platformer, ("controls", 0)), B(BehaviorKind.Patrol), B(BehaviorKind.Health), B(BehaviorKind.Damager));
            return W(patrolChase, BehaviorKind.Patrol) != null && W(patrolChase, BehaviorKind.Chase) == null
                   && W(ranged, BehaviorKind.Patrol) == null
                   && W(boss, BehaviorKind.Chase) != null && W(both, BehaviorKind.Platformer) != null
                   && W(self, BehaviorKind.Chase) != null && W(push, BehaviorKind.Pushable) != null
                   && plain.Behaviors.All(b => BehaviorCombos.Warning(plain, b) == null);
        });
        t.Test("combos: Chase and Smart Chase offer 'Only when closer than' (picked from a list, 0 = always)", () =>
            new[] { BehaviorKind.Chase, BehaviorKind.PathChase }.All(k =>
                BehaviorCatalog.Info(k).Nums.FirstOrDefault(n => n.Key == "range") is { Default: 0, Options: { Length: >= 3 } } r && r.Options[0].StartsWith("0|")));
    }
}
