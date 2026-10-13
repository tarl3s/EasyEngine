using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace EasyEngine
{
    // Editor-only object tools:
    // - 🔒 Lock: a locked object can be selected and inspected, but not dragged, resized, nudged, deleted or edited
    //   (its inspector is read-only except the lock itself; the AI skips it; the game ignores the lock).
    // - Z layer: the order inside a layer (higher = in front; ZOrder.InDrawOrder draws it here and in the game).
    // - Arrow keys nudge the selection 1 px (Shift: 10 px, or a grid cell when the grid is on); a burst is one undo step.
    // - Keep proportions (resize keeps the picture's aspect; Shift while dragging a handle does it once) and Original size.
    public partial class MainWindow
    {
        private const string LockTag = "lock"; // inspector rows that stay enabled on a locked object
        private GameObjectData? _selectedBefore;  // the status hint shows once per newly selected object

        private void FocusScene() => SceneScroll.Focus();

        // ============================================================
        //  LOCK
        // ============================================================
        private void LockRow(GameObjectData obj)
        {
            var row = new WrapPanel { Tag = LockTag, Margin = new Thickness(0, 0, 0, 6) };
            var btn = new Button
            {
                Content = obj.Locked ? L.T("🔒 Locked") : L.T("🔓 Unlocked"), Tag = LockTag,
                Style = (Style)FindResource("CardButton"), Height = 28, FontSize = 11, Padding = new Thickness(10, 0, 10, 0),
                ToolTip = L.T("A locked object can't be moved, resized, deleted or edited in the editor. The game ignores the lock.")
            };
            btn.Click += (s, e) => SetLocked(_selection.Count > 1 && _selection.Contains(obj) ? _selection.ToList() : new List<GameObjectData> { obj }, !obj.Locked);
            row.Children.Add(btn);
            InspectorRoot.Children.Add(row);
            var layer = SceneTools.LayerOf(_scene, obj);
            if (obj.Locked || layer?.Locked == true || _scene.Locked)
                InspectorRoot.Children.Add(new TextBlock
                {
                    Tag = LockTag, Text = obj.Locked ? L.T("Locked — unlock to edit") : layer?.Locked == true ? L.F("Its layer \"{0}\" is locked — unlock the layer to edit", layer!.Name) : L.T("This scene is locked — unlock it (🔒 next to the scene) to change it"), Foreground = (SolidColorBrush)FindResource("TextDim"),
                    FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6)
                });
        }

        private void SetLocked(List<GameObjectData> targets, bool locked)
        {
            targets = targets.Where(o => o.Locked != locked).ToList();
            if (targets.Count == 0) return;
            PushUndo();
            foreach (var o in targets) o.Locked = locked;
            if (locked && _tmObj != null && targets.Contains(_tmObj)) { _tmObj = null; _tmBh = null; } // stop painting a locked tilemap
            RefreshObjects();
            if (_selected != null) SetPrimary(_selected); // handles, tilemap edit state and the inspector follow the lock
            RenderScene(); Dirty();
            Status(locked ? L.F("Locked {0}", string.Join(", ", targets.Select(o => o.Name))) : L.F("Unlocked {0}", string.Join(", ", targets.Select(o => o.Name))));
        }

        /// <summary>A locked object's inspector: every row but the lock is disabled (and dimmed).</summary>
        private void ApplyInspectorLock(GameObjectData obj)
        {
            if (!IsLockedObj(obj)) return;
            foreach (UIElement c in InspectorRoot.Children)
            {
                if (c is FrameworkElement fe && (fe.Tag as string) == LockTag) continue;
                c.IsEnabled = false;
                c.Opacity = 0.55;
            }
        }

        private string ObjectRowText(GameObjectData obj) => $"{obj.Name}   ·   {L.T(obj.Type)}" + (obj.Z != 0 ? $"   ·   z{obj.Z}" : "");

        /// <summary>An Objects-list row: "Name · Type" (+ "· z2" when Z isn't 0), the ▶ on/off-in-the-game toggle
        /// (MainWindow.Layers.cs), and a 🔒 button on a locked object. An object that is off is listed dimmed.</summary>
        private object ObjectRowContent(GameObjectData obj)
        {
            var dock = new DockPanel { LastChildFill = true };
            var active = ObjectActiveToggle(obj);
            DockPanel.SetDock(active, Dock.Right);
            dock.Children.Add(active);
            if (obj.Locked)
            {
                var lockBtn = new Button
                {
                    Content = "🔒", Style = (Style)FindResource("GhostButton"), Height = 22, FontSize = 11,
                    Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(6, 0, 0, 0), ToolTip = L.T("Locked — click to unlock")
                };
                lockBtn.Click += (s, e) => { SetLocked(new List<GameObjectData> { obj }, false); e.Handled = true; };
                DockPanel.SetDock(lockBtn, Dock.Right);
                dock.Children.Add(lockBtn);
            }
            dock.Children.Add(L.Keep(new TextBlock
            {
                Text = ObjectRowText(obj), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (SolidColorBrush)FindResource(obj.Active ? "Text" : "TextDim"), FontStyle = obj.Active ? FontStyles.Normal : FontStyles.Italic
            }));
            return dock;
        }

        // the objects the AI wanted to change but are locked (listed in its change summary)
        private readonly List<string> _aiSkippedLocked = new();

        private void AddLockedNotes(List<string> changes)
        {
            foreach (var name in _aiSkippedLocked)
                changes.Add(L.F("Skipped \"{0}\": it's locked (unlock it to let the AI change it)", name));
        }

        // ============================================================
        //  Z LAYER
        // ============================================================
        private void ZRow(GameObjectData obj)
        {
            var grid = RowGrid(L.T("Z layer"));
            var wrap = new WrapPanel { ToolTip = L.T("Higher draws in front of the other objects in this layer (it never moves above another layer).") };
            Button Small(string text, Action act, double minWidth = 28)
            {
                var b = new Button { Content = text, Style = (Style)FindResource("CardButton"), Height = 28, MinWidth = minWidth, FontSize = 11, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 6, 4) };
                b.Click += (s, e) => act();
                wrap.Children.Add(b);
                return b;
            }
            Small("−", () => SetZ(obj, obj.Z - 1));
            wrap.Children.Add(L.Keep(new TextBlock { Text = obj.Z.ToString(), MinWidth = 22, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = (SolidColorBrush)FindResource("Text"), FontSize = 12, Margin = new Thickness(0, 0, 6, 4) }));
            Small("+", () => SetZ(obj, obj.Z + 1));
            Small(L.T("To front"), () => ZToFront(obj));
            Small(L.T("To back"), () => ZToBack(obj));
            Grid.SetColumn(wrap, 1); grid.Children.Add(wrap);
        }

        private List<GameObjectData> LayerMates(GameObjectData obj) =>
            _scene.Layers.FirstOrDefault(l => l.Objects.Contains(obj))?.Objects.Where(o => !ReferenceEquals(o, obj)).ToList() ?? new List<GameObjectData>();

        private void ZToFront(GameObjectData obj) { var m = LayerMates(obj); if (m.Count > 0) SetZ(obj, m.Max(o => o.Z) + 1); }
        private void ZToBack(GameObjectData obj) { var m = LayerMates(obj); if (m.Count > 0) SetZ(obj, m.Min(o => o.Z) - 1); }

        private void SetZ(GameObjectData obj, int z)
        {
            if (IsLockedObj(obj) || obj.Z == z) return;
            PushUndo();
            obj.Z = z;
            RefreshObjects();
            SelectInObjectsList(obj);
            RenderScene(); BuildInspector(obj); Dirty();
        }

        // ============================================================
        //  ARROW-KEY NUDGE
        // ============================================================
        private DateTime _nudgeAt;
        private int _nudgeUndo = -1;
        private string _nudgeIds = "";
        private bool _inspectorQueued;

        private bool NudgeKey(KeyEventArgs e)
        {
            int dx = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0, dy = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
            if (dx == 0 && dy == 0) return false;
            var mods = Keyboard.Modifiers;
            if (mods != ModifierKeys.None && mods != ModifierKeys.Shift) return false;
            if (_selection.Count == 0) return false;
            // typing, picking in a list or a dropdown keeps its arrows
            if (Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox or ComboBoxItem or ListBox or ListBoxItem or MenuItem) return false;
            Nudge(dx, dy, mods == ModifierKeys.Shift);
            return true;
        }

        /// <summary>Moves the selected (unlocked) objects by 1 px, or 10 px / one grid cell with Shift. Nudges less than
        /// half a second apart on the same objects are one undo step.</summary>
        private void Nudge(int dx, int dy, bool big)
        {
            var targets = _selection.Where(o => !IsLockedObj(o)).ToList();
            if (targets.Count == 0) { Status(L.T("Locked — unlock to edit")); return; }
            double step = big ? (_snap ? GridSize : 10) : 1;
            string ids = string.Join(",", targets.Select(o => o.Id));
            var now = DateTime.UtcNow;
            if (_undo.Count != _nudgeUndo || ids != _nudgeIds || (now - _nudgeAt).TotalSeconds > 0.5) PushUndo();
            _nudgeUndo = _undo.Count; _nudgeIds = ids; _nudgeAt = now;

            foreach (var o in targets) { o.X += dx * step; o.Y += dy * step; }
            if (targets.Any(o => o.Behaviors.Any(b => b.Kind == BehaviorKind.Tilemap))) RenderScene(); // its cells are drawn apart
            else
            {
                foreach (var o in targets)
                    if (_visuals.TryGetValue(o, out var el)) { Canvas.SetLeft(el, o.X); Canvas.SetTop(el, o.Y); }
                if (_selected != null) PositionHandles(_selected);
            }
            Dirty();
            if (_inspectorQueued) return; // key repeat: rebuild the inspector (X/Y) once things settle
            _inspectorQueued = true;
            Dispatcher.BeginInvoke(new Action(() => { _inspectorQueued = false; if (_selected != null) BuildInspector(_selected); }), DispatcherPriority.Background);
        }

        // ============================================================
        //  SIZE: keep proportions, original size
        // ============================================================
        private readonly Dictionary<string, (long stamp, int w, int h)> _imageSizes = new();

        /// <summary>The pixel size of the picture the object shows (its image, or its default animation's first frame).</summary>
        private (int w, int h)? ImageSize(GameObjectData obj)
        {
            var abs = ProjectIO.DisplayImageAbs(_project, obj);
            if (abs == null || !File.Exists(abs)) return null;
            long stamp = File.GetLastWriteTimeUtc(abs).Ticks;
            if (!_imageSizes.TryGetValue(abs, out var c) || c.stamp != stamp)
            {
                var bmp = HitboxFit.LoadBitmap(abs);
                c = (stamp, bmp?.PixelWidth ?? 0, bmp?.PixelHeight ?? 0);
                _imageSizes[abs] = c;
            }
            return c.w > 0 && c.h > 0 ? (c.w, c.h) : null;
        }

        /// <summary>The width/height ratio to keep: the picture's, else the one saved when Keep proportions was turned on,
        /// else the box's (Shift on an object without a picture).</summary>
        private double? AspectOf(GameObjectData obj, Rect box)
        {
            if (ImageSize(obj) is (int w, int h)) return (double)w / h;
            if (obj.KeepAspect && obj.Aspect > 0) return obj.Aspect;
            return box.Width > 0 && box.Height > 0 ? box.Width / box.Height : null;
        }

        /// <summary>A handle drag with the proportions kept: a corner follows the side that moved most and stays anchored
        /// at the opposite corner; a side handle keeps the box centred on the other axis.</summary>
        private static (double L, double T, double W, double H) AspectRect(Rect start, double ratio, bool left, bool right, bool top, bool bottom, double W, double H, double min)
        {
            bool horiz = left || right, vert = top || bottom;
            if (horiz && vert)
            {
                if (Math.Abs(W - start.Width) / start.Width >= Math.Abs(H - start.Height) / start.Height) H = W / ratio;
                else W = H * ratio;
            }
            else if (horiz) H = W / ratio;
            else W = H * ratio;
            if (W < min) { W = min; H = W / ratio; }
            if (H < min) { H = min; W = H * ratio; }
            double L = left ? start.Right - W : horiz ? start.X : start.X + (start.Width - W) / 2;
            double T = top ? start.Bottom - H : vert ? start.Y : start.Y + (start.Height - H) / 2;
            return (L, T, W, H);
        }

        /// <summary>Width or Height typed in the inspector (undo already pushed); Keep proportions sets the other one.</summary>
        private void SetSize(GameObjectData obj, double? w, double? h)
        {
            if (IsLockedObj(obj)) return;
            double W = Math.Max(2, w ?? obj.Width), H = Math.Max(2, h ?? obj.Height);
            double? ratio = obj.KeepAspect ? AspectOf(obj, new Rect(0, 0, obj.Width, obj.Height)) : null;
            bool keep = ratio != null;
            if (ratio is double r)
            {
                if (w != null) H = Math.Max(2, Math.Round(W / r, 2));
                else W = Math.Max(2, Math.Round(H * r, 2));
            }
            obj.Width = W; obj.Height = H;
            RenderScene(); Dirty();
            if (keep) Dispatcher.BeginInvoke(new Action(() => { if (ReferenceEquals(_selected, obj)) BuildInspector(obj); }), DispatcherPriority.Background); // show the other side
        }

        private void SizeRow(GameObjectData obj)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            var keep = new CheckBox
            {
                Content = L.T("🔗 Keep proportions"), IsChecked = obj.KeepAspect, VerticalAlignment = VerticalAlignment.Center,
                Foreground = (SolidColorBrush)FindResource("Text"), Margin = new Thickness(0, 0, 10, 4),
                ToolTip = L.T("Resizing keeps the picture's proportions. Hold Shift while dragging a handle to keep them just once.")
            };
            keep.Checked += (s, e) => SetKeepAspect(obj, true);
            keep.Unchecked += (s, e) => SetKeepAspect(obj, false);
            var size = ImageSize(obj);
            var orig = new Button
            {
                Content = L.T("Original size"), Tag = "originalSize", IsEnabled = size != null,
                Style = (Style)FindResource("CardButton"), Height = 28, FontSize = 11, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 0, 4),
                ToolTip = size is (int w, int h) ? L.F("Use the picture's real size: {0} × {1} px", w, h) : L.T("Give it a picture first: this sets the size to the picture's real pixels.")
            };
            ToolTipService.SetShowOnDisabled(orig, true);
            orig.Click += (s, e) => OriginalSize(obj);
            wrap.Children.Add(keep); wrap.Children.Add(orig);
            InspectorRoot.Children.Add(wrap);
        }

        private void SetKeepAspect(GameObjectData obj, bool on)
        {
            if (IsLockedObj(obj) || obj.KeepAspect == on) return;
            PushUndo();
            obj.KeepAspect = on;
            if (on && obj.Height > 0) obj.Aspect = obj.Width / obj.Height; // kept when there is no picture
            Dirty();
        }

        /// <summary>Sets the box to the picture's real pixel size (the top-left corner stays).</summary>
        private void OriginalSize(GameObjectData obj)
        {
            if (IsLockedObj(obj) || ImageSize(obj) is not (int w, int h)) return;
            if (obj.Width == w && obj.Height == h) return;
            PushUndo();
            obj.Width = w; obj.Height = h;
            RenderScene(); BuildInspector(obj); Dirty();
            Status(L.F("Size set to the picture's {0} × {1} px", w, h));
        }

        // ---- test hooks ----
        internal void TestSelectMany(params GameObjectData[] objs) { Select(null); foreach (var o in objs) ToggleSelect(o); }
        internal void TestNudge(int dx, int dy, bool shift) => Nudge(dx, dy, shift);
        internal void TestNudgeBurstEnd() => _nudgeAt = default;
        internal void TestDeleteSelected() => DeleteSelected();
        internal void TestMarkClean() { _dirty = false; UpdateTitle(); }
        internal void TestDuplicate() => DuplicateSelected();
        internal void TestSetSnap(bool on) => _snap = on;
        internal bool TestHasHandles => _handles.Count > 0;
        internal int TestCanvasIndex(GameObjectData o) => _visuals.TryGetValue(o, out var el) ? SceneCanvas.Children.IndexOf(el) : -1;
        internal void TestZFront(GameObjectData o) => ZToFront(o);
        internal void TestZBack(GameObjectData o) => ZToBack(o);
        internal void TestSetZ(GameObjectData o, int z) => SetZ(o, z);
        internal void TestSetKeepAspect(GameObjectData o, bool on) => SetKeepAspect(o, on);
        internal void TestTypeSize(GameObjectData o, double? w, double? h) { PushUndo(); SetSize(o, w, h); }
        internal string TestObjectRow(GameObjectData o) => (o.Locked ? "🔒 " : "") + ((DockPanel)ObjectRowContent(o)).Children.OfType<TextBlock>().First().Text;

        /// <summary>Presses the mouse on the object's picture in the scene; true = a drag started.</summary>
        internal bool TestPressOn(GameObjectData o)
        {
            if (!_visuals.TryGetValue(o, out var el)) return false;
            el.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            bool dragging = ReferenceEquals(_dragObj, o);
            _dragObj = null; SceneCanvas.ReleaseMouseCapture();
            return dragging;
        }

        /// <summary>Drags a resize handle of the selected object by (dx, dy), like the mouse would; false = no such handle.</summary>
        internal bool TestResize(string handle, double dx, double dy, bool shift)
        {
            var h = Enum.Parse<Handle>(handle);
            if (_selected == null || !_handles.ContainsKey(h)) return false;
            PushUndo();
            _resizeHandle = h; _resizeStartMouse = new Point(0, 0);
            _resizeRect = new Rect(_selected.X, _selected.Y, _selected.Width, _selected.Height);
            ApplyResize(new Point(dx, dy), shift);
            RefreshInspectorTransform(); Dirty();
            return true;
        }

        /// <summary>The inspector's lock state: the lock button is enabled, and whether every other row is disabled.</summary>
        internal (bool lockEnabled, bool othersDisabled, bool othersEnabled) TestInspectorLock()
        {
            var lockRows = InspectorRoot.Children.OfType<FrameworkElement>().Where(f => (f.Tag as string) == LockTag).ToList();
            var others = InspectorRoot.Children.OfType<FrameworkElement>().Where(f => (f.Tag as string) != LockTag).ToList();
            return (lockRows.Count > 0 && lockRows.All(f => f.IsEnabled), others.Count > 0 && others.All(f => !f.IsEnabled), others.All(f => f.IsEnabled));
        }

        internal void TestClickLock()
        {
            var btn = InspectorRoot.Children.OfType<WrapPanel>().Where(w => (w.Tag as string) == LockTag).SelectMany(w => w.Children.OfType<Button>()).First();
            btn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        /// <summary>Clicks "Original size" in the inspector; returns whether the button was enabled.</summary>
        internal bool TestClickOriginalSize()
        {
            var btn = InspectorRoot.Children.OfType<WrapPanel>().SelectMany(w => w.Children.OfType<Button>()).FirstOrDefault(b => (b.Tag as string) == "originalSize");
            if (btn == null || !btn.IsEnabled) return false;
            btn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            return true;
        }
    }
}
