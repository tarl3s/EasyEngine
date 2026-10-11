using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace EasyEngine
{
    /// <summary>
    /// Unity-style console panel for play mode: level toggles with counts, category chips, search,
    /// merged (×N) entries, a detail pane for the selected line, Clear / Copy / auto-scroll.
    /// It mirrors a <see cref="DebugLog"/>; call <see cref="Flush"/> once per frame while it is visible.
    /// </summary>
    public class DebugConsoleView : Border
    {
        private readonly DebugLog _log;
        private readonly ObservableCollection<LogEntry> _items = new();
        private readonly ICollectionView _view;
        private readonly ListBox _list;
        private readonly TextBox _search, _detail;
        private readonly ToggleButton _info, _warn, _error, _autoScroll;
        private readonly WrapPanel _catPanel;
        private readonly HashSet<string> _hiddenCats = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ToggleButton> _catChips = new(StringComparer.OrdinalIgnoreCase);
        private long _syncedSeq;

        public event Action? CloseRequested;
        public event Action? FocusGameRequested;

        private static readonly Brush PanelBg = Br("#15161C"), BarBg = Br("#1C1D25"), Edge = Br("#2C2F3A"), Text = Br("#D7DAE3"), Dim = Br("#8E95A8");
        private static Brush Br(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }

        public DebugConsoleView(DebugLog log)
        {
            _log = log;
            Background = PanelBg; BorderBrush = Edge; BorderThickness = new Thickness(1, 0, 0, 0);

            var root = new DockPanel();

            // ---- toolbar: title + actions ----
            var bar1 = new DockPanel { Background = BarBg, LastChildFill = false };
            bar1.Children.Add(Docked(new TextBlock { Text = "Console", Foreground = Text, FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(10, 7, 8, 7) }, Dock.Left));
            var close = Btn("✕", "Hide the console (F1)"); close.Click += (s, e) => CloseRequested?.Invoke();
            bar1.Children.Add(Docked(close, Dock.Right));
            _autoScroll = Toggle("⤓ Auto-scroll", true, "Keep the newest line in view");
            bar1.Children.Add(Docked(_autoScroll, Dock.Right));
            var copy = Btn("Copy", "Copy the visible lines"); copy.Click += (s, e) => CopyVisible();
            bar1.Children.Add(Docked(copy, Dock.Right));
            var clear = Btn("Clear", "Clear the console"); clear.Click += (s, e) => _log.Clear();
            bar1.Children.Add(Docked(clear, Dock.Right));
            root.Children.Add(Docked(bar1, Dock.Top));

            // ---- level toggles + search ----
            var bar2 = new DockPanel { Background = BarBg, Margin = new Thickness(0, 0, 0, 1) };
            _info = Toggle("ⓘ 0", true, "Show info messages");
            _warn = Toggle("⚠ 0", true, "Show warnings");
            _error = Toggle("⛔ 0", true, "Show errors");
            foreach (var t in new[] { _info, _warn, _error }) { t.Checked += (s, e) => _view?.Refresh(); t.Unchecked += (s, e) => _view?.Refresh(); bar2.Children.Add(Docked(t, Dock.Left)); }
            _search = new TextBox { Margin = new Thickness(6, 5, 8, 5), Background = PanelBg, Foreground = Text, BorderBrush = Edge, CaretBrush = Text, ToolTip = "Search (Esc returns to the game)", VerticalContentAlignment = VerticalAlignment.Center };
            _search.TextChanged += (s, e) => _view?.Refresh();
            bar2.Children.Add(_search);
            root.Children.Add(Docked(bar2, Dock.Top));

            // ---- category chips (appear as categories show up) ----
            _catPanel = new WrapPanel { Background = BarBg, Margin = new Thickness(0, 0, 0, 1) };
            root.Children.Add(Docked(_catPanel, Dock.Top));

            // ---- detail of the selected line ----
            _detail = new TextBox
            {
                IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 110, Background = BarBg, Foreground = Text, BorderBrush = Edge, BorderThickness = new Thickness(0, 1, 0, 0),
                FontFamily = new FontFamily("Consolas"), FontSize = 11.5, Padding = new Thickness(8, 6, 8, 6), Text = "Select a line to see its details."
            };
            root.Children.Add(Docked(_detail, Dock.Bottom));

            // ---- the list ----
            _list = new ListBox
            {
                Background = PanelBg, BorderThickness = new Thickness(0), Foreground = Text,
                FontFamily = new FontFamily("Consolas"), FontSize = 11.5,
                ItemTemplate = (DataTemplate)XamlReader.Parse(RowTemplate),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
            _list.SelectionChanged += (s, e) =>
            {
                if (_list.SelectedItem is LogEntry le) _detail.Text = le.FullText();
                // give the keyboard straight back to the game so A/D/Space keep working
                Dispatcher.BeginInvoke(new Action(() => FocusGameRequested?.Invoke()), System.Windows.Threading.DispatcherPriority.Input);
            };
            root.Children.Add(_list);

            _view = CollectionViewSource.GetDefaultView(_items);
            _view.Filter = Passes;
            _list.ItemsSource = _view;

            _log.Cleared += () => { _items.Clear(); _syncedSeq = _log.LastSeq; _detail.Text = ""; RefreshCounts(); };
            Child = root;
        }

        private const string RowTemplate =
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
            "<Grid Margin='0,1,0,1'>" +
            "<Grid.ColumnDefinitions><ColumnDefinition Width='16'/><ColumnDefinition Width='54'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>" +
            "<TextBlock Text='{Binding LevelIcon}' Foreground='{Binding LevelBrush}'/>" +
            "<TextBlock Grid.Column='1' Text='{Binding TimeText}' Foreground='#7E8496'/>" +
            "<TextBlock Grid.Column='2' Text='{Binding Line}' Foreground='{Binding LineBrush}' TextWrapping='Wrap'/>" +
            "<TextBlock Grid.Column='3' Text='{Binding CountText}' Foreground='#18B98A' FontWeight='Bold' Margin='6,0,2,0'/>" +
            "</Grid></DataTemplate>";

        /// <summary>Pull new entries from the log (call once per frame while visible).</summary>
        public void Flush()
        {
            var all = _log.Entries;
            int start = all.Count;
            while (start > 0 && all[start - 1].Seq > _syncedSeq) start--;
            bool added = false;
            for (int i = start; i < all.Count; i++)
            {
                var e = all[i];
                _items.Add(e); _syncedSeq = e.Seq; added = true;
                if (!_catChips.ContainsKey(e.Category)) AddCategoryChip(e.Category);
            }
            while (_items.Count > DebugLog.Capacity) _items.RemoveAt(0);
            RefreshCounts();
            if (added && _autoScroll.IsChecked == true && _list.Items.Count > 0)
                _list.ScrollIntoView(_list.Items[_list.Items.Count - 1]);
        }

        private bool Passes(object o)
        {
            if (o is not LogEntry e) return false;
            if (e.Level == LogLevel.Info && _info.IsChecked != true) return false;
            if (e.Level == LogLevel.Warn && _warn.IsChecked != true) return false;
            if (e.Level == LogLevel.Error && _error.IsChecked != true) return false;
            if (_hiddenCats.Contains(e.Category)) return false;
            var q = _search.Text.Trim();
            return q.Length == 0 || e.Line.Contains(q, StringComparison.OrdinalIgnoreCase) || e.Details.Contains(q, StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshCounts()
        {
            _info.Content = $"ⓘ {_log.Infos}";
            _warn.Content = $"⚠ {_log.Warns}";
            _error.Content = $"⛔ {_log.Errors}";
        }

        private void AddCategoryChip(string cat)
        {
            var chip = Toggle(cat, true, $"Show/hide [{cat}] lines");
            chip.FontSize = 10.5; chip.Padding = new Thickness(7, 1, 7, 1); chip.Margin = new Thickness(4, 4, 0, 4);
            chip.Checked += (s, e) => { _hiddenCats.Remove(cat); _view.Refresh(); };
            chip.Unchecked += (s, e) => { _hiddenCats.Add(cat); _view.Refresh(); };
            _catChips[cat] = chip;
            _catPanel.Children.Add(chip);
        }

        private void CopyVisible()
        {
            var lines = _view.Cast<LogEntry>().Select(e => $"{e.TimeText} {e.Line}{(e.Count > 1 ? " ×" + e.Count : "")}");
            try { Clipboard.SetText(string.Join(Environment.NewLine, lines)); } catch { }
        }

        public bool SearchHasFocus => _search.IsKeyboardFocusWithin;

        // test hooks (headless filter checks)
        public void TestSearch(string q) => _search.Text = q;
        public void TestShowLevel(LogLevel level, bool on) => (level switch { LogLevel.Warn => _warn, LogLevel.Error => _error, _ => _info }).IsChecked = on;
        public int TestVisibleCount => _view.Cast<object>().Count();

        // ---- small widgets (never take keyboard focus, so Space/Enter keep driving the game) ----
        private static FrameworkElement Docked(FrameworkElement el, Dock d) { DockPanel.SetDock(el, d); return el; }

        private static Button Btn(string text, string tip) => new()
        {
            Content = text, ToolTip = tip, Focusable = false, Margin = new Thickness(0, 5, 6, 5), Padding = new Thickness(9, 2, 9, 2),
            Background = Br("#262833"), Foreground = Text, BorderBrush = Edge, Cursor = System.Windows.Input.Cursors.Hand
        };

        private static ToggleButton Toggle(string text, bool on, string tip)
        {
            var t = new ToggleButton
            {
                Content = text, IsChecked = on, ToolTip = tip, Focusable = false, Margin = new Thickness(6, 5, 0, 5), Padding = new Thickness(8, 2, 8, 2),
                Background = Br("#262833"), Foreground = Text, BorderBrush = Edge, Cursor = System.Windows.Input.Cursors.Hand
            };
            void Look() => t.Opacity = t.IsChecked == true ? 1.0 : 0.45;
            t.Checked += (s, e) => Look(); t.Unchecked += (s, e) => Look(); Look();
            return t;
        }
    }
}
