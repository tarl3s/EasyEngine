using System.Windows;
using System.Windows.Media;

namespace EasyEngine
{
    // The look of the dialogue box for one Interact (its "Dialogue box" and "Font" settings): the game's theme (the
    // default), a ready-made look, or its own box and text colours. Each line remembers the look it was said with, so
    // lines from different speakers can look different.
    public partial class PlayWindow
    {
        internal sealed record DialogLook(string Bg, string Border, string Text, string Speaker, double Corner, string Font, string Image = "");

        // null = the game's theme as it is
        private DialogLook? LookOf(BehaviorData bh) => LookOf((k, d) => bh.Text(k, d));
        // a Cutscene's (or a boss's) "say" step: its own box, else the cutscene's font
        private DialogLook? LookOf(SeqStep s, string cutsceneFont) =>
            LookOf((k, d) => s.Str(k).Trim() is { Length: > 0 } v ? v : k == "font" ? cutsceneFont : d);
        // "look", "boxColor", "textColor", "boxImage" and "font", read the same way from a behavior or a step
        private DialogLook? LookOf(System.Func<string, string, string> get)
        {
            string font = get("font", "").Trim();
            DialogLook? look = get("look", "theme") switch
            {
                "dark" => new("#111418", "#3A3A46", "#FFFFFF", "#F4C03B", 8, ""),
                "light" => new("#F4F4F0", "#C8C8C0", "#1A1A20", "#2A6FDB", 10, ""),
                "parchment" => new("#EAD9B0", "#8B6B3E", "#3B2A14", "#8B2E1A", 4, "Georgia"),
                "retro" => new("#000000", "#FFFFFF", "#FFFFFF", "#FFD700", 0, "Consolas"),
                "custom" => new(get("boxColor", "#111418"), get("boxColor", "#111418"), get("textColor", "#FFFFFF"), get("textColor", "#FFFFFF"), 8, ""),
                // the person's own picture is the box (no border, square corners); with no picture yet it is the dark box
                "picture" => new("#111418", "#00000000", get("textColor", "#FFFFFF"), get("textColor", "#FFFFFF"), 0, "", get("boxImage", "").Trim()),
                _ => null,
            };
            if (font.Length == 0) return look;
            return (look ?? ThemeLookWithFont(font)) with { Font = font };
        }
        // the game's theme with another font (a Cutscene's Font, an Interact with only its Font set)
        private DialogLook ThemeLookWithFont(string font)
        {
            var t = _project.Theme ?? _themeDefaults;
            return new DialogLook(t.DialogBg, t.DialogBorder, t.DialogText, t.DialogSpeaker, t.DialogCorner, font);
        }
        // a big title (Cutscene "title" step) in the cutscene's font; "" = as it always was
        private void TitleFont(string font)
        {
            if (font.Length == 0) BigTitle.ClearValue(System.Windows.Controls.TextBlock.FontFamilyProperty);
            else BigTitle.FontFamily = ResolveFont(font);
        }
        private static readonly GameTheme _themeDefaults = new();
        internal string TDialogBg => (DialogBox.Background as SolidColorBrush)?.Color.ToString() ?? ""; // tests
        internal string TDialogFont => DialogText.FontFamily?.Source ?? "";
        internal FontFamily TDialogFontFamily => DialogText.FontFamily;
        internal FontFamily TTitleFont => BigTitle.FontFamily;
        internal FontFamily? TLabelFont(string name) => _entities.Find(e => e.Data.Name == name)?.LabelBlock?.FontFamily;
        internal void TSay(string text, string speaker) => ShowDialog(text, speaker);

        // a picture as the box: drawn over the whole box, which takes the picture's proportions (as wide as always, as tall
        // as the picture says, never smaller than the text needs). A picture that can't load leaves the box as the theme's.
        private void PictureBox(string image)
        {
            DialogBox.ClearValue(FrameworkElement.MinHeightProperty);
            if (image.Length == 0 || Load(image) is not System.Windows.Media.Imaging.BitmapImage bmp || bmp.PixelWidth <= 0)
            {
                if (image.Length > 0) Warn("Dialogue", "", $"The picture \"{image}\" can't be shown — the plain box is used instead");
                return;
            }
            DialogBox.Background = new ImageBrush(bmp) { Stretch = Stretch.Fill };
            DialogBox.BorderBrush = Brushes.Transparent;
            double w = System.Math.Max(100, (Root.ActualWidth > 0 ? Root.ActualWidth : Width) - 80);
            DialogBox.MinHeight = System.Math.Min(w * bmp.PixelHeight / bmp.PixelWidth, System.Math.Max(80, (Root.ActualHeight > 0 ? Root.ActualHeight : Height) * 0.6));
        }
        internal string TDialogImage => (DialogBox.Background as ImageBrush)?.ImageSource?.ToString() ?? "";
        internal double TDialogMinHeight => DialogBox.MinHeight;

        // a line is shown: its own look, or back to the game's theme
        private void ApplyDialogLook(DialogLook? look)
        {
            if (look == null) { ApplyTheme(); DialogBox.ClearValue(FrameworkElement.MinHeightProperty); return; }
            try
            {
                var t = _project.Theme ?? _themeDefaults;
                DialogBox.Background = Brush(look.Bg);
                DialogBox.BorderBrush = Brush(look.Border);
                DialogBox.CornerRadius = new CornerRadius(look.Corner);
                PictureBox(look.Image);
                DialogText.Foreground = Brush(look.Text);
                DialogSpeaker.Foreground = Brush(look.Speaker);
                var font = ResolveFont(look.Font.Length > 0 ? look.Font : null);
                DialogText.FontFamily = font; DialogSpeaker.FontFamily = font;
                DialogText.FontSize = System.Math.Max(8, t.DialogFontSize);
            }
            catch { }
        }
    }
}
