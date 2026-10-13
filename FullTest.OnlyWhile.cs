using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows.Controls;

namespace EasyEngine;

// "Only while" picked, not typed (MainWindow.OnlyWhile.cs): each choice writes its formula and reads back as the same
// choice, an unknown formula opens as "Formula (advanced)" untouched, "Always" removes it, every card that has the row
// gets the builder; the game: "key:E" is 1 while E is held (without allocating), a Go To Scene that only works while E is
// held or while a variable is set, a Spin that only turns while the player is near; the AI's "while E is held".
public partial class App
{
    partial void FeatureTestsOnlyWhile(FullTestKit t)
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
        static GameObjectData Player(double x)
        {
            var p = O(3, "Player", x, GroundY - 48, 32, 48, PhysicsMode.Dynamic, B(BehaviorKind.Platformer));
            p.Type = "Player";
            return p;
        }
        static GameObjectData Ground() => O(1, "Ground", 0, GroundY, 1600, 60, PhysicsMode.Static, B(BehaviorKind.Solid));
        PlayWindow Play(string tag, params GameObjectData[] objects)
        {
            var dir = Path.Combine(Path.GetTempPath(), "EE_Full_OnlyWhile_" + tag + "_" + Guid.NewGuid().ToString("N")[..6]);
            var p = ProjectIO.CreateNew("OnlyWhile", dir);
            var s = p.GetStartScene();
            s.Width = 1600; s.Height = 900;
            foreach (var l in s.Layers) l.Objects.Clear();
            s.Layers.First(l => l.Kind == LayerKind.Base).Objects.AddRange(objects);
            p.Scenes.Add(new Scene { Name = "S2", Layers = { new Layer { Name = "Base", Kind = LayerKind.Base } } });
            var pw = new PlayWindow(p, s.Name); t.Sims.Add(pw);
            pw.TSeed(1); pw.TLoad();
            return pw;
        }

        // ---- the formula each choice writes, and reading it back ----
        var table = new (OnlyWhile.Cond[] parts, string formula)[]
        {
            (new[] { new OnlyWhile.Cond(OnlyWhile.Kind.Key, "E") }, "key:E >= 1"),
            (new[] { new OnlyWhile.Cond(OnlyWhile.Kind.Key, "Space") }, "key:Space >= 1"),
            (new[] { new OnlyWhile.Cond(OnlyWhile.Kind.Variable, "coins", ">=", "10") }, "coins >= 10"),
            (new[] { new OnlyWhile.Cond(OnlyWhile.Kind.Variable, "local.hp", "<", "3") }, "local.hp < 3"),
            (new[] { new OnlyWhile.Cond(OnlyWhile.Kind.Variable, "doorOpen", "!=", "0") }, "doorOpen != 0"),
            (new[] { new OnlyWhile.Cond(OnlyWhile.Kind.Near, "Player", "<", "200") }, "dist(self.cx,self.cy,Player.cx,Player.cy) < 200"),
            (new[] { new OnlyWhile.Cond(OnlyWhile.Kind.Item, "key") }, "item:key >= 1"),
            (new[] { new OnlyWhile.Cond(OnlyWhile.Kind.Night) }, "isNight >= 1"),
            (new[] { new OnlyWhile.Cond(OnlyWhile.Kind.Day) }, "isNight < 1"),
            (new[] { new OnlyWhile.Cond(OnlyWhile.Kind.Ground) }, "self.grounded >= 1"),
            (new[] { new OnlyWhile.Cond(OnlyWhile.Kind.Mouse) }, "mousedown >= 1"),
            (new[] { new OnlyWhile.Cond(OnlyWhile.Kind.Key, "E"), new OnlyWhile.Cond(OnlyWhile.Kind.Item, "key") }, "key:E >= 1 && item:key >= 1"),
        };
        t.Test("only while: each choice writes its formula (key:E >= 1, coins >= 10, dist(…) < 200, item:key >= 1, isNight…) and reads back as the same choice", () =>
        {
            foreach (var (parts, formula) in table)
            {
                if (OnlyWhile.Format(parts) != formula) return false;
                var back = OnlyWhile.Parse(formula);
                if (back.Count != parts.Length) return false;
                for (int i = 0; i < parts.Length; i++)
                {
                    var a = parts[i]; var b = back[i];
                    if (a.Kind != b.Kind || a.A != b.A || (a.Kind is OnlyWhile.Kind.Variable or OnlyWhile.Kind.Near && (a.Op != b.Op || a.B != b.B))) return false;
                }
            }
            return true;
        });
        t.Test("only while: an unknown formula is 'Formula (advanced)' with its text as is; empty is Always (no cond)", () =>
        {
            foreach (var f in new[] { "Player.cx < self.cx", "coins >= 10 || lives < 1", "key:E >= 1 && Player.x > 3", "abs(self.vx) > 10", "item:key >= 2" })
            {
                var p = OnlyWhile.Parse(f);
                if (p.Count != 1 || p[0].Kind != OnlyWhile.Kind.Formula || p[0].A != f || OnlyWhile.Format(p) != f) return false;
            }
            return OnlyWhile.Parse("").Count == 0 && OnlyWhile.Parse("  ").Count == 0 && OnlyWhile.Format(new List<OnlyWhile.Cond>()) == ""
                   && OnlyWhile.Parse("key:Nothing >= 1")[0].Kind == OnlyWhile.Kind.Formula; // not a key: left as typed
        });

        // ---- the inspector ----
        t.Test("only while: the card opens on the saved choice (key E held), an unknown formula opens as advanced untouched, Always removes it, Key writes key:E >= 1", () =>
        {
            var project = ProjectIO.CreateNew("OnlyWhileUi", Path.Combine(Path.GetTempPath(), "EE_Full_OnlyWhileUi_" + Guid.NewGuid().ToString("N")[..6]));
            project.Scenes.Add(new Scene { Name = "Level2", Layers = { new Layer { Name = "Base", Kind = LayerKind.Base } } });
            var ed = new MainWindow(project);
            try
            {
                var door = new GameObjectData { Name = "Door" };
                var go = B(BehaviorKind.GoToScene, ("scene", "Level2"), ("cond", "key:E >= 1"));
                door.Behaviors.Add(go);
                static string? Sel(ComboBox cb) => (cb.SelectedItem as ComboBoxItem)?.Tag as string;
                var rows = ed.TestOnlyWhile(door);
                if (rows.Count != 1 || rows[0].combos.Count < 2 || Sel(rows[0].combos[0]) != "Key" || Sel(rows[0].combos[1]) != "E" || rows[0].boxes.Count != 0) return false;

                go.Texts["cond"] = "Player.cx < self.cx";
                rows = ed.TestOnlyWhile(door);
                if (Sel(rows[0].combos[0]) != "Formula" || rows[0].boxes.Count != 1 || rows[0].boxes[0].Text != "Player.cx < self.cx" || go.Text("cond", "") != "Player.cx < self.cx") return false;

                ComboBoxItem Item(ComboBox cb, string tag) => cb.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag! == tag);
                rows[0].combos[0].SelectedItem = Item(rows[0].combos[0], "Always");
                if (go.Texts.ContainsKey("cond")) return false;
                rows = ed.TestOnlyWhile(door);
                if (Sel(rows[0].combos[0]) != "Always" || rows[0].combos.Count != 1) return false;
                rows[0].combos[0].SelectedItem = Item(rows[0].combos[0], "Key");
                if (go.Text("cond", "") != "key:E >= 1") return false;

                // two choices: E held AND has the key → one formula with &&, opening with both rows
                go.Texts["cond"] = "key:E >= 1 && item:key >= 1";
                rows = ed.TestOnlyWhile(door);
                return rows[0].combos.Count >= 4 && Sel(rows[0].combos[0]) == "Key" && Sel(rows[0].combos[2]) == "Item";
            }
            finally { try { ed.Close(); } catch { } }
        });
        t.Test("only while: every behavior that can rest (OnlyWhileKinds) gets the picked builder, none the old typed row", () =>
        {
            var project = ProjectIO.CreateNew("OnlyWhileAll", Path.Combine(Path.GetTempPath(), "EE_Full_OnlyWhileAll_" + Guid.NewGuid().ToString("N")[..6]));
            var ed = new MainWindow(project);
            try
            {
                var o = new GameObjectData { Name = "Thing" };
                foreach (var k in PlayWindow.OnlyWhileKinds) o.Behaviors.Add(BehaviorCatalog.Info(k).CreateData());
                var rows = ed.TestOnlyWhile(o);
                return rows.Count == PlayWindow.OnlyWhileKinds.Count && rows.All(r => r.boxes.Count == 0 && r.combos.Count == 1);
            }
            finally { try { ed.Close(); } catch { } }
        });

        // ---- the game ----
        t.Test("only while: Go To Scene with 'a key is held (E)' changes scene only while E is held", () =>
        {
            var pw = Play("Key", Ground(), O(2, "Door", 300, GroundY - 80, 60, 80, PhysicsMode.None, B(BehaviorKind.GoToScene, ("scene", "S2"), ("cond", "key:E >= 1"))), Player(310));
            pw.TSteps(30);
            if (pw.TScene == "S2") return false; // touching it, E not held
            pw.TKey("E", true); pw.TSteps(3);
            return pw.TScene == "S2";
        });
        t.Test("only while: Go To Scene with 'a variable is… (open is at least 1)' waits for the variable", () =>
        {
            var pw = Play("Var", Ground(), O(2, "Door", 300, GroundY - 80, 60, 80, PhysicsMode.None, B(BehaviorKind.GoToScene, ("scene", "S2"), ("cond", "open >= 1"))), Player(310));
            pw.TSteps(30);
            if (pw.TScene == "S2") return false;
            pw.TSetVar("open", 1); pw.TSteps(3);
            return pw.TScene == "S2";
        });
        t.Test("only while: a Spin with 'the player is near (200 px)' turns only while the player is within 200 px", () =>
        {
            var pw = Play("Near", Ground(), O(2, "Fan", 1000, GroundY - 200, 40, 40, PhysicsMode.None, B(BehaviorKind.Spin, ("speed", 90), ("cond", "dist(self.cx,self.cy,Player.cx,Player.cy) < 200"))), Player(100));
            pw.TSteps(20);
            if (Math.Abs(pw.TRot("Fan")) > 0.001) return false;
            pw.TPlace("Player", 960, GroundY - 48); pw.TSteps(20);
            return pw.TRot("Fan") > 10;
        });
        t.Test("only while: key:E is 1 while E is held, 0 otherwise (any case, Space / shift / 1 too), and reading it allocates nothing", () =>
        {
            var pw = Play("Alloc", Ground(), Player(100));
            if (pw.TResolve("key:E") != 0 || pw.TEval("key:E >= 1") != 0) return false;
            pw.TKey("E", true);
            if (pw.TResolve("key:E") != 1 || pw.TResolve("KEY:e") != 1 || pw.TEval("key:E >= 1 && 1") != 1) return false;
            pw.TKey("Space", true); pw.TKey("LeftShift", true); pw.TKey("D1", true);
            if (pw.TResolve("key:space") != 1 || pw.TResolve("key:shift") != 1 || pw.TResolve("key:1") != 1 || pw.TResolve("key:nothing") != 0) return false;
            for (int i = 0; i < 10; i++) { pw.TResolve("key:E"); pw.TResolve("key:Q"); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            double sum = 0;
            for (int i = 0; i < 10000; i++) sum += pw.TResolve("key:E") + pw.TResolve("key:Q");
            long used = GC.GetAllocatedBytesForCurrentThread() - before;
            return used == 0 && sum == 10000;
        });

        // ---- the AI ----
        t.Test("only while: the AI's 'while E is held', key(Space), keydown:E become key:E >= 1; formulas and 'keys >= 1' stay", () =>
        {
            string Fix(string cond)
            {
                var item = new JsonObject { ["kind"] = "GoToScene", ["texts"] = new JsonObject { ["scene"] = "S2", ["cond"] = cond } };
                AiSpecFix.Behavior(BehaviorCatalog.Info(BehaviorKind.GoToScene), item, new List<string>());
                return (string?)item["texts"]?["cond"] ?? "";
            }
            return Fix("while E is held") == "key:E >= 1" && Fix("key(Space)") == "key:Space >= 1" && Fix("keydown:E") == "key:E >= 1"
                   && Fix("E pressed") == "key:E >= 1" && Fix("holding the shift key") == "key:LeftShift >= 1" && Fix("key:e") == "key:E >= 1"
                   && Fix("key:E >= 1") == "key:E >= 1" && Fix("keys >= 1") == "keys >= 1" && Fix("Player.cx < self.cx") == "Player.cx < self.cx"
                   && Fix("night") == "isNight >= 1";
        });
    }
}
