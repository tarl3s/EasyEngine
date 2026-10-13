using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace EasyEngine
{
    /// <summary>Type to search in every selection box: with a dropdown (or a picker menu) open, typing hides the entries
    /// that don't match and shows what was typed at the top of the list. Nothing is selected until the person picks an
    /// entry (click or Enter), so a search never saves or adds an undo step. Matching ignores case, accents and a leading
    /// emoji, and also accepts the English original of a translated entry and its value (Tag).</summary>
    public static class ListSearch
    {
        /// <summary>What was typed in this list (shown at the top of its dropdown by the styles in Styles.xaml).</summary>
        public static readonly DependencyProperty QueryProperty = DependencyProperty.RegisterAttached(
            "Query", typeof(string), typeof(ListSearch), new PropertyMetadata(""));
        public static string GetQuery(DependencyObject d) => (string)d.GetValue(QueryProperty);
        public static void SetQuery(DependencyObject d, string value) => d.SetValue(QueryProperty, value);

        /// <summary>"No match" while a search finds nothing, else "" (shown under the query).</summary>
        public static readonly DependencyProperty HintProperty = DependencyProperty.RegisterAttached(
            "Hint", typeof(string), typeof(ListSearch), new PropertyMetadata(""));
        public static string GetHint(DependencyObject d) => (string)d.GetValue(HintProperty);
        public static void SetHint(DependencyObject d, string value) => d.SetValue(HintProperty, value);

        // an entry this search hid (so a reset shows only those again, never one the code hid itself)
        private static readonly DependencyProperty HiddenProperty = DependencyProperty.RegisterAttached(
            "Hidden", typeof(bool), typeof(ListSearch), new PropertyMetadata(false));
        // a combo whose DropDownClosed resets its search (CLR event: hooked once per combo)
        private static readonly DependencyProperty HookedProperty = DependencyProperty.RegisterAttached(
            "Hooked", typeof(bool), typeof(ListSearch), new PropertyMetadata(false));

        private static bool _installed;

        /// <summary>Starts listening to every ComboBox and ContextMenu of the app (call once at startup).</summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            EventManager.RegisterClassHandler(typeof(ComboBox), UIElement.PreviewTextInputEvent, new TextCompositionEventHandler(ComboText));
            EventManager.RegisterClassHandler(typeof(ComboBox), UIElement.PreviewKeyDownEvent, new KeyEventHandler(ComboKey));
            EventManager.RegisterClassHandler(typeof(ContextMenu), UIElement.PreviewTextInputEvent, new TextCompositionEventHandler(MenuText));
            EventManager.RegisterClassHandler(typeof(ContextMenu), UIElement.PreviewKeyDownEvent, new KeyEventHandler(MenuKey));
            EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.ClosedEvent, new RoutedEventHandler((s, _) => { if (s is ContextMenu m) Reset(m); }), true);
        }

        /// <summary>True when this key belongs to a list: one is open with the keyboard in it, or a focused (closed)
        /// dropdown is about to open on a typed letter. Windows with their own shortcuts skip those keys.</summary>
        public static bool WantsKey(KeyEventArgs e)
        {
            switch (Keyboard.FocusedElement)
            {
                case ComboBox cb when cb.IsDropDownOpen: return true;
                case ComboBox { IsEditable: false }: return Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift && IsTypingKey(e.Key);
                case ComboBoxItem ci: return ItemsControl.ItemsControlFromItemContainer(ci) is ComboBox { IsDropDownOpen: true };
                case MenuItem: return true;
                case ContextMenu m: return m.IsOpen;
            }
            return false;
        }

        private static bool IsTypingKey(Key k) => k is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.NumPad9;

        // ------------------------------------------------------------------ matching (pure, tested in FullTest.ListSearch.cs)

        /// <summary>Lower case, no accents, letters and digits only (anything else — emoji, symbols, punctuation — becomes
        /// one space), trimmed: "🏃  Plataforma Rápida" → "plataforma rapida".</summary>
        public static string Fold(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var d = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            bool space = true;
            foreach (char c in d)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(c)) { sb.Append(char.ToLowerInvariant(c)); space = false; }
                else if (!space) { sb.Append(' '); space = true; }
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>How well a text matches a query (both already <see cref="Fold"/>ed): 3 starts with it, 2 a word starts
        /// with it, 1 contains it, 0 no match.</summary>
        public static int Score(string folded, string query)
        {
            if (query.Length == 0) return 1;
            int i = folded.IndexOf(query, StringComparison.Ordinal);
            if (i < 0) return 0;
            if (i == 0) return 3;
            return folded.IndexOf(" " + query, StringComparison.Ordinal) >= 0 ? 2 : 1;
        }

        /// <summary>Every text an entry can be found by: what it shows (a string, <see cref="TextSearch"/>.Text, the texts
        /// inside a panel), the English originals of translated texts, and its value.</summary>
        public static List<string> TextsOf(object item, DependencyObject? container)
        {
            var texts = new List<string>();
            void Add(string? s) { if (!string.IsNullOrWhiteSpace(s)) texts.Add(s); }
            void Element(object? o)
            {
                switch (o)
                {
                    case null: return;
                    case string s: Add(s); return;
                    case TextBlock tb: Add(tb.Text); texts.AddRange(L.EnglishOf(tb)); return;
                    case DependencyObject d:
                        Add(TextSearch.GetText(d));
                        texts.AddRange(L.EnglishOf(d));
                        if (d is HeaderedItemsControl hi) Element(hi.Header);
                        else if (d is ContentControl cc) Element(cc.Content);
                        else foreach (var child in LogicalTreeHelper.GetChildren(d)) Element(child);
                        return;
                    case Enum en: Add(en.ToString()); return;
                }
            }
            Element(item);
            if (container != null && !ReferenceEquals(container, item)) Element(container);
            var tag = (item as FrameworkElement)?.Tag ?? (container as FrameworkElement)?.Tag;
            if (tag is string ts && !ts.StartsWith('\u0001')) Add(ts);
            else if (tag is Enum te) Add(te.ToString());
            return texts;
        }

        // entries that always stay: "＋ New…", "My picture…" (tags starting with \u0001) and "(none)" (empty value)
        private static bool Sticky(FrameworkElement c) => c is ComboBoxItem { Tag: string t } && (t.Length == 0 || t[0] == '\u0001');

        private static FrameworkElement? ContainerAt(ItemsControl list, int i)
        {
            var item = list.Items[i];
            return list.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement
                   ?? (item is ComboBoxItem or MenuItem or Separator ? (FrameworkElement)item : null);
        }

        /// <summary>Shows only the entries of <paramref name="list"/> matching <paramref name="query"/> (never changes the
        /// selection) and returns the best one to highlight (prefix matches first, then list order), or null.</summary>
        public static FrameworkElement? Apply(ItemsControl list, string query)
        {
            SetQuery(list, query);
            var q = Fold(query);
            bool searching = q.Length > 0;
            FrameworkElement? best = null; int bestScore = 0; bool any = false;
            for (int i = 0; i < list.Items.Count; i++)
            {
                var c = ContainerAt(list, i);
                if (c == null) continue;
                if (c.Visibility != Visibility.Visible && !(bool)c.GetValue(HiddenProperty)) continue; // hidden by the code: leave it
                bool show;
                int score = 0;
                if (!searching) show = true;
                else if (c is Separator) show = false; // separators and category titles only make sense in the full list
                else
                {
                    score = c.IsEnabled ? TextsOf(list.Items[i] ?? c, c).Select(t => Score(Fold(t), q)).DefaultIfEmpty(0).Max() : 0; // disabled = a category title
                    any |= score > 0;
                    show = score > 0 || Sticky(c);
                    if (score > bestScore) { bestScore = score; best = c; }
                }
                c.SetValue(HiddenProperty, !show);
                c.SetCurrentValue(UIElement.VisibilityProperty, show ? Visibility.Visible : Visibility.Collapsed);
            }
            SetHint(list, searching && !any ? L.T("No match") : "");
            return best;
        }

        /// <summary>Every entry shows again and the query is cleared.</summary>
        public static void Reset(ItemsControl list)
        {
            if (GetQuery(list).Length == 0 && GetHint(list).Length == 0) return;
            Apply(list, "");
        }

        /// <summary>Adds typed text to the list's query and filters (what a keystroke does).</summary>
        public static FrameworkElement? Type(ItemsControl list, string text) => Apply(list, GetQuery(list) + text);

        /// <summary>The visible entries a person can pick, in list order.</summary>
        public static List<FrameworkElement> Visible(ItemsControl list)
        {
            var r = new List<FrameworkElement>();
            for (int i = 0; i < list.Items.Count; i++)
                if (ContainerAt(list, i) is { } c && c.Visibility == Visibility.Visible && c.IsEnabled && c is not Separator) r.Add(c);
            return r;
        }

        // ------------------------------------------------------------------ ComboBox

        private static void ComboText(object sender, TextCompositionEventArgs e)
        {
            if (sender is not ComboBox cb || cb.IsEditable || e.OriginalSource is TextBoxBase) return;
            var text = e.Text ?? "";
            if (text.Length == 0 || text.Any(char.IsControl)) return;
            if (!cb.IsDropDownOpen)
            {
                // a letter on a focused, closed dropdown opens it and starts the search (instead of jumping the selection)
                if (!cb.IsKeyboardFocused || !text.Any(char.IsLetterOrDigit)) return;
                Hook(cb);
                cb.IsDropDownOpen = true;
                Type(cb, text);
                // the dropdown highlights the selected entry once it has opened: highlight the best match after it
                cb.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (cb.IsDropDownOpen) Highlight(cb, Apply(cb, GetQuery(cb))); }));
                e.Handled = true;
                return;
            }
            if (text.Trim().Length == 0 && GetQuery(cb).Length == 0) return; // a space alone doesn't start a search
            Hook(cb);
            Highlight(cb, Type(cb, text));
            e.Handled = true;
        }

        private static void ComboKey(object sender, KeyEventArgs e)
        {
            if (sender is not ComboBox cb || cb.IsEditable || !cb.IsDropDownOpen || e.OriginalSource is TextBoxBase) return;
            var query = GetQuery(cb);
            switch (e.Key)
            {
                case Key.Back:
                    if (query.Length > 0) Highlight(cb, Apply(cb, query[..^1]));
                    e.Handled = true; // never reaches the window (Backspace isn't a shortcut while picking)
                    break;
                case Key.Escape when query.Length > 0:
                    Highlight(cb, Apply(cb, ""), keepOnNull: true);
                    e.Handled = true;
                    break;
                case Key.Enter when query.Length > 0:
                    {
                        var vis = Visible(cb);
                        var pick = vis.FirstOrDefault(c => c is ComboBoxItem { IsHighlighted: true }) ?? Apply(cb, query);
                        if (pick != null)
                        {
                            var item = cb.ItemContainerGenerator.ItemFromContainer(pick);
                            if (item == DependencyProperty.UnsetValue) item = pick;
                            cb.IsDropDownOpen = false; // closes (and resets the search) first, then picks — as a click does
                            cb.SelectedItem = item;
                        }
                        e.Handled = true;
                        break;
                    }
                case Key.Up or Key.Down when query.Length > 0 && Keyboard.Modifiers == ModifierKeys.None:
                    {
                        var vis = Visible(cb);
                        if (vis.Count > 0)
                        {
                            int at = vis.FindIndex(c => c is ComboBoxItem { IsHighlighted: true } || c.IsKeyboardFocused);
                            int next = at < 0 ? 0 : Math.Clamp(at + (e.Key == Key.Down ? 1 : -1), 0, vis.Count - 1);
                            Highlight(cb, vis[next]);
                        }
                        e.Handled = true;
                        break;
                    }
            }
        }

        private static void Hook(ComboBox cb)
        {
            if ((bool)cb.GetValue(HookedProperty)) return;
            cb.SetValue(HookedProperty, true);
            cb.DropDownClosed += (s, _) => Reset(cb);
        }

        private static readonly MethodInfo? InfoFromContainer =
            typeof(ItemsControl).GetMethod("ItemInfoFromContainer", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(DependencyObject) }, null);
        private static readonly PropertyInfo? HighlightedInfo =
            typeof(ComboBox).GetProperty("HighlightedInfo", BindingFlags.Instance | BindingFlags.NonPublic);

        // Moves the dropdown's highlight (what Enter and the arrows start from) to an entry; with none, the keyboard
        // stays on the combo itself so the next keys still reach the search and the dropdown stays open.
        private static void Highlight(ComboBox cb, FrameworkElement? c, bool keepOnNull = false)
        {
            if (c == null)
            {
                if (keepOnNull) return;
                try { HighlightedInfo?.SetValue(cb, null); } catch { }
                if (!cb.IsKeyboardFocused) cb.Focus();
                return;
            }
            try { if (InfoFromContainer?.Invoke(cb, new object[] { c }) is { } info) HighlightedInfo?.SetValue(cb, info); } catch { }
            c.BringIntoView();
            if (c.IsVisible) c.Focus();
        }

        // ------------------------------------------------------------------ ContextMenu pickers

        // the menu the keyboard is in (not one of its submenus)
        private static bool InMenu(ContextMenu m) =>
            m.IsOpen && (Keyboard.FocusedElement is not MenuItem mi || ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(mi), m));

        private static void MenuText(object sender, TextCompositionEventArgs e)
        {
            if (sender is not ContextMenu m || !InMenu(m) || e.OriginalSource is TextBoxBase) return;
            var text = e.Text ?? "";
            if (text.Length == 0 || text.Any(char.IsControl)) return;
            if (text.Trim().Length == 0 && GetQuery(m).Length == 0) return;
            FocusEntry(m, Type(m, text));
            e.Handled = true;
        }

        private static void MenuKey(object sender, KeyEventArgs e)
        {
            if (sender is not ContextMenu m || !InMenu(m) || e.OriginalSource is TextBoxBase) return;
            var query = GetQuery(m);
            if (query.Length == 0) return;
            switch (e.Key)
            {
                case Key.Back:
                    FocusEntry(m, Apply(m, query[..^1]));
                    e.Handled = true;
                    break;
                case Key.Escape:
                    FocusEntry(m, Apply(m, ""));
                    e.Handled = true;
                    break;
                case Key.Enter:
                    // the focused visible entry is clicked by the menu itself; otherwise the best match is
                    if (Keyboard.FocusedElement is MenuItem { IsVisible: true } f && ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(f), m)) return;
                    if (Apply(m, query) is MenuItem best)
                        try { (new System.Windows.Automation.Peers.MenuItemAutomationPeer(best) as System.Windows.Automation.Provider.IInvokeProvider)?.Invoke(); } catch { }
                    e.Handled = true;
                    break;
            }
        }

        private static void FocusEntry(ContextMenu m, FrameworkElement? c)
        {
            if (c != null) { c.BringIntoView(); if (c.IsVisible) c.Focus(); }
            else if (!m.IsKeyboardFocused) m.Focus();
        }
    }
}
