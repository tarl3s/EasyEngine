using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace EasyEngine
{
    public partial class AnimationWindow : Window
    {
        private readonly Project _project;
        private readonly GameObjectData _obj;
        private SpriteAnimation? _cur;

        private readonly DispatcherTimer _preview = new();
        private int _previewFrame;
        private readonly Dictionary<string, BitmapImage> _cache = new();

        public AnimationWindow(Project project, GameObjectData obj)
        {
            InitializeComponent();
            _project = project;
            _obj = obj;
            Title = L.F("Animations — {0}", obj.Name);
            L.SetSkip(AnimTitle, true); // the animation's name

            _preview.Tick += PreviewTick;
            RefreshAnimList();
            if (_obj.Animations.Count > 0) SelectAnim(_obj.ResolveDefaultAnimation());

            FpsBox.LostFocus += (s, e) => CommitFps();
            LoopBox.Checked += (s, e) => { if (_cur != null) _cur.Loop = true; RestartPreview(); };
            LoopBox.Unchecked += (s, e) => { if (_cur != null) _cur.Loop = false; RestartPreview(); };
            Closed += (s, e) => _preview.Stop();
        }

        // ---------------- animation list ----------------
        private void RefreshAnimList()
        {
            AnimList.Items.Clear();
            foreach (var anim in _obj.Animations)
                AnimList.Items.Add(MakeAnimItem(anim));
        }

        private ListBoxItem MakeAnimItem(SpriteAnimation anim)
        {
            bool isDefault = anim.Name == (_obj.DefaultAnimation ?? _obj.Animations.FirstOrDefault()?.Name);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new StackPanel();
            title.Children.Add(L.Keep(new TextBlock { Text = (isDefault ? "★ " : "") + anim.Name, Foreground = (SolidColorBrush)FindResource("Text"), FontWeight = FontWeights.SemiBold, FontSize = 13 }));
            title.Children.Add(new TextBlock { Text = L.F("{0} frame(s)", anim.Frames.Count), Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11 });
            Grid.SetColumn(title, 0);

            var del = new Button { Content = "✕", Width = 24, Height = 24, Style = (Style)FindResource("GhostButton"), Foreground = (SolidColorBrush)FindResource("Danger"), VerticalAlignment = VerticalAlignment.Center };
            del.Click += (s, e) => { e.Handled = true; DeleteAnimation(anim); };
            Grid.SetColumn(del, 1);

            grid.Children.Add(title);
            grid.Children.Add(del);

            return new ListBoxItem { Content = grid, Tag = anim, Padding = new Thickness(8, 6, 6, 6), Foreground = (SolidColorBrush)FindResource("Text") };
        }

        private void AnimList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AnimList.SelectedItem is ListBoxItem item && item.Tag is SpriteAnimation anim && !ReferenceEquals(anim, _cur))
                SelectAnim(anim);
        }

        private void SelectAnim(SpriteAnimation? anim)
        {
            _cur = anim;
            if (anim == null)
            {
                Editor.Visibility = Visibility.Collapsed;
                EmptyState.Visibility = Visibility.Visible;
                _preview.Stop();
                return;
            }
            Editor.Visibility = Visibility.Visible;
            EmptyState.Visibility = Visibility.Collapsed;

            AnimTitle.Text = anim.Name;
            FpsBox.Text = anim.Fps.ToString("0.###");
            LoopBox.IsChecked = anim.Loop;
            UpdateDefaultButton();
            RefreshFrames();
            RestartPreview();

            // sync selection in list
            foreach (ListBoxItem it in AnimList.Items)
                if (ReferenceEquals(it.Tag, anim)) { AnimList.SelectedItem = it; break; }
        }

        private void UpdateDefaultButton()
        {
            bool isDefault = _cur != null && _cur.Name == (_obj.DefaultAnimation ?? _obj.Animations.FirstOrDefault()?.Name);
            DefaultBtn.Content = isDefault ? L.T("★ Default") : L.T("☆ Set as default");
        }

        // ---------------- preview ----------------
        private void RestartPreview()
        {
            _preview.Stop();
            _previewFrame = 0;
            if (_cur == null || _cur.Frames.Count == 0) { PreviewImage.Source = null; return; }
            PreviewImage.Source = Load(_cur.Frames[0]);
            double fps = Math.Max(1, _cur.Fps);
            _preview.Interval = TimeSpan.FromSeconds(1.0 / fps);
            if (_cur.Frames.Count > 1) _preview.Start();
        }

        private void PreviewTick(object? sender, EventArgs e)
        {
            if (_cur == null || _cur.Frames.Count == 0) return;
            _previewFrame++;
            if (_previewFrame >= _cur.Frames.Count)
                _previewFrame = _cur.Loop ? 0 : _cur.Frames.Count - 1;
            PreviewImage.Source = Load(_cur.Frames[_previewFrame]);
        }

        private BitmapImage? Load(string relative)
        {
            if (_cache.TryGetValue(relative, out var cached)) return cached;
            var abs = ProjectIO.AbsoluteAsset(_project, relative);
            if (abs == null) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(abs); bmp.EndInit();
                _cache[relative] = bmp;
                return bmp;
            }
            catch { return null; }
        }

        // ---------------- frames ----------------
        private void RefreshFrames()
        {
            FramesPanel.Children.Clear();
            if (_cur == null) return;
            FrameCount.Text = L.F("{0} frame(s)", _cur.Frames.Count);
            for (int i = 0; i < _cur.Frames.Count; i++)
                FramesPanel.Children.Add(MakeFrameItem(i));
        }

        private FrameworkElement MakeFrameItem(int index)
        {
            var anim = _cur!;
            var stack = new StackPanel { Margin = new Thickness(0, 0, 10, 10) };

            var img = new Image { Source = Load(anim.Frames[index]), Width = 72, Height = 72, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            var thumb = new Border { Width = 80, Height = 80, Background = (SolidColorBrush)FindResource("Bg2"), BorderBrush = (SolidColorBrush)FindResource("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Child = img };
            stack.Children.Add(thumb);

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
            var left = SmallBtn("◀"); left.Click += (s, e) => MoveFrame(index, -1);
            var num = new TextBlock { Text = (index + 1).ToString(), Foreground = (SolidColorBrush)FindResource("TextDim"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
            var right = SmallBtn("▶"); right.Click += (s, e) => MoveFrame(index, +1);
            var edit = SmallBtn("✎"); edit.ToolTip = L.T("Edit in the sprite editor"); edit.Click += (s, e) => EditFrame(index);
            var del = SmallBtn("✕"); del.Foreground = (SolidColorBrush)FindResource("Danger"); del.Click += (s, e) => DeleteFrame(index);
            row.Children.Add(left); row.Children.Add(num); row.Children.Add(right); row.Children.Add(edit); row.Children.Add(del);
            stack.Children.Add(row);

            return stack;
        }

        private Button SmallBtn(string text) => new()
        {
            Content = text, Width = 22, Height = 20, Style = (Style)FindResource("GhostButton"),
            FontSize = 10, Padding = new Thickness(0), Foreground = (SolidColorBrush)FindResource("Text")
        };

        private void MoveFrame(int index, int dir)
        {
            if (_cur == null) return;
            int j = index + dir;
            if (j < 0 || j >= _cur.Frames.Count) return;
            (_cur.Frames[index], _cur.Frames[j]) = (_cur.Frames[j], _cur.Frames[index]);
            RefreshFrames(); RestartPreview();
        }

        private void DeleteFrame(int index)
        {
            if (_cur == null) return;
            _cur.Frames.RemoveAt(index);
            RefreshFrames(); RestartPreview(); RefreshAnimList();
        }

        // ---------------- actions ----------------
        private void AddAnimation_Click(object sender, RoutedEventArgs e)
        {
            var files = PickImages();
            if (files == null || files.Length == 0) return;

            var name = InputDialog.Prompt(this, L.T("New animation"), L.T("Animation name (e.g. idle, walk, jump):"), SuggestName());
            if (name == null) return;

            var anim = new SpriteAnimation { Name = name };
            ImportInto(anim, files);
            _obj.Animations.Add(anim);
            if (_obj.DefaultAnimation == null) _obj.DefaultAnimation = anim.Name;
            RefreshAnimList();
            SelectAnim(anim);
        }

        private void AddFrames_Click(object sender, RoutedEventArgs e)
        {
            if (_cur == null) return;
            var files = PickImages();
            if (files == null || files.Length == 0) return;
            ImportInto(_cur, files);
            RefreshFrames(); RestartPreview(); RefreshAnimList();
        }

        // ---------------- built-in sprite editor ----------------
        private string? DrawSprite(string? editAbsolute = null)
        {
            var win = new SpriteEditorWindow(_project, 32, editAbsolute) { Owner = this };
            return win.ShowDialog() == true ? win.SavedRelativePath : null;
        }

        private void DrawSprite_Click(object sender, RoutedEventArgs e)
        {
            var rel = DrawSprite();
            if (rel == null) return;
            if (_cur != null)
            {
                _cur.Frames.Add(rel);
                RefreshFrames(); RestartPreview(); RefreshAnimList();
            }
            else
            {
                var name = InputDialog.Prompt(this, L.T("New animation"), L.T("Animation name (e.g. idle, walk):"), SuggestName());
                if (name == null) return;
                var anim = new SpriteAnimation { Name = name };
                anim.Frames.Add(rel);
                _obj.Animations.Add(anim);
                if (_obj.DefaultAnimation == null) _obj.DefaultAnimation = anim.Name;
                RefreshAnimList(); SelectAnim(anim);
            }
        }

        private void DrawFrame_Click(object sender, RoutedEventArgs e)
        {
            if (_cur == null) return;
            var rel = DrawSprite();
            if (rel == null) return;
            _cur.Frames.Add(rel);
            RefreshFrames(); RestartPreview(); RefreshAnimList();
        }

        private void EditFrame(int index)
        {
            if (_cur == null || index < 0 || index >= _cur.Frames.Count) return;
            var abs = ProjectIO.AbsoluteAsset(_project, _cur.Frames[index]);
            var rel = DrawSprite(abs);
            if (rel == null) return;
            var old = _cur.Frames[index];
            _cur.Frames[index] = rel;
            _cache.Remove(old);
            RefreshFrames(); RestartPreview();
        }

        private void ImportInto(SpriteAnimation anim, string[] files)
        {
            foreach (var f in files.OrderBy(NaturalKey))
            {
                try { anim.Frames.Add(ProjectIO.ImportAsset(_project, f, "sprites")); }
                catch (Exception ex) { MessageBox.Show(L.F("Error importing {0}:", Path.GetFileName(f)) + " " + ex.Message); }
            }
        }

        private string[]? PickImages()
        {
            var dlg = new OpenFileDialog
            {
                Title = L.T("Choose frames"),
                Multiselect = true,
                Filter = L.T("Images") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
            };
            return dlg.ShowDialog() == true ? dlg.FileNames : null;
        }

        private string SuggestName()
        {
            string[] common = { "idle", "walk", "run", "jump", "attack", "death" };
            foreach (var c in common)
                if (!_obj.Animations.Any(a => a.Name.Equals(c, StringComparison.OrdinalIgnoreCase))) return c;
            return "anim" + (_obj.Animations.Count + 1);
        }

        private void Rename_Click(object sender, RoutedEventArgs e)
        {
            if (_cur == null) return;
            var name = InputDialog.Prompt(this, L.T("Rename animation"), L.T("New name:"), _cur.Name);
            if (name == null) return;
            bool wasDefault = _obj.DefaultAnimation == _cur.Name;
            _cur.Name = name;
            if (wasDefault) _obj.DefaultAnimation = name;
            AnimTitle.Text = name;
            RefreshAnimList(); UpdateDefaultButton();
        }

        private void SetDefault_Click(object sender, RoutedEventArgs e)
        {
            if (_cur == null) return;
            _obj.DefaultAnimation = _cur.Name;
            UpdateDefaultButton(); RefreshAnimList();
        }

        private void DeleteAnim_Click(object sender, RoutedEventArgs e)
        {
            if (_cur != null) DeleteAnimation(_cur);
        }

        private void DeleteAnimation(SpriteAnimation anim)
        {
            if (MessageBox.Show(L.F("Delete animation \"{0}\"?", anim.Name), L.T("Delete"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            _obj.Animations.Remove(anim);
            if (_obj.DefaultAnimation == anim.Name)
                _obj.DefaultAnimation = _obj.Animations.FirstOrDefault()?.Name;
            RefreshAnimList();
            SelectAnim(_obj.Animations.FirstOrDefault());
        }

        private void CommitFps()
        {
            if (_cur == null) return;
            if (double.TryParse(FpsBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v))
            {
                _cur.Fps = Math.Clamp(v, 1, 60);
                RestartPreview();
            }
        }

        // ordena frame01, frame02, ... frame10 corretamente
        private static string NaturalKey(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            return System.Text.RegularExpressions.Regex.Replace(name, @"\d+", m => m.Value.PadLeft(8, '0'));
        }
    }
}
