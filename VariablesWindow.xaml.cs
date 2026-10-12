using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyEngine
{
    public partial class VariablesWindow : Window
    {
        private readonly Project _project;
        private readonly Scene _scene;
        private readonly GameObjectData? _obj;
        public bool Changed { get; private set; }

        public VariablesWindow(Project project, Scene scene, GameObjectData? selected)
        {
            InitializeComponent();
            _project = project;
            _scene = scene;
            _obj = selected;
            Build();
        }

        private SolidColorBrush B(string key) => (SolidColorBrush)FindResource(key);

        // the kind picker: shown in the editor's language, saved by its English id
        internal static List<(string value, string label)> KindOptions() => new() { ("number", L.T("Number")), ("yesno", L.T("Yes/No")), ("text", L.T("Text")) };
        internal static string KindId(VarKind k) => k switch { VarKind.YesNo => "yesno", VarKind.Text => "text", _ => "number" };
        internal static VarKind KindFrom(string id) => id switch { "yesno" => VarKind.YesNo, "text" => VarKind.Text, _ => VarKind.Number };

        private void Build()
        {
            Root.Children.Clear();
            Section(L.T("Global"), L.T("Shared across the whole game (score, lives…). Persists between scenes."), VarScope.Of(_project), null);
            Section(L.T("Local (scene)"), L.T("Belongs to this scene. Reset every time the scene loads."), VarScope.Of(_scene), null);
            Section(L.T("Object"),
                _obj != null ? L.F("Belongs to \"{0}\". Each copy of it in the game gets its own.", _obj.Name) : L.T("Belongs to the selected object."),
                _obj != null ? VarScope.Of(_obj) : null,
                L.T("Select an object in the scene to add its variables."));
        }

        private void Section(string title, string desc, VarScope? scope, string? emptyNote)
        {
            Root.Children.Add(new TextBlock { Text = title.ToUpper(), Foreground = B("Accent"), FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 2) });
            Root.Children.Add(new TextBlock { Text = desc, Foreground = B("TextDim"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

            if (scope is not VarScope s)
            {
                Root.Children.Add(new TextBlock { Text = emptyNote ?? "", Foreground = B("TextDim"), FontSize = 12, FontStyle = FontStyles.Italic, Margin = new Thickness(0, 0, 0, 6) });
                return;
            }

            var names = s.Names();
            if (names.Count == 0)
                Root.Children.Add(new TextBlock { Text = L.T("No variables yet."), Foreground = B("TextDim"), FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });

            foreach (var name in names)
                Root.Children.Add(VarRow(s, name));

            Root.Children.Add(AddRow(s));
        }

        // name | kind | value | ✕
        private static Grid RowGrid(Thickness margin)
        {
            var grid = new Grid { Margin = margin };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) }); // ✕
            return grid;
        }

        private static void Put(Grid g, UIElement el, int col, int span = 1) { Grid.SetColumn(el, col); Grid.SetColumnSpan(el, span); g.Children.Add(el); }

        private FrameworkElement VarRow(VarScope scope, string name)
        {
            var grid = RowGrid(new Thickness(0, 0, 0, 5));
            var kind = scope.KindOf(name);

            var lbl = L.Keep(new TextBlock { Text = name, Foreground = B("Text"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }); // the variable's name

            // its kind: changing it converts the value (VarScope.ChangeKind)
            var kindBox = Dropdown.Make(KindOptions(), KindId(kind), v =>
            {
                scope.ChangeKind(name, KindFrom(v)); Changed = true;
                Dispatcher.BeginInvoke(new Action(Build)); // rebuilt once the dropdown has closed
            });
            kindBox.Height = 28; kindBox.Margin = new Thickness(6, 0, 0, 0);

            FrameworkElement val;
            switch (kind)
            {
                case VarKind.YesNo:
                {
                    var chk = new CheckBox { Content = L.T("Yes"), IsChecked = scope.YesNo(name), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Foreground = B("Text") };
                    chk.Checked += (s, e) => { scope.SetYesNo(name, true); Changed = true; };
                    chk.Unchecked += (s, e) => { scope.SetYesNo(name, false); Changed = true; };
                    val = chk;
                    break;
                }
                case VarKind.Text:
                {
                    var tb = L.Keep(new TextBox { Text = scope.Text(name), Height = 28, Margin = new Thickness(6, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center }); // the person's own words
                    tb.TextChanged += (s, e) => { scope.SetText(name, tb.Text); Changed = true; };
                    val = tb;
                    break;
                }
                default:
                {
                    var tb = new TextBox { Text = scope.Number(name).ToString("0.###", CultureInfo.InvariantCulture), Height = 28, Margin = new Thickness(6, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
                    void Commit() { if (LooseJson.TryParse(tb.Text, out var v)) { scope.SetNumber(name, v); Changed = true; } }
                    tb.LostFocus += (s, e) => Commit();
                    tb.KeyDown += (s, e) => { if (e.Key == Key.Enter) Commit(); };
                    val = tb;
                    break;
                }
            }

            var del = new Button { Content = "✕", Width = 28, Height = 28, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0), Style = (Style)FindResource("GhostButton"), Foreground = B("Danger") };
            del.Click += (s, e) => { scope.Remove(name); Changed = true; Build(); };

            Put(grid, lbl, 0); Put(grid, kindBox, 1); Put(grid, val, 2); Put(grid, del, 3);
            return grid;
        }

        private FrameworkElement AddRow(VarScope scope)
        {
            var grid = RowGrid(new Thickness(0, 4, 0, 0));

            var nameBox = new TextBox { Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
            string kindId = "number";
            var kindBox = Dropdown.Make(KindOptions(), kindId, v => kindId = v);
            kindBox.Height = 28; kindBox.Margin = new Thickness(6, 0, 0, 0);
            var add = new Button { Content = L.T("Add"), MinWidth = 56, Padding = new Thickness(8, 0, 8, 0), Height = 28, Margin = new Thickness(6, 0, 0, 0), Style = (Style)FindResource("CardButton"), HorizontalContentAlignment = HorizontalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };

            void DoAdd()
            {
                var n = nameBox.Text.Trim();
                if (string.IsNullOrEmpty(n)) return;
                if (scope.Has(n)) { MessageBox.Show(L.T("A variable with that name already exists.")); return; }
                switch (KindFrom(kindId))
                {
                    case VarKind.YesNo: scope.SetYesNo(n, false); break;
                    case VarKind.Text: scope.SetText(n, ""); break;
                    default: scope.SetNumber(n, 0); break;
                }
                Changed = true; Build();
            }
            add.Click += (s, e) => DoAdd();
            nameBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) DoAdd(); };

            // placeholder overlay
            var hint = new TextBlock { Text = L.T("new variable name"), Foreground = B("TextDim"), FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            var namePanel = new Grid();
            namePanel.Children.Add(nameBox);
            namePanel.Children.Add(hint);
            nameBox.TextChanged += (s, e) => hint.Visibility = string.IsNullOrEmpty(nameBox.Text) ? Visibility.Visible : Visibility.Collapsed;

            Put(grid, namePanel, 0); Put(grid, kindBox, 1); Put(grid, add, 2);
            return grid;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
