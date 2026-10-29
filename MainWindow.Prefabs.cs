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
    // Prefab copies stay linked to their prefab (GameObjectData.Prefab): changing one copy — a behavior's setting, its
    // picture, size, physics… — changes the prefab and every other copy in every scene. What belongs to one placement
    // stays its own: name, position, Z, lock, on/off. "Unlink" makes a copy independent again.
    public partial class MainWindow
    {
        private static readonly string[] PlacementKeys = { "Id", "Name", "X", "Y", "Z", "Locked", "Active", "Prefab" };

        /// <summary>Everything a prefab's copies share, as JSON (placement fields left out).</summary>
        internal static string PrefabShared(GameObjectData o)
        {
            var node = JsonSerializer.SerializeToNode(o)!.AsObject();
            foreach (var k in PlacementKeys) node.Remove(k);
            return node.ToJsonString();
        }

        // a copy of the shared part of src, with dst's own placement
        private static GameObjectData WithPlacement(string shared, GameObjectData place)
        {
            var o = JsonSerializer.Deserialize<GameObjectData>(shared)!;
            o.Id = place.Id; o.Name = place.Name; o.X = place.X; o.Y = place.Y; o.Z = place.Z;
            o.Locked = place.Locked; o.Active = place.Active; o.Prefab = place.Prefab;
            return o;
        }

        /// <summary>After a change: a selected copy that now differs from its prefab passes the change to the prefab and
        /// to every other copy. Returns how many other copies changed.</summary>
        internal int SyncPrefabCopies()
        {
            int changed = 0; bool here = false;
            foreach (var obj in _selection.ToList())
            {
                if (string.IsNullOrEmpty(obj.Prefab) || _project.Prefabs.FirstOrDefault(p => p.Name == obj.Prefab) is not PrefabDef pf) continue;
                var shared = PrefabShared(obj);
                if (shared == PrefabShared(pf.Template)) continue;
                pf.Template = WithPlacement(shared, pf.Template);
                foreach (var sc in _project.Scenes)
                    foreach (var layer in sc.Layers)
                        for (int i = 0; i < layer.Objects.Count; i++)
                        {
                            var o = layer.Objects[i];
                            if (ReferenceEquals(o, obj) || o.Prefab != obj.Prefab || o.Locked || layer.Locked || sc.Locked || PrefabShared(o) == shared) continue;
                            layer.Objects[i] = WithPlacement(shared, o);
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

        // "🔗 A copy of the prefab X — changes apply to every copy" + Unlink
        private void PrefabLinkRow(GameObjectData obj)
        {
            if (string.IsNullOrEmpty(obj.Prefab) || !_project.Prefabs.Any(p => p.Name == obj.Prefab)) return;
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var unlink = new Button { Content = L.T("Unlink"), Style = (Style)FindResource("CardButton"), Height = 26, Padding = new Thickness(8, 0, 8, 0), ToolTip = L.T("Make this one independent: its changes stay on it only") };
            unlink.Click += (s, e) => { PushUndo(); obj.Prefab = null; Dirty(); BuildInspector(obj); };
            DockPanel.SetDock(unlink, Dock.Right);
            row.Children.Add(unlink);
            var text = new TextBlock { Text = L.F("🔗 A copy of the prefab \"{0}\": what you change here changes every copy", obj.Prefab),
                                       TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = (SolidColorBrush)FindResource("TextDim"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            L.SetSkip(text, true); // already translated, holds the prefab's name
            row.Children.Add(text);
            InspectorRoot.Children.Add(row);
        }
    }
}
