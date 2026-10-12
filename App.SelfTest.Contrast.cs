using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyEngine;

// M1 in a real window: open the kinds of popups that used to show light text on light backgrounds (a code-built
// checkable menu like the minimap's "Also show", a tooltip, a plain button, a checkbox, plain window text) and measure
// the colour of the text actually drawn against what is behind it.
public partial class App
{
    partial void SelfTestContrast(SelfTestContext c)
    {
        var host = new StackPanel { Margin = new Thickness(12) };
        var plain = new TextBlock { Text = "Plain window text" };
        var button = new Button { Content = "Plain button", Margin = new Thickness(0, 8, 0, 0) };
        var check = new CheckBox { Content = "A checkbox", Margin = new Thickness(0, 8, 0, 0), IsChecked = true };
        host.Children.Add(plain); host.Children.Add(button); host.Children.Add(check);
        var win = new Window { Title = "Contrast check", Width = 360, Height = 240, Content = host, Background = (Brush)Current.FindResource("Bg0"), ShowActivated = false };
        win.Show();
        c.CloseLater.Add(win);

        var menu = new ContextMenu { PlacementTarget = button };
        menu.Items.Add(new MenuItem { Header = "Slime_1", IsCheckable = true, IsChecked = true, StaysOpenOnClick = true });
        menu.Items.Add(new MenuItem { Header = "Coin", IsCheckable = true });
        menu.IsOpen = true;
        var tip = new ToolTip { Content = "A tooltip that explains a field", PlacementTarget = check };
        tip.IsOpen = true;

        static TextBlock? FirstText(DependencyObject root, string startsWith)
        {
            var stack = new Stack<DependencyObject>(); stack.Push(root);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (n is TextBlock tb && tb.Text.StartsWith(startsWith)) return tb;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(n); i++) stack.Push(VisualTreeHelper.GetChild(n, i));
            }
            return null;
        }
        Brush Bg(string key) => (Brush)Current.FindResource(key);

        c.LateChecks.Add(() =>
        {
            var probes = new List<(string what, TextBlock? text, Brush? behind)>
            {
                ("plain window text", plain, win.Background),
                ("plain button", FirstText(button, "Plain button"), button.Background),
                ("checkbox", FirstText(check, "A checkbox"), win.Background),
                ("menu item (\"Also show\" style)", FirstText(menu, "Slime_1"), menu.Background),
                ("tooltip", FirstText(tip, "A tooltip"), tip.Background),
            };
            var report = string.Join(" · ", probes.Select(p => $"{p.what} {Contrast.Ratio(p.text?.Foreground, p.behind):0.0}:1"));
            c.Write("contrast (real window): " + report);
            menu.IsOpen = false; tip.IsOpen = false;
            var bad = probes.FirstOrDefault(p => p.text == null || Contrast.Ratio(p.text.Foreground, p.behind) < 4.5);
            if (bad.what != null) return $"unreadable text: {bad.what} ({(bad.text == null ? "not found" : $"{Contrast.Ratio(bad.text.Foreground, bad.behind):0.0}:1")})";
            // the underscore of "Slime_1" must survive (it is not a keyboard shortcut)
            return FirstText(menu, "Slime_1") == null ? "menu item lost its underscore" : null;
        });
    }
}
