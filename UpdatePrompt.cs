using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyEngine
{
    /// <summary>
    /// Asking to update (Updater.cs does the work): before the launcher opens, EasyEngine looks for a newer GitHub release
    /// (a few seconds at most, with a small "Checking…" window) and, if there is one, asks whether to install it first.
    /// No answer in time (offline, slow network) → it opens normally and the launcher asks later if one turns up.
    /// Builds run from the source code never ask (the code is the update), nor do tests.
    /// </summary>
    public static class UpdatePrompt
    {
        /// <summary>The startup check got an answer (newer or not), so the launcher doesn't ask a second time.</summary>
        public static bool Checked { get; private set; }

        public static bool Enabled => !Updater.IsDevBuild && !ProjectIO.SuppressRecent && AiSettings.CheckUpdates;

        /// <summary>Before the launcher opens. True when an update is being installed (EasyEngine closes and reopens).</summary>
        public static async Task<bool> BeforeOpeningAsync(Func<Task<Updater.Release?>>? check = null, Func<Updater.Release, bool>? ask = null,
                                                          int waitMs = 5000)
        {
            if (check == null && !Enabled) return false;
            var splash = check == null ? Splash() : null;
            Updater.Release? r = null;
            try
            {
                var t = (check ?? Updater.CheckAsync)();
                if (await Task.WhenAny(t, Task.Delay(waitMs)) == t) { r = await t; Checked = true; }
            }
            catch { }
            finally { splash?.Close(); }
            if (r == null || !(ask ?? (rel => Ask(rel, null)))(r)) return false;
            return check != null || await InstallAsync(r, null);
        }

        /// <summary>"A new version is out… install it now?" (Yes = true).</summary>
        public static bool Ask(Updater.Release r, Window? owner)
        {
            var notes = string.Join("\n", r.Notes.Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0).Take(10));
            var text = L.F("A new version of EasyEngine is out: v{0} (you have v{1}).", r.Version.ToString(3), Updater.Current.ToString(3)) + "\n\n" +
                       (notes.Length > 0 ? notes + "\n\n" : "") +
                       L.T("Install it now? EasyEngine will download it, close, replace the old version with the new one and open again. Your projects are not touched.");
            var answer = owner != null
                ? MessageBox.Show(owner, text, L.T("Update EasyEngine"), MessageBoxButton.YesNo, MessageBoxImage.Information)
                : MessageBox.Show(text, L.T("Update EasyEngine"), MessageBoxButton.YesNo, MessageBoxImage.Information);
            return answer == MessageBoxResult.Yes;
        }

        /// <summary>Downloads <paramref name="r"/> with a progress window, starts the swap and closes EasyEngine. False if
        /// the download failed (the person was told; this version keeps running).</summary>
        public static async Task<bool> InstallAsync(Updater.Release r, Window? owner)
        {
            var bar = new ProgressBar { Height = 14, Maximum = 1, Margin = new Thickness(0, 10, 0, 0) };
            var text = new TextBlock { Text = L.F("Downloading EasyEngine v{0}…", r.Version.ToString(3)), Foreground = Theme.Brush("Text", "#E6E6EA") };
            var win = new Window
            {
                Title = L.T("Updating EasyEngine"), Owner = owner, Width = 380, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Background = Theme.Brush("Bg1", "#1C1C22"),
                Content = new StackPanel { Margin = new Thickness(18), Children = { text, bar } },
            };
            win.Show();
            try
            {
                var dir = await Updater.DownloadAsync(r, new Progress<double>(p => bar.Value = p));
                text.Text = L.T("Installing… EasyEngine will open again in a moment.");
                Updater.StartSwap(dir, Updater.InstallDir, Environment.ProcessId);
                Application.Current.Shutdown();
                return true;
            }
            catch (Exception ex)
            {
                win.Close();
                var msg = L.T("The update couldn't be downloaded:") + "\n" + ex.Message + "\n\n" + L.T("You can keep using this version.");
                if (owner != null) MessageBox.Show(owner, msg, L.T("Update EasyEngine"), MessageBoxButton.OK, MessageBoxImage.Warning);
                else MessageBox.Show(msg, L.T("Update EasyEngine"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        // the small window shown while GitHub is asked (usually well under a second)
        private static Window Splash()
        {
            var w = new Window
            {
                Title = "EasyEngine", Width = 340, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow, WindowStartupLocation = WindowStartupLocation.CenterScreen, ShowInTaskbar = true,
                Background = Theme.Brush("Bg1", "#1C1C22"),
                Content = new TextBlock
                {
                    Text = L.T("Checking for a new version…"), Margin = new Thickness(22, 20, 22, 20), FontSize = 13,
                    TextAlignment = TextAlignment.Center, Foreground = Theme.Brush("Text", "#E6E6EA"),
                },
            };
            w.Show();
            return w;
        }
    }
}
