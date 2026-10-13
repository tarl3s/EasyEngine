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
        private bool _useCloud;                       // true = cloud provider; false = Ollama (local)
        private string _cloudProvider = "auto";       // "auto" or a provider id (groq/gemini/openrouter)
        private string _cloudModelOverride = "";       // optional model override for the selected provider

        // tolerant JSON parsing — small models sometimes add // comments or trailing commas

        private readonly List<string> _aiImages = new(); // relative paths of images the user gave the AI
        private readonly List<string> _aiSounds = new(); // relative paths of audio the user gave the AI
        private AiMode _aiMode; // Build makes changes; Brainstorm and Learn only chat
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
        private readonly List<TileKind> _tmPalette = new();
        private bool _tmPainting;
        private readonly Dictionary<int, Rectangle> _tmCellRects = new();

        // AdvancedPatrol: drawing a path on the scene
        private BehaviorData? _pathDrawBh;
        private GameObjectData? _pathDrawObj;

        // scene zoom
        private double _zoom = 1.0;
        private const double MinZoom = 0.08, MaxZoom = 6.0;

        // scene pan (middle-mouse drag)
        private bool _panning;
        private Point _panStartMouse;
        private double _panStartH, _panStartV;

        /// <summary>Projects saved before a behavior gained a setting lack its key: the inspector shows the catalog default
        /// but the game read nothing (e.g. an old Minimap's "maxKey"). Fill each missing key with that default.</summary>
        internal static void FillMissingBehaviorKeys(Project project)
        {
            var objects = project.Scenes.SelectMany(s => s.Layers).SelectMany(l => l.Objects).Concat(project.Prefabs.Select(p => p.Template));
            foreach (var bh in objects.SelectMany(o => o.Behaviors))
            {
                if (BehaviorCatalog.Info(bh.Kind) is not BehaviorInfo info) continue; // a kind this version doesn't know
                foreach (var n in info.Nums) bh.Nums.TryAdd(n.Key, n.Default);
                foreach (var t in info.Texts) bh.Texts.TryAdd(t.Key, t.Default);
            }
        }

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
            FillMissingBehaviorKeys(_project);
            RefreshSceneCombo();
            SwitchScene(project.GetStartScene());
            RefreshPalette();
            InitDocking();
            SetTab(false); // default to the AI tab
            SetAiMode(AiMode.Build);
            UpdateAiTarget();
            AiSettings.Load();
            BuildCloudKeyRows();
            RestoreAiChoice();
            if (!AnyCloudKey()) AiConfigPanel.Visibility = Visibility.Visible; // no key yet → show setup
            // the settings scroll inside a capped height so the Build row, chat input and log stay visible
            AiPanel.SizeChanged += (s, e) => AiConfigScroll.MaxHeight = Math.Max(160, AiPanel.ActualHeight - 300);
            _ = RefreshOllamaStatusAsync();
            L.Changed += OnLanguageChanged; // the XAML texts re-translate themselves; the code-built panels are rebuilt
            Closed += (s, e) => L.Changed -= OnLanguageChanged;
        }

        /// <summary>The editor's language changed: rebuild every panel the code builds (their texts go through L.T).
        /// The person's own content (names, the AI chat) is left as it is.</summary>
        private void OnLanguageChanged()
        {
            GridToggle.Content = _snap ? L.T("Grid: on") : L.T("Grid: off");
            StatusText.Text = L.T("Ready"); // the last message was in the old language
            RefreshPalette();
            RefreshLayersKeepActive(); // re-selecting the layer rebuilds the objects list and its header too
            var offset = InspectorScroll.VerticalOffset;
            BuildInspector(_selected);
            InspectorScroll.ScrollToVerticalOffset(offset);
            SetAiMode(_aiMode);
            UpdateAiTarget();
            BuildCloudKeyRows();
            AdvancedToggleText.Text = (AdvancedPanel.Visibility == Visibility.Visible ? "▾ " : "▸ ") + L.T("Advanced — paid keys, offline AI");
            foreach (var pane in _panes.Values) if (pane.FloatWin != null) pane.FloatWin.Title = PaneTitle(pane.Key);
            _ = RefreshOllamaStatusAsync();
        }

        // Re-select the AI the user picked last time (by its Tag); falls back to Cloud (Auto).
        private void RestoreAiChoice()
        {
            if (!string.IsNullOrEmpty(AiSettings.LocalModel)) CustomModel.Text = AiSettings.LocalModel;
            var saved = AiSettings.AiChoice;
            var match = saved == null ? null
                : StrengthCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)(i.Tag ?? ""), saved, StringComparison.Ordinal));
            if (match != null) StrengthCombo.SelectedItem = match;
            else StrengthCombo.SelectedIndex = 0; // default = Cloud (Auto)
        }

        // Mouse wheel scrolls the AI settings even while the pointer is over a key box or the combo
        // (those controls swallow the wheel otherwise). An open dropdown keeps scrolling itself.
        private void AiConfigScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Mouse.Captured is ComboBox) return;
            AiConfigScroll.ScrollToVerticalOffset(AiConfigScroll.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;
        }

        // ============================================================
        //  DOCKING (movable / floatable panels — custom, no dependency)
        // ============================================================
        private enum DockRegion { Left, Right, Bottom, Float }

        private class DockPane
        {
            public string Key = "";
            public Border Card = null!;
            public DockRegion Region;
            public DockRegion LastDocked = DockRegion.Right;
            public Window? FloatWin;
        }

        private readonly Dictionary<string, DockPane> _panes = new();
        private readonly List<string> _dockOrder = new();
        private const double LeftW = 200, RightW = 360, BottomH = 190;

        private void InitDocking()
        {
            Register("prefabs", CardPrefabs, DockRegion.Left);
            Register("ai", CardAi, DockRegion.Right);
            Register("layers", CardLayers, DockRegion.Bottom);
            Register("objects", CardObjects, DockRegion.Bottom);

            // apply saved layout (if any)
            foreach (var kv in AiSettings.DockLayout)
                if (_panes.TryGetValue(kv.Key, out var p) && Enum.TryParse<DockRegion>(kv.Value, out var r) && r != DockRegion.Float)
                { p.Region = r; p.LastDocked = r; }

            RebuildRegion(DockRegion.Left);
            RebuildRegion(DockRegion.Right);
            RebuildRegion(DockRegion.Bottom);
        }

        private void Register(string key, Border card, DockRegion def)
        {
            var pane = new DockPane { Key = key, Card = card, Region = def, LastDocked = def };
            _panes[key] = pane;
            _dockOrder.Add(key);
        }

        private Grid HostFor(DockRegion r) => r switch
        {
            DockRegion.Left => DockLeftHost,
            DockRegion.Right => DockRightHost,
            _ => DockBottomHost
        };

        private static void Detach(FrameworkElement el)
        {
            switch (el.Parent)
            {
                case Panel p: p.Children.Remove(el); break;
                case ContentControl cc: cc.Content = null; break;
                case System.Windows.Controls.Decorator dec: dec.Child = null; break;
            }
            if (el.Parent is Window w) w.Content = null;
            if (el.LayoutTransform is ScaleTransform) el.LayoutTransform = Transform.Identity; // its floating window's interface size
        }

        private void SetRegionVisible(DockRegion region, bool vis)
        {
            switch (region)
            {
                case DockRegion.Left:
                    DockLeftCol.Width = vis ? new GridLength(LeftW) : new GridLength(0);
                    LeftSplitter.Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case DockRegion.Right:
                    DockRightCol.Width = vis ? new GridLength(RightW) : new GridLength(0);
                    RightSplitter.Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case DockRegion.Bottom:
                    DockBottomRow.Height = vis ? new GridLength(BottomH) : new GridLength(0);
                    BottomSplitter.Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
                    break;
            }
        }

        private void RebuildRegion(DockRegion region)
        {
            if (region == DockRegion.Float) return;
            var host = HostFor(region);
            host.Children.Clear();
            host.RowDefinitions.Clear();
            host.ColumnDefinitions.Clear();

            var panes = _dockOrder.Select(k => _panes[k]).Where(p => p.Region == region).ToList();
            SetRegionVisible(region, panes.Count > 0);
            if (panes.Count == 0) return;

            bool vertical = region != DockRegion.Bottom;
            int cell = 0;
            for (int i = 0; i < panes.Count; i++)
            {
                if (i > 0)
                {
                    if (vertical) host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
                    else host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
                    var sp = new GridSplitter { Background = (Brush)FindResource("Border"), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
                    if (vertical) { sp.Height = 5; Grid.SetRow(sp, cell); } else { sp.Width = 5; Grid.SetColumn(sp, cell); }
                    host.Children.Add(sp);
                    cell++;
                }

                if (vertical) host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 60 });
                else host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });

                var card = panes[i].Card;
                Detach(card);
                if (vertical) Grid.SetRow(card, cell); else Grid.SetColumn(card, cell);
                host.Children.Add(card);
                cell++;
            }
        }

        private void PlacePane(DockPane pane, DockRegion target)
        {
            var prev = pane.Region;
            if (pane.FloatWin != null)
            {
                var w = pane.FloatWin;
                pane.FloatWin = null;
                Detach(pane.Card);
                w.Close();
            }
            else Detach(pane.Card);

            pane.Region = target;

            if (target == DockRegion.Float)
            {
                var win = new Window
                {
                    Title = PaneTitle(pane.Key),
                    Width = 380,
                    Height = 500,
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = (Brush)FindResource("Bg0"),
                    Content = pane.Card
                };
                win.Closed += (s, e) =>
                {
                    if (pane.FloatWin != null && !_closing) // closed by the user (X) → dock back (not when the editor itself closes: keep the layout)
                    {
                        pane.FloatWin = null;
                        Detach(pane.Card);
                        pane.Region = pane.LastDocked;
                        RebuildRegion(pane.Region);
                        SaveDockLayout();
                    }
                };
                pane.FloatWin = win;
                win.Show();
            }
            else
            {
                pane.LastDocked = target;
            }

            if (prev != DockRegion.Float) RebuildRegion(prev);
            if (target != DockRegion.Float) RebuildRegion(target);
            SaveDockLayout();
        }

        private void DockMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string key) return;
            if (!_panes.TryGetValue(key, out var pane)) return;

            var menu = new ContextMenu();
            void Add(string text, DockRegion target)
            {
                var mi = new MenuItem { Header = text };
                mi.Click += (_, __) => PlacePane(pane, target);
                menu.Items.Add(mi);
            }
            if (pane.Region != DockRegion.Left) Add(L.T("Dock left"), DockRegion.Left);
            if (pane.Region != DockRegion.Right) Add(L.T("Dock right"), DockRegion.Right);
            if (pane.Region != DockRegion.Bottom) Add(L.T("Dock bottom"), DockRegion.Bottom);
            menu.Items.Add(new Separator());
            if (pane.Region == DockRegion.Float) Add(L.T("Dock back"), pane.LastDocked);
            else Add(L.T("Float (separate window)"), DockRegion.Float);
            menu.PlacementTarget = fe;
            menu.IsOpen = true;
        }

        // Toolbar "Panels ▾": re-show a panel that was closed/floated, or reset layout.
        private void PanelsMenu_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            foreach (var key in _dockOrder)
            {
                var pane = _panes[key];
                var mi = new MenuItem { Header = PanelsMenuHeader(pane) };
                mi.Click += (_, __) => { if (pane.Region == DockRegion.Float && pane.FloatWin != null) pane.FloatWin.Activate(); else PlacePane(pane, pane.Region); };
                menu.Items.Add(mi);
            }
            menu.Items.Add(new Separator());
            var reset = new MenuItem { Header = L.T("Reset layout to default") };
            reset.Click += (_, __) => ResetDockLayout();
            menu.Items.Add(reset);
            menu.PlacementTarget = (UIElement)sender;
            menu.IsOpen = true;
        }

        private static string PanelsMenuHeader(DockPane pane) => $"{PaneTitle(pane.Key)}  ({RegionLabel(pane.Region)})";

        // the panel names shown in the Panels menu and on floating windows (in the current language)
        private static string PaneTitle(string key) => key switch
        {
            "prefabs" => L.T("Prefabs"),
            "ai" => L.T("AI / Inspector"),
            "layers" => L.T("Layers"),
            "objects" => L.T("Objects"),
            _ => key,
        };

        private static string RegionLabel(DockRegion r) => r switch
        {
            DockRegion.Left => L.T("Left"),
            DockRegion.Right => L.T("Right"),
            DockRegion.Bottom => L.T("Bottom"),
            _ => L.T("Floating"),
        };

        private void ResetDockLayout()
        {
            var defaults = new Dictionary<string, DockRegion>
            {
                ["prefabs"] = DockRegion.Left,
                ["ai"] = DockRegion.Right,
                ["layers"] = DockRegion.Bottom,
                ["objects"] = DockRegion.Bottom
            };
            foreach (var kv in defaults)
            {
                var pane = _panes[kv.Key];
                if (pane.FloatWin != null) { var w = pane.FloatWin; pane.FloatWin = null; Detach(pane.Card); w.Close(); }
                pane.Region = kv.Value;
                pane.LastDocked = kv.Value;
            }
            RebuildRegion(DockRegion.Left);
            RebuildRegion(DockRegion.Right);
            RebuildRegion(DockRegion.Bottom);
            SaveDockLayout();
        }

        private void SaveDockLayout()
        {
            AiSettings.DockLayout = _panes.ToDictionary(
                kv => kv.Key,
                kv => (kv.Value.Region == DockRegion.Float ? kv.Value.LastDocked : kv.Value.Region).ToString());
            AiSettings.Save();
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
            if (_undo.Count == 0) { Status(L.T("Nothing to undo")); return; }
            _redo.Push(SceneJson());
            ReplaceActiveScene(FromJson(_undo.Pop()));
            Status(L.T("Undo"));
        }

        private void Redo()
        {
            if (_redo.Count == 0) { Status(L.T("Nothing to redo")); return; }
            _undo.Push(SceneJson());
            ReplaceActiveScene(FromJson(_redo.Pop()));
            Status(L.T("Redo"));
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
            UpdateTitle();
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
            var name = InputDialog.Prompt(this, L.T("New scene"), L.T("Scene name:"), L.F("Scene {0}", _project.Scenes.Count + 1));
            if (name == null) return;
            if (_project.Scenes.Any(s => s.Name == name)) { MessageBox.Show(L.T("A scene with that name already exists.")); return; }
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
            var name = InputDialog.Prompt(this, L.T("Rename scene"), L.T("New name:"), _scene.Name);
            if (name == null) return;
            bool wasStart = _project.StartScene == _scene.Name;
            _scene.Name = name;
            if (wasStart) _project.StartScene = name;
            RefreshSceneCombo();
            SelectSceneInCombo(_scene);
            Dirty();
        }

        private void DeleteScene_Click(object sender, RoutedEventArgs e)
        {
            if (_project.Scenes.Count <= 1) { MessageBox.Show(L.T("A project needs at least one scene.")); return; }
            if (MessageBox.Show(L.F("Delete scene \"{0}\"?", _scene.Name), L.T("Delete scene"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
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
            Status(L.F("\"{0}\" is now the start scene", _scene.Name));
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
            SceneFrame.Background = Theme.Hex(_scene.BackgroundColor, "#1A1A20");

            if (_snap) DrawGrid();

            foreach (var layer in _scene.Layers)
            {
                if (!layer.Visible) continue;
                foreach (var obj in ZOrder.InDrawOrder(layer.Objects)) // Z orders objects inside their layer
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
            DrawPathOverlay();
        }

        private void AdvancedPatrolRow(GameObjectData obj, BehaviorData bh, Panel panel)
        {
            bool drawing = ReferenceEquals(_pathDrawBh, bh);
            int count = ParseEditorPath(bh.Text("path", "")).Count;

            panel.Children.Add(new TextBlock
            {
                Text = drawing
                    ? L.T("Drawing: click points on the scene to trace the route (green dot = start). The grid is on so points snap neatly. Click Finish when done.")
                    : (count > 0 ? L.F("Route: {0} point(s). Tip: the grid turns on while drawing so clicks snap to it.", count)
                                 : L.T("No route yet. Click 'Draw path' (the grid turns on automatically), then click points on the scene to trace where it should go.")),
                Foreground = (SolidColorBrush)FindResource("TextDim"),
                FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6)
            });

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var draw = new Button { Content = drawing ? L.T("✓ Finish") : L.T("✎ Draw path"), Height = 28, FontSize = 11, Padding = new Thickness(10, 0, 10, 0), Style = (Style)FindResource("CardButton") };
            draw.Click += (s, e) =>
            {
                if (ReferenceEquals(_pathDrawBh, bh)) { _pathDrawBh = null; _pathDrawObj = null; Status(L.T("Path saved.")); }
                else
                {
                    PushUndo();
                    _pathDrawBh = bh; _pathDrawObj = obj;
                    if (!_snap) { _snap = true; GridToggle.Content = L.T("Grid: on"); } // grid on so points snap
                    Status(L.T("Draw mode: click points on the scene, then click Finish."));
                }
                BuildInspector(obj); RenderScene();
            };
            var clear = new Button { Content = L.T("Clear"), Height = 28, FontSize = 11, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(6, 0, 0, 0), Style = (Style)FindResource("GhostButton") };
            clear.Click += (s, e) => { PushUndo(); bh.Texts["path"] = ""; Dirty(); BuildInspector(obj); RenderScene(); };
            row.Children.Add(draw); row.Children.Add(clear);
            panel.Children.Add(row);
        }

        // ---- AdvancedPatrol path (editor) ----
        private static List<(double x, double y)> ParseEditorPath(string json) => PathPoints.Parse(json);

        private void SetEditorPath(BehaviorData bh, List<(double x, double y)> pts)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append($"{{\"x\":{pts[i].x:0.##},\"y\":{pts[i].y:0.##}}}");
            }
            sb.Append(']');
            bh.Texts["path"] = sb.ToString();
        }

        private void DrawPathOverlay()
        {
            var obj = _pathDrawObj ?? _selected;
            var bh = obj?.Behaviors.FirstOrDefault(b => b.Kind == BehaviorKind.AdvancedPatrol);
            if (bh == null) return;
            var pts = ParseEditorPath(bh.Text("path", ""));
            if (pts.Count == 0) return;

            var accent = (SolidColorBrush)FindResource("Accent");
            if (pts.Count >= 2)
            {
                var poly = new System.Windows.Shapes.Polyline { Stroke = accent, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false };
                foreach (var (x, y) in pts) poly.Points.Add(new Point(x, y));
                SceneCanvas.Children.Add(poly);
            }
            for (int i = 0; i < pts.Count; i++)
            {
                var dot = new System.Windows.Shapes.Ellipse { Width = 11, Height = 11, Fill = i == 0 ? Brushes.LimeGreen : accent, Stroke = Brushes.White, StrokeThickness = 1, IsHitTestVisible = false };
                Canvas.SetLeft(dot, pts[i].x - 5.5); Canvas.SetTop(dot, pts[i].y - 5.5);
                SceneCanvas.Children.Add(dot);
            }
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
            double corner = obj.EffectiveCorner();
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
                    AddHitboxOutline(obj, border); // "Hitbox: fit to the drawing" → dashed outline (MainWindow.Hitbox.cs)
                }
                catch { border.Background = Theme.Hex(obj.Color, "#9A9AA0"); }
            }
            else border.Background = Theme.Hex(obj.Color, "#9A9AA0");

            Canvas.SetLeft(border, obj.X);
            Canvas.SetTop(border, obj.Y);
            if (Math.Abs(obj.Rotation) > 0.01)
            {
                border.RenderTransformOrigin = new Point(0.5, 0.5);
                border.RenderTransform = new RotateTransform(obj.Rotation);
            }

            border.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2) { _dragObj = null; SceneCanvas.ReleaseMouseCapture(); OpenInspector(obj); e.Handled = true; return; } // double-click: its inspector
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { ToggleSelect(obj); FocusScene(); e.Handled = true; return; }
                Select(obj);
                FocusScene(); // the arrow keys nudge the selection (MainWindow.LockZ.cs)
                if (obj.Locked) { e.Handled = true; return; } // locked: selected so it can be inspected, never dragged
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
            if (_pathDrawBh != null)
            {
                var p = e.GetPosition(SceneCanvas);
                if (_snap) { p.X = Math.Round(p.X / GridSize) * GridSize; p.Y = Math.Round(p.Y / GridSize) * GridSize; }
                var pts = ParseEditorPath(_pathDrawBh.Text("path", ""));
                pts.Add((p.X, p.Y));
                SetEditorPath(_pathDrawBh, pts); Dirty();
                RenderScene();
                if (_pathDrawObj != null) BuildInspector(_pathDrawObj);
                e.Handled = true;
                return;
            }
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
            if (_selected.Locked) return; // locked objects can't be resized
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

        private void ApplyResize(Point p, bool? shift = null)
        {
            if (_selected == null || _selected.Locked) return;
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

            // "Keep proportions" (or Shift held while dragging): the picture's aspect ratio (MainWindow.LockZ.cs)
            bool keep = _selected.KeepAspect || (shift ?? (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            if (keep && AspectOf(_selected, _resizeRect) is double ratio)
                (L, T, W, H) = AspectRect(_resizeRect, ratio, left, right, top, bottom, W, H, min);
            else if (_snap)
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
            GridToggle.Content = _snap ? L.T("Grid: on") : L.T("Grid: off");
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
            if (obj != null && !obj.Locked && obj.Behaviors.Any(b => b.Kind == BehaviorKind.Tilemap)) LoadTilemapEdit(obj); // a locked tilemap isn't painted
            else { _tmObj = null; _tmBh = null; }
            if (obj != null && !ReferenceEquals(obj, _selectedBefore)) Status(obj.Locked ? L.T("Locked — unlock to edit") : L.T("Arrows: move · Shift: 10 px"));
            _selectedBefore = obj;

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
                ? L.T("Target: auto-detect.")
                : _selection.Count == 1
                    ? L.F("Target: {0} (AI edits only this).", _selection[0].Name)
                    : L.F("Target: {0} (AI edits these {1}).", string.Join(", ", _selection.Select(o => o.Name)), _selection.Count);
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
                PaletteList.Children.Add(new TextBlock { Text = L.T("No prefabs match."), Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11, Margin = new Thickness(2, 4, 0, 0) });
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
            return L.Keep(btn); // the category is the person's name
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

            var add = L.Keep(new Button { Content = $"{pf.Icon}  {pf.Name}", Style = (Style)FindResource("CardButton"), Height = 36, HorizontalContentAlignment = HorizontalAlignment.Left });
            add.Click += (s, e) => AddPrefabInstance(pf);

            var ren = new Button { Content = "✏", Style = (Style)FindResource("GhostButton"), Height = 36, Width = 30, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0), ToolTip = L.T("Rename this prefab (everything that uses it follows the new name)") };
            ren.Click += (s, e) => RenamePrefab(pf);
            var del = new Button { Content = L.T("Delete"), Style = (Style)FindResource("GhostButton"), Height = 36, FontSize = 11, Foreground = (SolidColorBrush)FindResource("Danger"), Margin = new Thickness(4, 0, 0, 0) };
            del.Click += (s, e) => DeletePrefab(pf);

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(add, 0); Grid.SetColumn(ren, 1); Grid.SetColumn(del, 2);
            grid.Children.Add(add); grid.Children.Add(ren); grid.Children.Add(del);
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
            if (layer == null) { Status(L.T("Create a layer first")); return; }
            PushUndo();
            obj.Id = _project.NextId;
            obj.Name = UniqueName(baseName);
            obj.X = _scene.Width / 2 - obj.Width / 2;
            obj.Y = _scene.Height / 2 - obj.Height / 2;
            if (obj.Behaviors.Any(b => b.Kind == BehaviorKind.Tilemap)) { obj.X = 0; obj.Y = 0; } // tilemaps anchor at the scene origin
            layer.Objects.Add(obj);
            RenderScene(); RefreshObjects(); Select(obj); Dirty();
            Status(L.F("{0} added to \"{1}\"", obj.Name, layer.Name));
        }

        private void RenamePrefab(PrefabDef pf)
        {
            var name = InputDialog.Prompt(this, L.T("Rename prefab"), L.T("New name:"), pf.Name);
            if (name == null || name == pf.Name) return;
            if (PrefabRename.Problem(_project, pf, name) is string why) { MessageBox.Show(why, L.T("Rename prefab")); return; }
            // (no undo step: undo covers the scene, not the prefab library — it would bring back old references only)
            string old = pf.Name;
            int refs = PrefabRename.Apply(_project, pf, name);
            RefreshPalette(); if (_selected != null) BuildInspector(_selected); Dirty();
            Status(refs == 0 ? L.F("Prefab \"{0}\" is now \"{1}\"", old, pf.Name) : L.F("Prefab \"{0}\" is now \"{1}\" ({2} places that use it updated)", old, pf.Name, refs));
        }

        private void DeletePrefab(PrefabDef pf)
        {
            if (MessageBox.Show(L.F("Remove prefab \"{0}\" from the palette?\n(objects already placed in scenes are NOT affected)", pf.Name),
                L.T("Delete prefab"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _project.Prefabs.Remove(pf);
            RefreshPalette(); Dirty();
            Status(L.F("Prefab \"{0}\" removed", pf.Name));
        }

        private void SavePrefab_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) { Status(L.T("Select an object to save as a prefab")); return; }
            SaveAsPrefab(_selected);
        }

        private void SaveAsPrefab(GameObjectData obj)
        {
            var name = InputDialog.Prompt(this, L.T("Save as prefab"), L.T("Prefab name:"), obj.Name);
            if (name == null) return;
            var existingCats = _project.Prefabs.Select(p => p.Category).Distinct().Where(c => !string.IsNullOrWhiteSpace(c));
            var prompt = existingCats.Any()
                ? L.F("Category — groups it in the palette (existing: {0}):", string.Join(", ", existingCats))
                : L.T("Category — groups it in the palette:");
            var cat = InputDialog.Prompt(this, L.T("Category"), prompt, string.IsNullOrWhiteSpace(obj.Group) ? "General" : obj.Group);
            if (string.IsNullOrWhiteSpace(cat)) cat = "General";
            var icon = _project.Prefabs.FirstOrDefault(p => p.Name == obj.Type)?.Icon ?? "▢";
            var tmpl = JsonClone(obj);
            tmpl.Id = 0; tmpl.X = 0; tmpl.Y = 0; tmpl.Locked = false; // keeps its Z; new copies start unlocked
            _project.Prefabs.Add(new PrefabDef { Name = name, Icon = icon, Category = cat, Template = tmpl });
            RefreshPalette(); Dirty();
            Status(L.F("Saved \"{0}\" as a prefab under \"{1}\"", name, cat));
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
            var gone = _selection.Where(o => !o.Locked).ToList(); // locked objects are never deleted
            if (gone.Count == 0) { Status(L.T("Locked — unlock to edit")); return; }
            PushUndo();
            foreach (var o in gone)
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
            clone.Locked = false; // the copy is there to be placed
            layer.Objects.Add(clone);
            RenderScene(); RefreshObjects(); Select(clone); Dirty();
            Status(L.F("Duplicated {0}", name));
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (Keyboard.FocusedElement is TextBox) return;

            if (e.Key == Key.Delete && _selected != null) { DeleteSelected(); e.Handled = true; }
            else if (NudgeKey(e)) e.Handled = true; // arrows move the selection (MainWindow.LockZ.cs)
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

        private void RefreshLayersKeepActive()
        {
            var active = _activeLayer;
            RefreshLayers();
            int i = active != null ? _scene.Layers.IndexOf(active) : -1;
            LayersList.SelectedIndex = i >= 0 ? i : IndexOfBaseLayer();
        }

        private ListBoxItem MakeLayerItem(Layer layer)
        {
            var eye = new Button
            {
                Content = layer.Visible ? L.T("shown") : L.T("hidden"), Width = 50, Height = 22, FontSize = 10,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = (SolidColorBrush)FindResource(layer.Visible ? "Text" : "TextDim"), Cursor = Cursors.Hand, Tag = layer
            };
            eye.Click += (s, e) => { layer.Visible = !layer.Visible; eye.Content = layer.Visible ? L.T("shown") : L.T("hidden"); eye.Foreground = (SolidColorBrush)FindResource(layer.Visible ? "Text" : "TextDim"); RenderScene(); Dirty(); e.Handled = true; };

            var name = L.Keep(new TextBlock { Text = layer.Name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            var kind = new TextBlock { Text = LayerKindLabel(layer.Kind), Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };

            bool Fixed() => layer.ScreenSpace || layer.Kind == LayerKind.UI;
            var fix = new Button { Content = Fixed() ? L.T("fixed") : L.T("scroll"), MinWidth = 46, Padding = new Thickness(4, 0, 4, 0), Height = 22, FontSize = 10, Style = (Style)FindResource("GhostButton"), VerticalAlignment = VerticalAlignment.Center, ToolTip = L.T("Fixed = stays on screen (UI/HUD). Scroll = moves with the world.") };
            fix.Click += (s, e) => { e.Handled = true; PushUndo(); layer.ScreenSpace = !Fixed(); fix.Content = Fixed() ? L.T("fixed") : L.T("scroll"); Dirty(); };

            var del = new Button { Content = L.T("Delete"), Height = 22, FontSize = 10, Style = (Style)FindResource("GhostButton"), Foreground = (SolidColorBrush)FindResource("Danger"), VerticalAlignment = VerticalAlignment.Center };
            del.Click += (s, e) => { e.Handled = true; DeleteLayer(layer); };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(eye, 0); Grid.SetColumn(name, 1); Grid.SetColumn(kind, 2); Grid.SetColumn(fix, 3); Grid.SetColumn(del, 4);
            grid.Children.Add(eye); grid.Children.Add(name); grid.Children.Add(kind); grid.Children.Add(fix); grid.Children.Add(del);

            return new ListBoxItem { Content = grid, Tag = layer, Padding = new Thickness(4, 4, 4, 4), Foreground = (SolidColorBrush)FindResource("Text"), HorizontalContentAlignment = HorizontalAlignment.Stretch }; // stretch: the kind sits at the right, not glued to the name
        }

        private static string LayerKindLabel(LayerKind kind) => kind switch
        {
            LayerKind.Background => L.T("Background"),
            LayerKind.Base => L.T("Base"),
            LayerKind.UI => L.T("UI"),
            LayerKind.Sky => L.T("Sky"),
            _ => L.T("Custom"),
        };

        private void DeleteLayer(Layer layer)
        {
            if (_scene.Layers.Count <= 1) { MessageBox.Show(L.T("A scene needs at least one layer.")); return; }
            if (layer.Objects.Count > 0 &&
                MessageBox.Show(L.F("Delete layer \"{0}\" and its {1} object(s)?", layer.Name, layer.Objects.Count), L.T("Delete layer"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
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
                ObjectsHeader.Text = L.F("OBJECTS — {0}", layer.Name).ToUpper();
                RefreshObjects();
            }
        }

        private void AddLayer_Click(object sender, RoutedEventArgs e)
        {
            var name = InputDialog.Prompt(this, L.T("New layer"), L.T("Layer name:"), L.F("Layer {0}", _scene.Layers.Count + 1));
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
                        Content = (g.Key.Length == 0 ? L.T("Ungrouped") : g.Key).ToUpperInvariant(),
                        IsHitTestVisible = false, Focusable = false,
                        Foreground = (SolidColorBrush)FindResource("TextDim"),
                        FontSize = 10, FontWeight = FontWeights.SemiBold, Padding = new Thickness(6, 6, 6, 2)
                    });
                foreach (var obj in g)
                    ObjectsList.Items.Add(new ListBoxItem { Content = ObjectRowContent(obj), Tag = obj, Padding = new Thickness(anyGroup ? 18 : 8, 5, 8, 5), Foreground = (SolidColorBrush)FindResource("Text") });
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
            // leaving the object we were drawing a path for → exit draw mode
            if (_pathDrawObj != null && !ReferenceEquals(obj, _pathDrawObj)) { _pathDrawBh = null; _pathDrawObj = null; }

            InspectorRoot.Children.Clear();
            if (obj == null) { BuildSceneInspector(); return; }

            Section(L.T("OBJECT"));
            LockRow(obj); // 🔒/🔓 — the only thing left editable on a locked object (MainWindow.LockZ.cs)
            TextRow(L.T("Name"), obj.Name, v => { obj.Name = v; RefreshObjects(); Dirty(); });
            InfoRow(L.T("Type"), L.T(obj.Type));
            TextRow(L.T("Group"), obj.Group, v => { obj.Group = v.Trim(); RefreshObjects(); Dirty(); });
            var rowBtns = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) }; // wraps when the words are long (German, French…)
            var dup = new Button { Content = L.T("Duplicate"), Style = (Style)FindResource("CardButton"), Height = 30, Padding = new Thickness(10, 0, 10, 0), HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 8, 6) };
            dup.Click += (s, e) => DuplicateSelected();
            var del = new Button { Content = L.T("Delete"), Style = (Style)FindResource("CardButton"), Height = 30, Padding = new Thickness(10, 0, 10, 0), HorizontalContentAlignment = HorizontalAlignment.Center, Foreground = (SolidColorBrush)FindResource("Danger"), Margin = new Thickness(0, 0, 8, 6) };
            del.Click += (s, e) => DeleteSelected();
            var toPrefab = new Button { Content = L.T("Save as Prefab"), Style = (Style)FindResource("CardButton"), Height = 30, Padding = new Thickness(10, 0, 10, 0), HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
            toPrefab.Click += (s, e) => SaveAsPrefab(obj);
            rowBtns.Children.Add(dup); rowBtns.Children.Add(del); rowBtns.Children.Add(toPrefab);
            InspectorRoot.Children.Add(rowBtns);

            Section(L.T("TRANSFORM"));
            NumRow(L.T("X"), obj.X, v => { obj.X = v; ReposSelected(); Dirty(); });
            NumRow(L.T("Y"), obj.Y, v => { obj.Y = v; ReposSelected(); Dirty(); });
            NumRow(L.T("Width"), obj.Width, v => SetSize(obj, v, null));   // keeps proportions when asked (MainWindow.LockZ.cs)
            NumRow(L.T("Height"), obj.Height, v => SetSize(obj, null, v));
            SizeRow(obj);  // Keep proportions + Original size
            NumRow(L.T("Rotation"), obj.Rotation, v => { obj.Rotation = v; RenderScene(); Dirty(); });
            ZRow(obj);     // Z layer: − n + · To front · To back

            Section(L.T("APPEARANCE"));
            // a tilemap looks like its painted tiles (each tile's colour / picture is set in its card): only Opacity applies
            bool tilemap = obj.Behaviors.Any(b => b.Kind == BehaviorKind.Tilemap);
            if (!tilemap)
            {
                LookCopyRow(obj);
                ColorRow(L.T("Color"), obj.Color, v => { obj.Color = v; RenderScene(); Dirty(); });
                AnimationsRow(obj);
                SpriteRow(obj);
                if (obj.SpritePath != null || obj.Animations.Count > 0)
                    ComboRow(L.T("Image fit"), new[] { L.T("Stretch"), L.T("Tile (repeat)") }, obj.TileImage ? 1 : 0, i => { obj.TileImage = i == 1; RenderScene(); Dirty(); });
            }
            SliderRow(L.T("Opacity"), obj.Opacity, 0, 1, v => { obj.Opacity = Math.Clamp(v, 0, 1); RenderScene(); Dirty(); }, percent: true);
            if (!tilemap)
            {
                NumRow(L.T("Corner radius"), obj.EffectiveCorner(), v => { obj.CornerRadius = Math.Max(0, v); RenderScene(); Dirty(); });
                ColorRow(L.T("Border color"), obj.BorderColor ?? "#000000", v => { obj.BorderColor = v; Dirty(); });
                NumRow(L.T("Border width"), obj.BorderWidth, v => { obj.BorderWidth = Math.Max(0, v); Dirty(); });
            }
            else InspectorRoot.Children.Add(new TextBlock { Text = L.T("Each tile's colour, picture or animation is set in the Tilemap card below."), Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            if (obj.Behaviors.Any(b => b.Kind == BehaviorKind.Label))
            {
                TextRow(L.T("Font"), obj.FontFamily ?? "", v => { obj.FontFamily = string.IsNullOrWhiteSpace(v) ? null : v; Dirty(); });
                ComboRow(L.T("Bold"), new[] { L.T("No"), L.T("Yes") }, obj.Bold ? 1 : 0, i => { obj.Bold = i == 1; Dirty(); });
            }

            Section(L.T("PHYSICS"));
            ComboRow(L.T("Mode"), new[] { L.T("None"), L.T("Static"), L.T("Dynamic") }, (int)obj.Physics.Mode, i => { obj.Physics.Mode = (PhysicsMode)i; Dirty(); });
            NumRow(L.T("Gravity ×"), obj.Physics.GravityScale, v => { obj.Physics.GravityScale = v; Dirty(); });
            NumRow(L.T("Bounciness"), obj.Physics.Bounciness, v => { obj.Physics.Bounciness = Math.Clamp(v, 0, 1); Dirty(); });
            HitboxRows(obj); // MainWindow.Hitbox.cs

            Section(L.T("BEHAVIORS"));
            foreach (var bh in obj.Behaviors.ToList())
                BehaviorCard(obj, bh);
            if (_selection.Count > 1)
                InspectorRoot.Children.Add(new TextBlock
                {
                    Text = L.F("＋ adds to all {0} selected objects.", _selection.Count),
                    Foreground = (SolidColorBrush)FindResource("Accent"),
                    FontSize = 11, Margin = new Thickness(0, 4, 0, 2), TextWrapping = TextWrapping.Wrap
                });
            AddBehaviorControl(obj);
            ApplyInspectorLock(obj); // a locked object: everything but the lock is read-only (MainWindow.LockZ.cs)
        }

        private void BuildSceneInspector()
        {
            Section(L.T("SCENE"));
            TextRow(L.T("Name"), _scene.Name, v => { _scene.Name = v; RefreshSceneCombo(); SelectSceneInCombo(_scene); Dirty(); });
            NumRow(L.T("Width"), _scene.Width, v => { _scene.Width = v; SceneFrame.Width = v; Dirty(); });
            NumRow(L.T("Height"), _scene.Height, v => { _scene.Height = v; SceneFrame.Height = v; Dirty(); });
            ColorRow(L.T("Background"), _scene.BackgroundColor, v => { _scene.BackgroundColor = v; RenderScene(); Dirty(); });

            // the whole game's screen size (Project.ScreenWidth/Height): what the camera shows at once, scaled to the window
            Section(L.T("GAME SCREEN"));
            string Key(double w, double h) => $"{w:0}x{h:0}";
            var sizes = new List<(string value, string label)> { ("640x360", "640 × 360"), ("960x540", "960 × 540"), ("1280x720", "1280 × 720 (HD)"), ("1920x1080", "1920 × 1080 (Full HD)") };
            string cur = Key(_project.ScreenWidth, _project.ScreenHeight);
            if (!sizes.Any(s => s.value == cur)) sizes.Add((cur, $"{_project.ScreenWidth:0} × {_project.ScreenHeight:0}"));
            sizes.Add(("scene", L.T("Same as this scene")));
            PickRow(L.T("Screen size"), InspectorRoot, sizes, cur, false, null, v =>
            {
                if (v == "scene") { _project.ScreenWidth = _scene.Width; _project.ScreenHeight = _scene.Height; }
                else { var p = v.Split('x'); _project.ScreenWidth = double.Parse(p[0]); _project.ScreenHeight = double.Parse(p[1]); }
                Dirty(); RefreshAfterChoiceAlways(null!); // the scene inspector (nothing selected)
            });
            InspectorRoot.Children.Add(new TextBlock
            {
                Text = L.F("The game shows {0} × {1} of the scene at a time, scaled to fit the window (the same in tests and exported games).", _project.ScreenWidth, _project.ScreenHeight),
                TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = (SolidColorBrush)FindResource("TextDim"), Margin = new Thickness(0, 0, 0, 8)
            });

            Section(L.T("LIGHT"));
            NumRow(L.T("Ambient light (0-1)"), _scene.AmbientLight, v => { _scene.AmbientLight = Math.Clamp(v, 0, 1); Dirty(); });

            InspectorRoot.Children.Add(new TextBlock
            {
                Text = L.T("Select an object to edit it, or use the AI tab."),
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
            var grid = RowGrid(L.T("Animations"));
            var inner = new StackPanel();
            var open = new Button { Content = L.T("🎞 Edit Animations…"), Style = (Style)FindResource("CardButton"), Height = 32, HorizontalContentAlignment = HorizontalAlignment.Center };
            open.Click += (s, e) =>
            {
                PushUndo();
                var win = new AnimationWindow(_project, obj) { Owner = this };
                win.ShowDialog();
                RenderScene(); BuildInspector(obj); Dirty();
            };
            inner.Children.Add(open);
            string summary = obj.Animations.Count == 0 ? L.T("No animations yet") : L.F("{0} animation(s) · default: {1}", obj.Animations.Count, obj.DefaultAnimation ?? obj.Animations.First().Name);
            inner.Children.Add(new TextBlock { Text = summary, Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(inner, 1);
            grid.Children.Add(inner);
        }

        private void SpriteRow(GameObjectData obj)
        {
            var grid = RowGrid(L.T("Static image"));
            var inner = new StackPanel { Orientation = Orientation.Horizontal };
            var pick = new Button { Content = obj.SpritePath == null ? L.T("Choose…") : Path.GetFileName(obj.SpritePath), Style = (Style)FindResource("CardButton"), Height = 28, Padding = new Thickness(8, 0, 8, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
            if (obj.SpritePath != null) L.SetSkip(pick, true); // the file name
            pick.Click += (s, e) => PickSprite(obj);
            inner.Children.Add(pick);
            if (obj.SpritePath != null)
            {
                // ✎ edit it in the sprite editor (saved as a new file: the original stays) · ⧉ a copy of the file for this object
                var edit = new Button { Content = "✎", Style = (Style)FindResource("GhostButton"), Height = 28, Width = 28, Margin = new Thickness(4, 0, 0, 0), ToolTip = L.T("Edit this image in the sprite editor (the original file stays as it is)") };
                edit.Click += (s, e) =>
                {
                    var win = new SpriteEditorWindow(_project, 32, ProjectIO.AbsoluteAsset(_project, obj.SpritePath)) { Owner = this };
                    if (win.ShowDialog() != true || win.SavedRelativePath == null) return;
                    PushUndo(); obj.SpritePath = win.SavedRelativePath; RenderScene(); BuildInspector(obj); Dirty();
                };
                var dup = new Button { Content = "⧉", Style = (Style)FindResource("GhostButton"), Height = 28, Width = 28, Margin = new Thickness(2, 0, 0, 0), ToolTip = L.T("Duplicate this image: this object uses a copy of the file, so you can change it without touching the original") };
                dup.Click += (s, e) =>
                {
                    try { var copy = ProjectIO.DuplicateAsset(_project, obj.SpritePath); PushUndo(); obj.SpritePath = copy; RenderScene(); BuildInspector(obj); Dirty(); Status(L.F("Image copied to {0}", Path.GetFileName(copy))); }
                    catch (Exception ex) { MessageBox.Show(L.F("Error: {0}", ex.Message)); }
                };
                inner.Children.Add(edit); inner.Children.Add(dup);
                var clear = new Button { Content = "✕", Style = (Style)FindResource("GhostButton"), Height = 28, Width = 28, Margin = new Thickness(4, 0, 0, 0), Foreground = (SolidColorBrush)FindResource("Danger") };
                clear.Click += (s, e) => { PushUndo(); obj.SpritePath = null; RenderScene(); BuildInspector(obj); Dirty(); };
                inner.Children.Add(clear);
            }
            Grid.SetColumn(inner, 1);
            grid.Children.Add(inner);
        }

        // "Copy look" / "Paste look": an object's whole appearance — sprite, animations, colour, opacity, corners, border —
        // onto another object (any scene; kept for the session).
        private static GameObjectData? _lookClip;

        private void LookCopyRow(GameObjectData obj)
        {
            var row = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 8) };
            var copy = new Button { Content = L.T("📋 Copy look"), Style = (Style)FindResource("CardButton"), Height = 28, Margin = new Thickness(0, 0, 4, 0), HorizontalContentAlignment = HorizontalAlignment.Center,
                                    ToolTip = L.T("Copy this object's sprite, animations, colour and the rest of its look") };
            copy.Click += (s, e) => { _lookClip = CopyLook(obj, new GameObjectData()); BuildInspector(obj); Status(L.F("Look of {0} copied", obj.Name)); };
            var paste = new Button { Content = L.T("📥 Paste look"), Style = (Style)FindResource("CardButton"), Height = 28, Margin = new Thickness(4, 0, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Center,
                                     IsEnabled = _lookClip != null, ToolTip = L.T("Give this object the look you copied (its size, behaviors and name stay)") };
            paste.Click += (s, e) =>
            {
                if (_lookClip == null) return;
                PushUndo(); CopyLook(_lookClip, obj); RenderScene(); BuildInspector(obj); Dirty();
                Status(L.F("Look pasted on {0}", obj.Name));
            };
            row.Children.Add(copy); row.Children.Add(paste);
            InspectorRoot.Children.Add(row);
        }

        /// <summary>Copies the appearance of <paramref name="from"/> onto <paramref name="to"/> (animations as new lists) and
        /// returns <paramref name="to"/>.</summary>
        public static GameObjectData CopyLook(GameObjectData from, GameObjectData to)
        {
            to.SpritePath = from.SpritePath; to.TileImage = from.TileImage; to.Color = from.Color; to.Opacity = from.Opacity;
            to.CornerRadius = from.CornerRadius; to.BorderColor = from.BorderColor; to.BorderWidth = from.BorderWidth;
            to.Animations = from.Animations.Select(a => new SpriteAnimation { Name = a.Name, Frames = new List<string>(a.Frames), Fps = a.Fps, Loop = a.Loop }).ToList();
            to.DefaultAnimation = from.DefaultAnimation;
            return to;
        }

        private void PickSprite(GameObjectData obj)
        {
            var dlg = new OpenFileDialog { Title = L.T("Choose image"), Filter = L.T("Images") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
            if (dlg.ShowDialog() == true)
            {
                try { PushUndo(); obj.SpritePath = ProjectIO.ImportAsset(_project, dlg.FileName, "sprites"); RenderScene(); BuildInspector(obj); Dirty(); Status(L.F("Image set on {0}", obj.Name)); }
                catch (Exception ex) { MessageBox.Show(L.F("Error: {0}", ex.Message)); }
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
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new TextBlock { Text = $"{info.Icon}  {L.T(info.Name)}", FontWeight = FontWeights.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var help = new Button { Content = "?", Width = 24, Height = 24, FontSize = 11, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 0), Style = (Style)FindResource("CardButton"), HorizontalContentAlignment = HorizontalAlignment.Center,
                                    ToolTip = L.T("How does this work? (asks the AI in Learn mode)") };
            help.Click += (s, e) => AskHowTo(info);
            var remove = new Button { Content = L.T("Remove"), Height = 24, FontSize = 11, Padding = new Thickness(8, 0, 8, 0), Style = (Style)FindResource("CardButton"), Foreground = (SolidColorBrush)FindResource("Danger") };
            remove.Click += (s, e) => { PushUndo(); obj.Behaviors.Remove(bh); BuildInspector(obj); Dirty(); };
            Grid.SetColumn(title, 0); Grid.SetColumn(help, 1); Grid.SetColumn(remove, 2);
            header.Children.Add(title); header.Children.Add(help); header.Children.Add(remove);
            panel.Children.Add(header);

            if (bh.Kind == BehaviorKind.Tilemap)
            {
                TilemapRow(obj, bh, panel);
            }
            else
            {
                if (bh.Kind == BehaviorKind.LightSource) LightStyleRow(obj, info, panel);
                foreach (var np in info.Nums)
                {
                    if (bh.Kind == BehaviorKind.AdvancedPatrol && np.Key == "mode")
                    {
                        ChoiceRow(L.T("Format"), panel, (int)Math.Round(bh.Num("mode", 0)),
                            new[] { L.T("Platformer (gravity + jumps)"), L.T("Top-down (free + pathfinding)"), L.T("Free / flying (straight line)") },
                            i => { bh.Nums["mode"] = i; if (_selected == obj) BuildInspector(obj); });
                        continue;
                    }
                    if (bh.Kind == BehaviorKind.AdvancedPatrol && np.Key == "repeat")
                    {
                        ChoiceRow(L.T("Repeat"), panel, (int)Math.Round(bh.Num("repeat", 0)),
                            new[] { L.T("Back and forth (retrace)"), L.T("Loop (straight back to start)"), L.T("Once (stop at the end)") },
                            i => bh.Nums["repeat"] = i);
                        continue;
                    }
                    if (bh.Kind == BehaviorKind.AdvancedPatrol)
                    {
                        int m = (int)Math.Round(bh.Num("mode", 0));
                        if (np.Key == "pathfind" && m != 1) continue;                       // pathfinding only in top-down
                        if ((np.Key == "jump" || np.Key == "autoJump") && m != 0) continue;  // jumping only in platformer
                    }
                    NumParamRow(obj, bh, info, np, panel);
                }
                foreach (var tp in info.Texts)
                    ParamRow(obj, bh, info, tp, panel); // every parameter is picked (dropdowns, pickers, item editor) — see MainWindow.Params.cs
            }

            InspectorRoot.Children.Add(new Border
            {
                Background = (SolidColorBrush)FindResource("Bg2"),
                BorderBrush = (SolidColorBrush)FindResource("Border"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8), Child = panel
            });
        }

        // On a Light: the scene's lighting style (it lives on the Day/Night, but this is where it matters), both ways —
        // a Light's block options only exist in Blocks; in Smooth the card says so. With no Day/Night, how lights work.
        private void LightStyleRow(GameObjectData obj, BehaviorInfo info, Panel panel)
        {
            var dn = DayNightOf(_scene);
            if (dn != null)
            {
                var styles = CatalogOptions(BehaviorCatalog.Info(BehaviorKind.DayNight).Nums.First(n => n.Key == "lightStyle").Options);
                PickRow(L.T("Lighting style (the scene's Day/Night)"), panel, styles, dn.Num("lightStyle", 0) >= 0.5 ? "1" : "0", false, null, v =>
                {
                    SetBlockLighting(_scene, v == "1");
                    Dirty(); RefreshAfterChoice(obj, info);
                    Status(v == "1" ? L.T("Day/Night now lights the scene block by block.") : L.T("Day/Night now lights the scene with soft circles."));
                });
                if (dn.Num("lightStyle", 0) >= 0.5) return;
            }
            panel.Children.Add(new TextBlock
            {
                Text = dn != null
                    ? L.T("Blocks stopping this light, colouring what it touches and adding up with other lights need Day/Night → Lighting style: Blocks.")
                    : L.T("Lights shine in the dark of a Day/Night cycle. Add Day/Night to an object, with Lighting style: Blocks, for light that blocks stop and that colours what it touches."),
                TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 4, 0, 6), Foreground = (SolidColorBrush)FindResource("TextDim")
            });
        }

        private static BehaviorData? DayNightOf(Scene scene) =>
            scene.Layers.SelectMany(l => l.Objects).SelectMany(o => o.Behaviors).FirstOrDefault(b => b.Kind == BehaviorKind.DayNight);

        /// <summary>Sets the scene's Day/Night lighting to Blocks (<paramref name="blocks"/>) or Smooth; false if the scene
        /// has no Day/Night.</summary>
        public static bool SetBlockLighting(Scene scene, bool blocks)
        {
            var dn = DayNightOf(scene);
            if (dn == null) return false;
            dn.Nums["lightStyle"] = blocks ? 1 : 0;
            return true;
        }

        // "＋ New item": make a custom tool/weapon (shovel, hammer, your own picture…) and put it in this hotbar
        private void NewItemButton(GameObjectData obj, BehaviorData hotbar, Panel panel)
        {
            var btn = new Button { Content = L.T("＋ New item (tool / weapon)…"), Height = 28, Margin = new Thickness(0, 8, 0, 0), Style = (Style)FindResource("CardButton") };
            btn.Click += (s, e) =>
            {
                var dlg = new NewItemDialog(_project) { Owner = this };
                if (dlg.ShowDialog() != true || dlg.Result == null) return;
                PushUndo();
                var msg = ItemMaker.Add(_project, _scene, obj, hotbar, dlg.Result);
                BuildInspector(obj); RenderScene(); Dirty();
                Status(msg);
            };
            panel.Children.Add(btn);
        }

        private void AddBehaviorControl(GameObjectData obj)
        {
            var combo = new ComboBox { Margin = new Thickness(0, 2, 0, 0) };
            combo.Items.Add(new ComboBoxItem { Content = L.T("＋ Add behavior…"), Tag = null, IsSelected = true });
            foreach (var info in BehaviorCatalog.All)
                if (!info.Hidden) combo.Items.Add(new ComboBoxItem { Content = $"{info.Icon}  {L.T(info.Name)}", Tag = info.Kind });
            combo.SelectionChanged += (s, e) =>
            {
                if (combo.SelectedItem is ComboBoxItem ci && ci.Tag is BehaviorKind kind)
                {
                    PushUndo();
                    // apply to EVERY selected object, not just the primary; skip ones that already have it
                    var targets = _selection.Count > 1 ? _selection.Where(o => !o.Locked).ToList() : new List<GameObjectData> { obj }; // locked ones are skipped
                    int added = 0;
                    foreach (var t in targets)
                    {
                        if (t.Behaviors.Any(b => b.Kind == kind)) continue;
                        t.Behaviors.Add(BehaviorCatalog.Info(kind).CreateData());
                        added++;
                    }
                    var sky = kind == BehaviorKind.DayNight ? SkySetup.Ensure(_scene) : null; // Day/Night → Sky layer + Sun + Moon
                    if (sky != null) RefreshLayersKeepActive();
                    BuildInspector(obj); RenderScene(); Dirty();
                    if (sky != null) Status(sky);
                    else if (targets.Count > 1) Status(L.F("Added {0} to {1} of {2} selected object(s)", L.T(BehaviorCatalog.Info(kind).Name), added, targets.Count));
                }
            };
            InspectorRoot.Children.Add(combo);
        }

        private void ScriptRow(GameObjectData obj, BehaviorData bh, Panel target)
        {
            var blockBtn = new Button { Content = L.T("🧩  Build with blocks (easy)"), Style = (Style)FindResource("AccentButton"), Height = 36, Margin = new Thickness(0, 2, 0, 6), HorizontalContentAlignment = HorizontalAlignment.Center };
            blockBtn.Click += (s, e) =>
            {
                // the blocks' "✨ Ask the AI" goes through the same AI as the panel (or says why it can't)
                var win = new BlockEditorWindow(_project, _scene, bh.Text("script", "[]"), obj,
                    (prompt, system) => AiNotReadyReason() is string notReady ? throw new AiException(notReady + ".")
                                                                               : AiGenerateAsync(prompt, system, json: true, temperature: 0.1)) { Owner = this };
                if (win.ShowDialog() == true)
                {
                    PushUndo(); bh.Texts["script"] = win.ResultJson; Dirty();
                    if (_selected != null) BuildInspector(_selected);
                }
            };
            target.Children.Add(blockBtn);

            target.Children.Add(new TextBlock { Text = L.T("Advanced — raw rules (JSON). The blocks above edit this for you."), Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11, Margin = new Thickness(0, 2, 0, 4), TextWrapping = TextWrapping.Wrap });
            var tb = new TextBox
            {
                Text = bh.Text("script", "[]"),
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 110,
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
        private (double ts, int cols, int rows, int[] cells, List<TileKind> pal) ReadTilemap(BehaviorData bh)
        {
            double ts = Math.Max(4, bh.Num("tileSize", 32));
            var pal = TilePalette.Parse(bh.Text("palette", ""));
            if (pal.Count == 0) { pal.Add(new("#5BA862", true)); pal.Add(new("#6B5A45", true)); pal.Add(new("#3A3A46", true)); }

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
            _tmBh.Texts["palette"] = TilePalette.Serialize(_tmPalette);
            _tmBh.Texts["data"] = JsonSerializer.Serialize(new { cols = _tmCols, rows = _tmRows, cells = _tmCells });
            _tmBh.Nums["tileSize"] = _tmSize;
        }

        private void DrawTilemap(GameObjectData obj, bool active)
        {
            var bh = obj.Behaviors.First(b => b.Kind == BehaviorKind.Tilemap);
            double ts; int cols, rows; int[] cells; List<TileKind> pal;
            if (active && ReferenceEquals(obj, _tmObj)) { ts = _tmSize; cols = _tmCols; rows = _tmRows; cells = _tmCells; pal = _tmPalette; }
            else { var t = ReadTilemap(bh); ts = t.ts; cols = t.cols; rows = t.rows; cells = t.cells; pal = t.pal; }
            var fills = TileFills(pal); // one shared brush per tile kind (MainWindow.Tiles.cs)
            double opacity = Math.Clamp(obj.Opacity, 0, 1);

            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int val = cells[r * cols + c];
                    if (val <= 0 || val > pal.Count) continue;
                    var rect = TileRect(fills[val - 1], ts, opacity);
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
                var rect = TileRect(TileFill(_tmPalette[val - 1]), _tmSize, Math.Clamp(_tmObj.Opacity, 0, 1));
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
            panel.Children.Add(new TextBlock { Text = L.T("Left-drag paints · Right-drag erases"), Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11, Margin = new Thickness(0, 2, 0, 6), TextWrapping = TextWrapping.Wrap });

            NumRow(L.T("Tile size"), _tmSize, v => { _tmSize = Math.Max(4, v); SerializeTilemap(); RenderScene(); Dirty(); }, panel);
            NumRow(L.T("Columns"), _tmCols, v => ResizeGrid((int)v, _tmRows), panel);
            NumRow(L.T("Rows"), _tmRows, v => ResizeGrid(_tmCols, (int)v), panel);

            panel.Children.Add(new TextBlock { Text = L.T("TILES (click to select)"), Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
            var wrap = new WrapPanel();
            wrap.Children.Add(TileSwatch(0));
            for (int i = 1; i <= _tmPalette.Count; i++) wrap.Children.Add(TileSwatch(i));
            var add = new Border { Width = 34, Height = 34, Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, CornerRadius = new CornerRadius(4), Background = (SolidColorBrush)FindResource("Bg2"), BorderBrush = (SolidColorBrush)FindResource("Border"), BorderThickness = new Thickness(1), Child = new TextBlock { Text = "+", Foreground = (SolidColorBrush)FindResource("Text"), FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            add.MouseLeftButtonUp += (s, e) => { _tmPalette.Add(new("#8A8A8A", true)); _tmActive = _tmPalette.Count; SerializeTilemap(); BuildInspector(obj); };
            wrap.Children.Add(add);
            panel.Children.Add(wrap);

            if (_tmActive >= 1 && _tmActive <= _tmPalette.Count)
            {
                var cur = _tmPalette[_tmActive - 1];
                ColorRow(L.T("Tile color"), cur.Color, v => { cur.Color = v; SerializeTilemap(); RenderScene(); Dirty(); }, panel);
                TilePictureRow(obj, cur, panel); // 🎨 Draw · Choose image… · 🎞 Animate · ✕ (MainWindow.Tiles.cs)
                var chk = new CheckBox { Content = L.T("Solid (collision)"), IsChecked = cur.Solid, Foreground = (SolidColorBrush)FindResource("Text"), Margin = new Thickness(0, 2, 0, 6) };
                chk.Checked += (s, e) => { cur.Solid = true; SerializeTilemap(); Dirty(); };
                chk.Unchecked += (s, e) => { cur.Solid = false; SerializeTilemap(); Dirty(); };
                panel.Children.Add(chk);
                var rem = new Button { Content = L.T("Remove this tile"), Height = 28, FontSize = 11, Style = (Style)FindResource("CardButton"), Foreground = (SolidColorBrush)FindResource("Danger"), HorizontalContentAlignment = HorizontalAlignment.Center };
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
                Background = index == 0 ? (SolidColorBrush)FindResource("Bg2") : TileFill(_tmPalette[index - 1]) // its picture (first frame) or colour
            };
            if (index > 0 && _tmPalette[index - 1].HasPicture) RenderOptions.SetBitmapScalingMode(b, BitmapScalingMode.NearestNeighbor);
            if (index == 0) b.Child = new TextBlock { Text = "⌫", Foreground = (SolidColorBrush)FindResource("Text"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            b.ToolTip = index == 0 ? L.T("Eraser") : (_tmPalette[index - 1].Solid ? L.T("Solid tile") : L.T("Decoration"))
                        + (index > 0 && _tmPalette[index - 1].Animated ? " · " + L.T("animated") : "");
            b.MouseLeftButtonUp += (s, e) => { _tmActive = index; if (_tmObj != null) BuildInspector(_tmObj); };
            return b;
        }

        // ---- UI builders ----
        private void Section(string title) => InspectorRoot.Children.Add(new TextBlock { Text = title, Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 8) });

        private Grid RowGrid(string label, Panel? target = null)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = new TextBlock { Text = label, Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 6, 0), ToolTip = label };
            Grid.SetColumn(lbl, 0); grid.Children.Add(lbl);
            (target ?? InspectorRoot).Children.Add(grid);
            return grid;
        }

        private void SliderRow(string label, double value, double min, double max, Action<double> onChange, Panel? target = null, bool percent = false)
        {
            var grid = RowGrid(label, target);
            var inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            var val = new TextBlock { Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Margin = new Thickness(6, 0, 0, 0) };
            void Show(double v) => val.Text = percent ? $"{Math.Round(v * 100)}%" : v.ToString("0.##");
            var slider = MakeSlider(min, max, value, onChange, Show);
            Grid.SetColumn(slider, 0); Grid.SetColumn(val, 1);
            inner.Children.Add(slider); inner.Children.Add(val);
            Grid.SetColumn(inner, 1); grid.Children.Add(inner);
        }

        // click-to-position AND drag (SliderBar.cs); each click or drag is one undo step
        private FrameworkElement MakeSlider(double min, double max, double value, Action<double> onChange, Action<double> show) =>
            SliderBar.Make(min, max, value, onChange, show, onStart: PushUndo);

        private void ChoiceRow(string label, Panel panel, int current, string[] options, Action<int> onPick)
        {
            var g = RowGrid(label, panel);
            var cb = new ComboBox { Height = 28, VerticalAlignment = VerticalAlignment.Center };
            foreach (var o in options) cb.Items.Add(o);
            cb.SelectedIndex = Math.Clamp(current, 0, options.Length - 1);
            cb.SelectionChanged += (s, e) => { onPick(cb.SelectedIndex); Dirty(); };
            Grid.SetColumn(cb, 1); g.Children.Add(cb);
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
            var tb = L.Keep(new TextBlock { Text = value, Foreground = (SolidColorBrush)FindResource("Text"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center }); // a value, not a label
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
            // a button showing the colour: click → the colour picker (which also accepts a hex code)
            var grid = RowGrid(label, target);
            var swatch = new Rectangle { Width = 22, Height = 22, RadiusX = 4, RadiusY = 4, Fill = Theme.Hex(value, "#000"), Stroke = (Brush)FindResource("Border"), StrokeThickness = 1 };
            var hexText = L.Keep(new TextBlock { Text = value, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(swatch); content.Children.Add(hexText);
            var btn = new Button { Content = content, Height = 30, Style = (Style)FindResource("CardButton"), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 0, 6, 0), ToolTip = L.T("Click to pick a color") };
            btn.Click += (s, e) => ShowColorPicker(btn, hexText.Text, hex => { hexText.Text = hex; swatch.Fill = Theme.Hex(hex, "#000"); onChange(hex); });
            Grid.SetColumn(btn, 1); grid.Children.Add(btn);
        }

        private void ShowColorPicker(FrameworkElement anchor, string initial, Action<string> onPick)
            => ColorPickerPopup.Open(anchor, initial, onPick, PushUndo);

        // A sound parameter: pick a .wav/.mp3 (copied into Assets/sounds). Used for SoundEmitter's "asset"
        // and for any behavior text parameter whose key ends in "Sound" (hitSound, swingSound…).
        private void SoundRow(GameObjectData obj, BehaviorData bh, Panel target, string key = "asset", string? label = null)
        {
            var grid = RowGrid(label ?? L.T("Sound file"), target);
            var inner = new StackPanel { Orientation = Orientation.Horizontal };
            var current = bh.Text(key, "");
            var pick = new Button { Content = string.IsNullOrEmpty(current) ? L.T("Choose…") : Path.GetFileName(current), Style = (Style)FindResource("CardButton"), Height = 28, Padding = new Thickness(8, 0, 8, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
            if (!string.IsNullOrEmpty(current)) L.SetSkip(pick, true); // the file name
            pick.Click += (s, e) =>
            {
                var dlg = new OpenFileDialog { Title = L.T("Choose sound"), Filter = L.T("Audio") + "|*.wav;*.mp3" };
                if (dlg.ShowDialog() == true)
                {
                    try { PushUndo(); bh.Texts[key] = ProjectIO.ImportAsset(_project, dlg.FileName, "sounds"); BuildInspector(obj); Dirty(); }
                    catch (Exception ex) { MessageBox.Show(L.F("Error: {0}", ex.Message)); }
                }
            };
            inner.Children.Add(pick);
            if (!string.IsNullOrEmpty(current))
            {
                var clear = new Button { Content = "✕", ToolTip = L.T("No sound"), Style = (Style)FindResource("CardButton"), Height = 28, Width = 28, Margin = new Thickness(4, 0, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
                clear.Click += (s, e) => { PushUndo(); bh.Texts[key] = ""; BuildInspector(obj); Dirty(); };
                inner.Children.Add(clear);
            }
            Grid.SetColumn(inner, 1); grid.Children.Add(inner);
        }

        // ============================================================
        //  TABS
        // ============================================================
        // double-click an object (in the scene or the objects list): it is selected and its inspector comes forward
        private void OpenInspector(GameObjectData obj)
        {
            Select(obj);
            SetTab(true);
            if (_panes.TryGetValue("ai", out var pane) && pane.FloatWin != null) pane.FloatWin.Activate();
            InspectorScroll.ScrollToTop();
        }

        private void ObjectsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ObjectsList.SelectedItem is ListBoxItem { Tag: GameObjectData obj }) OpenInspector(obj);
        }

        internal void TestOpenInspector(GameObjectData obj) => OpenInspector(obj);
        internal bool TestInspectorShown => InspectorScroll.Visibility == Visibility.Visible;

        private void TabInspector_Click(object sender, RoutedEventArgs e) => SetTab(true);
        private void TabAi_Click(object sender, RoutedEventArgs e) => SetTab(false);

        private void SetTab(bool inspector)
        {
            InspectorScroll.Visibility = inspector ? Visibility.Visible : Visibility.Collapsed;
            AiPanel.Visibility = inspector ? Visibility.Collapsed : Visibility.Visible;
            TabInspector.Background = inspector ? (SolidColorBrush)FindResource("Accent") : (SolidColorBrush)FindResource("Bg2");
            TabAi.Background = inspector ? (SolidColorBrush)FindResource("Bg2") : (SolidColorBrush)FindResource("Accent");
            TabInspector.Foreground = inspector ? (SolidColorBrush)FindResource("AccentText") : (SolidColorBrush)FindResource("Text");
            TabAi.Foreground = inspector ? (SolidColorBrush)FindResource("Text") : (SolidColorBrush)FindResource("AccentText");
        }

        private void AiBuild_Click(object sender, RoutedEventArgs e) => SetAiMode(AiMode.Build);
        private void AiBrainstorm_Click(object sender, RoutedEventArgs e) => SetAiMode(AiMode.Brainstorm);
        private void AiLearn_Click(object sender, RoutedEventArgs e) => SetAiMode(AiMode.Learn);

        private void SetAiMode(AiMode mode)
        {
            _aiMode = mode;
            foreach (var (btn, m) in new[] { (AiModeBuild, AiMode.Build), (AiModeBrainstorm, AiMode.Brainstorm), (AiModeLearn, AiMode.Learn) })
            {
                btn.Background = (SolidColorBrush)FindResource(m == mode ? "Accent" : "Bg2");
                btn.Foreground = (SolidColorBrush)FindResource(m == mode ? "AccentText" : "Text");
            }
            AiSend.Content = mode switch { AiMode.Brainstorm => L.T("Send"), AiMode.Learn => L.T("Ask"), _ => L.T("Build it") };
            AiHint.Text = mode switch
            {
                AiMode.Brainstorm => L.T("Chat about ideas — the AI won't change anything."),
                AiMode.Learn => L.T("Ask how to use anything — the AI explains step by step."),
                _ => L.T("Describe what to build.")
            };
        }

        // ============================================================
        //  AI → BEHAVIOR BRIDGE
        // ============================================================
        private void StrengthCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CustomModel == null || OllamaStatus == null) return;
            if (StrengthCombo.SelectedItem is not ComboBoxItem item) return;
            var tag = (string)(item.Tag ?? "");

            _useCloud = tag.StartsWith("cloud:", StringComparison.Ordinal);
            bool custom = string.IsNullOrEmpty(tag);

            CustomModel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;

            if (_useCloud)
            {
                var rest = tag.Substring("cloud:".Length); // "auto" | providerId | providerId|model
                int bar = rest.IndexOf('|');
                if (bar >= 0) { _cloudProvider = rest.Substring(0, bar); _cloudModelOverride = rest.Substring(bar + 1); }
                else { _cloudProvider = rest; _cloudModelOverride = ""; }
            }
            else _model = custom ? CustomModel.Text.Trim() : tag;

            if (AiSettings.AiChoice != tag) { AiSettings.AiChoice = tag; AiSettings.Save(); } // remembered across editor launches
            _ = RefreshOllamaStatusAsync();
        }

        private void CustomModel_TextChanged(object sender, TextChangedEventArgs e)
        {
            _model = CustomModel.Text.Trim();
            if (AiSettings.LocalModel != _model) { AiSettings.LocalModel = _model; AiSettings.Save(); }
            _ = RefreshOllamaStatusAsync();
        }

        // ---- cloud provider keys (one masked row per free provider) ----
        private bool AnyCloudKey() => CloudClient.Providers.Any(p => !string.IsNullOrWhiteSpace(AiSettings.Key(p.Id)));

        /// <summary>Why the AI can't be asked right now (no cloud key / no local model chosen), or null when it's ready.</summary>
        private string? AiNotReadyReason() =>
            _useCloud ? (AnyCloudKey() ? null : L.T("Add a free cloud API key first (⚙ AI settings)"))
                      : (string.IsNullOrWhiteSpace(_model) ? L.T("Choose an AI model first (AI panel)") : null);

        // one-line friendly blurb shown on each free provider's card
        private static string FreeBlurb(string id) => id switch
        {
            "groq"       => L.T("★ Recommended — fast and simple, big free limit."),
            "gemini"     => L.T("Google. Very large daily limit."),
            "openrouter" => L.T("Lots of models, smaller daily limit."),
            "mistral"    => L.T("European. A good backup."),
            _            => L.T("Free, no credit card."),
        };

        private static readonly Brush ReadyGreen = Theme.Pick("#4ADE80", "#087A59");
        private static readonly Brush WarnYellow = Theme.Pick("#F4C03B", "#8A6300"); // "Ollama not found", "add a key": dark amber on the light theme

        private void BuildCloudKeyRows() { BuildFreeKeyRows(); BuildPaidKeyRows(); }

        // EASY: guided card per free provider — open the site, paste the key, see "Ready".
        private void BuildFreeKeyRows()
        {
            if (FreeKeysHost == null) return;
            FreeKeysHost.Children.Clear();
            var dim = (SolidColorBrush)FindResource("TextDim");
            foreach (var prov in CloudClient.Providers)
            {
                if (!prov.Free) continue;
                var p = prov; // capture
                var card = new Border
                {
                    Background = (SolidColorBrush)FindResource("Bg2"),
                    BorderBrush = (SolidColorBrush)FindResource("Border"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(9, 7, 9, 9),
                    Margin = new Thickness(0, 0, 0, 7)
                };
                var body = new StackPanel();

                // title row: name + live status
                var top = new Grid();
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                top.Children.Add(L.Keep(new TextBlock { Text = p.Name, FontSize = 12, FontWeight = FontWeights.SemiBold })); // a brand name
                var status = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(status, 1); top.Children.Add(status);
                body.Children.Add(top);
                body.Children.Add(new TextBlock { Text = FreeBlurb(p.Id), FontSize = 10, Foreground = dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 6) });

                // step 1: open the signup site
                var getBtn = new Button { Content = L.T("① Get free key (opens the site)"), Style = (Style)FindResource("CardButton"), Height = 28, HorizontalContentAlignment = HorizontalAlignment.Center };
                getBtn.Click += (s, e) => OpenUrl(p.KeyUrl);
                body.Children.Add(getBtn);

                // step 2: paste the key (masked, with Show)
                body.Children.Add(new TextBlock { Text = L.T("② Paste your key here:"), FontSize = 10, Foreground = dim, Margin = new Thickness(0, 6, 0, 2) });
                var pasteRow = new Grid();
                pasteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                pasteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var pw = new PasswordBox { VerticalContentAlignment = VerticalAlignment.Center };
                var tb = new TextBox { Visibility = Visibility.Collapsed, VerticalContentAlignment = VerticalAlignment.Center };
                var show = new CheckBox { Content = L.T("Show"), FontSize = 11, Foreground = dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0) };
                var field = new Grid();
                field.Children.Add(pw); field.Children.Add(tb);
                pasteRow.Children.Add(field);
                Grid.SetColumn(show, 1); pasteRow.Children.Add(show);
                body.Children.Add(pasteRow);

                void Refresh()
                {
                    bool set = !string.IsNullOrWhiteSpace(AiSettings.Key(p.Id));
                    status.Text = set ? L.T("✓ Ready") : L.T("not set yet");
                    status.Foreground = set ? ReadyGreen : dim;
                }
                bool syncing = false;
                pw.Password = AiSettings.Key(p.Id);
                pw.PasswordChanged += (s, e) => { if (syncing) return; AiSettings.SetKey(p.Id, pw.Password); AiSettings.Save(); Refresh(); if (_useCloud) _ = RefreshOllamaStatusAsync(); };
                tb.TextChanged += (s, e) => { if (syncing) return; AiSettings.SetKey(p.Id, tb.Text); AiSettings.Save(); Refresh(); if (_useCloud) _ = RefreshOllamaStatusAsync(); };
                show.Checked += (s, e) => { syncing = true; tb.Text = AiSettings.Key(p.Id); tb.Visibility = Visibility.Visible; pw.Visibility = Visibility.Collapsed; syncing = false; };
                show.Unchecked += (s, e) => { syncing = true; pw.Password = AiSettings.Key(p.Id); pw.Visibility = Visibility.Visible; tb.Visibility = Visibility.Collapsed; syncing = false; };
                Refresh();

                card.Child = body;
                FreeKeysHost.Children.Add(card);
            }
        }

        // ADVANCED: compact masked rows for the paid / bring-your-own-account providers.
        private void BuildPaidKeyRows()
        {
            if (PaidKeysHost == null) return;
            PaidKeysHost.Children.Clear();
            var dim = (SolidColorBrush)FindResource("TextDim");
            foreach (var prov in CloudClient.Providers)
            {
                if (prov.Free) continue;
                var p = prov; // capture
                var header = new Grid { Margin = new Thickness(0, 7, 0, 2) };
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                header.Children.Add(L.Keep(new TextBlock { Text = $"{p.Name}  ·  {p.KeyUrl}", Foreground = dim, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }));
                var show = new CheckBox { Content = L.T("Show"), FontSize = 11, Foreground = dim, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(show, 1); header.Children.Add(show);
                PaidKeysHost.Children.Add(header);

                var pw = new PasswordBox();
                var tb = new TextBox { Visibility = Visibility.Collapsed };
                bool syncing = false;
                pw.Password = AiSettings.Key(p.Id);
                pw.PasswordChanged += (s, e) => { if (syncing) return; AiSettings.SetKey(p.Id, pw.Password); AiSettings.Save(); if (_useCloud) _ = RefreshOllamaStatusAsync(); };
                tb.TextChanged += (s, e) => { if (syncing) return; AiSettings.SetKey(p.Id, tb.Text); AiSettings.Save(); if (_useCloud) _ = RefreshOllamaStatusAsync(); };
                show.Checked += (s, e) => { syncing = true; tb.Text = AiSettings.Key(p.Id); tb.Visibility = Visibility.Visible; pw.Visibility = Visibility.Collapsed; syncing = false; };
                show.Unchecked += (s, e) => { syncing = true; pw.Password = AiSettings.Key(p.Id); pw.Visibility = Visibility.Visible; tb.Visibility = Visibility.Collapsed; syncing = false; };
                PaidKeysHost.Children.Add(pw);
                PaidKeysHost.Children.Add(tb);

                // the Custom provider also needs an endpoint URL + model name
                if (p.Id == "custom")
                {
                    var urlBox = new TextBox { Text = AiSettings.CustomUrl, Margin = new Thickness(0, 3, 0, 0) };
                    urlBox.TextChanged += (s, e) => { AiSettings.CustomUrl = urlBox.Text.Trim(); AiSettings.Save(); };
                    var modelBox = new TextBox { Text = AiSettings.CustomModel, Margin = new Thickness(0, 3, 0, 0) };
                    modelBox.TextChanged += (s, e) => { AiSettings.CustomModel = modelBox.Text.Trim(); AiSettings.Save(); };
                    PaidKeysHost.Children.Add(new TextBlock { Text = L.T("Base URL (…/v1) and model name:"), Foreground = dim, FontSize = 10, Margin = new Thickness(0, 3, 0, 0) });
                    PaidKeysHost.Children.Add(urlBox);
                    PaidKeysHost.Children.Add(modelBox);
                }
            }
        }

        private static void OpenUrl(string url)
        {
            try
            {
                if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        private void AdvancedToggle_Click(object sender, RoutedEventArgs e)
        {
            if (AdvancedPanel == null) return;
            bool open = AdvancedPanel.Visibility != Visibility.Visible;
            AdvancedPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (AdvancedToggleText != null)
                AdvancedToggleText.Text = (open ? "▾ " : "▸ ") + L.T("Advanced — paid keys, offline AI");
        }

        private void AiConfigToggle_Click(object sender, RoutedEventArgs e)
        {
            if (AiConfigPanel == null) return;
            AiConfigPanel.Visibility = AiConfigPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        private string CloudLabel()
        {
            if (_cloudProvider == "auto") return L.T("Auto");
            var name = CloudClient.Provider(_cloudProvider)?.Name ?? _cloudProvider;
            if (string.IsNullOrEmpty(_cloudModelOverride)) return name;
            var sm = _cloudModelOverride.Contains('/') ? _cloudModelOverride[(_cloudModelOverride.IndexOf('/') + 1)..] : _cloudModelOverride;
            return $"{name} · {sm}";
        }

        private void UpdateAiConfigSummary()
        {
            if (AiConfigSummary == null) return;
            if (_useCloud)
                AiConfigSummary.Text = AnyCloudKey() ? L.F("⚙ ☁ Cloud · {0}", CloudLabel()) : L.T("⚙ ☁ Cloud — tap to add a free key");
            else
                AiConfigSummary.Text = L.F("⚙ ⚡ Local · {0}", _model);
        }

        private async Task RefreshOllamaStatusAsync()
        {
            if (OllamaStatus == null) return;
            UpdateAiConfigSummary();

            if (_useCloud)
            {
                bool hasKey = AnyCloudKey();
                OllamaDot.Foreground = hasKey ? (SolidColorBrush)FindResource("Accent") : WarnYellow;
                OllamaStatus.Foreground = hasKey ? (SolidColorBrush)FindResource("Accent") : WarnYellow;
                if (!hasKey)
                    OllamaStatus.Text = L.T("☁ Cloud selected — add a free API key below (any provider works).");
                else
                {
                    var names = string.Join(", ", CloudClient.Providers.Where(p => !string.IsNullOrWhiteSpace(AiSettings.Key(p.Id))).Select(p => p.Name));
                    OllamaStatus.Text = L.F("☁ Cloud · {0}. Keys set: {1}. Hitting one's daily limit auto-switches to the next.", CloudLabel(), names);
                }
                return;
            }

            if (!await _ollama.IsRunningAsync())
            {
                OllamaDot.Foreground = WarnYellow;
                OllamaStatus.Foreground = WarnYellow;
                OllamaStatus.Text = L.T("⚠ Ollama not found. The engine tries to start it automatically on launch.");
                return;
            }
            var models = await _ollama.ListModelsAsync();
            if (!string.IsNullOrWhiteSpace(_model) && models.Contains(_model))
            {
                OllamaDot.Foreground = (SolidColorBrush)FindResource("Accent");
                OllamaStatus.Foreground = (SolidColorBrush)FindResource("Accent");
                OllamaStatus.Text = L.F("✓ Connected · model \"{0}\" ready.", _model);
            }
            else
            {
                OllamaDot.Foreground = WarnYellow;
                OllamaStatus.Foreground = WarnYellow;
                OllamaStatus.Text = L.F("Ollama ok, but the model isn't downloaded:\n    ollama pull {0}", _model);
            }
        }

        private string ActiveModelLabel => _useCloud ? L.F("Cloud · {0}", CloudLabel()) : _model;

        // Routes generation to the active provider. Cloud mode tries providers in order and falls back when one is out of quota.
        private async Task<string> AiGenerateAsync(string prompt, string system, bool json, double temperature, CancellationToken ct = default)
        {
            if (!_useCloud) return await _ollama.GenerateAsync(_model, prompt, system, json, temperature, ct);

            List<CloudProvider> order;
            if (_cloudProvider == "auto")
            {
                // only rotate FREE providers with a key — never spend a paid account automatically
                order = CloudClient.Providers.Where(p => p.Free && !string.IsNullOrWhiteSpace(AiSettings.Key(p.Id))).ToList();
            }
            else
            {
                // a specific provider (incl. paid) is used alone — the user controls exactly what gets billed
                var sel = CloudClient.Provider(_cloudProvider);
                order = sel != null && !string.IsNullOrWhiteSpace(AiSettings.Key(sel.Id))
                    ? new List<CloudProvider> { string.IsNullOrEmpty(_cloudModelOverride) ? sel : sel with { Model = _cloudModelOverride } }
                    : new List<CloudProvider>();
            }

            if (order.Count == 0) throw new CloudException(_cloudProvider == "auto"
                ? L.T("No free cloud key set. Add one in the AI settings (⚙).")
                : L.T("Add the API key for this provider in the AI settings (⚙)."));

            // Auto: any failure moves on to the next free provider. When every one is only busy for a few seconds
            // (a per-minute limit), wait that long once and go round again instead of giving up.
            bool auto = _cloudProvider == "auto";
            CloudException? last = null;
            bool allBusy = false;
            for (int round = 0; round < 2; round++)
            {
                double wait = double.MaxValue;
                allBusy = true;
                foreach (var p in order)
                {
                    try { return await _cloud.GenerateAsync(p, prompt, system, AiSettings.Key(p.Id), json, temperature, ct); }
                    catch (CloudException ex) when (auto || ex.RateLimited)
                    {
                        last = ex; allBusy &= ex.RateLimited;
                        wait = ex.RateLimited ? Math.Min(wait, ex.RetryAfter > 0 ? ex.RetryAfter : 10) : wait;
                    }
                }
                if (round > 0 || wait > 60) break; // a per-minute limit: worth a short wait; a daily one isn't
                Status(L.F("The free AI is busy — trying again in {0} s…", Math.Ceiling(wait)));
                await Task.Delay(TimeSpan.FromSeconds(wait + 0.5), ct);
            }
            if (auto && allBusy && order.Count > 1) // every free tier is used up for now: say what to do, not one provider's raw message
                throw new CloudException(L.T("Every free AI is out of quota for now (it comes back during the day). Try again later, add a key for another free provider (⚙), or switch to the local AI."));
            throw last ?? new CloudException(L.T("All free providers are out of quota right now. Try again later."));
        }

        private async void AiSend_Click(object sender, RoutedEventArgs e)
        {
            var prompt = AiInput.Text.Trim();
            if (prompt.Length == 0) return;
            if (AiNotReadyReason() is string notReady) { Status(notReady); return; }

            if (_aiMode != AiMode.Build) // Brainstorm / Learn: just chat, never change the project
            {
                bool learn = _aiMode == AiMode.Learn;
                AddAiMessage(L.T("You"), prompt, false);
                AiInput.Clear();
                var th = AddAiMessage(L.F("AI ({0})", ActiveModelLabel), L.T("thinking…"), true);
                AiSend.IsEnabled = false; Status(learn ? L.T("Looking it up…") : L.T("Brainstorming…"));
                try { var ans = learn ? await TeachAsync(prompt) : await BrainstormAsync(prompt); AiLog.Children.Remove(th); AddAiMessage(L.T("AI"), ans.Length > 0 ? ans : L.T("(no response)"), true); Status(L.T("Ready")); }
                catch (AiException ex) { AiLog.Children.Remove(th); AddAiMessage(L.T("AI"), "⚠ " + ex.Message, true); Status(L.T("Failed")); }
                finally { AiSend.IsEnabled = true; }
                return;
            }

            AddAiMessage(_selected != null ? L.F("You (selected: {0})", _selected.Name) : L.T("You"), prompt, false);
            AiInput.Clear();
            var thinking = AddAiMessage(L.F("AI ({0})", ActiveModelLabel), L.T("thinking…"), true);
            AiSend.IsEnabled = false;
            Status(L.F("Asking {0}…", ActiveModelLabel));

            try
            {
                var (ok, summary, reply) = await AiBuildAsync(prompt);

                AiLog.Children.Remove(thinking);
                if (ok)
                {
                    _aiLastSummary = summary;
                    _aiLastObjects.Clear();
                    _aiLastObjects.AddRange(_aiSpecObjects); // the objects the change touched (not read back from the translated summary)
                    AddAiMessage(L.T("AI — applied ✓"), summary, true);
                    RenderScene(); RefreshLayers(); RefreshObjects(); RefreshPalette();
                    if (_selected != null) BuildInspector(_selected);
                    Dirty();
                    Status(L.T("AI applied changes. Press Test to try it."));
                }
                else
                {
                    AddAiMessage(L.T("AI"), L.F("I couldn't apply that. Try rephrasing. Raw reply:\n{0}", reply), true);
                    Status(L.T("Could not apply AI response"));
                }
            }
            catch (Exception ex) when (ex is AiException || ex is not OutOfMemoryException) // never take the editor down with unsaved work
            {
                AiLog.Children.Remove(thinking);
                AddAiMessage(L.T("AI"), "⚠ " + ex.Message, true);
                Status(L.T("Generation failed"));
            }
            finally { AiSend.IsEnabled = true; }
        }

        // 🔨 Build: ask the AI for a project spec and apply it (one retry with a firmer nudge). Shared by the panel and --aieval.
        private async Task<(bool ok, string summary, string reply)> AiBuildAsync(string prompt)
        {
            var sys = BuildProjectPrompt(compact: _useCloud || EvalCompact); // leaner prompt on the cloud (token limits); full detail on local
            var reply = await AiGenerateAsync(prompt, sys, json: true, temperature: 0.1);
            var own = StripCopies(sys, reply, prompt, out bool copied); // what a small model pasted from the examples is left out
            string summary = "";
            if (!copied && SafeApply(own, prompt, out summary)) return (true, summary, reply);

            var reply2 = await AiGenerateAsync(
                prompt + (copied ? "\n\nYour last answer only copied an example from the instructions. Answer THIS request instead." : "") +
                "\n\nReturn ONLY the JSON with \"create\"/\"edit\"/\"layers\". Use EXACT behavior kind names from the list. One object can have many behaviors.",
                sys, json: true, temperature: copied ? 0.4 : 0.1);
            var own2 = StripCopies(sys, reply2, prompt, out bool copied2);
            bool ok = !copied2 && SafeApply(own2, prompt, out summary);
            return (ok, summary, reply2);
        }

        private void AddImages_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = L.T("Add files for the AI to use (images or audio)"), Multiselect = true, Filter = L.T("Images & audio") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.wav;*.mp3|" + L.T("Images") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif|" + L.T("Audio") + "|*.wav;*.mp3" };
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
                catch (Exception ex) { MessageBox.Show(L.F("Error importing {0}: {1}", Path.GetFileName(f), ex.Message)); }
            }
            RefreshAiImages();
            Status(L.F("Added {0} image(s) and {1} sound(s) for the AI", img, snd));
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
                AiImages.Children.Add(L.Keep(thumb)); // the tooltip is the file name
            }
            foreach (var rel in _aiSounds)
            {
                AiImages.Children.Add(new Border
                {
                    Margin = new Thickness(0, 0, 4, 4), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 3, 6, 3),
                    Background = (SolidColorBrush)FindResource("Bg2"), BorderBrush = (SolidColorBrush)FindResource("Border"), BorderThickness = new Thickness(1),
                    Child = L.Keep(new TextBlock { Text = "♪ " + Path.GetFileName(rel), FontSize = 10, Foreground = (SolidColorBrush)FindResource("Text") })
                });
            }
        }

        // ============================================================
        //  AI TEST HARNESS (run many prompts through the real pipeline)
        // ============================================================
        private void ResetTestScene()
        {
            _project.Prefabs = PrefabFactory.Defaults();
            _project.Variables.Clear(); _project.Flags.Clear(); _project.Texts.Clear(); _project.Colors.Clear(); _project.Lists.Clear();

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
                if (VarScope.Of(o).Count > 0) sb.Append(" vars(" + VarScope.Of(o).Describe() + ")");
                sb.Append("} ");
            }
            var extraPrefabs = _project.Prefabs.Where(p => !new[] { "Player", "Enemy", "Bullet", "Particle", "Platform", "Door", "Tilemap" }.Contains(p.Name)).Select(p => p.Name).ToList();
            if (extraPrefabs.Count > 0) sb.Append("PREFABS[" + string.Join(",", extraPrefabs) + "] ");
            if (VarScope.Of(_project).Count > 0) sb.Append("GLOBAL(" + VarScope.Of(_project).Describe() + ") ");
            if (VarScope.Of(_scene).Count > 0) sb.Append("SCENE(" + VarScope.Of(_scene).Describe() + ") ");
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

        private string BuildProjectPrompt(bool compact = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are the game-builder AI of a 2D engine. Output ONLY a JSON object. Schema:");
            sb.AppendLine("{");
            sb.AppendLine("  \"create\": [ {\"name\",\"x\",\"y\",\"width\",\"height\",\"color\",\"layer\",\"physics\":\"None|Static|Dynamic\",\"behaviors\":[{\"kind\",\"nums\":{},\"texts\":{}}],\"custom\":[...],\"animations\":[{\"name\",\"frames\":[],\"fps\"}],\"sprite\":\"file.png\"} ],");
            sb.AppendLine("  \"edit\":   [ {\"name\":\"<existing object>\",\"behaviors\":[...],\"remove\":[\"Kind\"],\"custom\":[...],\"animations\":[...],\"color\",\"width\",\"height\"} ],");
            sb.AppendLine("  \"layers\": [ {\"name\",\"screenSpace\":true,\"visible\":true} ],");
            sb.AppendLine("  \"delete\": [\"<existing object name>\"],");
            sb.AppendLine("  \"variables\": {\"global\":{\"name\":value}, \"scene\":{\"name\":value}}, \"removeVariables\": {\"global\":[\"name\"], \"scene\":[\"name\"]}");
            sb.AppendLine("}");
            sb.AppendLine("Only include the fields/arrays you need. Omit empty ones.");
            sb.AppendLine("ORDER: \"z\":n on a create/edit object draws it in front of (higher) or behind (lower) the others in its layer (default 0). Objects marked LOCKED can't be changed — leave them out.");
            sb.AppendLine("VARIABLES: top-level \"variables\".global/.scene create or set the game's global/scene variables (initial values); \"removeVariables\" deletes them. Per-object variables go inside that object's edit/create as \"variables\":{\"name\":value} (and \"removeVariables\":[\"name\"]). You can change ANY variable this way. A value is a number, true/false (a Yes/No), \"words\" (a Text, e.g. \"playerName\":\"Hero\"), a colour \"#RRGGBB\" (a Colour, e.g. \"teamColor\":\"#FF8800\") or an array of words (a List, e.g. \"quests\":[\"Find the key\"]).");
            sb.AppendLine();
            sb.AppendLine("KEY RULES:");
            sb.AppendLine("- To make a NEW thing (player, enemy, boss, coin, platform…), add it to \"create\" and it will be placed in the scene. ONE object can and should have MANY behaviors.");
            sb.AppendLine("- To change an EXISTING object, add it to \"edit\" by its name. Behaviors are ADDED/UPDATED; keep existing ones unless you list them in \"remove\". Its \"custom\" rules are ADDED to the ones it has (a rule with the same event+action replaces the old one); to rewrite them all, send the full list with \"replaceCustom\":true. To delete objects: \"delete\":[\"Name\"].");
            sb.AppendLine("- If the user talks about \"the player\" but there is an object with Platformer/TopDown, that IS the player — edit it by its name. Don't require it to be selected.");
            sb.AppendLine("- If an object with that name ALREADY EXISTS in the scene, put it in \"edit\" (never create a duplicate).");
            sb.AppendLine("- To SPAWN objects during play (rocks falling, enemies appearing on a key/timer): create the thing with \"asPrefab\":true (it stays OUT of the scene until spawned; give it physics Dynamic if it should fall), then spawn it with a Spawner behavior or a custom spawn rule.");
            sb.AppendLine("- Use EXACT kind names from this list. Never invent kinds. One object, many behaviors.");
            sb.AppendLine();
            sb.AppendLine("EXAMPLES (the shape of an answer — never copy one unless it is exactly what was asked):");
            sb.AppendLine("User: \"make a player that walks, jumps, has health and can attack\" =>");
            sb.AppendLine("{\"create\":[{\"name\":\"Player\",\"x\":120,\"y\":300,\"width\":48,\"height\":64,\"color\":\"#3BC97A\",\"physics\":\"Dynamic\",\"behaviors\":[{\"kind\":\"Platformer\",\"nums\":{\"speed\":260,\"jump\":640}},{\"kind\":\"FollowCamera\"},{\"kind\":\"Health\",\"nums\":{\"max\":5,\"respawn\":1}},{\"kind\":\"ToolUse\",\"texts\":{\"tool\":\"sword\"}}]}]}");
            sb.AppendLine("User: \"give the player more health\" (player already exists) =>");
            sb.AppendLine("{\"edit\":[{\"name\":\"Player\",\"behaviors\":[{\"kind\":\"Health\",\"nums\":{\"max\":8}}]}]}");
            sb.AppendLine("User: \"create a global score at 0 and lives at 3, and set the scene timer to 60\" => {\"variables\":{\"global\":{\"score\":0,\"lives\":3},\"scene\":{\"timer\":60}}}");
            if (_aiImages.Count > 0)
                sb.AppendLine("User: \"with these images make a boss with full movement\" => {\"create\":[{\"name\":\"Boss\",\"x\":700,\"y\":200,\"width\":120,\"height\":120,\"physics\":\"Dynamic\",\"behaviors\":[{\"kind\":\"Platformer\",\"nums\":{\"speed\":120,\"jump\":500}},{\"kind\":\"Health\",\"nums\":{\"max\":20,\"respawn\":0}},{\"kind\":\"Chase\",\"texts\":{\"target\":\"Player\"}},{\"kind\":\"Damager\",\"texts\":{\"target\":\"Player\"}}],\"animations\":[{\"name\":\"idle\",\"frames\":[\"<idle images>\"],\"fps\":6},{\"name\":\"walk\",\"frames\":[\"<walk images>\"],\"fps\":10}]}]}");
            sb.AppendLine();
            sb.AppendLine("Behavior kinds — nums(texts):");
            foreach (var info in BehaviorCatalog.All)
            {
                if (info.Kind == BehaviorKind.Tilemap || info.Hidden) continue; // painted by hand / legacy — never via AI
                var nums = string.Join(",", info.Nums.Select(n => n.Key));
                var texts = string.Join(",", info.Texts.Select(t => t.Key));
                sb.AppendLine($"  {info.Kind}: {nums}" + (texts.Length > 0 ? $" ({texts})" : ""));
            }
            // bosses and cutscenes keep their steps in texts.script (a JSON string); the card's "Ask the AI" writes them in detail
            sb.AppendLine("BOSS = an object with BossAI + Health (respawn=0, texts.var its own variable e.g. bossHP) + Damager (target Player). BossAI texts: moves=ground|fly, starts=start|near|var (+var), target, title. Its texts.script may stay empty: a new BossAI already charges and shoots; the user designs the attacks/phases in its card (🧩 Advanced Scratch / ✨ Ask the AI).");
            sb.AppendLine("CUTSCENE = an object with Cutscene: texts.trigger=start|enter|var|key (+var/key/who), nums once/lock/bars. Its texts.script is a JSON STRING {\"steps\":[{\"do\":\"say\",\"text\":\"...\",\"speaker\":\"...\"},...]} with step ids: " +
                          string.Join(",", StepCatalog.Cutscene.Select(d => d.Id)) + ". Keep it short or leave it for the card's ✨ Ask the AI.");
            sb.AppendLine();
            if (compact)
            {
                sb.AppendLine("For logic the kinds can't do, use a \"custom\" rule list: [{\"when\":<start|always|key|keypress|timer|after|touch|death|ifvar|iflocal|iftext|iflist|hasitem|expr|click|rightclick|mouseover|mousepress>,...params...,\"do\":<velocity|move|accelerate|jump|rotate|shoot|spawn|destroy|hide|show|goscene|setvar|addvar|setlocal|addlocal|settext|toggle|setcolor|tint|listadd|listremove|listclear|set|additem|removeitem|boost|setspeed|say|save|load|tween|emit>,...params...}]. Params by name: key, every, name, var/op/value, force, speed, vx/vy, dx/dy, to, amount, factor, text, scene, item, directions, prop. Texts: settext(var,to:\"Hi {name}\"), toggle(var) flips a Yes/No, iftext(var,op:is|isnot|contains|empty,value). Colours: setcolor(var,to:\"#FF8800\"), tint(color,target) paints an object; any colour param can be \"{colourVar}\". Lists: listadd/listremove(var,value), listclear(var), iflist(var,op:contains|notcontains|empty|notempty|countis|countatleast,value); {quests} shows the items, count:quests is how many. ANY value can be an \"expr\" string (math; reads self.x/self.cx/self.health, OtherObj.cx, a variable, item:NAME, count:LIST, mousex, time, dist(x1,y1,x2,y2)). Guard any rule with \"cond\":\"<expr>\". timer repeats; after(seconds) fires once. Fade/grow/slide = do:tween(prop=opacity|scale|x|y, to, duration), e.g. {\"when\":\"after\",\"seconds\":5,\"do\":\"tween\",\"prop\":\"opacity\",\"to\":0,\"duration\":3}. setcolor only sets a Colour variable. 'WHEN X happens, effect' is ALWAYS a custom rule (condition→action), never a static param.");
                sb.AppendLine("Player = Platformer, physics Dynamic, controls=1 (dash/doubleJump/wallJump are 0/1 nums). Enemies: controls=0; prefer Patrol/Chase/custom. Killable enemy = Health respawn=0; player = respawn=1. Moving platform = Solid+Patrol. To spawn during play: make it asPrefab:true, then a Spawner or a custom spawn rule. Per-object looks: opacity, cornerRadius, borderColor+borderWidth, color, font. Draw art: \"pixelArt\":{\"palette\":{\"X\":\"#RRGGBB\"},\"rows\":[\"..XX..\"]} ('.'=transparent, <=16px). Image with empty space around the drawing: \"fitHitbox\":true (collides with the drawing, not the box). Only touch the exact object(s)/layer(s) the user names.");
            }
            else
            {
            sb.AppendLine("Intent → kind: walk+jump=Platformer; top-down/8-dir=TopDown; ground/wall/platform=Solid; camera=FollowCamera; patrol=Patrol; chase player=Chase; shoot on key=Shooter; melee/sword/attack/use an item by clicking=ToolUse (texts tool=sword, or a Hotbar for several items); hp/lives=Health; spike/hazard/contact damage=Damager; checkpoint=Checkpoint; coin/pickup=Collectible; door/next level=GoToScene; HUD/score text=Label; glow=LightSource; spinning=Spin; spawner=Spawner; parallax bg=Parallax.");
            sb.AppendLine("Intent → kind (NEW): pushable crate/box/sokoban block=Pushable; lever/switch/toggle=Switch; locked gate/ability-gate/barrier that opens with a key=Gate (NOT GoToScene — Gate blocks the way, GoToScene changes scene); NPC/sign/chest/press-E-to-talk=Interact; pick up an ability or key that unlocks something=AbilityPickup; invisible trigger zone/region/level-exit area=Area.");
            sb.AppendLine("Gate opens when its var meets the condition: {\"kind\":\"Gate\",\"texts\":{\"var\":\"hasRedKey\"},\"nums\":{\"value\":1}}. Give the key with AbilityPickup {\"kind\":\"AbilityPickup\",\"texts\":{\"var\":\"hasRedKey\"}}. Metroidvania ability+gate: AbilityPickup texts{var:hasDash,grant:dash} then Gate texts{var:hasDash}.");
            sb.AppendLine("PROCEDURAL maps: put a Generator on an empty object sized to the region to fill (or scene-sized). It spawns prefab(s) when the scene loads. mode=once makes a STABLE map (same when you return); mode=regen RECREATES it on every visit (roguelike). Make the spawned things prefabs with asPrefab:true. e.g. {\"create\":[{\"name\":\"Coin\",\"asPrefab\":true,...}],\"edit\":[{\"name\":\"World\",\"behaviors\":[{\"kind\":\"Generator\",\"texts\":{\"prefab\":\"Coin,Enemy\",\"mode\":\"regen\",\"layout\":\"scatter\"},\"nums\":{\"count\":20,\"minGap\":80}}]}]}.");
            sb.AppendLine("Persistence: a Collectible/AbilityPickup with a texts \"id\" stays gone after it's taken (survives revisits + save). Save/continue progress with the save/load actions (custom do:save / do:load, or Interact action:save) — saves all global vars + current scene. Collectible can also carry texts{item:NAME}+nums{amount} to add to an inventory count.");
            sb.AppendLine("AESTHETICS (per object, in create/edit): \"opacity\"(0-1), \"cornerRadius\"(px, 0=square), \"borderColor\"(hex)+\"borderWidth\"(px outline), \"font\"(family name) + \"bold\"(true/false) for Label text, \"color\"(fill). Example rounded glowing box: {\"edit\":[{\"name\":\"Box\",\"cornerRadius\":16,\"borderColor\":\"#00E5FF\",\"borderWidth\":3,\"opacity\":0.9}]}.");
            sb.AppendLine("TEXT EFFECTS: a Label takes texts{effect: none|typewriter|fade|blink|pulse} + nums{effectSpeed}. e.g. a title that types out: {\"kind\":\"Label\",\"texts\":{\"text\":\"GAME OVER\",\"effect\":\"typewriter\"},\"nums\":{\"fontSize\":48}}.");
            sb.AppendLine("GAME THEME (top-level \"theme\" object, styles what the PLAYER sees): {\"theme\":{\"font\":\"Consolas\",\"dialogBg\":\"#101418\",\"dialogBorder\":\"#00E5FF\",\"dialogText\":\"#FFFFFF\",\"dialogSpeaker\":\"#F4C03B\",\"dialogFontSize\":18,\"dialogCorner\":10,\"dialogEffect\":\"typewriter\",\"dialogSpeed\":45}}. Use it when the user wants to restyle dialogue boxes or the game font.");
            sb.AppendLine("Intent → kind (MORE): enemy that chases AROUND walls (not straight) = PathChase (best for top-down); trampoline/bounce pad/jump pad = Spring (nums{force},texts{dir:up/down/left/right}); minimap/radar = Minimap (put on a UI layer, texts{corner:tl/tr/bl/br,player,markers}). Bouncy objects: set the object's physics Bounciness (0-1) in \"physics\" — a ball with bounciness 0.7 bounces off floors/walls. A character/enemy whose image has empty (transparent) space around the drawing: \"fitHitbox\":true in its create/edit — it then collides with the drawing, not the whole box.");
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
            sb.AppendLine("- Patrol nums: speed, distance, axis (0=horizontal, 1=vertical), smooth (1=ease to a stop at the ends), slowSteps (how many times it slows near an end; 0=no slowing), pause (seconds stopped at each end; 0=none). E.g. vertical lift that waits: Patrol axis=1, smooth=1, pause=1.5.");
            sb.AppendLine("- AdvancedPatrol = follows a free route the USER draws in the editor. You can ADD it but cannot draw the route — tell the user to select it and click 'Draw path'. Nums: mode (0=platformer gravity+jumps, 1=top-down free, 2=free/flying), speed, jump, autoJump, pathfind (route around walls; top-down only), repeat (0=back-and-forth, 1=loop, 2=once), pause, pauseStart, collide (1=has hitbox, 0=passes through walls).");
            sb.AppendLine("- Platformer/TopDown/AdvancedPatrol have a 'collide' num (default 1). Set collide=0 to make a character ignore walls/tilemaps (no hitbox / ghost).");
            sb.AppendLine();
            sb.AppendLine("DRAW SPRITES (pixel art): you can DRAW an object's look. On a create/edit object add \"pixelArt\":{\"palette\":{\"X\":\"#RRGGBB\",...},\"rows\":[\"..XX..\",...]}. Each row is a string; each character is a palette key; '.' or space = transparent. Keep it small (<=16x16 is best, up to 32). Match the object's width/height to the art size for crisp pixels. Only do this when the user asks you to draw/make art, or when no image was given and art would help.");
            sb.AppendLine("For animations with drawn frames: \"animations\":[{\"name\":\"walk\",\"fps\":8,\"loop\":true,\"frames\":[{\"palette\":{...},\"rows\":[...]},{\"palette\":{...},\"rows\":[...]}]}]. Name them idle/walk/jump/crouch so the engine auto-plays them. Example tiny coin: {\"pixelArt\":{\"palette\":{\"Y\":\"#ffd700\",\"o\":\"#b8860b\"},\"rows\":[\".YYY.\",\"YoYoY\",\"YoYoY\",\"YoYoY\",\".YYY.\"]}}.");
            sb.AppendLine();
            sb.AppendLine("CUSTOM behavior — for ANY special/unique logic the kinds can't do (enemy patterns, dialogue, variables, inventory, conditions). ALWAYS use \"custom\" for things like 'every N do X', timed attacks, phases:");
            sb.AppendLine("CRUCIAL: 'WHEN <something> happens, <effect>' (e.g. 'when the player hits X, go 2x faster', 'when touch spike, lose a life') is ALWAYS a custom rule (condition → action). NEVER express it by changing a static param — a static param would apply from the start, not on the event.");
            sb.AppendLine("\"custom\":[{\"when\":<start|always|key|keypress|timer|after|touch|death|ifvar|iflocal|iftext|iflist|hasitem|expr|click|rightclick|mousedown|mouseover|mousepress>, ...params, \"do\":<velocity|move|accelerate|jump|rotate|shoot|spawn|destroy|hide|show|goscene|setvar|addvar|setlocal|addlocal|settext|addtext|toggle|setcolor|tint|listadd|listremove|listclear|set|additem|removeitem|boost|setspeed|say|save|load|tween|emit>, ...params}]");
            sb.AppendLine("Condition params: key(key); keypress(key,cooldown); timer(every); touch(name) [fires once when contact starts]; death [fires when this object's health hits 0]; ifvar/iflocal(var,op,value); iftext(var,op=is|isnot|contains|empty,value) [compares a Text variable]; iflist(var,op=contains|notcontains|empty|notempty|countis|countatleast,value) [checks a List: an item, or how many]; hasitem(item); expr(expr) [any expression truthy]; click/rightclick/mousedown/mouseover [mouse over THIS object]; mousepress [mouse clicked anywhere].");
            sb.AppendLine("Action params: velocity(vx,vy); move(dx,dy); accelerate(ax,ay); jump(force); rotate(speed); shoot(directions,speed); spawn(object); goscene(scene); setvar/setlocal(var,to); addvar/addlocal(var,amount); settext/addtext(var,to) [to = words, {name} puts a variable in: \"Hello {playerName}!\"]; toggle(var) [flips a Yes/No]; setcolor(var,to) [to = \"#RRGGBB\": a Colour variable]; tint(color,[target]) [paints a plain-colour object, default target=self; color can be \"{teamColor}\" — so can emit's color]; listadd/listremove(var,value) [adds an item / removes the first equal one; value may hold {name}]; listclear(var); {quests} in a text shows a List's items, count:quests (in an expr or {count:quests}) is how many; settext/toggle/iftext/setcolor/list actions/iflist take \"local\":true for the object's own variable; set(prop,to,[target]) [prop=x|y|vx|vy|rot|facing|health|w|h — set a property, default target=self]; boost(factor); setspeed(factor); say(text,speaker). Timing: when:timer(every) REPEATS; when:after(seconds) fires ONCE that many seconds after the object appears. Gradual changes (fade, grow, slide) are do:tween(prop=opacity|scale|x|y|rot|w|h, to, duration) — e.g. fade out after 5 s over 3 s: {\"when\":\"after\",\"seconds\":5,\"do\":\"tween\",\"prop\":\"opacity\",\"to\":0,\"duration\":3}. setcolor only stores a colour in a Colour VARIABLE; it never changes how an object looks (use tint or tween).");
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
            }
            sb.Append(Recipes);
            sb.AppendLine();

            // scene inventory so the AI can target/avoid-dup
            sb.AppendLine("CURRENT SCENE \"" + _scene.Name + "\":");
            sb.AppendLine("Layers: " + string.Join(", ", _scene.Layers.Select(l => $"\"{l.Name}\"({l.Kind}{(l.ScreenSpace || l.Kind == LayerKind.UI ? ",fixed" : "")})")));
            var allObjs = _scene.Layers.SelectMany(l => l.Objects).ToList();
            if (allObjs.Count == 0) sb.AppendLine("Objects: (none yet)");
            int ruleBudget = 3000; // the rules shown in all: a big project must not blow the free tiers' token limits
            if (allObjs.Count > 0) foreach (var o in allObjs)
            {
                var ov = VarScope.Of(o).Count > 0 ? " vars{" + VarScope.Of(o).Describe() + "}" : "";
                var script = Compact(o.Behaviors.FirstOrDefault(b => b.Kind == BehaviorKind.Custom)?.Text("script", "") ?? "");
                var shown = script.Length > 600 ? script[..600] + "…" : script;
                var rules = script.Length <= 2 ? "" : shown.Length <= ruleBudget ? " custom=" + shown : " custom=(has rules)"; // so a change can keep or retune them
                if (script.Length > 2 && shown.Length <= ruleBudget) ruleBudget -= shown.Length;
                sb.AppendLine($"  \"{o.Name}\" [{string.Join(",", o.Behaviors.Select(b => b.Kind))}]{ov}{(o.Locked ? " LOCKED" : "")}{(o.Z != 0 ? $" z{o.Z}" : "")}{rules}");
            }
            if (VarScope.Of(_project).Count > 0) sb.AppendLine("Global variables: " + VarScope.Of(_project).Describe());
            if (VarScope.Of(_scene).Count > 0) sb.AppendLine("Scene variables: " + VarScope.Of(_scene).Describe());
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

            sb.AppendLine();
            sb.AppendLine("Now answer ONLY the user's request below: change just what it asks, edit objects that already exist instead of re-creating them, and keep everything else as it is.");
            return sb.ToString();
        }

        // Applies a behavior/property spec (JsonElement object) to an existing object. Caller handles undo.
        private bool ApplyBehaviorsToObject(GameObjectData target, JsonElement spec, string userPrompt)
        {
            if (target.Locked) { if (!_aiSkippedLocked.Contains(target.Name)) _aiSkippedLocked.Add(target.Name); return false; } // locked: never edited by the AI
            var result = target.Behaviors.Select(JsonClone).ToList();
            bool changed = false;
            var impliedRules = new List<string>();
            double? behaviorBounce = null, behaviorGravity = null; // physics the model wrote inside a behavior
            AiAddedKinds.Clear(); // rules the behaviors stand for (a Custom written as a behavior, a door "on death")

            if (spec.TryGetProperty("behaviors", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var raw in arr.EnumerateArray())
                {
                    if (raw.ValueKind != JsonValueKind.Object || !raw.TryGetProperty("kind", out var kindEl)) continue;
                    var kindOpt = ResolveKind(kindEl.ValueKind == JsonValueKind.String ? kindEl.GetString() : null);
                    if (kindOpt is not BehaviorKind kind || kind == BehaviorKind.Tilemap) continue;
                    if (kind == BehaviorKind.Custom) { AiSpecFix.CustomAsBehavior(raw, impliedRules); continue; }
                    if (LooseJson.Node(raw) is not System.Text.Json.Nodes.JsonObject node) continue; // unreadable: skipped, never a crash
                    AiSpecFix.Behavior(BehaviorCatalog.Info(kind), node, impliedRules);
                    if (node.ContainsKey("drop")) continue;
                    if (node["_bounciness"] is System.Text.Json.Nodes.JsonNode nb && LooseJson.TryParse(nb.ToString(), out var nbv)) behaviorBounce = nbv;
                    if ((node["_gravity"] ?? node["_gravityscale"]) is System.Text.Json.Nodes.JsonNode ng && LooseJson.TryParse(ng.ToString(), out var ngv)) behaviorGravity = ngv;
                    AiAddedKinds.Add(kind);
                    using var fixedDoc = JsonDocument.Parse(node.ToJsonString());
                    var item = fixedDoc.RootElement.Clone();

                    var existing = result.FirstOrDefault(x => x.Kind == kind);
                    var data = existing ?? BehaviorCatalog.Info(kind).CreateData();
                    if (item.TryGetProperty("nums", out var nums) && nums.ValueKind == JsonValueKind.Object)
                        foreach (var p in nums.EnumerateObject())
                            if (LooseJson.TryNumber(p.Value, out var nv)) data.Nums[p.Name] = nv;
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

            // the rules: "custom" (also "rules"/"script", a single rule object, or a JSON string of them) plus the implied ones, cleaned up
            foreach (var key in new[] { "custom", "rules", "script" })
                if (spec.TryGetProperty(key, out var c)) { AiSpecFix.CollectRules(c, impliedRules); break; }
            if (impliedRules.Count > 0)
            {
                using var rulesDoc = JsonDocument.Parse(AiSpecFix.Rules(impliedRules, _aiNotes));
                var cust = rulesDoc.RootElement;
                if (cust.GetArrayLength() > 0)
                {
                    var existing = result.FirstOrDefault(x => x.Kind == BehaviorKind.Custom);
                    var data = existing ?? new BehaviorData { Kind = BehaviorKind.Custom };
                    bool replace = spec.TryGetProperty("replaceCustom", out var rc) && LooseJson.IsTrue(rc);
                    data.Texts["script"] = existing == null || replace ? cust.GetRawText() : MergeRules(existing.Text("script", "[]"), cust);
                    if (existing == null) result.Add(data);
                    changed = true;
                }
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
                    if (VarScope.Of(target).SetFromJson(p.Name, p.Value, out _)) changed = true; // number, true/false (Yes/No) or words (Text)
            if (spec.TryGetProperty("removeVariables", out var orv) && orv.ValueKind == JsonValueKind.Array)
                foreach (var el in orv.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.String && VarScope.Of(target).Remove(el.GetString() ?? "")) changed = true;

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
                            HasK(BehaviorKind.AdvancedPatrol) || HasK(BehaviorKind.Chase) || HasK(BehaviorKind.Projectile) || HasK(BehaviorKind.Shooter);
            var low = userPrompt.ToLowerInvariant();
            bool gravityIntent = new[] { "gravity", "gravid", "fall", "falling", "cair", "queda" }.Any(low.Contains);
            PhysicsMode desired = target.Physics.Mode;
            var (askedMode, bounce, grav) = SpecPhysics(spec);
            bounce ??= behaviorBounce; grav ??= behaviorGravity;
            if (StripCopiedHabits(target, result, userPrompt)) changed = true;
            if (bounce is double bv) { target.Physics.Bounciness = Math.Clamp(bv, 0, 1); changed = true; }
            if (grav is double gv) { target.Physics.GravityScale = Math.Clamp(gv, -5, 5); changed = true; }
            if (HasK(BehaviorKind.Platformer) || gravityIntent || customJumps) desired = PhysicsMode.Dynamic;
            else if (bounce > 0 && (askedMode == PhysicsMode.Dynamic || askedMode == null && !result.Any(b => StaysPut.Contains(b.Kind)))) // a bouncy ball falls and bounces; a bouncy platform stays put
            {
                desired = PhysicsMode.Dynamic;
                if (result.RemoveAll(b => b.Kind == BehaviorKind.Solid) > 0) changed = true;
            }
            else if (HasK(BehaviorKind.Solid) && !hasMover) desired = PhysicsMode.Static;
            // the model's own "physics" is trusted only where it can't pin a HUD/door/pickup to gravity by mistake
            else if ((askedMode == PhysicsMode.Dynamic || bounce > 0) && !result.Any(b => StaysPut.Contains(b.Kind))) desired = PhysicsMode.Dynamic;
            if (desired != target.Physics.Mode) { target.Physics.Mode = desired; changed = true; }

            target.Behaviors = result;
            return changed;
        }

        // x/y/width/height/color
        private bool ApplyBasics(GameObjectData obj, JsonElement spec)
        {
            bool changed = false;
            if (spec.TryGetProperty("x", out var x) && LooseJson.TryNumber(x, out var xv)) { obj.X = xv; changed = true; }
            if (spec.TryGetProperty("y", out var y) && LooseJson.TryNumber(y, out var yv)) { obj.Y = yv; changed = true; }
            if (spec.TryGetProperty("width", out var w) && LooseJson.TryNumber(w, out var wv)) { obj.Width = Math.Max(2, wv); changed = true; }
            if (spec.TryGetProperty("height", out var h) && LooseJson.TryNumber(h, out var hv)) { obj.Height = Math.Max(2, hv); changed = true; }
            if (spec.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String) { obj.Color = c.GetString() ?? obj.Color; changed = true; }
            if (spec.TryGetProperty("sprite", out var sp) && sp.ValueKind == JsonValueKind.String)
            {
                var rel = ResolveImage(sp.GetString() ?? "");
                if (rel != null) { obj.SpritePath = rel; changed = true; }
            }
            if (spec.TryGetProperty("pixelArt", out var pa) && pa.ValueKind == JsonValueKind.Object)
            {
                var rel = RenderPixelArt(pa, obj.Name + "_sprite");
                if (rel != null) { obj.SpritePath = rel; changed = true; }
            }
            // appearance / aesthetics
            if (spec.TryGetProperty("opacity", out var op) && LooseJson.TryNumber(op, out var opv)) { obj.Opacity = Math.Clamp(opv, 0, 1); changed = true; }
            if (spec.TryGetProperty("borderColor", out var bc) && bc.ValueKind == JsonValueKind.String) { obj.BorderColor = bc.GetString(); changed = true; }
            if (spec.TryGetProperty("borderWidth", out var bw) && LooseJson.TryNumber(bw, out var bwv)) { obj.BorderWidth = Math.Max(0, bwv); changed = true; }
            if (spec.TryGetProperty("cornerRadius", out var cr) && LooseJson.TryNumber(cr, out var crv)) { obj.CornerRadius = Math.Max(0, crv); changed = true; }
            if (spec.TryGetProperty("font", out var ff) && ff.ValueKind == JsonValueKind.String) { obj.FontFamily = ff.GetString(); changed = true; }
            if (spec.TryGetProperty("bold", out var bd) && AsBool(bd, out var bdv)) { obj.Bold = bdv; changed = true; }
            if (spec.TryGetProperty("fitHitbox", out var fh) && AsBool(fh, out var fhv)) { obj.Physics.FitHitbox = fhv; changed = true; } // collide with the drawing (HitboxFit.cs)
            if (spec.TryGetProperty("group", out var gr) && gr.ValueKind == JsonValueKind.String) { obj.Group = gr.GetString() ?? ""; changed = true; }
            if (spec.TryGetProperty("z", out var zz) && LooseJson.TryNumber(zz, out var zv)) { obj.Z = (int)Math.Round(zv); changed = true; } // order inside its layer
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
                if (a.TryGetProperty("fps", out var fps) && LooseJson.TryNumber(fps, out var fpsv)) anim.Fps = Math.Clamp(fpsv, 1, 60);
                if (a.TryGetProperty("loop", out var lp) && AsBool(lp, out var lpv)) anim.Loop = lpv;
                if (a.TryGetProperty("frames", out var fr) && fr.ValueKind == JsonValueKind.Array)
                    foreach (var f in fr.EnumerateArray())
                    {
                        if (f.ValueKind == JsonValueKind.String) { var rel = ResolveImage(f.GetString() ?? ""); if (rel != null) anim.Frames.Add(rel); }
                        else if (f.ValueKind == JsonValueKind.Object) { var rel = RenderPixelArt(f, obj.Name + "_" + anim.Name + "_" + anim.Frames.Count); if (rel != null) anim.Frames.Add(rel); }
                    }
                if (anim.Frames.Count == 0) continue;
                obj.Animations.RemoveAll(x => x.Name.Equals(anim.Name, StringComparison.OrdinalIgnoreCase));
                obj.Animations.Add(anim);
                obj.DefaultAnimation ??= anim.Name;
                changed = true;
            }
            return changed;
        }

        // ---- AI-drawn sprites: pixel art described as a palette + rows of characters → PNG ----
        private static bool ParseColorBgra(string s, out (byte b, byte g, byte r, byte a) c)
        {
            c = (0, 0, 0, 0);
            if (string.IsNullOrWhiteSpace(s)) return true; // transparent
            var t = s.Trim().ToLowerInvariant();
            if (t is "." or "none" or "transparent" or "clear" or "empty") return true;
            try { var col = (Color)ColorConverter.ConvertFromString(s.Trim()); c = (col.B, col.G, col.R, 255); return true; }
            catch { return false; }
        }

        private string? RenderPixelArt(JsonElement art, string baseName)
        {
            if (string.IsNullOrEmpty(_project.FolderPath)) return null; // need a saved project to write the PNG
            JsonElement rowsEl;
            if (!(art.TryGetProperty("rows", out rowsEl) && rowsEl.ValueKind == JsonValueKind.Array) &&
                !(art.TryGetProperty("pixels", out rowsEl) && rowsEl.ValueKind == JsonValueKind.Array)) return null;

            var rows = new List<string>();
            foreach (var r in rowsEl.EnumerateArray()) if (r.ValueKind == JsonValueKind.String) rows.Add(r.GetString() ?? "");
            if (rows.Count == 0) return null;

            var pal = new Dictionary<char, (byte b, byte g, byte r, byte a)>();
            if (art.TryGetProperty("palette", out var pj) && pj.ValueKind == JsonValueKind.Object)
                foreach (var p in pj.EnumerateObject())
                {
                    if (p.Name.Length == 0) continue;
                    var val = p.Value.ValueKind == JsonValueKind.String ? (p.Value.GetString() ?? "") : "";
                    if (ParseColorBgra(val, out var col)) pal[p.Name[0]] = col;
                }

            int h = Math.Min(rows.Count, 128);
            int w = Math.Min(128, rows.Max(r => r.Length));
            if (w < 1 || h < 1) return null;
            var data = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                var row = rows[y];
                for (int x = 0; x < w && x < row.Length; x++)
                {
                    char ch = row[x];
                    if (ch is '.' or ' ' or '0') continue; // transparent background
                    if (!pal.TryGetValue(ch, out var col) || col.a == 0) continue;
                    int i = (y * w + x) * 4;
                    data[i] = col.b; data[i + 1] = col.g; data[i + 2] = col.r; data[i + 3] = 255;
                }
            }
            var bmp = System.Windows.Media.Imaging.BitmapSource.Create(w, h, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, data, w * 4);
            try { return ProjectIO.SaveSprite(_project, bmp, baseName); } catch { return null; }
        }

        // Maps the model's loose kind strings to real BehaviorKinds (synonyms) — key to small-model reliability.
        private static BehaviorKind? ResolveKind(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (Enum.TryParse<BehaviorKind>(s, true, out var k)) return k == BehaviorKind.Melee ? BehaviorKind.ToolUse : k; // Melee is legacy
            var t = s.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
            return t switch
            {
                "walk" or "run" or "move" or "movement" or "jump" or "platform" or "platforming" or "sidescroller" or "sidescroll" or "jumping" or "move+jump" => BehaviorKind.Platformer,
                "topdown" or "eightway" or "8way" or "wasd" or "topdownmovement" => BehaviorKind.TopDown,
                "attack" or "sword" or "hit" or "nail" or "slash" or "swing" or "swordattack" or "meleeattack" or "melee" => BehaviorKind.ToolUse,
                "heal" or "hp" or "life" or "lives" or "healthbar" or "hitpoints" => BehaviorKind.Health,
                "hurt" or "damage" or "spike" or "spikes" or "hazard" or "contactdamage" or "trap" or "dmg" => BehaviorKind.Damager,
                "shoot" or "fire" or "gun" or "ranged" or "blaster" or "shooting" => BehaviorKind.Shooter,
                "bullet" or "projectilemove" => BehaviorKind.Projectile,
                "follow" or "camera" or "followcam" or "camerafollow" => BehaviorKind.FollowCamera,
                "pace" or "backandforth" => BehaviorKind.Patrol,
                "advancedpatrol" or "advanced patrol" or "pathpatrol" or "smartpatrol" or "patrolpath" or "drawnpath" => BehaviorKind.AdvancedPatrol,
                "pursue" or "follower" or "chaseplayer" => BehaviorKind.Chase,
                "rotate" or "rotation" or "spinning" => BehaviorKind.Spin,
                "generator" or "emitter" => BehaviorKind.Spawner,
                "audio" or "sfx" or "music" or "sound" => BehaviorKind.SoundEmitter,
                "glow" or "lamp" or "lantern" or "lighting" => BehaviorKind.LightSource,
                "tool" or "useitem" or "swingtool" or "usetool" or "axeuse" or "chop" or "weapon" => BehaviorKind.ToolUse,
                "tree" or "choppable" or "mineable" or "resource" or "harvest" or "ore" => BehaviorKind.Harvestable,
                "objectives" or "tasks" or "testchecklist" or "checks" => BehaviorKind.Checklist,
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

        // the name IS a name for the player ("Hero", "Jogador") — not just contains one ("PlayerHealthBar" is a new thing)
        private static bool NameIsPlayer(string name) => name.Trim().ToLowerInvariant() is "player" or "the player" or "hero" or "jogador" or "personagem" or "character" or "protagonist" or "protagonista" or "pc" or "player1" or "player 1";

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
                if (NameIsPlayer(name)) { var p = FindPlayer(); if (p != null) return p; }
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
            BehaviorKind.Solid, BehaviorKind.Collectible, BehaviorKind.Patrol, BehaviorKind.AdvancedPatrol, BehaviorKind.Chase,
            BehaviorKind.GoToScene, BehaviorKind.Parallax, BehaviorKind.DestroyOffscreen, BehaviorKind.Projectile,
            BehaviorKind.Pushable, BehaviorKind.Switch, BehaviorKind.Gate, BehaviorKind.Interact,
            BehaviorKind.AbilityPickup, BehaviorKind.Area, BehaviorKind.Generator,
            BehaviorKind.PathChase, BehaviorKind.Spring, BehaviorKind.Minimap,
            BehaviorKind.Particles, BehaviorKind.UIButton, BehaviorKind.Bar
        };

        private static readonly string[] ThingNouns =
        {
            "coin","platform","enemy","boss","npc","door","spike","wall","obstacle","bullet","item","powerup",
            "crystal","orb","gem","rock","block","trap","portal","chest","monster","slime","ghost","turret","hazard","pickup","collectible",
            "ball","box","crate","flag","checkpoint","sign","button","trampoline","lever","star","ladder","cloud","tree","label","bar",
            "moeda","plataforma","inimigo","chefe","porta","espinho","parede","bola","caixa","bandeira","chave","placa","botão","botao","estrela","pedra","bloco","armadilha","baú","bau","monstro"
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

        // ---- headless test hooks (used by --aijsontest) ----
        public (bool ok, string summary) TestApplySpec(string reply, string prompt = "") { bool r = ApplyProjectSpec(reply, prompt, out var s); return (r, s); }
        public GameObjectData? TestFindObject(string name) => _scene.Layers.SelectMany(l => l.Objects).FirstOrDefault(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        public GameTheme TestTheme => _project.Theme;
        internal void TestLanguageChanged() => OnLanguageChanged();
        internal IEnumerable<string> TestPanelsMenu() => _dockOrder.Select(k => PanelsMenuHeader(_panes[k]));
        internal IReadOnlyList<string> TestAiSpecObjects => _aiSpecObjects;
        internal void TestSelect(GameObjectData? obj) => Select(obj);
        internal void TestMarkDirty() => Dirty();
        internal bool TestIsDirty => IsDirty;

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
            void SetInto(JsonElement obj, VarScope scope, string label) // a number, true/false (a Yes/No) or words (a Text)
            {
                foreach (var p in obj.EnumerateObject())
                    if (scope.SetFromJson(p.Name, p.Value, out var shown)) parts.Add($"{label} {shown}");
            }
            void RemoveFrom(JsonElement arr, VarScope scope, string label)
            {
                foreach (var e in arr.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String && scope.Remove(e.GetString() ?? "")) parts.Add($"{label} -{e.GetString()}");
            }

            if (root.TryGetProperty("variables", out var v) && v.ValueKind == JsonValueKind.Object)
            {
                if (v.TryGetProperty("global", out var g) && g.ValueKind == JsonValueKind.Object) SetInto(g, VarScope.Of(_project), "global");
                if (v.TryGetProperty("scene", out var s) && s.ValueKind == JsonValueKind.Object) SetInto(s, VarScope.Of(_scene), "scene");
            }
            if (root.TryGetProperty("removeVariables", out var rv) && rv.ValueKind == JsonValueKind.Object)
            {
                if (rv.TryGetProperty("global", out var g) && g.ValueKind == JsonValueKind.Array) RemoveFrom(g, VarScope.Of(_project), "global");
                if (rv.TryGetProperty("scene", out var s) && s.ValueKind == JsonValueKind.Array) RemoveFrom(s, VarScope.Of(_scene), "scene");
            }

            if (parts.Count > 0) { ensureUndo(); changes.Add(L.F("Variables — {0}", string.Join("; ", parts))); }

            // in-game THEME (fonts + dialogue box look)
            if (root.TryGetProperty("theme", out var th) && th.ValueKind == JsonValueKind.Object)
            {
                ensureUndo();
                var t = _project.Theme ??= new GameTheme();
                string S(string k, string d) => th.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.String ? (e.GetString() ?? d) : d;
                double N(string k, double d) => th.TryGetProperty(k, out var e) && LooseJson.TryNumber(e, out var nv) ? nv : d;
                t.FontFamily = S("font", t.FontFamily);
                t.DialogBg = S("dialogBg", t.DialogBg);
                t.DialogBorder = S("dialogBorder", t.DialogBorder);
                t.DialogText = S("dialogText", t.DialogText);
                t.DialogSpeaker = S("dialogSpeaker", t.DialogSpeaker);
                t.DialogEffect = S("dialogEffect", t.DialogEffect);
                t.DialogFontSize = N("dialogFontSize", t.DialogFontSize);
                t.DialogCorner = N("dialogCorner", t.DialogCorner);
                t.DialogSpeed = N("dialogSpeed", t.DialogSpeed);
                changes.Add(L.T("Theme updated"));
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
            if (acts.Count > 0) { ensureUndo(); foreach (var a in acts) a(); changes.Add(L.F("Layers — {0}", string.Join("; ", lch))); }
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
            _aiSpecObjects.Clear(); _aiSkippedLocked.Clear();
            _aiNotes.Clear();
            var json = LooseJson.Extract(reply);
            if (json == null) return false;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(json, LooseJson.Options); } catch { return false; }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return false;

                var changes = new List<string>();
                bool pushed = false;
                void EnsureUndo() { if (!pushed) { PushUndo(); pushed = true; } }
                ApplyDeletes(root, EnsureUndo, changes);

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
                        if (ApplyBehaviorsToObject(obj, spec, userPrompt)) changes.Add(EditedLine(obj));
                    }

                    if (root.TryGetProperty("layers", out var lyScoped) && lyScoped.ValueKind == JsonValueKind.Array)
                        ApplyLayerArray(lyScoped, EnsureUndo, changes);
                    ApplyGlobalSceneVars(root, EnsureUndo, changes);

                    AddLockedNotes(changes);
                    if (changes.Count == 0) return false;
                    summary = SummaryOf(changes);
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
                                if (ApplyBehaviorsToObject(player, spec, userPrompt)) changes.Add(EditedLine(player));
                                continue;
                            }
                        }

                        // don't duplicate: unless it's a prefab or the user clearly wants a NEW/another one, edit the existing object
                        GameObjectData? existing = null;
                        if (!asPrefab && !IsNewIntent(userPrompt))
                        {
                            existing = _scene.Layers.SelectMany(l => l.Objects).FirstOrDefault(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                            // a "create" named like the player (Hero, Jogador…) is the player; any other new name is a NEW thing,
                            // even when the request mentions the player ("an enemy that chases the player")
                            if (existing == null && IsModifyIntent(userPrompt) && NameIsPlayer(name ?? "")) existing = FindPlayer();
                        }

                        EnsureUndo();
                        if (existing != null)
                        {
                            if (ApplyBehaviorsToObject(existing, spec, userPrompt)) changes.Add(EditedLine(existing));
                        }
                        else
                        {
                            var obj = BuildObjectFromSpec(spec, userPrompt);
                            if (obj != null) changes.Add(asPrefab ? PrefabLine(obj) : CreatedLine(obj));
                        }
                    }

                if (hasEdit)
                    foreach (var spec in ed.EnumerateArray())
                    {
                        if (spec.ValueKind != JsonValueKind.Object) continue;
                        var nm = spec.TryGetProperty("name", out var n) ? n.GetString() : null;
                        var obj = ResolveTarget(nm, userPrompt) ?? _selected;
                        if (obj == null)
                        {
                            // an "edit" of something that isn't there yet (a ScoreText the model forgot to create): make it
                            if (!string.IsNullOrWhiteSpace(nm) && (spec.TryGetProperty("behaviors", out _) || spec.TryGetProperty("custom", out _)))
                            { EnsureUndo(); var made = BuildObjectFromSpec(spec, userPrompt); if (made != null) changes.Add(CreatedLine(made)); }
                            continue;
                        }

                        // REDIRECT A: the model tried to put a platform/coin/enemy/door behavior ON the player.
                        // That's almost always wrong → make a NEW object with those behaviors instead.
                        if (ReferenceEquals(obj, FindPlayer()) && SpecHasNonPlayerBehavior(spec))
                        {
                            EnsureUndo();
                            var created = BuildObjectFromSpec(spec, userPrompt, FirstThingNoun(userPrompt) ?? "Object");
                            if (created != null) changes.Add(CreatedLine(created));
                            continue;
                        }

                        EnsureUndo();
                        if (ApplyBehaviorsToObject(obj, spec, userPrompt)) changes.Add(EditedLine(obj));
                    }

                // simple/back-compat: top-level behaviors/custom and no create/edit → selected, player, or a new object
                if (!hasCreate && !hasEdit && (root.TryGetProperty("behaviors", out _) || root.TryGetProperty("custom", out _)))
                {
                    if (_selection.Count == 0 && SpecHasNonPlayerBehavior(root))
                    {
                        // platform/coin/enemy/door behavior with no target → it's a NEW object, not the player
                        EnsureUndo();
                        var created = BuildObjectFromSpec(root, userPrompt, FirstThingNoun(userPrompt) ?? "Object");
                        if (created != null) changes.Add(CreatedLine(created));
                    }
                    else
                    {
                        var obj = _selected ?? FindPlayer();
                        if (obj != null) { EnsureUndo(); if (ApplyBehaviorsToObject(obj, root, userPrompt)) changes.Add(EditedLine(obj)); }
                    }
                }

                if (root.TryGetProperty("layers", out var lyArr) && lyArr.ValueKind == JsonValueKind.Array)
                    ApplyLayerArray(lyArr, EnsureUndo, changes);
                ApplyGlobalSceneVars(root, EnsureUndo, changes);
                ApplyLevel(root, EnsureUndo, changes); // "level": the engine lays out a whole level (MainWindow.AiLevel.cs)

                if (changes.Count > 0)
                {
                    var probe = JsonClone(_scene); // only touch (and undo-snapshot) the scene if the sky really changes
                    if (SkySetup.Ensure(probe) != null) { EnsureUndo(); var sky = SkySetup.Ensure(_scene); if (sky != null) changes.Add(sky); }
                }

                AddLockedNotes(changes); // locked objects the AI wanted to change (MainWindow.LockZ.cs)
                if (changes.Count == 0) return false;
                summary = SummaryOf(changes);
                return true;
            }
        }

        // the lines of the AI's change summary; each also records the object it touched (for Ctrl+K, "show the last AI change")
        private readonly List<string> _aiSpecObjects = new();
        private string EditedLine(GameObjectData o) { _aiSpecObjects.Add(o.Name); return L.F("Edited \"{0}\"", o.Name); }
        private string PrefabLine(GameObjectData o) { _aiSpecObjects.Add(o.Name); return L.F("Prefab \"{0}\"", o.Name); }
        private string CreatedLine(GameObjectData o)
        {
            _aiSpecObjects.Add(o.Name);
            return L.F("Created \"{0}\" [{1}]", o.Name, string.Join(",", o.Behaviors.Select(b => b.Kind)));
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
            if (!L.IsEnglish) sys.AppendLine(L.AiReplyRule);
            return await AiGenerateAsync(userPrompt, sys.ToString(), json: false, temperature: 0.7);
        }

        private Border AddAiMessage(string who, string text, bool fromAi)
        {
            // AI bubbles: dark with light text. The user's: green with DARK text (white/grey on green was hard to read).
            var fg = fromAi ? (SolidColorBrush)FindResource("Text") : (SolidColorBrush)FindResource("AccentText");
            // the body is a read-only TextBox so an answer can be selected and copied
            var body = new TextBox
            {
                Text = text, TextWrapping = TextWrapping.Wrap, IsReadOnly = true, BorderThickness = new Thickness(0), Padding = new Thickness(0),
                Background = Brushes.Transparent, Foreground = fg, FontFamily = new FontFamily("Consolas"), VerticalContentAlignment = VerticalAlignment.Top
            };
            body.PreviewMouseWheel += AiLogWheel; // the TextBox would swallow the wheel and the chat wouldn't scroll
            var bubble = new Border
            {
                Background = fromAi ? (SolidColorBrush)FindResource("Bg2") : (SolidColorBrush)FindResource("Accent"),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = who, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = fg, Opacity = 0.75, Margin = new Thickness(0,0,0,3) },
                        body
                    }
                }
            };
            AiLog.Children.Add(L.Keep(bubble)); // the chat: the person's words and the AI's answers (the speaker is translated by the caller)
            (AiLog.Parent as ScrollViewer)?.ScrollToEnd(); // keep the newest message in view
            return bubble;
        }

        private void AiLogWheel(object sender, MouseWheelEventArgs e)
        {
            if (AiLog.Parent is not ScrollViewer log) return;
            log.ScrollToVerticalOffset(log.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;
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
            var dlg = new OpenFileDialog { Filter = L.F("EasyEngine project (*{0})", ProjectIO.Extension) + $"|*{ProjectIO.Extension}" };
            if (dlg.ShowDialog() == true)
            {
                try { var p = ProjectIO.Load(dlg.FileName); var ed = new MainWindow(p); Application.Current.MainWindow = ed; ed.Show(); Close(); }
                catch (Exception ex) { MessageBox.Show(L.F("Error opening: {0}", ex.Message)); }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try { ProjectIO.Save(_project); _dirty = false; UpdateTitle(); Status(L.F("Saved to {0}", _project.FilePath)); }
            catch (Exception ex) { MessageBox.Show(L.F("Error saving: {0}", ex.Message)); }
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

        private void Settings_Click(object sender, RoutedEventArgs e) => SettingsWindow.Open(this);

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_project.FolderPath)) { MessageBox.Show(L.T("Save the project first.")); return; }
            try { ProjectIO.Save(_project); } catch { }

            var dlg = new OpenFolderDialog { Title = L.T("Choose a folder to export the game into") };
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
            Status(selfContained ? L.T("Exporting standalone game… (can take a minute)") : L.T("Exporting…"));

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

            if (error != null) { Status(L.T("Export failed")); MessageBox.Show(L.F("Export failed:\n{0}", error), L.T("Export"), MessageBoxButton.OK, MessageBoxImage.Error); return; }

            Status(L.F("Exported to {0}", outRoot));
            var note = selfContained
                ? L.T("Self-contained build — the player does NOT need .NET installed. Zip and share the whole folder.")
                : L.T("The target PC needs the free .NET 8 Desktop Runtime (couldn't find the SDK to make a self-contained build).");
            MessageBox.Show(L.F("Game exported!\n\nFolder:\n{0}\n\nDouble-click \"{1}.exe\" to play.\n\n{2}", outRoot, safe, note),
                L.T("Export complete"), MessageBoxButton.OK, MessageBoxImage.Information);
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
            using var p = Process.Start(psi) ?? throw new Exception(L.F("Could not start {0}", file));
            string outp = p.StandardOutput.ReadToEnd();
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) throw new Exception(L.F("Build failed (exit {0}).", p.ExitCode) + "\n" +(string.IsNullOrWhiteSpace(err) ? outp : err));
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
        private void ShowLastAiChange()
        {
            if (string.IsNullOrEmpty(_aiLastSummary))
            {
                Status(L.T("No AI changes yet."));
                return;
            }

            Status(L.F("Last AI change: {0}", _aiLastSummary.Replace("\n", " | ")));

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

            MessageBox.Show(_aiLastSummary, L.T("Last AI Change"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ScrollToObject(GameObjectData obj)
        {
            double cx = (obj.X + obj.Width / 2.0) * _zoom;
            double cy = (obj.Y + obj.Height / 2.0) * _zoom;
            SceneScroll.ScrollToHorizontalOffset(cx - SceneScroll.ViewportWidth / 2);
            SceneScroll.ScrollToVerticalOffset(cy - SceneScroll.ViewportHeight / 2);
        }

        // unsaved changes (a flag, not read back from the window title — the title is shown text)
        private bool _dirty;
        private bool IsDirty => _dirty;

        private void Dirty() { if (!_restoring) { _dirty = true; UpdateTitle(); } }
        private void UpdateTitle() => Title = $"EasyEngine — {_project.Name} · {_scene.Name}" + (_dirty ? " *" : "");
        private void Status(string msg) => StatusText.Text = msg;

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (IsDirty && IsVisible) // an editor that was never shown (tests, headless runs) has no one to ask
            {
                var result = MessageBox.Show(
                    L.T("You have unsaved changes. Do you want to save before closing?"),
                    "EasyEngine",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    try { ProjectIO.Save(_project); }
                    catch (Exception ex) { MessageBox.Show(L.F("Error saving: {0}", ex.Message)); e.Cancel = true; return; }
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
            }
            base.OnClosing(e);
            if (!e.Cancel) _closing = true;
        }

        private bool _closing; // the editor is closing: its floating panels close with it and keep their place in the layout
    }
}
