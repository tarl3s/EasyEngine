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
            VersionText.Text = "v" + Updater.Current.ToString(3);
            if (Environment.GetCommandLineArgs().Contains("--updated")) VersionText.Text += "  — updated ✓";
            SampleProjects.EnsureInstalled(); // the projects that come with the engine (SampleProjects.cs)
            LoadRecent();
            NewLocation.Text = DefaultParentFolder();
            _ = CheckOllamaAsync();
            Loaded += async (s, e) => await CheckForUpdateAsync();
        }

        // ---------------- updates from GitHub (Updater.cs) ----------------
        private async System.Threading.Tasks.Task CheckForUpdateAsync()
        {
            if (Updater.IsDevBuild || ProjectIO.SuppressRecent) return; // run from the code (or a test): the code is the update
            var r = await Updater.CheckAsync();
            if (r == null || !IsVisible) return;
            var notes = string.Join("\n", r.Notes.Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0).Take(10));
            var ask = MessageBox.Show(this,
                $"A new version of EasyEngine is out: v{r.Version.ToString(3)} (you have v{Updater.Current.ToString(3)}).\n\n" +
                (notes.Length > 0 ? notes + "\n\n" : "") +
                "Install it now? EasyEngine will download it, close, replace the old version with the new one and open again. Your projects are not touched.",
                "Update EasyEngine", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (ask == MessageBoxResult.Yes) await InstallUpdateAsync(r);
        }

        private async System.Threading.Tasks.Task InstallUpdateAsync(Updater.Release r)
        {
            var bar = new System.Windows.Controls.ProgressBar { Height = 14, Maximum = 1, Margin = new Thickness(0, 10, 0, 0) };
            var text = new System.Windows.Controls.TextBlock { Text = $"Downloading EasyEngine v{r.Version.ToString(3)}…", Foreground = (System.Windows.Media.Brush)FindResource("Text") };
            var win = new Window
            {
                Title = "Updating EasyEngine", Owner = this, Width = 380, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (System.Windows.Media.Brush)FindResource("Bg1"),
                Content = new System.Windows.Controls.StackPanel { Margin = new Thickness(18), Children = { text, bar } },
            };
            win.Show();
            try
            {
                var dir = await Updater.DownloadAsync(r, new Progress<double>(p => bar.Value = p));
                text.Text = "Installing… EasyEngine will open again in a moment.";
                Updater.StartSwap(dir, Updater.InstallDir, Environment.ProcessId);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                win.Close();
                MessageBox.Show(this, "The update couldn't be downloaded:\n" + ex.Message + "\n\nYou can keep using this version.", "Update EasyEngine", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        private async System.Threading.Tasks.Task CheckOllamaAsync()
        {
            var state = await OllamaManager.EnsureRunningAsync();
            OllamaStatus.Text = state switch
            {
                OllamaManager.State.Running      => "AI: Ollama connected ✓",
                OllamaManager.State.Started      => "AI: Ollama started ✓",
                OllamaManager.State.NotInstalled => "AI: Ollama not installed (get it at ollama.com)",
                _                                => "AI: Ollama did not respond",
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
            var dlg = new OpenFolderDialog { Title = "Where to save the project" };
            if (Directory.Exists(NewLocation.Text)) dlg.InitialDirectory = NewLocation.Text;
            if (dlg.ShowDialog() == true) NewLocation.Text = dlg.FolderName;
        }

        private void CreateNew_Click(object sender, RoutedEventArgs e)
        {
            var name = NewName.Text.Trim();
            if (string.IsNullOrEmpty(name)) { MessageBox.Show("Give the project a name."); return; }
            var parent = NewLocation.Text;
            if (!Directory.Exists(parent)) { MessageBox.Show("Invalid destination folder."); return; }
            try { OpenEditor(ProjectIO.CreateNew(name, parent)); }
            catch (Exception ex) { MessageBox.Show("Create failed: " + ex.Message); }
        }

        // ---------------- open ----------------
        private void OpenProject_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Open project",
                Filter = $"EasyEngine project (*{ProjectIO.Extension})|*{ProjectIO.Extension}"
            };
            if (dlg.ShowDialog() == true) OpenPath(dlg.FileName);
        }

        private void RecentList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (RecentList.SelectedItem is RecentProject r) OpenPath(r.Path);
        }

        private void OpenPath(string path)
        {
            if (!File.Exists(path)) { MessageBox.Show("That project file no longer exists."); ProjectIO.RemoveRecent(path); LoadRecent(); return; }
            try { OpenEditor(ProjectIO.Load(path)); }
            catch (Exception ex) { MessageBox.Show("Open failed: " + ex.Message); }
        }

        // ---------------- delete ----------------
        private void DeleteRecent_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not RecentProject r) return;

            var res = MessageBox.Show(
                $"Delete project \"{r.Name}\"?\n\n" +
                "Yes = delete the files from disk\n" +
                "No = only remove it from this list\n" +
                "Cancel = do nothing",
                "Delete project", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            if (res == MessageBoxResult.Cancel) return;
            try
            {
                if (res == MessageBoxResult.Yes) ProjectIO.DeleteProjectFiles(r.Path);
                else ProjectIO.RemoveRecent(r.Path);
                LoadRecent();
            }
            catch (Exception ex) { MessageBox.Show("Delete failed: " + ex.Message); }
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
