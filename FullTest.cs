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

            // ========================= I) CODE-REVIEW REGRESSIONS =========================
            // custom #1: an omitted trigger param uses its default, NOT an action param of the same name
            Test("trigger 'value' defaults to 0 (not the action's value)", () =>
            {
                var fires = Sim("[{\"when\":\"ifvar\",\"var\":\"hp\",\"op\":\"<=\",\"do\":\"setvar\",\"var\":\"dead\",\"value\":1}]");
                fires.TSetVar("hp", 0); fires.TSteps(2);
                var holds = Sim("[{\"when\":\"ifvar\",\"var\":\"hp\",\"op\":\"<=\",\"do\":\"setvar\",\"var\":\"dead\",\"value\":1}]");
                holds.TSetVar("hp", 1); holds.TSteps(2);
                return fires.TVar("dead") >= 1 && holds.TVar("dead") < 0.5; // hp<=0: fires at 0, not at 1
            });
            // custom #2: an action param written before "do" is still honored
            Test("action param before 'do' is honored", () =>
            {
                var p = Sim("[{\"when\":\"keypress\",\"key\":\"Space\",\"force\":900,\"do\":\"jump\"}]", extra: MakePlatformerOnFloor);
                p.TSteps(30); p.TKey("Space", true); p.TStep();
                return p.TVY("A") < -700; // used 900, not the 600 default
            });
            // touch 'name' + spawn: spawn uses its default, not the touch target's name
            Test("touch name does not leak into spawn", () =>
            {
                var p = Sim("[{\"when\":\"touch\",\"name\":\"B\",\"do\":\"spawn\"}]", extra: Combine(AddTouchTarget, AddEnemyPrefab));
                p.TSteps(3); return p.TAlive("Enemy") >= 1;
            });
            // editor #1: an action param before "do" round-trips to the right value (not the default)
            Test("editor keeps action param written before 'do'", () => RT("[{\"when\":\"always\",\"force\":999,\"do\":\"jump\"}]").Contains("999"));
            // editor #2: an expr-event condition written before "when" survives round-trip
            Test("editor keeps expr-event condition before 'when'", () => RT("[{\"expr\":\"hp<5\",\"when\":\"expr\",\"do\":\"jump\"}]").Replace(" ", "").Contains("hp<5"));
            // breakable: a non-solid (decorative) tile is not broken and does not consume a bullet
            Test("breakable: non-solid tiles are not breakable", () =>
            {
                var p = Sim("[{\"when\":\"start\",\"do\":\"shoot\",\"directions\":1,\"speed\":0}]", extra: BreakMapNonSolid(200, 200));
                p.TSteps(3); return p.TTiles("Map") == 1; // decoration survives
            });

            // ========================= J) DAY / NIGHT CYCLE =========================
            Test("daynight: starts in day (isDay=1)", () =>
            {
                var p = Sim("[]", extra: AddDayNight(("dayLength", 10), ("nightLength", 10), ("transition", 0)));
                p.TSteps(3); return p.TVar("isDay") >= 1 && p.TVar("isNight") < 0.5 && p.TVar("dayness") > 0.9;
            });
            Test("daynight: crosses into night after the day length", () =>
            {
                var p = Sim("[]", extra: AddDayNight(("dayLength", 1), ("nightLength", 2), ("transition", 0)));
                p.TSteps(80); return p.TVar("isNight") >= 1 && p.TVar("dayness") < 0.1; // ~1.28s in → night
            });
            Test("daynight: startAtNight begins at night", () =>
            {
                var p = Sim("[]", extra: AddDayNight(("startAtNight", 1), ("dayLength", 10), ("nightLength", 10)));
                p.TSteps(2); return p.TVar("isNight") >= 1;
            });
            Test("daynight: night is darker than day (lower light)", () =>
            {
                var day = Sim("[]", extra: AddDayNight(("dayLength", 10), ("nightLength", 10), ("transition", 0), ("dayLight", 1), ("nightLight", 0.2)));
                day.TSteps(3);
                var night = Sim("[]", extra: AddDayNight(("startAtNight", 1), ("dayLength", 10), ("nightLength", 10), ("transition", 0), ("dayLight", 1), ("nightLight", 0.2)));
                night.TSteps(3);
                return day.TVar("dayness") > night.TVar("dayness");
            });
            Test("daynight: the sun moves across the sky during day", () =>
            {
                Action<Project, Scene> setup = (pr, sc) =>
                {
                    var l = sc.Layers.First(z => z.Kind == LayerKind.Base);
                    var dn = new BehaviorData { Kind = BehaviorKind.DayNight };
                    dn.Nums["dayLength"] = 5; dn.Nums["nightLength"] = 5; dn.Nums["transition"] = 0; dn.Texts["sun"] = "Sun";
                    l.Objects.Add(new GameObjectData { Id = 80, Name = "DN", Type = "Object", X = 0, Y = 0, Width = 10, Height = 10, Color = "#FFFFFF", Physics = new PhysicsData { Mode = PhysicsMode.None }, Behaviors = { dn } });
                    l.Objects.Add(new GameObjectData { Id = 81, Name = "Sun", Type = "Object", X = 0, Y = 0, Width = 24, Height = 24, Color = "#FFDD55", Physics = new PhysicsData { Mode = PhysicsMode.None } });
                };
                var p = Sim("[]", extra: setup);
                p.TSteps(3); double x1 = p.TX("Sun");
                p.TSteps(40); double x2 = p.TX("Sun");
                return x2 > x1; // the sun travels left → right as the day advances
            });
            Test("daynight: isnight event fires only at night", () =>
            {
                var night = Sim("[{\"when\":\"isnight\",\"do\":\"setvar\",\"var\":\"spawned\",\"to\":1}]", extra: AddDayNight(("startAtNight", 1), ("dayLength", 10), ("nightLength", 10)));
                night.TSteps(3);
                var day = Sim("[{\"when\":\"isnight\",\"do\":\"setvar\",\"var\":\"spawned\",\"to\":1}]", extra: AddDayNight(("dayLength", 10), ("nightLength", 10), ("transition", 0)));
                day.TSteps(3);
                return night.TVar("spawned") >= 1 && day.TVar("spawned") < 0.5;
            });
            Test("daynight: isday event fires only by day", () =>
            {
                var p = Sim("[{\"when\":\"isday\",\"do\":\"setvar\",\"var\":\"ok\",\"to\":1}]", extra: AddDayNight(("dayLength", 10), ("nightLength", 10), ("transition", 0)));
                p.TSteps(3); return p.TVar("ok") >= 1;
            });
            Test("editor: isnight/isday blocks round-trip", () => RT("[{\"when\":\"isnight\",\"do\":\"spawn\",\"object\":\"Zombie\"}]") == RT(RT("[{\"when\":\"isnight\",\"do\":\"spawn\",\"object\":\"Zombie\"}]")));

            // ========================= K) HOTBAR + MINING (Mining primitives) =========================
            Test("wheel: wheeldown event fires on scroll down", () => { var p = Sim("[{\"when\":\"wheeldown\",\"do\":\"addvar\",\"var\":\"n\",\"amount\":1}]"); p.TWheel(-1); p.TStep(); return p.TVar("n") >= 1; });
            Test("wheel: wheelup event fires on scroll up", () => { var p = Sim("[{\"when\":\"wheelup\",\"do\":\"addvar\",\"var\":\"n\",\"amount\":1}]"); p.TWheel(1); p.TStep(); return p.TVar("n") >= 1; });
            Test("hotbar: selected owned slot sets its holding flag", () =>
            {
                var p = Sim("[]", extra: AddHotbar("pick:#111111,axe:#222222"));
                p.TSetVar("item:pick", 1); p.TStep();
                return Math.Abs(p.TVar("holding:pick") - 1) < 0.01 && p.TVar("holding:axe") < 0.5;
            });
            Test("hotbar: an unowned slot holds nothing", () => { var p = Sim("[]", extra: AddHotbar("pick:#111111")); p.TStep(); return p.TVar("holding:pick") < 0.5; });
            Test("hotbar: duplicate slot names keep the selected holding flag", () =>
            {
                var p = Sim("[]", extra: AddHotbar("wood:#111111,wood:#222222"));
                p.TSetVar("item:wood", 1); p.TStep();
                return Math.Abs(p.TVar("holding:wood") - 1) < 0.01; // slot 1 must not clobber it back to 0
            });
            Test("hotbar: the wheel changes the selected slot", () =>
            {
                var p = Sim("[]", extra: AddHotbar("pick:#111111,axe:#222222,sword:#333333"));
                p.TStep(); double s0 = p.TVar("heldSlot");
                p.TWheel(-1); p.TStep(); double s1 = p.TVar("heldSlot");
                return Math.Abs(s0) < 0.01 && Math.Abs(s1 - 1) < 0.01;
            });
            Test("mining: a broken tile drops its resource", () =>
            {
                var p = Sim("[{\"when\":\"start\",\"do\":\"breaktile\",\"where\":\"touching\"}]", extra: BreakMapRes(200, 200, "dirt", ""));
                p.TSteps(2); return p.TItem("dirt") >= 1;
            });
            Test("mining: 'requires' gates breaking until the flag is set", () =>
            {
                var blocked = Sim("[{\"when\":\"start\",\"do\":\"breaktile\",\"where\":\"touching\"}]", extra: BreakMapRes(200, 200, "", "holding:pickaxe"));
                blocked.TSteps(2);
                var allowed = Sim("[{\"when\":\"start\",\"do\":\"breaktile\",\"where\":\"touching\"}]", extra: BreakMapRes(200, 200, "", "holding:pickaxe"));
                allowed.TSetVar("holding:pickaxe", 1); allowed.TSteps(2);
                return blocked.TTiles("Map") == 9 && allowed.TTiles("Map") == 8;
            });

            // ========================= L) SHOP / CRAFTING =========================
            Test("shop: buying charges currency and grants the item", () =>
            {
                var p = Sim("[]", extra: AddShop("coins", 5, "pickaxe"));
                p.TSetVar("coins", 10); p.TKey("E", true); p.TStep();
                return Math.Abs(p.TVar("coins") - 5) < 0.01 && p.TItem("pickaxe") >= 1;
            });
            Test("shop: can't buy without enough currency", () =>
            {
                var p = Sim("[]", extra: AddShop("coins", 5, "pickaxe"));
                p.TSetVar("coins", 2); p.TKey("E", true); p.TStep();
                return Math.Abs(p.TVar("coins") - 2) < 0.01 && p.TItem("pickaxe") < 0.5;
            });
            Test("shop: buys only once", () =>
            {
                var p = Sim("[]", extra: AddShop("coins", 5, "pickaxe"));
                p.TSetVar("coins", 20);
                p.TKey("E", true); p.TStep();            // buy
                p.TKey("E", false); p.TStep();           // release
                p.TKey("E", true); p.TStep();            // press again → blocked
                return Math.Abs(p.TVar("coins") - 15) < 0.01 && p.TItem("pickaxe") >= 1;
            });
            Test("craft: an item currency works as a bench (wood → sword)", () =>
            {
                var p = Sim("[]", extra: AddShop("item:wood", 3, "sword"));
                p.TSetVar("item:wood", 5); p.TKey("E", true); p.TStep();
                return Math.Abs(p.TItem("wood") - 2) < 0.01 && p.TItem("sword") >= 1;
            });
            // ---- Use Item replaces Melee: the sword works like every other item (click → arm swings → hit) ----
            Action<Project, Scene> SwordVsEnemy(double enemyX, string itemDamage = "sword=2") => (pr, sc) =>
            {
                var l = sc.Layers.First(z => z.Kind == LayerKind.Base);
                var a = l.Objects.First(o => o.Name == "A"); // A: box at x 200..264
                a.Behaviors.Add(new BehaviorData { Kind = BehaviorKind.ToolUse, Nums = { ["reach"] = 60, ["knockback"] = 0 }, Texts = { ["tool"] = "sword", ["itemDamage"] = itemDamage } });
                l.Objects.Add(new GameObjectData { Id = 2, Name = "E", Type = "Object", X = enemyX, Y = 200, Width = 30, Height = 48, Color = "#E54", Physics = new PhysicsData { Mode = PhysicsMode.None }, Behaviors = { new BehaviorData { Kind = BehaviorKind.Health, Nums = { ["max"] = 5, ["respawn"] = 0 } } } });
            };
            Test("use item: a sword click hurts an enemy in reach (sword=2 damage)", () =>
            {
                var p = Sim("[]", extra: SwordVsEnemy(290));
                p.TClick(300, 220); p.TSteps(20);
                return Math.Abs(p.THp("E") - 3) < 0.01 && p.TLogHas("Hit by the sword") && p.TVar("stat:enemyHits") == 1;
            });
            Test("use item: an enemy out of reach is not hurt", () =>
            {
                var p = Sim("[]", extra: SwordVsEnemy(600));
                p.TClick(610, 220); p.TSteps(20);
                return Math.Abs(p.THp("E") - 5) < 0.01 && p.TVar("stat:swings") == 1;
            });
            Test("use item: a breakable tile under the cursor breaks with clicks", () =>
            {
                var p = Sim("[]", extra: Combine(BreakMap(200, 264, 3, 2),
                    (pr, sc) => sc.Layers.First(z => z.Kind == LayerKind.Base).Objects.First(o => o.Name == "A")
                                  .Behaviors.Add(new BehaviorData { Kind = BehaviorKind.ToolUse, Texts = { ["tool"] = "pickaxe" } })));
                p.TClick(215, 280); p.TSteps(20);
                return p.TTiles("Map") == 5 && p.TLogHas("Broke tile");
            });
            Test("use item: old projects with Melee are converted to Use Item when they load", () =>
            {
                var tmp = Path.Combine(Path.GetTempPath(), "EE_FullMig_" + Guid.NewGuid().ToString("N").Substring(0, 6));
                Directory.CreateDirectory(tmp);
                var pr = ProjectIO.CreateNew("Mig", tmp);
                var o = pr.GetStartScene().Layers.First(z => z.Kind == LayerKind.Base).Objects.First();
                o.Behaviors.Add(new BehaviorData { Kind = BehaviorKind.Melee, Nums = { ["damage"] = 3, ["range"] = 50 } });
                ProjectIO.Save(pr);
                var loaded = ProjectIO.Load(pr.FilePath!);
                var lo = loaded.GetStartScene().Layers.First(z => z.Kind == LayerKind.Base).Objects.First();
                var tu = lo.Behaviors.FirstOrDefault(b => b.Kind == BehaviorKind.ToolUse);
                return lo.Behaviors.All(b => b.Kind != BehaviorKind.Melee) && tu != null && Math.Abs(tu.Num("damage") - 3) < 0.01 && tu.Text("tool") == "sword";
            });
            Test("use item: Melee is no longer offered (hidden), but still known so old files parse", () =>
                BehaviorCatalog.Info(BehaviorKind.Melee).Hidden && !BehaviorCatalog.Info(BehaviorKind.ToolUse).Hidden);

            // ========================= M) MINING DEMO SMOKE TEST =========================
            Test("mining: the demo project loads and runs without error", () =>
            {
                var proj = BuildMining();
                var pw = new PlayWindow(proj, proj.StartScene ?? proj.Scenes[0].Name); sims.Add(pw);
                pw.TLoad(); pw.TSteps(60);
                return pw.TExists("Player") && pw.TAlive("Coin") > 0; // player alive, coins were generated (once)
            });
            Test("mining: holding the pickaxe mines dirt into the inventory", () =>
            {
                var proj = BuildMining();
                var pw = new PlayWindow(proj, proj.StartScene ?? proj.Scenes[0].Name); sims.Add(pw);
                pw.TLoad();
                pw.TSetVar("item:pickaxe", 1); pw.TSteps(2);   // own + select the pickaxe (hotbar slot 0)
                pw.TMouseAt(100, 760);                          // aim at a dirt tile (Dirt starts at y=720)
                pw.TMouseDown(true); pw.TSteps(5);
                return pw.TItem("dirt") >= 1;
            });
            Test("mining: without the pickaxe, dirt can't be mined", () =>
            {
                var proj = BuildMining();
                var pw = new PlayWindow(proj, proj.StartScene ?? proj.Scenes[0].Name); sims.Add(pw);
                pw.TLoad();
                pw.TMouseAt(100, 760); pw.TMouseDown(true); pw.TSteps(5);
                return pw.TItem("dirt") < 0.5;
            });
            Test("mining: the axe fells a tree (4 clicks next to it) and gives wood", () =>
            {
                var proj = BuildMining();
                var pw = new PlayWindow(proj, proj.StartScene ?? proj.Scenes[0].Name); sims.Add(pw);
                pw.TLoad();
                pw.TSetVar("item:axe", 1); pw.TKey("D2", true); pw.TStep(); pw.TKey("D2", false); pw.TSteps(3); // own + select the axe
                pw.TPlace("Player", 466, 660); pw.TSteps(15);                                                 // stand left of Tree1 (x 520)
                for (int k = 0; k < 4; k++) { pw.TClick(568, 620); pw.TSteps(25); }
                return pw.THeld == "axe" && pw.TFelled("Tree1") && pw.TItem("wood") >= 5;
            });
            Test("mining: mining is limited to a reach around the player", () =>
            {
                var proj = BuildMining();
                var pw = new PlayWindow(proj, proj.StartScene ?? proj.Scenes[0].Name); sims.Add(pw);
                pw.TLoad();
                pw.TSetVar("item:pickaxe", 1); pw.TSteps(2);
                pw.TMouseAt(1500, 760); pw.TMouseDown(true); pw.TSteps(5); // far from the player
                return pw.TItem("dirt") < 0.5;
            });

            // ========================= N) ENGINE REGRESSIONS (v0.4.0 foundations) =========================
            Test("engine: Advanced Patrol with an empty path stops instead of crashing", () =>
            {
                var p = Sim("[]", extra: (pr, sc) =>
                {
                    var l = sc.Layers.First(z => z.Kind == LayerKind.Base);
                    var ap = new BehaviorData { Kind = BehaviorKind.AdvancedPatrol }; ap.Texts["path"] = ""; ap.Nums["mode"] = 1;
                    l.Objects.Add(new GameObjectData { Id = 2, Name = "AP", Type = "Object", X = 300, Y = 300, Width = 30, Height = 30, Color = "#8844FF", Physics = new PhysicsData { Mode = PhysicsMode.None }, Behaviors = { ap } });
                });
                p.TSteps(5); return Math.Abs(p.TVX("AP")) < 0.01;
            });
            Test("engine: death rules fire exactly once", () =>
            {
                var p = Sim("[{\"when\":\"death\",\"do\":\"addvar\",\"var\":\"deaths\",\"amount\":1}]", extra: MakeKillable);
                p.TSteps(30); return Math.Abs(p.TVar("deaths") - 1) < 0.01;
            });
            Test("engine: a click pressed and released within one frame still registers", () =>
            {
                var p = Sim("[{\"when\":\"mousepress\",\"do\":\"addvar\",\"var\":\"n\",\"amount\":1}]");
                p.TMouseTap(); p.TStep(); p.TStep();
                return Math.Abs(p.TVar("n") - 1) < 0.01;
            });
            Test("engine: every behavior kind has a catalog entry", () =>
                Enum.GetValues<BehaviorKind>().All(k => BehaviorCatalog.All.Any(b => b.Kind == k)));

            // ========================= O) DEBUG CONSOLE =========================
            Test("console: the 'log' action writes a line with {variable} substitution", () =>
            {
                var p = Sim("[{\"when\":\"start\",\"do\":\"setvar\",\"var\":\"coins\",\"to\":7},{\"when\":\"start\",\"do\":\"log\",\"text\":\"Coins: {coins}\"}]");
                p.TSteps(2); return p.TLogHas("Coins: 7");
            });
            Test("console: a continuous rule ('always') logs once, not every frame", () =>
            {
                var p = Sim("[{\"when\":\"always\",\"do\":\"addvar\",\"var\":\"n\",\"amount\":1}]");
                p.TSteps(30); return p.TLogCount("Script", "rule started") == 1 && p.TVar("n") >= 30;
            });
            Test("console: a key-press rule logs each press", () =>
            {
                var p = Sim("[{\"when\":\"keypress\",\"key\":\"F\",\"cooldown\":0.05,\"do\":\"addvar\",\"var\":\"n\",\"amount\":1}]");
                p.TKey("F", true); p.TStep(); p.TKey("F", false); p.TSteps(20);
                p.TKey("F", true); p.TStep(); p.TKey("F", false);
                return p.TLogCount("Script", "rule fired") >= 2;
            });
            Test("console: an unknown action warns once", () =>
            {
                var p = Sim("[{\"when\":\"always\",\"do\":\"flyaway\"}]");
                p.TSteps(10); return p.TLogCount("Script", "Unknown action") == 1;
            });
            Test("console: a broken script JSON is reported", () =>
            {
                var p = Sim("[{\"when\":\"always\",, \"do\":");
                p.TStep(); return p.TLogHas("bad JSON");
            });
            Test("console: identical lines merge into one entry with a count", () =>
            {
                var log = new DebugLog();
                for (int i = 0; i < 3; i++) log.Add(i, i, LogLevel.Info, "Tool", "Player", "swing");
                log.Add(4, 4, LogLevel.Info, "Tool", "Player", "hit");
                return log.Entries.Count == 2 && log.Entries[0].Count == 3 && log.Infos == 4;
            });
            Test("console: search and level filters narrow the list", () =>
            {
                var log = new DebugLog();
                log.Add(0, 0, LogLevel.Info, "Tool", "Player", "swing");
                log.Add(1, 1, LogLevel.Warn, "Tool", "Player", "not owned");
                log.Add(2, 2, LogLevel.Info, "Scene", "", "loaded");
                var view = new DebugConsoleView(log); view.Flush();
                int all = view.TestVisibleCount;
                view.TestShowLevel(LogLevel.Info, false); int warnsOnly = view.TestVisibleCount;
                view.TestShowLevel(LogLevel.Info, true); view.TestSearch("loaded"); int searched = view.TestVisibleCount;
                return all == 3 && warnsOnly == 1 && searched == 1;
            });
            Test("editor: 'Write to the console' block round-trips", () =>
            {
                var once = RT("[{\"when\":\"click\",\"do\":\"log\",\"text\":\"hp={hp}\",\"level\":\"warn\"}]");
                return RT(once) == once && once.Contains("hp={hp}") && once.Contains("warn");
            });

            // ========================= P) SOUND =========================
            Test("sound: generated effects are valid WAV files, long enough, and all different", () =>
            {
                var clips = new[] { SoundSynth.Whoosh(), SoundSynth.Chop(), SoundSynth.TreeFall(), SoundSynth.Denied() };
                bool Valid(byte[] w) => w.Length > 44 && Encoding.ASCII.GetString(w, 0, 4) == "RIFF" && Encoding.ASCII.GetString(w, 8, 4) == "WAVE"
                                        && BitConverter.ToInt32(w, 40) == w.Length - 44 && (w.Length - 44) / 2.0 / SoundSynth.Rate >= 0.15;
                return clips.All(Valid) && clips.Select(c => Convert.ToBase64String(c)).Distinct().Count() == 4;
            });
            Test("sound: 'playsound' records + logs the sound (muted in tests)", () =>
            {
                var p = Sim("[{\"when\":\"start\",\"do\":\"playsound\",\"sound\":\"Assets\\\\sounds\\\\chop.wav\"}]",
                    extra: (pr, sc) => ProjectIO.SaveSound(pr, SoundSynth.Chop(), "chop"));
                p.TSteps(2); return p.TSounds.Contains("chop.wav") && p.TLogHas("♪ chop.wav");
            });
            Test("sound: a missing sound file is reported, not silently ignored", () =>
            {
                var p = Sim("[{\"when\":\"start\",\"do\":\"playsound\",\"sound\":\"Assets\\\\sounds\\\\nope.wav\"}]");
                p.TSteps(2); return p.TLogHas("Sound file not found") && p.TSounds.Count == 0;
            });
            Test("editor: 'Play a sound' block round-trips", () =>
            {
                var once = RT("[{\"when\":\"click\",\"do\":\"playsound\",\"sound\":\"Assets\\\\sounds\\\\chop.wav\",\"volume\":0.5}]");
                return RT(once) == once && once.Contains("chop.wav");
            });

            // ========================= Q) TOOL USE + HARVESTABLE =========================
            PlayWindow ToolSim(Action<Project, Scene> scene, bool ownAxe = true, bool ownPick = true)
            {
                var p = Sim("[]", extra: scene);
                if (ownAxe) p.TSetVar("item:axe", 1);
                if (ownPick) p.TSetVar("item:pickaxe", 1);
                p.TSteps(5); // land on the ground; the hotbar puts the item in hand
                return p;
            }
            Test("tool: a click anywhere swings (logged + counted); far from trees it's a miss", () =>
            {
                var p = ToolSim(ToolScene(nearTree: false));
                p.TClick(650, 250); p.TSteps(30);
                return p.TVar("stat:swings") == 1 && p.TVar("stat:hits") == 0 && p.TVar("stat:missed") == 1
                       && p.TLogHas("Axe swing") && p.TLogHas("Nothing in reach") && p.TLogHas("Tree2");
            });
            Test("tool: 4 axe hits fell the tree, drop 5 wood, and it disappears", () =>
            {
                var p = ToolSim(ToolScene());
                for (int k = 0; k < 4; k++) { p.TClick(310, 440); p.TSteps(25); }
                bool felled = p.TFelled("Tree1") && p.TVar("stat:hits") == 4 && Math.Abs(p.TItem("wood") - 5) < 0.01 && p.TLogHas("FELLED");
                p.TSteps(45); // the topple finishes
                return felled && p.TDead("Tree1") && p.TLogHas("Hit 3/4");
            });
            Test("tool: the rock refuses the axe (needs a pickaxe)", () =>
            {
                var p = ToolSim(ToolScene(nearTree: false, rock: true));
                p.TClick(310, 440); p.TSteps(30);
                return p.TVar("stat:denied") == 1 && p.TVar("stat:hits") == 0 && p.TLogHas("needs a pickaxe");
            });
            Test("tool: the pickaxe breaks the rock", () =>
            {
                var p = ToolSim(ToolScene(nearTree: false, rock: true, slot: 0));
                for (int k = 0; k < 3; k++) { p.TClick(310, 440); p.TSteps(25); }
                return p.TFelled("Rock") && Math.Abs(p.TItem("stone") - 5) < 0.01;
            });
            Test("tool: the cooldown stops a double swing", () =>
            {
                var p = ToolSim(ToolScene());
                p.TClick(310, 440); p.TStep(); p.TClick(310, 440); p.TStep();
                p.TSteps(25);
                return p.TVar("stat:swings") == 1 && p.TVar("stat:hits") == 1;
            });
            Test("tool: clicking a hotbar slot selects it and does not swing", () =>
            {
                var p = ToolSim(ToolScene());
                var c = p.THotbarSlotCenter(0);
                bool isUi = p.TIsUi(c.X, c.Y);
                p.TClick(c.X, c.Y); p.TSteps(25);
                return isUi && p.TVar("heldSlot") == 0 && p.THeld == "pickaxe" && p.TVar("stat:swings") == 0;
            });
            Test("tool: an unowned slot warns and does not swing", () =>
            {
                var p = ToolSim(ToolScene(), ownAxe: false);
                p.TClick(310, 440); p.TSteps(25);
                return p.TVar("stat:swings") == 0 && p.TLogHas("not owned");
            });
            Test("tool: a felled tree can't be hit again", () =>
            {
                var p = ToolSim(ToolScene(farTree: false));
                for (int k = 0; k < 4; k++) { p.TClick(310, 440); p.TSteps(25); }
                p.TSteps(45);
                p.TClick(310, 440); p.TSteps(25);
                return p.TVar("stat:hits") == 4 && p.TVar("stat:swings") == 5;
            });
            Test("tool: without a hotbar, the fixed 'tool' parameter is used", () =>
            {
                var p = ToolSim(ToolScene(hotbar: false, fixedTool: "axe"));
                p.TClick(310, 440); p.TSteps(25);
                return p.TVar("stat:hits") == 1;
            });
            Test("tool: the swing sound plays first, the chop a few frames later", () =>
            {
                var p = ToolSim(ToolScene(sounds: true));
                p.TClick(310, 440); p.TStep();
                bool swingNow = p.TSounds.Contains("swing.wav") && !p.TSounds.Contains("chop.wav");
                p.TSteps(25);
                return swingNow && p.TSounds.ToList().IndexOf("swing.wav") < p.TSounds.ToList().IndexOf("chop.wav");
            });

            // ========================= R) CHECKLIST =========================
            Action<Project, Scene> Checklist(string items) => (pr, sc) =>
            {
                var l = sc.Layers.First(z => z.Kind == LayerKind.Base);
                l.Objects.Add(new GameObjectData { Id = 50, Name = "Checks", Type = "Object", X = 0, Y = 0, Width = 4, Height = 4, Color = "#00000000", Physics = new PhysicsData { Mode = PhysicsMode.None },
                    Behaviors = { new BehaviorData { Kind = BehaviorKind.Checklist, Texts = { ["items"] = items, ["title"] = "Test" } } } });
            };
            Test("checklist: lines tick when true and stay ticked; ALL PASSED at the end", () =>
            {
                var p = Sim("[]", extra: Checklist("Get 3 coins => coins >= 3; Press E => pressedE >= 1"));
                p.TSetVar("coins", 3); p.TStep();
                bool one = p.TVar("check:done") == 1;
                p.TSetVar("coins", 0); p.TStep();
                bool stays = p.TVar("check:done") == 1;
                p.TSetVar("pressedE", 1); p.TStep();
                return one && stays && p.TVar("check:all") == 1 && p.TLogHas("✓ Get 3 coins") && p.TLogHas("ALL PASSED");
            });
            Test("checklist: a line without '=>' is reported", () =>
            {
                var p = Sim("[]", extra: Checklist("no arrow here; Ok => 1"));
                p.TStep(); return p.TLogHas("without '=>'") && p.TVar("check:total") == 1;
            });
            Test("checklist: its panel counts as UI (clicks on it don't reach the game)", () =>
            {
                var p = Sim("[]", extra: Checklist("A => 0"));
                p.TStep(); var c = p.TUiCenter("the checklist");
                return c.X > 0 && p.TIsUi(c.X, c.Y);
            });

            // ========================= S) E2E: the "Axe Test" scenario, played step by step =========================
            foreach (var (step, ok, lines) in RunAxeE2E())
                Test("e2e axe: " + step + (ok ? "" : "  ⟶ " + string.Join(" | ", lines.TakeLast(4))), () => ok);
            foreach (var (step, ok, lines) in RunLightingE2E())
                Test("e2e lighting: " + step + (ok ? "" : "  ⟶ " + string.Join(" | ", lines.TakeLast(4))), () => ok);

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
    // a 1x1 breakable tilemap whose single tile is NON-solid (decoration)
    private static Action<Project, Scene> BreakMapNonSolid(double x, double y)
    {
        return (p, s) =>
        {
            var l = s.Layers.First(z => z.Kind == LayerKind.Base);
            var bh = new BehaviorData { Kind = BehaviorKind.Tilemap };
            bh.Texts["palette"] = "[{\"color\":\"#55AA55\",\"solid\":false}]";
            bh.Texts["data"] = "{\"cols\":1,\"rows\":1,\"cells\":[1]}";
            bh.Nums["tileSize"] = 32; bh.Nums["breakable"] = 1;
            l.Objects.Add(new GameObjectData { Id = 92, Name = "Map", Type = "Object", X = x, Y = y, Width = 32, Height = 32, Color = "#000000", Physics = new PhysicsData { Mode = PhysicsMode.None }, Behaviors = { bh } });
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

    private static Project BuildMining()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "EE_FullTerr_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(tmp);
        return ProjectIO.Load(MiningGame.Build(tmp));
    }

    // ---- tool use / harvesting scene: ground, a player (Hotbar + ToolUse), trees and an optional rock ----
    // Player box: x 200..230. Near tree at x 290 (gap 60, inside reach 80); far tree at x 1000.
    private static Action<Project, Scene> ToolScene(string items = "pickaxe:#B5651D,axe:#9AA0A0", int slot = 1, bool hotbar = true, string fixedTool = "",
        bool nearTree = true, bool farTree = true, bool rock = false, bool sounds = false)
    {
        return (p, s) =>
        {
            var l = s.Layers.First(z => z.Kind == LayerKind.Base);
            l.Objects.Add(new GameObjectData { Id = 40, Name = "Ground", Type = "Object", X = 0, Y = 500, Width = 3000, Height = 40, Color = "#4A5A3A", Physics = new PhysicsData { Mode = PhysicsMode.Static }, Behaviors = { new BehaviorData { Kind = BehaviorKind.Solid } } });
            string swing = "", chop = "", fall = "", denied = "";
            if (sounds)
            {
                swing = ProjectIO.SaveSound(p, SoundSynth.Whoosh(), "swing");
                chop = ProjectIO.SaveSound(p, SoundSynth.Chop(), "chop");
                fall = ProjectIO.SaveSound(p, SoundSynth.TreeFall(), "fall");
                denied = ProjectIO.SaveSound(p, SoundSynth.Denied(), "denied");
            }
            GameObjectData Harv(int id, string name, double x, string tool, string drop, int hits) =>
                new() { Id = id, Name = name, Type = "Object", X = x, Y = 380, Width = 40, Height = 120, Color = "#6E4A24", Physics = new PhysicsData { Mode = PhysicsMode.None },
                        Behaviors = { new BehaviorData { Kind = BehaviorKind.Harvestable, Nums = { ["hits"] = hits, ["amount"] = 5 }, Texts = { ["tool"] = tool, ["drop"] = drop, ["hitSound"] = chop, ["fellSound"] = fall } } } };
            if (nearTree) l.Objects.Add(Harv(41, "Tree1", 290, "axe", "wood", 4));
            if (farTree) l.Objects.Add(Harv(42, "Tree2", 1000, "axe", "wood", 4));
            if (rock) l.Objects.Add(Harv(43, "Rock", 290, "pickaxe", "stone", 3));
            var pl = new GameObjectData { Id = 44, Name = "Player", Type = "Player", X = 200, Y = 450, Width = 30, Height = 50, Color = "#3BC97A", Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            pl.Behaviors.Add(new BehaviorData { Kind = BehaviorKind.Platformer });
            if (hotbar) pl.Behaviors.Add(new BehaviorData { Kind = BehaviorKind.Hotbar, Nums = { ["slot"] = slot }, Texts = { ["items"] = items, ["player"] = "Player" } });
            pl.Behaviors.Add(new BehaviorData { Kind = BehaviorKind.ToolUse, Nums = { ["reach"] = 80, ["cooldown"] = 0.35, ["swingTime"] = 0.25 }, Texts = { ["tool"] = fixedTool, ["swingSound"] = swing, ["deniedSound"] = denied } });
            l.Objects.Add(pl);
        };
    }

    // ---- hotbar + mining setups ----
    private static Action<Project, Scene> AddHotbar(string items)
    {
        return (p, s) =>
        {
            var l = s.Layers.First(z => z.Kind == LayerKind.Base);
            var bh = new BehaviorData { Kind = BehaviorKind.Hotbar };
            bh.Texts["items"] = items; bh.Texts["player"] = "none";
            l.Objects.Add(new GameObjectData { Id = 70, Name = "HB", Type = "Object", X = 0, Y = 0, Width = 10, Height = 10, Color = "#FFFFFF", Physics = new PhysicsData { Mode = PhysicsMode.None }, Behaviors = { bh } });
        };
    }
    private static Action<Project, Scene> BreakMapRes(double x, double y, string drop, string requires)
    {
        return (p, s) =>
        {
            var l = s.Layers.First(z => z.Kind == LayerKind.Base);
            var bh = new BehaviorData { Kind = BehaviorKind.Tilemap };
            bh.Texts["palette"] = "[{\"color\":\"#8B5A2B\",\"solid\":true}]";
            bh.Texts["data"] = "{\"cols\":3,\"rows\":3,\"cells\":[1,1,1,1,1,1,1,1,1]}";
            bh.Nums["tileSize"] = 32; bh.Nums["breakable"] = 1;
            if (drop.Length > 0) bh.Texts["drop"] = drop;
            if (requires.Length > 0) bh.Texts["requires"] = requires;
            l.Objects.Add(new GameObjectData { Id = 93, Name = "Map", Type = "Object", X = x, Y = y, Width = 96, Height = 96, Color = "#000000", Physics = new PhysicsData { Mode = PhysicsMode.None }, Behaviors = { bh } });
        };
    }

    private static Action<Project, Scene> AddShop(string currency, double cost, string item, bool once = true)
    {
        return (p, s) =>
        {
            var l = s.Layers.First(z => z.Kind == LayerKind.Base);
            var bh = new BehaviorData { Kind = BehaviorKind.Shop };
            bh.Texts["currency"] = currency; bh.Texts["item"] = item; bh.Texts["key"] = "E"; bh.Texts["player"] = "P";
            bh.Nums["cost"] = cost; bh.Nums["radius"] = 80; bh.Nums["once"] = once ? 1 : 0;
            l.Objects.Add(new GameObjectData { Id = 60, Name = "Shop", Type = "Object", X = 200, Y = 200, Width = 40, Height = 40, Color = "#884422", Physics = new PhysicsData { Mode = PhysicsMode.None }, Behaviors = { bh } });
            l.Objects.Add(new GameObjectData { Id = 61, Name = "P", Type = "Object", X = 210, Y = 210, Width = 20, Height = 20, Color = "#44AAFF", Physics = new PhysicsData { Mode = PhysicsMode.None } });
        };
    }

    // ---- day/night setups ----
    private static Action<Project, Scene> AddDayNight(params (string key, double val)[] nums)
    {
        return (p, s) =>
        {
            var l = s.Layers.First(z => z.Kind == LayerKind.Base);
            var bh = new BehaviorData { Kind = BehaviorKind.DayNight };
            foreach (var (k, v) in nums) bh.Nums[k] = v;
            l.Objects.Add(new GameObjectData { Id = 80, Name = "DN", Type = "Object", X = 0, Y = 0, Width = 10, Height = 10, Color = "#FFFFFF", Physics = new PhysicsData { Mode = PhysicsMode.None }, Behaviors = { bh } });
        };
    }

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
