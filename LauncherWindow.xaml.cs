using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace EasyEngine
{
    public partial class LauncherWindow : Window
    {
        public LauncherWindow()
        {
            InitializeComponent();
            ShowVersion();
            L.SetSkip(RecentList, true); // the person's projects (names, folders)
            SampleProjects.EnsureInstalled(); // the projects that come with the engine (SampleProjects.cs)
            LoadRecent();
            NewLocation.Text = DefaultParentFolder();
            _ = CheckOllamaAsync();
            Loaded += async (s, e) => await CheckForUpdateAsync();
            L.Changed += OnLanguageChanged;
            Closed += (s, e) => L.Changed -= OnLanguageChanged;
        }

        // the texts the code sets (the rest of the window re-translates itself)
        private void OnLanguageChanged()
        {
            ShowVersion();
            ShowOllamaState();
            LoadRecent(); // the cards' tooltips
        }

        private void ShowVersion()
        {
            VersionText.Text = "v" + Updater.Current.ToString(3);
            if (Environment.GetCommandLineArgs().Contains("--updated")) VersionText.Text += "  " + L.T("— updated ✓");
        }

        // the cards come from a template (not translated by themselves)
        private void RenameButton_Loaded(object sender, RoutedEventArgs e) => ((FrameworkElement)sender).ToolTip = L.T("Rename");
        private void DeleteButton_Loaded(object sender, RoutedEventArgs e) => ((FrameworkElement)sender).ToolTip = L.T("Delete / remove");

        // ---------------- updates from GitHub (Updater.cs) ----------------
        private async System.Threading.Tasks.Task CheckForUpdateAsync()
        {
            if (Updater.IsDevBuild || ProjectIO.SuppressRecent || !AiSettings.CheckUpdates) return; // run from the code (or a test): the code is the update
            var r = await Updater.CheckAsync();
            if (r == null || !IsVisible) return;
            var notes = string.Join("\n", r.Notes.Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0).Take(10));
            var ask = MessageBox.Show(this,
                L.F("A new version of EasyEngine is out: v{0} (you have v{1}).", r.Version.ToString(3), Updater.Current.ToString(3)) + "\n\n" +
                (notes.Length > 0 ? notes + "\n\n" : "") +
                L.T("Install it now? EasyEngine will download it, close, replace the old version with the new one and open again. Your projects are not touched."),
                L.T("Update EasyEngine"), MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (ask == MessageBoxResult.Yes) await InstallUpdateAsync(r);
        }

        private async System.Threading.Tasks.Task InstallUpdateAsync(Updater.Release r)
        {
            var bar = new System.Windows.Controls.ProgressBar { Height = 14, Maximum = 1, Margin = new Thickness(0, 10, 0, 0) };
            var text = new System.Windows.Controls.TextBlock { Text = L.F("Downloading EasyEngine v{0}…", r.Version.ToString(3)), Foreground = (System.Windows.Media.Brush)FindResource("Text") };
            var win = new Window
            {
                Title = L.T("Updating EasyEngine"), Owner = this, Width = 380, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (System.Windows.Media.Brush)FindResource("Bg1"),
                Content = new System.Windows.Controls.StackPanel { Margin = new Thickness(18), Children = { text, bar } },
            };
            win.Show();
            try
            {
                var dir = await Updater.DownloadAsync(r, new Progress<double>(p => bar.Value = p));
                text.Text = L.T("Installing… EasyEngine will open again in a moment.");
                Updater.StartSwap(dir, Updater.InstallDir, Environment.ProcessId);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                win.Close();
                MessageBox.Show(this, L.T("The update couldn't be downloaded:") + "\n" + ex.Message + "\n\n" + L.T("You can keep using this version."),
                                L.T("Update EasyEngine"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static string DefaultParentFolder()
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var games = Path.Combine(desktop, "Jogos");
            return Directory.Exists(games) ? games : desktop;
        }

        private void LoadRecent()
        {
            var recent = ProjectIO.GetRecent();
            RecentList.ItemsSource = recent;
            NoRecent.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private OllamaManager.State? _ollama; // null while it is being checked

        private async System.Threading.Tasks.Task CheckOllamaAsync()
        {
            _ollama = await OllamaManager.EnsureRunningAsync();
            ShowOllamaState();
        }

        private void ShowOllamaState()
        {
            if (_ollama is not { } state) return;
            OllamaStatus.Text = state switch
            {
                OllamaManager.State.Running      => L.T("AI: Ollama connected ✓"),
                OllamaManager.State.Started      => L.T("AI: Ollama started ✓"),
                OllamaManager.State.NotInstalled => L.T("AI: Ollama not installed (get it at ollama.com)"),
                _                                => L.T("AI: Ollama did not respond"),
            };
        }

        // ---------------- new project ----------------
        private void NewProject_Click(object sender, RoutedEventArgs e)
        {
            RecentPanel.Visibility = Visibility.Collapsed;
            NewForm.Visibility = Visibility.Visible;
            NewName.Focus();
            NewName.SelectAll();
        }

        private void CancelNew_Click(object sender, RoutedEventArgs e)
        {
            NewForm.Visibility = Visibility.Collapsed;
            RecentPanel.Visibility = Visibility.Visible;
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = L.T("Where to save the project") };
            if (Directory.Exists(NewLocation.Text)) dlg.InitialDirectory = NewLocation.Text;
            if (dlg.ShowDialog() == true) NewLocation.Text = dlg.FolderName;
        }

        private void CreateNew_Click(object sender, RoutedEventArgs e)
        {
            var name = NewName.Text.Trim();
            if (string.IsNullOrEmpty(name)) { MessageBox.Show(L.T("Give the project a name.")); return; }
            var parent = NewLocation.Text;
            if (!Directory.Exists(parent)) { MessageBox.Show(L.T("Invalid destination folder.")); return; }
            try { OpenEditor(ProjectIO.CreateNew(name, parent)); }
            catch (Exception ex) { MessageBox.Show(L.T("Create failed:") + " " + ex.Message); }
        }

        private void Settings_Click(object sender, RoutedEventArgs e) => SettingsWindow.Open(this);

        // ---------------- open ----------------
        private void OpenProject_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = L.T("Open project"),
                Filter = L.T("EasyEngine project") + $" (*{ProjectIO.Extension})|*{ProjectIO.Extension}"
            };
            if (dlg.ShowDialog() == true) OpenPath(dlg.FileName);
        }

        private void RecentList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (RecentList.SelectedItem is RecentProject r) OpenPath(r.Path);
        }

        private void OpenPath(string path)
        {
            if (!File.Exists(path)) { MessageBox.Show(L.T("That project file no longer exists.")); ProjectIO.RemoveRecent(path); LoadRecent(); return; }
            try { OpenEditor(ProjectIO.Load(path)); }
            catch (Exception ex) { MessageBox.Show(L.T("Open failed:") + " " + ex.Message); }
        }

        // ---------------- rename ----------------
        private void RenameRecent_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not RecentProject r) return;
            var name = InputDialog.Prompt(this, L.T("Rename project"), L.T("New name for the project:"), r.Name);
            if (name == null || name == r.Name) return;
            try { ProjectIO.RenameProject(r.Path, name); LoadRecent(); }
            catch (Exception ex) { MessageBox.Show(L.T("Rename failed:") + " " + ex.Message); }
        }

        // ---------------- delete ----------------
        private void DeleteRecent_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not RecentProject r) return;

            var res = MessageBox.Show(
                L.F("Delete project \"{0}\"?", r.Name) + "\n\n" +
                L.T("Yes = delete the files from disk") + "\n" +
                L.T("No = only remove it from this list") + "\n" +
                L.T("Cancel = do nothing"),
                L.T("Delete project"), MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            if (res == MessageBoxResult.Cancel) return;
            try
            {
                if (res == MessageBoxResult.Yes) ProjectIO.DeleteProjectFiles(r.Path);
                else ProjectIO.RemoveRecent(r.Path);
                LoadRecent();
            }
            catch (Exception ex) { MessageBox.Show(L.T("Delete failed:") + " " + ex.Message); }
        }

        private void OpenEditor(Project project)
        {
            var editor = new MainWindow(project);
            Application.Current.MainWindow = editor;
            editor.Show();
            Close();
        }
    }
}
