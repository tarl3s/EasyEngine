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
        /// the list is kept and shown (empty → "(none)"). With <paramref name="newPrompt"/> the last entry is "＋ New…",
        /// which asks <paramref name="owner"/> for a new name. <paramref name="set"/> runs when the person picks another value.</summary>
        public static ComboBox Make(IEnumerable<(string value, string label)> options, string? current, Action<string> set,
                                    string? newPrompt = null, Window? owner = null)
        {
            current ??= "";
            var cb = new ComboBox { MaxDropDownHeight = 320 };
            ComboBoxItem? Find(string v) => cb.Items.Cast<ComboBoxItem>().FirstOrDefault(i => ((string)i.Tag!).Equals(v, StringComparison.OrdinalIgnoreCase));
            foreach (var (v, l) in options)
                if (Find(v) == null) cb.Items.Add(new ComboBoxItem { Content = l, Tag = v });
            if (Find(current) == null) cb.Items.Add(new ComboBoxItem { Content = current.Length > 0 ? current : "(none)", Tag = current });
            if (newPrompt != null) cb.Items.Add(new ComboBoxItem { Content = "＋ New…", Tag = NewTag, FontStyle = FontStyles.Italic });

            cb.SelectedItem = Find(current);
            object? last = cb.SelectedItem;
            cb.SelectionChanged += (s, e) =>
            {
                if (cb.SelectedItem is not ComboBoxItem ci || ReferenceEquals(ci, last)) return;
                var v = (string)ci.Tag!;
                if (v == NewTag)
                {
                    var name = owner != null ? InputDialog.Prompt(owner, "New", newPrompt!, "")?.Trim() : null;
                    if (name == null) { cb.SelectedItem = last; return; }
                    ci = new ComboBoxItem { Content = name, Tag = name };
                    cb.Items.Insert(cb.Items.Count - 1, ci);
                    last = ci; cb.SelectedItem = ci;
                    v = name;
                }
                last = ci;
                set(v);
            };
            return cb;
        }
    }
}
