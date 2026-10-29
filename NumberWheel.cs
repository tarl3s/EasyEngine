using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace EasyEngine
{
    /// <summary>The mouse wheel changes the number in the box you clicked into (scene size, tilemap columns, a behavior's
    /// speed…), applied at every notch so the change is seen live: up = more, Shift = 10 steps, Ctrl = a tenth of a step.
    /// The step follows the number (1 for 12, 0.1 for 0.8). Only a focused box: just scrolling past a box never changes it.
    /// A box the change rebuilt (the inspector redraws) gets the focus back, so the next notch keeps going.
    /// A whole burst of notches is one undo step (<see cref="SameBurst"/>).</summary>
    public static class NumberWheel
    {
        private static bool _installed;
        private static DateTime _last = DateTime.MinValue;

        /// <summary>A wheel notch is being applied right now (its undo step is the burst's, see <see cref="SameBurst"/>).</summary>
        public static bool Applying { get; private set; }
        /// <summary>This notch came less than 0.9 s after the last one: the same burst, one undo step for all of it.</summary>
        public static bool SameBurst { get; private set; }

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            EventManager.RegisterClassHandler(typeof(TextBox), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel));
        }

        private static void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not TextBox tb || !tb.IsKeyboardFocusWithin) return;
            if (!Apply(tb, e.Delta > 0 ? 1 : -1, Keyboard.Modifiers)) return;
            e.Handled = true;
            if (Window.GetWindow(tb) is Window w)
                w.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Refocus(w, tb)));
        }

        /// <summary>One notch on a box: its number a step up or down, applied at once. False when the box holds no plain number.</summary>
        internal static bool Apply(TextBox tb, int dir, ModifierKeys mods = ModifierKeys.None)
        {
            if (tb.IsReadOnly || !tb.IsEnabled || tb.AcceptsReturn || Step(tb.Text, dir, mods) is not string next) return false;
            SameBurst = (DateTime.Now - _last).TotalMilliseconds < 900;
            _last = DateTime.Now;
            tb.Text = next;
            tb.CaretIndex = next.Length;
            // apply it now: every box applies when it loses focus (boxes that apply as you type already did)
            Applying = true;
            try { tb.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, tb)); }
            finally { Applying = false; }
            return true;
        }

        // the change rebuilt the panel: focus the box now under the mouse (the new copy of this one)
        private static void Refocus(Window w, TextBox old)
        {
            if (old.IsLoaded && PresentationSource.FromVisual(old) != null) { if (!old.IsKeyboardFocusWithin) old.Focus(); return; }
            if (w.InputHitTest(Mouse.GetPosition(w)) is DependencyObject hit && Ancestor<TextBox>(hit) is TextBox tb)
            {
                tb.Focus();
                tb.CaretIndex = tb.Text.Length;
            }
        }

        private static T? Ancestor<T>(DependencyObject? d) where T : DependencyObject
        {
            while (d != null && d is not T) d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as T;
        }

        /// <summary>The number in <paramref name="text"/> one step up (dir 1) or down (dir −1); null when it isn't a plain
        /// number (a formula, a word). A number that was 0 or more doesn't go below 0.</summary>
        internal static string? Step(string text, int dir, ModifierKeys mods = ModifierKeys.None)
        {
            var s = (text ?? "").Trim().Replace(',', '.');
            if (s.Length == 0 || !double.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v)) return null;
            int dot = s.IndexOf('.');
            int decimals = dot < 0 ? 0 : Math.Min(4, s.Length - dot - 1);
            double step = Math.Pow(10, -decimals);
            if (mods.HasFlag(ModifierKeys.Shift)) step *= 10;
            if (mods.HasFlag(ModifierKeys.Control)) { step /= 10; decimals = Math.Min(4, decimals + 1); }
            double n = Math.Round(v + dir * step, decimals);
            if (v >= 0 && n < 0) n = 0;
            return n.ToString("0." + new string('#', decimals), CultureInfo.InvariantCulture);
        }
    }
}
