using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace EasyEngine
{
    /// <summary>The editor's one dropdown of (value, label) options (inspector pickers, minimap markers, sequence blocks).</summary>
    internal static class Dropdown
    {
        private const string NewTag = "\u0001new";

        /// <summary>Options are matched case-insensitively (the first of equal values wins); a current value that isn't in
        /// the list is kept and shown (empty → "(none)"). An option shown as its own value (a name: object, item, variable…)
        /// is the person's content and is never translated; other labels are translated like any text (pass them in English
        /// or already through <see cref="L.T"/>). With <paramref name="newPrompt"/> the last entry is "＋ New…",
        /// which asks <paramref name="owner"/> for a new name. <paramref name="set"/> runs when the person picks another value.</summary>
        public static ComboBox Make(IEnumerable<(string value, string label)> options, string? current, Action<string> set,
                                    string? newPrompt = null, Window? owner = null)
        {
            current ??= "";
            var cb = new ComboBox { MaxDropDownHeight = 320 };
            ComboBoxItem? Find(string v) => cb.Items.Cast<ComboBoxItem>().FirstOrDefault(i => ((string)i.Tag!).Equals(v, StringComparison.OrdinalIgnoreCase));
            foreach (var (v, l) in options)
                if (Find(v) == null) cb.Items.Add(Item(l, v));
            if (Find(current) == null) cb.Items.Add(current.Length > 0 ? Item(current, current) : new ComboBoxItem { Content = L.T("(none)"), Tag = current });
            if (newPrompt != null) cb.Items.Add(new ComboBoxItem { Content = L.T("＋ New…"), Tag = NewTag, FontStyle = FontStyles.Italic });

            cb.SelectedItem = Find(current);
            object? last = cb.SelectedItem;
            cb.SelectionChanged += (s, e) =>
            {
                if (cb.SelectedItem is not ComboBoxItem ci || ReferenceEquals(ci, last)) return;
                var v = (string)ci.Tag!;
                if (v == NewTag)
                {
                    var name = owner != null ? InputDialog.Prompt(owner, L.T("New"), newPrompt!, "")?.Trim() : null;
                    if (name == null) { cb.SelectedItem = last; return; }
                    ci = Item(name, name);
                    cb.Items.Insert(cb.Items.Count - 1, ci);
                    last = ci; cb.SelectedItem = ci;
                    v = name;
                }
                last = ci;
                set(v);
            };
            return cb;
        }

        // an option shown as its own value is a name (the person's content): never translated
        private static ComboBoxItem Item(string label, string value)
        {
            var it = new ComboBoxItem { Content = label, Tag = value };
            if (label == value) L.SetSkip(it, true);
            return it;
        }
    }
}
