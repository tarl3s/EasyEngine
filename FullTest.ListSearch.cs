using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace EasyEngine;

// Type to search in every selection box (ListSearch.cs): typing in an open dropdown or picker menu hides what doesn't
// match, never changes the selection, and everything comes back when it closes.
public partial class App
{
    partial void FeatureTestsListSearch(FullTestKit t)
    {
        static bool Shown(FrameworkElement e) => e.Visibility == Visibility.Visible;
        static ComboBoxItem ByTag(ComboBox cb, string tag) => cb.Items.OfType<ComboBoxItem>().First(i => (string?)i.Tag == tag);

        t.Test("list search: matching ignores case, accents and a leading emoji; prefixes score above 'contains'", () =>
        {
            string q(string s) => ListSearch.Fold(s);
            return q("🏃  Plataforma Rápida") == "plataforma rapida" && q("＋ New…") == "new" && q("(none)") == "none"
                && ListSearch.Score(q("🏃  Platformer"), q("PLAT")) == 3
                && ListSearch.Score(q("Plataforma rápida"), q("RAPIDA")) == 2
                && ListSearch.Score(q("Ação"), q("acao")) == 3
                && ListSearch.Score(q("Wall jump"), q("all")) == 1
                && ListSearch.Score(q("Follow camera"), q("plat")) == 0;
        });

        t.Test("list search: typing in a dropdown hides what doesn't match, keeps the selection, '＋ New…' and (none); closing shows all", () =>
        {
            string? picked = null;
            var cb = Dropdown.Make(new[] { ("follow", "🎥  Follow camera"), ("platformer", "🏃  Platformer"), ("topdown", "🧭  Top-down"), ("jumppad", "Jump pad") },
                                   "follow", v => picked = v, newPrompt: "Name");
            var selected = cb.SelectedItem;
            var best = ListSearch.Type(cb, "p");
            best = ListSearch.Type(cb, "lat");
            bool filtered = ListSearch.GetQuery(cb) == "plat" && Shown(ByTag(cb, "platformer")) && !Shown(ByTag(cb, "topdown"))
                            && !Shown(ByTag(cb, "follow")) && !Shown(ByTag(cb, "jumppad")) && Shown(ByTag(cb, "\u0001new"))
                            && ReferenceEquals(best, ByTag(cb, "platformer")) && ListSearch.GetHint(cb) == "";
            bool kept = ReferenceEquals(cb.SelectedItem, selected) && picked == null; // a search never picks (no save, no undo step)
            // "contains" also matches; the value (Tag) too
            ListSearch.Apply(cb, "camera");
            bool contains = Shown(ByTag(cb, "follow")) && !Shown(ByTag(cb, "platformer"));
            ListSearch.Apply(cb, "jumppad");
            bool byTag = Shown(ByTag(cb, "jumppad")) && !Shown(ByTag(cb, "follow"));
            // nothing matches: "No match", only the special entries stay
            ListSearch.Apply(cb, "zzz");
            bool none = ListSearch.GetHint(cb) == L.T("No match") && Shown(ByTag(cb, "\u0001new")) && ListSearch.Visible(cb).Count == 1;
            ListSearch.Reset(cb);
            bool reset = cb.Items.OfType<ComboBoxItem>().All(Shown) && ListSearch.GetQuery(cb) == "" && ListSearch.GetHint(cb) == "";
            return filtered && kept && contains && byTag && none && reset;
        });

        t.Test("list search: the best match prefers a label that starts with the query; an entry the code hid stays hidden", () =>
        {
            var cb = new ComboBox();
            var dbl = new ComboBoxItem { Content = "Double jump", Tag = "a" };
            var pad = new ComboBoxItem { Content = "🦘 Jump pad", Tag = "b" };
            var secret = new ComboBoxItem { Content = "Jump secret", Tag = "c", Visibility = Visibility.Collapsed };
            cb.Items.Add(dbl); cb.Items.Add(pad); cb.Items.Add(secret);
            var best = ListSearch.Apply(cb, "jump");
            bool order = ReferenceEquals(best, pad) && Shown(dbl) && !Shown(secret);
            ListSearch.Reset(cb);
            return order && !Shown(secret) && Shown(dbl) && Shown(pad);
        });

        t.Test("list search: translated entries are found by what they show AND by their English; behaviors by their name; panels by their texts", () =>
        {
            L.UseTableForTests("pt-BR", new Dictionary<string, string> { ["Jump"] = "Pular", ["Platformer"] = "Plataforma" });
            try
            {
                var cb = new ComboBox();
                var jump = new ComboBoxItem { Content = "Jump", Tag = "j" };
                var other = new ComboBoxItem { Content = "Shoot", Tag = "s" };
                cb.Items.Add(jump); cb.Items.Add(other);
                L.TranslateTree(cb);
                bool translated = (string)jump.Content == "Pular";
                ListSearch.Apply(cb, "jum");
                bool english = Shown(jump) && !Shown(other);
                ListSearch.Apply(cb, "PULAR");
                bool shown = Shown(jump) && !Shown(other);

                // the "＋ Add behavior…" entries: "{icon}  {translated name}", Tag = the kind, TextSearch = the English name
                var beh = new ComboBox();
                var plat = new ComboBoxItem { Content = $"🏃  {L.T("Platformer")}", Tag = BehaviorKind.Platformer };
                TextSearch.SetText(plat, "Platformer");
                var cam = new ComboBoxItem { Content = "🎥  Follow camera", Tag = BehaviorKind.FollowCamera };
                beh.Items.Add(plat); beh.Items.Add(cam);
                ListSearch.Apply(beh, "plataf");
                bool behPt = Shown(plat) && !Shown(cam);
                ListSearch.Apply(beh, "platf");
                bool behEn = Shown(plat) && !Shown(cam);
                ListSearch.Apply(beh, "followcam"); // the kind's own name
                bool behKind = Shown(cam) && !Shown(plat);

                // an entry whose content is a panel (LookPicker): its texts
                var look = new ComboBox();
                var sword = new ComboBoxItem { Content = new StackPanel { Children = { new Border(), new TextBlock { Text = "Sword" } } }, Tag = "sword" };
                var pick = new ComboBoxItem { Content = new StackPanel { Children = { new TextBlock { Text = "🖼" }, new TextBlock { Text = "My picture…" } } }, Tag = "\u0001picture" };
                var axe = new ComboBoxItem { Content = new StackPanel { Children = { new TextBlock { Text = "Axe" } } }, Tag = "x" };
                look.Items.Add(sword); look.Items.Add(pick); look.Items.Add(axe);
                ListSearch.Apply(look, "swo");
                bool panel = Shown(sword) && !Shown(axe) && Shown(pick);
                return translated && english && shown && behPt && behEn && behKind && panel;
            }
            finally { L.UseTableForTests("en", null); }
        });

        t.Test("list search: picker menus filter their entries and hide category titles and separators while searching", () =>
        {
            var menu = new ContextMenu();
            var header = new MenuItem { Header = "MOVEMENT", IsEnabled = false, Tag = "category" };
            var move = new MenuItem { Header = "Move to" };
            var sep = new Separator();
            var header2 = new MenuItem { Header = "TIME", IsEnabled = false, Tag = "category" };
            var wait = new MenuItem { Header = "Wait", IsCheckable = true };
            foreach (var o in new object[] { header, move, sep, header2, wait }) menu.Items.Add(o);
            var best = ListSearch.Type(menu, "wa");
            bool filtered = !Shown(header) && !Shown(header2) && !Shown(sep) && !Shown(move) && Shown(wait) && ReferenceEquals(best, wait)
                            && ListSearch.GetQuery(menu) == "wa";
            ListSearch.Reset(menu);
            bool reset = new FrameworkElement[] { header, move, sep, header2, wait }.All(Shown) && ListSearch.GetQuery(menu) == "";
            return filtered && reset;
        });

        t.Test("list search: dropdowns no longer jump-select on a typed letter (WPF's own type-ahead is off)", () =>
            Current.TryFindResource(typeof(ComboBox)) is Style s
            && s.Setters.OfType<Setter>().Any(x => x.Property == ItemsControl.IsTextSearchEnabledProperty && Equals(x.Value, false)));
    }
}
