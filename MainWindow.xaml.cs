using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Line = System.Windows.Shapes.Line;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace EasyEngine
{
    public partial class MainWindow : Window
    {
        private readonly Project _project;
        private Scene _scene = null!;
        private Layer? _activeLayer;
        private GameObjectData? _selected;                       // primary (shown in the inspector)
        private readonly List<GameObjectData> _selection = new(); // AI target set (1+ objects)

        private readonly Dictionary<GameObjectData, FrameworkElement> _visuals = new();
        private readonly OllamaClient _ollama = new();
        private readonly CloudClient _cloud = new();
        private string _model = "qwen2.5-coder:1.5b";
        private bool _useCloud;                                   // true = Groq (nuvem); false = Ollama (local)
        private string _cloudModel = "llama-3.3-70b-versatile";   // modelo usado quando _useCloud

        // tolerant JSON parsing — small models sometimes add // comments or trailing commas
        private static readonly JsonDocumentOptions LenientJson = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        private readonly List<string> _aiImages = new(); // relative paths of images the user gave the AI
        private readonly List<string> _aiSounds = new(); // relative paths of audio the user gave the AI
        private bool _aiBrainstorm; // false = Build (makes changes), true = Brainstorm (just chats)
        private string? _aiLastSummary;
        private readonly List<string> _aiLastObjects = new();

        private GameObjectData? _dragObj;
        private Point _dragStart;

        private bool _snap;
        private const double GridSize = 16;
        private bool _loadingScenes;

        // organization (larger projects): palette/object filters + collapsed prefab categories
        private string _prefabFilter = "", _objectFilter = "";
        private readonly HashSet<string> _collapsedCats = new();

        // undo / redo (snapshots of the active scene)
        private readonly Stack<string> _undo = new();
        private readonly Stack<string> _redo = new();
        private bool _restoring;

        // resize
        private enum Handle { TL, T, TR, R, BR, B, BL, L }
        private readonly Dictionary<Handle, Rectangle> _handles = new();
        private bool _resizing;
        private Handle _resizeHandle;
        private Point _resizeStartMouse;
        private Rect _resizeRect;

        // tilemap editing
        private GameObjectData? _tmObj;
        private BehaviorData? _tmBh;
        private int _tmCols, _tmRows, _tmActive = 1;
        private double _tmSize = 32;
        private int[] _tmCells = Array.Empty<int>();
        private readonly List<(string color, bool solid)> _tmPalette = new();
        private bool _tmPainting;
        private readonly Dictionary<int, Rectangle> _tmCellRects = new();

        // scene zoom
        private double _zoom = 1.0;
        private const double MinZoom = 0.08, MaxZoom = 6.0;

        // scene pan (middle-mouse drag)
        private bool _panning;
        private Point _panStartMouse;
        private double _panStartH, _panStartV;

        public MainWindow(Project project)
        {
            InitializeComponent();
            _project = project;
            if (_project.Scenes.Count == 0) _project.Scenes.Add(new Scene());
            ProjectNameText.Text = project.Name;

            SceneCanvas.MouseMove += Canvas_MouseMove;
            SceneCanvas.MouseLeftButtonUp += Canvas_MouseUp;
            SceneCanvas.MouseRightButtonDown += Canvas_RightDown;
            SceneCanvas.MouseRightButtonUp += Canvas_RightUp;

            SceneScroll.PreviewMouseDown += SceneScroll_MouseDown;
            SceneScroll.PreviewMouseMove += SceneScroll_MouseMove;
            SceneScroll.PreviewMouseUp += SceneScroll_MouseUp;

            if (_project.Prefabs.Count == 0) _project.Prefabs = PrefabFactory.Defaults();
            RefreshSceneCombo();
            SwitchScene(project.GetStartScene());
            RefreshPalette();
            SetTab(true);
            SetAiMode(false);
            UpdateAiTarget();
            AiSettings.Load();
            GroqKey.Text = AiSettings.GroqApiKey;
            _ = RefreshOllamaStatusAsync();
        }

        // ============================================================
        //  UNDO / REDO
        // ============================================================
        private string SceneJson() => JsonSerializer.Serialize(_scene);
        private Scene FromJson(string s) => JsonSerializer.Deserialize<Scene>(s)!;

        private void PushUndo()
        {
            if (_restoring) return;
            _undo.Push(SceneJson());
            _redo.Clear();
        }

        private void Undo()
        {
            if (_undo.Count == 0) { Status("Nothing to undo"); return; }
            _redo.Push(SceneJson());
            ReplaceActiveScene(FromJson(_undo.Pop()));
            Status("Undo");
        }

        private void Redo()
        {
            if (_redo.Count == 0) { Status("Nothing to redo"); return; }
            _undo.Push(SceneJson());
            ReplaceActiveScene(FromJson(_redo.Pop()));
            Status("Redo");
        }

        private void ReplaceActiveScene(Scene newScene)
        {
            _restoring = true;
            int idx = _project.Scenes.IndexOf(_scene);
            if (idx < 0) idx = 0;
            newScene.Name = _scene.Name; // keep identity/name stable
            _project.Scenes[idx] = newScene;
            _scene = newScene;
            SceneFrame.Width = _scene.Width;
            SceneFrame.Height = _scene.Height;
            _selected = null;
            RefreshLayers();
            if (LayersList.Items.Count > 0) LayersList.SelectedIndex = IndexOfBaseLayer();
            RenderScene();
            BuildInspector(null);
            QueueFit();
            _restoring = false;
        }

        // ============================================================
        //  SCENES
        // ============================================================
        private void RefreshSceneCombo()
        {
            _loadingScenes = true;
            SceneCombo.Items.Clear();
            foreach (var s in _project.Scenes)
            {
                bool start = s.Name == _project.StartScene;
                SceneCombo.Items.Add(new ComboBoxItem { Content = (start ? "★ " : "") + s.Name, Tag = s });
            }
            _loadingScenes = false;
        }

        private void SelectSceneInCombo(Scene s)
        {
            _loadingScenes = true;
            foreach (ComboBoxItem it in SceneCombo.Items)
                if (ReferenceEquals(it.Tag, s)) { SceneCombo.SelectedItem = it; break; }
            _loadingScenes = false;
        }

        private void SwitchScene(Scene scene)
        {
            _scene = scene;
            _undo.Clear(); _redo.Clear();
            Title = $"EasyEngine — {_project.Name} · {scene.Name}";
            SelectSceneInCombo(scene);
            SceneFrame.Width = scene.Width;
            SceneFrame.Height = scene.Height;
            _selected = null;
            RefreshLayers();
            if (LayersList.Items.Count > 0) LayersList.SelectedIndex = IndexOfBaseLayer();
            RenderScene();
            BuildInspector(null);
            QueueFit();
        }

        private void SceneCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingScenes) return;
            if (SceneCombo.SelectedItem is ComboBoxItem it && it.Tag is Scene s && !ReferenceEquals(s, _scene))
                SwitchScene(s);
        }

        private void AddScene_Click(object sender, RoutedEventArgs e)
        {
            var name = InputDialog.Prompt(this, "New scene", "Scene name:", $"Scene {_project.Scenes.Count + 1}");
            if (name == null) return;
            if (_project.Scenes.Any(s => s.Name == name)) { MessageBox.Show("A scene with that name already exists."); return; }
            var scene = new Scene { Name = name };
            scene.Layers.Add(new Layer { Name = "Background", Kind = LayerKind.Background });
            scene.Layers.Add(new Layer { Name = "Base", Kind = LayerKind.Base });
            scene.Layers.Add(new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true });
            _project.Scenes.Add(scene);
            RefreshSceneCombo();
            SwitchScene(scene);
            Dirty();
        }

        private void RenameScene_Click(object sender, RoutedEventArgs e)
        {
            var name = InputDialog.Prompt(this, "Rename scene", "New name:", _scene.Name);
            if (name == null) return;
            bool wasStart = _project.StartScene == _scene.Name;
            _scene.Name = name;
            if (wasStart) _project.StartScene = name;
            RefreshSceneCombo();
            SelectSceneInCombo(_scene);
            Title = $"EasyEngine — {_project.Name} · {name}";
            Dirty();
        }

        private void DeleteScene_Click(object sender, RoutedEventArgs e)
        {
            if (_project.Scenes.Count <= 1) { MessageBox.Show("A project needs at least one scene."); return; }
            if (MessageBox.Show($"Delete scene \"{_scene.Name}\"?", "Delete scene", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var removed = _scene;
            _project.Scenes.Remove(removed);
            if (_project.StartScene == removed.Name) _project.StartScene = _project.Scenes[0].Name;
            RefreshSceneCombo();
            SwitchScene(_project.Scenes[0]);
            Dirty();
        }

        private void SetStartScene_Click(object sender, RoutedEventArgs e)
        {
            _project.StartScene = _scene.Name;
            RefreshSceneCombo();
            SelectSceneInCombo(_scene);
            Status($"\"{_scene.Name}\" is now the start scene");
            Dirty();
        }

        private int IndexOfBaseLayer()
        {
            for (int i = 0; i < _scene.Layers.Count; i++)
                if (_scene.Layers[i].Kind == LayerKind.Base) return i;
            return 0;
        }

        // ============================================================
        //  RENDER + SELECTION + DRAG + RESIZE
        // ============================================================
        private void RenderScene()
        {
            SceneCanvas.Children.Clear();
            _visuals.Clear();
            _handles.Clear();
            _tmCellRects.Clear();
            SceneFrame.Background = BrushFrom(_scene.BackgroundColor, "#1A1A20");

            if (_snap) DrawGrid();

            foreach (var layer in _scene.Layers)
            {
                if (!layer.Visible) continue;
                foreach (var obj in layer.Objects)
                {
                    var el = MakeVisual(obj);
                    _visuals[obj] = el;
                    SceneCanvas.Children.Add(el);
                    if (obj.Behaviors.Any(b => b.Kind == BehaviorKind.Tilemap))
                        DrawTilemap(obj, ReferenceEquals(obj, _tmObj));
                }
            }
            UpdateSelectionOutline();
            AddResizeHandles();
        }

        private void DrawGrid()
        {
            var brush = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
            for (double x = 0; x <= _scene.Width; x += GridSize)
                SceneCanvas.Children.Add(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = _scene.Height, Stroke = brush, StrokeThickness = 1 });
            for (double y = 0; y <= _scene.Height; y += GridSize)
                SceneCanvas.Children.Add(new Line { X1 = 0, Y1 = y, X2 = _scene.Width, Y2 = y, Stroke = brush, StrokeThickness = 1 });
        }

        private FrameworkElement MakeVisual(GameObjectData obj)
        {
            double corner = obj.CornerRadius >= 0
                ? obj.CornerRadius
                : (obj.Type is "Bullet" or "Particle" ? Math.Min(obj.Width, obj.Height) / 2 : 4);
            var border = new Border
            {
                Width = obj.Width,
                Height = obj.Height,
                CornerRadius = new CornerRadius(corner),
                BorderBrush = (SolidColorBrush)FindResource("Accent"),
                BorderThickness = new Thickness(0),
                Opacity = Math.Clamp(obj.Opacity, 0, 1),
                Tag = obj
            };
            // (custom outline is applied in play; in the editor the border is reused for the selection highlight)

            bool isTilemap = obj.Behaviors.Any(b => b.Kind == BehaviorKind.Tilemap);
            if (isTilemap)
            {
                // the tilemap draws its own cells; its box is just a transparent, click-through anchor
                border.Background = System.Windows.Media.Brushes.Transparent;
                border.IsHitTestVisible = false;
                Canvas.SetLeft(border, obj.X);
                Canvas.SetTop(border, obj.Y);
                return border;
            }

            var img = ProjectIO.DisplayImageAbs(_project, obj);
            if (img != null)
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(img); bmp.EndInit();
                    border.Background = MakeImageBrush(bmp, obj);
                    RenderOptions.SetBitmapScalingMode(border, BitmapScalingMode.NearestNeighbor);
                }
                catch { border.Background = BrushFrom(obj.Color, "#9A9AA0"); }
            }
            else border.Background = BrushFrom(obj.Color, "#9A9AA0");

            Canvas.SetLeft(border, obj.X);
            Canvas.SetTop(border, obj.Y);
            if (Math.Abs(obj.Rotation) > 0.01)
            {
                border.RenderTransformOrigin = new Point(0.5, 0.5);
                border.RenderTransform = new RotateTransform(obj.Rotation);
            }

            border.MouseLeftButtonDown += (s, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { ToggleSelect(obj); e.Handled = true; return; }
                Select(obj);
                PushUndo();
                _dragObj = obj;
                _dragStart = e.GetPosition(SceneCanvas);
                SceneCanvas.CaptureMouse();
                e.Handled = true;
            };
            return border;
        }

        private ImageBrush MakeImageBrush(BitmapSource bmp, GameObjectData obj)
        {
            if (obj.TileImage)
                return new ImageBrush(bmp)
                {
                    Stretch = Stretch.None,
                    TileMode = TileMode.Tile,
                    Viewport = new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight),
                    ViewportUnits = BrushMappingMode.Absolute,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top
                };
            return new ImageBrush(bmp) { Stretch = Stretch.Fill };
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_tmObj != null && ReferenceEquals(_selected, _tmObj))
            {
                PushUndo();
                _tmPainting = true; SceneCanvas.CaptureMouse();
                PaintAt(e.GetPosition(SceneCanvas), erase: false);
                e.Handled = true;
                return;
            }
            Select(null);
        }

        private void Canvas_RightDown(object sender, MouseButtonEventArgs e)
        {
            if (_tmObj != null && ReferenceEquals(_selected, _tmObj))
            {
                PushUndo();
                _tmPainting = true; SceneCanvas.CaptureMouse();
                PaintAt(e.GetPosition(SceneCanvas), erase: true);
                e.Handled = true;
            }
        }

        private void Canvas_RightUp(object sender, MouseButtonEventArgs e)
        {
            if (_tmPainting) { _tmPainting = false; SceneCanvas.ReleaseMouseCapture(); SerializeTilemap(); Dirty(); e.Handled = true; }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_tmPainting)
            {
                PaintAt(e.GetPosition(SceneCanvas), erase: e.RightButton == MouseButtonState.Pressed);
                return;
            }
            if (_resizing && _selected != null) { ApplyResize(e.GetPosition(SceneCanvas)); return; }
            if (_dragObj == null || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(SceneCanvas);
            _dragObj.X += p.X - _dragStart.X;
            _dragObj.Y += p.Y - _dragStart.Y;
            _dragStart = p;
            double x = _dragObj.X, y = _dragObj.Y;
            if (_snap) { x = Math.Round(x / GridSize) * GridSize; y = Math.Round(y / GridSize) * GridSize; }
            if (_visuals.TryGetValue(_dragObj, out var el)) { Canvas.SetLeft(el, x); Canvas.SetTop(el, y); }
            PositionHandles(_dragObj);
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_tmPainting)
            {
                _tmPainting = false; SceneCanvas.ReleaseMouseCapture();
                SerializeTilemap(); Dirty();
                return;
            }
            if (_resizing)
            {
                _resizing = false;
                SceneCanvas.ReleaseMouseCapture();
                RefreshInspectorTransform(); Dirty();
                return;
            }
            if (_dragObj != null)
            {
                if (_snap) { _dragObj.X = Math.Round(_dragObj.X / GridSize) * GridSize; _dragObj.Y = Math.Round(_dragObj.Y / GridSize) * GridSize; }
                RefreshInspectorTransform(); Dirty();
            }
            _dragObj = null;
            SceneCanvas.ReleaseMouseCapture();
        }

        private void UpdateSelectionOutline()
        {
            foreach (var kv in _visuals)
                if (kv.Value is Border b)
                    b.BorderThickness = new Thickness(_selection.Contains(kv.Key) ? 2.5 : 0);
        }

        // ---- resize handles ----
        private void AddResizeHandles()
        {
            if (_selected == null || !_visuals.ContainsKey(_selected)) return;
            if (_selected.Behaviors.Any(b => b.Kind == BehaviorKind.Tilemap)) return; // tilemaps are painted, not resized
            foreach (Handle h in Enum.GetValues(typeof(Handle)))
            {
                var r = new Rectangle
                {
                    Width = 11, Height = 11,
                    Fill = (SolidColorBrush)FindResource("Accent"),
                    Stroke = Brushes.White, StrokeThickness = 1.5,
                    Cursor = CursorFor(h), Tag = h
                };
                r.MouseLeftButtonDown += (s, e) =>
                {
                    PushUndo();
                    _resizing = true;
                    _resizeHandle = h;
                    _resizeStartMouse = e.GetPosition(SceneCanvas);
                    _resizeRect = new Rect(_selected.X, _selected.Y, _selected.Width, _selected.Height);
                    SceneCanvas.CaptureMouse();
                    e.Handled = true;
                };
                _handles[h] = r;
                SceneCanvas.Children.Add(r);
            }
            PositionHandles(_selected);
        }

        private void PositionHandles(GameObjectData obj)
        {
            if (!ReferenceEquals(obj, _selected) || _handles.Count == 0) return;
            double x = obj.X, y = obj.Y, w = obj.Width, h = obj.Height;
            Place(Handle.TL, x, y); Place(Handle.T, x + w / 2, y); Place(Handle.TR, x + w, y);
            Place(Handle.R, x + w, y + h / 2); Place(Handle.BR, x + w, y + h); Place(Handle.B, x + w / 2, y + h);
            Place(Handle.BL, x, y + h); Place(Handle.L, x, y + h / 2);
        }

        private void Place(Handle h, double cx, double cy)
        {
            if (!_handles.TryGetValue(h, out var r)) return;
            Canvas.SetLeft(r, cx - r.Width / 2);
            Canvas.SetTop(r, cy - r.Height / 2);
        }

        private static Cursor CursorFor(Handle h) => h switch
        {
            Handle.TL or Handle.BR => Cursors.SizeNWSE,
            Handle.TR or Handle.BL => Cursors.SizeNESW,
            Handle.T or Handle.B => Cursors.SizeNS,
            _ => Cursors.SizeWE
        };

        private void ApplyResize(Point p)
        {
            if (_selected == null) return;
            const double min = 8;
            double dx = p.X - _resizeStartMouse.X, dy = p.Y - _resizeStartMouse.Y;
            double L = _resizeRect.X, T = _resizeRect.Y, W = _resizeRect.Width, H = _resizeRect.Height;

            bool left = _resizeHandle is Handle.TL or Handle.L or Handle.BL;
            bool right = _resizeHandle is Handle.TR or Handle.R or Handle.BR;
            bool top = _resizeHandle is Handle.TL or Handle.T or Handle.TR;
            bool bottom = _resizeHandle is Handle.BL or Handle.B or Handle.BR;

            if (left) { L = _resizeRect.X + dx; W = _resizeRect.Width - dx; if (W < min) { W = min; L = _resizeRect.Right - min; } }
            if (right) { W = _resizeRect.Width + dx; if (W < min) W = min; }
            if (top) { T = _resizeRect.Y + dy; H = _resizeRect.Height - dy; if (H < min) { H = min; T = _resizeRect.Bottom - min; } }
            if (bottom) { H = _resizeRect.Height + dy; if (H < min) H = min; }

            if (_snap)
            {
                L = Math.Round(L / GridSize) * GridSize; T = Math.Round(T / GridSize) * GridSize;
                W = Math.Max(min, Math.Round(W / GridSize) * GridSize); H = Math.Max(min, Math.Round(H / GridSize) * GridSize);
            }

            _selected.X = L; _selected.Y = T; _selected.Width = W; _selected.Height = H;
            if (_visuals.TryGetValue(_selected, out var el) && el is Border b)
            {
                b.Width = W; b.Height = H; Canvas.SetLeft(b, L); Canvas.SetTop(b, T);
                // re-tile if needed
                if (_selected.TileImage && b.Background is ImageBrush) { /* viewport is absolute, stays */ }
            }
            PositionHandles(_selected);
        }

        private void GridToggle_Click(object sender, RoutedEventArgs e)
        {
            _snap = !_snap;
            GridToggle.Content = _snap ? "Grid: on" : "Grid: off";
            RenderScene();
        }

        // ============================================================
        //  SELECTION
        // ============================================================
        // single selection (clears any multi-selection)
        private void Select(GameObjectData? obj)
        {
            _selection.Clear();
            if (obj != null) _selection.Add(obj);
            SetPrimary(obj);
        }

        // Ctrl+click: add/remove from the selection set
        private void ToggleSelect(GameObjectData obj)
        {
            if (_selection.Contains(obj)) { _selection.Remove(obj); SetPrimary(_selection.LastOrDefault()); }
            else { _selection.Add(obj); SetPrimary(obj); }
        }

        private void SetPrimary(GameObjectData? obj)
        {
            _selected = obj;
            // load tilemap edit state before building the inspector (which reads it)
            if (obj != null && obj.Behaviors.Any(b => b.Kind == BehaviorKind.Tilemap)) LoadTilemapEdit(obj);
            else { _tmObj = null; _tmBh = null; }

            UpdateSelectionOutline();
            // rebuild handles for the new primary
            foreach (var r in _handles.Values) SceneCanvas.Children.Remove(r);
            _handles.Clear();
            AddResizeHandles();
            BuildInspector(obj);
            UpdateAiTarget();

            if (obj != null)
            {
                var layer = _scene.Layers.FirstOrDefault(l => l.Objects.Contains(obj));
                if (layer != null && layer != _activeLayer)
                {
                    int li = _scene.Layers.IndexOf(layer);
                    if (LayersList.SelectedIndex != li) LayersList.SelectedIndex = li;
                }
                if (!ReferenceEquals(ObjectsList.SelectedItem is ListBoxItem it ? it.Tag : null, obj))
                    SelectInObjectsList(obj);
            }
        }

        private void UpdateAiTarget()
        {
            if (AiTarget == null) return;
            AiTarget.Text = _selection.Count == 0
                ? "Target: auto-detect (the AI figures out what to edit, or creates/edits anything)."
                : _selection.Count == 1
                    ? $"Target: {_selection[0].Name} (AI edits only this)."
                    : $"Target: {string.Join(", ", _selection.Select(o => o.Name))} (AI edits these {_selection.Count}).";
        }

        private void SelectInObjectsList(GameObjectData obj)
        {
            foreach (ListBoxItem item in ObjectsList.Items)
                if (ReferenceEquals(item.Tag, obj)) { ObjectsList.SelectedItem = item; break; }
        }

        // ============================================================
        //  PREFAB PALETTE
        // ============================================================
        private void RefreshPalette()
        {
            PaletteList.Children.Clear();
            var filter = _prefabFilter.Trim();
            var groups = _project.Prefabs
                .Where(p => filter.Length == 0 || p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .GroupBy(p => string.IsNullOrWhiteSpace(p.Category) ? "General" : p.Category)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var g in groups)
            {
                bool collapsed = _collapsedCats.Contains(g.Key) && filter.Length == 0;
                PaletteList.Children.Add(MakeCategoryHeader(g.Key, g.Count(), collapsed));
                if (collapsed) continue;
                foreach (var pf in g) PaletteList.Children.Add(MakePrefabRow(pf));
            }
            if (PaletteList.Children.Count == 0)
                PaletteList.Children.Add(new TextBlock { Text = "No prefabs match.", Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11, Margin = new Thickness(2, 4, 0, 0) });
        }

        private FrameworkElement MakeCategoryHeader(string name, int count, bool collapsed)
        {
            var btn = new Button
            {
                Content = $"{(collapsed ? "▸" : "▾")}  {name}  ({count})",
                Style = (Style)FindResource("GhostButton"),
                Height = 26, FontSize = 11, FontWeight = FontWeights.SemiBold,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 0, 2),
                Foreground = (SolidColorBrush)FindResource("TextDim")
            };
            btn.Click += (s, e) => { if (!_collapsedCats.Remove(name)) _collapsedCats.Add(name); RefreshPalette(); };
            return btn;
        }

        private void PrefabSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _prefabFilter = PrefabSearch.Text ?? "";
            RefreshPalette();
        }

        private FrameworkElement MakePrefabRow(PrefabDef pf)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var add = new Button { Content = $"{pf.Icon}  {pf.Name}", Style = (Style)FindResource("CardButton"), Height = 36, HorizontalContentAlignment = HorizontalAlignment.Left };
            add.Click += (s, e) => AddPrefabInstance(pf);

            var del = new Button { Content = "Delete", Style = (Style)FindResource("GhostButton"), Height = 36, FontSize = 11, Foreground = (SolidColorBrush)FindResource("Danger"), Margin = new Thickness(4, 0, 0, 0) };
            del.Click += (s, e) => DeletePrefab(pf);

            Grid.SetColumn(add, 0); Grid.SetColumn(del, 1);
            grid.Children.Add(add); grid.Children.Add(del);
            return grid;
        }

        private void AddPrefabInstance(PrefabDef pf)
        {
            var obj = JsonClone(pf.Template);
            AddToActiveLayer(obj, pf.Name);
        }

        private void AddEmpty_Click(object sender, RoutedEventArgs e)
        {
            var obj = new GameObjectData { Type = "Object", Color = "#9A9AA0", Width = 64, Height = 64 };
            AddToActiveLayer(obj, "Object");
        }

        private void AddToActiveLayer(GameObjectData obj, string baseName)
        {
            var layer = _activeLayer ?? _scene.Layers.FirstOrDefault();
            if (layer == null) { Status("Create a layer first"); return; }
            PushUndo();
            obj.Id = _project.NextId;
            obj.Name = UniqueName(baseName);
            obj.X = _scene.Width / 2 - obj.Width / 2;
            obj.Y = _scene.Height / 2 - obj.Height / 2;
            if (obj.Behaviors.Any(b => b.Kind == BehaviorKind.Tilemap)) { obj.X = 0; obj.Y = 0; } // tilemaps anchor at the scene origin
            layer.Objects.Add(obj);
            RenderScene(); RefreshObjects(); Select(obj); Dirty();
            Status($"{obj.Name} added to \"{layer.Name}\"");
        }

        private void DeletePrefab(PrefabDef pf)
        {
            if (MessageBox.Show($"Remove prefab \"{pf.Name}\" from the palette?\n(objects already placed in scenes are NOT affected)",
                "Delete prefab", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _project.Prefabs.Remove(pf);
            RefreshPalette(); Dirty();
            Status($"Prefab \"{pf.Name}\" removed");
        }

        private void SavePrefab_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) { Status("Select an object to save as a prefab"); return; }
            SaveAsPrefab(_selected);
        }

        private void SaveAsPrefab(GameObjectData obj)
        {
            var name = InputDialog.Prompt(this, "Save as prefab", "Prefab name:", obj.Name);
            if (name == null) return;
            var existingCats = _project.Prefabs.Select(p => p.Category).Distinct().Where(c => !string.IsNullOrWhiteSpace(c));
            var hint = existingCats.Any() ? " (existing: " + string.Join(", ", existingCats) + ")" : "";
            var cat = InputDialog.Prompt(this, "Category", "Category — groups it in the palette" + hint + ":", string.IsNullOrWhiteSpace(obj.Group) ? "General" : obj.Group);
            if (string.IsNullOrWhiteSpace(cat)) cat = "General";
            var icon = _project.Prefabs.FirstOrDefault(p => p.Name == obj.Type)?.Icon ?? "▢";
            var tmpl = JsonClone(obj);
            tmpl.Id = 0; tmpl.X = 0; tmpl.Y = 0;
            _project.Prefabs.Add(new PrefabDef { Name = name, Icon = icon, Category = cat, Template = tmpl });
            RefreshPalette(); Dirty();
            Status($"Saved \"{name}\" as a prefab under \"{cat}\"");
        }

        private string UniqueName(string baseName)
        {
            bool Exists(string n) => _scene.Layers.SelectMany(l => l.Objects).Any(o => o.Name == n);
            if (!Exists(baseName)) return baseName;
            int i = 2; while (Exists($"{baseName}_{i}")) i++;
            return $"{baseName}_{i}";
        }

        private static T JsonClone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

        private void DeleteSelected()
        {
            if (_selection.Count == 0) return;
            PushUndo();
            foreach (var o in _selection.ToList())
                _scene.Layers.FirstOrDefault(l => l.Objects.Contains(o))?.Objects.Remove(o);
            Select(null);
            RenderScene(); RefreshObjects(); Dirty();
        }

        private void DuplicateSelected()
        {
            if (_selected == null) return;
            var layer = _scene.Layers.FirstOrDefault(l => l.Objects.Contains(_selected));
            if (layer == null) return;
            PushUndo();
            var name = _selected.Name;
            var clone = JsonSerializer.Deserialize<GameObjectData>(JsonSerializer.Serialize(_selected))!;
            clone.Id = _project.NextId;
            clone.Name = name + " copy";
            clone.X += 24; clone.Y += 24;
            layer.Objects.Add(clone);
            RenderScene(); RefreshObjects(); Select(clone); Dirty();
            Status($"Duplicated {name}");
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (Keyboard.FocusedElement is TextBox) return;

            if (e.Key == Key.Delete && _selected != null) { DeleteSelected(); e.Handled = true; }
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) { Undo(); e.Handled = true; }
            else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control) { Redo(); e.Handled = true; }
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { Save_Click(this, new RoutedEventArgs()); e.Handled = true; }
            else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control) { DuplicateSelected(); e.Handled = true; }
            else if (e.Key == Key.D0 && Keyboard.Modifiers == ModifierKeys.Control) { FitZoom(); e.Handled = true; }
            else if ((e.Key == Key.OemPlus || e.Key == Key.Add) && Keyboard.Modifiers == ModifierKeys.Control) { ZoomButton(1.25); e.Handled = true; }
            else if ((e.Key == Key.OemMinus || e.Key == Key.Subtract) && Keyboard.Modifiers == ModifierKeys.Control) { ZoomButton(1 / 1.25); e.Handled = true; }
            else if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control) { ShowLastAiChange(); e.Handled = true; }
        }

        // ============================================================
        //  SCENE ZOOM
        // ============================================================
        // Fit the whole scene once layout has settled (viewport size is known).
        private void QueueFit() => Dispatcher.BeginInvoke(new Action(FitZoom), DispatcherPriority.Loaded);

        private void UpdateZoomLabel() => ZoomLabel.Text = Math.Round(_zoom * 100) + "%";

        // Set zoom while keeping the scene point `scenePt` pinned under the viewport point `viewAnchor`.
        private void ApplyZoom(double newZoom, Point scenePt, Point viewAnchor)
        {
            newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
            _zoom = newZoom;
            SceneScale.ScaleX = SceneScale.ScaleY = newZoom;
            UpdateZoomLabel();
            SceneScroll.UpdateLayout(); // extent reflects the new scale before we scroll
            SceneScroll.ScrollToHorizontalOffset(scenePt.X * newZoom - viewAnchor.X);
            SceneScroll.ScrollToVerticalOffset(scenePt.Y * newZoom - viewAnchor.Y);
        }

        // Zoom from a button/shortcut, anchored at the center of the viewport.
        private void ZoomButton(double factor)
        {
            double cx = SceneScroll.HorizontalOffset + SceneScroll.ViewportWidth / 2;
            double cy = SceneScroll.VerticalOffset + SceneScroll.ViewportHeight / 2;
            var scenePt = new Point(cx / _zoom, cy / _zoom);
            ApplyZoom(_zoom * factor, scenePt, new Point(SceneScroll.ViewportWidth / 2, SceneScroll.ViewportHeight / 2));
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomButton(1.25);
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomButton(1 / 1.25);
        private void ZoomFit_Click(object sender, RoutedEventArgs e) => FitZoom();

        private void SceneScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return; // plain wheel = normal scroll
            e.Handled = true;
            var scenePt = e.GetPosition(SceneCanvas);   // scene coords under the cursor (unscaled)
            var viewAnchor = e.GetPosition(SceneScroll); // same cursor, in viewport pixels
            double factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
            ApplyZoom(_zoom * factor, scenePt, viewAnchor);
        }

        // Scale the scene so it fits the current viewport, then center it.
        private void FitZoom()
        {
            double availW = SceneScroll.ViewportWidth, availH = SceneScroll.ViewportHeight;
            if (availW <= 0 || availH <= 0) { availW = SceneScroll.ActualWidth; availH = SceneScroll.ActualHeight; }
            if (availW <= 0 || availH <= 0 || _scene.Width <= 0 || _scene.Height <= 0) return;
            const double pad = 40;
            double z = Math.Min((availW - pad) / _scene.Width, (availH - pad) / _scene.Height);
            z = Math.Clamp(z, MinZoom, MaxZoom);
            _zoom = z;
            SceneScale.ScaleX = SceneScale.ScaleY = z;
            UpdateZoomLabel();
            // content is now smaller than the viewport, so centering is automatic
        }

        // Middle-mouse drag pans the scene around.
        private void SceneScroll_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            _panning = true;
            _panStartMouse = e.GetPosition(SceneScroll);
            _panStartH = SceneScroll.HorizontalOffset;
            _panStartV = SceneScroll.VerticalOffset;
            SceneScroll.Cursor = Cursors.ScrollAll;
            SceneScroll.CaptureMouse();
            e.Handled = true;
        }

        private void SceneScroll_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_panning) return;
            var p = e.GetPosition(SceneScroll);
            SceneScroll.ScrollToHorizontalOffset(_panStartH - (p.X - _panStartMouse.X));
            SceneScroll.ScrollToVerticalOffset(_panStartV - (p.Y - _panStartMouse.Y));
            e.Handled = true;
        }

        private void SceneScroll_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle || !_panning) return;
            _panning = false;
            SceneScroll.ReleaseMouseCapture();
            SceneScroll.Cursor = Cursors.Arrow;
            e.Handled = true;
        }

        // ============================================================
        //  LAYERS
        // ============================================================
        private void RefreshLayers()
        {
            LayersList.Items.Clear();
            foreach (var layer in _scene.Layers)
                LayersList.Items.Add(MakeLayerItem(layer));
        }

        private ListBoxItem MakeLayerItem(Layer layer)
        {
            var eye = new Button
            {
                Content = layer.Visible ? "shown" : "hidden", Width = 50, Height = 22, FontSize = 10,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = (SolidColorBrush)FindResource(layer.Visible ? "Text" : "TextDim"), Cursor = Cursors.Hand, Tag = layer
            };
            eye.Click += (s, e) => { layer.Visible = !layer.Visible; eye.Content = layer.Visible ? "shown" : "hidden"; eye.Foreground = (SolidColorBrush)FindResource(layer.Visible ? "Text" : "TextDim"); RenderScene(); Dirty(); e.Handled = true; };

            var name = new TextBlock { Text = layer.Name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            var kind = new TextBlock { Text = layer.Kind.ToString(), Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };

            bool Fixed() => layer.ScreenSpace || layer.Kind == LayerKind.UI;
            var fix = new Button { Content = Fixed() ? "fixed" : "scroll", Width = 46, Height = 22, FontSize = 10, Style = (Style)FindResource("GhostButton"), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Fixed = stays on screen (UI/HUD). Scroll = moves with the world." };
            fix.Click += (s, e) => { e.Handled = true; PushUndo(); layer.ScreenSpace = !Fixed(); fix.Content = Fixed() ? "fixed" : "scroll"; Dirty(); };

            var del = new Button { Content = "Delete", Height = 22, FontSize = 10, Style = (Style)FindResource("GhostButton"), Foreground = (SolidColorBrush)FindResource("Danger"), VerticalAlignment = VerticalAlignment.Center };
            del.Click += (s, e) => { e.Handled = true; DeleteLayer(layer); };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(eye, 0); Grid.SetColumn(name, 1); Grid.SetColumn(kind, 2); Grid.SetColumn(fix, 3); Grid.SetColumn(del, 4);
            grid.Children.Add(eye); grid.Children.Add(name); grid.Children.Add(kind); grid.Children.Add(fix); grid.Children.Add(del);

            return new ListBoxItem { Content = grid, Tag = layer, Padding = new Thickness(4, 4, 4, 4), Foreground = (SolidColorBrush)FindResource("Text") };
        }

        private void DeleteLayer(Layer layer)
        {
            if (_scene.Layers.Count <= 1) { MessageBox.Show("A scene needs at least one layer."); return; }
            if (layer.Objects.Count > 0 &&
                MessageBox.Show($"Delete layer \"{layer.Name}\" and its {layer.Objects.Count} object(s)?", "Delete layer", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            PushUndo();
            _scene.Layers.Remove(layer);
            RefreshLayers();
            LayersList.SelectedIndex = 0;
            RenderScene(); Dirty();
        }

        private void LayersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LayersList.SelectedItem is ListBoxItem item && item.Tag is Layer layer)
            {
                _activeLayer = layer;
                ObjectsHeader.Text = $"OBJECTS — {layer.Name}".ToUpper();
                RefreshObjects();
            }
        }

        private void AddLayer_Click(object sender, RoutedEventArgs e)
        {
            var name = InputDialog.Prompt(this, "New layer", "Layer name:", $"Layer {_scene.Layers.Count + 1}");
            if (name == null) return;
            PushUndo();
            _scene.Layers.Add(new Layer { Name = name, Kind = LayerKind.Custom });
            RefreshLayers();
            LayersList.SelectedIndex = _scene.Layers.Count - 1;
            RenderScene(); Dirty();
        }

        // ============================================================
        //  OBJECTS IN LAYER
        // ============================================================
        private void RefreshObjects()
        {
            ObjectsList.Items.Clear();
            if (_activeLayer == null) return;
            var filter = _objectFilter.Trim();
            var objs = _activeLayer.Objects.Where(o =>
                filter.Length == 0 || o.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || o.Type.Contains(filter, StringComparison.OrdinalIgnoreCase));
            bool anyGroup = _activeLayer.Objects.Any(o => !string.IsNullOrWhiteSpace(o.Group));
            var groups = objs
                .GroupBy(o => string.IsNullOrWhiteSpace(o.Group) ? "" : o.Group)
                .OrderBy(g => g.Key.Length == 0 ? "￿" : g.Key, StringComparer.OrdinalIgnoreCase); // ungrouped last

            foreach (var g in groups)
            {
                if (anyGroup)
                    ObjectsList.Items.Add(new ListBoxItem
                    {
                        Content = (g.Key.Length == 0 ? "Ungrouped" : g.Key).ToUpperInvariant(),
                        IsHitTestVisible = false, Focusable = false,
                        Foreground = (SolidColorBrush)FindResource("TextDim"),
                        FontSize = 10, FontWeight = FontWeights.SemiBold, Padding = new Thickness(6, 6, 6, 2)
                    });
                foreach (var obj in g)
                    ObjectsList.Items.Add(new ListBoxItem { Content = $"{obj.Name}   ·   {obj.Type}", Tag = obj, Padding = new Thickness(anyGroup ? 18 : 8, 5, 8, 5), Foreground = (SolidColorBrush)FindResource("Text") });
            }
        }

        private void ObjectSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _objectFilter = ObjectSearch.Text ?? "";
            RefreshObjects();
        }

        private void ObjectsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ObjectsList.SelectedItem is ListBoxItem item && item.Tag is GameObjectData obj && !ReferenceEquals(obj, _selected))
                Select(obj);
        }

        // ============================================================
        //  INSPECTOR
        // ============================================================
        private void BuildInspector(GameObjectData? obj)
        {
            InspectorRoot.Children.Clear();
            if (obj == null) { BuildSceneInspector(); return; }

            Section("OBJECT");
            TextRow("Name", obj.Name, v => { obj.Name = v; RefreshObjects(); Dirty(); });
            InfoRow("Type", obj.Type);
            TextRow("Group", obj.Group, v => { obj.Group = v.Trim(); RefreshObjects(); Dirty(); });
            var rowBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            var dup = new Button { Content = "Duplicate", Style = (Style)FindResource("CardButton"), Height = 30, Padding = new Thickness(10, 0, 10, 0), HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            dup.Click += (s, e) => DuplicateSelected();
            var del = new Button { Content = "Delete", Style = (Style)FindResource("CardButton"), Height = 30, Padding = new Thickness(10, 0, 10, 0), HorizontalContentAlignment = HorizontalAlignment.Center, Foreground = (SolidColorBrush)FindResource("Danger"), Margin = new Thickness(0, 0, 8, 0) };
            del.Click += (s, e) => DeleteSelected();
            var toPrefab = new Button { Content = "Save as Prefab", Style = (Style)FindResource("CardButton"), Height = 30, Padding = new Thickness(10, 0, 10, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
            toPrefab.Click += (s, e) => SaveAsPrefab(obj);
            rowBtns.Children.Add(dup); rowBtns.Children.Add(del); rowBtns.Children.Add(toPrefab);
            InspectorRoot.Children.Add(rowBtns);

            Section("TRANSFORM");
            NumRow("X", obj.X, v => { obj.X = v; ReposSelected(); Dirty(); });
            NumRow("Y", obj.Y, v => { obj.Y = v; ReposSelected(); Dirty(); });
            NumRow("Width", obj.Width, v => { obj.Width = Math.Max(2, v); RenderScene(); Dirty(); });
            NumRow("Height", obj.Height, v => { obj.Height = Math.Max(2, v); RenderScene(); Dirty(); });
            NumRow("Rotation", obj.Rotation, v => { obj.Rotation = v; RenderScene(); Dirty(); });

            Section("APPEARANCE");
            ColorRow("Color", obj.Color, v => { obj.Color = v; RenderScene(); Dirty(); });
            AnimationsRow(obj);
            SpriteRow(obj);
            if (obj.SpritePath != null || obj.Animations.Count > 0)
                ComboRow("Image fit", new[] { "Stretch", "Tile (repeat)" }, obj.TileImage ? 1 : 0, i => { obj.TileImage = i == 1; RenderScene(); Dirty(); });
            NumRow("Opacity", obj.Opacity, v => { obj.Opacity = Math.Clamp(v, 0, 1); RenderScene(); Dirty(); });
            NumRow("Corner radius", obj.CornerRadius < 0 ? 4 : obj.CornerRadius, v => { obj.CornerRadius = Math.Max(0, v); RenderScene(); Dirty(); });
            ColorRow("Border color", obj.BorderColor ?? "#000000", v => { obj.BorderColor = v; Dirty(); });
            NumRow("Border width", obj.BorderWidth, v => { obj.BorderWidth = Math.Max(0, v); Dirty(); });
            if (obj.Behaviors.Any(b => b.Kind == BehaviorKind.Label))
            {
                TextRow("Font", obj.FontFamily ?? "", v => { obj.FontFamily = string.IsNullOrWhiteSpace(v) ? null : v; Dirty(); });
                ComboRow("Bold", new[] { "No", "Yes" }, obj.Bold ? 1 : 0, i => { obj.Bold = i == 1; Dirty(); });
            }

            Section("PHYSICS");
            ComboRow("Mode", new[] { "None", "Static", "Dynamic" }, (int)obj.Physics.Mode, i => { obj.Physics.Mode = (PhysicsMode)i; Dirty(); });
            NumRow("Gravity ×", obj.Physics.GravityScale, v => { obj.Physics.GravityScale = v; Dirty(); });
            NumRow("Bounciness", obj.Physics.Bounciness, v => { obj.Physics.Bounciness = Math.Clamp(v, 0, 1); Dirty(); });

            Section("BEHAVIORS");
            foreach (var bh in obj.Behaviors.ToList())
                BehaviorCard(obj, bh);
            if (_selection.Count > 1)
                InspectorRoot.Children.Add(new TextBlock
                {
                    Text = $"＋ adds to all {_selection.Count} selected objects.",
                    Foreground = (SolidColorBrush)FindResource("Accent"),
                    FontSize = 11, Margin = new Thickness(0, 4, 0, 2), TextWrapping = TextWrapping.Wrap
                });
            AddBehaviorControl(obj);
        }

        private void BuildSceneInspector()
        {
            Section("SCENE");
            TextRow("Name", _scene.Name, v => { _scene.Name = v; RefreshSceneCombo(); SelectSceneInCombo(_scene); Dirty(); });
            NumRow("Width", _scene.Width, v => { _scene.Width = v; SceneFrame.Width = v; Dirty(); });
            NumRow("Height", _scene.Height, v => { _scene.Height = v; SceneFrame.Height = v; Dirty(); });
            ColorRow("Background", _scene.BackgroundColor, v => { _scene.BackgroundColor = v; RenderScene(); Dirty(); });

            Section("LIGHT");
            NumRow("Ambient light (0-1)", _scene.AmbientLight, v => { _scene.AmbientLight = Math.Clamp(v, 0, 1); Dirty(); });

            InspectorRoot.Children.Add(new TextBlock
            {
                Text = "Select an object to edit it, or use the AI tab.",
                Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0)
            });
        }

        private void ReposSelected()
        {
            if (_selected != null && _visuals.TryGetValue(_selected, out var el))
            { Canvas.SetLeft(el, _selected.X); Canvas.SetTop(el, _selected.Y); PositionHandles(_selected); }
        }

        private void RefreshInspectorTransform() { if (_selected != null) BuildInspector(_selected); }

        // ---- animations ----
        private void AnimationsRow(GameObjectData obj)
        {
            var grid = RowGrid("Animations");
            var inner = new StackPanel();
            var open = new Button { Content = "🎞 Edit Animations…", Style = (Style)FindResource("CardButton"), Height = 32, HorizontalContentAlignment = HorizontalAlignment.Center };
            open.Click += (s, e) =>
            {
                PushUndo();
                var win = new AnimationWindow(_project, obj) { Owner = this };
                win.ShowDialog();
                RenderScene(); BuildInspector(obj); Dirty();
            };
            inner.Children.Add(open);
            string summary = obj.Animations.Count == 0 ? "No animations yet" : $"{obj.Animations.Count} animation(s) · default: {obj.DefaultAnimation ?? obj.Animations.First().Name}";
            inner.Children.Add(new TextBlock { Text = summary, Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(inner, 1);
            grid.Children.Add(inner);
        }

        private void SpriteRow(GameObjectData obj)
        {
            var grid = RowGrid("Static image");
            var inner = new StackPanel { Orientation = Orientation.Horizontal };
            var pick = new Button { Content = obj.SpritePath == null ? "Choose…" : Path.GetFileName(obj.SpritePath), Style = (Style)FindResource("CardButton"), Height = 28, Padding = new Thickness(8, 0, 8, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
            pick.Click += (s, e) => PickSprite(obj);
            inner.Children.Add(pick);
            if (obj.SpritePath != null)
            {
                var clear = new Button { Content = "✕", Style = (Style)FindResource("GhostButton"), Height = 28, Width = 28, Margin = new Thickness(4, 0, 0, 0), Foreground = (SolidColorBrush)FindResource("Danger") };
                clear.Click += (s, e) => { PushUndo(); obj.SpritePath = null; RenderScene(); BuildInspector(obj); Dirty(); };
                inner.Children.Add(clear);
            }
            Grid.SetColumn(inner, 1);
            grid.Children.Add(inner);
        }

        private void PickSprite(GameObjectData obj)
        {
            var dlg = new OpenFileDialog { Title = "Choose image", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
            if (dlg.ShowDialog() == true)
            {
                try { PushUndo(); obj.SpritePath = ProjectIO.ImportAsset(_project, dlg.FileName, "sprites"); RenderScene(); BuildInspector(obj); Dirty(); Status($"Image set on {obj.Name}"); }
                catch (Exception ex) { MessageBox.Show("Error: " + ex.Message); }
            }
        }

        // ---- behavior card ----
        private void BehaviorCard(GameObjectData obj, BehaviorData bh)
        {
            var info = BehaviorCatalog.Info(bh.Kind);
            var panel = new StackPanel();

            var header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new TextBlock { Text = $"{info.Icon}  {info.Name}", FontWeight = FontWeights.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var remove = new Button { Content = "Remove", Height = 24, FontSize = 11, Padding = new Thickness(8, 0, 8, 0), Style = (Style)FindResource("CardButton"), Foreground = (SolidColorBrush)FindResource("Danger") };
            remove.Click += (s, e) => { PushUndo(); obj.Behaviors.Remove(bh); BuildInspector(obj); Dirty(); };
            Grid.SetColumn(title, 0); Grid.SetColumn(remove, 1);
            header.Children.Add(title); header.Children.Add(remove);
            panel.Children.Add(header);

            if (bh.Kind == BehaviorKind.Tilemap)
            {
                TilemapRow(obj, bh, panel);
            }
            else
            {
                foreach (var np in info.Nums)
                {
                    if (np.Label.Contains("(0/1)"))
                        CheckRow(np.Label.Replace("(0/1)", "").Trim(), bh.Num(np.Key, np.Default) >= 0.5, v => { bh.Nums[np.Key] = v ? 1 : 0; Dirty(); }, panel);
                    else
                        NumRow(np.Label, bh.Num(np.Key, np.Default), v => { bh.Nums[np.Key] = v; Dirty(); }, panel);
                }
                foreach (var tp in info.Texts)
                {
                    if (bh.Kind == BehaviorKind.SoundEmitter && tp.Key == "asset") SoundRow(obj, bh, panel);
                    else if (bh.Kind == BehaviorKind.GoToScene && tp.Key == "scene") ScenePickerRow(bh, panel);
                    else if (bh.Kind == BehaviorKind.Custom && tp.Key == "script") ScriptRow(bh, panel);
                    else TextRow(tp.Label, bh.Text(tp.Key, tp.Default), v => { bh.Texts[tp.Key] = v; Dirty(); }, panel);
                }
            }

            InspectorRoot.Children.Add(new Border
            {
                Background = (SolidColorBrush)FindResource("Bg2"),
                BorderBrush = (SolidColorBrush)FindResource("Border"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8), Child = panel
            });
        }

        private void AddBehaviorControl(GameObjectData obj)
        {
            var combo = new ComboBox { Margin = new Thickness(0, 2, 0, 0) };
            combo.Items.Add(new ComboBoxItem { Content = "＋ Add behavior…", Tag = null, IsSelected = true });
            foreach (var info in BehaviorCatalog.All)
                combo.Items.Add(new ComboBoxItem { Content = $"{info.Icon}  {info.Name}", Tag = info.Kind });
            combo.SelectionChanged += (s, e) =>
            {
                if (combo.SelectedItem is ComboBoxItem ci && ci.Tag is BehaviorKind kind)
                {
                    PushUndo();
                    // apply to EVERY selected object, not just the primary; skip ones that already have it
                    var targets = _selection.Count > 1 ? _selection.ToList() : new List<GameObjectData> { obj };
                    int added = 0;
                    foreach (var t in targets)
                    {
                        if (t.Behaviors.Any(b => b.Kind == kind)) continue;
                        t.Behaviors.Add(BehaviorCatalog.Info(kind).CreateData());
                        added++;
                    }
                    BuildInspector(obj); RenderScene(); Dirty();
                    if (targets.Count > 1) Status($"Added {BehaviorCatalog.Info(kind).Name} to {added} of {targets.Count} selected object(s)");
                }
            };
            InspectorRoot.Children.Add(combo);
        }

        private void ScriptRow(BehaviorData bh, Panel target)
        {
            target.Children.Add(new TextBlock { Text = "Rules (JSON) — usually written by the AI:", Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11, Margin = new Thickness(0, 2, 0, 4), TextWrapping = TextWrapping.Wrap });
            var tb = new TextBox
            {
                Text = bh.Text("script", "[]"),
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 130,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"), FontSize = 11,
                VerticalContentAlignment = VerticalAlignment.Top
            };
            tb.LostFocus += (s, e) => { PushUndo(); bh.Texts["script"] = tb.Text; Dirty(); };
            target.Children.Add(tb);
        }

        // ============================================================
        //  TILEMAP EDITING
        // ============================================================
        private (double ts, int cols, int rows, int[] cells, List<(string color, bool solid)> pal) ReadTilemap(BehaviorData bh)
        {
            double ts = Math.Max(4, bh.Num("tileSize", 32));
            var pal = new List<(string, bool)>();
            try
            {
                var pj = bh.Text("palette", "");
                if (!string.IsNullOrWhiteSpace(pj))
                    using (var doc = JsonDocument.Parse(pj))
                        foreach (var e in doc.RootElement.EnumerateArray())
                        {
                            string color = e.TryGetProperty("color", out var c) ? (c.GetString() ?? "#888888") : "#888888";
                            bool solid = e.TryGetProperty("solid", out var s) && (s.ValueKind == JsonValueKind.True || (s.ValueKind == JsonValueKind.Number && s.GetDouble() >= 0.5));
                            pal.Add((color, solid));
                        }
            }
            catch { }
            if (pal.Count == 0) { pal.Add(("#5BA862", true)); pal.Add(("#6B5A45", true)); pal.Add(("#3A3A46", true)); }

            int cols = Math.Max(1, (int)Math.Ceiling(_scene.Width / ts));
            int rows = Math.Max(1, (int)Math.Ceiling(_scene.Height / ts));
            int[] cells = new int[cols * rows];
            try
            {
                var dj = bh.Text("data", "");
                if (!string.IsNullOrWhiteSpace(dj))
                    using (var doc = JsonDocument.Parse(dj))
                    {
                        var r = doc.RootElement;
                        cols = r.GetProperty("cols").GetInt32();
                        rows = r.GetProperty("rows").GetInt32();
                        cells = new int[cols * rows];
                        int i = 0;
                        foreach (var v in r.GetProperty("cells").EnumerateArray()) { if (i < cells.Length) cells[i++] = v.GetInt32(); }
                    }
            }
            catch { }
            return (ts, cols, rows, cells, pal);
        }

        private void LoadTilemapEdit(GameObjectData obj)
        {
            _tmBh = obj.Behaviors.FirstOrDefault(b => b.Kind == BehaviorKind.Tilemap);
            if (_tmBh == null) { _tmObj = null; return; }
            _tmObj = obj;
            var (ts, cols, rows, cells, pal) = ReadTilemap(_tmBh);
            _tmSize = ts; _tmCols = cols; _tmRows = rows; _tmCells = cells;
            _tmPalette.Clear(); _tmPalette.AddRange(pal);
            if (_tmActive > _tmPalette.Count) _tmActive = _tmPalette.Count >= 1 ? 1 : 0;
            SerializeTilemap();
        }

        private void SerializeTilemap()
        {
            if (_tmBh == null) return;
            _tmBh.Texts["palette"] = JsonSerializer.Serialize(_tmPalette.Select(p => new { color = p.color, solid = p.solid }));
            _tmBh.Texts["data"] = JsonSerializer.Serialize(new { cols = _tmCols, rows = _tmRows, cells = _tmCells });
            _tmBh.Nums["tileSize"] = _tmSize;
        }

        private void DrawTilemap(GameObjectData obj, bool active)
        {
            var bh = obj.Behaviors.First(b => b.Kind == BehaviorKind.Tilemap);
            double ts; int cols, rows; int[] cells; List<(string color, bool solid)> pal;
            if (active && ReferenceEquals(obj, _tmObj)) { ts = _tmSize; cols = _tmCols; rows = _tmRows; cells = _tmCells; pal = _tmPalette; }
            else { var t = ReadTilemap(bh); ts = t.ts; cols = t.cols; rows = t.rows; cells = t.cells; pal = t.pal; }

            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int val = cells[r * cols + c];
                    if (val <= 0 || val > pal.Count) continue;
                    var rect = new Rectangle { Width = ts, Height = ts, Fill = BrushFrom(pal[val - 1].color, "#888"), IsHitTestVisible = false };
                    Canvas.SetLeft(rect, obj.X + c * ts); Canvas.SetTop(rect, obj.Y + r * ts);
                    SceneCanvas.Children.Add(rect);
                    if (active) _tmCellRects[r * cols + c] = rect;
                }

            if (active)
            {
                var gb = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
                for (int c = 0; c <= cols; c++) SceneCanvas.Children.Add(new Line { X1 = obj.X + c * ts, Y1 = obj.Y, X2 = obj.X + c * ts, Y2 = obj.Y + rows * ts, Stroke = gb, StrokeThickness = 1, IsHitTestVisible = false });
                for (int r = 0; r <= rows; r++) SceneCanvas.Children.Add(new Line { X1 = obj.X, Y1 = obj.Y + r * ts, X2 = obj.X + cols * ts, Y2 = obj.Y + r * ts, Stroke = gb, StrokeThickness = 1, IsHitTestVisible = false });
            }
        }

        private void PaintAt(Point p, bool erase)
        {
            if (_tmObj == null || _tmCells.Length == 0) return;
            int c = (int)Math.Floor((p.X - _tmObj.X) / _tmSize);
            int r = (int)Math.Floor((p.Y - _tmObj.Y) / _tmSize);
            if (c < 0 || r < 0 || c >= _tmCols || r >= _tmRows) return;
            int idx = r * _tmCols + c;
            int val = erase ? 0 : _tmActive;
            if (idx >= _tmCells.Length || _tmCells[idx] == val) return;
            _tmCells[idx] = val;
            UpdateCellRect(idx, c, r);
        }

        private void UpdateCellRect(int idx, int c, int r)
        {
            if (_tmObj == null) return;
            if (_tmCellRects.TryGetValue(idx, out var old)) { SceneCanvas.Children.Remove(old); _tmCellRects.Remove(idx); }
            int val = _tmCells[idx];
            if (val > 0 && val <= _tmPalette.Count)
            {
                var rect = new Rectangle { Width = _tmSize, Height = _tmSize, Fill = BrushFrom(_tmPalette[val - 1].color, "#888"), IsHitTestVisible = false };
                Canvas.SetLeft(rect, _tmObj.X + c * _tmSize); Canvas.SetTop(rect, _tmObj.Y + r * _tmSize);
                SceneCanvas.Children.Add(rect);
                _tmCellRects[idx] = rect;
            }
        }

        private void ResizeGrid(int cols, int rows)
        {
            cols = Math.Clamp(cols, 1, 400); rows = Math.Clamp(rows, 1, 400);
            var nc = new int[cols * rows];
            for (int r = 0; r < Math.Min(rows, _tmRows); r++)
                for (int c = 0; c < Math.Min(cols, _tmCols); c++)
                    nc[r * cols + c] = _tmCells[r * _tmCols + c];
            _tmCols = cols; _tmRows = rows; _tmCells = nc;
            SerializeTilemap(); RenderScene(); Dirty();
        }

        private void TilemapRow(GameObjectData obj, BehaviorData bh, Panel panel)
        {
            panel.Children.Add(new TextBlock { Text = "Left-drag paints · Right-drag erases", Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11, Margin = new Thickness(0, 2, 0, 6), TextWrapping = TextWrapping.Wrap });

            NumRow("Tile size", _tmSize, v => { _tmSize = Math.Max(4, v); SerializeTilemap(); RenderScene(); Dirty(); }, panel);
            NumRow("Columns", _tmCols, v => ResizeGrid((int)v, _tmRows), panel);
            NumRow("Rows", _tmRows, v => ResizeGrid(_tmCols, (int)v), panel);

            panel.Children.Add(new TextBlock { Text = "TILES (click to select)", Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
            var wrap = new WrapPanel();
            wrap.Children.Add(TileSwatch(0));
            for (int i = 1; i <= _tmPalette.Count; i++) wrap.Children.Add(TileSwatch(i));
            var add = new Border { Width = 34, Height = 34, Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, CornerRadius = new CornerRadius(4), Background = (SolidColorBrush)FindResource("Bg2"), BorderBrush = (SolidColorBrush)FindResource("Border"), BorderThickness = new Thickness(1), Child = new TextBlock { Text = "+", Foreground = (SolidColorBrush)FindResource("Text"), FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            add.MouseLeftButtonUp += (s, e) => { _tmPalette.Add(("#8A8A8A", true)); _tmActive = _tmPalette.Count; SerializeTilemap(); BuildInspector(obj); };
            wrap.Children.Add(add);
            panel.Children.Add(wrap);

            if (_tmActive >= 1 && _tmActive <= _tmPalette.Count)
            {
                var cur = _tmPalette[_tmActive - 1];
                ColorRow("Tile color", cur.color, v => { _tmPalette[_tmActive - 1] = (v, _tmPalette[_tmActive - 1].solid); SerializeTilemap(); RenderScene(); Dirty(); }, panel);
                var chk = new CheckBox { Content = "Solid (collision)", IsChecked = cur.solid, Foreground = (SolidColorBrush)FindResource("Text"), Margin = new Thickness(0, 2, 0, 6) };
                chk.Checked += (s, e) => { _tmPalette[_tmActive - 1] = (_tmPalette[_tmActive - 1].color, true); SerializeTilemap(); Dirty(); };
                chk.Unchecked += (s, e) => { _tmPalette[_tmActive - 1] = (_tmPalette[_tmActive - 1].color, false); SerializeTilemap(); Dirty(); };
                panel.Children.Add(chk);
                var rem = new Button { Content = "Remove this tile", Height = 28, FontSize = 11, Style = (Style)FindResource("CardButton"), Foreground = (SolidColorBrush)FindResource("Danger"), HorizontalContentAlignment = HorizontalAlignment.Center };
                rem.Click += (s, e) => { _tmPalette.RemoveAt(_tmActive - 1); _tmActive = _tmPalette.Count >= 1 ? 1 : 0; SerializeTilemap(); RenderScene(); BuildInspector(obj); Dirty(); };
                panel.Children.Add(rem);
            }
        }

        private FrameworkElement TileSwatch(int index)
        {
            var b = new Border
            {
                Width = 34, Height = 34, Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(_tmActive == index ? 2.5 : 1),
                BorderBrush = (SolidColorBrush)FindResource(_tmActive == index ? "Accent" : "Border"),
                Background = index == 0 ? (SolidColorBrush)FindResource("Bg2") : BrushFrom(_tmPalette[index - 1].color, "#888")
            };
            if (index == 0) b.Child = new TextBlock { Text = "⌫", Foreground = (SolidColorBrush)FindResource("Text"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            b.ToolTip = index == 0 ? "Eraser" : (_tmPalette[index - 1].solid ? "Solid tile" : "Decoration");
            b.MouseLeftButtonUp += (s, e) => { _tmActive = index; if (_tmObj != null) BuildInspector(_tmObj); };
            return b;
        }

        private void ScenePickerRow(BehaviorData bh, Panel target)
        {
            var grid = RowGrid("Target scene", target);
            var combo = new ComboBox();
            combo.Items.Add(new ComboBoxItem { Content = "(none)", Tag = "" });
            foreach (var s in _project.Scenes)
                combo.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s.Name });
            var cur = bh.Text("scene", "");
            combo.SelectedIndex = 0;
            for (int i = 0; i < combo.Items.Count; i++)
                if ((string)((ComboBoxItem)combo.Items[i]).Tag! == cur) { combo.SelectedIndex = i; break; }
            combo.SelectionChanged += (s, e) => { if (combo.SelectedItem is ComboBoxItem ci) { bh.Texts["scene"] = (string)ci.Tag!; Dirty(); } };
            Grid.SetColumn(combo, 1);
            grid.Children.Add(combo);
        }

        // ---- UI builders ----
        private void Section(string title) => InspectorRoot.Children.Add(new TextBlock { Text = title, Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 8) });

        private Grid RowGrid(string label, Panel? target = null)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = new TextBlock { Text = label, Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lbl, 0); grid.Children.Add(lbl);
            (target ?? InspectorRoot).Children.Add(grid);
            return grid;
        }

        private void TextRow(string label, string value, Action<string> onChange, Panel? target = null)
        {
            var grid = RowGrid(label, target);
            var tb = new TextBox { Text = value, Height = 30 };
            void Commit() { PushUndo(); onChange(tb.Text); }
            tb.LostFocus += (s, e) => Commit();
            tb.KeyDown += (s, e) => { if (e.Key == Key.Enter) Commit(); };
            Grid.SetColumn(tb, 1); grid.Children.Add(tb);
        }

        private void NumRow(string label, double value, Action<double> onChange, Panel? target = null)
        {
            var grid = RowGrid(label, target);
            var tb = new TextBox { Text = value.ToString("0.###"), Height = 30 };
            void Commit() { if (double.TryParse(tb.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v)) { PushUndo(); onChange(v); } }
            tb.LostFocus += (s, e) => Commit();
            tb.KeyDown += (s, e) => { if (e.Key == Key.Enter) Commit(); };
            Grid.SetColumn(tb, 1); grid.Children.Add(tb);
        }

        private void CheckRow(string label, bool value, Action<bool> onChange, Panel? target = null)
        {
            var grid = RowGrid(label, target);
            var chk = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center, Foreground = (SolidColorBrush)FindResource("Text") };
            chk.Checked += (s, e) => { PushUndo(); onChange(true); };
            chk.Unchecked += (s, e) => { PushUndo(); onChange(false); };
            Grid.SetColumn(chk, 1); grid.Children.Add(chk);
        }

        private void InfoRow(string label, string value, Panel? target = null)
        {
            var grid = RowGrid(label, target);
            var tb = new TextBlock { Text = value, Foreground = (SolidColorBrush)FindResource("Text"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(tb, 1); grid.Children.Add(tb);
        }

        private void ComboRow(string label, string[] options, int selected, Action<int> onChange, Panel? target = null)
        {
            var grid = RowGrid(label, target);
            var combo = new ComboBox();
            foreach (var o in options) combo.Items.Add(new ComboBoxItem { Content = o });
            combo.SelectedIndex = Math.Clamp(selected, 0, options.Length - 1);
            combo.SelectionChanged += (s, e) => { PushUndo(); onChange(combo.SelectedIndex); };
            Grid.SetColumn(combo, 1); grid.Children.Add(combo);
        }

        private void ColorRow(string label, string value, Action<string> onChange, Panel? target = null)
        {
            var grid = RowGrid(label, target);
            var inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tb = new TextBox { Text = value, Height = 30 };
            var swatch = new Rectangle { Width = 24, Height = 24, Margin = new Thickness(6, 0, 0, 0), RadiusX = 4, RadiusY = 4, Fill = BrushFrom(value, "#000") };
            void Commit() { PushUndo(); onChange(tb.Text); swatch.Fill = BrushFrom(tb.Text, "#000"); }
            tb.LostFocus += (s, e) => Commit();
            tb.KeyDown += (s, e) => { if (e.Key == Key.Enter) Commit(); };
            Grid.SetColumn(tb, 0); Grid.SetColumn(swatch, 1);
            inner.Children.Add(tb); inner.Children.Add(swatch);
            Grid.SetColumn(inner, 1); grid.Children.Add(inner);
        }

        private void SoundRow(GameObjectData obj, BehaviorData bh, Panel target)
        {
            var grid = RowGrid("Sound file", target);
            var inner = new StackPanel { Orientation = Orientation.Horizontal };
            var current = bh.Text("asset", "");
            var pick = new Button { Content = string.IsNullOrEmpty(current) ? "Choose…" : Path.GetFileName(current), Style = (Style)FindResource("CardButton"), Height = 28, Padding = new Thickness(8, 0, 8, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
            pick.Click += (s, e) =>
            {
                var dlg = new OpenFileDialog { Title = "Choose sound", Filter = "Audio|*.wav;*.mp3" };
                if (dlg.ShowDialog() == true)
                {
                    try { PushUndo(); bh.Texts["asset"] = ProjectIO.ImportAsset(_project, dlg.FileName, "sounds"); BuildInspector(obj); Dirty(); }
                    catch (Exception ex) { MessageBox.Show("Error: " + ex.Message); }
                }
            };
            inner.Children.Add(pick);
            Grid.SetColumn(inner, 1); grid.Children.Add(inner);
        }

        // ============================================================
        //  TABS
        // ============================================================
        private void TabInspector_Click(object sender, RoutedEventArgs e) => SetTab(true);
        private void TabAi_Click(object sender, RoutedEventArgs e) => SetTab(false);

        private void SetTab(bool inspector)
        {
            InspectorScroll.Visibility = inspector ? Visibility.Visible : Visibility.Collapsed;
            AiPanel.Visibility = inspector ? Visibility.Collapsed : Visibility.Visible;
            TabInspector.Background = inspector ? (SolidColorBrush)FindResource("Accent") : (SolidColorBrush)FindResource("Bg2");
            TabAi.Background = inspector ? (SolidColorBrush)FindResource("Bg2") : (SolidColorBrush)FindResource("Accent");
            TabInspector.Foreground = inspector ? Brushes.White : (SolidColorBrush)FindResource("Text");
            TabAi.Foreground = inspector ? (SolidColorBrush)FindResource("Text") : Brushes.White;
        }

        private void AiBuild_Click(object sender, RoutedEventArgs e) => SetAiMode(false);
        private void AiBrainstorm_Click(object sender, RoutedEventArgs e) => SetAiMode(true);

        private void SetAiMode(bool brainstorm)
        {
            _aiBrainstorm = brainstorm;
            AiModeBuild.Background = brainstorm ? (SolidColorBrush)FindResource("Bg2") : (SolidColorBrush)FindResource("Accent");
            AiModeBuild.Foreground = brainstorm ? (SolidColorBrush)FindResource("Text") : Brushes.White;
            AiModeBrainstorm.Background = brainstorm ? (SolidColorBrush)FindResource("Accent") : (SolidColorBrush)FindResource("Bg2");
            AiModeBrainstorm.Foreground = brainstorm ? Brushes.White : (SolidColorBrush)FindResource("Text");
            AiSend.Content = brainstorm ? "Send" : "Build it";
            AiHint.Text = brainstorm
                ? "Chat about your game — ideas, mechanics, story. The AI won't change anything here."
                : "Describe what to build. The AI creates/edits objects in the scene (even without selecting one).";
        }

        // ============================================================
        //  AI → BEHAVIOR BRIDGE
        // ============================================================
        private void StrengthCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CustomModel == null || OllamaStatus == null || CloudKeyPanel == null) return;
            if (StrengthCombo.SelectedItem is not ComboBoxItem item) return;
            var tag = (string)(item.Tag ?? "");

            _useCloud = tag.StartsWith("cloud:", StringComparison.Ordinal);
            bool custom = string.IsNullOrEmpty(tag);

            CloudKeyPanel.Visibility = _useCloud ? Visibility.Visible : Visibility.Collapsed;
            CustomModel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;

            if (_useCloud) _cloudModel = tag.Substring("cloud:".Length);
            else _model = custom ? CustomModel.Text.Trim() : tag;

            _ = RefreshOllamaStatusAsync();
        }

        private void CustomModel_TextChanged(object sender, TextChangedEventArgs e) { _model = CustomModel.Text.Trim(); _ = RefreshOllamaStatusAsync(); }

        private void GroqKey_TextChanged(object sender, TextChangedEventArgs e)
        {
            AiSettings.GroqApiKey = GroqKey.Text.Trim();
            AiSettings.Save();
            if (_useCloud) _ = RefreshOllamaStatusAsync();
        }

        private async Task RefreshOllamaStatusAsync()
        {
            if (OllamaStatus == null) return;

            if (_useCloud)
            {
                bool hasKey = !string.IsNullOrWhiteSpace(AiSettings.GroqApiKey);
                OllamaDot.Foreground = hasKey ? (SolidColorBrush)FindResource("Accent") : BrushFrom("#F4C03B", "#F4C03B");
                OllamaStatus.Foreground = hasKey ? (SolidColorBrush)FindResource("Accent") : BrushFrom("#F4C03B", "#F4C03B");
                OllamaStatus.Text = hasKey
                    ? $"☁ Cloud (Groq) · model \"{_cloudModel}\". Fast and runs off your PC."
                    : "☁ Cloud (Groq) selected — paste your free API key above to use it.";
                return;
            }

            if (!await _ollama.IsRunningAsync())
            {
                OllamaDot.Foreground = BrushFrom("#F4C03B", "#F4C03B");
                OllamaStatus.Foreground = BrushFrom("#F4C03B", "#F4C03B");
                OllamaStatus.Text = "⚠ Ollama not found. The engine tries to start it automatically on launch.";
                return;
            }
            var models = await _ollama.ListModelsAsync();
            if (!string.IsNullOrWhiteSpace(_model) && models.Contains(_model))
            {
                OllamaDot.Foreground = (SolidColorBrush)FindResource("Accent");
                OllamaStatus.Foreground = (SolidColorBrush)FindResource("Accent");
                OllamaStatus.Text = $"✓ Connected · model \"{_model}\" ready.";
            }
            else
            {
                OllamaDot.Foreground = BrushFrom("#F4C03B", "#F4C03B");
                OllamaStatus.Foreground = BrushFrom("#F4C03B", "#F4C03B");
                OllamaStatus.Text = $"Ollama ok, but the model isn't downloaded:\n    ollama pull {_model}";
            }
        }

        // Modelo mostrado nas bolhas/status; muda conforme local ou nuvem.
        private string ActiveModelLabel => _useCloud ? $"Groq · {_cloudModel}" : _model;

        // Roteia a geração pro provedor atual (Ollama local ou Groq nuvem). Mesma assinatura dos dois.
        private Task<string> AiGenerateAsync(string prompt, string system, bool json, double temperature, CancellationToken ct = default)
            => _useCloud
                ? _cloud.GenerateAsync(_cloudModel, prompt, system, AiSettings.GroqApiKey, json, temperature, ct)
                : _ollama.GenerateAsync(_model, prompt, system, json, temperature, ct);

        private async void AiSend_Click(object sender, RoutedEventArgs e)
        {
            var prompt = AiInput.Text.Trim();
            if (prompt.Length == 0) return;
            if (_useCloud) { if (string.IsNullOrWhiteSpace(AiSettings.GroqApiKey)) { Status("Paste your free Groq API key first"); return; } }
            else if (string.IsNullOrWhiteSpace(_model)) { Status("Choose an AI model"); return; }

            if (_aiBrainstorm)
            {
                AddAiMessage("You", prompt, false);
                AiInput.Clear();
                var th = AddAiMessage($"AI ({ActiveModelLabel})", "thinking…", true);
                AiSend.IsEnabled = false; Status("Brainstorming…");
                try { var ans = await BrainstormAsync(prompt); AiLog.Children.Remove(th); AddAiMessage("AI", ans.Length > 0 ? ans : "(no response)", true); Status("Ready"); }
                catch (AiException ex) { AiLog.Children.Remove(th); AddAiMessage("AI", "⚠ " + ex.Message, true); Status("Failed"); }
                finally { AiSend.IsEnabled = true; }
                return;
            }

            AddAiMessage("You" + (_selected != null ? " (selected: " + _selected.Name + ")" : ""), prompt, false);
            AiInput.Clear();
            var thinking = AddAiMessage($"AI ({ActiveModelLabel})", "thinking…", true);
            AiSend.IsEnabled = false;
            Status($"Asking {ActiveModelLabel}…");

            try
            {
                var sys = BuildProjectPrompt();
                var reply = await AiGenerateAsync(prompt, sys, json: true, temperature: 0.1);
                bool ok = ApplyProjectSpec(reply, prompt, out var summary);

                if (!ok)
                {
                    // one retry with a firmer nudge
                    var reply2 = await AiGenerateAsync(
                        prompt + "\n\nReturn ONLY the JSON with \"create\"/\"edit\"/\"layers\". Use EXACT behavior kind names from the list. One object can have many behaviors.",
                        sys, json: true, temperature: 0.1);
                    ok = ApplyProjectSpec(reply2, prompt, out summary);
                    if (!ok) reply = reply2;
                }

                AiLog.Children.Remove(thinking);
                if (ok)
                {
                    _aiLastSummary = summary;
                    _aiLastObjects.Clear();
                    foreach (var line in summary.Split('\n'))
                    {
                        var q1 = line.IndexOf('"');
                        var q2 = q1 >= 0 ? line.IndexOf('"', q1 + 1) : -1;
                        if (q1 >= 0 && q2 > q1) _aiLastObjects.Add(line.Substring(q1 + 1, q2 - q1 - 1));
                    }
                    AddAiMessage("AI — applied ✓", summary, true);
                    RenderScene(); RefreshLayers(); RefreshObjects(); RefreshPalette();
                    if (_selected != null) BuildInspector(_selected);
                    Dirty();
                    Status("AI applied changes. Press Test to try it.");
                }
                else
                {
                    AddAiMessage("AI", "I couldn't apply that. Try rephrasing. Raw reply:\n" + reply, true);
                    Status("Could not apply AI response");
                }
            }
            catch (AiException ex)
            {
                AiLog.Children.Remove(thinking);
                AddAiMessage("AI", "⚠ " + ex.Message, true);
                Status("Generation failed");
            }
            finally { AiSend.IsEnabled = true; }
        }

        private void AddImages_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "Add files for the AI to use (images or audio)", Multiselect = true, Filter = "Images & audio|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.wav;*.mp3|Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Audio|*.wav;*.mp3" };
            if (dlg.ShowDialog() != true) return;
            int img = 0, snd = 0;
            foreach (var f in dlg.FileNames)
            {
                try
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext is ".wav" or ".mp3") { var rel = ProjectIO.ImportAsset(_project, f, "sounds"); if (!_aiSounds.Contains(rel)) { _aiSounds.Add(rel); snd++; } }
                    else { var rel = ProjectIO.ImportAsset(_project, f, "sprites"); if (!_aiImages.Contains(rel)) { _aiImages.Add(rel); img++; } }
                }
                catch (Exception ex) { MessageBox.Show("Error importing " + Path.GetFileName(f) + ": " + ex.Message); }
            }
            RefreshAiImages();
            Status($"Added {img} image(s) and {snd} sound(s) for the AI");
        }

        private string? ResolveSound(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) return null;
            var f = Path.GetFileName(filename);
            var hit = _aiSounds.FirstOrDefault(r => string.Equals(Path.GetFileName(r), f, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;
            try
            {
                var dir = Path.Combine(_project.FolderPath!, "Assets", "sounds");
                if (Directory.Exists(dir))
                {
                    var match = Directory.GetFiles(dir).FirstOrDefault(p => string.Equals(Path.GetFileName(p), f, StringComparison.OrdinalIgnoreCase));
                    if (match != null) return Path.Combine("Assets", "sounds", Path.GetFileName(match));
                }
            }
            catch { }
            return null;
        }

        private void RefreshAiImages()
        {
            if (AiImages == null) return;
            AiImages.Children.Clear();
            foreach (var rel in _aiImages)
            {
                var abs = ProjectIO.AbsoluteAsset(_project, rel);
                var thumb = new Border { Width = 40, Height = 40, Margin = new Thickness(0, 0, 4, 4), CornerRadius = new CornerRadius(4), BorderBrush = (SolidColorBrush)FindResource("Border"), BorderThickness = new Thickness(1), ToolTip = Path.GetFileName(rel), Background = (SolidColorBrush)FindResource("Bg2") };
                if (abs != null)
                {
                    try { var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.DecodePixelWidth = 40; bmp.UriSource = new Uri(abs); bmp.EndInit(); thumb.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill }; } catch { }
                }
                AiImages.Children.Add(thumb);
            }
            foreach (var rel in _aiSounds)
            {
                AiImages.Children.Add(new Border
                {
                    Margin = new Thickness(0, 0, 4, 4), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 3, 6, 3),
                    Background = (SolidColorBrush)FindResource("Bg2"), BorderBrush = (SolidColorBrush)FindResource("Border"), BorderThickness = new Thickness(1),
                    Child = new TextBlock { Text = "♪ " + Path.GetFileName(rel), FontSize = 10, Foreground = (SolidColorBrush)FindResource("Text") }
                });
            }
        }

        // ============================================================
        //  AI TEST HARNESS (run many prompts through the real pipeline)
        // ============================================================
        public async Task RunAiBatchAsync(List<string> prompts, string logPath, string model)
        {
            _model = model;
            await OllamaManager.EnsureRunningAsync();
            var sb = new StringBuilder();
            sb.AppendLine($"AI BATCH TEST — model {model} — {DateTime.Now}");
            int i = 0;
            foreach (var prompt in prompts)
            {
                i++;
                ResetTestScene();
                sb.AppendLine("\n============================================================");
                sb.AppendLine($"#{i} PROMPT: {prompt}");
                string reply;
                try { reply = await _ollama.GenerateAsync(_model, prompt, BuildProjectPrompt(), json: true, temperature: 0.1); }
                catch (Exception ex) { sb.AppendLine("  GEN ERROR: " + ex.Message); continue; }
                bool ok;
                try { ok = ApplyProjectSpec(reply, prompt, out var summary); sb.AppendLine("  APPLIED: " + (ok ? "yes — " + summary.Replace("\n", " | ") : "NO")); }
                catch (Exception ex) { sb.AppendLine("  APPLY ERROR: " + ex.Message); sb.AppendLine("  RAW: " + Compact(reply)); continue; }
                sb.AppendLine("  STATE: " + DumpState());
                if (!ok) sb.AppendLine("  RAW: " + Compact(reply));
            }
            try { File.WriteAllText(logPath, sb.ToString()); } catch { }
        }

        private void ResetTestScene()
        {
            _project.Prefabs = PrefabFactory.Defaults();
            _project.Variables.Clear();

            var scene = new Scene { Name = "Scene 1" };
            scene.Layers.Add(new Layer { Name = "Background", Kind = LayerKind.Background });
            var baseL = new Layer { Name = "Base", Kind = LayerKind.Base };
            baseL.Objects.Add(new GameObjectData { Id = 1, Name = "Ground", Type = "Object", X = 0, Y = 460, Width = 960, Height = 80, Color = "#3A3A46", Physics = new PhysicsData { Mode = PhysicsMode.Static }, Behaviors = { BehaviorCatalog.Info(BehaviorKind.Solid).CreateData() } });
            var player = new GameObjectData { Id = 2, Name = "Player", Type = "Player", X = 120, Y = 360, Width = 48, Height = 64, Color = "#3BC97A", Physics = new PhysicsData { Mode = PhysicsMode.Dynamic } };
            player.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Platformer).CreateData());
            player.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.FollowCamera).CreateData());
            player.Behaviors.Add(BehaviorCatalog.Info(BehaviorKind.Health).CreateData());
            baseL.Objects.Add(player);
            scene.Layers.Add(baseL);
            scene.Layers.Add(new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true });

            _project.Scenes = new List<Scene> { scene };
            _project.StartScene = scene.Name;
            _scene = scene;
            _selection.Clear(); _selected = null; _activeLayer = baseL;
        }

        private string DumpState()
        {
            var sb = new StringBuilder();
            foreach (var o in _scene.Layers.SelectMany(l => l.Objects))
            {
                sb.Append($"{{{o.Name} {o.Physics.Mode}");
                foreach (var b in o.Behaviors) sb.Append(" " + b.Kind + DumpBeh(b));
                if (o.Variables.Count > 0) sb.Append(" vars(" + string.Join(",", o.Variables.Select(kv => $"{kv.Key}={kv.Value:0.##}")) + ")");
                sb.Append("} ");
            }
            var extraPrefabs = _project.Prefabs.Where(p => !new[] { "Player", "Enemy", "Bullet", "Particle", "Platform", "Door", "Tilemap" }.Contains(p.Name)).Select(p => p.Name).ToList();
            if (extraPrefabs.Count > 0) sb.Append("PREFABS[" + string.Join(",", extraPrefabs) + "] ");
            if (_project.Variables.Count > 0) sb.Append("GLOBAL(" + string.Join(",", _project.Variables.Select(kv => $"{kv.Key}={kv.Value:0.##}")) + ") ");
            if (_scene.Variables.Count > 0) sb.Append("SCENE(" + string.Join(",", _scene.Variables.Select(kv => $"{kv.Key}={kv.Value:0.##}")) + ") ");
            return sb.ToString();
        }

        private static string DumpBeh(BehaviorData b)
        {
            var parts = new List<string>();
            foreach (var kv in b.Nums) parts.Add($"{kv.Key}={kv.Value:0.##}");
            foreach (var kv in b.Texts) parts.Add(kv.Key == "script" ? "script=" + Compact(kv.Value) : $"{kv.Key}={kv.Value}");
            return parts.Count > 0 ? "(" + string.Join(",", parts) + ")" : "";
        }

        private static string Compact(string s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim();

        private string BuildProjectPrompt()
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are the game-builder AI of a 2D engine. Output ONLY a JSON object. Schema:");
            sb.AppendLine("{");
            sb.AppendLine("  \"create\": [ {\"name\",\"x\",\"y\",\"width\",\"height\",\"color\",\"layer\",\"physics\":\"None|Static|Dynamic\",\"behaviors\":[{\"kind\",\"nums\":{},\"texts\":{}}],\"custom\":[...],\"animations\":[{\"name\",\"frames\":[],\"fps\"}],\"sprite\":\"file.png\"} ],");
            sb.AppendLine("  \"edit\":   [ {\"name\":\"<existing object>\",\"behaviors\":[...],\"remove\":[\"Kind\"],\"custom\":[...],\"animations\":[...],\"color\",\"width\",\"height\"} ],");
            sb.AppendLine("  \"layers\": [ {\"name\",\"screenSpace\":true,\"visible\":true} ],");
            sb.AppendLine("  \"variables\": {\"global\":{\"name\":value}, \"scene\":{\"name\":value}}, \"removeVariables\": {\"global\":[\"name\"], \"scene\":[\"name\"]}");
            sb.AppendLine("}");
            sb.AppendLine("Only include the fields/arrays you need. Omit empty ones.");
            sb.AppendLine("VARIABLES: top-level \"variables\".global/.scene create or set the game's global/scene variables (initial values); \"removeVariables\" deletes them. Per-object variables go inside that object's edit/create as \"variables\":{\"name\":value} (and \"removeVariables\":[\"name\"]). You can change ANY variable this way.");
            sb.AppendLine();
            sb.AppendLine("KEY RULES:");
            sb.AppendLine("- To make a NEW thing (player, enemy, boss, coin, platform…), add it to \"create\" and it will be placed in the scene. ONE object can and should have MANY behaviors.");
            sb.AppendLine("- To change an EXISTING object, add it to \"edit\" by its name. Behaviors are ADDED/UPDATED; keep existing ones unless you list them in \"remove\".");
            sb.AppendLine("- If the user talks about \"the player\" but there is an object with Platformer/TopDown, that IS the player — edit it by its name. Don't require it to be selected.");
            sb.AppendLine("- If an object with that name ALREADY EXISTS in the scene, put it in \"edit\" (never create a duplicate).");
            sb.AppendLine("- To SPAWN objects during play (rocks falling, enemies appearing on a key/timer): create the thing with \"asPrefab\":true (it stays OUT of the scene until spawned; give it physics Dynamic if it should fall), then spawn it with a Spawner behavior or a custom spawn rule.");
            sb.AppendLine("- Use EXACT kind names from this list. Never invent kinds. One object, many behaviors.");
            sb.AppendLine();
            sb.AppendLine("Behavior kinds — nums(texts):");
            foreach (var info in BehaviorCatalog.All)
            {
                if (info.Kind == BehaviorKind.Tilemap) continue; // painted by hand, never via AI
                var nums = string.Join(",", info.Nums.Select(n => n.Key));
                var texts = string.Join(",", info.Texts.Select(t => t.Key));
                sb.AppendLine($"  {info.Kind}: {nums}" + (texts.Length > 0 ? $" ({texts})" : ""));
            }
            sb.AppendLine();
            sb.AppendLine("Intent → kind: walk+jump=Platformer; top-down/8-dir=TopDown; ground/wall/platform=Solid; camera=FollowCamera; patrol=Patrol; chase player=Chase; shoot on key=Shooter; melee/sword/attack=Melee; hp/lives=Health; spike/hazard/contact damage=Damager; checkpoint=Checkpoint; coin/pickup=Collectible; door/next level=GoToScene; HUD/score text=Label; glow=LightSource; spinning=Spin; spawner=Spawner; parallax bg=Parallax.");
            sb.AppendLine("Intent → kind (NEW): pushable crate/box/sokoban block=Pushable; lever/switch/toggle=Switch; locked gate/ability-gate/barrier that opens with a key=Gate (NOT GoToScene — Gate blocks the way, GoToScene changes scene); NPC/sign/chest/press-E-to-talk=Interact; pick up an ability or key that unlocks something=AbilityPickup; invisible trigger zone/region/level-exit area=Area.");
            sb.AppendLine("Gate opens when its var meets the condition: {\"kind\":\"Gate\",\"texts\":{\"var\":\"hasRedKey\"},\"nums\":{\"value\":1}}. Give the key with AbilityPickup {\"kind\":\"AbilityPickup\",\"texts\":{\"var\":\"hasRedKey\"}}. Metroidvania ability+gate: AbilityPickup texts{var:hasDash,grant:dash} then Gate texts{var:hasDash}.");
            sb.AppendLine("PROCEDURAL maps: put a Generator on an empty object sized to the region to fill (or scene-sized). It spawns prefab(s) when the scene loads. mode=once makes a STABLE map (same when you return); mode=regen RECREATES it on every visit (roguelike). Make the spawned things prefabs with asPrefab:true. e.g. {\"create\":[{\"name\":\"Coin\",\"asPrefab\":true,...}],\"edit\":[{\"name\":\"World\",\"behaviors\":[{\"kind\":\"Generator\",\"texts\":{\"prefab\":\"Coin,Enemy\",\"mode\":\"regen\",\"layout\":\"scatter\"},\"nums\":{\"count\":20,\"minGap\":80}}]}]}.");
            sb.AppendLine("Persistence: a Collectible/AbilityPickup with a texts \"id\" stays gone after it's taken (survives revisits + save). Save/continue progress with the save/load actions (custom do:save / do:load, or Interact action:save) — saves all global vars + current scene. Collectible can also carry texts{item:NAME}+nums{amount} to add to an inventory count.");
            sb.AppendLine("AESTHETICS (per object, in create/edit): \"opacity\"(0-1), \"cornerRadius\"(px, 0=square), \"borderColor\"(hex)+\"borderWidth\"(px outline), \"font\"(family name) + \"bold\"(true/false) for Label text, \"color\"(fill). Example rounded glowing box: {\"edit\":[{\"name\":\"Box\",\"cornerRadius\":16,\"borderColor\":\"#00E5FF\",\"borderWidth\":3,\"opacity\":0.9}]}.");
            sb.AppendLine("TEXT EFFECTS: a Label takes texts{effect: none|typewriter|fade|blink|pulse} + nums{effectSpeed}. e.g. a title that types out: {\"kind\":\"Label\",\"texts\":{\"text\":\"GAME OVER\",\"effect\":\"typewriter\"},\"nums\":{\"fontSize\":48}}.");
            sb.AppendLine("GAME THEME (top-level \"theme\" object, styles what the PLAYER sees): {\"theme\":{\"font\":\"Consolas\",\"dialogBg\":\"#101418\",\"dialogBorder\":\"#00E5FF\",\"dialogText\":\"#FFFFFF\",\"dialogSpeaker\":\"#F4C03B\",\"dialogFontSize\":18,\"dialogCorner\":10,\"dialogEffect\":\"typewriter\",\"dialogSpeed\":45}}. Use it when the user wants to restyle dialogue boxes or the game font.");
            sb.AppendLine("Intent → kind (MORE): enemy that chases AROUND walls (not straight) = PathChase (best for top-down); trampoline/bounce pad/jump pad = Spring (nums{force},texts{dir:up/down/left/right}); minimap/radar = Minimap (put on a UI layer, texts{corner:tl/tr/bl/br,player,markers}). Bouncy objects: set the object's physics Bounciness (0-1) in \"physics\" — a ball with bounciness 0.7 bounces off floors/walls.");
            sb.AppendLine("Intent → kind (FX/UI): explosion/sparks/smoke/trail = Particles (on=start bursts once, on=always is continuous at nums{rate}; nums{burst,life,speed,spread,angle,size,gravity}, texts{color}); clickable menu/HUD button = UIButton on a UI layer (same action params as Area: goscene/setvar/addvar/say/save/load) — add a Label for its text; health/mana/progress bar = Bar (texts{var,color,bg}, nums{max} or texts{maxVar}).");
            sb.AppendLine("ACTIONS (MORE): do:emit (particle burst — count,speed,spread,angle,life,size,gravity,color); do:tween (smoothly animate a property: prop=x|y|opacity|scale|rot|w|h, to, nums{duration,loop,yoyo}, ease=linear|in|out|inout) e.g. a pulsing title {\"when\":\"start\",\"do\":\"tween\",\"prop\":\"scale\",\"to\":1.2,\"duration\":0.6,\"loop\":1,\"yoyo\":1}; do:emit on death for explosions {\"when\":\"death\",\"do\":\"emit\",\"count\":24,\"color\":\"#E5484B\"}; do:setvolume (channel=music/sfx/master, to=0-1). SoundEmitter texts{channel:music|sfx}.");
            sb.AppendLine("Platformer/TopDown obey the default keys only when their num \"controls\"=1 (that's the PLAYER). If you give Platformer/TopDown to an enemy/boss, set controls=0 so it does NOT copy the player's keys (move it with a custom script or custom keys). For simple enemies prefer Patrol/Chase/custom (physics Dynamic if it should fall/jump).");
            sb.AppendLine("Player uses Platformer physics=Dynamic (dash/doubleJump/wallJump are nums on it, each 0 or 1). Enemy you can kill = Health respawn=0. Player = Health respawn=1.");
            sb.AppendLine();
            sb.AppendLine("AVOID THESE MISTAKES:");
            sb.AppendLine("- A new thing the user names (coin, platform, enemy, boss, NPC, door, spike, wall, obstacle, crystal…) is a NEW object → put it in \"create\" with that name. NEVER put Solid/Collectible/Patrol/Chase/GoToScene/Parallax/Projectile/DestroyOffscreen on the Player.");
            sb.AppendLine("- An ability/stat for the player (health, speed, dash, jump, wall jump, attack, shoot, light) goes in \"edit\" of the existing player — do NOT create a new object for it, and do NOT invent a new player.");
            sb.AppendLine("- 0/1 flags (controls,doubleJump,wallJump,dash,oneWay,respawn,onStart,loop) must be exactly 0 or 1.");
            sb.AppendLine("- 'X times faster' / 'double speed' ON AN EVENT => custom boost(factor). NEVER use velocity for speed. A permanent speed change => set the Platformer \"speed\" num.");
            sb.AppendLine("- To spawn on a timer you MUST add a Spawner behavior (or a custom timer→spawn rule); creating the prefab alone spawns nothing.");
            sb.AppendLine("- An enemy that shoots AT the player => custom {\"when\":\"timer\",\"every\":2,\"do\":\"shoot\",\"directions\":1,\"speed\":400}. Projectile makes the object ITSELF fly, so don't put Projectile on a stationary shooter.");
            sb.AppendLine("- When changing layers, ONLY change the exact layer(s) the user names.");
            sb.AppendLine("- \"when it dies, do X\" (explode, drop item, spawn particles) => a custom rule with \"when\":\"death\".");
            sb.AppendLine("- A MOVING platform = a new object with Solid + Patrol (NOT Platformer). A one-way platform = Solid oneWay=1 (keep physics Static).");
            sb.AppendLine();
            sb.AppendLine("CUSTOM behavior — for ANY special/unique logic the kinds can't do (enemy patterns, dialogue, variables, inventory, conditions). ALWAYS use \"custom\" for things like 'every N do X', timed attacks, phases:");
            sb.AppendLine("CRUCIAL: 'WHEN <something> happens, <effect>' (e.g. 'when the player hits X, go 2x faster', 'when touch spike, lose a life') is ALWAYS a custom rule (condition → action). NEVER express it by changing a static param — a static param would apply from the start, not on the event.");
            sb.AppendLine("\"custom\":[{\"when\":<start|always|key|keypress|timer|touch|death|ifvar|iflocal|hasitem|expr|click|rightclick|mousedown|mouseover|mousepress>, ...params, \"do\":<velocity|move|accelerate|jump|rotate|shoot|spawn|destroy|goscene|setvar|addvar|setlocal|addlocal|set|additem|removeitem|boost|setspeed|say|save|load>, ...params}]");
            sb.AppendLine("Condition params: key(key); keypress(key,cooldown); timer(every); touch(name) [fires once when contact starts]; death [fires when this object's health hits 0]; ifvar/iflocal(var,op,value); hasitem(item); expr(expr) [any expression truthy]; click/rightclick/mousedown/mouseover [mouse over THIS object]; mousepress [mouse clicked anywhere].");
            sb.AppendLine("Action params: velocity(vx,vy); move(dx,dy); accelerate(ax,ay); jump(force); rotate(speed); shoot(directions,speed); spawn(object); goscene(scene); setvar/setlocal(var,to); addvar/addlocal(var,amount); set(prop,to,[target]) [prop=x|y|vx|vy|rot|facing|health|w|h — set a property, default target=self]; boost(factor); setspeed(factor); say(text,speaker).");
            sb.AppendLine("EXPRESSIONS (the big unlock): any set/add/compare value can be an \"expr\" string instead of a fixed number. Supports + - * / %, comparisons > < >= <= == !=, logic && || !, parentheses, functions abs min max floor ceil round sqrt sign sin cos atan2 clamp(x,lo,hi) rand(a,b) randi(a,b) dist(x1,y1,x2,y2). Read values: a variable name (score), item:NAME, self.x/self.y/self.vx/self.cx/self.health/self.facing/local.NAME, OtherObjectName.x/.cy/..., mousex/mousey (world), mousescreenx/y, time, scenew/sceneh.");
            sb.AppendLine("Universal guard: add \"cond\":\"<expression>\" to ANY rule and it only fires when the expression is truthy. e.g. {\"when\":\"click\",\"cond\":\"coins >= 15\",\"do\":\"addvar\",\"var\":\"coins\",\"expr\":\"coins - 15\"}.");
            sb.AppendLine("Expression examples: make an object follow the mouse => {\"when\":\"always\",\"do\":\"set\",\"prop\":\"x\",\"expr\":\"mousex - self.w/2\"}. Score from a formula => {\"when\":\"touch\",\"name\":\"Coin\",\"do\":\"setvar\",\"var\":\"score\",\"expr\":\"score + 10 * lives\"}. Enemy that charges only when the player is close => {\"when\":\"always\",\"cond\":\"dist(self.cx,self.cy,Player.cx,Player.cy) < 200\",\"do\":\"set\",\"prop\":\"vx\",\"expr\":\"sign(Player.cx - self.cx) * 180\"}. Click to buy/select => {\"when\":\"click\",\"do\":\"setvar\",\"var\":\"selected\",\"to\":1}.");
            sb.AppendLine("Example — \"when the player hits the Orb, move 2x faster\": {\"edit\":[{\"name\":\"Player\",\"custom\":[{\"when\":\"touch\",\"name\":\"Orb\",\"do\":\"boost\",\"factor\":2}]}]}");
            sb.AppendLine("IMPORTANT: a set action uses \"to\" (NOT \"value\") so it never clashes with a condition's \"value\". e.g. reset a counter with {\"when\":\"iflocal\",\"var\":\"jumps\",\"op\":\">=\",\"value\":3,\"do\":\"setlocal\",\"var\":\"jumps\",\"to\":0}.");
            sb.AppendLine("LOCAL vars (setlocal/addlocal/iflocal) are PER-OBJECT counters — each enemy copy has its own. Use them for 'every N' patterns. GLOBAL vars (setvar/addvar/ifvar) are shared (score, lives).");
            sb.AppendLine("'jump' needs physics Dynamic and some ground. vx right+, vy down+ (negative vy = up).");
            sb.AppendLine("Show variables with a Label whose text uses {name} (e.g. \"HP: {health}\"). Dialogue: say with text lines split by |.");
            sb.AppendLine("Example — \"enemy that jumps and every 3 jumps dashes forward\":");
            sb.AppendLine("{\"create\":[{\"name\":\"Jumper\",\"x\":640,\"y\":300,\"width\":48,\"height\":48,\"color\":\"#E5484B\",\"physics\":\"Dynamic\",\"behaviors\":[{\"kind\":\"Health\",\"nums\":{\"max\":3,\"respawn\":0}},{\"kind\":\"Damager\",\"texts\":{\"target\":\"Player\"}}],\"custom\":[{\"when\":\"timer\",\"every\":1.1,\"do\":\"jump\",\"force\":540},{\"when\":\"timer\",\"every\":1.1,\"do\":\"addlocal\",\"var\":\"jumps\",\"amount\":1},{\"when\":\"iflocal\",\"var\":\"jumps\",\"op\":\">=\",\"value\":3,\"do\":\"velocity\",\"vx\":-320,\"vy\":-200},{\"when\":\"iflocal\",\"var\":\"jumps\",\"op\":\">=\",\"value\":3,\"do\":\"setlocal\",\"var\":\"jumps\",\"to\":0}]}]}");
            sb.AppendLine("Example — \"spawn rocks that fall from the sky when I press Y\":");
            sb.AppendLine("{\"create\":[{\"name\":\"Rock\",\"width\":40,\"height\":40,\"color\":\"#8A7A6A\",\"physics\":\"Dynamic\",\"asPrefab\":true}],\"edit\":[{\"name\":\"Player\",\"behaviors\":[{\"kind\":\"Spawner\",\"texts\":{\"target\":\"Rock\",\"key\":\"Y\",\"at\":\"top\"},\"nums\":{\"max\":0}}]}]}");
            sb.AppendLine();

            // scene inventory so the AI can target/avoid-dup
            sb.AppendLine("CURRENT SCENE \"" + _scene.Name + "\":");
            sb.AppendLine("Layers: " + string.Join(", ", _scene.Layers.Select(l => $"\"{l.Name}\"({l.Kind}{(l.ScreenSpace || l.Kind == LayerKind.UI ? ",fixed" : "")})")));
            var allObjs = _scene.Layers.SelectMany(l => l.Objects).ToList();
            if (allObjs.Count == 0) sb.AppendLine("Objects: (none yet)");
            else foreach (var o in allObjs)
            {
                var ov = o.Variables.Count > 0 ? " vars{" + string.Join(",", o.Variables.Select(kv => $"{kv.Key}={kv.Value:0.##}")) + "}" : "";
                sb.AppendLine($"  \"{o.Name}\" [{string.Join(",", o.Behaviors.Select(b => b.Kind))}]{ov}");
            }
            if (_project.Variables.Count > 0) sb.AppendLine("Global variables: " + string.Join(", ", _project.Variables.Select(kv => $"{kv.Key}={kv.Value:0.##}")));
            if (_scene.Variables.Count > 0) sb.AppendLine("Scene variables: " + string.Join(", ", _scene.Variables.Select(kv => $"{kv.Key}={kv.Value:0.##}")));
            if (_selection.Count > 0)
                sb.AppendLine($"The user SELECTED these object(s): {string.Join(", ", _selection.Select(o => o.Name))}. Apply the requested change ONLY to the selected object(s) via \"edit\" (one entry per selected object, by name). Do NOT create new objects or touch other objects.");
            sb.AppendLine();

            if (_aiImages.Count > 0)
            {
                sb.AppendLine("IMAGES the user provided (use them in \"animations\" frames or \"sprite\"): " + string.Join(", ", _aiImages.Select(Path.GetFileName)));
                sb.AppendLine("Group frames by their names (e.g. idle*, walk*, attack*) into animations with those names.");
            }
            if (_aiSounds.Count > 0)
            {
                sb.AppendLine("SOUNDS the user provided: " + string.Join(", ", _aiSounds.Select(Path.GetFileName)));
                sb.AppendLine("To play a sound, add a SoundEmitter behavior with texts.asset = the sound filename (nums onStart=1 to play at start, loop=1 to loop, volume 0-1).");
            }
            if (_aiImages.Count > 0 || _aiSounds.Count > 0) sb.AppendLine();

            sb.AppendLine("EXAMPLES:");
            sb.AppendLine("User: \"make a player that walks, jumps, has health and can attack\" =>");
            sb.AppendLine("{\"create\":[{\"name\":\"Player\",\"x\":120,\"y\":300,\"width\":48,\"height\":64,\"color\":\"#3BC97A\",\"physics\":\"Dynamic\",\"behaviors\":[{\"kind\":\"Platformer\",\"nums\":{\"speed\":260,\"jump\":640}},{\"kind\":\"FollowCamera\"},{\"kind\":\"Health\",\"nums\":{\"max\":5,\"respawn\":1}},{\"kind\":\"Melee\",\"texts\":{\"key\":\"J\"}}]}]}");
            sb.AppendLine("User: \"give the player more health\" (player already exists) =>");
            sb.AppendLine("{\"edit\":[{\"name\":\"Player\",\"behaviors\":[{\"kind\":\"Health\",\"nums\":{\"max\":8}}]}]}");
            sb.AppendLine("User: \"create a global score at 0 and lives at 3, and set the scene timer to 60\" => {\"variables\":{\"global\":{\"score\":0,\"lives\":3},\"scene\":{\"timer\":60}}}");
            if (_aiImages.Count > 0)
                sb.AppendLine("User: \"with these images make a boss with full movement\" => {\"create\":[{\"name\":\"Boss\",\"x\":700,\"y\":200,\"width\":120,\"height\":120,\"physics\":\"Dynamic\",\"behaviors\":[{\"kind\":\"Platformer\",\"nums\":{\"speed\":120,\"jump\":500}},{\"kind\":\"Health\",\"nums\":{\"max\":20,\"respawn\":0}},{\"kind\":\"Chase\",\"texts\":{\"target\":\"Player\"}},{\"kind\":\"Damager\",\"texts\":{\"target\":\"Player\"}}],\"animations\":[{\"name\":\"idle\",\"frames\":[\"<idle images>\"],\"fps\":6},{\"name\":\"walk\",\"frames\":[\"<walk images>\"],\"fps\":10}]}]}");
            return sb.ToString();
        }

        // Applies a behavior/property spec (JsonElement object) to an existing object. Caller handles undo.
        private bool ApplyBehaviorsToObject(GameObjectData target, JsonElement spec, string userPrompt)
        {
            var result = target.Behaviors.Select(JsonClone).ToList();
            bool changed = false;

            if (spec.TryGetProperty("behaviors", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    if (!item.TryGetProperty("kind", out var kindEl)) continue;
                    var kindOpt = ResolveKind(kindEl.GetString());
                    if (kindOpt is not BehaviorKind kind || kind == BehaviorKind.Tilemap) continue;

                    var existing = result.FirstOrDefault(x => x.Kind == kind);
                    var data = existing ?? BehaviorCatalog.Info(kind).CreateData();
                    if (item.TryGetProperty("nums", out var nums) && nums.ValueKind == JsonValueKind.Object)
                        foreach (var p in nums.EnumerateObject())
                            if (AsNum(p.Value, out var nv)) data.Nums[p.Name] = nv;
                    if (item.TryGetProperty("texts", out var texts) && texts.ValueKind == JsonValueKind.Object)
                        foreach (var p in texts.EnumerateObject())
                            if (p.Value.ValueKind == JsonValueKind.String) data.Texts[p.Name] = p.Value.GetString() ?? "";
                            else if (p.Value.ValueKind == JsonValueKind.Number) data.Texts[p.Name] = p.Value.GetRawText(); // tolerate quoted-less values
                    if (existing == null) result.Add(data);
                    changed = true;
                }
            }

            if (spec.TryGetProperty("remove", out var rem) && rem.ValueKind == JsonValueKind.Array)
                foreach (var r in rem.EnumerateArray())
                    if (r.ValueKind == JsonValueKind.String && ResolveKind(r.GetString()) is BehaviorKind rk)
                        if (result.RemoveAll(x => x.Kind == rk) > 0) changed = true;

            if (spec.TryGetProperty("custom", out var cust) && cust.ValueKind == JsonValueKind.Array && cust.GetArrayLength() > 0)
            {
                var existing = result.FirstOrDefault(x => x.Kind == BehaviorKind.Custom);
                var data = existing ?? new BehaviorData { Kind = BehaviorKind.Custom };
                data.Texts["script"] = cust.GetRawText();
                if (existing == null) result.Add(data);
                changed = true;
            }

            if (ApplyBasics(target, spec)) changed = true;
            if (ApplyAnimations(target, spec)) changed = true;

            // resolve sound filenames (SoundEmitter asset) to project-relative paths
            foreach (var b in result.Where(b => b.Kind == BehaviorKind.SoundEmitter))
                if (b.Texts.TryGetValue("asset", out var a) && !string.IsNullOrWhiteSpace(a) && !a.Replace('\\', '/').Contains("Assets/"))
                { var rel = ResolveSound(a); if (rel != null) b.Texts["asset"] = rel; }

            // object variables
            if (spec.TryGetProperty("variables", out var ov) && ov.ValueKind == JsonValueKind.Object)
                foreach (var p in ov.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.Number) { target.Variables[p.Name] = p.Value.GetDouble(); changed = true; }
            if (spec.TryGetProperty("removeVariables", out var orv) && orv.ValueKind == JsonValueKind.Array)
                foreach (var el in orv.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.String && target.Variables.Remove(el.GetString() ?? "")) changed = true;

            // SAFEGUARD: Platformer/TopDown read the PLAYER's keyboard. On anything that isn't the player,
            // turn OFF the default controls so it doesn't mirror the player (it keeps the movement abilities;
            // move it with custom keys or a script).
            bool looksLikePlayer = NameLooksPlayer(target.Name) || result.Any(b => b.Kind == BehaviorKind.FollowCamera);
            if (!looksLikePlayer)
                foreach (var b in result.Where(b => b.Kind == BehaviorKind.Platformer || b.Kind == BehaviorKind.TopDown))
                    if (b.Num("controls", 1) >= 0.5) { b.Nums["controls"] = 0; changed = true; }

            bool customJumps = result.Any(b => b.Kind == BehaviorKind.Custom && b.Text("script", "").ToLowerInvariant().Contains("\"jump\""));

            // infer physics from resulting behaviors (don't trust the model's physics field)
            bool HasK(BehaviorKind k) => result.Any(x => x.Kind == k);
            bool hasMover = HasK(BehaviorKind.Platformer) || HasK(BehaviorKind.TopDown) || HasK(BehaviorKind.Patrol) ||
                            HasK(BehaviorKind.Chase) || HasK(BehaviorKind.Projectile) || HasK(BehaviorKind.Shooter);
            var low = userPrompt.ToLowerInvariant();
            bool gravityIntent = new[] { "gravity", "gravid", "fall", "falling", "cair", "queda" }.Any(low.Contains);
            PhysicsMode desired = target.Physics.Mode;
            if (HasK(BehaviorKind.Platformer) || gravityIntent || customJumps) desired = PhysicsMode.Dynamic;
            else if (HasK(BehaviorKind.Solid) && !hasMover) desired = PhysicsMode.Static;
            if (desired != target.Physics.Mode) { target.Physics.Mode = desired; changed = true; }

            target.Behaviors = result;
            return changed;
        }

        // x/y/width/height/color
        private bool ApplyBasics(GameObjectData obj, JsonElement spec)
        {
            bool changed = false;
            if (spec.TryGetProperty("x", out var x) && AsNum(x, out var xv)) { obj.X = xv; changed = true; }
            if (spec.TryGetProperty("y", out var y) && AsNum(y, out var yv)) { obj.Y = yv; changed = true; }
            if (spec.TryGetProperty("width", out var w) && AsNum(w, out var wv)) { obj.Width = Math.Max(2, wv); changed = true; }
            if (spec.TryGetProperty("height", out var h) && AsNum(h, out var hv)) { obj.Height = Math.Max(2, hv); changed = true; }
            if (spec.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String) { obj.Color = c.GetString() ?? obj.Color; changed = true; }
            if (spec.TryGetProperty("sprite", out var sp) && sp.ValueKind == JsonValueKind.String)
            {
                var rel = ResolveImage(sp.GetString() ?? "");
                if (rel != null) { obj.SpritePath = rel; changed = true; }
            }
            // appearance / aesthetics
            if (spec.TryGetProperty("opacity", out var op) && AsNum(op, out var opv)) { obj.Opacity = Math.Clamp(opv, 0, 1); changed = true; }
            if (spec.TryGetProperty("borderColor", out var bc) && bc.ValueKind == JsonValueKind.String) { obj.BorderColor = bc.GetString(); changed = true; }
            if (spec.TryGetProperty("borderWidth", out var bw) && AsNum(bw, out var bwv)) { obj.BorderWidth = Math.Max(0, bwv); changed = true; }
            if (spec.TryGetProperty("cornerRadius", out var cr) && AsNum(cr, out var crv)) { obj.CornerRadius = Math.Max(0, crv); changed = true; }
            if (spec.TryGetProperty("font", out var ff) && ff.ValueKind == JsonValueKind.String) { obj.FontFamily = ff.GetString(); changed = true; }
            if (spec.TryGetProperty("bold", out var bd) && AsBool(bd, out var bdv)) { obj.Bold = bdv; changed = true; }
            if (spec.TryGetProperty("group", out var gr) && gr.ValueKind == JsonValueKind.String) { obj.Group = gr.GetString() ?? ""; changed = true; }
            return changed;
        }

        private bool ApplyAnimations(GameObjectData obj, JsonElement spec)
        {
            if (!spec.TryGetProperty("animations", out var arr) || arr.ValueKind != JsonValueKind.Array) return false;
            bool changed = false;
            foreach (var a in arr.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.Object) continue;
                var anim = new SpriteAnimation { Name = a.TryGetProperty("name", out var nm) ? (nm.GetString() ?? "anim") : "anim" };
                if (a.TryGetProperty("fps", out var fps) && fps.ValueKind == JsonValueKind.Number) anim.Fps = Math.Clamp(fps.GetDouble(), 1, 60);
                if (a.TryGetProperty("frames", out var fr) && fr.ValueKind == JsonValueKind.Array)
                    foreach (var f in fr.EnumerateArray())
                        if (f.ValueKind == JsonValueKind.String) { var rel = ResolveImage(f.GetString() ?? ""); if (rel != null) anim.Frames.Add(rel); }
                if (anim.Frames.Count == 0) continue;
                obj.Animations.RemoveAll(x => x.Name.Equals(anim.Name, StringComparison.OrdinalIgnoreCase));
                obj.Animations.Add(anim);
                obj.DefaultAnimation ??= anim.Name;
                changed = true;
            }
            return changed;
        }

        // Maps the model's loose kind strings to real BehaviorKinds (synonyms) — key to small-model reliability.
        private static BehaviorKind? ResolveKind(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (Enum.TryParse<BehaviorKind>(s, true, out var k)) return k;
            var t = s.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
            return t switch
            {
                "walk" or "run" or "move" or "movement" or "jump" or "platform" or "platforming" or "sidescroller" or "sidescroll" or "jumping" or "move+jump" => BehaviorKind.Platformer,
                "topdown" or "eightway" or "8way" or "wasd" or "topdownmovement" => BehaviorKind.TopDown,
                "attack" or "sword" or "hit" or "nail" or "slash" or "swing" or "swordattack" or "meleeattack" => BehaviorKind.Melee,
                "heal" or "hp" or "life" or "lives" or "healthbar" or "hitpoints" => BehaviorKind.Health,
                "hurt" or "damage" or "spike" or "spikes" or "hazard" or "contactdamage" or "trap" or "dmg" => BehaviorKind.Damager,
                "shoot" or "fire" or "gun" or "ranged" or "blaster" or "shooting" => BehaviorKind.Shooter,
                "bullet" or "projectilemove" => BehaviorKind.Projectile,
                "follow" or "camera" or "followcam" or "camerafollow" => BehaviorKind.FollowCamera,
                "pace" or "backandforth" => BehaviorKind.Patrol,
                "pursue" or "follower" or "chaseplayer" => BehaviorKind.Chase,
                "rotate" or "rotation" or "spinning" => BehaviorKind.Spin,
                "generator" or "emitter" => BehaviorKind.Spawner,
                "audio" or "sfx" or "music" or "sound" => BehaviorKind.SoundEmitter,
                "glow" or "lamp" or "lantern" or "lighting" => BehaviorKind.LightSource,
                "scrollbackground" => BehaviorKind.Parallax,
                "collect" or "coin" or "pickup" or "gem" or "collect ible" => BehaviorKind.Collectible,
                "door" or "teleport" or "goto" or "scenechange" or "portal" or "nextlevel" => BehaviorKind.GoToScene,
                "ground" or "wall" or "block" or "collider" or "floor" => BehaviorKind.Solid,
                "text" or "hud" or "score" or "scoretext" or "healthtext" => BehaviorKind.Label,
                "respawnpoint" or "flag" or "save" => BehaviorKind.Checkpoint,
                "offscreen" or "despawn" => BehaviorKind.DestroyOffscreen,
                "pushblock" or "pushableblock" or "crate" or "movableblock" or "sokoban" => BehaviorKind.Pushable,
                "lever" or "toggle" or "switchlever" => BehaviorKind.Switch,
                "gate" or "lockeddoor" or "lockedgate" or "barrier" or "keydoor" or "abilitygate" => BehaviorKind.Gate,
                "npc" or "sign" or "talk" or "dialogue" or "dialog" or "read" or "interactable" => BehaviorKind.Interact,
                "ability" or "unlock" or "keypickup" or "upgrade" or "powerup" or "abilitypickup" or "keyitem" => BehaviorKind.AbilityPickup,
                "zone" or "trigger" or "triggerzone" or "region" or "triggerarea" => BehaviorKind.Area,
                "procedural" or "procgen" or "proceduralgeneration" or "randommap" or "spawnarea" or "mapgenerator" => BehaviorKind.Generator,
                "pathfind" or "pathfinding" or "smartchase" or "navigate" or "chasearound" or "seek" => BehaviorKind.PathChase,
                "spring" or "bouncepad" or "trampoline" or "jumppad" or "bouncer" or "launchpad" => BehaviorKind.Spring,
                "minimap" or "radar" or "worldmap" => BehaviorKind.Minimap,
                "particle" or "particles" or "emitter" or "explosion" or "sparkle" or "smoke" or "fx" => BehaviorKind.Particles,
                "button" or "uibutton" or "menubutton" or "btn" => BehaviorKind.UIButton,
                "bar" or "healthbar" or "progressbar" or "progress" or "hpbar" or "manabar" => BehaviorKind.Bar,
                _ => null
            };
        }

        private static bool NameLooksPlayer(string name)
        {
            var n = name.ToLowerInvariant();
            return new[] { "player", "hero", "protagon", "jogador", "personagem", "character", "pc" }.Any(n.Contains);
        }

        private static bool IsNewIntent(string p)
        {
            p = p.ToLowerInvariant();
            return new[] { "another", "a new", "new ", "second", "third", "spawn", "extra", "outro", "outra", "mais um", "mais uma", "novo ", "nova " }.Any(p.Contains);
        }

        private static bool IsModifyIntent(string p)
        {
            p = p.ToLowerInvariant();
            return new[] { "add", "give", "set ", "change", "increase", "more", "the player", "player's", "adicion", "aumenta", "muda", "coloca", "deixa", "bota", "poe", "põe", "vida do", "mais vida" }.Any(p.Contains);
        }

        private GameObjectData? ResolveTarget(string? name, string userPrompt)
        {
            var all = _scene.Layers.SelectMany(l => l.Objects).ToList();
            if (!string.IsNullOrWhiteSpace(name))
            {
                var exact = all.FirstOrDefault(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
                if (name.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0) { var p = FindPlayer(); if (p != null) return p; }
                var contains = all.FirstOrDefault(o => o.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf(o.Name, StringComparison.OrdinalIgnoreCase) >= 0);
                if (contains != null) return contains;
            }
            var low = userPrompt.ToLowerInvariant();
            if (low.Contains("player") || low.Contains("jogador") || low.Contains("character") || low.Contains("personagem")) return FindPlayer();
            return null;
        }

        private GameObjectData? FindPlayer()
        {
            var all = _scene.Layers.SelectMany(l => l.Objects).ToList();
            return all.FirstOrDefault(o => o.Name.Equals("Player", StringComparison.OrdinalIgnoreCase))
                ?? all.FirstOrDefault(o => o.Behaviors.Any(b => b.Kind == BehaviorKind.Platformer))
                ?? all.FirstOrDefault(o => o.Behaviors.Any(b => b.Kind == BehaviorKind.TopDown))
                ?? all.FirstOrDefault(o => o.Name.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // behaviors that turn an object into a platform/coin/enemy/door/etc. — almost never belong on the Player
        private static readonly BehaviorKind[] NonPlayerKinds =
        {
            BehaviorKind.Solid, BehaviorKind.Collectible, BehaviorKind.Patrol, BehaviorKind.Chase,
            BehaviorKind.GoToScene, BehaviorKind.Parallax, BehaviorKind.DestroyOffscreen, BehaviorKind.Projectile,
            BehaviorKind.Pushable, BehaviorKind.Switch, BehaviorKind.Gate, BehaviorKind.Interact,
            BehaviorKind.AbilityPickup, BehaviorKind.Area, BehaviorKind.Generator,
            BehaviorKind.PathChase, BehaviorKind.Spring, BehaviorKind.Minimap,
            BehaviorKind.Particles, BehaviorKind.UIButton, BehaviorKind.Bar
        };

        private static readonly string[] ThingNouns =
        {
            "coin","platform","enemy","boss","npc","door","spike","wall","obstacle","bullet","item","powerup",
            "crystal","orb","gem","rock","block","trap","portal","chest","monster","slime","ghost","turret","hazard","pickup","collectible"
        };

        private static readonly string[] AbilityWords =
        { "health","hp","heart","life","lives","speed","faster","dash","double jump","wall jump","attack","sword","melee","shoot","gun","fire","glow","gravity","jump" };

        private static string? FirstThingNoun(string prompt)
        {
            var p = prompt.ToLowerInvariant();
            foreach (var n in ThingNouns)
                if (System.Text.RegularExpressions.Regex.IsMatch(p, $@"\b{n}s?\b")) return char.ToUpper(n[0]) + n.Substring(1);
            return null;
        }
        private static bool MentionsThing(string prompt) => FirstThingNoun(prompt) != null;
        private static bool HasAbility(string prompt) { var p = prompt.ToLowerInvariant(); return AbilityWords.Any(p.Contains); }

        private bool SpecHasNonPlayerBehavior(JsonElement spec)
        {
            if (!spec.TryGetProperty("behaviors", out var arr) || arr.ValueKind != JsonValueKind.Array) return false;
            foreach (var it in arr.EnumerateArray())
                if (it.ValueKind == JsonValueKind.Object && it.TryGetProperty("kind", out var k) && ResolveKind(k.GetString()) is BehaviorKind bk && NonPlayerKinds.Contains(bk))
                    return true;
            return false;
        }

        private string? ResolveImage(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) return null;
            var f = Path.GetFileName(filename);
            var hit = _aiImages.FirstOrDefault(r => string.Equals(Path.GetFileName(r), f, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;
            try
            {
                var dir = Path.Combine(_project.FolderPath!, "Assets", "sprites");
                if (Directory.Exists(dir))
                {
                    var match = Directory.GetFiles(dir).FirstOrDefault(p => string.Equals(Path.GetFileName(p), f, StringComparison.OrdinalIgnoreCase));
                    if (match != null) return Path.Combine("Assets", "sprites", Path.GetFileName(match));
                }
            }
            catch { }
            return null;
        }

        private static string? ExtractJson(string reply)
        {
            int a = reply.IndexOf('{'), b = reply.LastIndexOf('}');
            return (a >= 0 && b > a) ? reply.Substring(a, b - a + 1) : null;
        }

        // Accepts a JSON number OR a numeric string ("260") — small models often quote numbers.
        private static bool AsNum(JsonElement e, out double val)
        {
            if (e.ValueKind == JsonValueKind.Number) { val = e.GetDouble(); return true; }
            if (e.ValueKind == JsonValueKind.String &&
                double.TryParse(e.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out val))
                return true;
            val = 0; return false;
        }

        // ---- headless test hooks (used by --aijsontest) ----
        public (bool ok, string summary) TestApplySpec(string reply) { bool r = ApplyProjectSpec(reply, "", out var s); return (r, s); }
        public GameObjectData? TestFindObject(string name) => _scene.Layers.SelectMany(l => l.Objects).FirstOrDefault(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        public GameTheme TestTheme => _project.Theme;

        // Accepts a JSON bool OR "true"/"false"/"1"/"0" string.
        private static bool AsBool(JsonElement e, out bool val)
        {
            if (e.ValueKind == JsonValueKind.True) { val = true; return true; }
            if (e.ValueKind == JsonValueKind.False) { val = false; return true; }
            if (e.ValueKind == JsonValueKind.String) { var s = (e.GetString() ?? "").Trim().ToLowerInvariant(); if (s is "true" or "1" or "yes") { val = true; return true; } if (s is "false" or "0" or "no") { val = false; return true; } }
            if (e.ValueKind == JsonValueKind.Number) { val = e.GetDouble() != 0; return true; }
            val = false; return false;
        }

        private void ApplyGlobalSceneVars(JsonElement root, Action ensureUndo, List<string> changes)
        {
            var parts = new List<string>();
            void SetInto(JsonElement obj, Dictionary<string, double> dict, string label)
            {
                foreach (var p in obj.EnumerateObject())
                    if (AsNum(p.Value, out var nv)) { dict[p.Name] = nv; parts.Add($"{label} {p.Name}={nv:0.##}"); }
            }
            void RemoveFrom(JsonElement arr, Dictionary<string, double> dict, string label)
            {
                foreach (var e in arr.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String && dict.Remove(e.GetString() ?? "")) parts.Add($"{label} -{e.GetString()}");
            }

            if (root.TryGetProperty("variables", out var v) && v.ValueKind == JsonValueKind.Object)
            {
                if (v.TryGetProperty("global", out var g) && g.ValueKind == JsonValueKind.Object) SetInto(g, _project.Variables, "global");
                if (v.TryGetProperty("scene", out var s) && s.ValueKind == JsonValueKind.Object) SetInto(s, _scene.Variables, "scene");
            }
            if (root.TryGetProperty("removeVariables", out var rv) && rv.ValueKind == JsonValueKind.Object)
            {
                if (rv.TryGetProperty("global", out var g) && g.ValueKind == JsonValueKind.Array) RemoveFrom(g, _project.Variables, "global");
                if (rv.TryGetProperty("scene", out var s) && s.ValueKind == JsonValueKind.Array) RemoveFrom(s, _scene.Variables, "scene");
            }

            if (parts.Count > 0) { ensureUndo(); changes.Add("Variables — " + string.Join("; ", parts)); }

            // in-game THEME (fonts + dialogue box look)
            if (root.TryGetProperty("theme", out var th) && th.ValueKind == JsonValueKind.Object)
            {
                ensureUndo();
                var t = _project.Theme ??= new GameTheme();
                string S(string k, string d) => th.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.String ? (e.GetString() ?? d) : d;
                double N(string k, double d) => th.TryGetProperty(k, out var e) && AsNum(e, out var nv) ? nv : d;
                t.FontFamily = S("font", t.FontFamily);
                t.DialogBg = S("dialogBg", t.DialogBg);
                t.DialogBorder = S("dialogBorder", t.DialogBorder);
                t.DialogText = S("dialogText", t.DialogText);
                t.DialogSpeaker = S("dialogSpeaker", t.DialogSpeaker);
                t.DialogEffect = S("dialogEffect", t.DialogEffect);
                t.DialogFontSize = N("dialogFontSize", t.DialogFontSize);
                t.DialogCorner = N("dialogCorner", t.DialogCorner);
                t.DialogSpeed = N("dialogSpeed", t.DialogSpeed);
                changes.Add("Theme updated");
            }
        }

        private void ApplyLayerArray(JsonElement lyArr, Action ensureUndo, List<string> changes)
        {
            var acts = new List<Action>(); var lch = new List<string>();
            foreach (var item in lyArr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("name", out var nm)) continue;
                var layer = _scene.Layers.FirstOrDefault(l => l.Name.Equals(nm.GetString(), StringComparison.OrdinalIgnoreCase));
                if (layer == null) continue;
                var lyr = layer;
                if (item.TryGetProperty("screenSpace", out var ss) && (ss.ValueKind == JsonValueKind.True || ss.ValueKind == JsonValueKind.False))
                { bool v = ss.GetBoolean(); acts.Add(() => lyr.ScreenSpace = v); lch.Add($"{lyr.Name}:{(v ? "fixed" : "scroll")}"); }
                if (item.TryGetProperty("visible", out var vv) && (vv.ValueKind == JsonValueKind.True || vv.ValueKind == JsonValueKind.False))
                { bool v = vv.GetBoolean(); acts.Add(() => lyr.Visible = v); lch.Add($"{lyr.Name}:{(v ? "visible" : "hidden")}"); }
            }
            if (acts.Count > 0) { ensureUndo(); foreach (var a in acts) a(); changes.Add("Layers — " + string.Join("; ", lch)); }
        }

        private GameObjectData? BuildObjectFromSpec(JsonElement spec, string userPrompt, string? nameOverride = null)
        {
            var obj = new GameObjectData { Color = "#9A9AA0", Width = 48, Height = 48 };
            obj.Name = nameOverride ?? (spec.TryGetProperty("name", out var nm) ? (nm.GetString() ?? "Object") : "Object");
            obj.Type = obj.Name;

            ApplyBehaviorsToObject(obj, spec, userPrompt);

            // "asPrefab": a thing to spawn later (doesn't appear in the scene until spawned)
            if (spec.TryGetProperty("asPrefab", out var ap) && ap.ValueKind == JsonValueKind.True)
            {
                obj.Id = 0; obj.X = 0; obj.Y = 0;
                _project.Prefabs.RemoveAll(p => p.Name.Equals(obj.Name, StringComparison.OrdinalIgnoreCase));
                _project.Prefabs.Add(new PrefabDef { Name = obj.Name, Icon = "▢", Template = obj });
                return obj;
            }

            var layerName = spec.TryGetProperty("layer", out var ly) ? ly.GetString() : null;
            var layer = _scene.Layers.FirstOrDefault(l => l.Name.Equals(layerName, StringComparison.OrdinalIgnoreCase))
                        ?? _activeLayer ?? _scene.Layers.FirstOrDefault(l => l.Kind == LayerKind.Base) ?? _scene.Layers.FirstOrDefault();
            if (layer == null) return null;

            if (!spec.TryGetProperty("x", out _)) obj.X = _scene.Width / 2 - obj.Width / 2;
            if (!spec.TryGetProperty("y", out _)) obj.Y = _scene.Height / 2 - obj.Height / 2;

            obj.Id = _project.NextId;
            obj.Name = UniqueName(obj.Name);
            layer.Objects.Add(obj);
            return obj;
        }

        private bool ApplyProjectSpec(string reply, string userPrompt, out string summary)
        {
            summary = "";
            var json = ExtractJson(reply);
            if (json == null) return false;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(json, LenientJson); } catch { return false; }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return false;

                var changes = new List<string>();
                bool pushed = false;
                void EnsureUndo() { if (!pushed) { PushUndo(); pushed = true; } }

                bool hasCreate = root.TryGetProperty("create", out var cr) && cr.ValueKind == JsonValueKind.Array;
                bool hasEdit = root.TryGetProperty("edit", out var ed) && ed.ValueKind == JsonValueKind.Array;

                // SCOPED to selection: when the user selected objects, apply the change ONLY to them (no creation).
                if (_selection.Count > 0)
                {
                    var specs = new List<(string? name, JsonElement spec)>();
                    if (hasEdit) foreach (var s in ed.EnumerateArray()) if (s.ValueKind == JsonValueKind.Object) specs.Add((s.TryGetProperty("name", out var n) ? n.GetString() : null, s));
                    if (hasCreate) foreach (var s in cr.EnumerateArray()) if (s.ValueKind == JsonValueKind.Object) specs.Add((s.TryGetProperty("name", out var n) ? n.GetString() : null, s));
                    if (specs.Count == 0 && (root.TryGetProperty("behaviors", out _) || root.TryGetProperty("custom", out _))) specs.Add((null, root));

                    JsonElement? SpecFor(GameObjectData o)
                    {
                        foreach (var (nm, sp) in specs) if (nm != null && o.Name.Equals(nm, StringComparison.OrdinalIgnoreCase)) return sp;
                        if (specs.Count == 1) return specs[0].spec;
                        foreach (var (nm, sp) in specs) if (nm == null) return sp;
                        return null;
                    }

                    foreach (var obj in _selection.ToList())
                    {
                        var sp = SpecFor(obj);
                        if (sp is not JsonElement spec) continue;
                        EnsureUndo();
                        if (ApplyBehaviorsToObject(obj, spec, userPrompt)) changes.Add($"Edited \"{obj.Name}\"");
                    }

                    if (root.TryGetProperty("layers", out var lyScoped) && lyScoped.ValueKind == JsonValueKind.Array)
                        ApplyLayerArray(lyScoped, EnsureUndo, changes);
                    ApplyGlobalSceneVars(root, EnsureUndo, changes);

                    if (changes.Count == 0) return false;
                    summary = string.Join("\n", changes);
                    return true;
                }

                if (hasCreate)
                    foreach (var spec in cr.EnumerateArray())
                    {
                        if (spec.ValueKind != JsonValueKind.Object) continue;
                        var name = spec.TryGetProperty("name", out var cn) ? cn.GetString() : null;

                        bool asPrefab = spec.TryGetProperty("asPrefab", out var apf) && apf.ValueKind == JsonValueKind.True;

                        // REDIRECT B: "add a dash/melee/health…" (an ability for the player, no new thing named) that the
                        // model wrongly turned into a new object → apply it to the player instead.
                        if (!asPrefab && HasAbility(userPrompt) && !MentionsThing(userPrompt) && !SpecHasNonPlayerBehavior(spec))
                        {
                            var player = FindPlayer();
                            if (player != null && !(name ?? "").Equals(player.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                EnsureUndo();
                                if (ApplyBehaviorsToObject(player, spec, userPrompt)) changes.Add($"Edited \"{player.Name}\"");
                                continue;
                            }
                        }

                        // don't duplicate: unless it's a prefab or the user clearly wants a NEW/another one, edit the existing object
                        GameObjectData? existing = null;
                        if (!asPrefab && !IsNewIntent(userPrompt))
                        {
                            existing = _scene.Layers.SelectMany(l => l.Objects).FirstOrDefault(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                            if (existing == null && IsModifyIntent(userPrompt)) existing = ResolveTarget(name, userPrompt);
                        }

                        EnsureUndo();
                        if (existing != null)
                        {
                            if (ApplyBehaviorsToObject(existing, spec, userPrompt)) changes.Add($"Edited \"{existing.Name}\"");
                        }
                        else
                        {
                            var obj = BuildObjectFromSpec(spec, userPrompt);
                            if (obj != null) changes.Add((asPrefab ? "Prefab \"" : "Created \"") + obj.Name + (asPrefab ? "\"" : $"\" [{string.Join(",", obj.Behaviors.Select(b => b.Kind))}]"));
                        }
                    }

                if (hasEdit)
                    foreach (var spec in ed.EnumerateArray())
                    {
                        if (spec.ValueKind != JsonValueKind.Object) continue;
                        var nm = spec.TryGetProperty("name", out var n) ? n.GetString() : null;
                        var obj = ResolveTarget(nm, userPrompt) ?? _selected;
                        if (obj == null) continue;

                        // REDIRECT A: the model tried to put a platform/coin/enemy/door behavior ON the player.
                        // That's almost always wrong → make a NEW object with those behaviors instead.
                        if (ReferenceEquals(obj, FindPlayer()) && SpecHasNonPlayerBehavior(spec))
                        {
                            EnsureUndo();
                            var created = BuildObjectFromSpec(spec, userPrompt, FirstThingNoun(userPrompt) ?? "Object");
                            if (created != null) changes.Add($"Created \"{created.Name}\" [{string.Join(",", created.Behaviors.Select(b => b.Kind))}]");
                            continue;
                        }

                        EnsureUndo();
                        if (ApplyBehaviorsToObject(obj, spec, userPrompt)) changes.Add($"Edited \"{obj.Name}\"");
                    }

                // simple/back-compat: top-level behaviors/custom and no create/edit → selected, player, or a new object
                if (!hasCreate && !hasEdit && (root.TryGetProperty("behaviors", out _) || root.TryGetProperty("custom", out _)))
                {
                    if (_selection.Count == 0 && SpecHasNonPlayerBehavior(root))
                    {
                        // platform/coin/enemy/door behavior with no target → it's a NEW object, not the player
                        EnsureUndo();
                        var created = BuildObjectFromSpec(root, userPrompt, FirstThingNoun(userPrompt) ?? "Object");
                        if (created != null) changes.Add($"Created \"{created.Name}\" [{string.Join(",", created.Behaviors.Select(b => b.Kind))}]");
                    }
                    else
                    {
                        var obj = _selected ?? FindPlayer();
                        if (obj != null) { EnsureUndo(); if (ApplyBehaviorsToObject(obj, root, userPrompt)) changes.Add($"Edited \"{obj.Name}\""); }
                    }
                }

                if (root.TryGetProperty("layers", out var lyArr) && lyArr.ValueKind == JsonValueKind.Array)
                    ApplyLayerArray(lyArr, EnsureUndo, changes);
                ApplyGlobalSceneVars(root, EnsureUndo, changes);

                if (changes.Count == 0) return false;
                summary = string.Join("\n", changes);
                return true;
            }
        }

        // ---- Brainstorm: free-form chat about the game (no edits) ----
        private async Task<string> BrainstormAsync(string userPrompt)
        {
            var sys = new StringBuilder();
            sys.AppendLine("You are a friendly, concise game-design assistant inside EasyEngine (a 2D game maker).");
            sys.AppendLine("Help the user brainstorm and plan their 2D game: ideas, mechanics, levels, characters, story, balancing, scope.");
            sys.AppendLine("Be practical and specific; keep answers short (a few sentences or a tight list). Reply in the user's language.");
            sys.AppendLine("You are only chatting here — do NOT output JSON or try to build anything. When an idea is ready to build, tell them to switch to Build mode and what to ask for.");
            var allObjs = _scene.Layers.SelectMany(l => l.Objects).Select(o => o.Name);
            sys.AppendLine($"Context: project \"{_project.Name}\", scene \"{_scene.Name}\" with objects: {(allObjs.Any() ? string.Join(", ", allObjs) : "(empty)")}.");
            return await AiGenerateAsync(userPrompt, sys.ToString(), json: false, temperature: 0.7);
        }

        private Border AddAiMessage(string who, string text, bool fromAi)
        {
            var bubble = new Border
            {
                Background = fromAi ? (SolidColorBrush)FindResource("Bg2") : (SolidColorBrush)FindResource("Accent"),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = who, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = (SolidColorBrush)FindResource("TextDim"), Margin = new Thickness(0,0,0,3) },
                        new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = fromAi ? (SolidColorBrush)FindResource("Text") : Brushes.White, FontFamily = new FontFamily("Consolas") }
                    }
                }
            };
            AiLog.Children.Add(bubble);
            return bubble;
        }

        // ============================================================
        //  TOOLBAR
        // ============================================================
        private void New_Click(object sender, RoutedEventArgs e)
        {
            var launcher = new LauncherWindow();
            Application.Current.MainWindow = launcher;
            launcher.Show();
            Close();
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = $"EasyEngine project (*{ProjectIO.Extension})|*{ProjectIO.Extension}" };
            if (dlg.ShowDialog() == true)
            {
                try { var p = ProjectIO.Load(dlg.FileName); var ed = new MainWindow(p); Application.Current.MainWindow = ed; ed.Show(); Close(); }
                catch (Exception ex) { MessageBox.Show("Error opening: " + ex.Message); }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try { ProjectIO.Save(_project); Title = $"EasyEngine — {_project.Name} · {_scene.Name}"; Status($"Saved to {_project.FilePath}"); }
            catch (Exception ex) { MessageBox.Show("Error saving: " + ex.Message); }
        }

        private void Variables_Click(object sender, RoutedEventArgs e)
        {
            var win = new VariablesWindow(_project, _scene, _selected) { Owner = this };
            win.ShowDialog();
            if (win.Changed) Dirty();
            if (_selected != null) BuildInspector(_selected);
        }

        private void Test_Click(object sender, RoutedEventArgs e)
        {
            try { ProjectIO.Save(_project); } catch { }
            var play = new PlayWindow(_project, _scene.Name) { Owner = this };
            play.ShowDialog();
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_project.FolderPath)) { MessageBox.Show("Save the project first."); return; }
            try { ProjectIO.Save(_project); } catch { }

            var dlg = new OpenFolderDialog { Title = "Choose a folder to export the game into" };
            if (dlg.ShowDialog() != true) return;

            var safe = string.Concat(_project.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
            if (string.IsNullOrWhiteSpace(safe)) safe = "Game";
            var outRoot = Path.Combine(dlg.FolderName, safe);
            var dotnet = FindDotnet();
            var csproj = FindEngineCsproj();
            bool selfContained = dotnet != null && csproj != null;
            var gameSrc = _project.FolderPath!;

            var btn = (Button)sender;
            btn.IsEnabled = false;
            Status(selfContained ? "Exporting standalone game… (can take a minute)" : "Exporting…");

            string? error = null;
            try
            {
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(outRoot);
                    if (selfContained)
                        RunProcess(dotnet!, $"publish \"{csproj}\" -c Release -r win-x64 --self-contained true -p:DebugType=none -p:SatelliteResourceLanguages=en -o \"{outRoot}\"");
                    else
                        CopyDir(AppContext.BaseDirectory, outRoot, new[] { ".pdb" });

                    CopyDir(gameSrc, Path.Combine(outRoot, "game"));

                    // rename the launcher exe to the game's name (apphost still runs EasyEngine.dll)
                    var src = Path.Combine(outRoot, "EasyEngine.exe");
                    var dst = Path.Combine(outRoot, safe + ".exe");
                    if (File.Exists(src) && !string.Equals(src, dst, StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(dst)) File.Delete(dst);
                        File.Copy(src, dst);
                    }
                });
            }
            catch (Exception ex) { error = ex.Message; }
            finally { btn.IsEnabled = true; }

            if (error != null) { Status("Export failed"); MessageBox.Show("Export failed:\n" + error, "Export", MessageBoxButton.OK, MessageBoxImage.Error); return; }

            Status("Exported to " + outRoot);
            var note = selfContained
                ? "Self-contained build — the player does NOT need .NET installed. Zip and share the whole folder."
                : "The target PC needs the free .NET 8 Desktop Runtime (couldn't find the SDK to make a self-contained build).";
            MessageBox.Show($"Game exported!\n\nFolder:\n{outRoot}\n\nDouble-click \"{safe}.exe\" to play.\n\n{note}",
                "Export complete", MessageBoxButton.OK, MessageBoxImage.Information);
            try { Process.Start("explorer.exe", outRoot); } catch { }
        }

        private static string? FindDotnet()
        {
            var pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
            if (File.Exists(pf)) return pf;
            return "dotnet"; // fall back to PATH
        }

        private static string? FindEngineCsproj()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                var proj = dir.GetFiles("*.csproj").FirstOrDefault();
                if (proj != null) return proj.FullName;
            }
            return null;
        }

        private static void RunProcess(string file, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = file, Arguments = args,
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using var p = Process.Start(psi) ?? throw new Exception("Could not start " + file);
            string outp = p.StandardOutput.ReadToEnd();
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) throw new Exception($"Build failed (exit {p.ExitCode}).\n" + (string.IsNullOrWhiteSpace(err) ? outp : err));
        }

        private static void CopyDir(string src, string dst, string[]? skipExt = null)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src))
            {
                if (skipExt != null && skipExt.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);
            }
            foreach (var dir in Directory.GetDirectories(src))
                CopyDir(dir, Path.Combine(dst, Path.GetFileName(dir)), skipExt);
        }

        // ============================================================
        //  UTIL
        // ============================================================
        private SolidColorBrush BrushFrom(string hex, string fallback)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)); } catch { return Brushes.Gray; } }
        }

        private void ShowLastAiChange()
        {
            if (string.IsNullOrEmpty(_aiLastSummary))
            {
                Status("No AI changes yet.");
                return;
            }

            Status("Last AI change: " + _aiLastSummary.Replace("\n", " | "));

            if (_aiLastObjects.Count > 0)
            {
                var name = _aiLastObjects[0];
                var obj = _scene.Layers.SelectMany(l => l.Objects)
                    .FirstOrDefault(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (obj != null)
                {
                    _selected = obj;
                    _selection.Clear();
                    foreach (var n in _aiLastObjects)
                    {
                        var o = _scene.Layers.SelectMany(l => l.Objects)
                            .FirstOrDefault(x => x.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
                        if (o != null) _selection.Add(o);
                    }
                    BuildInspector(obj);
                    RenderScene();
                    ScrollToObject(obj);
                }
            }

            MessageBox.Show(_aiLastSummary, "Last AI Change", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ScrollToObject(GameObjectData obj)
        {
            double cx = (obj.X + obj.Width / 2.0) * _zoom;
            double cy = (obj.Y + obj.Height / 2.0) * _zoom;
            SceneScroll.ScrollToHorizontalOffset(cx - SceneScroll.ViewportWidth / 2);
            SceneScroll.ScrollToVerticalOffset(cy - SceneScroll.ViewportHeight / 2);
        }

        private bool IsDirty => Title.EndsWith(" *");

        private void Dirty() { if (!_restoring) Title = $"EasyEngine — {_project.Name} · {_scene.Name} *"; }
        private void Status(string msg) => StatusText.Text = msg;

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (IsDirty)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Do you want to save before closing?",
                    "EasyEngine",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    try { ProjectIO.Save(_project); }
                    catch (Exception ex) { MessageBox.Show("Error saving: " + ex.Message); e.Cancel = true; return; }
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
            }
            base.OnClosing(e);
        }
    }
}
