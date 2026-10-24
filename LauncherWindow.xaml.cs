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
            StartMenu.EnsureShortcut(); // findable in Windows search (StartMenu.cs)
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
            // the startup check (UpdatePrompt, before this window opened) already asked; this one is for when it got no
            // answer in time (offline, slow network)
            if (!UpdatePrompt.Enabled || UpdatePrompt.Checked) return;
            var r = await Updater.CheckAsync();
            if (r == null || !IsVisible) return;
            if (UpdatePrompt.Ask(r, this)) await UpdatePrompt.InstallAsync(r, this);
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
