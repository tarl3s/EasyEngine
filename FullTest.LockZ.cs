using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace EasyEngine;

// Editor object tools: 🔒 Lock (no drag/resize/nudge/delete, read-only inspector except the lock, the AI skips it,
// undoable), Z layer (order inside a layer in the editor and the game, To front / To back, spawned copies, never across
// layers), arrow-key nudge (1 px, Shift 10 px or a grid cell, one undo per burst), Keep proportions and Original size.
public partial class App
{
    partial void FeatureTestsLockZ(FullTestKit t)
    {
        static bool Near(double a, double b, double tol = 0.01) => Math.Abs(a - b) <= tol;
        (Project p, Layer layer, MainWindow ed) Editor(string tag, Action<Project, Layer>? fill = null)
        {
            var dir = Path.Combine(Path.GetTempPath(), "EE_Full_LockZ_" + tag + "_" + Guid.NewGuid().ToString("N")[..6]);
            var p = ProjectIO.CreateNew("LZ", dir);
            var layer = p.GetStartScene().Layers.First(l => l.Kind == LayerKind.Base);
            fill?.Invoke(p, layer);
            return (p, layer, new MainWindow(p));
        }
        GameObjectData Box(int id, string name, double x, double y, int z = 0, bool locked = false) =>
            new() { Id = id, Name = name, X = x, Y = y, Width = 64, Height = 32, Z = z, Locked = locked };

        // ---------------- model + old files
        t.Test("lockz: an old project loads unlocked with Z 0, keeps its list order, and the fields save", () =>
        {
            var old = JsonSerializer.Deserialize<GameObjectData>("{\"Name\":\"A\",\"X\":5}")!;
            var on = new GameObjectData { Locked = true, Z = 3, KeepAspect = true, Aspect = 2 };
            var back = JsonSerializer.Deserialize<GameObjectData>(JsonSerializer.Serialize(on))!;
            var list = new[] { Box(1, "a", 0, 0), Box(2, "b", 0, 0), Box(3, "c", 0, 0) }.ToList();
            return !old.Locked && old.Z == 0 && !old.KeepAspect && back.Locked && back.Z == 3 && back.KeepAspect && back.Aspect == 2
                   && ReferenceEquals(ZOrder.InDrawOrder(list), list);
        });

        // ---------------- lock
        t.Test("lock: a locked object is selected on click but never dragged, has no resize handles, isn't nudged or deleted", () =>
        {
            var (p, layer, ed) = Editor("L1", (_, l) => { l.Objects.Add(Box(10, "Rock", 100, 100, locked: true)); l.Objects.Add(Box(11, "Free", 300, 100)); });
            var rock = ed.TestFindObject("Rock")!; var free = ed.TestFindObject("Free")!;
            bool dragRock = ed.TestPressOn(rock); bool handles = ed.TestHasHandles;
            bool dragFree = ed.TestPressOn(free); bool freeHandles = ed.TestHasHandles;
            ed.TestSelect(rock);
            bool resized = ed.TestResize("BR", 40, 40, false);
            ed.TestNudge(1, 0, false); ed.TestNudge(0, 1, true);
            ed.TestSelectMany(rock, free); ed.TestNudgeBurstEnd(); ed.TestNudge(1, 0, false); // multi-selection: only the free one moves
            ed.TestSelect(rock); ed.TestDeleteSelected();
            return !dragRock && !handles && dragFree && freeHandles && !resized
                   && rock.X == 100 && rock.Y == 100 && rock.Width == 64 && free.X == 301 && ed.TestFindObject("Rock") != null;
        });
        t.Test("lock: the inspector is read-only except the lock; unlocking re-enables it; undo restores the lock", () =>
        {
            var (p, layer, ed) = Editor("L2", (_, l) => l.Objects.Add(Box(10, "Rock", 100, 100)));
            var rock = ed.TestFindObject("Rock")!;
            ed.TestSelect(rock);
            var open = ed.TestInspectorLock();
            ed.TestClickLock();
            rock = ed.TestFindObject("Rock")!;
            var locked = ed.TestInspectorLock();
            bool isLocked = rock.Locked, row = ed.TestObjectRow(rock).StartsWith("🔒");
            ed.TestClickLock();
            var unlocked = ed.TestInspectorLock();
            bool isUnlocked = !ed.TestFindObject("Rock")!.Locked;
            ed.TestUndo(); // undo the unlock
            bool backLocked = ed.TestFindObject("Rock")!.Locked;
            return open.othersEnabled && isLocked && row && locked.lockEnabled && locked.othersDisabled
                   && isUnlocked && unlocked.othersEnabled && unlocked.lockEnabled && backLocked;
        });
        t.Test("lock: the AI skips a locked object (noted in its summary); the prompt marks it LOCKED and explains \"z\"", () =>
        {
            var (p, layer, ed) = Editor("L3", (_, l) => { l.Objects.Add(Box(10, "Rock", 100, 100, locked: true)); l.Objects.Add(Box(11, "Tree", 300, 100)); });
            var (ok, summary) = ed.TestApplySpec("{\"edit\":[{\"name\":\"Rock\",\"x\":500,\"color\":\"#FF0000\",\"z\":4},{\"name\":\"Tree\",\"z\":\"2\"}]}");
            var rock = ed.TestFindObject("Rock")!; var tree = ed.TestFindObject("Tree")!;
            string prompt = ed.TestBuildPrompt(), compact = ed.TestBuildPrompt(true);
            return ok && rock.X == 100 && rock.Z == 0 && rock.Color != "#FF0000" && tree.Z == 2
                   && summary.Contains("Rock") && summary.Contains("locked")
                   && prompt.Contains("\"Rock\"") && prompt.Contains("LOCKED") && prompt.Contains("\"z\"") && compact.Contains("\"z\"");
        });

        // ---------------- arrow-key nudge
        t.Test("nudge: arrows move 1 px, Shift 10 px (a grid cell with the grid on), a burst is one undo step", () =>
        {
            var (p, layer, ed) = Editor("N1", (_, l) => l.Objects.Add(Box(10, "Crate", 100, 100)));
            var c = ed.TestFindObject("Crate")!;
            ed.TestSelect(c);
            ed.TestNudge(1, 0, false); ed.TestNudge(1, 0, false); ed.TestNudge(0, -1, false);
            bool px = c.X == 102 && c.Y == 99;
            ed.TestNudge(1, 0, true); bool shift = c.X == 112;
            ed.TestSetSnap(true); ed.TestNudge(0, 1, true); ed.TestSetSnap(false);
            bool grid = c.Y == 115;
            ed.TestUndo(); // the whole burst at once
            var back = ed.TestFindObject("Crate")!;
            return px && shift && grid && back.X == 100 && back.Y == 100;
        });

        // ---------------- Z
        t.Test("z: the editor canvas draws by Z inside a layer; equal Z keeps the list order; Z never crosses layers", () =>
        {
            var (p, layer, ed) = Editor("Z1", (proj, l) =>
            {
                l.Objects.Add(Box(10, "A", 0, 0, z: 2)); l.Objects.Add(Box(11, "B", 0, 0)); l.Objects.Add(Box(12, "C", 0, 0));
                proj.GetStartScene().Layers.First(x => x.Kind == LayerKind.Background).Objects.Add(Box(13, "Back", 0, 0, z: 99));
            });
            int I(string n) => ed.TestCanvasIndex(ed.TestFindObject(n)!);
            return I("Back") < I("B") && I("B") < I("C") && I("C") < I("A");
        });
        t.Test("z: To front / To back (max+1 / min−1 among the layer), − / +, the list shows z, undo", () =>
        {
            var (p, layer, ed) = Editor("Z2", (_, l) => { l.Objects.Add(Box(10, "A", 0, 0, z: 3)); l.Objects.Add(Box(11, "B", 0, 0, z: -1)); l.Objects.Add(Box(12, "C", 0, 0)); });
            var c = ed.TestFindObject("C")!;
            ed.TestSelect(c);
            ed.TestZFront(c); bool front = c.Z == 4 && ed.TestCanvasIndex(c) > ed.TestCanvasIndex(ed.TestFindObject("A")!);
            ed.TestZBack(c); bool back = c.Z == -2 && ed.TestCanvasIndex(c) < ed.TestCanvasIndex(ed.TestFindObject("B")!);
            ed.TestSetZ(c, c.Z + 1); bool plus = c.Z == -1 && ed.TestObjectRow(c).EndsWith("z-1");
            bool zeroRow = !ed.TestObjectRow(new GameObjectData { Name = "D" }).Contains("z");
            ed.TestUndo();
            return front && back && plus && zeroRow && ed.TestFindObject("C")!.Z == -2;
        });
        t.Test("z: the game draws by Z inside a layer (equal Z keeps list order, layers keep theirs); spawned copies keep Z", () =>
        {
            var s = t.Sim("[]", PhysicsMode.None, (proj, sc) =>
            {
                var l = sc.Layers.First(x => x.Kind == LayerKind.Base);
                l.Objects.Add(Box(20, "Hi", 0, 0, z: 5)); l.Objects.Add(Box(21, "Mid1", 0, 0)); l.Objects.Add(Box(22, "Mid2", 0, 0)); l.Objects.Add(Box(23, "Lo", 0, 0, z: -3));
                sc.Layers.First(x => x.Kind == LayerKind.Background).Objects.Add(Box(24, "Bg", 0, 0, z: 50));
                proj.Prefabs.Add(new PrefabDef { Name = "Front", Template = new GameObjectData { Name = "Front", Width = 10, Height = 10, Z = 9 } });
                proj.Prefabs.Add(new PrefabDef { Name = "Behind", Template = new GameObjectData { Name = "Behind", Width = 10, Height = 10, Z = -9 } });
            });
            s.TStep();
            int I(int id) => s.TVisualIndexById(id);
            bool scene = I(24) < I(23) && I(23) < I(21) && I(21) < I(22) && I(22) < I(20);
            s.TSpawn("Front", 10, 10); s.TSpawn("Behind", 10, 10); s.TStep();
            int front = s.TSpawnedIndexes("Front").Single(), behind = s.TSpawnedIndexes("Behind").Single();
            return scene && s.TZOf("Front") == 9 && s.TZOf("Behind") == -9 && behind < front && front > I(20);
        });
        t.Test("z: a duplicate keeps Z and starts unlocked", () =>
        {
            var (p, layer, ed) = Editor("Z3", (_, l) => l.Objects.Add(Box(10, "A", 0, 0, z: 7, locked: true)));
            var a = ed.TestFindObject("A")!;
            ed.TestSelect(a); ed.TestDuplicate();
            var copy = ed.TestFindObject("A copy");
            return copy != null && copy.Z == 7 && !copy.Locked;
        });

        // ---------------- keep proportions + original size
        string SavePng(Project p, int w, int h, string name) => ProjectIO.SaveSprite(p, HitboxTestSprite(w, h, new Int32Rect(0, 0, w, h)), name);
        t.Test("size: Keep proportions on handle resize (corner + side) and on typed Width/Height; undo", () =>
        {
            var (p, layer, ed2) = Editor("S1", (proj, l) =>
                l.Objects.Add(new GameObjectData { Id = 10, Name = "Pic", X = 100, Y = 100, Width = 40, Height = 20, SpritePath = SavePng(proj, 40, 20, "pic") })); // 2:1
            var pic = ed2.TestFindObject("Pic")!;
            ed2.TestSelect(pic); ed2.TestSetKeepAspect(pic, true);
            ed2.TestResize("BR", 60, 0, false); bool corner = Near(pic.Width, 100) && Near(pic.Height, 50) && pic.X == 100 && pic.Y == 100;
            ed2.TestResize("T", 0, -50, false); bool side = Near(pic.Height, 100) && Near(pic.Width, 200) && Near(pic.Y, 50) && Near(pic.X, 50);
            ed2.TestTypeSize(pic, 300, null); bool typedW = Near(pic.Width, 300) && Near(pic.Height, 150);
            ed2.TestTypeSize(pic, null, 30); bool typedH = Near(pic.Height, 30) && Near(pic.Width, 60);
            ed2.TestUndo();
            var back = ed2.TestFindObject("Pic")!;
            return corner && side && typedW && typedH && Near(back.Width, 300) && Near(back.Height, 150);
        });
        t.Test("size: Shift keeps proportions for one drag even with the option off; off + no Shift resizes freely; no picture keeps its own ratio", () =>
        {
            var (p, layer, ed) = Editor("S2", (_, l) => l.Objects.Add(new GameObjectData { Id = 10, Name = "Box", X = 0, Y = 0, Width = 30, Height = 10 }));
            var b = ed.TestFindObject("Box")!;
            ed.TestSelect(b);
            ed.TestResize("BR", 30, 0, true); bool shift = Near(b.Width, 60) && Near(b.Height, 20);
            ed.TestResize("BR", 0, 30, false); bool free = Near(b.Width, 60) && Near(b.Height, 50);
            ed.TestSetKeepAspect(b, true); // remembers 60:50 (no picture)
            ed.TestTypeSize(b, 120, null);
            return shift && free && !b.Locked && Near(b.Height, 100) && b.KeepAspect;
        });
        t.Test("size: Original size from a PNG and from an animation's first frame; disabled without a picture; locked objects unchanged; undo", () =>
        {
            var (p, layer, _) = Editor("S3");
            var still = new GameObjectData { Id = 10, Name = "Still", X = 10, Y = 10, Width = 100, Height = 100, SpritePath = SavePng(p, 24, 40, "still") };
            var anim = new GameObjectData { Id = 11, Name = "Anim", X = 10, Y = 10, Width = 100, Height = 100 };
            anim.Animations.Add(new SpriteAnimation { Name = "idle", Frames = { SavePng(p, 36, 18, "f0"), SavePng(p, 50, 50, "f1") } });
            var plain = new GameObjectData { Id = 12, Name = "Plain", Width = 100, Height = 100 };
            var locked = new GameObjectData { Id = 13, Name = "Locked", Width = 100, Height = 100, SpritePath = still.SpritePath, Locked = true };
            layer.Objects.AddRange(new[] { still, anim, plain, locked });
            var ed = new MainWindow(p);
            GameObjectData F(string n) => ed.TestFindObject(n)!;
            ed.TestSelect(F("Still")); bool s1 = ed.TestClickOriginalSize() && F("Still").Width == 24 && F("Still").Height == 40 && F("Still").X == 10;
            ed.TestSelect(F("Anim")); bool s2 = ed.TestClickOriginalSize() && F("Anim").Width == 36 && F("Anim").Height == 18;
            ed.TestSelect(F("Plain")); bool s3 = !ed.TestClickOriginalSize() && F("Plain").Width == 100;
            ed.TestSelect(F("Locked")); bool s4 = !ed.TestClickOriginalSize() && F("Locked").Width == 100;
            ed.TestSetKeepAspect(F("Locked"), true); ed.TestTypeSize(F("Locked"), 10, null);
            bool s5 = !F("Locked").KeepAspect && F("Locked").Width == 100;
            ed.TestUndo(); ed.TestUndo(); // the locked typed size (a no-op step), then Anim's original size
            bool undone = F("Anim").Width == 100 && F("Anim").Height == 100;
            return s1 && s2 && s3 && s4 && s5 && undone;
        });
    }
}
