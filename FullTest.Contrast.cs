using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace EasyEngine;

// M1 — text must be readable everywhere: no app-wide TextBlock colour (it overrode the text inside menus, tooltips and
// buttons), and every popup/control the app styles pairs a dark background with light text (and vice versa).
public partial class App
{
    partial void FeatureTestsContrast(FullTestKit t)
    {
        Style? S(System.Type type) => Current.TryFindResource(type) as Style;
        Style? Named(string key) => Current.TryFindResource(key) as Style;
        Brush? B(string key) => Current.TryFindResource(key) as Brush;

        t.Test("contrast: the app-wide TextBlock style no longer forces a text colour (it leaked into menus/tooltips/buttons)", () =>
            S(typeof(TextBlock)) is Style tb && Contrast.StyleValue(tb, TextBlock.ForegroundProperty) == null);

        foreach (var (name, type) in new[] { ("menus", typeof(ContextMenu)), ("menu items", typeof(MenuItem)), ("tooltips", typeof(ToolTip)),
                                             ("plain buttons", typeof(Button)), ("toggle buttons", typeof(ToggleButton)), ("checkboxes", typeof(CheckBox)),
                                             ("text boxes", typeof(TextBox)), ("dropdowns", typeof(ComboBox)), ("dropdown items", typeof(ComboBoxItem)) })
        {
            t.Test($"contrast: {name} are styled with readable text (≥ 4.5:1 on the dark theme)", () =>
            {
                var st = S(type);
                if (st == null) return false;
                var fg = Contrast.StyleValue(st, Control.ForegroundProperty) as Brush;
                var bg = Contrast.StyleValue(st, Control.BackgroundProperty) as Brush ?? B("Bg1"); // items without a background sit on Bg1/Bg2
                return Contrast.Ratio(fg, bg) >= 4.5 && Contrast.Ratio(fg, B("Bg2")) >= 4.5;
            });
        }

        t.Test("contrast: green buttons and the user's chat bubbles use dark text on the green (white was ~2.5:1)", () =>
            Contrast.Ratio(Contrast.StyleValue(Named("AccentButton"), Control.ForegroundProperty) as Brush, B("Accent")) >= 4.5
            && Contrast.Ratio(B("AccentText"), B("Accent")) >= 4.5 && Contrast.Ratio(B("AccentText"), B("AccentHover")) >= 4.5);

        t.Test("contrast: red Delete/Remove buttons really are red now (and still readable)", () =>
            Contrast.StyleValue(Named("DangerButton"), Control.ForegroundProperty) == B("Danger") && Contrast.Ratio(B("Danger"), B("Bg2")) >= 3.0);

        t.Test("contrast: text boxes get the dark Cut/Copy/Paste menu", () =>
            Contrast.StyleValue(S(typeof(TextBox)), FrameworkElement.ContextMenuProperty) is DynamicResourceExtension d && (string)d.ResourceKey == "TextEditMenu"
            && Current.TryFindResource("TextEditMenu") is ContextMenu m && m.Items.Count >= 4);
    }
}
