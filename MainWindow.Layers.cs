using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EasyEngine
{
    // Scenes and layers in the editor:
    // - ⧉ Duplicate scene (next to rename/delete): a deep copy right after it, every object with a new id (SceneTools).
    // - A layer row: [shown/hidden] = the editor's eye only · name · kind (+ its opacity) · [▶ on/off] = in the game ·
    //   [🔒] lock · [fixed/scroll] · [⋯] the rest (also on right-click): bring forward / send backward (the list order is the
    //   draw order — lower in the list draws in front, here and in the game), rename, duplicate, lock, on/off in the game,
    //   opacity (a slider popup), delete.
    // - A locked layer: its objects are drawn but can't be picked on the canvas, moved, nudged, deleted or edited, and
    //   the AI skips them (like a locked object: IsLockedObj). A layer or object that is off is drawn dimmed and dashed.
    // - Objects: "Active — exists in the game" in the inspector, a ▶ toggle on each row of the Objects list.
    public partial class MainWindow
    {
        private const double OffDim = 0.35; // how an object that is off in the game is drawn in the editor

        /// <summary>Locked for editing: the object itself, or the layer it is in.</summary>
        private bool IsLockedObj(GameObjectData o) => SceneTools.IsLocked(_scene, o);
        /// <summary>Left out when the game plays: the object, or its whole layer, is off.</summary>
        private bool IsOffInGame(GameObjectData o) => !o.Active || SceneTools.LayerOf(_scene, o)?.Active == false;

        // the layer as the AI's SCENE line names it
        private static string LayerNote(Layer l) => l.Name
            + (l.ScreenSpace || l.Kind == LayerKind.UI ? "(fixed UI)" : "") + (l.Locked ? "(LOCKED)" : "") + (!l.Active ? "(OFF)" : "")
            + (l.Opacity < 0.995 ? $"(opacity {Math.Round(l.Opacity * 100)}%)" : "");

        /// <summary>Where the AI puts a new object: the layer it wanted unless that one is locked, else the active layer,
        /// the Base layer, any unlocked layer (null = every layer is locked).</summary>
        private Layer? AiLayer(Layer? wanted)
        {
            if (wanted != null && !wanted.Locked) return wanted;
            if (_activeLayer != null && !_activeLayer.Locked && _scene.Layers.Contains(_activeLayer)) return _activeLayer;
            return _scene.Layers.FirstOrDefault(l => l.Kind == LayerKind.Base && !l.Locked) ?? _scene.Layers.FirstOrDefault(l => !l.Locked);
        }

        // ============================================================
        //  DUPLICATE SCENE
        // ============================================================
        private void DuplicateScene_Click(object sender, RoutedEventArgs e) => DuplicateScene();

        private void DuplicateScene()
        {
            var original = _scene;
            var copy = SceneTools.DuplicateScene(_project, original);
            RefreshSceneCombo();
            SwitchScene(copy);
            Dirty();
            Status(L.F("Duplicated \"{0}\" as \"{1}\" (doors and rules still point where they did)", original.Name, copy.Name));
        }

        // ============================================================
        //  THE LAYER ROW
        // ============================================================
        private Button RowToggle(string text, string brush, string tip, double minWidth = 0)
        {
            var b = new Button
            {
                Content = text, Height = 22, FontSize = 10, MinWidth = minWidth, Padding = new Thickness(4, 0, 4, 0), Style = (Style)FindResource("GhostButton"),
                Foreground = (SolidColorBrush)FindResource(brush), VerticalAlignment = VerticalAlignment.Center, ToolTip = tip
            };
            return b;
        }

        private ListBoxItem MakeLayerItem(Layer layer)
        {
            var eye = RowToggle(layer.Visible ? L.T("shown") : L.T("hidden"), layer.Visible ? "Text" : "TextDim",
                L.T("Shown or hidden while you edit. The game ignores this — use ▶ on/off for the game."), 50);
            eye.Click += (s, e) => { e.Handled = true; layer.Visible = !layer.Visible; RefreshLayersKeepActive(); RenderScene(); Dirty(); };

            var name = L.Keep(new TextBlock
            {
                Text = layer.Name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (SolidColorBrush)FindResource(layer.Active ? "Text" : "TextDim"), FontStyle = layer.Active ? FontStyles.Normal : FontStyles.Italic
            });
            var kind = new TextBlock
            {
                Text = LayerKindLabel(layer.Kind) + (layer.Opacity < 0.995 ? $" · {Math.Round(layer.Opacity * 100)}%" : ""),
                Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0)
            };

            var game = RowToggle(layer.Active ? L.T("▶ on") : L.T("▶ off"), layer.Active ? "Text" : "Danger",
                layer.Active ? L.T("In the game: on. Click to leave this layer's objects out of the game.") : L.T("Off in the game: its objects won't exist when playing. Click to turn it on."));
            game.Click += (s, e) => { e.Handled = true; SetLayerActive(layer, !layer.Active); };

            var lockBtn = RowToggle(layer.Locked ? "🔒" : "🔓", layer.Locked ? "Text" : "TextDim",
                layer.Locked ? L.T("Locked: its objects can't be picked, moved or deleted, and the AI leaves them alone. Click to unlock.") : L.T("Lock this layer (its objects can't be picked, moved or deleted; the AI leaves them alone)"));
            lockBtn.Click += (s, e) => { e.Handled = true; SetLayerLocked(layer, !layer.Locked); };

            bool Fixed() => layer.ScreenSpace || layer.Kind == LayerKind.UI;
            var fix = RowToggle(Fixed() ? L.T("fixed") : L.T("scroll"), "Text", L.T("Fixed = stays on screen (UI/HUD). Scroll = moves with the world."), 46);
            fix.Click += (s, e) => { e.Handled = true; PushUndo(); layer.ScreenSpace = !Fixed(); fix.Content = Fixed() ? L.T("fixed") : L.T("scroll"); Dirty(); };

            var more = RowToggle("⋯", "Text", L.T("More: order, rename, duplicate, opacity, delete"), 24);
            more.FontSize = 13;
            more.Click += (s, e) => { e.Handled = true; ShowLayerMenu(layer, more); };

            var grid = new Grid();
            var parts = new FrameworkElement[] { eye, name, kind, game, lockBtn, fix, more };
            for (int i = 0; i < parts.Length; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
                Grid.SetColumn(parts[i], i);
                grid.Children.Add(parts[i]);
            }

            var item = new ListBoxItem { Content = grid, Tag = layer, Padding = new Thickness(4, 4, 4, 4), Foreground = (SolidColorBrush)FindResource("Text"), HorizontalContentAlignment = HorizontalAlignment.Stretch }; // stretch: the kind sits at the right, not glued to the name
            item.MouseRightButtonUp += (s, e) => { e.Handled = true; ShowLayerMenu(layer, item); };
            return item;
        }

        /// <summary>The ⋯ menu of a layer (also its right-click menu).</summary>
        private ContextMenu LayerMenu(Layer layer, FrameworkElement anchor)
        {
            var menu = new ContextMenu { PlacementTarget = anchor };
            void Add(string text, Action act)
            {
                var mi = new MenuItem { Header = text };
                mi.Click += (_, __) => act();
                menu.Items.Add(mi);
            }
            int i = _scene.Layers.IndexOf(layer);
            if (i < _scene.Layers.Count - 1) Add(L.T("Bring forward (draw in front)"), () => MoveLayer(layer, +1));
            if (i > 0) Add(L.T("Send backward (draw behind)"), () => MoveLayer(layer, -1));
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            Add(L.T("Rename…"), () => RenameLayer(layer));
            Add(L.T("Duplicate layer"), () => DuplicateLayer(layer));
            Add(L.T("Opacity…"), () => Dispatcher.BeginInvoke(new Action(() => ShowLayerOpacity(layer, anchor)))); // after the menu has closed
            menu.Items.Add(new Separator());
            Add(layer.Locked ? L.T("🔓 Unlock layer") : L.T("🔒 Lock layer"), () => SetLayerLocked(layer, !layer.Locked));
            Add(layer.Active ? L.T("Turn off in the game") : L.T("Turn on in the game"), () => SetLayerActive(layer, !layer.Active));
            menu.Items.Add(new Separator());
            Add(L.T("Delete layer"), () => DeleteLayer(layer));
            return menu;
        }

        private void ShowLayerMenu(Layer layer, FrameworkElement anchor) => LayerMenu(layer, anchor).IsOpen = true;

        // ============================================================
        //  LAYER ACTIONS (each one undoable)
        // ============================================================
        private void AfterLayerChange(Layer? select = null)
        {
            var keep = select ?? _activeLayer;
            RefreshLayers();
            int i = keep != null ? _scene.Layers.IndexOf(keep) : -1;
            LayersList.SelectedIndex = i >= 0 ? i : IndexOfBaseLayer();
            RenderScene(); Dirty();
        }

        private void MoveLayer(Layer layer, int delta)
        {
            int i = _scene.Layers.IndexOf(layer), j = i + Math.Sign(delta);
            if (i < 0 || j < 0 || j >= _scene.Layers.Count) return;
            PushUndo();
            SceneTools.MoveLayer(_scene, layer, delta);
            AfterLayerChange(layer);
            Status(delta > 0 ? L.F("\"{0}\" now draws in front of \"{1}\"", layer.Name, _scene.Layers[j - 1].Name)
                             : L.F("\"{0}\" now draws behind \"{1}\"", layer.Name, _scene.Layers[j + 1].Name));
        }

        private void RenameLayer(Layer layer)
        {
            var name = InputDialog.Prompt(this, L.T("Rename layer"), L.T("New name:"), layer.Name);
            if (name == null || (name = name.Trim()).Length == 0 || name == layer.Name) return;
            if (_scene.Layers.Any(l => !ReferenceEquals(l, layer) && l.Name == name)) { MessageBox.Show(L.T("A layer with that name already exists.")); return; }
            PushUndo();
            layer.Name = name;
            AfterLayerChange(layer);
            if (ReferenceEquals(layer, _activeLayer)) ObjectsHeader.Text = L.F("OBJECTS — {0}", layer.Name).ToUpper();
        }

        private void DuplicateLayer(Layer layer)
        {
            PushUndo();
            var copy = SceneTools.DuplicateLayer(_project, _scene, layer);
            AfterLayerChange(copy);
            Status(L.F("Duplicated layer \"{0}\" as \"{1}\" (in front of it)", layer.Name, copy.Name));
        }

        private void SetLayerLocked(Layer layer, bool locked)
        {
            if (layer.Locked == locked) return;
            PushUndo();
            layer.Locked = locked;
            if (locked && _selection.Any(o => layer.Objects.Contains(o))) Select(null); // its objects can't stay picked
            AfterLayerChange(layer);
            if (_selected != null) BuildInspector(_selected);
            Status(locked ? L.F("Layer \"{0}\" locked", layer.Name) : L.F("Layer \"{0}\" unlocked", layer.Name));
        }

        private void SetLayerActive(Layer layer, bool active)
        {
            if (layer.Active == active) return;
            PushUndo();
            layer.Active = active;
            AfterLayerChange(layer);
            if (_selected != null) BuildInspector(_selected);
            Status(active ? L.F("Layer \"{0}\" is on in the game", layer.Name) : L.F("Layer \"{0}\" is off in the game: its objects won't exist when playing", layer.Name));
        }

        private void SetLayerOpacity(Layer layer, double v)
        {
            layer.Opacity = Math.Clamp(Math.Round(v, 2), 0, 1);
            RenderScene(); Dirty();
        }

        /// <summary>A small popup with the layer's opacity slider (each drag is one undo step).</summary>
        private void ShowLayerOpacity(Layer layer, FrameworkElement anchor)
        {
            var val = new TextBlock { Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11, MinWidth = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            var slider = SliderBar.Make(0, 1, layer.Opacity, v => SetLayerOpacity(layer, v), v => val.Text = $"{Math.Round(v * 100)}%", step: 0.05, onStart: PushUndo);
            slider.Width = 180;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            row.Children.Add(slider); row.Children.Add(val);
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = L.F("Opacity of \"{0}\"", layer.Name), FontWeight = FontWeights.SemiBold, FontSize = 12 });
            panel.Children.Add(new TextBlock { Text = L.T("The whole layer, in the editor and in the game."), Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
            panel.Children.Add(row);
            var root = new Border { Background = Theme.Brush("Bg1", "#808080"), BorderBrush = Theme.Brush("Border", "#808080"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 10, 12, 10), Child = panel };
            System.Windows.Documents.TextElement.SetForeground(root, Theme.Brush("Text", "#DCDCDC")); // a popup isn't inside the window: give it the theme text colour
            var pop = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, Child = root };
            pop.Closed += (s, e) => AfterLayerChange(layer);
            pop.IsOpen = true;
        }

        // ============================================================
        //  THE CANVAS: layer opacity, off = dimmed + dashed, locked layer = not pickable
        // ============================================================
        private void LayerLook(Layer layer, GameObjectData obj, FrameworkElement el)
        {
            bool off = !layer.Active || !obj.Active;
            el.Opacity *= Math.Clamp(layer.Opacity, 0, 1) * (off ? OffDim : 1);
            if (layer.Locked) el.IsHitTestVisible = false; // clicks go through to what is under it
            if (!off || el is not Border b) return;
            var dash = new Rectangle { Stroke = (SolidColorBrush)FindResource("Text"), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false, Tag = "off" };
            switch (b.Child)
            {
                case null: b.Child = dash; break;
                case Panel p: p.Children.Add(dash); break; // e.g. the hitbox outline's grid
                default: { var old = b.Child; b.Child = null; var g = new Grid(); g.Children.Add(old); g.Children.Add(dash); b.Child = g; break; }
            }
        }

        /// <summary>How see-through the editor draws a tilemap's cells: its own opacity × its layer's (dimmed when off).</summary>
        private double EditorAlpha(GameObjectData obj)
        {
            var layer = SceneTools.LayerOf(_scene, obj);
            bool off = !obj.Active || layer?.Active == false;
            return Math.Clamp(obj.Opacity, 0, 1) * Math.Clamp(layer?.Opacity ?? 1, 0, 1) * (off ? OffDim : 1);
        }

        // ============================================================
        //  OBJECTS ON / OFF IN THE GAME
        // ============================================================
        private void ActiveRow(GameObjectData obj)
        {
            var chk = new CheckBox
            {
                Content = L.T("Active — exists in the game"), IsChecked = obj.Active, Margin = new Thickness(0, 0, 0, 6),
                ToolTip = L.T("Off: it stays in the scene (and can still be spawned or turned on by a rule) but isn't there when the game starts.")
            };
            chk.Click += (s, e) => SetObjectsActive(_selection.Count > 1 && _selection.Contains(obj) ? _selection.ToList() : new List<GameObjectData> { obj }, chk.IsChecked == true);
            InspectorRoot.Children.Add(chk);
            var layer = SceneTools.LayerOf(_scene, obj);
            if (layer != null && !layer.Active)
                InspectorRoot.Children.Add(new TextBlock
                {
                    Text = L.F("Its layer \"{0}\" is off in the game, so it won't be there either.", layer.Name), Foreground = (SolidColorBrush)FindResource("TextDim"),
                    FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6)
                });
        }

        private void SetObjectsActive(List<GameObjectData> targets, bool active)
        {
            targets = targets.Where(o => o.Active != active && !IsLockedObj(o)).ToList();
            if (targets.Count == 0) { if (_selected != null) BuildInspector(_selected); return; }
            PushUndo();
            foreach (var o in targets) o.Active = active;
            RefreshObjects();
            if (_selected != null) { SelectInObjectsList(_selected); BuildInspector(_selected); }
            RenderScene(); Dirty();
            var names = string.Join(", ", targets.Select(o => o.Name));
            Status(active ? L.F("{0}: on in the game", names) : L.F("{0}: off in the game (it stays in the scene)", names));
        }

        /// <summary>The ▶ toggle at the end of an Objects-list row.</summary>
        private Button ObjectActiveToggle(GameObjectData obj)
        {
            var b = RowToggle(obj.Active ? "▶" : L.T("▶ off"), obj.Active ? "TextDim" : "Danger",
                obj.Active ? L.T("In the game: on. Click to leave it out of the game.") : L.T("Off in the game. Click to turn it on."));
            b.Margin = new Thickness(6, 0, 0, 0);
            b.Click += (s, e) => { e.Handled = true; SetObjectsActive(new List<GameObjectData> { obj }, !obj.Active); };
            return b;
        }

        // ---- test hooks ----
        internal Scene TestScene => _scene;
        internal Layer? TestActiveLayer => _activeLayer;
        internal void TestDuplicateScene() => DuplicateScene();
        internal void TestMoveLayer(Layer l, int delta) => MoveLayer(l, delta);
        internal void TestDuplicateLayer(Layer l) => DuplicateLayer(l);
        internal void TestSetLayerLocked(Layer l, bool on) => SetLayerLocked(l, on);
        internal void TestSetLayerActive(Layer l, bool on) => SetLayerActive(l, on);
        internal void TestSetLayerOpacity(Layer l, double v) { PushUndo(); SetLayerOpacity(l, v); AfterLayerChange(l); } // as the popup does when it closes
        internal void TestSetObjectsActive(GameObjectData o, bool on) => SetObjectsActive(new List<GameObjectData> { o }, on);
        internal void TestAddEmpty() => AddEmpty_Click(this, new RoutedEventArgs());
        internal bool TestPickable(GameObjectData o) => _visuals.TryGetValue(o, out var el) && el.IsHitTestVisible;
        internal double TestCanvasOpacity(GameObjectData o) => _visuals.TryGetValue(o, out var el) ? el.Opacity : -1;
        internal bool TestOffMark(GameObjectData o) => _visuals.TryGetValue(o, out var el) && el is Border b
            && (b.Child is Rectangle { Tag: "off" } || b.Child is Panel p && p.Children.OfType<Rectangle>().Any(r => (r.Tag as string) == "off"));
        internal List<string> TestLayerMenu(Layer l) => LayerMenu(l, LayersList).Items.OfType<MenuItem>().Select(m => m.Header as string ?? "").ToList();
        internal List<string> TestLayerRowTexts(Layer l)
        {
            var item = LayersList.Items.OfType<ListBoxItem>().First(i => ReferenceEquals(i.Tag, l));
            return ((Grid)item.Content).Children.OfType<FrameworkElement>().Select(f => f is Button b ? b.Content as string ?? "" : f is TextBlock t ? t.Text : "").ToList();
        }
    }
}
