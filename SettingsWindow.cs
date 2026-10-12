using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyEngine
{
    /// <summary>App-wide settings (⚙ in the launcher and the editor): language, theme, interface size, update check and
    /// clearing EasyEngine's temporary files. Every change applies at once and is saved in settings.json.</summary>
    public class SettingsWindow : Window
    {
        private readonly TextBlock _tempInfo = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        private readonly Button _clear = new() { Content = "🧹  Clear temporary files", Height = 34, Padding = new Thickness(14, 0, 14, 0), HorizontalAlignment = HorizontalAlignment.Left };

        public SettingsWindow()
        {
            Title = "Settings"; Width = 480; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; WindowStyle = WindowStyle.ToolWindow;
            Background = Theme.Brush("Bg0", "#141418"); FontFamily = new FontFamily("Segoe UI");

            var root = new StackPanel { Margin = new Thickness(22, 18, 22, 20) };

            // language: shown in its own name, never translated
            var langs = Dropdown.Make(L.All.Select(l => (l.Code, l.Native)), L.Code, code =>
            {
                AiSettings.Language = code; AiSettings.Save();
                L.SetLanguage(code);
                RefreshTempInfo();
            });
            L.SetSkip(langs, true);
            root.Children.Add(Row("🌐  Language", langs, "The editor's language. The AI also answers you in it. Your games keep their own texts."));

            // the theme is set when EasyEngine starts (see Theme.Init): offer to restart right away
            var restart = new Button { Content = "↻  Restart now", Height = 30, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            var restartNote = new TextBlock { Text = "The new theme is used when EasyEngine starts again.", FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Brush("TextDim", "#8C8C97"), Margin = new Thickness(0, 6, 0, 0) };
            var restartPanel = new StackPanel { Visibility = Visibility.Collapsed };
            restartPanel.Children.Add(restartNote); restartPanel.Children.Add(restart);
            restart.Click += (s, e) => App.Restart();
            var theme = Dropdown.Make(new[] { ("dark", "Dark"), ("light", "Light") }, AiSettings.ThemeName, name =>
            {
                AiSettings.ThemeName = name; AiSettings.Save();
                restartPanel.Visibility = name == Theme.Current ? Visibility.Collapsed : Visibility.Visible;
            });
            var themeBox = new StackPanel();
            themeBox.Children.Add(theme); themeBox.Children.Add(restartPanel);
            root.Children.Add(Row("🎨  Theme", themeBox, null));

            var scales = new[] { 0.9, 1.0, 1.1, 1.25 };
            var size = Dropdown.Make(scales.Select(s => (Key(s), $"{s * 100:0}%")), Key(AiSettings.UiScale), v =>
            {
                AiSettings.UiScale = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture); AiSettings.Save();
                App.ApplyUiScale();
            });
            root.Children.Add(Row("🔍  Interface size", size, "Makes text and buttons bigger or smaller in every window."));

            var updates = new CheckBox { Content = "Look for a new version when EasyEngine starts", IsChecked = AiSettings.CheckUpdates, Margin = new Thickness(0, 4, 0, 0) };
            updates.Checked += (s, e) => { AiSettings.CheckUpdates = true; AiSettings.Save(); };
            updates.Unchecked += (s, e) => { AiSettings.CheckUpdates = false; AiSettings.Save(); };
            root.Children.Add(Row("⬆  Updates", updates, null));

            _clear.Click += (s, e) =>
            {
                var (count, bytes) = CacheCleaner.Clean();
                RefreshTempInfo();
                _tempInfo.Text = (count == 0 ? L.T("Nothing to clear.") : L.F("Removed {0} items ({1}).", count, CacheCleaner.Pretty(bytes))) + " " +
                                 L.T("Your projects and settings were not touched.");
            };
            var tempPanel = new StackPanel();
            tempPanel.Children.Add(_tempInfo);
            tempPanel.Children.Add(_clear);
            root.Children.Add(Row("🗂  Temporary files", tempPanel, null));
            RefreshTempInfo();

            var close = new Button { Content = "Close", Style = Theme.Style("AccentButton"), Width = 110, Height = 36, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), IsDefault = true, IsCancel = true };
            close.Click += (s, e) => Close();
            root.Children.Add(close);

            Content = root;
        }

        private static string Key(double s) => s.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        private void RefreshTempInfo()
        {
            var targets = CacheCleaner.Targets();
            _tempInfo.Text = L.F("EasyEngine's logs, test projects and finished downloads in the Windows temp folder: {0}.", CacheCleaner.Pretty(CacheCleaner.Size(targets)));
            _clear.IsEnabled = targets.Count > 0;
        }

        private static FrameworkElement Row(string title, FrameworkElement control, string? hint)
        {
            var p = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            p.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            p.Children.Add(control);
            if (hint != null)
                p.Children.Add(new TextBlock { Text = hint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Brush("TextDim", "#8C8C97"), Margin = new Thickness(0, 5, 0, 0) });
            return p;
        }

        /// <summary>Opens the settings over <paramref name="owner"/>.</summary>
        public static void Open(Window owner) => new SettingsWindow { Owner = owner }.ShowDialog();
    }
}
