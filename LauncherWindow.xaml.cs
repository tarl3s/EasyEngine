using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace EasyEngine
{
    public partial class LauncherWindow : Window
    {
        public LauncherWindow()
        {
            InitializeComponent();
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (v != null) VersionText.Text = $"v{v.Major}.{v.Minor}.{v.Build}";
            SampleProjects.EnsureInstalled(); // the projects that come with the engine, the first time (SampleProjects.cs)
            LoadRecent();
            NewLocation.Text = DefaultParentFolder();
            _ = CheckOllamaAsync();
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
