using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace EasyEngine;

// Big headless test battery: exercises every Custom-script action and event, the
// "Only if" guard + expressions, several complex multi-rule programs, the Scratch
// block editor's round-trip fidelity (parse→serialize is stable AND preserves
// behavior when re-run), plus a full sweep of all the built-in behaviors.
// Run with:  EasyEngine.exe --fulltest   → writes easyengine_fulltest.log
public partial class App
{
    private void RunFullTest()
    {
        var log = Path.Combine(Path.GetTempPath(), "easyengine_fulltest.log");
        var sb = new StringBuilder();
        int pass = 0, fail = 0;
        var sims = new List<PlayWindow>();

        void Test(string name, Func<bool> f)
        {
            bool ok;
            try { ok = f(); }
            catch (Exception ex) { sb.AppendLine("FAIL  " + name + "  [EX: " + ex.GetType().Name + ": " + Trunc(ex.Message) + "]"); fail++; return; }
            if (ok) pass++; else fail++;
            sb.AppendLine((ok ? "PASS" : "FAIL") + "  " + name);
        }
        string Trunc(string s) => s.Length > 80 ? s.Substring(0, 80) : s;

        PlayWindow Sim(string script, PhysicsMode phys = PhysicsMode.None, Action<Project, Scene>? extra = null)
        {
            var pw = PlayWindow.NewScriptSim(script, out _, phys: phys, extra: extra);
            sims.Add(pw);
            return pw;
        }

        // round-trip a rule JSON through the Scratch block editor
        var rtProject = ProjectIO.CreateNew("RT", Path.Combine(Path.GetTempPath(), "EE_Full_RT_" + Guid.NewGuid().ToString("N").Substring(0, 6)));
        string RT(string js) => new BlockEditorWindow(rtProject, rtProject.GetStartScene(), js).SerializeTest();

        try
        {
            // ========================= A) EVERY "DO" ACTION =========================
            Test("do velocity sets VX/VY", () => { var p = Sim("[{\"when\":\"always\",\"do\":\"velocity\",\"vx\":120,\"vy\":-60}]"); p.TSteps(3); return Math.Abs(p.TVX("A") - 120) < 1 && Math.Abs(p.TVY("A") + 60) < 1; });
            Test("do move shifts position", () => { var p = Sim("[{\"when\":\"always\",\"do\":\"move\",\"dx\":600,\"dy\":0}]"); double x0 = p.TX("A"); p.TSteps(10); return p.TX("A") - x0 > 50; });
            Test("do accelerate builds up speed", () => { var p = Sim("[{\"when\":\"always\",\"do\":\"accelerate\",\"ax\":600,\"ay\":0}]"); p.TSteps(10); return p.TVX("A") > 50; });
            Test("do rotate spins", () => { var p = Sim("[{\"when\":\"always\",\"do\":\"rotate\",\"speed\":180}]"); p.TSteps(10); return p.TRot("A") > 10; });
            Test("do setvar sets a global", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"setvar\",\"var\":\"score\",\"to\":42}]"); p.TSteps(2); return Math.Abs(p.TVar("score") - 42) < 0.01; });
            Test("do addvar accumulates", () => { var p = Sim("[{\"when\":\"timer\",\"every\":0.05,\"do\":\"addvar\",\"var\":\"c\",\"amount\":1}]"); p.TSteps(40); return p.TVar("c") >= 3; });
            Test("do setlocal/addlocal on self", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"setlocal\",\"var\":\"hp\",\"to\":3},{\"when\":\"timer\",\"every\":0.05,\"do\":\"addlocal\",\"var\":\"hp\",\"amount\":1}]"); p.TSteps(20); return p.TLocal("A", "hp") >= 4; });
            Test("do set property (x)", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"set\",\"target\":\"self\",\"prop\":\"x\",\"to\":777}]"); p.TSteps(2); return Math.Abs(p.TX("A") - 777) < 0.01; });
            Test("do additem/removeitem", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"additem\",\"item\":\"key\",\"amount\":3}]"); p.TSteps(2); return Math.Abs(p.TItem("key") - 3) < 0.01; });
            Test("do boost multiplies speed scale", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"boost\",\"factor\":2.5}]"); p.TSteps(2); return Math.Abs(p.TSpeed("A") - 2.5) < 0.01; });
            Test("do setspeed sets speed scale", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"setspeed\",\"factor\":0.5}]"); p.TSteps(2); return Math.Abs(p.TSpeed("A") - 0.5) < 0.01; });
            Test("do destroy kills self", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"destroy\"}]"); p.TSteps(2); return p.TDead("A"); });
            Test("do say opens a dialog", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"say\",\"text\":\"hello there\"}]"); p.TSteps(2); return p.TDialog; });
            Test("do emit spawns particles", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"emit\",\"count\":24}]"); p.TSteps(2); return p.TParticles >= 20; });
            Test("do tween registers + moves", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"tween\",\"prop\":\"x\",\"to\":1000,\"duration\":1}]"); p.TSteps(30); return p.TX("A") > 300; });
            Test("do setvolume sets channel var", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"setvolume\",\"channel\":\"music\",\"to\":0.3}]"); p.TSteps(2); return Math.Abs(p.TVar("vol.music") - 0.3) < 0.01; });
            Test("do shoot spawns a bullet", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"shoot\",\"directions\":1,\"speed\":300}]"); p.TSteps(2); return p.TExists("bullet"); });
            Test("do spawn creates a prefab clone", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"spawn\",\"object\":\"Enemy\"}]", extra: AddEnemyPrefab); p.TSteps(2); return p.TAlive("Enemy") >= 1; });
            Test("do goscene switches scene", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"goscene\",\"scene\":\"S2\"}]", extra: AddScene2); p.TSteps(3); return p.TScene == "S2" && p.TExists("Won"); });
            Test("do jump (grounded platformer)", () => { var p = Sim("[{\"when\":\"keypress\",\"key\":\"Space\",\"do\":\"jump\",\"force\":600}]", extra: MakePlatformerOnFloor); p.TSteps(30); bool g = p.TGrounded("A"); p.TKey("Space", true); p.TStep(); return g && p.TVY("A") < -100; });

            // ========================= B) EVERY "WHEN" EVENT =========================
            Test("when start fires exactly once", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"addvar\",\"var\":\"n\",\"amount\":1}]"); p.TSteps(10); return Math.Abs(p.TVar("n") - 1) < 0.01; });
            Test("when always fires every frame", () => { var p = Sim("[{\"when\":\"always\",\"do\":\"addvar\",\"var\":\"n\",\"amount\":1}]"); p.TSteps(5); return p.TVar("n") >= 5; });
            Test("when key (held) fires while down", () => { var p = Sim("[{\"when\":\"key\",\"key\":\"D\",\"do\":\"addvar\",\"var\":\"n\",\"amount\":1}]"); p.TKey("D", true); p.TSteps(5); return p.TVar("n") >= 5; });
            Test("when keypress fires once per press (cooldown)", () => { var p = Sim("[{\"when\":\"keypress\",\"key\":\"F\",\"cooldown\":10,\"do\":\"addvar\",\"var\":\"n\",\"amount\":1}]"); p.TKey("F", true); p.TSteps(20); return Math.Abs(p.TVar("n") - 1) < 0.01; });
            Test("when timer repeats on interval", () => { var p = Sim("[{\"when\":\"timer\",\"every\":0.1,\"do\":\"addvar\",\"var\":\"n\",\"amount\":1}]"); p.TSteps(100); double n = p.TVar("n"); return n >= 10 && n <= 20; });
            Test("when touch fires on contact (once)", () => { var p = Sim("[{\"when\":\"touch\",\"name\":\"B\",\"do\":\"addvar\",\"var\":\"n\",\"amount\":1}]", extra: AddTouchTarget); p.TSteps(5); return Math.Abs(p.TVar("n") - 1) < 0.01; });
            Test("when ifvar fires while condition holds", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"setvar\",\"var\":\"g\",\"to\":5},{\"when\":\"ifvar\",\"var\":\"g\",\"op\":\">=\",\"value\":5,\"do\":\"setvar\",\"var\":\"ok\",\"to\":1}]"); p.TSteps(3); return p.TVar("ok") >= 1; });
            Test("when iflocal fires on own var", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"setlocal\",\"var\":\"lv\",\"to\":2},{\"when\":\"iflocal\",\"var\":\"lv\",\"op\":\">=\",\"value\":2,\"do\":\"setvar\",\"var\":\"ok\",\"to\":1}]"); p.TSteps(3); return p.TVar("ok") >= 1; });
            Test("when hasitem fires when item owned", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"additem\",\"item\":\"sword\",\"amount\":1},{\"when\":\"hasitem\",\"item\":\"sword\",\"do\":\"setvar\",\"var\":\"armed\",\"to\":1}]"); p.TSteps(3); return p.TVar("armed") >= 1; });
            Test("when expr fires when expression truthy", () => { var p = Sim("[{\"when\":\"expr\",\"expr\":\"self.x > 100\",\"do\":\"setvar\",\"var\":\"ok\",\"to\":1}]"); p.TSteps(2); return p.TVar("ok") >= 1; });
            Test("when click fires on click over it", () => { var p = Sim("[{\"when\":\"click\",\"do\":\"addvar\",\"var\":\"n\",\"amount\":1}]"); p.TMouseAt(232, 232); p.TMouseDown(true); p.TStep(); return p.TVar("n") >= 1; });
            Test("when rightclick fires on RMB over it", () => { var p = Sim("[{\"when\":\"rightclick\",\"do\":\"addvar\",\"var\":\"n\",\"amount\":1}]"); p.TMouseAt(232, 232); p.TRightDown(true); p.TStep(); return p.TVar("n") >= 1; });
            Test("when mouseover fires while hovering", () => { var p = Sim("[{\"when\":\"mouseover\",\"do\":\"addvar\",\"var\":\"n\",\"amount\":1}]"); p.TMouseAt(232, 232); p.TSteps(3); return p.TVar("n") >= 3; });
            Test("when mousedown fires while pressed on it", () => { var p = Sim("[{\"when\":\"mousedown\",\"do\":\"addvar\",\"var\":\"n\",\"amount\":1}]"); p.TMouseAt(232, 232); p.TMouseDown(true); p.TSteps(3); return p.TVar("n") >= 3; });
            Test("when mousepress fires on click anywhere", () => { var p = Sim("[{\"when\":\"mousepress\",\"do\":\"addvar\",\"var\":\"n\",\"amount\":1}]"); p.TMouseAt(5, 5); p.TMouseDown(true); p.TStep(); return p.TVar("n") >= 1; });
            Test("when death fires when it dies", () => { var p = Sim("[{\"when\":\"death\",\"do\":\"setvar\",\"var\":\"died\",\"to\":1}]", extra: MakeKillable); p.TSteps(5); return p.TVar("died") >= 1 && p.TDead("A"); });

            // ========================= C) GUARDS + EXPRESSIONS =========================
            Test("cond guard stops action when false", () => { var p = Sim("[{\"when\":\"always\",\"do\":\"addvar\",\"var\":\"c\",\"amount\":1,\"cond\":\"c < 3\"}]"); p.TSteps(20); return Math.Abs(p.TVar("c") - 3) < 0.01; });
            Test("legacy if/ifop/ifval guard honored", () => { var p = Sim("[{\"when\":\"always\",\"do\":\"addvar\",\"var\":\"c\",\"amount\":1,\"if\":\"c\",\"ifop\":\"<\",\"ifval\":2}]"); p.TSteps(20); return Math.Abs(p.TVar("c") - 2) < 0.01; });
            Test("expr value: setvar to an expression", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"setvar\",\"var\":\"x\",\"expr\":\"10 * 4 + 2\"}]"); p.TSteps(2); return Math.Abs(p.TVar("x") - 42) < 0.01; });
            Test("expr with functions (min/max/abs)", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"setvar\",\"var\":\"x\",\"expr\":\"max(3, min(100, abs(-50)))\"}]"); p.TSteps(2); return Math.Abs(p.TVar("x") - 50) < 0.01; });
            Test("expr reads object props (self.x)", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"setvar\",\"var\":\"x\",\"expr\":\"self.x + 100\"}]"); p.TSteps(2); return Math.Abs(p.TVar("x") - 300) < 0.01; });
            Test("expr reads a variable in a condition", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"setvar\",\"var\":\"g\",\"to\":7},{\"when\":\"always\",\"do\":\"setvar\",\"var\":\"ok\",\"to\":1,\"cond\":\"g == 7\"}]"); p.TSteps(3); return p.TVar("ok") >= 1; });

            // ========================= D) COMPLEX MULTI-RULE PROGRAMS =========================
            // D1: coin collector that unlocks a gate variable at 5 coins
            Test("combo: coins accumulate then unlock at 5", () =>
            {
                var p = Sim("[{\"when\":\"timer\",\"every\":0.05,\"do\":\"addvar\",\"var\":\"coins\",\"amount\":1}," +
                            "{\"when\":\"ifvar\",\"var\":\"coins\",\"op\":\">=\",\"value\":5,\"do\":\"setvar\",\"var\":\"unlocked\",\"to\":1}]");
                p.TSteps(60); return p.TVar("coins") >= 5 && p.TVar("unlocked") >= 1;
            });
            // D2: ammo-limited shooter — shoots + decrements ammo only while ammo > 0
            Test("combo: ammo-limited shooter stops at 0", () =>
            {
                var p = Sim("[{\"when\":\"keypress\",\"key\":\"Space\",\"cooldown\":0.1,\"do\":\"addvar\",\"var\":\"ammo\",\"amount\":-1,\"cond\":\"ammo > 0\"}]");
                p.TSetVar("ammo", 3); p.TKey("Space", true);
                p.TSteps(120); return Math.Abs(p.TVar("ammo")) < 0.01;
            });
            // D3: two-state toggle machine using a local flag
            Test("combo: local state machine toggles", () =>
            {
                var p = Sim("[{\"when\":\"keypress\",\"key\":\"T\",\"cooldown\":0.1,\"do\":\"setlocal\",\"var\":\"on\",\"expr\":\"1 - local.on\"}]");
                p.TKey("T", true); p.TSteps(1); double a = p.TLocal("A", "on");
                p.TKey("T", false); p.TSteps(10); p.TKey("T", true); p.TSteps(1); double b = p.TLocal("A", "on");
                return a > 0.5 && b < 0.5;
            });
            // D4: chase the mouse with expression-driven velocity
            Test("combo: follows the mouse via expressions", () =>
            {
                var p = Sim("[{\"when\":\"always\",\"do\":\"set\",\"target\":\"self\",\"prop\":\"vx\",\"expr\":\"(mousex - self.cx) * 3\"}]");
                p.TMouseAt(1000, 232); p.TSteps(3); return p.TVX("A") > 50;
            });
            // D5: health bar var mirrors a counter and a win fires
            Test("combo: damage down to 0 fires a win flag", () =>
            {
                var p = Sim("[{\"when\":\"timer\",\"every\":0.05,\"do\":\"addvar\",\"var\":\"hp\",\"amount\":-1}," +
                            "{\"when\":\"ifvar\",\"var\":\"hp\",\"op\":\"<=\",\"value\":0,\"do\":\"setvar\",\"var\":\"dead\",\"to\":1}]");
                p.TSetVar("hp", 3); p.TSteps(60); return p.TVar("dead") >= 1;
            });

            // ========================= E) SCRATCH ROUND-TRIP FIDELITY =========================
            // E1: parsing→serializing is stable (idempotent) for a variety of scripts
            string[] battery =
            {
                "[{\"when\":\"touch\",\"name\":\"Player\",\"do\":\"addvar\",\"var\":\"coins\",\"amount\":1}]",
                "[{\"when\":\"keypress\",\"key\":\"Space\",\"cooldown\":0.3,\"do\":\"jump\",\"force\":650}]",
                "[{\"when\":\"start\",\"do\":\"emit\",\"count\":20,\"color\":\"#FF0000\"}]",
                "[{\"when\":\"click\",\"do\":\"tween\",\"prop\":\"y\",\"to\":400,\"duration\":0.5,\"ease\":\"easeout\"}]",
                "[{\"when\":\"always\",\"do\":\"setvar\",\"var\":\"x\",\"expr\":\"coins - 15\",\"cond\":\"coins >= 15\"}]",
                "[{\"when\":\"mousedown\",\"do\":\"setvolume\",\"channel\":\"music\",\"to\":0.5}]",
            };
            for (int i = 0; i < battery.Length; i++)
            {
                string once = RT(battery[i]);
                Test($"round-trip stable #{i + 1}", () => RT(once) == once);
            }

            // E2: round-tripping does not change BEHAVIOR (re-run original vs round-tripped → same outcome)
            Test("round-trip preserves addvar behavior", () => SameOutcome(
                "[{\"when\":\"timer\",\"every\":0.05,\"do\":\"addvar\",\"var\":\"c\",\"amount\":2}]", 40, p => p.TVar("c"), RT));
            Test("round-trip preserves expr-value behavior", () => SameOutcome(
                "[{\"when\":\"start\",\"do\":\"setvar\",\"var\":\"x\",\"expr\":\"3 * 7 + 1\"}]", 2, p => p.TVar("x"), RT));
            Test("round-trip preserves cond guard", () => SameOutcome(
                "[{\"when\":\"always\",\"do\":\"addvar\",\"var\":\"c\",\"amount\":1,\"cond\":\"c < 4\"}]", 20, p => p.TVar("c"), RT));
            Test("round-trip preserves emit behavior", () => SameOutcome(
                "[{\"when\":\"start\",\"do\":\"emit\",\"count\":18}]", 2, p => p.TParticles, RT));
            Test("round-trip preserves tween behavior", () => SameOutcome(
                "[{\"when\":\"start\",\"do\":\"tween\",\"prop\":\"x\",\"to\":900,\"duration\":1}]", 20, p => Math.Round(p.TX("A")), RT));
            Test("round-trip preserves setvolume behavior", () => SameOutcome(
                "[{\"when\":\"start\",\"do\":\"setvolume\",\"channel\":\"sfx\",\"to\":0.25}]", 2, p => p.TVar("vol.sfx"), RT));
            Test("round-trip preserves legacy if→cond behavior", () => SameOutcome(
                "[{\"when\":\"always\",\"do\":\"addvar\",\"var\":\"c\",\"amount\":1,\"if\":\"c\",\"ifop\":\"<\",\"ifval\":3}]", 20, p => p.TVar("c"), RT));

            // ========================= G) EXTREME: EDITOR COLLISIONS + HARD EXPRESSIONS =========================
            // the Scratch editor must keep a trigger param and an action param that share a name
            Test("editor: ifvar+setvar (var collision) works after round-trip", () =>
            {
                var orig = "[{\"when\":\"start\",\"do\":\"setvar\",\"var\":\"g\",\"to\":5}," +
                           "{\"when\":\"ifvar\",\"var\":\"g\",\"op\":\">=\",\"value\":5,\"do\":\"setvar\",\"var\":\"ok\",\"to\":1}]";
                var p = Sim(RT(orig)); p.TSteps(3); return p.TVar("ok") >= 1;
            });
            Test("editor: iflocal+setlocal (var collision) works after round-trip", () =>
            {
                var orig = "[{\"when\":\"start\",\"do\":\"setlocal\",\"var\":\"a\",\"to\":3}," +
                           "{\"when\":\"iflocal\",\"var\":\"a\",\"op\":\">=\",\"value\":3,\"do\":\"setlocal\",\"var\":\"b\",\"to\":9}]";
                var p = Sim(RT(orig)); p.TSteps(3); return Math.Abs(p.TLocal("A", "b") - 9) < 0.01;
            });
            Test("editor: hasitem+additem (item collision) works after round-trip", () =>
            {
                var orig = "[{\"when\":\"start\",\"do\":\"additem\",\"item\":\"coin\",\"amount\":1}," +
                           "{\"when\":\"hasitem\",\"item\":\"coin\",\"do\":\"additem\",\"item\":\"gem\",\"amount\":1}]";
                var p = Sim(RT(orig)); p.TSteps(2); return p.TItem("gem") >= 1;
            });
            Test("editor: expr-event + expr-value both survive round-trip", () =>
            {
                var p = Sim(RT("[{\"when\":\"expr\",\"expr\":\"self.x > 100\",\"do\":\"setvar\",\"var\":\"y\",\"expr\":\"self.x * 2\"}]"));
                p.TSteps(2); return Math.Abs(p.TVar("y") - 400) < 0.01;
            });
            // hard expressions
            Test("expr operator precedence (2+3*4=14)", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"setvar\",\"var\":\"x\",\"expr\":\"2 + 3 * 4\"}]"); p.TSteps(2); return Math.Abs(p.TVar("x") - 14) < 0.01; });
            Test("expr parentheses ((2+3)*4=20)", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"setvar\",\"var\":\"x\",\"expr\":\"(2 + 3) * 4\"}]"); p.TSteps(2); return Math.Abs(p.TVar("x") - 20) < 0.01; });
            Test("expr logical && in a cond", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"setvar\",\"var\":\"g\",\"to\":5},{\"when\":\"always\",\"do\":\"setvar\",\"var\":\"ok\",\"to\":1,\"cond\":\"g > 0 && g < 10\"}]"); p.TSteps(3); return p.TVar("ok") >= 1; });
            Test("expr functions dist()/clamp()", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"setvar\",\"var\":\"d\",\"expr\":\"dist(0,0,3,4) + clamp(50,0,6)\"}]"); p.TSteps(2); return Math.Abs(p.TVar("d") - 11) < 0.01; });
            Test("cross-object expr: A reads B.x", () =>
            {
                var p = Sim("[{\"when\":\"always\",\"do\":\"set\",\"target\":\"self\",\"prop\":\"vx\",\"expr\":\"(B.x - self.cx)\"}]",
                    extra: (pr, sc) => { var l = sc.Layers.First(x => x.Kind == LayerKind.Base); l.Objects.Add(new GameObjectData { Id = 2, Name = "B", Type = "Object", X = 2000, Y = 200, Width = 40, Height = 40, Color = "#0AF", Physics = new PhysicsData { Mode = PhysicsMode.None } }); });
                p.TSteps(3); return p.TVX("A") > 100;
            });
            Test("rules fire in order within a frame (chained)", () => { var p = Sim("[{\"when\":\"always\",\"do\":\"setvar\",\"var\":\"a\",\"to\":1},{\"when\":\"always\",\"do\":\"setvar\",\"var\":\"b\",\"to\":1,\"cond\":\"a == 1\"}]"); p.TStep(); return p.TVar("b") >= 1; });
            Test("tween yoyo returns toward start", () => { var p = Sim("[{\"when\":\"start\",\"do\":\"tween\",\"prop\":\"x\",\"to\":1000,\"duration\":0.2,\"yoyo\":1}]"); p.TSteps(30); return p.TTweens >= 1 && p.TX("A") < 1000; });
            Test("negative speed moves left", () => { var p = Sim("[{\"when\":\"always\",\"do\":\"velocity\",\"vx\":-250}]"); double x0 = p.TX("A"); p.TSteps(5); return p.TX("A") < x0; });

            // ========================= H) BREAKABLE TILEMAPS =========================
            Test("breakable: 'break tile' (touching) removes a tile", () =>
            {
                var p = Sim("[{\"when\":\"start\",\"do\":\"breaktile\",\"where\":\"touching\"}]", extra: BreakMap(200, 200, 3, 3));
                p.TSteps(2); return p.TTiles("Map") == 8;
            });
            Test("breakable: 'break tile' (below) removes the tile under feet", () =>
            {
                var p = Sim("[{\"when\":\"start\",\"do\":\"breaktile\",\"where\":\"below\"}]", extra: BreakMap(200, 264, 3, 2));
                p.TSteps(2); return p.TTiles("Map") == 5;
            });
            Test("breakable: 'break tile' (atmouse) removes the pointed tile", () =>
            {
                var p = Sim("[{\"when\":\"start\",\"do\":\"breaktile\",\"where\":\"atmouse\"}]", extra: BreakMap(600, 600, 3, 3));
                p.TMouseAt(650, 650); p.TSteps(2); return p.TTiles("Map") == 8;
            });
            Test("breakable: a bullet breaks a tile and is consumed", () =>
            {
                var p = Sim("[{\"when\":\"start\",\"do\":\"shoot\",\"directions\":1,\"speed\":0}]", extra: BreakMap(200, 200, 3, 3));
                p.TSteps(3); return p.TTiles("Map") < 9 && !p.TExists("bullet");
            });
            Test("breakable: stomping on top breaks the tile", () =>
            {
                var p = Sim("[]", extra: Combine(BreakMap(200, 500, 3, 1, ("byStomp", 1)), AddFaller(210, 300)));
                p.TSteps(120); return p.TTiles("Map") < 3;
            });
            Test("breakable: touching breaks tiles when byTouch is on", () =>
            {
                var p = Sim("[]", extra: Combine(BreakMap(200, 200, 3, 3, ("byTouch", 1)), AddFaller(205, 205)));
                p.TSteps(3); return p.TTiles("Map") < 9;
            });
            Test("non-breakable tilemap keeps no per-tile state", () =>
            {
                var p = Sim("[{\"when\":\"start\",\"do\":\"breaktile\",\"where\":\"touching\"}]", extra: BreakMap(200, 200, 3, 3, ("breakable", 0)));
                p.TSteps(2); return p.TTiles("Map") == -1; // no crash, nothing tracked
            });
            Test("breakable: byBullet off ignores bullets", () =>
            {
                var p = Sim("[{\"when\":\"start\",\"do\":\"shoot\",\"directions\":1,\"speed\":0}]", extra: BreakMap(200, 200, 3, 3, ("byBullet", 0)));
                p.TSteps(3); return p.TTiles("Map") == 9;
            });
            Test("editor: 'break tile' block round-trips", () => RT(RT("[{\"when\":\"click\",\"do\":\"breaktile\",\"where\":\"below\"}]")) == RT("[{\"when\":\"click\",\"do\":\"breaktile\",\"where\":\"below\"}]"));

            // ========================= F) FULL BEHAVIOR SWEEP =========================
            var (bp, bs) = BuildBehaviorScene();
            var bplay = new PlayWindow(bp, bs); sims.Add(bplay);
            foreach (var r in bplay.RunBehaviorChecks()) { if (r.StartsWith("PASS")) pass++; else fail++; sb.AppendLine(r); }
        }
        catch (Exception ex) { sb.AppendLine("FATAL: " + ex); fail++; }

        foreach (var s in sims) { try { s.Close(); } catch { } }

        // clean up the throwaway project folders this run created
        try
        {
            foreach (var pat in new[] { "EE_Full*", "EE_BehTest_*" })
                foreach (var dir in Directory.GetDirectories(Path.GetTempPath(), pat))
                    try { Directory.Delete(dir, true); } catch { }
        }
        catch { }

        sb.Insert(0, $"fulltest {DateTime.Now}\n");
        sb.AppendLine($"\n{pass}/{pass + fail} passed" + (fail == 0 ? "  — ALL PASS" : $"  — {fail} FAILED"));
        try { File.WriteAllText(log, sb.ToString()); } catch { }
        Shutdown();
    }

    // Re-run a script and its block-editor round-trip; the measured outcome must match.
    private static bool SameOutcome(string script, int steps, Func<PlayWindow, double> read, Func<string, string> rt)
    {
        double a = OneOutcome(script, steps, read);
        double b = OneOutcome(rt(script), steps, read);
        return Math.Abs(a - b) < 0.01;
    }
    private static double OneOutcome(string script, int steps, Func<PlayWindow, double> read)
    {
        var pw = PlayWindow.NewScriptSim(script, out _);
        pw.TSteps(steps);
        double v = read(pw);
        try { pw.Close(); } catch { }
        return v;
    }

    // ---- breakable-tilemap scene setups ----
    private static Action<Project, Scene> BreakMap(double x, double y, int cols, int rows, params (string key, double val)[] nums)
    {
        return (p, s) =>
        {
            var l = s.Layers.First(z => z.Kind == LayerKind.Base);
            var cells = new int[cols * rows];
            for (int i = 0; i < cells.Length; i++) cells[i] = 1; // every cell = palette entry 1 (solid)
            string data = "{\"cols\":" + cols + ",\"rows\":" + rows + ",\"cells\":[" + string.Join(",", cells) + "]}";
            var bh = new BehaviorData { Kind = BehaviorKind.Tilemap };
            bh.Texts["palette"] = "[{\"color\":\"#888888\",\"solid\":true}]";
            bh.Texts["data"] = data;
            bh.Nums["tileSize"] = 32; bh.Nums["breakable"] = 1;
            foreach (var (k, v) in nums) bh.Nums[k] = v;
            l.Objects.Add(new GameObjectData { Id = 90, Name = "Map", Type = "Object", X = x, Y = y, Width = cols * 32, Height = rows * 32, Color = "#000000", Physics = new PhysicsData { Mode = PhysicsMode.None }, Behaviors = { bh } });
        };
    }
    private static Action<Project, Scene> AddFaller(double x, double y)
    {
        return (p, s) =>
        {
            var l = s.Layers.First(z => z.Kind == LayerKind.Base);
            l.Objects.Add(new GameObjectData { Id = 91, Name = "Faller", Type = "Object", X = x, Y = y, Width = 20, Height = 20, Color = "#EE4444", Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } });
        };
    }
    private static Action<Project, Scene> Combine(params Action<Project, Scene>[] setups) =>
        (p, s) => { foreach (var a in setups) a(p, s); };

    // ---- scene setups for specific tests ----
    private static void AddEnemyPrefab(Project p, Scene s) =>
        p.Prefabs.Add(new PrefabDef { Name = "Enemy", Template = new GameObjectData { Id = 998, Name = "Enemy", Type = "Object", Width = 20, Height = 20, Color = "#FF5555" } });

    private static void AddScene2(Project p, Scene s)
    {
        var sc2 = new Scene { Name = "S2", Width = 800, Height = 600 };
        sc2.Layers.Add(new Layer { Name = "Base", Kind = LayerKind.Base });
        sc2.Layers[0].Objects.Add(new GameObjectData { Id = 50, Name = "Won", Type = "Object", X = 10, Y = 10, Width = 20, Height = 20, Color = "#22DD88", Physics = new PhysicsData { Mode = PhysicsMode.None } });
        p.Scenes.Add(sc2);
    }

    private static void AddTouchTarget(Project p, Scene s)
    {
        var layer = s.Layers.First(l => l.Kind == LayerKind.Base);
        layer.Objects.Add(new GameObjectData { Id = 2, Name = "B", Type = "Object", X = 210, Y = 210, Width = 40, Height = 40, Color = "#88AAFF", Physics = new PhysicsData { Mode = PhysicsMode.None } });
    }

    private static void MakePlatformerOnFloor(Project p, Scene s)
    {
        var layer = s.Layers.First(l => l.Kind == LayerKind.Base);
        var a = layer.Objects.First(o => o.Name == "A");
        a.Physics.Mode = PhysicsMode.Dynamic; a.Y = 200;
        a.Behaviors.Insert(0, new BehaviorData { Kind = BehaviorKind.Platformer, Nums = { ["controls"] = 0 } });
        layer.Objects.Add(new GameObjectData { Id = 2, Name = "Floor", Type = "Object", X = 100, Y = 300, Width = 500, Height = 40, Color = "#555555", Physics = new PhysicsData { Mode = PhysicsMode.Static }, Behaviors = { new BehaviorData { Kind = BehaviorKind.Solid } } });
    }

    private static void MakeKillable(Project p, Scene s)
    {
        var layer = s.Layers.First(l => l.Kind == LayerKind.Base);
        var a = layer.Objects.First(o => o.Name == "A");
        a.Behaviors.Insert(0, new BehaviorData { Kind = BehaviorKind.Health, Nums = { ["hp"] = 1, ["max"] = 1, ["respawn"] = 0 }, Texts = { ["var"] = "ahp" } });
        layer.Objects.Add(new GameObjectData { Id = 2, Name = "Spike", Type = "Object", X = 205, Y = 205, Width = 40, Height = 40, Color = "#CC3333", Physics = new PhysicsData { Mode = PhysicsMode.None }, Behaviors = { new BehaviorData { Kind = BehaviorKind.Damager, Nums = { ["damage"] = 5 }, Texts = { ["target"] = "A" } } } });
    }

    // The behavior test scene (shared with --behtest).
    private static (Project project, string sceneName) BuildBehaviorScene()
    {
        var temp = Path.Combine(Path.GetTempPath(), "EE_BehTest_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(temp);
        var project = ProjectIO.CreateNew("BehTest", temp);
        var scene = project.GetStartScene();
        scene.Width = 9000; scene.Height = 3000;
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

        var styled = O(11, "StyledBox", 8000, 0, 50, 50, PhysicsMode.None);
        styled.Opacity = 0.5; styled.CornerRadius = 16; styled.BorderColor = "#00E5FF"; styled.BorderWidth = 3;
        layer.Objects.Add(styled);
        layer.Objects.Add(O(12, "Title", 8100, 0, 200, 50, PhysicsMode.None,
            B(BehaviorKind.Label, new[] { ("effectSpeed", 30.0) }, new[] { ("text", "HELLO WORLD"), ("effect", "typewriter") })));
        project.Theme.DialogCorner = 14; project.Theme.DialogEffect = "typewriter"; project.Theme.DialogBg = "#223344";

        layer.Objects.Add(O(13, "Chaser", 200, 1500, 24, 24, PhysicsMode.None, B(BehaviorKind.PathChase, new[] { ("speed", 120.0), ("cell", 20.0) }, new[] { ("target", "Player") })));
        layer.Objects.Add(O(14, "PWall", 380, 1400, 40, 400, PhysicsMode.Static, B(BehaviorKind.Solid)));
        layer.Objects.Add(O(15, "SpringPad", 500, 1500, 40, 20, PhysicsMode.None, B(BehaviorKind.Spring, new[] { ("force", 820.0) }, new[] { ("dir", "up") })));
        layer.Objects.Add(O(16, "Faller", 500, 1485, 24, 24, PhysicsMode.Dynamic));
        var ball = O(17, "Ball", 700, 1500, 20, 20, PhysicsMode.Dynamic);
        ball.Physics.Bounciness = 0.7; layer.Objects.Add(ball);
        layer.Objects.Add(O(18, "BFloor", 680, 1560, 80, 20, PhysicsMode.Static, B(BehaviorKind.Solid)));
        layer.Objects.Add(O(19, "Map", 50, 50, 10, 10, PhysicsMode.None, B(BehaviorKind.Minimap, new[] { ("size", 160.0) }, new[] { ("corner", "tr"), ("player", "Player") })));

        layer.Objects.Add(O(20, "Boom", 1000, 2500, 20, 20, PhysicsMode.None, B(BehaviorKind.Particles, new[] { ("burst", 10.0), ("life", 0.8) }, new[] { ("on", "start") })));
        layer.Objects.Add(O(21, "Tweeny", 1200, 2500, 30, 30, PhysicsMode.None));
        layer.Objects.Add(O(22, "MenuBtn", 1300, 2500, 120, 40, PhysicsMode.None, B(BehaviorKind.UIButton, new[] { ("to", 1.0) }, new[] { ("action", "setvar"), ("var", "clicked") })));
        layer.Objects.Add(O(23, "HealthBar", 1500, 2500, 100, 16, PhysicsMode.None, B(BehaviorKind.Bar, new[] { ("max", 4.0) }, new[] { ("var", "hp") })));

        layer.Objects.Add(O(24, "Walker", 2000, 2700, 40, 40, PhysicsMode.Dynamic,
            B(BehaviorKind.Patrol, new[] { ("speed", 80.0), ("distance", 200.0) }),
            B(BehaviorKind.Platformer, new[] { ("controls", 0.0) })));
        layer.Objects.Add(O(25, "VWalker", 2300, 2700, 40, 40, PhysicsMode.None,
            B(BehaviorKind.Patrol, new[] { ("speed", 80.0), ("distance", 150.0), ("axis", 1.0) })));
        layer.Objects.Add(O(26, "SmoothWalker", 2600, 2700, 40, 40, PhysicsMode.None,
            B(BehaviorKind.Patrol, new[] { ("speed", 80.0), ("distance", 150.0), ("smooth", 1.0), ("slowSteps", 3.0), ("pause", 1.0) })));
        layer.Objects.Add(O(27, "PathWalker", 2900, 2700, 40, 40, PhysicsMode.None,
            B(BehaviorKind.AdvancedPatrol, new[] { ("speed", 120.0) }, new[] { ("path", "[{\"x\":3200,\"y\":2720},{\"x\":2900,\"y\":2720}]") })));
        layer.Objects.Add(O(33, "PGhost", 5600, 100, 40, 40, PhysicsMode.Dynamic, B(BehaviorKind.Platformer, new[] { ("collide", 0.0) })));
        layer.Objects.Add(O(28, "PathFinder", 3500, 2700, 40, 40, PhysicsMode.None,
            B(BehaviorKind.AdvancedPatrol, new[] { ("speed", 120.0), ("mode", 1.0), ("pathfind", 1.0) }, new[] { ("path", "[{\"x\":3800,\"y\":2720},{\"x\":3500,\"y\":2720}]") })));
        layer.Objects.Add(O(29, "TDWall", 4200, 2700, 40, 160, PhysicsMode.Static, B(BehaviorKind.Solid)));
        layer.Objects.Add(O(30, "TDGuy", 4100, 2760, 40, 40, PhysicsMode.None, B(BehaviorKind.TopDown, new[] { ("speed", 200.0) })));
        layer.Objects.Add(O(31, "TDGhost", 4100, 2760, 40, 40, PhysicsMode.None, B(BehaviorKind.TopDown, new[] { ("speed", 200.0), ("collide", 0.0) })));
        layer.Objects.Add(O(32, "APTD", 5000, 2760, 40, 40, PhysicsMode.None,
            B(BehaviorKind.AdvancedPatrol, new[] { ("speed", 150.0), ("mode", 1.0) }, new[] { ("path", "[{\"x\":5300,\"y\":2780},{\"x\":5000,\"y\":2780}]") })));

        return (project, scene.Name);
    }
}
