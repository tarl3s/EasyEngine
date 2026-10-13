using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace EasyEngine
{
    /// <summary>A small sound editor (the "✂ Edit…" next to a sound in the inspector): cut the start/end with two handles
    /// and add simple effects (speed, pitch, volume, fades, reverse, echo). The original file is never changed — the
    /// result is saved as a new WAV in Assets/sounds. All the sound work is in <see cref="AudioEdit"/>.</summary>
    public class AudioEditorWindow : Window
    {
        private readonly Project _project;
        private readonly AudioData _src;
        private readonly string _baseName;
        private AudioEffects _fx = new();
        private AudioData _result;
        private bool _stale;

        private readonly WaveView _srcView, _resultView;
        private readonly TextBlock _trimInfo, _resultInfo;
        private readonly StackPanel _effectsHost = new();
        private readonly Button _play;
        private readonly DispatcherTimer _recalc, _tick;
        private System.Media.SoundPlayer? _player;
        private readonly Stopwatch _clock = new();
        private double _playSeconds;

        /// <summary>Set when the person saves: the project-relative path of the new WAV.</summary>
        public string? SavedRelativePath { get; private set; }

        /// <summary>Opens the editor for a project sound and returns the new sound's project-relative path, or null when
        /// nothing was saved (cancelled, missing file, an MP3…). Explains any problem to the person.</summary>
        public static string? Edit(Window owner, Project project, string relative)
        {
            string title = L.T("Sound editor");
            var abs = ProjectIO.AbsoluteAsset(project, relative);
            if (abs == null) { MessageBox.Show(owner, L.T("The sound file is missing."), title); return null; }
            AudioData data;
            try { data = AudioEdit.ReadWav(abs); }
            catch (Exception ex)
            {
                MessageBox.Show(owner, abs.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
                    ? L.T("Only WAV sounds can be edited. Turn the MP3 into a WAV (many free programs and websites do it) and choose that file instead.")
                    : L.F("Couldn't open the sound: {0}", ex.Message), title);
                return null;
            }
            if (data.Length == 0) { MessageBox.Show(owner, L.T("This sound is empty."), title); return null; }
            var w = new AudioEditorWindow(project, data, Path.GetFileNameWithoutExtension(abs)) { Owner = owner };
            return w.ShowDialog() == true ? w.SavedRelativePath : null;
        }

        public AudioEditorWindow(Project project, AudioData source, string baseName)
        {
            _project = project; _src = source; _baseName = baseName; _result = source;
            Title = L.T("Sound editor");
            Width = 720; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.CanResizeWithGrip; MinWidth = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Theme.Brush("Bg0", "#15151A"); FontFamily = new FontFamily("Segoe UI");
            Brush dim = Theme.Brush("TextDim", "#8C8C97");

            var root = new DockPanel { Margin = new Thickness(14) };

            // --- bottom: play / stop on the left, cancel / save on the right ---
            var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
            DockPanel.SetDock(bottom, Dock.Bottom);
            _play = new Button { Content = L.T("▶ Play"), Height = 32, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 0, 6, 0), Style = Theme.Style("CardButton"), ToolTip = L.T("Hear the result (Space)") };
            _play.Click += (s, e) => TogglePlay();
            var stop = new Button { Content = L.T("■ Stop"), Height = 32, Padding = new Thickness(14, 0, 14, 0), Style = Theme.Style("CardButton") };
            stop.Click += (s, e) => StopPlay();
            DockPanel.SetDock(_play, Dock.Left); DockPanel.SetDock(stop, Dock.Left);
            bottom.Children.Add(_play); bottom.Children.Add(stop);
            var save = new Button { Content = L.T("💾 Save as new sound"), Height = 32, Padding = new Thickness(16, 0, 16, 0), Style = Theme.Style("AccentButton"), ToolTip = L.T("The original sound is kept; the edited one is saved next to it") };
            save.Click += (s, e) => SaveAndClose();
            var cancel = new Button { Content = L.T("Cancel"), Height = 32, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 0, 8, 0), Style = Theme.Style("GhostButton"), IsCancel = true };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            DockPanel.SetDock(save, Dock.Right); DockPanel.SetDock(cancel, Dock.Right);
            bottom.Children.Add(save); bottom.Children.Add(cancel);
            root.Children.Add(bottom);

            var body = new StackPanel();
            var head = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) };
            head.Inlines.Add(L.Keep(new System.Windows.Documents.Run(baseName)));
            body.Children.Add(head);
            body.Children.Add(new TextBlock { Text = L.F("{0} s · {1} Hz · {2}", Sec(_src.Seconds), _src.Rate, _src.Channels.Length > 1 ? L.T("stereo") : L.T("mono")), Foreground = dim, FontSize = 12, Margin = new Thickness(0, 0, 0, 10) });

            body.Children.Add(Section(L.T("✂ CUT — drag the two handles to keep only a part")));
            _srcView = new WaveView(_src, selectable: true) { Height = 110 };
            _srcView.SelectionChanged += OnTrimChanged;
            body.Children.Add(_srcView);
            var trimBar = new DockPanel { Margin = new Thickness(0, 6, 0, 12) };
            var resetCut = new Button { Content = L.T("↺ Keep it all"), Height = 26, Padding = new Thickness(10, 0, 10, 0), Style = Theme.Style("GhostButton") };
            resetCut.Click += (s, e) => _srcView.SetSelection(0, 1);
            DockPanel.SetDock(resetCut, Dock.Right); trimBar.Children.Add(resetCut);
            _trimInfo = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
            trimBar.Children.Add(_trimInfo);
            body.Children.Add(trimBar);

            body.Children.Add(Section(L.T("🎛 EFFECTS")));
            body.Children.Add(_effectsHost);
            BuildEffects();

            var resultHead = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            _resultInfo = new TextBlock { FontSize = 12, Foreground = dim, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(_resultInfo, Dock.Right); resultHead.Children.Add(_resultInfo);
            resultHead.Children.Add(Section(L.T("🔊 RESULT")));
            body.Children.Add(resultHead);
            _resultView = new WaveView(_src, selectable: false) { Height = 70 };
            body.Children.Add(_resultView);

            root.Children.Add(body);
            Content = root;

            _recalc = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
            _recalc.Tick += (s, e) => { _recalc.Stop(); Recalc(); };
            _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            _tick.Tick += (s, e) => UpdatePlayhead();
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Space && e.OriginalSource is not TextBox && !ListSearch.WantsKey(e)) { TogglePlay(); e.Handled = true; } };
            Closed += (s, e) => { StopPlay(); _recalc.Stop(); };

            OnTrimChanged();
            Recalc();
        }

        // ---------------------------------------------------------------- effects panel
        private void BuildEffects()
        {
            _effectsHost.Children.Clear();
            double maxFade = Math.Round(Math.Max(0.1, Math.Min(5, _src.Seconds)), 2);
            _effectsHost.Children.Add(SliderRow("🐢 Speed", L.T("Slower is longer and deeper, faster is shorter and higher — like a tape."),
                0.5, 2, _fx.Speed, 0.05, v => _fx.Speed = v, v => Math.Abs(v - 1) < 1e-9 ? L.T("Normal") : L.F("{0}×", v.ToString("0.00"))));
            _effectsHost.Children.Add(SliderRow("🎵 Pitch", L.T("Higher or lower voice; the length stays the same."),
                -12, 12, _fx.Pitch, 1, v => _fx.Pitch = v, v => v == 0 ? L.T("Normal") : L.F("{0} semitones", (v > 0 ? "+" : "") + v.ToString("0"))));
            _effectsHost.Children.Add(SliderRow("🔊 Volume", L.T("Louder or quieter. A sound that would get too loud is kept just below the limit."),
                0, 200, _fx.Volume * 100, 5, v => _fx.Volume = v / 100, v => $"{v:0}%"));
            _effectsHost.Children.Add(SliderRow("↗ Fade in", L.T("Starts silent and grows to full volume."),
                0, maxFade, Math.Min(_fx.FadeIn, maxFade), 0.05, v => _fx.FadeIn = v, v => v <= 0 ? L.T("Off") : L.F("{0} s", v.ToString("0.00"))));
            _effectsHost.Children.Add(SliderRow("↘ Fade out", L.T("Ends by getting quieter until silent."),
                0, maxFade, Math.Min(_fx.FadeOut, maxFade), 0.05, v => _fx.FadeOut = v, v => v <= 0 ? L.T("Off") : L.F("{0} s", v.ToString("0.00"))));
            _effectsHost.Children.Add(SliderRow("🏔 Echo", L.T("Repeats the sound, quieter each time, as in a cave."),
                0, 100, _fx.Echo * 100, 5, v => _fx.Echo = v / 100, v => v <= 0 ? L.T("Off") : $"{v:0}%"));

            var bar = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var reset = new Button { Content = L.T("↺ No effects"), Height = 26, Padding = new Thickness(10, 0, 10, 0), Style = Theme.Style("GhostButton") };
            reset.Click += (s, e) =>
            {
                _fx = new AudioEffects { TrimStart = _fx.TrimStart, TrimEnd = _fx.TrimEnd };
                BuildEffects(); Changed();
            };
            DockPanel.SetDock(reset, Dock.Right); bar.Children.Add(reset);
            var reverse = new CheckBox { Content = "⇆ Reverse", ToolTip = L.T("Plays the sound backwards."), IsChecked = _fx.Reverse, VerticalAlignment = VerticalAlignment.Center };
            L.SetSkip(reverse, true); // effect names stay in English in every language (the owner's choice); their explanations are translated
            reverse.Checked += (s, e) => { _fx.Reverse = true; Changed(); };
            reverse.Unchecked += (s, e) => { _fx.Reverse = false; Changed(); };
            bar.Children.Add(reverse);
            _effectsHost.Children.Add(bar);
        }

        private FrameworkElement SliderRow(string label, string tip, double min, double max, double value, double step, Action<double> set, Func<double, string> fmt)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6), ToolTip = tip, Background = Brushes.Transparent };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
            L.SetSkip(name, true); // the effect name, in English everywhere
            var shown = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Margin = new Thickness(10, 0, 0, 0) };
            var slider = SliderBar.Make(min, max, value, v => { set(v); Changed(); }, v => shown.Text = fmt(v), step);
            Grid.SetColumn(slider, 1); Grid.SetColumn(shown, 2);
            g.Children.Add(name); g.Children.Add(slider); g.Children.Add(shown);
            return g;
        }

        private static TextBlock Section(string text) =>
            new() { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Theme.Brush("TextDim", "#8C8C97"), Margin = new Thickness(0, 0, 0, 6) };

        private static string Sec(double s) => s.ToString("0.00");

        // ---------------------------------------------------------------- recompute the result
        private void OnTrimChanged()
        {
            double len = _src.Seconds;
            _fx.TrimStart = _srcView.SelStart * len;
            _fx.TrimEnd = _srcView.SelEnd >= 1 ? -1 : _srcView.SelEnd * len;
            double end = _srcView.SelEnd * len;
            _trimInfo.Text = L.F("Keeping {0} s → {1} s ({2} s)", Sec(_fx.TrimStart), Sec(end), Sec(end - _fx.TrimStart));
            Changed();
        }

        private void Changed()
        {
            _stale = true;
            if (_recalc == null) return; // still building the window
            _recalc.Stop(); _recalc.Start();
        }

        private void Recalc()
        {
            _stale = false;
            _result = AudioEdit.Apply(_src, _fx);
            _resultView.SetAudio(_result);
            _resultInfo.Text = L.F("{0} s", Sec(_result.Seconds));
        }

        private AudioData Current() { if (_stale) { _recalc.Stop(); Recalc(); } return _result; }

        // ---------------------------------------------------------------- preview
        private void TogglePlay() { if (_player != null) StopPlay(); else Play(); }

        private void Play()
        {
            StopPlay();
            var r = Current();
            if (r.Length == 0) return;
            try
            {
                _player = new System.Media.SoundPlayer(new MemoryStream(AudioEdit.ToWav16(r)));
                _player.Play(); // loads the (in-memory) WAV, then plays in the background
                _playSeconds = r.Seconds;
                _clock.Restart(); _tick.Start();
                _play.Content = L.T("■ Stop");
            }
            catch (Exception ex) { _player = null; MessageBox.Show(this, L.F("Couldn't play the sound: {0}", ex.Message), Title); }
        }

        private void StopPlay()
        {
            try { _player?.Stop(); } catch { }
            _player?.Dispose(); _player = null;
            _tick.Stop(); _clock.Reset();
            _resultView.Playhead = -1;
            _play.Content = L.T("▶ Play");
        }

        private void UpdatePlayhead()
        {
            double t = _clock.Elapsed.TotalSeconds;
            if (t >= _playSeconds) { StopPlay(); return; }
            _resultView.Playhead = _playSeconds > 0 ? t / _playSeconds : -1;
        }

        // ---------------------------------------------------------------- save
        private void SaveAndClose()
        {
            if (Current().Length == 0) { MessageBox.Show(this, L.T("Nothing is left to save — keep a longer part."), Title); return; }
            var name = InputDialog.Prompt(this, L.T("Save as new sound"), L.T("Sound name:"), _baseName + "_edited");
            if (name == null) return;
            try
            {
                SavedRelativePath = SaveAs(name);
                DialogResult = true;
                Close();
            }
            catch (Exception ex) { MessageBox.Show(this, L.F("Error: {0}", ex.Message), Title); }
        }

        private string SaveAs(string name)
        {
            StopPlay();
            return ProjectIO.SaveSound(_project, AudioEdit.ToWav16(Current()), name);
        }

        // ---------------------------------------------------------------- tests
        internal AudioEffects TestEffects => _fx;
        internal void TestTrim(double startFrac, double endFrac) => _srcView.SetSelection(startFrac, endFrac);
        internal AudioData TestResult => Current();
        internal string TestSave(string name) { var rel = SaveAs(name); SavedRelativePath = rel; return rel; }
        internal int TestSliderCount => CountSliders(_effectsHost);
        private static int CountSliders(Panel p)
        {
            int n = 0;
            foreach (var c in p.Children) if (c is Grid g && g.Children.Count == 3 && g.Children[1] is Canvas) n++;
            return n;
        }
    }

    /// <summary>Draws a sound's waveform (the lowest/highest value of each pixel column). When selectable, two handles
    /// mark the part that is kept; dragging moves the nearest one.</summary>
    internal sealed class WaveView : FrameworkElement
    {
        private AudioData _audio;
        private readonly bool _selectable;
        private StreamGeometry? _wave;
        private int _waveCols = -1;
        private readonly Brush _bg = Theme.Brush("Bg2", "#24242B"), _shade, _handle = Theme.Brush("Text", "#EEE"), _play = Theme.Brush("Danger", "#E5484B");
        private readonly Pen _wavePen, _borderPen, _handlePen, _playPen, _midPen;
        private int _dragging = -1; // 0 = start handle, 1 = end handle
        private double _playhead = -1;
        private const double Grab = 8, MinGapFrac = 0.002;

        public double SelStart { get; private set; }
        public double SelEnd { get; private set; } = 1;
        public event Action? SelectionChanged;

        /// <summary>0…1 position of the play line; −1 hides it.</summary>
        public double Playhead { get => _playhead; set { _playhead = value; InvalidateVisual(); } }

        public WaveView(AudioData audio, bool selectable)
        {
            _audio = audio; _selectable = selectable;
            var bg0 = Theme.ParseColor(Theme.IsLight ? "#E8EAEE" : "#0E0E12");
            if (Application.Current?.TryFindResource("Bg0") is SolidColorBrush b0) bg0 = b0.Color;
            _shade = new SolidColorBrush(Color.FromArgb(175, bg0.R, bg0.G, bg0.B)); _shade.Freeze();
            _wavePen = new Pen(Theme.Brush("Accent", "#18B98A"), 1); _wavePen.Freeze();
            _borderPen = new Pen(Theme.Brush("Border", "#35353F"), 1); _borderPen.Freeze();
            _midPen = new Pen(Theme.Brush("Border", "#35353F"), 1); _midPen.Freeze();
            _handlePen = new Pen(_handle, 2); _handlePen.Freeze();
            _playPen = new Pen(_play, 2); _playPen.Freeze();
            if (selectable) Cursor = Cursors.Hand;
            SizeChanged += (s, e) => { _waveCols = -1; InvalidateVisual(); };
        }

        public void SetAudio(AudioData a) { _audio = a; _waveCols = -1; InvalidateVisual(); }

        public void SetSelection(double start, double end)
        {
            start = Math.Clamp(start, 0, 1); end = Math.Clamp(end, 0, 1);
            if (end < start) (start, end) = (end, start);
            if (end - start < MinGapFrac) end = Math.Min(1, start + MinGapFrac);
            SelStart = start; SelEnd = end;
            InvalidateVisual();
            SelectionChanged?.Invoke();
        }

        private void BuildWave(double w, double h)
        {
            int cols = Math.Max(1, (int)w);
            _waveCols = cols;
            var (mn, mx) = AudioEdit.Peaks(_audio, cols);
            var g = new StreamGeometry();
            double mid = h / 2, amp = h / 2 - 3;
            using (var ctx = g.Open())
                for (int x = 0; x < cols; x++)
                {
                    double y1 = mid - mx[x] * amp, y2 = mid - mn[x] * amp;
                    if (y2 - y1 < 1) { y1 -= 0.5; y2 += 0.5; }
                    ctx.BeginFigure(new Point(x + 0.5, y1), false, false);
                    ctx.LineTo(new Point(x + 0.5, y2), true, false);
                }
            g.Freeze();
            _wave = g;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            dc.DrawRoundedRectangle(_bg, _borderPen, new Rect(0.5, 0.5, w - 1, h - 1), 4, 4);
            dc.DrawLine(_midPen, new Point(0, h / 2), new Point(w, h / 2));
            if (_waveCols != (int)w || _wave == null) BuildWave(w, h);
            dc.DrawGeometry(null, _wavePen, _wave);
            if (_selectable)
            {
                double xs = SelStart * w, xe = SelEnd * w;
                if (xs > 0) dc.DrawRectangle(_shade, null, new Rect(0, 0, xs, h));
                if (xe < w) dc.DrawRectangle(_shade, null, new Rect(xe, 0, w - xe, h));
                foreach (var x in new[] { xs, xe })
                {
                    double cx = Math.Clamp(x, 1, w - 1);
                    dc.DrawLine(_handlePen, new Point(cx, 0), new Point(cx, h));
                    dc.DrawRoundedRectangle(_handle, null, new Rect(cx - 5, h / 2 - 12, 10, 24), 3, 3);
                }
            }
            if (_playhead >= 0) { double px = _playhead * w; dc.DrawLine(_playPen, new Point(px, 0), new Point(px, h)); }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (!_selectable || ActualWidth <= 0) return;
            double x = e.GetPosition(this).X, w = ActualWidth;
            _dragging = Math.Abs(x - SelStart * w) <= Math.Abs(x - SelEnd * w) ? 0 : 1;
            CaptureMouse();
            MoveHandle(x);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_selectable || ActualWidth <= 0) return;
            double x = e.GetPosition(this).X, w = ActualWidth;
            if (_dragging >= 0) { MoveHandle(x); return; }
            bool near = Math.Abs(x - SelStart * w) <= Grab || Math.Abs(x - SelEnd * w) <= Grab;
            Cursor = near ? Cursors.SizeWE : Cursors.Hand;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { _dragging = -1; ReleaseMouseCapture(); }
        protected override void OnLostMouseCapture(MouseEventArgs e) => _dragging = -1;

        private void MoveHandle(double x)
        {
            double f = Math.Clamp(x / ActualWidth, 0, 1);
            if (_dragging == 0) SetSelection(Math.Min(f, SelEnd - MinGapFrac), SelEnd);
            else SetSelection(SelStart, Math.Max(f, SelStart + MinGapFrac));
        }
    }
}
