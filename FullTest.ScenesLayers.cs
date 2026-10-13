using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Controls;

namespace EasyEngine;

// Scenes and layers: Duplicate scene (deep copy, unique name and ids, rules untouched), layer order = draw order (editor
// and game), 🔒 locked layers (not pickable, not editable, the AI skips them), layer opacity (editor and game), layers and
// objects off in the game (not loaded, dimmed in the editor, the "setactive" rule brings them in / takes them out),
// duplicate / rename layer, old projects loading with the new keys' defaults, and the save/load round trip.
public partial class App
{
    partial void FeatureTestsScenesLayers(FullTestKit t)
    {
        static bool Near(double a, double b, double tol = 0.01) => Math.Abs(a - b) <= tol;
        static GameObjectData Box(int id, string name, double x = 0, double y = 0) => new() { Id = id, Name = name, X = x, Y = y, Width = 40, Height = 40 };
        (Project p, Scene s, MainWindow ed) Editor(string tag, Action<Project, Scene>? fill = null)
        {
            var dir = Path.Combine(Path.GetTempPath(), "EE_Full_Layers_" + tag + "_" + Guid.NewGuid().ToString("N")[..6]);
            var p = ProjectIO.CreateNew("SL", dir);
            var s = p.GetStartScene();
            fill?.Invoke(p, s);
            return (p, s, new MainWindow(p));
        }
        static Layer L(Scene s, LayerKind k) => s.Layers.First(l => l.Kind == k);
        PlayWindow Play(Project p, string scene)
        {
            var pw = new PlayWindow(p, scene); t.Sims.Add(pw);
            pw.TSeed(1); pw.TLoad();
            return pw;
        }

        // ---------------- model + files
        t.Test("layers: an old project loads with layers on, unlocked and opaque, objects active; the new keys save and load", () =>
        {
            var oldLayer = JsonSerializer.Deserialize<Layer>("{\"Name\":\"L\",\"Objects\":[{\"Name\":\"A\"}]}")!;
            bool defaults = oldLayer.Active && !oldLayer.Locked && oldLayer.Opacity == 1 && oldLayer.Visible && oldLayer.Objects[0].Active;
            var (p, s, _) = Editor("IO");
            var b = L(s, LayerKind.Base);
            b.Locked = true; b.Opacity = 0.4; b.Active = false; b.Objects[0].Active = false;
            ProjectIO.Save(p);
            var back = ProjectIO.Load(p.FilePath!);
            var bb = back.GetStartScene().Layers.First(l => l.Kind == LayerKind.Base);
            return defaults && bb.Locked && Near(bb.Opacity, 0.4) && !bb.Active && !bb.Objects[0].Active && bb.Objects[1].Active
                   && back.GetStartScene().Layers.First(l => l.Kind == LayerKind.Background).Active;
        });

        // ---------------- duplicate scene
        t.Test("scenes: Duplicate scene = a deep copy right after it, unique name, new unique ids, rules untouched, original unchanged", () =>
        {
            const string rule = "[{\"when\":\"touch\",\"name\":\"Player\",\"do\":\"goscene\",\"scene\":\"Level 1\"}]";
            var (p, s, ed) = Editor("D1", (proj, sc) =>
            {
                sc.Name = proj.StartScene = "Level 1";
                var door = Box(30, "Door", 300, 100);
                door.Behaviors.Add(new BehaviorData { Kind = BehaviorKind.Custom, Texts = { ["script"] = rule } });
                L(sc, LayerKind.Base).Objects.Add(door);
                proj.Scenes.Add(new Scene { Name = "Level 2", Layers = { new Layer { Name = "Base", Objects = { Box(40, "Other") } } } });
            });
            ed.TestDuplicateScene();
            var copy = ed.TestScene;
            bool placed = p.Scenes.IndexOf(copy) == 1 && copy.Name == "Level 1 copy" && p.Scenes[2].Name == "Level 2" && p.StartScene == "Level 1";
            var ids = p.Scenes.SelectMany(x => x.Layers).SelectMany(l => l.Objects).Select(o => o.Id).ToList();
            bool unique = ids.Count == ids.Distinct().Count() && ids.All(i => i > 0);
            var cDoor = copy.Layers.SelectMany(l => l.Objects).First(o => o.Name == "Door");
            bool same = copy.Layers.Count == s.Layers.Count && cDoor.X == 300 && cDoor.Behaviors[0].Texts["script"] == rule
                        && copy.Layers.SelectMany(l => l.Objects).Count() == s.Layers.SelectMany(l => l.Objects).Count();
            cDoor.X = 999; copy.Layers[0].Name = "Changed";
            var oDoor = s.Layers.SelectMany(l => l.Objects).First(o => o.Name == "Door");
            bool deep = oDoor.X == 300 && s.Layers[0].Name != "Changed" && !ReferenceEquals(oDoor.Behaviors[0], cDoor.Behaviors[0]);
            var second = SceneTools.DuplicateScene(p, s);
            return placed && unique && same && deep && second.Name == "Level 1 copy 2" && p.Scenes.IndexOf(second) == 1;
        });

        // ---------------- order
        t.Test("layers: Bring forward / Send backward = the draw order on the canvas and in the game; undo; the menu offers only possible moves", () =>
        {
            var (p, s, ed) = Editor("O1", (_, sc) => L(sc, LayerKind.Background).Objects.Add(Box(50, "Back")));
            var bg = L(s, LayerKind.Background);
            var player = ed.TestFindObject("Player")!; var back = ed.TestFindObject("Back")!;
            bool before = ed.TestCanvasIndex(back) < ed.TestCanvasIndex(player);
            var firstMenu = ed.TestLayerMenu(s.Layers[0]); var lastMenu = ed.TestLayerMenu(s.Layers[^1]);
            bool menus = firstMenu.Contains("Bring forward (draw in front)") && !firstMenu.Contains("Send backward (draw behind)")
                         && lastMenu.Contains("Send backward (draw behind)") && !lastMenu.Contains("Bring forward (draw in front)")
                         && firstMenu.Contains("Duplicate layer") && firstMenu.Contains("Opacity…") && firstMenu.Contains("Delete layer");
            int bi = s.Layers.IndexOf(bg);
            ed.TestMoveLayer(bg, +1);
            bool moved = ed.TestScene.Layers.IndexOf(bg) == bi + 1 && ed.TestCanvasIndex(back) > ed.TestCanvasIndex(player);
            var game = Play(p, s.Name);
            bool inGame = game.TVisualIndexById(50) > game.TVisualIndexById(player.Id);
            ed.TestMoveLayer(ed.TestScene.Layers[0], -1); // already at the back: nothing (no undo step)
            ed.TestUndo();
            var undone = ed.TestScene.Layers.Select(l => l.Name).ToList();
            return before && menus && moved && inGame && undone.IndexOf("Background") < undone.IndexOf("Base");
        });

        // ---------------- lock
        t.Test("layers: a locked layer's objects can't be picked, dragged, nudged or deleted, nothing is added to it; undo unlocks", () =>
        {
            var (p, s, ed) = Editor("K1", (_, sc) => L(sc, LayerKind.Base).Objects.Add(Box(60, "Rock", 100, 100)));
            var b = L(s, LayerKind.Base);
            ed.TestSetLayerLocked(b, true);
            var rock = ed.TestFindObject("Rock")!;
            bool pick = !ed.TestPickable(rock), drag = !ed.TestPressOn(rock);
            ed.TestSelect(rock); ed.TestNudge(5, 0, false); ed.TestDeleteSelected();
            bool kept = ed.TestFindObject("Rock") is { X: 100 } && !ed.TestHasHandles;
            int count = b.Objects.Count; ed.TestAddEmpty();
            bool notAdded = b.Objects.Count == count;
            var row = ed.TestLayerRowTexts(b);
            ed.TestUndo();
            return pick && drag && kept && notAdded && row.Contains("🔒") && !L(ed.TestScene, LayerKind.Base).Locked
                   && ed.TestPickable(ed.TestFindObject("Rock")!);
        });
        t.Test("layers: the AI never edits, deletes or adds to a locked layer (noted in its summary); the prompt marks it LOCKED", () =>
        {
            var (p, s, ed) = Editor("K2", (_, sc) => L(sc, LayerKind.Base).Objects.Add(Box(61, "Rock", 100, 100)));
            var b = L(s, LayerKind.Base);
            ed.TestSetLayerLocked(b, true);
            var (ok, summary) = ed.TestApplySpec("{\"edit\":[{\"name\":\"Rock\",\"x\":500,\"color\":\"#FF0000\"}],\"layers\":[{\"name\":\"Base\",\"visible\":false}]}");
            ed.TestApplySpec("{\"delete\":[\"Rock\"]}");
            ed.TestApplySpec("{\"create\":[{\"name\":\"Crate\",\"layer\":\"Base\",\"behaviors\":[{\"kind\":\"Pushable\"}]}]}", "add a new crate");
            var rock = ed.TestFindObject("Rock");
            var crate = ed.TestFindObject("Crate");
            string prompt = ed.TestBuildPrompt();
            return rock is { X: 100 } && rock.Color != "#FF0000" && b.Visible && summary.Contains("Rock") && summary.Contains("locked")
                   && crate != null && !b.Objects.Contains(crate)
                   && prompt.Contains("Base(LOCKED)") && prompt.Contains("\"Rock\"") && prompt.Contains("LOCKED = never change");
        });

        // ---------------- opacity
        t.Test("layers: opacity multiplies the objects' own, on the canvas and in the game (tilemap cells too)", () =>
        {
            var (p, s, ed) = Editor("A1", (_, sc) =>
            {
                var g = Box(70, "Ghost"); g.Opacity = 0.8;
                L(sc, LayerKind.Base).Objects.Add(g);
            });
            var b = L(s, LayerKind.Base);
            ed.TestSetLayerOpacity(b, 0.5);
            bool canvas = Near(ed.TestCanvasOpacity(ed.TestFindObject("Ghost")!), 0.4) && ed.TestLayerRowTexts(b).Any(x => x.Contains("50%"));
            var game = t.Sim("[]", PhysicsMode.None, (proj, sc) =>
            {
                var l = L(sc, LayerKind.Base); l.Opacity = 0.5;
                var g = Box(71, "Ghost"); g.Opacity = 0.8; l.Objects.Add(g);
                var tm = new BehaviorData { Kind = BehaviorKind.Tilemap };
                tm.Texts["palette"] = "[{\"color\":\"#7A5230\",\"solid\":true}]";
                tm.Texts["data"] = "{\"cols\":2,\"rows\":1,\"cells\":[1,1]}";
                tm.Nums["tileSize"] = 40;
                l.Objects.Add(new GameObjectData { Id = 72, Name = "Tiles", X = 1000, Y = 1000, Width = 80, Height = 40, Behaviors = { tm } });
                L(sc, LayerKind.Background).Objects.Add(Box(73, "Solid"));
            });
            game.TStep();
            return canvas && Near(game.TLayerAlpha("Ghost"), 0.5) && Near(game.TOpacity("Ghost"), 0.8) && Near(game.TTileOpacity("Tiles"), 0.5)
                   && Near(game.TLayerAlpha("Solid"), 1);
        });

        // ---------------- on / off in the game
        t.Test("layers: a layer or object that is off isn't in the game (the editor's eye is ignored); dimmed + dashed in the editor; the prompt says OFF", () =>
        {
            var game = t.Sim("[]", PhysicsMode.None, (proj, sc) =>
            {
                var off = new Layer { Name = "Secret", Kind = LayerKind.Custom, Active = false, Objects = { Box(80, "Hidden1") } };
                sc.Layers.Add(off);
                var bo = Box(81, "Sleeper"); bo.Active = false;
                L(sc, LayerKind.Base).Objects.Add(bo);
                var eye = new Layer { Name = "EyeOff", Kind = LayerKind.Custom, Visible = false, Objects = { Box(82, "Seen") } };
                sc.Layers.Add(eye);
            });
            game.TStep();
            bool play = !game.TExists("Hidden1") && !game.TExists("Sleeper") && game.TExists("Seen") && game.TExists("A");
            var (p, s, ed) = Editor("F1", (_, sc) => sc.Layers.Add(new Layer { Name = "Secret", Kind = LayerKind.Custom, Objects = { Box(83, "Hidden2") } }));
            var secret = s.Layers.First(l => l.Name == "Secret");
            ed.TestSetLayerActive(secret, false);
            var h = ed.TestFindObject("Hidden2")!;
            bool dim = ed.TestOffMark(h) && Near(ed.TestCanvasOpacity(h), 0.35) && ed.TestLayerRowTexts(secret).Contains("▶ off");
            string prompt = ed.TestBuildPrompt();
            ed.TestUndo();
            return play && dim && prompt.Contains("Secret(OFF)") && prompt.Contains("\"Hidden2\"") && prompt.Contains(" OFF")
                   && ed.TestScene.Layers.First(l => l.Name == "Secret").Active;
        });
        t.Test("objects: \"Active — exists in the game\" in the inspector and the list's ▶ toggle; dimmed; undo; a locked object can't be switched", () =>
        {
            var (p, s, ed) = Editor("F2", (_, sc) => { L(sc, LayerKind.Base).Objects.Add(Box(90, "Lamp")); var k = Box(91, "Stone"); k.Locked = true; L(sc, LayerKind.Base).Objects.Add(k); });
            var lamp = ed.TestFindObject("Lamp")!;
            ed.TestSelect(lamp);
            bool box = ed.TestInspectorItems().OfType<CheckBox>().Any(c => (c.Content as string) == "Active — exists in the game" && c.IsChecked == true);
            ed.TestSetObjectsActive(lamp, false);
            lamp = ed.TestFindObject("Lamp")!;
            bool off = !lamp.Active && ed.TestOffMark(lamp) && ed.TestInspectorItems().OfType<CheckBox>().Any(c => c.IsChecked == false);
            ed.TestSetObjectsActive(ed.TestFindObject("Stone")!, false);
            bool stone = ed.TestFindObject("Stone")!.Active;
            ed.TestUndo();
            return box && off && stone && ed.TestFindObject("Lamp")!.Active && !ed.TestOffMark(ed.TestFindObject("Lamp")!);
        });

        // ---------------- the "setactive" rule
        t.Test("setactive: an object turns on (in its layer's place in the draw order) and off; a whole layer on and off, its tiles and colliders too", () =>
        {
            const string rules = "[{'when':'ifvar','var':'g','op':'==','value':1,'do':'setactive','target':'Bridge','on':'on'},"
                               + "{'when':'ifvar','var':'g','op':'==','value':2,'do':'setactive','target':'Bridge','on':'off'},"
                               + "{'when':'ifvar','var':'g','op':'==','value':3,'do':'setactive','layer':'Secret','on':'on'},"
                               + "{'when':'ifvar','var':'g','op':'==','value':4,'do':'setactive','layer':'secret','on':false}]";
            var game = t.Sim(rules.Replace('\'', '"'), PhysicsMode.None, (proj, sc) =>
            {
                var bas = L(sc, LayerKind.Base);
                var bridge = Box(101, "Bridge", 500, 500); bridge.Active = false; bas.Objects.Add(bridge);
                bas.Objects.Add(Box(102, "C", 600, 500));
                sc.Layers.Insert(sc.Layers.IndexOf(bas) + 1, new Layer { Name = "Fore", Kind = LayerKind.Custom, Objects = { Box(103, "F") } });
                var tm = new BehaviorData { Kind = BehaviorKind.Tilemap };
                tm.Texts["palette"] = "[{\"color\":\"#7A5230\",\"solid\":true}]";
                tm.Texts["data"] = "{\"cols\":2,\"rows\":1,\"cells\":[1,1]}";
                tm.Nums["tileSize"] = 40;
                sc.Layers.Add(new Layer { Name = "Secret", Kind = LayerKind.Custom, Active = false, Objects = { Box(104, "S1"), new GameObjectData { Id = 105, Name = "Tiles", X = 1000, Y = 1000, Width = 80, Height = 40, Behaviors = { tm } } } });
            });
            game.TStep();
            bool start = !game.TExists("Bridge") && !game.TExists("S1") && !game.TSolids().Contains("(1000,1000,80x40)");
            game.TSetVar("g", 1); game.TStep(); game.TStep();
            bool on = game.TAlive("Bridge") == 1 && game.TVisualIndexById(101) > game.TVisualIndexById(102) && game.TVisualIndexById(101) < game.TVisualIndexById(103);
            game.TSetVar("g", 2); game.TStep(); game.TCleanup();
            bool offObj = !game.TExists("Bridge");
            game.TSetVar("g", 3); game.TStep(); game.TStep();
            bool layerOn = game.TAlive("S1") == 1 && game.TTileRects("Tiles") == 2 && game.TSolids().Contains("(1000,1000,80x40)")
                           && game.TVisualIndexById(104) > game.TVisualIndexById(103);
            game.TSetVar("g", 4); game.TStep(); game.TCleanup();
            bool layerOff = !game.TExists("S1") && !game.TExists("Tiles") && !game.TSolids().Contains("(1000,1000,80x40)") && game.TExists("F");
            return start && on && offObj && layerOn && layerOff;
        });
        t.Test("setactive: \"self\" off takes it away; a bare block does nothing; an unknown name or layer is only a console warning", () =>
        {
            var self = t.Sim("[{\"when\":\"start\",\"do\":\"setactive\",\"on\":\"off\"}]", PhysicsMode.None, null); self.TSteps(2);
            var bare = t.Sim("[{\"when\":\"start\",\"do\":\"setactive\"}]", PhysicsMode.None, null); bare.TSteps(2);
            var none = t.Sim("[{\"when\":\"start\",\"do\":\"setactive\",\"target\":\"Nope\"},{\"when\":\"start\",\"do\":\"setactive\",\"layer\":\"Nope\",\"on\":\"off\"}]", PhysicsMode.None, null); none.TSteps(2);
            return self.TDead("A") && bare.TExists("A") && none.TExists("A") && none.TLogHas("no object \"Nope\"") && none.TLogHas("no layer \"Nope\"");
        });
        t.Test("setactive: the AI's words become it (deactivate / activate / turn off, \"active\":false, \"object\")", () =>
        {
            var notes = new List<string>();
            string Fix(string r) => AiSpecFix.Rules(new[] { r.Replace('\'', '"') }, notes);
            var a = Fix("{'when':'start','do':'deactivate','object':'Bridge'}");
            var b = Fix("{'when':'start','do':'activate','layer':'Secret'}");
            var c = Fix("{'when':'start','do':'setactive','target':'Bridge','active':false}");
            var d = Fix("{'when':'start','do':'turnOff','target':'Door'}");
            return a.Contains("\"do\":\"setactive\"") && a.Contains("\"target\":\"Bridge\"") && a.Contains("\"on\":\"off\"")
                   && b.Contains("\"layer\":\"Secret\"") && b.Contains("\"on\":\"on\"") && c.Contains("\"on\":false") && d.Contains("\"on\":\"off\"") && notes.Count == 0;
        });

        // ---------------- duplicate / rename layer
        t.Test("layers: Duplicate layer = a copy in front of it (\"copy\" names, new ids, a Base copy becomes Custom, unlocked); undo", () =>
        {
            var (p, s, ed) = Editor("L1");
            var b = L(s, LayerKind.Base);
            b.Locked = true;
            int count = s.Layers.Count, idx = s.Layers.IndexOf(b);
            var names = b.Objects.Select(o => o.Name).ToList();
            ed.TestDuplicateLayer(b);
            var copy = ed.TestScene.Layers[idx + 1];
            var ids = ed.TestScene.Layers.SelectMany(l => l.Objects).Select(o => o.Id).ToList();
            bool ok = ed.TestScene.Layers.Count == count + 1 && copy.Name == "Base copy" && copy.Kind == LayerKind.Custom && !copy.Locked
                      && copy.Objects.Select(o => o.Name).SequenceEqual(names.Select(n => n + " copy")) && ids.Count == ids.Distinct().Count()
                      && ReferenceEquals(ed.TestActiveLayer, copy);
            ed.TestDuplicateLayer(b);
            bool second = ed.TestScene.Layers.Any(l => l.Name == "Base copy 2");
            ed.TestUndo(); ed.TestUndo();
            return ok && second && ed.TestScene.Layers.Count == count;
        });
        t.Test("scenes: a locked scene can't be changed — nothing picked, moved, deleted, added, duplicated or undone, and the AI isn't asked; it saves", () =>
        {
            var (p, s, ed) = Editor("SL", (_, sc) => L(sc, LayerKind.Base).Objects.Add(Box(70, "Tree", 100, 100)));
            ed.TestToggleSceneLock();
            var tree = ed.TestFindObject("Tree")!;
            bool pick = !ed.TestPickable(tree), drag = !ed.TestPressOn(tree);
            int before = L(ed.TestScene, LayerKind.Base).Objects.Count;
            ed.TestSelect(tree); ed.TestNudge(5, 0, false); ed.TestDeleteSelected(); ed.TestDuplicate();
            var b = L(ed.TestScene, LayerKind.Base);
            ed.TestAddEmpty(); ed.TestUndo();
            bool kept = ed.TestFindObject("Tree") is { X: 100 } && b.Objects.Count == before;
            var (ok, summary, reply) = ed.EvalBuild("make the Tree red").GetAwaiter().GetResult();
            bool ai = !ok && reply == "" && summary.Contains("locked") && ed.TestFindObject("Tree")!.Color != "#FF0000";
            ProjectIO.Save(p); bool saved = ProjectIO.Load(p.FilePath!).GetStartScene().Locked;
            ed.TestToggleSceneLock();
            bool all = pick && drag && kept && ai && saved && !ed.TestScene.Locked && ed.TestPickable(ed.TestFindObject("Tree")!);
            if (!all) throw new Exception($"pick={pick} drag={drag} kept={kept} (x={ed.TestFindObject("Tree")?.X}, n={b.Objects.Count}) ai={ai} ({summary}) saved={saved}");
            return all;
        });
    }
}
