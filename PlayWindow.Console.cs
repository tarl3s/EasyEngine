using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace EasyEngine
{
    // Play-mode debug console: the log store, the logging helpers every system calls,
    // the F1 panel, and the UI hit-test used to tell "clicked the HUD" from "clicked the world".
    public partial class PlayWindow
    {
        private readonly DebugLog _log = new();
        private DebugConsoleView? _console;
        private const double ConsoleWidth = 400;
        private readonly Dictionary<string, (double t, int skipped)> _throttle = new();

        /// <summary>False in exported games: no F1 console and no play log file.</summary>
        public bool DevTools { get; set; } = true;

        /// <summary>Shows or hides the bar on top of a test run (Stop, Console, hints). With <paramref name="remember"/>
        /// the next test runs start the same way.</summary>
        public void ShowTestBar(bool show, bool remember)
        {
            TopBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (remember && !ProjectIO.SuppressRecent) { AiSettings.HideTestBar = !show; AiSettings.Save(); }
            if (!show && remember) Info("Input", "", "Top bar hidden — F2 shows it again");
            // a test run without the bar says for a few seconds how to bring it back (an exported game never has it)
            BarHint.Visibility = !show && DevTools ? Visibility.Visible : Visibility.Collapsed;
            if (BarHint.IsVisible || (!show && DevTools))
            {
                var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromSeconds(1)) { BeginTime = TimeSpan.FromSeconds(3) };
                fade.Completed += (s, e) => { if (TopBar.Visibility != Visibility.Visible) BarHint.Visibility = Visibility.Collapsed; BarHint.BeginAnimation(OpacityProperty, null); BarHint.Opacity = 1; };
                BarHint.BeginAnimation(OpacityProperty, fade);
            }
        }

        /// <summary>How a run starts: an exported game never shows the bar; a test run as the person left it.</summary>
        public void StartTestBar() { if (!DevTools || (!ProjectIO.SuppressRecent && AiSettings.HideTestBar)) ShowTestBar(false, remember: false); }

        private void HideBar_Click(object sender, RoutedEventArgs e) { ShowTestBar(false, remember: true); FocusGame(); }
        public bool TTopBarVisible => TopBar.Visibility == Visibility.Visible;
        public (double w, double h) TScreenSize => (Root.Width, Root.Height);

        // ---------------- logging ----------------
        private void Log(LogLevel lvl, string cat, string src, string msg, string details = "") => _log.Add(_gameTime, _frameNo, lvl, cat, src, msg, details);
        private void Info(string cat, string src, string msg, string details = "") => Log(LogLevel.Info, cat, src, msg, details);
        private void Warn(string cat, string src, string msg, string details = "") => Log(LogLevel.Warn, cat, src, msg, details);
        private void Error(string cat, string src, string msg, string details = "") => Log(LogLevel.Error, cat, src, msg, details);

        /// <summary>At most one line per <paramref name="key"/> every <paramref name="interval"/> seconds; the skipped ones are counted in the details.</summary>
        private void LogThrottled(string key, double interval, LogLevel lvl, string cat, string src, string msg, string details = "")
        {
            if (_throttle.TryGetValue(key, out var st) && _gameTime - st.t < interval) { _throttle[key] = (st.t, st.skipped + 1); return; }
            int skipped = st.skipped;
            _throttle[key] = (_gameTime, 0);
            if (skipped > 0) details = $"{skipped} similar line(s) were skipped (rate-limited)" + (details.Length > 0 ? "\n" + details : "");
            Log(lvl, cat, src, msg, details);
        }

        private void LogClick(string button)
        {
            var ui = UiHitAt(_mouseScreen);
            if (ui != null) Info("Input", "", $"{button} click on {ui}", $"screen ({_mouseScreen.X:0}, {_mouseScreen.Y:0})");
            else Info("Input", "", $"{button} click at world ({_mouseWorld.X:0}, {_mouseWorld.Y:0})", $"screen ({_mouseScreen.X:0}, {_mouseScreen.Y:0})");
        }

        private static readonly HashSet<Key> MovementKeys = new() { Key.A, Key.D, Key.W, Key.S, Key.Left, Key.Right, Key.Up, Key.Down, Key.Space };

        // ---------------- the F1 panel ----------------
        private bool ConsoleVisible => ConsoleHost.Visibility == Visibility.Visible;
        private void ConsoleBtn_Click(object sender, RoutedEventArgs e) => ToggleConsole();
        private void ToggleConsole() => ShowConsole(!ConsoleVisible);

        private void ShowConsole(bool show)
        {
            if (!DevTools || show == ConsoleVisible) return;
            if (show)
            {
                if (_console == null)
                {
                    _console = new DebugConsoleView(_log);
                    _console.CloseRequested += () => ShowConsole(false);
                    _console.FocusGameRequested += FocusGame;
                }
                ConsoleHost.Content = _console;
                ConsoleHost.Visibility = Visibility.Visible; ConsoleSplitter.Visibility = Visibility.Visible;
                ConsoleSplitCol.Width = new GridLength(5); ConsoleCol.Width = new GridLength(ConsoleWidth);
                // widen the window so the game area keeps its size (capped to the screen)
                if (WindowState == WindowState.Normal)
                {
                    var wa = SystemParameters.WorkArea;
                    Width = Math.Min(Width + ConsoleWidth + 5, wa.Width);
                    if (Left + Width > wa.Right) Left = Math.Max(wa.Left, wa.Right - Width);
                }
                _console.Flush();
            }
            else
            {
                double w = ConsoleCol.ActualWidth + 5;
                ConsoleHost.Visibility = Visibility.Collapsed; ConsoleSplitter.Visibility = Visibility.Collapsed;
                ConsoleSplitCol.Width = new GridLength(0); ConsoleCol.Width = new GridLength(0);
                if (WindowState == WindowState.Normal) Width = Math.Max(400, Width - w);
                FocusGame();
            }
        }

        private void FocusGame() { Focus(); Keyboard.Focus(this); }

        /// <summary>True while the user is typing in a text box (e.g. the console search): keys must not drive the game.</summary>
        private static bool Typing => Keyboard.FocusedElement is TextBoxBase;

        private void FlushConsole() { if (_console != null && ConsoleVisible) _console.Flush(); }

        private void WritePlayLog()
        {
            if (!DevTools) return;
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "easyengine_play.log");
                File.WriteAllText(path, $"EasyEngine play session — {_project.Name} — {DateTime.Now}\r\n" +
                                        $"{_log.Infos} info · {_log.Warns} warnings · {_log.Errors} errors\r\n\r\n" + _log.ToText());
            }
            catch { }
        }

        // ---------------- "did the click land on the HUD?" ----------------
        // Decided with the same rectangles that draw the HUD, so it behaves the same live and headless.
        private string? UiHitAt(Point screen)
        {
            if (TopBar.IsVisible && TopBar.ActualWidth > 0)
            {
                var tl = TopBar.TranslatePoint(new Point(0, 0), Root);
                if (new Rect(tl, new Size(TopBar.ActualWidth, TopBar.ActualHeight)).Contains(screen)) return "the top bar";
            }
            if (DialogBox.Visibility == Visibility.Visible && DialogBox.ActualWidth > 0)
            {
                var tl = DialogBox.TranslatePoint(new Point(0, 0), Root);
                if (new Rect(tl, new Size(DialogBox.ActualWidth, DialogBox.ActualHeight)).Contains(screen)) return "the dialog box";
            }
            foreach (var kv in _uiRects) if (kv.Value.Contains(screen)) return kv.Key;
            return null;
        }

        // HUD systems (hotbar slots, checklist panel) register their screen rectangles here every logic tick.
        private readonly Dictionary<string, Rect> _uiRects = new();
    }
}
