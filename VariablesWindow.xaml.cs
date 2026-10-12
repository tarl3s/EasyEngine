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

        private void Build()
        {
            Root.Children.Clear();
            Section(L.T("Global"), L.T("Shared across the whole game (score, lives…). Persists between scenes."), _project.Variables, null);
            Section(L.T("Local (scene)"), L.T("Belongs to this scene. Reset every time the scene loads."), _scene.Variables, null);
            Section(L.T("Object"),
                _obj != null ? L.F("Belongs to \"{0}\". Each copy of it in the game gets its own.", _obj.Name) : L.T("Belongs to the selected object."),
                _obj?.Variables,
                L.T("Select an object in the scene to add its variables."));
        }

        private void Section(string title, string desc, Dictionary<string, double>? dict, string? emptyNote)
        {
            Root.Children.Add(new TextBlock { Text = title.ToUpper(), Foreground = B("Accent"), FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 2) });
            Root.Children.Add(new TextBlock { Text = desc, Foreground = B("TextDim"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

            if (dict == null)
            {
                Root.Children.Add(new TextBlock { Text = emptyNote ?? "", Foreground = B("TextDim"), FontSize = 12, FontStyle = FontStyles.Italic, Margin = new Thickness(0, 0, 0, 6) });
                return;
            }

            if (dict.Count == 0)
                Root.Children.Add(new TextBlock { Text = L.T("No variables yet."), Foreground = B("TextDim"), FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });

            foreach (var kv in dict.OrderBy(k => k.Key).ToList())
                Root.Children.Add(VarRow(dict, kv.Key, kv.Value));

            Root.Children.Add(AddRow(dict));
        }

        private FrameworkElement VarRow(Dictionary<string, double> dict, string name, double value)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lbl = L.Keep(new TextBlock { Text = name, Foreground = B("Text"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center }); // the variable's name
            var val = new TextBox { Text = value.ToString("0.###", CultureInfo.InvariantCulture), Height = 28, Margin = new Thickness(6, 0, 0, 0) };
            void Commit() { if (double.TryParse(val.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) { dict[name] = v; Changed = true; } }
            val.LostFocus += (s, e) => Commit();
            val.KeyDown += (s, e) => { if (e.Key == Key.Enter) Commit(); };

            var del = new Button { Content = "✕", Width = 28, Height = 28, Margin = new Thickness(6, 0, 0, 0), Style = (Style)FindResource("GhostButton"), Foreground = B("Danger") };
            del.Click += (s, e) => { dict.Remove(name); Changed = true; Build(); };

            Grid.SetColumn(lbl, 0); Grid.SetColumn(val, 1); Grid.SetColumn(del, 2);
            grid.Children.Add(lbl); grid.Children.Add(val); grid.Children.Add(del);
            return grid;
        }

        private FrameworkElement AddRow(Dictionary<string, double> dict)
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameBox = new TextBox { Height = 28 };
            nameBox.Tag = "name";
            var valBox = new TextBox { Text = "0", Height = 28, Margin = new Thickness(6, 0, 0, 0) };
            var add = new Button { Content = L.T("Add"), Width = 56, Height = 28, Margin = new Thickness(6, 0, 0, 0), Style = (Style)FindResource("CardButton"), HorizontalContentAlignment = HorizontalAlignment.Center };

            void DoAdd()
            {
                var n = nameBox.Text.Trim();
                if (string.IsNullOrEmpty(n)) return;
                if (dict.ContainsKey(n)) { MessageBox.Show(L.T("A variable with that name already exists.")); return; }
                double v = 0; double.TryParse(valBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out v);
                dict[n] = v; Changed = true; Build();
            }
            add.Click += (s, e) => DoAdd();
            nameBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) DoAdd(); };
            valBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) DoAdd(); };

            // placeholder hint
            nameBox.GotFocus += (s, e) => { if (nameBox.Text == "") { } };
            nameBox.Text = "";
            var hint = new TextBlock { Text = L.T("new variable name"), Foreground = B("TextDim"), FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            // simple placeholder overlay
            var namePanel = new Grid();
            namePanel.Children.Add(nameBox);
            namePanel.Children.Add(hint);
            nameBox.TextChanged += (s, e) => hint.Visibility = string.IsNullOrEmpty(nameBox.Text) ? Visibility.Visible : Visibility.Collapsed;

            Grid.SetColumn(namePanel, 0); Grid.SetColumn(valBox, 1); Grid.SetColumn(add, 2);
            grid.Children.Add(namePanel); grid.Children.Add(valBox); grid.Children.Add(add);
            return grid;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
