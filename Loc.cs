using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace EasyEngine
{
    /// <summary>
    /// The editor's languages. Texts are written in English in the code; <see cref="T"/> looks the English text up in the
    /// chosen language's table (Lang\&lt;code&gt;.json, embedded) and falls back to English when there is no entry.
    /// Two ways in:
    /// - every window, when it opens, translates its elements (buttons, labels, menu items, tooltips, its title…)
    ///   (<see cref="Install"/>), as long as its text was set directly (not bound to data) and it isn't inside an element
    ///   marked <see cref="SkipProperty"/> — the places that show the person's own names (objects, layers, chat…);
    /// - text the code changes later, message boxes and texts with numbers in them go through <see cref="T"/> / <see cref="F"/>.
    /// Games (PlayWindow) are never translated: what they show is the game's own content.
    /// </summary>
    public static class L
    {
        public record Lang(string Code, string Native, string English);

        /// <summary>Every language the editor speaks; English first (the default and the fallback).</summary>
        public static readonly Lang[] All =
        {
            new("en", "English", "English"),
            new("pt-BR", "Português (Brasil)", "Brazilian Portuguese"),
            new("pt-PT", "Português (Portugal)", "European Portuguese"),
            new("es", "Español", "Spanish"),
            new("fr", "Français", "French"),
            new("de", "Deutsch", "German"),
            new("ja", "日本語", "Japanese"),
            new("zh-Hans", "简体中文", "Simplified Chinese"),
            new("zh-Hant", "繁體中文", "Traditional Chinese"),
        };

        public static string Code { get; private set; } = "en";
        public static Lang Current => All.FirstOrDefault(l => l.Code == Code) ?? All[0];
        public static bool IsEnglish => Code == "en";

        /// <summary>Raised after the language changes (open windows re-translate themselves; code-built panels rebuild).</summary>
        public static event Action? Changed;

        private static Dictionary<string, string> _map = new();

        /// <summary>Switches the language ("en", "pt-BR"…; unknown → English) and re-translates every open window.</summary>
        public static void SetLanguage(string? code)
        {
            var lang = All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)) ?? All[0];
            Code = lang.Code;
            _map = lang.Code == "en" ? new() : LoadTable(lang.Code);
            if (Application.Current != null)
                foreach (Window w in Application.Current.Windows) TranslateTree(w);
            Changed?.Invoke();
        }

        /// <summary>Tests: use <paramref name="table"/> as the current language's texts (null = back to English).</summary>
        internal static void UseTableForTests(string code, Dictionary<string, string>? table)
        {
            Code = table == null ? "en" : code;
            _map = table ?? new();
        }

        /// <summary>The translation table of <paramref name="code"/> (empty if missing or broken — English shows instead).</summary>
        public static Dictionary<string, string> LoadTable(string code)
        {
            try
            {
                using var s = typeof(L).Assembly.GetManifestResourceStream($"Lang.{code}.json");
                if (s == null) return new();
                var map = JsonSerializer.Deserialize<Dictionary<string, string>>(s, new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                return map?.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value) ?? new();
            }
            catch { return new(); }
        }

        /// <summary>The text <paramref name="en"/> in the current language. Icons/emoji in front ("💾 Save") and endings like
        /// "…", ":" or " ▾" are kept around the translated words, so one entry ("Save") serves every spelling.</summary>
        public static string T(string? en)
        {
            if (string.IsNullOrEmpty(en) || _map.Count == 0) return en ?? "";
            if (_map.TryGetValue(en, out var t)) return t;
            int a = 0, b = en.Length - 1;
            while (a <= b && !IsWordChar(en, a)) a++;
            while (b >= a && !IsWordChar(en, b) && en[b] != ')') b--;
            if (a > b || (a == 0 && b == en.Length - 1)) return en;
            return _map.TryGetValue(en.Substring(a, b - a + 1), out t) ? en.Substring(0, a) + t + en.Substring(b + 1) : en;
        }

        private static bool IsWordChar(string s, int i)
        {
            if (char.IsLowSurrogate(s[i]) && i > 0) i--; // judge the whole character (emoji are not words)
            return char.IsLetterOrDigit(s, i);
        }

        /// <summary><see cref="T"/> of a format ("{0} objects") filled with <paramref name="args"/>; a broken translation
        /// falls back to the English format.</summary>
        public static string F(string enFormat, params object?[] args)
        {
            try { return string.Format(T(enFormat), args); }
            catch (FormatException) { return string.Format(enFormat, args); }
        }

        /// <summary>The line the AI prompts end with, so the chat answers in the person's language (empty for English).</summary>
        public static string AiReplyRule => IsEnglish ? "" :
            $"Always write your messages to the person in {Current.English}. Keep JSON keys and object names exactly as they are; when you name a button, behavior or field of the editor, give its English name followed by your translation in parentheses.";

        // ------------------------------------------------------------ automatic translation of what loads

        /// <summary>Marks an element whose texts (and everything inside it) are the person's own content: never translated.</summary>
        public static readonly DependencyProperty SkipProperty = DependencyProperty.RegisterAttached(
            "Skip", typeof(bool), typeof(L), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
        public static void SetSkip(DependencyObject d, bool value) => d.SetValue(SkipProperty, value);
        public static bool GetSkip(DependencyObject d) => (bool)d.GetValue(SkipProperty);
        /// <summary>Returns <paramref name="e"/> marked as content (for initialisers: <c>panel.Children.Add(L.Keep(tb))</c>).</summary>
        public static T0 Keep<T0>(T0 e) where T0 : DependencyObject { SetSkip(e, true); return e; }

        // per translated property: the English text and the text we put there (so a later language switch, or a new
        // text set by the code in the meantime, is recognised)
        private static readonly DependencyProperty OriginalProperty = DependencyProperty.RegisterAttached(
            "Original", typeof(Dictionary<DependencyProperty, (string En, string Shown)>), typeof(L), new PropertyMetadata(null));

        private static bool _installed, _everTranslated;

        /// <summary>Translates every window as it opens, and elements that load later (call once at startup).</summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            // WPF only raises Loaded on the elements that listen to it themselves, so a class handler alone misses most
            // of a window: when a window opens, its whole tree (XAML and what the code built so far) is translated.
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((s, _) => { if (s is Window w && (!IsEnglish || _everTranslated)) TranslateTree(w); }), true);
            EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((s, _) => { if (s is FrameworkElement fe) TranslateOne(fe); }), true);
            EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent,
                new RoutedEventHandler((s, _) => { if (s is DependencyObject d) TranslateTree(d); }), true);
        }

        /// <summary>Translates <paramref name="root"/> and everything under it (logical and visual tree).</summary>
        public static void TranslateTree(DependencyObject root)
        {
            var seen = new HashSet<DependencyObject>();
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                if (!seen.Add(d)) continue;
                if (d is FrameworkElement fe)
                {
                    if (GetSkip(fe)) continue;
                    TranslateOne(fe);
                    if (fe.ContextMenu != null) stack.Push(fe.ContextMenu);
                }
                foreach (var c in LogicalTreeHelper.GetChildren(d)) if (c is DependencyObject cd) stack.Push(cd);
                if (d is System.Windows.Media.Visual)
                {
                    int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);
                    for (int i = 0; i < n; i++) stack.Push(System.Windows.Media.VisualTreeHelper.GetChild(d, i));
                }
            }
        }

        private static void TranslateOne(FrameworkElement e)
        {
            if (IsEnglish && !_everTranslated) return;
            if (GetSkip(e)) return;
            if (e is Window win) { win.Language = XmlLanguage.GetLanguage(LanguageTag); Swap(e, Window.TitleProperty); }
            switch (e)
            {
                case TextBlock:
                    Swap(e, TextBlock.TextProperty);
                    break;
                case HeaderedItemsControl:
                    Swap(e, HeaderedItemsControl.HeaderProperty);
                    break;
                case HeaderedContentControl:
                    Swap(e, HeaderedContentControl.HeaderProperty);
                    Swap(e, ContentControl.ContentProperty);
                    break;
                // a combo/list item without a Tag may be read back by its text: leave those alone
                case ContentControl cc when cc is not Window && cc is not ComboBoxItem { Tag: null } && cc is not ListBoxItem { Tag: null }:
                    Swap(e, ContentControl.ContentProperty);
                    break;
            }
            Swap(e, FrameworkElement.ToolTipProperty);
        }

        /// <summary>Puts the current language's text in a string property that was set directly on the element.</summary>
        private static void Swap(FrameworkElement e, DependencyProperty p)
        {
            if (e.ReadLocalValue(p) is not string shown) return; // unset, bound, or not text
            var originals = (Dictionary<DependencyProperty, (string En, string Shown)>?)e.GetValue(OriginalProperty);
            var en = originals != null && originals.TryGetValue(p, out var o) && o.Shown == shown ? o.En : shown;
            var target = T(en);
            if (target == shown && originals == null) return;
            if (originals == null) { originals = new(); e.SetValue(OriginalProperty, originals); }
            originals[p] = (en, target);
            _everTranslated = true;
            if (target != shown) e.SetValue(p, target);
        }

        /// <summary>The language tag for WPF (picks the Japanese / Simplified / Traditional look of Han characters).</summary>
        public static string LanguageTag => Code switch { "zh-Hans" => "zh-CN", "zh-Hant" => "zh-TW", _ => Code };
    }
}
