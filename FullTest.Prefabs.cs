using System.IO;
using System.Linq;

namespace EasyEngine;

// Prefab copies stay linked: changing one changes the prefab and every copy in every scene; placement stays each copy's own.
public partial class App
{
    partial void FeatureTestsPrefabs(FullTestKit t)
    {
        t.Test("prefabs: changing a copy (jump 640 → 800) changes the prefab and the copies in other scenes; new copies come with 800; names and places stay", () =>
        {
            var p = ProjectIO.CreateNew("PrefabLink", Path.Combine(Path.GetTempPath(), "EE_Full_Prefab_" + System.Guid.NewGuid().ToString("N")[..6]));
            p.Prefabs = PrefabFactory.Defaults();
            p.Scenes.Add(new Scene { Name = "Two", Layers = { new Layer { Name = "Main", Kind = LayerKind.Base } } });
            var ed = new MainWindow(p);
            try
            {
                var pf = p.Prefabs.First(x => x.Template.Behaviors.Any(b => b.Kind == BehaviorKind.Platformer));
                var a = ed.TestAddPrefab(pf.Name); a.X = 111;
                ed.TestSwitchScene(p.Scenes[1]);
                var b = ed.TestAddPrefab(pf.Name); b.X = 222;
                ed.TestSwitchScene(p.Scenes[0]);
                ed.TestSelect(a);
                a.Behaviors.First(x => x.Kind == BehaviorKind.Platformer).Nums["jump"] = 800;
                ed.TestChanged();
                double Jump(GameObjectData o) => o.Behaviors.First(x => x.Kind == BehaviorKind.Platformer).Num("jump", 0);
                var b2 = p.Scenes[1].Layers.SelectMany(l => l.Objects).First(o => o.Prefab == pf.Name);
                ed.TestSwitchScene(p.Scenes[1]);
                var c = ed.TestAddPrefab(pf.Name);
                bool linked = Jump(pf.Template) == 800 && Jump(b2) == 800 && b2.X == 222 && b2.Name == b.Name && Jump(c) == 800;
                // an unlinked copy keeps its own changes
                c.Prefab = null; ed.TestSelect(c);
                c.Behaviors.First(x => x.Kind == BehaviorKind.Platformer).Nums["jump"] = 300; ed.TestChanged();
                return linked && Jump(pf.Template) == 800 && Jump(b2) == 800;
            }
            finally { ed.TestCloseUnsaved(); try { Directory.Delete(p.FolderPath!, true); } catch { } }
        });
        t.Test("prefabs: an old project's objects named like a prefab (Player, Player_2) of its type become its copies; others don't", () =>
        {
            var p = new Project { Prefabs = PrefabFactory.Defaults() };
            var pf = p.Prefabs[0];
            var s = new Scene(); var l = new Layer(); s.Layers.Add(l); p.Scenes.Add(s);
            l.Objects.Add(new GameObjectData { Name = pf.Name, Type = pf.Template.Type });
            l.Objects.Add(new GameObjectData { Name = pf.Name + "_2", Type = pf.Template.Type });
            l.Objects.Add(new GameObjectData { Name = pf.Name + " boss", Type = pf.Template.Type });
            return EasyEngine.MainWindow.LinkOldPrefabCopies(p) == 2 && l.Objects[2].Prefab == null && l.Objects[0].Prefab == pf.Name;
        });
        t.Test("chat: the AI's Markdown shows as formatting — no asterisks; bold, lists and code are real, names with _ stay whole", () =>
        {
            var doc = ChatMarkdown.ToDocument("## Steps\n6. **(Optional) Keep the player** inside\n   - Click **'＋ Add behavior…'** → choose **🎥 Follow Camera**.\n- Use `A-D` and *Space* on my_player_2",System.Windows.Media.Brushes.White);
            var plain = ChatMarkdown.Plain(doc);
            bool bold = ChatMarkdown.Inlines("a **b** c").Exists(i => i is System.Windows.Documents.Bold);
            return !plain.Contains('*') && !plain.Contains('#') && !plain.Contains('`') && plain.Contains("• Use A-D and Space on my_player_2") && plain.Contains("6. (Optional) Keep the player inside") && bold;
        });
        t.Test("new object: it appears in the middle of the part of the scene being looked at, not the scene's middle", () =>
        {
            var p = ProjectIO.CreateNew("ViewMid", Path.Combine(Path.GetTempPath(), "EE_Full_ViewMid_" + System.Guid.NewGuid().ToString("N")[..6]));
            p.Prefabs = PrefabFactory.Defaults();
            p.GetStartScene().Width = 6000; p.GetStartScene().Height = 4000;
            var ed = new MainWindow(p) { ShowActivated = false, Left = -20000, Width = 1200, Height = 800 };
            try
            {
                ed.Show(); ed.TestSwitchScene(p.GetStartScene()); ed.UpdateLayout();
                ed.SceneScroll.ScrollToHorizontalOffset(ed.SceneScroll.ScrollableWidth); ed.SceneScroll.ScrollToVerticalOffset(0); ed.UpdateLayout();
                var mid = ed.ViewCenter();
                var o = ed.TestAddPrefab(p.Prefabs[0].Name);
                double cx = o.X + o.Width / 2, cy = o.Y + o.Height / 2;
                return System.Math.Abs(mid.X - 3000) > 300 && System.Math.Abs(cx - mid.X) < 2 && (System.Math.Abs(cy - mid.Y) < 2 || o.Y == 0);
            }
            finally { ed.TestCloseUnsaved(); try { Directory.Delete(p.FolderPath!, true); } catch { } }
        });
        t.Test("prefabs: renaming a prefab keeps its copies linked", () =>
        {
            var p = new Project { Prefabs = PrefabFactory.Defaults() };
            var pf = p.Prefabs[0];
            var s = new Scene(); var l = new Layer(); s.Layers.Add(l); p.Scenes.Add(s);
            l.Objects.Add(new GameObjectData { Name = "x", Prefab = pf.Name });
            PrefabRename.Apply(p, pf, "Hero9");
            return l.Objects[0].Prefab == "Hero9";
        });
    }
}
