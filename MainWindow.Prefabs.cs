using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyEngine
{
    // Prefab copies remember their prefab (GameObjectData.Prefab). A copy with "Changes here change every copy" ticked
    // (PrefabAll) passes its changes — a behavior's setting, picture, size, physics… — to the prefab and to the other
    // ticked copies in every scene, in place. Unticked copies keep their own changes. What belongs to one placement stays
    // its own: name, position, Z, lock, on/off. "Unlink" forgets the prefab.
    public partial class MainWindow
    {
        private static readonly string[] PlacementKeys = { "Id", "Name", "X", "Y", "Z", "Locked", "Active", "Prefab", "PrefabAll" };
        private static readonly System.Reflection.PropertyInfo[] SharedProps = typeof(GameObjectData).GetProperties()
            .Where(p => p.CanRead && p.CanWrite && !PlacementKeys.Contains(p.Name)).ToArray();

        /// <summary>Everything a prefab's copies share, as JSON (placement fields left out).</summary>
        internal static string PrefabShared(GameObjectData o)
        {
            var node = JsonSerializer.SerializeToNode(o)!.AsObject();
            foreach (var k in PlacementKeys) node.Remove(k);
            return node.ToJsonString();
        }

        // gives dst the shared part (a fresh copy of it), keeping dst's own placement — in place: the editor, the
        // selection and undo keep pointing at the same object
        private static void CopyShared(string shared, GameObjectData dst)
        {
            var src = JsonSerializer.Deserialize<GameObjectData>(shared)!;
            foreach (var p in SharedProps) p.SetValue(dst, p.GetValue(src));
        }

        /// <summary>After a change: a selected copy with "change every copy" ticked that now differs from its prefab
        /// passes the change to the prefab and to the other ticked copies. Returns how many other copies changed.</summary>
        internal int SyncPrefabCopies()
        {
            int changed = 0; bool here = false;
            foreach (var obj in _selection.ToList())
            {
                if (!obj.PrefabAll || string.IsNullOrEmpty(obj.Prefab) || _project.Prefabs.FirstOrDefault(p => p.Name == obj.Prefab) is not PrefabDef pf) continue;
                var shared = PrefabShared(obj);
                if (shared == PrefabShared(pf.Template)) continue;
                CopyShared(shared, pf.Template);
                foreach (var sc in _project.Scenes)
                    foreach (var layer in sc.Layers)
                        foreach (var o in layer.Objects)
                        {
                            if (ReferenceEquals(o, obj) || o.Prefab != obj.Prefab || !o.PrefabAll || o.Locked || layer.Locked || sc.Locked || PrefabShared(o) == shared) continue;
                            CopyShared(shared, o);
                            changed++;
                            if (sc == _scene) here = true;
                        }
            }
            if (here) RenderScene();
            return changed;
        }

        /// <summary>Projects from before the link: an object named like a prefab ("Player", "Player_2") of the same type is a copy of it.</summary>
        internal static int LinkOldPrefabCopies(Project p)
        {
            int n = 0;
            foreach (var pf in p.Prefabs)
            {
                var name = new Regex("^" + Regex.Escape(pf.Name) + @"(_\d+)?$");
                foreach (var o in p.Scenes.SelectMany(s => s.Layers).SelectMany(l => l.Objects))
                    if (o.Prefab == null && o.Type == pf.Template.Type && name.IsMatch(o.Name)) { o.Prefab = pf.Name; n++; }
            }
            return n;
        }

        /// <summary>The scene point in the middle of what the editor shows now (the scene's middle when nothing is shown yet).</summary>
        internal Point ViewCenter()
        {
            var whole = new Point(_scene.Width / 2, _scene.Height / 2);
            if (!SceneScroll.IsLoaded || SceneScroll.ViewportWidth <= 0 || PresentationSource.FromVisual(SceneCanvas) == null) return whole;
            try { return SceneScroll.TranslatePoint(new Point(SceneScroll.ViewportWidth / 2, SceneScroll.ViewportHeight / 2), SceneCanvas); }
            catch { return whole; }
        }

        internal GameObjectData TestAddPrefab(string name) { AddPrefabInstance(_project.Prefabs.First(p => p.Name == name)); return _selected!; }
        internal void TestChanged() => Dirty();
        internal void TestCloseUnsaved() { _dirty = false; Close(); } // a shown test editor closes without asking to save

        // "🔗 A copy of the prefab X" + [ ] Changes here change every copy + Unlink
        private void PrefabLinkRow(GameObjectData obj)
        {
            if (string.IsNullOrEmpty(obj.Prefab) || !_project.Prefabs.Any(p => p.Name == obj.Prefab)) return;
            var box = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            var row = new DockPanel();
            var unlink = new Button { Content = L.T("Unlink"), Style = (Style)FindResource("CardButton"), Height = 26, Padding = new Thickness(8, 0, 8, 0), ToolTip = L.T("Make this one independent: it stops being a copy of the prefab") };
            unlink.Click += (s, e) => { PushUndo(); obj.Prefab = null; obj.PrefabAll = false; Dirty(); BuildInspector(obj); };
            DockPanel.SetDock(unlink, Dock.Right);
            row.Children.Add(unlink);
            var text = new TextBlock { Text = L.F("🔗 A copy of the prefab \"{0}\"", obj.Prefab),
                                       TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = (SolidColorBrush)FindResource("TextDim"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            L.SetSkip(text, true); // already translated, holds the prefab's name
            row.Children.Add(text);
            box.Children.Add(row);
            var all = new CheckBox { Content = L.T("Changes here change every copy"), IsChecked = obj.PrefabAll, Margin = new Thickness(0, 4, 0, 0),
                                     ToolTip = L.T("Ticked: what you change on this object also changes the prefab and the other copies that have this ticked. Off: changes stay on this one only.") };
            all.Click += (s, e) => { PushUndo(); obj.PrefabAll = all.IsChecked == true; Dirty(); };
            box.Children.Add(all);
            InspectorRoot.Children.Add(box);
        }
    }
}
