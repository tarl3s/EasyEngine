using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyEngine
{
    // Ctrl+C / Ctrl+V: copy the selected objects and paste them into the active layer — in the same scene or another one
    // of the project (or another editor window open in this engine). Each paste gets new ids and names, lands 20 px further
    // than the last one (or where the scene was right-clicked) and is one undo step. Typing keeps its own Ctrl+C / Ctrl+V.
    public partial class MainWindow
    {
        // the copied objects, as JSON (deep copies: later edits to the originals don't change what is pasted)
        private static List<string> _clipboard = new();
        private static int _pasteCount; // pastes since the last copy: each lands a little further

        /// <summary>Ctrl+C / Ctrl+V are the editor's only while no text field has the keyboard (typing copies text).</summary>
        internal static bool ClipboardKeysAreOurs(object? focused) => focused is not (TextBoxBase or PasswordBox);

        private bool CopyPasteKey(KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control || !ClipboardKeysAreOurs(Keyboard.FocusedElement)) return false;
            if (e.Key == Key.C) { if (_selection.Count == 0) return false; CopySelected(); return true; }
            if (e.Key == Key.V) { PasteObjects(null); return true; }
            return false;
        }

        private void CopySelected()
        {
            if (_selection.Count == 0) { Status(L.T("Select objects to copy")); return; }
            // in the scene's order (layer by layer), so the copies stack the same way
            var order = _scene.Layers.SelectMany(l => l.Objects).ToList();
            var objs = _selection.OrderBy(o => order.IndexOf(o)).ToList();
            _clipboard = objs.Select(o => JsonSerializer.Serialize(o)).ToList();
            _pasteCount = 0;
            Status(objs.Count == 1 ? L.F("Copied {0} — Ctrl+V pastes it", objs[0].Name) : L.F("Copied {0} objects — Ctrl+V pastes them", objs.Count));
        }

        /// <summary>Pastes the copied objects into the active layer; <paramref name="at"/> = the scene point where the group's
        /// top-left corner goes (null = 20 px further than the last paste).</summary>
        private void PasteObjects(Point? at)
        {
            if (_clipboard.Count == 0) { Status(L.T("Nothing to paste — select objects and press Ctrl+C first")); return; }
            if (SceneLockedNote()) return;
            var layer = _activeLayer ?? _scene.Layers.FirstOrDefault();
            if (layer == null) { Status(L.T("Create a layer first")); return; }
            if (layer.Locked) { Status(L.F("Layer \"{0}\" is locked — unlock it to add objects", layer.Name)); return; }

            var copies = _clipboard.Select(j => JsonSerializer.Deserialize<GameObjectData>(j)).OfType<GameObjectData>().ToList();
            if (copies.Count == 0) return;
            double dx, dy;
            if (at is Point p) { dx = p.X - copies.Min(o => o.X); dy = p.Y - copies.Min(o => o.Y); }
            else { _pasteCount++; dx = dy = 20 * _pasteCount; }

            PushUndo();
            foreach (var c in copies)
            {
                c.Id = _project.NextId;
                c.Name = UniqueName(c.Name); // added one by one, so two copies of "Coin" become Coin_2 and Coin_3
                c.X = Math.Round(c.X + dx); c.Y = Math.Round(c.Y + dy);
                c.Locked = false; // the copy is there to be placed
                layer.Objects.Add(c);
            }
            RenderScene(); RefreshObjects();
            Select(null); foreach (var c in copies) ToggleSelect(c);
            Dirty();
            Status(copies.Count == 1 ? L.F("Pasted {0} into \"{1}\"", copies[0].Name, layer.Name) : L.F("Pasted {0} objects into \"{1}\"", copies.Count, layer.Name));
        }

        // right-click on the scene: Copy / Paste (and Duplicate / Delete) for what is under the mouse
        private bool ShowObjectMenu(MouseButtonEventArgs e)
        {
            var at = e.GetPosition(SceneCanvas);
            GameObjectData? hit = null;
            for (var d = e.OriginalSource as DependencyObject; d != null && !ReferenceEquals(d, SceneCanvas); d = VisualTreeHelper.GetParent(d))
                if (d is FrameworkElement fe && fe.Tag is GameObjectData o) { hit = o; break; }
            if (hit != null && !_selection.Contains(hit)) Select(hit);
            ObjectMenu(at).IsOpen = true;
            return true;
        }

        private ContextMenu ObjectMenu(Point at)
        {
            var menu = new ContextMenu { PlacementTarget = SceneCanvas };
            MenuItem Item(string text, string keys, bool on, Action run)
            {
                var mi = new MenuItem { Header = text, InputGestureText = keys, IsEnabled = on };
                mi.Click += (s, e) => run();
                menu.Items.Add(mi);
                return mi;
            }
            bool any = _selection.Count > 0;
            Item(L.T("Copy"), "Ctrl+C", any, CopySelected);
            Item(L.T("Paste"), "Ctrl+V", _clipboard.Count > 0, () => PasteObjects(at));
            menu.Items.Add(new Separator());
            Item(L.T("Duplicate"), "Ctrl+D", _selected != null, DuplicateSelected);
            Item(L.T("Delete"), "Del", any, DeleteSelected);
            menu.Items.Add(MoveToLayerMenu()); // MainWindow.MoveLayer.cs
            return menu;
        }

        // ---- test hooks ----
        internal void TestCopy() => CopySelected();
        internal void TestPaste(Point? at = null) => PasteObjects(at);
        internal List<string> TestObjectMenu() => ObjectMenu(new Point(0, 0)).Items.OfType<MenuItem>().Select(m => (m.Header as string ?? "") + (m.IsEnabled ? "" : " (off)")).ToList();
        internal List<GameObjectData> TestSelection => _selection.ToList();
        internal void TestSwitchScene(Scene s) => SwitchScene(s);
        internal void TestPickSceneInList(string name) { foreach (ComboBoxItem it in SceneCombo.Items) if (it.Tag is Scene s && s.Name == name) { SceneCombo.SelectedItem = it; break; } } // as picking it in the scene list
        internal void TestSetActiveLayer(Layer l) => _activeLayer = l;
        internal static void TestClearClipboard() { _clipboard = new(); _pasteCount = 0; }
    }
}
