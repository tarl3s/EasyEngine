using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace EasyEngine;

// Type-to-search (ListSearch.cs) with real keyboard input in a shown window: typing on a closed list opens it and
// filters, the picked value doesn't change while typing, Enter picks the best match.
public partial class App
{
    partial void SelfTestListSearch(SelfTestContext c)
    {
        string? err;
        string picked = "";
        var combo = Dropdown.Make(new[] { ("solid", "🧱  Solid"), ("platformer", "🏃  Platformer"), ("patrol", "🚶  Patrol"), ("spring", "🌀  Spring") }, "solid", v => picked = v);
        var win = new Window { Width = 320, Height = 240, Content = new StackPanel { Children = { combo } }, ShowInTaskbar = false, Left = -2000, Top = -2000, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            win.Show();
            static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            // the keys go straight to the list (the same routed events a keyboard raises), so the test doesn't depend on
            // Windows letting this window take the focus while someone else is using the computer
            void Type(string text) { combo.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, combo, text)) { RoutedEvent = UIElement.PreviewTextInputEvent }); Pump(); }
            void Press(Key k)
            {
                var src = PresentationSource.FromVisual(win);
                if (src == null) return;
                combo.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, src, 0, k) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Pump();
            }

            win.Activate(); Keyboard.Focus(combo); Pump();
            bool focused = combo.IsKeyboardFocused; // typing on a CLOSED list opens it only with the keyboard in it
            if (!focused) { combo.IsDropDownOpen = true; Pump(); }
            Type("p"); Type("l"); Type("a");
            var visible = combo.Items.OfType<ComboBoxItem>().Where(i => i.Visibility == Visibility.Visible).Select(i => i.Tag as string).ToList();
            bool opened = combo.IsDropDownOpen, unchanged = picked == "" && (combo.SelectedItem as ComboBoxItem)?.Tag as string == "solid";
            if (!focused) c.Write("list search: this window couldn't take the keyboard focus — the list was opened by the test");
            Press(Key.Enter);
            err = !opened ? "typing on a closed list didn't open it"
                : !visible.SequenceEqual(new[] { "platformer" }) ? "typing \"pla\" left these visible: " + string.Join(", ", visible)
                : !unchanged ? "typing changed the picked value"
                : picked != "platformer" ? $"Enter picked \"{picked}\" instead of platformer"
                : combo.IsDropDownOpen ? "the list stayed open after Enter"
                : combo.Items.OfType<ComboBoxItem>().Any(i => i.Visibility != Visibility.Visible) ? "the list wasn't reset after picking" : null;
        }
        catch (Exception ex) { err = "crashed: " + ex.Message; }
        finally { try { win.Close(); } catch { } }
        c.Write($"list search (real keys): {(err == null ? "OK" : "FAILED — " + err)}");
        c.LateChecks.Add(() => err == null ? null : "list search: " + err);
    }
}
