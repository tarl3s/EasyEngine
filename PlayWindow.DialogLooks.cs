using System.Windows;
using System.Windows.Media;

namespace EasyEngine
{
    // The look of the dialogue box for one Interact (its "Dialogue box" and "Font" settings): the game's theme (the
    // default), a ready-made look, or its own box and text colours. Each line remembers the look it was said with, so
    // lines from different speakers can look different.
    public partial class PlayWindow
    {
        internal sealed record DialogLook(string Bg, string Border, string Text, string Speaker, double Corner, string Font);

        // null = the game's theme as it is
        private DialogLook? LookOf(BehaviorData bh)
        {
            string font = bh.Text("font", "");
            DialogLook? look = bh.Text("look", "theme") switch
            {
                "dark" => new("#111418", "#3A3A46", "#FFFFFF", "#F4C03B", 8, ""),
                "light" => new("#F4F4F0", "#C8C8C0", "#1A1A20", "#2A6FDB", 10, ""),
                "parchment" => new("#EAD9B0", "#8B6B3E", "#3B2A14", "#8B2E1A", 4, "Georgia"),
                "retro" => new("#000000", "#FFFFFF", "#FFFFFF", "#FFD700", 0, "Consolas"),
                "custom" => new(bh.Text("boxColor", "#111418"), bh.Text("boxColor", "#111418"), bh.Text("textColor", "#FFFFFF"), bh.Text("textColor", "#FFFFFF"), 8, ""),
                _ => null,
            };
            if (font.Length == 0) return look;
            var t = _project.Theme ?? _themeDefaults;
            return (look ?? new DialogLook(t.DialogBg, t.DialogBorder, t.DialogText, t.DialogSpeaker, t.DialogCorner, "")) with { Font = font };
        }
        private static readonly GameTheme _themeDefaults = new();
        internal string TDialogBg => (DialogBox.Background as SolidColorBrush)?.Color.ToString() ?? ""; // tests
        internal string TDialogFont => DialogText.FontFamily?.Source ?? "";
        internal void TSay(string text, string speaker) => ShowDialog(text, speaker);

        // a line is shown: its own look, or back to the game's theme
        private void ApplyDialogLook(DialogLook? look)
        {
            if (look == null) { ApplyTheme(); return; }
            try
            {
                var t = _project.Theme ?? _themeDefaults;
                DialogBox.Background = Brush(look.Bg);
                DialogBox.BorderBrush = Brush(look.Border);
                DialogBox.CornerRadius = new CornerRadius(look.Corner);
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
