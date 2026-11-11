using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using BE = BuilderEngine.Script;

namespace EasyEngine
{
    // Settings → Sound: which sound engine games use (FMOD when it is installed, or the classic Windows players), and the
    // FMOD Studio helper: open FMOD Studio, reload the banks, and what the open project has in its "fmod" folder.
    public partial class SettingsWindow
    {
        private void AddSoundRows(StackPanel root)
        {
            // ---- the sound engine ----
            var status = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            L.SetSkip(status, true); // made in the current language below (it has a version number in it)
            void ShowStatus() => status.Text = GameAudio.StatusText();
            var backend = Dropdown.Make(new[]
            {
                ("auto", L.T("Automatic (FMOD when it is installed)")),
                ("fmod", L.T("FMOD")),
                ("classic", L.T("Classic (Windows)")),
            }, BE.FmodLibrary.BackendKey(GameAudio.Backend), v =>
            {
                AiSettings.AudioBackend = v; AiSettings.Save();
                ShowStatus();
            });
            ShowStatus();
            var box = new StackPanel();
            box.Children.Add(backend); box.Children.Add(status);
            root.Children.Add(Row(L.T("🔊  Sound"), box,
                L.T("Used from the next time a game starts. FMOD is optional and free for small games: install the FMOD Engine from fmod.com, or put fmod.dll next to EasyEngine.exe. FMOD Studio events need FMOD whatever you choose here.")));

            // ---- FMOD Studio ----
            var banks = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            L.SetSkip(banks, true); // made in the current language (with numbers and FMOD's own words)
            void ShowBanks() => banks.Text = (_project != null ? L.T("This project:") + " " : "") + GameAudio.ProjectBanks(_project?.FolderPath).text;
            ShowBanks();

            var open = new Button { Content = L.T("Open FMOD Studio"), Height = 30, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 8, 0) };
            open.Click += (s, e) =>
            {
                var exe = BE.FmodLibrary.FindStudioApp();
                if (exe == null)
                {
                    MessageBox.Show(this, L.T("FMOD Studio isn't installed. It's free: download it from fmod.com (you need a free account), install it, then come back here."),
                        L.T("FMOD Studio"), MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show(this, L.F("Error: {0}", ex.Message), L.T("FMOD Studio")); }
            };
            var reload = new Button { Content = L.T("↻  Reload banks"), Height = 30, Padding = new Thickness(12, 0, 12, 0) };
            reload.Click += (s, e) => { GameAudio.ReloadBanks(); ShowBanks(); };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(open); buttons.Children.Add(reload);
            var studio = new StackPanel();
            studio.Children.Add(buttons); studio.Children.Add(banks);
            if (_project?.FolderPath is { Length: > 0 } dir)
            {
                var folder = GameAudio.BankFolder(dir);
                var openFolder = new Button { Content = L.T("📂  Open the banks folder"), Height = 28, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                openFolder.Click += (s, e) =>
                {
                    try { Directory.CreateDirectory(folder); Process.Start("explorer.exe", folder); } catch { }
                };
                studio.Children.Add(openFolder);
            }
            root.Children.Add(Row(L.T("🎚  FMOD Studio"), studio,
                L.T("Make your sounds in FMOD Studio, set its built banks folder (File → Preferences → Build) to this project's \"fmod\" folder, and press Build. Then pick the events in a Sound behavior, in the blocks and in cutscenes.")));
        }
    }
}
